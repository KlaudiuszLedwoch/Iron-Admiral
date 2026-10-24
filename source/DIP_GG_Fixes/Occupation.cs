// 1.11.151 (beta): occupation administration. Holding another great power's national core provinces in peacetime is a problem
// every turn, not only when a rising breaks out (applies to every great power with national cores - Independence.Cores):
// - relations: every month the dispossessed nation, its allies, the neighbours of the province and (lightly) the other great
//   powers think worse of the occupier;
// - one event per turn for every occupier (the AI decides by its treasury and unrest for all its occupied provinces at once;
//   the player, since 1.11.191, decides in a window for every occupied province in turn, strongest rising first):
//     pay for garrisons and administration (money),
//     grant one step of autonomy to the province (less burden and rising pressure, better relations with its nation),
//     or rule with a hard hand (no money, but more unrest at home, risings sooner and worse relations);
// - after the last step of autonomy the province returns peacefully to its nation (a dissolved nation is restored).
// Works on top of the monthly burden (1.11.150) and the rising decision (1.11.57).
using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Occupation
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> RelMonthly, PayShare, HardUnrest, HardRevolt, Relief;
        internal static MelonPreferences_Entry<float> Steps;

        class Held { public string Id, Nation; public Province Prov; public Player Orig; public float Sf; }
        class Occ { public Player Who; public readonly List<Held> List = new List<Held>(); }
        class Decision { public Player Who; public List<Held> List; public Held Target; public float Cost, Unrest; public int Month; }

        static readonly Dictionary<IntPtr, Occ> held = new Dictionary<IntPtr, Occ>();
        static readonly Dictionary<string, int> autonomy = new Dictionary<string, int>();   // province -> steps granted
        static readonly Dictionary<string, int> hardUntil = new Dictionary<string, int>();  // province -> month the hard hand ends
        static readonly List<Decision> queue = new List<Decision>();
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>(), next = new List<KeyValuePair<Rect, Action>>();
        static GUIStyle sTitle, sText, sBtn; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Occupation] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("OccupationEvents", true, null, "Campaign: holding another great power's core provinces in peacetime brings an event every turn (pay for garrisons, grant autonomy or rule with a hard hand) and worsens relations every month");
            RelMonthly = cat.CreateEntry("OccupationRelations", 1.0f, null, "Attitude lost every month per occupied core province with its own nation (its allies and the province's neighbours: half, other great powers: a fifth)");
            PayShare = cat.CreateEntry("OccupationPayShare", 30f, null, "Paying for garrisons and administration costs this % of monthly income per occupied core province (x the strength of the occupied nation)");
            HardUnrest = cat.CreateEntry("OccupationHardUnrest", 1.5f, null, "Ruling with a hard hand adds this much unrest at home per occupied core province (x the strength of the occupied nation)");
            HardRevolt = cat.CreateEntry("OccupationHardRevolt", 0.5f, null, "Ruling with a hard hand makes risings in the occupied provinces stronger by this share for three months");
            Relief = cat.CreateEntry("OccupationAutonomyRelief", 1.0f, null, "Unrest at home lowered when autonomy is granted to an occupied province");
            Steps = cat.CreateEntry("OccupationAutonomySteps", 3f, null, "Steps of autonomy after which an occupied core province returns peacefully to its nation");
        }

        // ---- state (saved with the campaign by Independence.Serialize) ----
        internal static bool Parse(string line)
        {
            if (line == null) return false;
            bool a = line.StartsWith("A|"), h = line.StartsWith("H|"); if (!a && !h) return false;
            var s = line.Split('|'); int n; if (s.Length >= 3 && int.TryParse(s[2], out n)) { if (a) autonomy[s[1]] = n; else hardUntil[s[1]] = n; }
            return true;
        }
        internal static IEnumerable<string> Serialize()
        {
            foreach (var kv in autonomy) yield return "A|" + kv.Key + "|" + kv.Value;
            foreach (var kv in hardUntil) yield return "H|" + kv.Key + "|" + kv.Value;
        }
        internal static void Reset() { autonomy.Clear(); hardUntil.Clear(); held.Clear(); queue.Clear(); }

        // 1.11.153: cost base - the net monthly income, but never less than a quarter of the monthly revenue (Budget; Income =
        // Budget - Expenses). 23.10 log: Germany, Spain, Russia paid 0.0M because their net income was not positive.
        internal static float CostBase(Player p)
        {
            float inc = 0f, bud = 0f; try { inc = p.Income(); } catch { } try { bud = p.Budget(); } catch { }
            return Math.Max(0f, Math.Max(inc, bud * 0.25f));
        }
        static int Level(string id) { int n; return autonomy.TryGetValue(id, out n) ? n : 0; }
        static int MaxSteps() { return Math.Max(1, Steps != null ? (int)Math.Round(Steps.Value) : 3); }
        // share of the burden and of the rising pressure that is left after the autonomy granted
        internal static float BurdenFactor(string id) { if (On == null || !On.Value) return 1f; return 1f - (float)Level(id) / (MaxSteps() + 1); }
        internal static float RevoltFactor(string id, int now)
        {
            if (On == null || !On.Value) return 1f;
            float f = BurdenFactor(id); int u;
            if (hardUntil.TryGetValue(id, out u) && now <= u) f *= 1f + HardRevolt.Value;
            return f;
        }
        internal static void Forget(string id) { autonomy.Remove(id); hardUntil.Remove(id); }

        // ---- called by Independence.Tick ----
        internal static void Begin() { held.Clear(); }
        internal static void Collect(Player occ, string id, string nation, Player orig, Province pr, float sf)
        {
            if (On == null || !On.Value || occ == null || occ.data == null || occ.data.name == "neutral") return;
            if (!held.TryGetValue(occ.Pointer, out var o)) { o = new Occ { Who = occ }; held[occ.Pointer] = o; }
            o.List.Add(new Held { Id = id, Nation = nation, Prov = pr, Orig = orig, Sf = sf });
        }

        internal static void Month(int now)
        {
            if (On == null || !On.Value || held.Count == 0) return;
            foreach (var o in held.Values)
            {
                try { Relations(o.Who, o.List, 1f, "monthly"); } catch (Exception e) { Log("relations: " + e.Message); }
                try { Event(o, now); } catch (Exception e) { Log("event: " + e.Message); }
            }
        }

        static Relation Rel(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b) && a.Pointer != b.Pointer) return r; } } catch { }
            return null;
        }
        static bool Allied(Player a, Player b) { if (a == null || b == null) return false; var r = Rel(a, b); try { return r != null && r.isAlliance; } catch { return false; } }
        // 1.11.194: through the game's own AdjustAttitude (kept within -100..100). A direct write took Germany-China to -101.5,
        //           which the game reported as "corrupt attitude / corrupted relation" - and no war could follow from it
        internal static void Adjust(Relation r, float d, string why)
        {
            if (r == null || Math.Abs(d) < 0.001f) return;
            try { CampaignController.Instance.AdjustAttitude(r, d, false, true, why, false, false, false); }
            catch { try { r.attitude = Mathf.Clamp(r.attitude + d, -100f, 100f); } catch { } }
        }

        // attitude lost with the other nations for holding these provinces (mult: 1 monthly, extra for the hard hand)
        static void Relations(Player who, List<Held> list, float mult, string why)
        {
            float rel = RelMonthly.Value * mult; if (rel <= 0f) return;
            var neigh = new Dictionary<string, HashSet<string>>();
            foreach (var h in list)
            {
                var set = new HashSet<string>();
                try { var nb = h.Prov.NeighbourProvinces; for (int i = 0; i < nb.Count; i++) { var q = nb[i]; var c = q != null ? q.ControllerPlayer : null; if (c != null && c.data != null) set.Add(c.data.name); } } catch { }
                neigh[h.Id] = set;
            }
            int touched = 0; float most = 0f; string mostName = "";
            foreach (var kv in CampaignController.Instance.CampaignData.Relations)
            {
                var r = kv.Value; if (r == null || !r.AnyOnePlayerIs(who)) continue;
                bool war = false; try { war = r.isWar; } catch { } if (war) continue;
                var o = r.OppositePlayer(who); if (o == null || o.data == null || o.Pointer == who.Pointer) continue;
                bool dis = false, major = false; try { dis = o.isDisabled; major = o.isMajor; } catch { }
                if (dis) continue;
                float pen = 0f;
                foreach (var h in list)
                {
                    float w = Math.Max(0.3f, h.Sf);
                    if (o.data.name == h.Nation) pen += rel * w;
                    else if (Allied(o, h.Orig) || neigh[h.Id].Contains(o.data.name)) pen += rel * 0.5f * w;
                    else if (major) pen += rel * 0.2f * w;
                }
                pen = Math.Min(pen, rel * 5f);
                if (pen <= 0.01f) continue;
                try { Adjust(r, -pen, "occupation"); touched++; if (pen > most) { most = pen; mostName = Economy.Name(o); } } catch { }
            }
            if (touched > 0) Log($"{Economy.Name(who)} ({why}, {list.Count} occupied core province(s)): relations worse with {touched} nation(s), most with {mostName} (-{most:0.#})");
        }

        static void Event(Occ o, int now)
        {
            var who = o.Who;
            float income = CostBase(who);
            float sf = o.List.Sum(h => h.Sf);
            var d = new Decision
            {
                Who = who, List = o.List, Month = now,
                Target = o.List.OrderByDescending(h => h.Sf).ThenByDescending(h => Level(h.Id)).First(),
                Cost = Math.Max(0f, income) * PayShare.Value / 100f * sf,
                Unrest = HardUnrest.Value * sf,
            };
            bool player = false; try { player = who.isMain; } catch { }
            if (player)
            {
                // a decision left open from the last turn counts as doing nothing - the hard hand
                for (int i = queue.Count - 1; i >= 0; i--) if (queue[i].Who.Pointer == who.Pointer) { Log("last turn's occupation decision left open - treated as the hard hand"); var old = queue[i]; queue.RemoveAt(i); Hard(old); }
                // 1.11.191: one decision per province, one after another (strongest rising first) - before, one window per turn
                //           always proposed autonomy for the same province (the strongest rising) and the others never got it
                foreach (var h in o.List.OrderByDescending(x => x.Sf).ThenBy(x => x.Id))
                {
                    var one = new Decision
                    {
                        Who = who, List = new List<Held> { h }, Target = h, Month = now,
                        Cost = Math.Max(0f, income) * PayShare.Value / 100f * h.Sf,
                        Unrest = HardUnrest.Value * h.Sf,
                    };
                    queue.Add(one);
                }
                Log($"{Economy.Name(who)}: {o.List.Count} occupation decision(s) waiting for the player (one window per province: pay, autonomy or hard hand)");
                return;
            }
            // 1.11.226 (author, 24.10): the AI decides for every occupied province each month, as the player does province by province.
            //           Before, one decision a month went to the strongest rising only, and with six Italian and Spanish provinces France
            //           spread single autonomy steps over them for a year and a half while paying 180-200M a month for garrisons.
            //           Provinces furthest along the autonomy road are settled first, so a province is released, not six started.
            float max = DipGgFixesMod.CoreSuppressAiMaxUnrest != null ? DipGgFixesMod.CoreSuppressAiMaxUnrest.Value : 40f;
            var counts = new Dictionary<string, int>();
            foreach (var h in o.List.OrderByDescending(x => Level(x.Id)).ThenByDescending(x => x.Sf).ThenBy(x => x.Id))
            {
                float cash = 0f, unrest = 0f; try { cash = who.cash; unrest = who.unrest; } catch { }
                var one = new Decision { Who = who, List = new List<Held> { h }, Target = h, Month = now, Cost = Math.Max(0f, income) * PayShare.Value / 100f * h.Sf, Unrest = HardUnrest.Value * h.Sf };
                string choice;
                if (unrest >= max * 0.75f) { choice = "grants autonomy"; Autonomy(one); }
                else if (cash >= one.Cost * 3f) { choice = "pays"; Pay(one); }
                else if (unrest + one.Unrest < max) { choice = "rules with a hard hand"; Hard(one); }
                else { choice = "grants autonomy"; Autonomy(one); }
                int n; counts.TryGetValue(choice, out n); counts[choice] = n + 1;
            }
            float cash2 = 0f, unrest2 = 0f; try { cash2 = who.cash; unrest2 = who.unrest; } catch { }
            Log($"{Economy.Name(who)} (AI) {string.Join(", ", counts.Select(kv => $"{kv.Key} x{kv.Value}"))} - cash {cash2 / 1e6f:0.0}M, unrest {unrest2:0.#}, {o.List.Count} occupied core province(s)");
        }

        static void Pay(Decision d)
        {
            try { d.Who.cash = d.Who.cash - d.Cost; } catch { }
            Log($"{Economy.Name(d.Who)} pays for garrisons and administration: {d.Cost / 1e6f:0.0}M");
        }

        static void Hard(Decision d)
        {
            try { d.Who.AddUnrest(d.Unrest); } catch { }
            foreach (var h in d.List) hardUntil[h.Id] = d.Month + 3;
            try { Relations(d.Who, d.List, 1f, "hard hand"); } catch { }
            Log($"{Economy.Name(d.Who)} rules with a hard hand: unrest +{d.Unrest:0.#}, risings stronger for three months");
        }

        static void Autonomy(Decision d)
        {
            var h = d.Target; int lv = Level(h.Id) + 1;
            try { d.Who.AddUnrest(-Relief.Value); } catch { }
            if (h.Orig != null) { var r = Rel(d.Who, h.Orig); try { if (r != null) Adjust(r, RelMonthly.Value * 3f, "autonomy"); } catch { } }
            if (lv < MaxSteps())
            {
                autonomy[h.Id] = lv;
                Log($"{Economy.Name(d.Who)} grants autonomy to {h.Id} ({h.Nation}): step {lv} of {MaxSteps()}");
                return;
            }
            Forget(h.Id);
            try
            {
                CampaignController.Instance.TransferProvinceToNewOwner(h.Orig, h.Id, true, true, "autonomy");
                Independence.peaceSince.Remove(h.Id);
                Log($"{Economy.Name(d.Who)} grants full self-rule to {h.Id}: the province returns peacefully to {Economy.Name(h.Orig)}");
            }
            catch (Exception e) { Log($"{h.Id} to {h.Nation}: {e.Message}"); }
        }

        // ---- window for the player (same style as the rising decision) ----
        static bool OnMap() { bool w = false; try { w = GameManager.IsWorldMap; } catch { } return w; }
        static bool Busy() { return RevoltDecision.queue.Count > 0; }

        internal static void Tick()
        {
            try
            {
                if (queue.Count == 0 || !OnMap() || Busy()) return;
                if (Input.GetMouseButtonDown(0))
                {
                    var pt = UiScale.Mouse();
                    foreach (var kv in clicks) if (kv.Key.Contains(pt)) { kv.Value(); break; }
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Occupation] tick: " + e.Message); }
        }

        internal static void OnGUI()
        {
            if (queue.Count == 0 || !OnMap() || Busy()) { if (clicks.Count > 0) clicks.Clear(); return; }
            try
            {
                if (sTitle == null)
                {
                    sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
                    sText = new GUIStyle(); sText.fontSize = 13; sText.richText = true; sText.wordWrap = true; sText.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
                    sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.richText = true; sBtn.wordWrap = true; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.normal.textColor = Color.white;
                }
                next.Clear();
                var d = queue[0];
                // 1.11.191: one window per occupied province, one after another (the queue is filled strongest rising first)
                float w = Math.Min(640f, UiScale.W - 40f), h = 330f;
                var r = new Rect((UiScale.W - w) / 2f, (UiScale.H - h) / 2f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                string tgt = Nice(d.Target.Id); int lv = Level(d.Target.Id) + 1;
                int left = queue.Count(q => q.Who.Pointer == d.Who.Pointer) - 1;
                string owner = d.Target.Nation; try { if (d.Target.Orig != null) owner = Economy.Name(d.Target.Orig); } catch { }
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 28f, 24f), $"<b>Administration of the occupied lands: {tgt}</b>" + (left > 0 ? $"  ({left} more to decide)" : ""), sTitle);
                GUI.Label(new Rect(r.x + 14f, r.y + 40f, w - 28f, 190f),
                    $"We hold {tgt}, national land of {owner}" + (Level(d.Target.Id) > 0 ? $" (autonomy {Level(d.Target.Id)}/{MaxSteps()})" : "") + ". Its people do not accept our rule and the other powers watch us. How shall we govern it this turn?\n\n" +
                    $"<b>Pay</b> for garrisons and administration: about {d.Cost / 1e6f:0.0}M.\n" +
                    $"<b>Grant autonomy</b> to {tgt}: step {lv} of {MaxSteps()}" + (lv >= MaxSteps() ? " - the province returns peacefully to its nation" : " - less unrest and fewer risings there, better relations with its nation") + ".\n" +
                    $"<b>Hard hand</b>: no money, but unrest at home +{d.Unrest:0.#}, stronger risings for three months and worse relations.", sText);
                float bw = (w - 56f) / 3f, by = r.yMax - 64f;
                Btn(new Rect(r.x + 14f, by, bw, 48f), "<b>Pay</b>", () => { queue.RemoveAt(0); Pay(d); });
                Btn(new Rect(r.x + 28f + bw, by, bw, 48f), lv >= MaxSteps() ? "<b>Grant self-rule</b>" : "<b>Grant autonomy</b>", () => { queue.RemoveAt(0); Autonomy(d); });
                Btn(new Rect(r.x + 42f + 2f * bw, by, bw, 48f), "<b>Hard hand</b>", () => { queue.RemoveAt(0); Hard(d); });
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Occupation] window: " + e); }
        }
        static void Btn(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }
        static string Nice(string id) { if (string.IsNullOrEmpty(id)) return id; var s = id.Replace('_', ' '); return char.ToUpper(s[0]) + s.Substring(1); }
    }
}

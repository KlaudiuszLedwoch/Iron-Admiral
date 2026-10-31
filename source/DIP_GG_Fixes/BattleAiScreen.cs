// 1.11.86: battle AI lessons from a vanilla 1.7 test battle (1916: 4 BB against BB + CA + 3 CL + 6 DD). The AI battleship
// held back at extreme range until its screen had been destroyed, and the destroyers fired once and then hovered just
// outside their torpedo range. Three rules:
//  - ScreenSupport: AI bb/bc/ca close to effective gun range while their own light divisions are attacking and engage
//    the enemy ships nearest to the attacking screen (closer range = higher hit chance = heavier fire).
//  - Torpedo attack cycle: light divisions press the attack until no tube is loaded, then turn away (smoke when the
//    ship has it), open the range, reload and come back.
//  - Covering retreat: when the attack has failed and the enemy is stronger, the most valuable ships (cost and build
//    time) withdraw while the cheaper ones attack the pursuers or lay smoke between them and the main ships.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static partial class BattleAi
    {
        static int tubeErr, valueErr, smokeErr;

        // fraction of the division's torpedo tubes (with ammunition left) that are loaded; -1 when the state cannot be read
        // 1.11.302 (author 26.10, 20:23): which side of the leader still has loaded tubes (centre-line mounts count for both sides)
        internal static void LoadedSides(Ship leader, out bool port, out bool stbd)
        {
            port = false; stbd = false;
            try
            {
                var parts = leader.torpedoesAll; var lt = leader.transform; if (parts == null || lt == null) return;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; if (p == null) continue;
                    var r = p.reloading; if (r != null && !r.isDone) continue;
                    float x = lt.InverseTransformPoint(p.transform.position).x;
                    if (x > 1f) stbd = true; else if (x < -1f) port = true; else { port = true; stbd = true; }
                }
            }
            catch { port = true; stbd = true; }
        }
        // 1.11.302 (author 26.10, 20:24: torpedoes from two sides, the tracks crossing at 90 degrees, so the target cannot comb both):
        // divisions under a torpedo attack on the same target take approach bearings 45 degrees either side of its bow
        static readonly Dictionary<IntPtr, Dictionary<IntPtr, float>> attackers = new Dictionary<IntPtr, Dictionary<IntPtr, float>>();
        internal static bool CrossSlot(Division div, Ship target, out UnityEngine.Vector3 dir, out int slot, out int of)
        {
            dir = UnityEngine.Vector3.zero; slot = 0; of = 1;
            try
            {
                float now = UnityEngine.Time.time;
                if (!attackers.TryGetValue(target.Pointer, out var m)) attackers[target.Pointer] = m = new Dictionary<IntPtr, float>();
                m[div.Pointer] = now;
                var live = new List<IntPtr>(); foreach (var kv in m) if (now - kv.Value < 10f) live.Add(kv.Key);
                if (live.Count < 2) return false;
                live.Sort((a, b) => a.ToInt64().CompareTo(b.ToInt64()));
                slot = live.IndexOf(div.Pointer); of = live.Count;
                var f = target.transform.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) return false; f.Normalize();
                float ang = (slot % 2 == 0 ? 45f : -45f) + (slot / 2) * (slot % 2 == 0 ? 20f : -20f);
                dir = UnityEngine.Quaternion.Euler(0f, ang, 0f) * f;
                return true;
            }
            catch { return false; }
        }
        internal static float LoadedTubes(Division div, Ship leader)
        {
            int n = 0, loaded = 0;
            var ships = div.ships; int cnt = ships != null ? ships.Count : 0;
            for (int k = -1; k < cnt; k++)
            {
                var s = k < 0 ? leader : ships[k];
                if (s == null || s.isSinking || s.isDead) continue;
                if (k >= 0 && s.Pointer == leader.Pointer) continue;
                var parts = s.torpedoesAll; if (parts == null) continue;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; if (p == null) continue;
                    try { if (s.GetTotalAmmo(p.data) <= 0) continue; } catch { }
                    n++;
                    try { var r = p.reloading; if (r == null || r.isDone) loaded++; }
                    catch (Exception e) { if (tubeErr++ < 3) Log("torpedo tube state unknown: " + e.Message); return -1f; }
                }
            }
            return n == 0 ? 0f : (float)loaded / n;
        }

        static Ship NearestVisibleEnemy(Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, out float bestD)
        {
            Ship best = null; bestD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !e.IsVisibleFor(leader)) continue;
                float d = Dist(leader, e); if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // distance at which a reloading flotilla stops opening the range
        static float SafeDistance(Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            float mx = 0f;
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (!Alive(e) || !e.IsVisibleFor(leader)) continue; mx = UnityEngine.Mathf.Max(mx, GunRange(e)); }
            float torp = TorpedoRange(leader);
            return UnityEngine.Mathf.Clamp(mx * 0.7f, torp * 1.3f, 15000f);
        }

        static void AwayFrom(Division div, Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, float dist)
        {
            var lp = leader.transform.position; var c = UnityEngine.Vector3.zero; int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !e.IsVisibleFor(leader)) continue;
                if (Dist(leader, e) > 25000f) continue;
                c += e.transform.position; n++;
            }
            var away = n > 0 ? lp - c / n : -leader.transform.forward; away.y = 0f;
            if (away.sqrMagnitude < 1f) away = leader.transform.forward;
            away.Normalize();
            try { div.MoveTo(lp + away * dist); } catch { }
        }

        static void TrySmoke(Division div, Ship leader, DivState st)
        {
            float now = UnityEngine.Time.time; if (now < st.nextSmoke) return;
            st.nextSmoke = now + 30f;
            var ships = div.ships; int cnt = ships != null ? ships.Count : 0; int laid = 0;
            for (int k = -1; k < cnt; k++)
            {
                var s = k < 0 ? leader : ships[k];
                if (s == null || s.isSinking || s.isDead || (k >= 0 && s.Pointer == leader.Pointer)) continue;
                try { if (s.CanLaunchSmokeScreen()) { s.LaunchSmokeScreen(); laid++; } }
                catch (Exception e) { if (smokeErr++ < 3) Log("smoke screen failed: " + e.Message); return; }
            }
            if (laid > 0) Log($"{st.name}: {laid} ship(s) lay a smoke screen");
        }

        // ---- ship value: cost and build time relative to the most valuable ship of the same side (0..1)
        static readonly Dictionary<IntPtr, float> valueCache = new Dictionary<IntPtr, float>();
        static readonly Dictionary<IntPtr, float> sideMaxValue = new Dictionary<IntPtr, float>();
        static IntPtr valueBattle;
        static float RawValue(Ship s, out float cost, out float months)
        {
            cost = 0f; months = 0f;
            try { cost = s.Cost(); } catch (Exception e) { if (valueErr++ < 3) Log("ship cost unknown: " + e.Message); }
            try { months = s.BuildingTime(false); } catch (Exception e) { if (valueErr++ < 3) Log("build time unknown: " + e.Message); }
            if (cost <= 0f) cost = Ton(s);
            return cost;
        }
        internal static float RelValue(Ship s)
        {
            if (s == null || s.player == null) return 0.5f;
            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            IntPtr bp = battle != null ? battle.Pointer : IntPtr.Zero;
            if (bp != valueBattle) { valueBattle = bp; valueCache.Clear(); sideMaxValue.Clear(); }
            float v;
            if (!sideMaxValue.TryGetValue(s.player.Pointer, out v))
            {
                // side maximum of cost and build time, combined 50/50
                float mc = 0f, mt = 0f;
                var own = battle != null ? battle.ShipsOf(s.player, true) : null;
                var cm = new Dictionary<IntPtr, float>(); var tm = new Dictionary<IntPtr, float>();
                for (int i = 0; own != null && i < own.Count; i++)
                {
                    var x = own[i]; if (x == null) continue;
                    float c, t; RawValue(x, out c, out t); cm[x.Pointer] = c; tm[x.Pointer] = t;
                    mc = UnityEngine.Mathf.Max(mc, c); mt = UnityEngine.Mathf.Max(mt, t);
                }
                foreach (var kv in cm)
                {
                    float r = mc > 0f ? kv.Value / mc : 0.5f;
                    float t = tm[kv.Key]; float rt = mt > 0f ? t / mt : r;
                    valueCache[kv.Key] = 0.5f * r + 0.5f * rt;
                }
                sideMaxValue[s.player.Pointer] = 1f;
            }
            return valueCache.TryGetValue(s.Pointer, out v) ? v : 0.5f;
        }
        // damage at which a charge is aborted: cheap ships fight longer, the most valuable ones break off earlier
        internal static float AbortFor(Ship leader, Doctrine doc)
        {
            float a = AbortDamage(doc);
            if (!DipGgFixesMod.ValueBasedAbort.Value) return a;
            return a * UnityEngine.Mathf.Lerp(1.2f, 0.8f, RelValue(leader));
        }

        // 1.11.487 (thunderspirit2014's video 30.10, 18:18: a reloading destroyer steamed in circles - the game turns a ship round and round
        // on a move point it has reached): at a safe distance the reloading flotilla steams legs of about 2.5 km square to the line to the
        // nearest enemy, turning back each leg, so it keeps its distance and stays a moving target.
        static void HoldLeg(Division div, Ship leader, Ship near, DivState st)
        {
            try
            {
                var p = leader.transform.position; var to = near != null ? near.transform.position - p : leader.transform.forward; to.y = 0f;
                if (to.sqrMagnitude < 1f) to = leader.transform.forward; to.Normalize();
                var beam = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, to) * st.holdSide; st.holdSide = -st.holdSide;
                div.MoveTo(p + beam * 2500f);
            }
            catch { }
        }
        // 1.11.487: the division of this ship is in a torpedo charge (the torpedo settings keep it off the extreme-range setting)
        internal static bool InCharge(Ship s)
        {
            try { var d = s.division; if (d != null && States.TryGetValue(d.Pointer, out var st)) return st.charging; } catch { }
            return false;
        }
        // ---- torpedo attack cycle; returns true when the cycle took over the division
        internal static bool ReloadCycle(Ship leader, Division div, DivState st, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            if (!DipGgFixesMod.TorpedoReloadCycle.Value || st.noReloads) return false;
            float now = UnityEngine.Time.time;
            if (st.reloading)
            {
                float lt = LoadedTubes(div, leader);
                float nd; var near = NearestVisibleEnemy(leader, enemies, out nd);
                float safe = SafeDistance(leader, enemies);
                bool ready = lt < 0f || lt >= DipGgFixesMod.ReloadReturnFraction.Value;
                if (ready && (near == null || nd >= safe * 0.8f || now - st.reloadStart > DipGgFixesMod.ReloadMaxSeconds.Value))
                {
                    st.reloading = false; st.returnAttack = true;
                    Log($"{st.name} ({leader.shipType.name}): torpedo tubes reloaded ({(lt < 0f ? "state unknown" : (lt * 100f).ToString("0") + "%")}) after {now - st.reloadStart:0} s - back to the attack");
                    return false;
                }
                // 1.11.89 (test 22.10): the tubes never showed as reloaded when the ship had no reloads left - give up after 1.5 x the limit
                if (!ready && now - st.reloadStart > DipGgFixesMod.ReloadMaxSeconds.Value * 1.5f)
                {
                    st.reloading = false; st.returnAttack = false; st.noReloads = true;
                    Log($"{st.name} ({leader.shipType.name}): tubes not reloaded after {now - st.reloadStart:0} s ({(lt < 0f ? "state unknown" : (lt * 100f).ToString("0") + "% loaded")}) - no reloads on board, the flotilla rejoins the fight");
                    return false;
                }                if (near != null && nd < safe) { AwayFrom(div, leader, enemies, 4000f); TrySmoke(div, leader, st); st.holding = false; }
                else if (!st.holding || now - st.holdAt > 40f) { st.holding = true; st.holdAt = now; HoldLeg(div, leader, near, st); } // 1.11.89: at a safe distance the flotilla waits (test: it ran 50 km away); 1.11.487: on slow legs, not on a point
                Why(st, "reloading torpedoes");
                return true;
            }
            if (!st.charging) { st.firstInRange = 0f; return false; }
            float bestD; var best = NearestVisibleCapital(leader, enemies, out bestD);
            float torp = TorpedoRange(leader);
            if (best == null || bestD > torp * 1.1f) { st.firstInRange = 0f; return false; }
            if (st.firstInRange <= 0f) st.firstInRange = now;
            float l2 = LoadedTubes(div, leader);
            if (l2 < 0f || l2 > 0.001f) return false;   // tubes left: keep pressing
            if (now - st.firstInRange < 3f) return false;
            st.charging = false; st.reloading = true; st.reloadStart = now; st.firstInRange = 0f;
            AwayFrom(div, leader, enemies, 4000f); TrySmoke(div, leader, st);
            Log($"{st.name} ({leader.shipType.name}): all torpedoes away at {bestD:0} m from {best.name} - turns away to reload");
            Why(st, "reloading torpedoes");
            return true;
        }

        // ---- capital ships support their attacking screen; returns true when it took over the division
        internal static bool ScreenSupport(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            // 1.11.87 (War Instructions 1944): the battle line opens fire at the maximum effective range and keeps the enemy line under
            // fire (1295, 1298); the flotillas attack once the lines are engaged (12124). The line moves up first, unless the enemy is
            // clearly stronger - then the torpedoes go first (delay action, 12298) and the massed attack rule decides.
            if (!DipGgFixesMod.CapitalSupportScreen.Value) return false;
            var p = leader.player; if (p == null) return false;
            float now = UnityEngine.Time.time;
            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (battle == null) return false;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(p, true); } catch { }
            bool fog = Fog.AiActive && !p.isMain;
            float own = Strength(mine), foe = fog ? PerceivedStrength(p, enemies) : Strength(enemies);
            // 1.11.88: doctrines that protect the battle line (heavyProtect) avoid the engagement already at smaller enemy superiority;
            // at night heavy ships do not close the range unnecessarily (War Instructions 1944, 829)
            bool weaker = !LineWillEngage(own, foe, doc);
            bool night = DipGgFixesMod.NightRules.Value && IsNight();
            if (weaker || night || DivDamage(div) > AbortFor(leader, doc))
            {
                if (st.supporting) { st.supporting = false; Log($"{st.name}: {(night ? "night - heavy ships do not close the range" : weaker ? "enemy stronger - the line does not seek the engagement, torpedoes first" : "too damaged")} - left to the game"); }
                return false;
            }
            // target: the enemy nearest to an attacking own flotilla, otherwise the nearest enemy capital ship
            var c = UnityEngine.Vector3.zero; int n = 0;
            foreach (var kv in States)
            {
                var s2 = kv.Value; if (s2 == st || s2.playerPtr != p.Pointer) continue;
                if ((s2.charging || s2.reloading) && now - s2.activeAt < 15f) { c += s2.pos; n++; }
            }
            Ship tgt = null; float td = float.MaxValue;
            if (n > 0)
            {
                c /= n;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (!Alive(e) || !Combat(e) || !e.IsVisibleFor(leader)) continue;
                    var dv = e.transform.position - c; dv.y = 0f; float d = dv.magnitude;
                    if (d < td) { td = d; tgt = e; }
                }
            }
            else tgt = NearestVisibleCapital(leader, enemies, out td);
            if (tgt == null) return false;
            float ownR = GunRange(leader); if (ownR <= 0f) return false;
            float want = ownR * DipGgFixesMod.SupportRangeFraction.Value;
            float pen, belt;
            if (CannotPenetrate(leader, tgt, want, out pen, out belt)) return false; // the close-in rules decide for armoured targets
            float dist = Dist(leader, tgt);
            if (!st.supporting || st.supportTarget != tgt.Pointer)
                Log($"{st.name} ({leader.shipType.name}): {(n > 0 ? "own flotillas attacking - supports them" : "battle line moves up to effective range")}, closes to {want:0} m and engages {tgt.name} ({tgt.shipType.name}, {dist:0} m)");
            st.supporting = true; st.supportTarget = tgt.Pointer;
            if (dist > want)
            {
                var tt = tgt.transform;
                if (tt != null) { if (dist > ownR) MoveEvasive(div, leader, st, tt.position, "moving up to support the screen"); else div.MoveTo(tt.position); }
            }
            Why(st, "supporting the screen");
            return true;
        }

        // 1.11.88: does the own battle line seek the engagement? Not when the enemy is clearly stronger; doctrines that protect the
        // battle line (heavyProtect 0..1) already at parity
        internal static bool LineWillEngage(float own, float foe, Doctrine doc)
        {
            float ratio = DipGgFixesMod.CoverRetreatStrengthRatio.Value - 0.2f * (doc != null ? doc.heavyProtect : 0f);
            return foe <= own * UnityEngine.Mathf.Max(1f, ratio);
        }
        internal static bool LineWillEngage(Player p, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies, Doctrine doc)
        {
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(p, true); } catch { }
            bool fog = Fog.AiActive && !p.isMain;
            return LineWillEngage(Strength(mine), fog ? PerceivedStrength(p, enemies) : Strength(enemies), doc);
        }

        // ---- 1.11.87: are the battle lines engaged? (War Instructions 1944, 12124: torpedo attacks are normally delayed until then)
        static readonly Dictionary<IntPtr, float> engagedNext = new Dictionary<IntPtr, float>();
        static readonly Dictionary<IntPtr, int> engagedState = new Dictionary<IntPtr, int>(); // 0 no capitals, 1 not engaged, 2 engaged
        internal static int LinesEngaged(Player p, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            float now = UnityEngine.Time.time; float nx; int stt;
            if (engagedNext.TryGetValue(p.Pointer, out nx) && now < nx && engagedState.TryGetValue(p.Pointer, out stt)) return stt;
            stt = 0;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(p, true); } catch { }
            for (int i = 0; mine != null && i < mine.Count; i++)
            {
                var x = mine[i]; if (!Alive(x) || !IsCapital(x)) continue;
                if (stt == 0) stt = 1;
                float r = GunRange(x); if (r <= 0f) continue;
                for (int k = 0; k < enemies.Count; k++)
                {
                    var e = enemies[k]; if (!Alive(e) || !Combat(e) || !e.IsVisibleFor(x)) continue;
                    if (!IsCapital(e) && Ton(e) < Ton(x) * 0.5f) continue;
                    if (Dist(x, e) <= r * 0.9f) { stt = 2; break; }
                }
                if (stt == 2) break;
            }
            int was; engagedState.TryGetValue(p.Pointer, out was);
            if (was != stt && stt == 2) Log($"{p.data.name}: the battle lines are engaged - flotillas cleared for torpedo attacks");
            engagedState[p.Pointer] = stt; engagedNext[p.Pointer] = now + 3f;
            return stt;
        }

        // ---- 1.11.87: torpedoes expended and no spares on board - the flotilla turns on the enemy light forces (12128-12129)
        internal static bool EngageLightForces(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            if (!DipGgFixesMod.TorpedoReloadCycle.Value) return false;
            if (doc != null && doc.hitAndRun) return false; // 1.11.88: hit-and-run doctrine - the flotilla withdraws after its attack
            if (DivDamage(div) > AbortFor(leader, doc)) return false;
            Ship best = null; float bd = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !IsLight(e) || !e.IsVisibleFor(leader)) continue;
                float d = Dist(leader, e); if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return false;
            float r = GunRange(leader); if (r <= 0f) return false;
            if (st.reason != "torpedoes expended - engaging enemy light forces")
                Log($"{st.name} ({leader.shipType.name}): torpedoes expended, no reloads on board - turns on the enemy light forces, {best.name} ({best.shipType.name}) at {bd:0} m");
            if (bd > r * 0.7f) { var tt = best.transform; if (tt != null) { if (div.isRetreating || div.retreat) div.Retreat(false); div.MoveTo(tt.position); } }
            Why(st, "torpedoes expended - engaging enemy light forces");
            return true;
        }

        // ---- covering retreat
        class SideState { public float next; public bool attacked; public float screenTon0 = -1f; public bool failed; }
        static readonly Dictionary<IntPtr, SideState> sides = new Dictionary<IntPtr, SideState>();
        static IntPtr sidesBattle;
        static bool AttackFailed(Player p, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies, Doctrine doc)
        {
            if (battle.Pointer != sidesBattle) { sidesBattle = battle.Pointer; sides.Clear(); }
            SideState ss; if (!sides.TryGetValue(p.Pointer, out ss)) { ss = new SideState(); sides[p.Pointer] = ss; }
            float now = UnityEngine.Time.time;
            foreach (var kv in States) { var s2 = kv.Value; if (s2.playerPtr == p.Pointer && (s2.charging || s2.reloading)) ss.attacked = true; }
            if (now < ss.next) return ss.failed;
            ss.next = now + 5f;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(p, true); } catch { }
            if (mine == null) return false;
            float screen = 0f; int able = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                var x = mine[i]; if (!Alive(x) || !IsLight(x) || !x.haveTorpedoes) continue;
                screen += Ton(x);
                float dmg = 0f; try { dmg = x.DamageEstimation(); } catch { }
                if (HasTorpedoAmmo(x) && dmg <= AbortFor(x, doc)) able++;
            }
            if (ss.screenTon0 < 0f) ss.screenTon0 = screen;
            float own = Strength(mine);
            bool fog = Fog.AiActive && !p.isMain;
            float foe = fog ? PerceivedStrength(p, enemies) : Strength(enemies);
            bool lost = able == 0 || (ss.screenTon0 > 0f && screen < ss.screenTon0 * 0.5f);
            bool failed = ss.attacked && lost && foe > own * DipGgFixesMod.CoverRetreatStrengthRatio.Value;
            if (failed != ss.failed)
                Log($"{p.data.name}: torpedo attack {(failed ? "has failed" : "no longer failed")} - {able} torpedo craft still able to attack, screen {screen:0} t of {ss.screenTon0:0} t, enemy {foe:0} t vs own {own:0} t{(failed ? " - covering retreat" : "")}");
            ss.failed = failed;
            return failed;
        }

        static Ship MostValuableOwn(Player p, CampaignBattle battle)
        {
            Ship best = null; float bv = -1f;
            var own = battle.ShipsOf(p, true);
            for (int i = 0; own != null && i < own.Count; i++) { var x = own[i]; if (!Alive(x)) continue; float v = RelValue(x); if (v > bv) { bv = v; best = x; } }
            return best;
        }

        internal static bool CoverRetreat(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, CampaignBattle battle, Player player)
        {
            if (!DipGgFixesMod.CoverRetreat.Value) return false;
            // 1.11.294 (#36): transports and other non-combat ships never cover a retreat (1.2.74-beta.8: four French transports, one
            // of them panicked, "covering the retreat - screening") - they keep the game's own behaviour
            if (!Combat(leader)) { st.covering = false; return false; }
            if (!AttackFailed(player, battle, enemies, doc)) { st.covering = false; return false; }
            float rel = RelValue(leader);
            if (rel >= DipGgFixesMod.CoverValueRatio.Value)
            {
                if (!div.isRetreating && !div.retreat) div.Retreat(true);
                if (!st.covering) Log($"{st.name} ({leader.shipType.name}): the attack has failed and the enemy is stronger - withdraws while the lighter ships cover it");
                st.covering = true; st.charging = false; st.closing = false; st.fighting = false; st.supporting = false;
                Why(st, "withdrawing under cover");
                return true;
            }
            var main = MostValuableOwn(player, battle);
            if (main == null || main.Pointer == leader.Pointer) return false;
            Ship pursuer = null; float pd = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !Combat(e) || !e.IsVisibleFor(leader)) continue;
                float d = Dist(main, e); if (d < pd) { pd = d; pursuer = e; }
            }
            if (pursuer == null) return false;
            if (div.isRetreating || div.retreat) div.Retreat(false);
            st.fighting = false; st.supporting = false;
            TrySmoke(div, leader, st);
            float lt = leader.haveTorpedoes ? LoadedTubes(div, leader) : 0f;
            bool torp = leader.haveTorpedoes && HasTorpedoAmmo(leader) && lt != 0f;
            if (!st.covering) Log($"{st.name} ({leader.shipType.name}): covers the withdrawal of {main.name} - {(torp ? "torpedo attack on" : "moves between the main ships and")} {pursuer.name} ({pursuer.shipType.name})");
            st.covering = true;
            if (torp) { st.charging = true; st.target = pursuer.Pointer; div.MoveTo(pursuer.transform.position); Why(st, "covering the retreat - attacking the pursuer"); }
            else { st.charging = false; var mid = UnityEngine.Vector3.Lerp(main.transform.position, pursuer.transform.position, 0.4f); div.MoveTo(mid); Why(st, "covering the retreat - screening"); }
            return true;
        }
    }
}

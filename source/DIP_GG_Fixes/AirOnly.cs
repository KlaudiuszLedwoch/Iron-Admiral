// 1.11.440 (#96, author 30.10: "there should also be a battle with aircraft only - when the fleet of the player or the AI is
// caught by aviation"; decisions: 1) a task force within reach of the enemy's shore aviation (or carriers operating from its
// ports), by day, in flying weather, with a chance per turn; 2) the player's fleet attacked: a playable battle; 3) AI vs AI: an
// automatic result - later). Experimental, off by default (AirOnlyBattles):
// - on a turn's battle generation (MissionGenerator.TryGenerateTaskForceBattle, per pair of nations at war) a task force of the
//   player at sea, not already in a battle, within the reach of an enemy's torpedo aviation may be attacked from the air: a
//   campaign battle with the player's task force as defender and no attacking ships (the game's generator allows a side with
//   zero ships - FilterShipsForBattle canHaveZeroShips), marked "IA_AIRONLY|land|<km>" in MissionGeneratorInfo (saved with it);
// - in the battle AirStrike sends the attacker's waves (shore or carrier tables, #95); the battle is kept from finishing while
//   waves are still to come or aircraft are in the air; then the game ends it as usual.
// Night and the weather of the moment are decided in the battle (no waves - the battle ends with no attack).
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class AirOnly
    {
        internal static MelonPreferences_Entry<bool> On, Test; internal static MelonPreferences_Entry<float> Chance;
        internal const string Tag = "IA_AIRONLY";
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AirOnlyBattles", false, null, "Campaign (experimental, #96): a player's task force at sea within reach of an enemy's torpedo aviation can be attacked by aircraft alone - a playable battle with no enemy ships, only the air waves");
            Chance = cat.CreateEntry("AirOnlyBattleChance", 20f, null, "Campaign (#96): chance (%) per turn that a player's task force within the enemy's reach is attacked from the air; it falls linearly to 0 at the edge of the reach");
            Test = cat.CreateEntry("AirOnlyBattleTest", false, null, "Test (#96): on the next turn the first player's task force within an enemy's reach is attacked from the air (chance 100%)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirOnly] " + m); }
        static int errors, made, probes; static string turnKey = ""; static readonly HashSet<IntPtr> hitThisTurn = new HashSet<IntPtr>();

        internal static bool Is(CampaignBattle b) { try { var g = b != null ? b.MissionGeneratorInfo : null; return g != null && g.StartsWith(Tag); } catch { return false; } }
        internal static bool IsLand(CampaignBattle b) { try { return b.MissionGeneratorInfo.Contains("|land|"); } catch { return true; } }
        internal static float Km(CampaignBattle b) { try { var a = b.MissionGeneratorInfo.Split('|'); return a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 100f; } catch { return 100f; } }
        static bool Enabled { get { return (On != null && On.Value) || (Test != null && Test.Value); } }

        static float PortKm(Vector3 pos, Player p)
        {
            try
            {
                float sqr = float.MaxValue; var pt = CampaignMap.PortsDb.NearestPort(pos, out sqr, p, true);
                float kpu = 1f; try { if (CampaignMap.UnityDistanceToWorldKmMultiplier > 0f) kpu = CampaignMap.UnityDistanceToWorldKmMultiplier; } catch { }
                return pt != null && sqr < float.MaxValue ? Mathf.Sqrt(sqr) * kpu : 99999f;
            }
            catch { return 99999f; }
        }

        // the attacker's reach: shore torpedo aviation (at_1 + av_recon_N radius), else a carrier force operating near its ports
        static bool Reach(Player att, Vector3 pos, out bool land, out float km, out float R, out string why)
        {
            land = true; km = PortKm(pos, att); R = 0f; why = "";
            int tl = 0, rl = 0, cl = 0; try { tl = NavalAviation.TorpLevel(att); rl = NavalAviation.ReconLevel(att); cl = NavalAviation.CarrierLevel(att); } catch { }
            if (tl == 0) { why = "no aerial torpedo"; return false; }
            if (rl > 0) { R = AirStrike.ShoreReach(rl); if (km < R) return true; }
            if (cl > 0) { land = false; R = 300f + 150f * cl; if (km < R) return true; }   // carriers strike up to ~450-900 km from their base
            why = $"{km:0} km from its nearest port, beyond the reach ({(rl > 0 ? AirStrike.ShoreReach(rl) : 0f):0} km shore{(cl > 0 ? $", {300f + 150f * cl:0} km carriers" : "")})";
            return false;
        }

        static BattleTypeEx PickType()
        {
            BattleTypeEx best = null, any = null;
            try
            {
                foreach (var kv in G.GameData.battleTypesEx)
                {
                    var t = kv.Value; if (t == null) continue; bool dis = false; try { dis = t.Disabled; } catch { }
                    if (any == null) any = t;
                    if (probes++ < 1) { var sb = new System.Text.StringBuilder(); foreach (var k2 in G.GameData.battleTypesEx) { try { sb.Append(k2.Key + "=" + k2.Value.CurrentType + (k2.Value.IsTaskForceType ? "(tf)" : "") + "; "); } catch { } } Log("battle types: " + sb); }
                    if (dis) continue;
                    if (t.CurrentType == BattleTypeEx.EBattleType.Strike) best = t;
                    else if (best == null && t.CurrentType == BattleTypeEx.EBattleType.TaskForce) best = t;
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("battle types: " + e.Message); }
            return best ?? any;
        }

        static void Consider(Player att, Player def)
        {
            if (att == null || def == null || !def.isMain || att.isMain) return;
            var cc = CampaignController.Instance; if (cc == null) return;
            string key = ""; try { key = cc.CurrentDate.ToString(); } catch { }
            if (key != turnKey) { turnKey = key; hitThisTurn.Clear(); }
            if (hitThisTurn.Contains(def.Pointer)) return;   // one air attack on the player per turn
            var tfs = cc.CampaignData.GetPlayerTaskForces(def, null);
            for (int i = 0; tfs != null && i < tfs.Count; i++)
            {
                var tf = tfs[i]; if (tf == null) continue;
                bool busy = false; try { busy = tf.BattleId != Il2CppSystem.Guid.Empty || tf.GroupMovingToBattle; } catch { }
                if (busy) continue;
                var ships = new Il2CppSystem.Collections.Generic.List<Ship>();
                try { var v = tf.Vessels; for (int k = 0; v != null && k < v.Count; k++) { var s = v[k] != null ? v[k].TryCast<Ship>() : null; if (s != null) ships.Add(s); } } catch { }
                if (ships.Count == 0) continue;
                Vector3 pos = tf.WorldPos;
                if (!Reach(att, pos, out bool land, out float km, out float R, out string why)) continue;
                bool test = Test != null && Test.Value;
                float ch = test ? 1f : Mathf.Clamp01(Chance.Value / 100f * (1f - km / R)), r = UnityEngine.Random.value;
                if (r >= ch) { Log($"{att.data.name} vs task force of {ships.Count} ship(s): {km:0} km from its port (reach {R:0}), no air attack (chance {ch * 100f:0}%)"); continue; }
                if (Make(att, def, tf, ships, pos, land, km)) { hitThisTurn.Add(def.Pointer); if (test) Test.Value = false; return; }
            }
        }

        static bool Make(Player att, Player def, CampaignController.TaskForce tf, Il2CppSystem.Collections.Generic.List<Ship> ships, Vector3 pos, bool land, float km)
        {
            try
            {
                var bt = PickType(); if (bt == null) { Log("no battle type - no air attack"); return false; }
                var b = new CampaignBattle();
                b.Id = Il2CppSystem.Guid.NewGuid();
                b.Attacker = att; b.Defender = def; b.Type = bt; b.BattleWorldPos = pos;
                b.AttackerShips = new Il2CppSystem.Collections.Generic.List<Ship>();
                b.DefenderShips = ships;
                var pa = new Il2CppSystem.Collections.Generic.List<Player>(); pa.Add(att); b.ParticipatePlayersAttacker = pa;
                var pd = new Il2CppSystem.Collections.Generic.List<Player>(); pd.Add(def); b.ParticipatePlayersDefender = pd;
                try { b.DefenderGroupId = tf.Id; } catch { }
                b.MissionGeneratorInfo = Tag + "|" + (land ? "land" : "carrier") + "|" + km.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                try { b.Init(); } catch (Exception e) { Log("init: " + e.Message); }
                b.MissionGeneratorInfo = Tag + "|" + (land ? "land" : "carrier") + "|" + km.ToString("0", System.Globalization.CultureInfo.InvariantCulture);   // Init may reset it
                CampaignController.Instance.BattleGenerator.AddBattle(b);
                try { tf.BattleId = b.Id; } catch { }
                made++;
                Log($"air attack: {att.data.name} {(land ? "shore-based torpedo aircraft" : "carrier aircraft")} on the player's task force of {ships.Count} ship(s) {km:0} km from its nearest port (type {bt.BattleType}/{bt.CurrentType}); battle {b.Id}");
                return true;
            }
            catch (Exception e) { if (errors++ < 10) Log("make: " + (e.InnerException?.Message ?? e.Message) + " " + e.StackTrace); return false; }
        }

        [HarmonyPatch(typeof(MissionGenerator), "TryGenerateTaskForceBattle", new Type[] { typeof(Player), typeof(Player) })]
        static class GenPatch
        {
            static void Postfix(Player __0, Player __1)
            {
                if (!Enabled) return;
                try { Consider(__0, __1); Consider(__1, __0); } catch (Exception e) { if (errors++ < 10) Log("generate: " + e.Message); }
            }
        }

        // the game drops battles it finds incorrect (a side without ships); an air-only battle stays
        [HarmonyPatch(typeof(CampaignBattle), "IsCorrectBattle")]
        static class CorrectPatch
        {
            static bool Prefix(CampaignBattle __instance, ref bool __result)
            {
                if (!Is(__instance)) return true;
                bool ok = false; try { ok = __instance.DefenderShips != null && __instance.DefenderShips.Count > 0; } catch { }
                __result = ok; return false;
            }
        }

        // in the battle: not finished while waves are still to come or aircraft are in the air
        static float lastLog;
        [HarmonyPatch(typeof(BattleManager), "MaybeFinishCombat")]
        static class FinishPatch
        {
            static bool Prefix(BattleManager __instance)
            {
                try
                {
                    var b = __instance.CurrentBattle; if (!Is(b)) return true;
                    if (!AirStrike.AirOnlyBusy()) return true;
                    if (Time.realtimeSinceStartup - lastLog > 30f) { lastLog = Time.realtimeSinceStartup; Log("air-only battle: waves still to come or in the air - the battle goes on"); }
                    return false;
                }
                catch { return true; }
            }
        }
    }
}

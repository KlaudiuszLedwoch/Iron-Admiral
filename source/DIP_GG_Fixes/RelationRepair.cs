// 1.11.286 (#6): missing relation records between great powers are restored.
// In a 1923 campaign (Germany) the politics window found no relation record between Germany and the USA, Russia, Japan, Spain
// and China (and Italy - Scandinavia): the 1.11.249 guards kept the window open, but diplomatic actions between these pairs
// were unavailable and the AI's diplomacy lambdas (ChoosePlayerForAction, AreaRequiredTonnage) threw on the same pairs.
// The game builds the relation table only once, at a new campaign (CampaignController.PrepareRelationships rebuilds the whole
// table, so it cannot be called on a running campaign). Here every pair of active great powers is checked once the campaign date
// has settled after loading or after a turn, and a missing pair gets a neutral relation (attitude 0, no war, no alliance) with
// the treaty dates copied from an existing peaceful relation of the same nation. Existing relations are never touched.
// The check runs from OnUpdate, outside any loop of the game over the relation table. F10 / cfg: RelationRepair (default on).
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class RelationRepair
    {
        static string checkedDate = null, seenDate = null;
        static float seenAt = 0f, nextPoll = 0f;
        internal static int created;

        static bool Usable(Player p)
        {
            try { return p != null && p.data != null && !p.isDisabled && p.isMajor; } catch { return false; }
        }

        static Relation Find(Il2CppSystem.Collections.Generic.Dictionary<Il2CppSystem.Collections.Generic.KeyValuePair<Player, Player>, Relation> map, Player a, Player b)
        {
            try { var r = RelationExt.Between<Player, Relation>(map, a, b); if (r != null) return r; } catch { }
            try { var r = RelationExt.Between<Player, Relation>(map, b, a); if (r != null) return r; } catch { }
            try { foreach (var kv in map) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return r; } } catch { }
            return null;
        }

        static Relation Template(Il2CppSystem.Collections.Generic.Dictionary<Il2CppSystem.Collections.Generic.KeyValuePair<Player, Player>, Relation> map, Player a)
        {
            Relation any = null;
            try
            {
                foreach (var kv in map)
                {
                    var r = kv.Value; if (r == null) continue;
                    bool war = false; try { war = r.isWar; } catch { }
                    if (war || r.isAlliance) continue;
                    if (r.AnyOnePlayerIs(a)) return r;
                    if (any == null) any = r;
                }
            }
            catch { }
            return any;
        }

        internal static void Tick()
        {
            if (DipGgFixesMod.RelationRepairOn == null || !DipGgFixesMod.RelationRepairOn.Value) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 2f;
            CampaignController cc = null; try { cc = CampaignController.Instance; } catch { }
            if (cc == null || cc.CampaignData == null) { checkedDate = null; seenDate = null; return; }
            string date = null; try { date = cc.CurrentDate.ToString(); } catch { }
            if (date == null) return;
            if (date != seenDate) { seenDate = date; seenAt = now; return; }
            if (date == checkedDate || now - seenAt < 4f) return; // wait until the turn has settled
            checkedDate = date;
            Sweep(cc);
        }

        static void Sweep(CampaignController cc)
        {
            try
            {
                var map = cc.CampaignData.Relations; if (map == null) return;
                var majors = new List<Player>();
                var ps = cc.CampaignData.Players;
                for (int i = 0; i < ps.Count; i++) if (Usable(ps[i])) majors.Add(ps[i]);
                var missing = new List<KeyValuePair<Player, Player>>();
                for (int i = 0; i < majors.Count; i++)
                    for (int j = i + 1; j < majors.Count; j++)
                        if (Find(map, majors[i], majors[j]) == null) missing.Add(new KeyValuePair<Player, Player>(majors[i], majors[j]));
                if (missing.Count == 0) return;
                var names = new List<string>();
                foreach (var pair in missing)
                {
                    Player a = pair.Key, b = pair.Value;
                    var t = Template(map, a);
                    var r = new Relation();
                    r.a = a; r.b = b; r.attitude = 0f; r.isAlliance = false;
                    r.victoryPointsA = 0f; r.victoryPointsB = 0f;
                    if (t != null)
                    {
                        try { r.recentWarStartDate = t.recentWarStartDate; } catch { }
                        try { r.LastTreatyCheckDate = t.LastTreatyCheckDate; } catch { }
                        try { r.LastSpecialEventDate = t.LastSpecialEventDate; } catch { }
                        try { r.TreatyCheckMonthTreashold = t.TreatyCheckMonthTreashold; } catch { }
                        try { r.PeaceTreatyChance = t.PeaceTreatyChance; } catch { }
                    }
                    RelationExt.Assign<Player, Relation>(map, a, b, r);
                    created++;
                    string an = "?", bn = "?"; try { an = a.data.name; bn = b.data.name; } catch { }
                    names.Add(an + "-" + bn);
                }
                MelonLogger.Msg($"[DIP GG Fixes][Politics] relation records restored: {missing.Count} missing pair(s) of great powers got a neutral relation (attitude 0, no war): {string.Join(", ", names)}");
            }
            catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][Politics] relation record check failed: {e.GetType().Name}: {e.Message}"); }
        }
    }
}

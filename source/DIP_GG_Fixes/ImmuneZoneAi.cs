// 1.11.502 (#117, author 31.10, 10:17: the AI chooses its range by the immune zone - "exactly"): an AI capital division whose guns
// are adequate (no close-in, no charge) and whose nearest enemy capital ship is identified by its side seeks a range inside its own
// immune zone against that ship's main battery and, where possible, outside the enemy's zone against its own guns (the historical US
// doctrine of fighting at the range where your protection holds and his does not). Only within its own gun range; held with a band of
// +-2 km so it does not see-saw; nothing changes when there is no own zone. Switch: ImmuneZoneAi.
using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static partial class BattleAi
    {
        internal static MelonPreferences_Entry<bool> IzAiOn;
        internal static void CreateIzPrefs(MelonPreferences_Category cat)
        {
            IzAiOn = cat.CreateEntry("ImmuneZoneAi", true, null, "Battle AI: AI capital divisions whose guns are adequate seek the range inside their own immune zone against the nearest identified enemy capital ship, and outside his zone against their guns where possible");
        }
        static int izLogs;
        static bool IzRange(Ship leader, Division div, DivState st, Ship best, float bestD, float ownR)
        {
            if (IzAiOn == null || !IzAiOn.Value || ImmuneZone.On == null || !ImmuneZone.On.Value) return false;
            try
            {
                if (leader == null || best == null || div == null || ownR <= 0f) return false;
                if (!Fog.Identified(leader.player, best)) return false;
                var ours = ImmuneZone.Battle(leader, best);     // our citadel against his guns
                if (!ours.Any) return false;
                var his = ImmuneZone.Battle(best, leader);      // his citadel against our guns
                float lo = ours.Inner, hi = Mathf.Min(ours.Outer, ownR * 0.95f);
                if (hi <= lo + 500f) return false;
                float want = (lo + hi) / 2f; string how = "inside our immune zone";
                if (his.Any)
                {
                    // the part of our zone where his zone does not protect him: below his inner limit or beyond his outer limit
                    float a1 = lo, b1 = Mathf.Min(hi, his.Inner - 500f), a2 = Mathf.Max(lo, his.Outer + 500f), b2 = hi;
                    float l1 = b1 - a1, l2 = b2 - a2;
                    if (l1 >= 1000f || l2 >= 1000f) { if (l1 >= l2) want = (a1 + b1) / 2f; else want = (a2 + b2) / 2f; how = "inside our immune zone, outside his"; }
                    else how = "inside our immune zone (his zone covers it too)";
                }
                if (Mathf.Abs(bestD - want) <= 2000f) { Why(st, $"holding {bestD / 1000f:0.0} km - {how} ({ImmuneZone.Text(ours)})"); return true; }
                Vector3 me = leader.transform.position, ep = best.transform.position; Vector3 dir = me - ep; dir.y = 0f; if (dir.sqrMagnitude < 1f) dir = leader.transform.forward; dir.Normalize();
                Vector3 dest = ep + dir * want;
                string why = bestD > want ? "closing to the immune zone" : "opening to the immune zone";
                MoveEvasive(div, leader, st, dest, why);
                Why(st, $"{why}: {want / 1000f:0.0} km from {best.name} - {how} ({ImmuneZone.Text(ours)})");
                if (izLogs++ < 60) Log($"{st.name}: immune zone against {best.name} {ImmuneZone.Text(ours)}, his against ours {ImmuneZone.Text(his)} - seeks {want / 1000f:0.0} km (now {bestD / 1000f:0.0} km)");
                return true;
            }
            catch (Exception e) { if (izLogs++ < 60) Log("immune zone range: " + e.Message); return false; }
        }
    }
}

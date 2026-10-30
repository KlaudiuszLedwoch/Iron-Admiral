// 1.11.484 (#104 stage 2, author 30.10 20:51-20:52: "carriers must be treated as floating air bases - their own air support reach,
// and when attacked in a battle they can be destroyed"). Hull data of the carrier hulls are based on the CV Barebone Modification
// by ChonkE (MIT, see NOTICE.md).
//   A ship whose hull name starts with "cv_" is a carrier. When a side of a battle has a carrier afloat, its torpedo aircraft fly
//   off that ship instead of a carrier beyond the horizon: no presence roll, the waves start at the carrier's position, the size of
//   the strike follows the carrier's air group (from its tonnage, CarrierAirGroup) and the nation's air strength (AirBudget).
//   A carrier sunk, burning out or surrendered flies off no more waves, and the aircraft of its air group still aboard are lost
//   (AirBudget.Lost). Switch: CarriersAsShips.
using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class CarrierShips
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<string> GroupScale;
        internal static MelonPreferences_Entry<float> StrikeShare;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("CarriersAsShips", true, null, "Battle: a carrier hull (cv_) in the battle flies off its own torpedo aircraft from its position - no carrier beyond the horizon for that side; the strike follows its air group; a carrier sunk sends no more waves and loses the aircraft still aboard (#104)");
            GroupScale = cat.CreateEntry("CarrierAirGroup", "7500:15,11500:30,20000:80,27000:95,45000:130", null, "Battle: air group of a carrier by its tonnage - pairs tons:aircraft, linear between them (Hosho 15, Langley 30, Yorktown 80, Essex 95, Midway 130)");
            StrikeShare = cat.CreateEntry("CarrierStrikeShare", 30f, null, "Battle: share (%) of a carrier's air group flown off as torpedo aircraft in one battle, over all its waves (torpedo squadrons were about a quarter to a third of an air group)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Carrier] " + m); }
        internal static bool Enabled { get { return On != null && On.Value; } }

        internal static string HullName(Ship s) { try { return s.hull != null && s.hull.data != null ? s.hull.data.name : null; } catch { return null; } }
        internal static bool IsCarrier(Ship s) { var h = HullName(s); return h != null && h.StartsWith("cv_", StringComparison.OrdinalIgnoreCase); }
        internal static bool Afloat(Ship s) { try { return s != null && !s.isSinking && !s.isDead && !s.IsSurrendered; } catch { return false; } }

        // air group from the tonnage (pairs tons:aircraft)
        internal static int AirGroup(Ship s)
        {
            float t = 0f; try { t = s.Tonnage(); } catch { }
            var pts = new List<(float t, float n)>();
            foreach (var part in (GroupScale != null ? GroupScale.Value : "").Split(','))
            {
                var kv = part.Split(':'); if (kv.Length != 2) continue;
                if (float.TryParse(kv[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) pts.Add((a, b));
            }
            if (pts.Count == 0) return 30;
            pts.Sort((x, y) => x.t.CompareTo(y.t));
            if (t <= pts[0].t) return Mathf.Max(6, Mathf.RoundToInt(pts[0].n * Mathf.Max(0.4f, t / pts[0].t)));
            for (int i = 1; i < pts.Count; i++)
                if (t <= pts[i].t) { float f = (t - pts[i - 1].t) / (pts[i].t - pts[i - 1].t); return Mathf.RoundToInt(Mathf.Lerp(pts[i - 1].n, pts[i].n, f)); }
            return Mathf.RoundToInt(pts[pts.Count - 1].n);
        }

        // the carriers afloat of one side (player's or the other); the one with the biggest air group first
        internal static List<Ship> Of(Il2CppSystem.Collections.Generic.List<Ship> ships, bool player)
        {
            var r = new List<Ship>();
            if (!Enabled || ships == null) return r;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (!Afloat(s) || !IsCarrier(s)) continue;
                bool pl = false; try { pl = s.player != null && s.player.isMain; } catch { }
                if (pl == player) r.Add(s);
            }
            r.Sort((a, b) => AirGroup(b).CompareTo(AirGroup(a)));
            return r;
        }
        internal static int Group(List<Ship> cvs) { int n = 0; foreach (var s in cvs) n += AirGroup(s); return n; }

        // torpedo aircraft of one wave: the strike share of the air group, spread over the waves, times the air strength
        internal static int PerWave(int group, int waves, Player p)
        {
            float f = 1f; try { if (p != null && AirBudget.Enabled) f = AirBudget.Stock(p); } catch { }
            float share = (StrikeShare != null ? StrikeShare.Value : 30f) / 100f;
            return Mathf.Clamp(Mathf.RoundToInt(group * share * f / Math.Max(1, waves)), 2, 40);   // at most 40 aircraft drawn in one wave
        }

        // the carrier is lost: the aircraft still aboard go down with it
        internal static void Sunk(Ship cv, Player p, int group, int flown, int strikeSize)
        {
            int aboard = Math.Max(0, group - flown);
            string nm = "?"; try { nm = cv.name; } catch { }
            Log($"{nm} is lost - no more waves from her; {aboard} aircraft of her air group of {group} lost with her");
            try { Dispatch.Air($"{Dispatch.Ship(nm)} is lost - her air group of {group} aircraft is gone with her."); } catch { }
            try { if (p != null && aboard > 0) AirBudget.Lost(p, aboard, strikeSize); } catch { }
        }
        internal static void Found(List<Ship> cvs, Player p, bool pl)
        {
            var names = new List<string>(); foreach (var s in cvs) { string nm = "?"; try { nm = s.name; } catch { } names.Add($"{nm} ({HullName(s)}, {AirGroup(s)} aircraft)"); }
            string nat = "?"; try { nat = p != null ? p.data.name : (pl ? "player" : "enemy"); } catch { }
            Log($"{nat}: carrier(s) in the battle - {string.Join(", ", names)}; the torpedo aircraft fly off them, not from beyond the horizon");
        }
    }
}

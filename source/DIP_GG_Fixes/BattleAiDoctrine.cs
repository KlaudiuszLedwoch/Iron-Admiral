// 1.11.88: battle AI rules from the fleet battle instructions (US Navy War Instructions 1944, chapters 8 and 12) with national and
// period differences from Mods\battleDoctrines.csv (sources in that file and in the project notes):
//  - cruiser roles: defend the battle line against enemy flotillas (12117, 1207), support an own flotilla attack (12117), pin the
//    enemy line from the flank opposite the torpedo attack (12126), keep out of the enemy line's concentrated fire (12120);
//    nations with a "flying squadron" (Japan 1894) send fast cruisers against the enemy flank
//  - night: heavy ships do not close the range unnecessarily (829); flotillas attack by surprise and break off when surprise is
//    lost against a stronger enemy (827); nations that avoid night action hold their flotillas back
//  - turn-away from torpedoes: always (Grand Fleet) or not when the enemy line is nearly beaten (WI 12100)
//  - fire distribution (ShipTick): at most N ships on one target (12165), divided fire when outnumbered (12164), targets that
//    combine hitting power with weak protection first (12166)
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static partial class BattleAi
    {
        static int nightErr;
        internal static bool IsNight()
        {
            try
            {
                float day = BattleManager.Instance.GetDaytimeDetection();
                // the game reports visibility in km (Fog.cs 1.11.67); small values are shares in some battles
                return day > 1.5f ? day < DipGgFixesMod.NightVisibilityKm.Value : day < 0.35f;
            }
            catch (Exception e) { if (nightErr++ < 3) Log("daytime detection unknown: " + e.Message); return false; }
        }

        static string TName(Ship s) { try { var t = s.shipType; return t != null ? t.name : ""; } catch { return ""; } }
        static bool IsFlotilla(Ship s) { var n = TName(s); return n == "dd" || n == "tb"; }

        // keep the course through torpedo tracks when the doctrine allows it and the enemy line is nearly beaten (WI 12100)
        internal static bool HoldCourseThroughTorpedoes(Player p, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies, Doctrine doc)
        {
            if (doc == null || doc.turnAwayAlways) return false;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(p, true); } catch { }
            float own = 0f, foe = 0f;
            for (int i = 0; mine != null && i < mine.Count; i++) { var x = mine[i]; if (Alive(x) && IsLine(x)) own += Ton(x) * (1f - UnityEngine.Mathf.Clamp01(SafeDmg(x))); }
            for (int i = 0; i < enemies.Count; i++) { var x = enemies[i]; if (Alive(x) && IsLine(x)) foe += Ton(x) * (1f - UnityEngine.Mathf.Clamp01(SafeDmg(x))); }
            return own > 0f && foe < own * 0.5f;
        }
        static float SafeDmg(Ship x) { try { return x.DamageEstimation(); } catch { return 0f; } }

        // night rules for a light division that is about to start or is running a torpedo attack; true = took over the division
        internal static bool NightFlotilla(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, float bestD, float launchAt)
        {
            if (!DipGgFixesMod.NightRules.Value || !IsNight()) return false;
            int night = doc != null ? doc.night : 0;
            if (night < 0 && !st.charging)
            {
                Why(st, "avoiding night action - flotilla held back");
                return true;
            }
            if (st.charging && bestD > launchAt)
            {
                // surprise lost: the enemy can see the flotilla before it reaches launch range and the enemy is stronger (827)
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (battle == null) return false;
                var foeP = battle.OppositePlayer(leader.player);
                bool seen = false; try { seen = leader.IsVisibleFor(foeP); } catch { }
                if (!seen) return false;
                Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(leader.player, true); } catch { }
                if (Strength(enemies) <= Strength(mine) * DipGgFixesMod.CoverRetreatStrengthRatio.Value) return false;
                st.charging = false;
                AwayFrom(div, leader, enemies, 5000f); TrySmoke(div, leader, st);
                Log($"{st.name} ({leader.shipType.name}): night attack spotted at {bestD:0} m by a stronger enemy - surprise lost, turns away at full speed");
                Why(st, "night attack broken off - surprise lost");
                return true;
            }
            return false;
        }

        // ---- cruiser roles (ca always, cl when the side has destroyers/torpedo boats); true = took over the division
        internal static bool CruiserRole(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, CampaignBattle battle, Player player)
        {
            if (!DipGgFixesMod.CruiserRoles.Value || battle == null) return false;
            string tn = TName(leader); bool cl = tn == "cl", ca = tn == "ca";
            if (!cl && !ca) return false;
            if (DivDamage(div) > AbortFor(leader, doc)) return false;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(player, true); } catch { }
            if (mine == null) return false;
            var caps = new List<Ship>(); bool flotillas = false;
            for (int i = 0; i < mine.Count; i++) { var x = mine[i]; if (!Alive(x)) continue; if (IsLine(x)) caps.Add(x); else if (IsFlotilla(x) && x.haveTorpedoes) flotillas = true; }
            if (cl && !flotillas) return false;   // no flotillas: the light cruiser is itself the torpedo craft
            float ownR = GunRange(leader); if (ownR <= 0f) return false;
            bool night = IsNight();

            // 1) defend the battle line against enemy flotillas (12117, 1207)
            Ship threat = null; float tbest = float.MaxValue;
            if (caps.Count > 0)
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (!Alive(e) || !e.haveTorpedoes || !(IsFlotilla(e) || TName(e) == "cl") || !e.IsVisibleFor(leader)) continue;
                    float lim = UnityEngine.Mathf.Max(TorpedoRange(e) * 1.5f, 6000f);
                    foreach (var c in caps) { float d = Dist(c, e); if (d < lim && d < tbest) { tbest = d; threat = e; } }
                }
            if (threat != null)
            {
                if (st.reason != "defending the line against enemy flotillas") Log($"{st.name} ({tn}): enemy {threat.name} ({TName(threat)}) {tbest:0} m from the battle line - moves to break up the attack");
                float d = Dist(leader, threat);
                if (d > ownR * 0.6f) { if (div.isRetreating || div.retreat) div.Retreat(false); div.MoveTo(threat.transform.position); }
                Why(st, "defending the line against enemy flotillas");
                return true;
            }

            // enemy line centre (capital ships, else everything visible)
            var ec = UnityEngine.Vector3.zero; int en = 0; float maxEnemyR = 0f; Ship nearestCap = null; float ncd = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !IsLine(e) || !e.IsVisibleFor(leader)) continue;
                ec += e.transform.position; en++; maxEnemyR = UnityEngine.Mathf.Max(maxEnemyR, GunRange(e));
                float d = Dist(leader, e); if (d < ncd) { ncd = d; nearestCap = e; }
            }
            if (en > 0) ec /= en;

            // 2) an own flotilla is attacking: light cruisers support it, heavy cruisers pin the enemy line from the other flank (12117, 12126)
            var fc = UnityEngine.Vector3.zero; int fn = 0; float now = UnityEngine.Time.time;
            foreach (var kv in States) { var s2 = kv.Value; if (s2 == st || s2.playerPtr != player.Pointer) continue; if ((s2.charging || s2.reloading) && now - s2.activeAt < 15f) { fc += s2.pos; fn++; } }
            if (fn > 0 && en > 0)
            {
                fc /= fn;
                var a = fc - ec; a.y = 0f; if (a.sqrMagnitude < 1f) a = leader.transform.forward; a.Normalize();
                if (ca && !night)
                {
                    var pnt = ec - a * ownR * 0.8f;
                    if (st.reason != "pinning the enemy line from the opposite flank") Log($"{st.name} ({tn}): own flotillas attacking - threatens the enemy line from the opposite flank");
                    div.MoveTo(pnt);
                    Why(st, "pinning the enemy line from the opposite flank");
                    return true;
                }
                if (cl)
                {
                    Ship lt = null; float ld = float.MaxValue;
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var e = enemies[i]; if (!Alive(e) || IsLine(e) || !e.IsVisibleFor(leader)) continue;
                        var dv = e.transform.position - fc; dv.y = 0f; float d = dv.magnitude; if (d < ld) { ld = d; lt = e; }
                    }
                    if (st.reason != "supporting the flotilla attack") Log($"{st.name} ({tn}): supports the flotilla attack{(lt != null ? $", engages {lt.name} ({TName(lt)})" : "")}");
                    if (lt != null && ld < 8000f) { if (Dist(leader, lt) > ownR * 0.7f) div.MoveTo(lt.transform.position); }
                    else div.MoveTo(fc - a * 2000f);
                    Why(st, "supporting the flotilla attack");
                    return true;
                }
            }

            // 3) flying squadron: fast cruisers strike the enemy flank (Yalu 1894)
            if (doc != null && doc.flyingSquadron && !night && en > 0)
            {
                float fastest = 0f; for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (Alive(e) && IsLine(e)) fastest = UnityEngine.Mathf.Max(fastest, Speed(e)); }
                if (Speed(leader) > fastest * 1.15f)
                {
                    Ship weak = null; float wd = float.MaxValue;
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var e = enemies[i]; if (!Alive(e) || IsLine(e) || !Combat(e) || !e.IsVisibleFor(leader)) continue;
                        float d = Dist(leader, e); if (d < wd) { wd = d; weak = e; }
                    }
                    if (weak != null)
                    {
                        if (st.reason != "flying squadron - strikes the enemy flank") Log($"{st.name} ({tn}): flying squadron - faster than the enemy line, strikes its flank at {weak.name} ({TName(weak)})");
                        if (wd > ownR * 0.7f) MoveEvasive(div, leader, st, weak.transform.position, "flying squadron");
                        Why(st, "flying squadron - strikes the enemy flank");
                        return true;
                    }
                }
            }

            // 4) keep out of the enemy battle line's concentrated fire (12120)
            if (nearestCap != null && maxEnemyR > 0f && ncd < maxEnemyR * 0.6f && caps.Count > 0)
            {
                var lp = leader.transform.position; var away = lp - nearestCap.transform.position; away.y = 0f;
                if (away.sqrMagnitude > 1f) { away.Normalize(); div.MoveTo(lp + away * 3000f); }
                if (st.reason != "keeping out of the enemy battle line's fire") Log($"{st.name} ({tn}): {ncd:0} m from the enemy battle line - opens the range to avoid its concentrated fire");
                Why(st, "keeping out of the enemy battle line's fire");
                return true;
            }
            return false;
        }
    }

    internal static partial class FireDistribution
    {
        // number of other own ships whose main guns are on this target (from the per-ship target locks)
        internal static int Shooters(Ship self, Ship target, bool lowerIdsOnly = false)
        {
            int n = 0; float now = UnityEngine.Time.time; var pp = self.player; if (pp == null) return 0;
            foreach (var kv in ShipTick.States)
            {
                if (kv.Key == self.Pointer) continue; if (lowerIdsOnly && kv.Key.ToInt64() > self.Pointer.ToInt64()) continue; var st = kv.Value;
                if (st.playerPtr == pp.Pointer && st.mainTarget == target.Pointer && now - st.mainAt < 6f) n++;
            }
            return n;
        }
        static int Count(Il2CppSystem.Collections.Generic.List<Ship> l)
        {
            int n = 0; if (l == null) return 0;
            for (int i = 0; i < l.Count; i++) { var x = l[i]; if (x != null && !x.isDead && !x.isSinking && !x.IsSurrendered) { var t = x.shipType; if (t != null && t.name != "tr") n++; } }
            return n;
        }
        // how many own ships may fire at one target: doctrine limit, 1 when outnumbered (divided fire, 12164)
        internal static int Limit(Ship s)
        {
            var doc = Doctrines.For(s.player); int lim = doc != null ? doc.maxPerTarget : 3;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (b != null && Count(b.ShipsOf(s.player, true)) < Count(b.ShipsOf(b.OppositePlayer(s.player), true))) lim = 1;
            }
            catch { }
            return lim;
        }
        // 0..1: hitting power (main calibre relative to the heaviest in range) x vulnerability (own guns penetrate its belt) (12166)
        internal static float Priority(Ship s, Ship e, float dist, float maxCal)
        {
            float power = maxCal > 0f ? BattleAi.MainCaliber(e) / maxCal : 0.5f;
            float pen, belt; bool noPen = BattleAi.CannotPenetrate(s, e, dist, out pen, out belt);
            return power * (noPen ? 0.3f : 1f);
        }
    }
}

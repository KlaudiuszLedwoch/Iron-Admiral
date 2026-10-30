// 1.11.422 (author 30.10 06:53: "as a rule take screenshots always at spectacular moments - explosions and so on"): in every
// test battle (AutoTestBattle set) and whenever the air strike screenshot switch is on, the mod photographs the spectacular
// moments of the battle with a camera of its own placed for the event: heavy-shell hits with fire, flooding or a magazine or
// machinery hit, and ships sinking. The camera stands 350-450 m off the ship's beam, a little above the water, looking at the
// hit; the shot waits 0.4 s so the explosion is on the picture. At most one shot every 6 s of real time and 30 per battle.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Highlights
    {
        static float next; static int n; static IntPtr battle;
        static readonly List<(float at, Ship s, string tag, float dist, float h)> pending = new List<(float, Ship, string, float, float)>();
        internal static bool Active()
        {
            bool test = false; try { test = AutoTest.Spec != null && !string.IsNullOrEmpty(AutoTest.Spec.Value); } catch { }
            return test || (AirStrike.Screens != null && AirStrike.Screens.Value);
        }
        static void NewBattle()
        {
            IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer; } catch { }
            if (b != battle) { battle = b; n = 0; pending.Clear(); }
        }
        // a heavy hit (HitDispatch): grave hits by guns of 11 in and more, and every magazine hit
        internal static void Hit(Ship victim, float calMm, string kind, bool fire, bool flood)
        {
            if (victim == null || !Active()) return;
            bool big = calMm >= 280f && (fire || flood || kind == "magazine" || kind == "machinery" || kind == "turret");
            if (!big && kind != "magazine") return;
            Queue(victim, kind == "magazine" ? "magazine_hit" : fire ? "hit_fire" : flood ? "hit_flood" : "hit_" + kind, 380f, 25f);
        }
        internal static void Sinking(Ship s) { if (s != null && Active()) Queue(s, "sinking", 450f, 35f); }
        static void Queue(Ship s, string tag, float dist, float h)
        {
            NewBattle();
            if (n >= 30 || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 6f; n++;
            pending.Add((Time.realtimeSinceStartup + 0.4f, s, tag, dist, h));
        }
        internal static void Tick()
        {
            if (pending.Count == 0) return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var e = pending[i]; if (Time.realtimeSinceStartup < e.at) continue;
                pending.RemoveAt(i);
                try
                {
                    if (e.s == null) continue;
                    var sp = e.s.transform.position; var r = e.s.transform.right; r.y = 0f; r.Normalize(); var f = e.s.transform.forward; f.y = 0f; f.Normalize();
                    float side = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                    var cam = sp + r * side * e.dist - f * e.dist * 0.35f + Vector3.up * e.h;
                    var look = sp + Vector3.up * 12f;
                    string name = "ship"; try { name = Dispatch.Ship(e.s.name); } catch { }
                    AirStrike.ShotAt(cam, look, 40f, $"{e.tag}_{name}", true);
                }
                catch { }
            }
        }
        // a ship sinking, from its name (Dispatch.Sunk)
        internal static void SinkingByName(string name)
        {
            if (!Active() || string.IsNullOrEmpty(name)) return;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || b.Ships == null) return;
                for (int i = 0; i < b.Ships.Count; i++) { var s = b.Ships[i]; if (s != null && (s.name == name || Dispatch.Ship(s.name) == name)) { Sinking(s); return; } }
            }
            catch { }
        }
    }
}

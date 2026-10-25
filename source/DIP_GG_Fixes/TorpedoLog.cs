// 1.11.143 (beta): torpedo log. Every torpedo the game launches (Torpedo.Create) is counted per ship; a salvo (torpedoes of one
// ship within 2 s) is written to the log with its target and range, and the battle ends with a summary: torpedoes fired per
// ship and the ships with torpedoes that never fired. Answers "why did my ship not fire torpedoes" from the log alone.
// Also compares the game's own ship velocity (Ship.velocityCurrent) with the velocity the mod measures from positions
// (first few readings per battle) - before the torpedo check switches to the game's value.
// (Hook found in the Lilyz TTS mod: Torpedo.Create(Part from).)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoLog
    {
        class S { public string Name; public int Fired, Salvos, Open; public float Last, Since; public bool Player; }
        static readonly Dictionary<IntPtr, S> ships = new Dictionary<IntPtr, S>();
        static bool inBattle; static int lines, velLogs, errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Torpedo] " + m); }

        internal static void Created(Part from)
        {
            try
            {
                var s = from != null ? from.ship : null; if (s == null) return;
                float now = Time.time;
                if (!ships.TryGetValue(s.Pointer, out var r)) { r = new S { Name = s.name }; try { r.Player = s.player != null && s.player.isMain; } catch { } ships[s.Pointer] = r; }
                r.Fired++;
                if (now - r.Last > 2f) { Flush(s, r); r.Salvos++; r.Open = 0; r.Since = now; }
                r.Open++; r.Last = now;
            }
            catch (Exception e) { if (errors++ < 5) Log("count: " + e.Message); }
        }
        // the previous salvo of this ship is written when the next one starts or at the battle end
        static void Flush(Ship s, S r)
        {
            if (r.Open <= 0 || lines >= 200) return;
            lines++;
            string tgt = ""; try { var t = ShipTick.TorpedoTarget(s); if (t != null) tgt = $" at {t.name}, {BattleAi.Dist(s, t):0} m"; } catch { }
            Log($"{r.Name}{(r.Player ? " (player)" : "")}: salvo of {r.Open} torpedo(es){tgt}");
            r.Open = 0;
        }

        internal static void Velocity(Ship x, Vector3 measured)
        {
            if (velLogs >= 6) return;
            try
            {
                var v = x.velocityCurrent; v.y = 0f; measured.y = 0f;
                if (measured.sqrMagnitude < 1f) return;
                velLogs++;
                Log($"velocity check {x.name}: game {v.magnitude:0.00} (dir {v.normalized.x:0.00},{v.normalized.z:0.00}), measured {measured.magnitude:0.00} (dir {measured.normalized.x:0.00},{measured.normalized.z:0.00}), top speed {BattleAi.Speed(x):0.0}");
            }
            catch (Exception e) { if (errors++ < 5) Log("velocity: " + e.Message); }
        }

        internal static void Tick()
        {
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (battle && !inBattle) { inBattle = true; ships.Clear(); lines = 0; velLogs = 0; }
            else if (!battle && inBattle) { inBattle = false; Summary(); }
        }

        static void Summary()
        {
            try
            {
                var fired = ships.Values.Where(x => x.Fired > 0).OrderByDescending(x => x.Fired).ToList();
                var silent = new List<string>();
                try
                {
                    var all = Ship.AllShips;
                    if (all != null) for (int i = 0; i < all.Count; i++)
                        {
                            var s = all[i]; if (s == null) continue;
                            bool tp = false; try { tp = s.haveTorpedoes; } catch { }
                            if (tp && !ships.ContainsKey(s.Pointer)) { string pl = ""; try { pl = s.player != null && s.player.isMain ? " (player)" : ""; } catch { } silent.Add(s.name + pl); }
                        }
                }
                catch { }
                Log($"battle summary - torpedoes fired: {(fired.Count == 0 ? "none" : string.Join("; ", fired.Select(x => $"{x.Name}{(x.Player ? " (player)" : "")} {x.Fired} in {x.Salvos} salvo(s)")))}");
                if (silent.Count > 0) Log($"battle summary - ships with torpedoes that never fired: {string.Join("; ", silent.Take(40))}");
            }
            catch (Exception e) { Log("summary: " + e.Message); }
            ships.Clear();
        }
    }

    [HarmonyPatch]
    internal static class Patch_TorpedoLog
    {
        static MethodBase TargetMethod()
        {
            try { return AccessTools.GetDeclaredMethods(typeof(Torpedo)).FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(Part)); } catch { return null; }
        }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Part __0) { TorpedoLog.Created(__0); }
    }
}

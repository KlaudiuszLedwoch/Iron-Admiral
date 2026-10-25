// 1.11.134: safe patch registration. MelonLoader used to apply all [HarmonyPatch] classes of the mod in one PatchAll call:
// one patch whose game method had changed (a game, DIP or TAF update) threw and the classes after it were never applied.
// Now the automatic PatchAll is off ([assembly: HarmonyDontPatchAll] in Mod.cs) and every patch class is applied on its own:
// a class that cannot be applied is skipped with a warning, the rest of the mod keeps working. Classes that check their
// target themselves use TargetMethod() + Prepare() (Prepare returns false when the game method is missing).
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace DipGgFixes
{
    internal static class SafePatches
    {
        internal static readonly List<string> Failed = new List<string>();
        internal static int Applied, Skipped;

        internal static void ApplyAll(HarmonyLib.Harmony h)
        {
            Applied = Skipped = 0; Failed.Clear();
            Type[] types;
            try { types = AccessTools.GetTypesFromAssembly(typeof(SafePatches).Assembly); }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Patches] type list failed (" + e.Message + ") - falling back to PatchAll"); try { h.PatchAll(typeof(SafePatches).Assembly); } catch (Exception e2) { MelonLogger.Warning("[DIP GG Fixes][Patches] PatchAll: " + e2.Message); } return; }
            foreach (var t in types)
            {
                bool marked = false;
                try { marked = t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0; } catch { }
                if (!marked) continue;
                try
                {
                    var r = h.CreateClassProcessor(t).Patch();
                    if (r != null && r.Count > 0) Applied++; else Skipped++; // Prepare() returned false or nothing to patch
                }
                catch (Exception e)
                {
                    var ex = e; while (ex.InnerException != null) ex = ex.InnerException;
                    Failed.Add(t.Name + ": " + ex.Message);
                    MelonLogger.Warning("[DIP GG Fixes][Patches] " + t.Name + " not applied - this part of the mod is off: " + ex.Message);
                }
            }
            MelonLogger.Msg($"[DIP GG Fixes][Patches] {Applied} patch classes applied, {Skipped} skipped (game method missing or switched off), {Failed.Count} failed");
        }

        // for classes that want to resolve their target themselves: AccessTools.Method with exact parameter types, null when missing
        internal static MethodBase Find(Type type, string name, params Type[] args)
        {
            try { return args != null && args.Length > 0 ? AccessTools.Method(type, name, args) : AccessTools.Method(type, name); } catch { return null; }
        }
    }
}

// 1.11.464 (author 30.10 20:06: "after pressing Refit it pointlessly asks whether I want to enter refit"): TweaksAndFixes wraps
// every button of the designer's upper bar in a "Are you sure?" pop-up (UiM.AddConfirmationPopups -> AddConfirmPopupToButton with
// the text "$TAF_Ui_Dockyard_Confirm_Action_<button>"). Entering refit mode changes nothing by itself - leaving it without saving
// discards the refit - so the question only costs a click. A prefix on AddConfirmPopupToButton skips the wrapping for the RefitShip
// button and leaves the game's own button as it was; the other confirmations (delete, scrap, automatic refit) stay.
// Switch: SkipRefitConfirm (on). TweaksAndFixes builds the pop-ups once, when the designer bar is set up, so a change of the switch
// takes effect after a restart of the game.
using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace DipGgFixes
{
    internal static class RefitConfirm
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SkipRefitConfirm", true, null, "Pressing Refit in the dockyard enters refit mode at once, without TweaksAndFixes' 'Are you sure you want to enter Refit Mode?' question (other confirmations stay; takes effect after a restart)");
        }
        static int skipped;

        internal static void Install(HarmonyLib.Harmony h)
        {
            MethodInfo target = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "TweaksAndFixes") continue;
                Type[] types; try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    var m = t.GetMethod("AddConfirmPopupToButton", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null) { target = m; break; }
                }
                break;
            }
            if (target == null) { MelonLogger.Msg("[DIP GG Fixes][RefitConfirm] TweaksAndFixes' confirmation pop-ups not found - nothing to change"); return; }
            h.Patch(target, prefix: new HarmonyMethod(typeof(RefitConfirm).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic)));
            MelonLogger.Msg("[DIP GG Fixes][RefitConfirm] hooked " + target.DeclaringType?.Name + "." + target.Name);
        }

        // parameter names as in TweaksAndFixes 3.21.1: (Button button, string text, Action before, Action after, bool invokeOnYes, bool invokeOnNo)
        static bool Pre(object[] __args)
        {
            try
            {
                if (On == null || !On.Value || __args == null || __args.Length < 2) return true;
                var text = __args[1] as string;
                if (text == null || !text.EndsWith("_Confirm_Action_RefitShip")) return true;
                if (skipped++ < 3) MelonLogger.Msg("[DIP GG Fixes][RefitConfirm] Refit button left without the confirmation pop-up");
                return false;
            }
            catch { return true; }
        }
    }
}

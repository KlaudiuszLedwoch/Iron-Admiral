// DIP GG Fixes - MelonLoader add-on for DIP GG Extended.
//
// Fix 1: CampaignController.CheckMinorNationThreat sometimes throws
//   "ArgumentException: An item with the same key has already been added.
//   Key: <name> (Province)". The exception is thrown inside the game's own
//   code and aborts the rest of the end-of-turn processing (fleet management,
//   events, autosave). This patch catches that exception so the turn can
//   finish. Only the minor-nation threat check for that turn is skipped.
//
// Feature 2: player's campaign designs are copied to the shared designs pool
//   (Designs folder) every time the player leaves the ship constructor, for
//   every major nation that is allowed to use the design's hull. AI uses
//   shared designs only if the campaign's design-usage setting allows it.
//   New shared designs are loaded by the game on the next start.
//   Settings: UserData/MelonPreferences.cfg, section [DIP_GG_Fixes].
//
// Feature 3: AI majors that cannot afford a battleship shift building
//   towards cruisers. Player.GetShipBuildRatio (personality buildRatio,
//   used by BuildNewShips to pick ship types) is scaled down for bb/bc and
//   up for ca/cl while the AI's cash is below the cost of its cheapest
//   battleship design, or while its monthly income is negative.
//
// Fix 5: ProvinceBattleManager.TryGenerateSmallerNationsBattle and
//   GenerateBattlesForMediumNations can throw the same duplicate-key error
//   ("Key: Prussia (Province)") and abort the turn. Same guard as Fix 1.
//
// Feature 6: "balance of power" events. Once per turn, if the player's fleet
//   tonnage exceeds the strongest AI major's by a set ratio, each AI major
//   has a chance to get an emergency naval appropriation (cash grant worth
//   a few months of its naval budget), reported in the news feed.
//
// Feature 7: stronger diplomatic swings. CampaignController.AdjustAttitude
//   (every relation/attitude change) gets its delta multiplied
//   (improvements: DiplomacyAttitudeMultiplier, default 1.5; worsening:
//   DiplomacyNegativeAttitudeMultiplier, default 4, since 1.6). Only the
//   outermost call is scaled (1.6: recursive propagation to allies/enemies
//   is not multiplied again). Initial setup calls
//   (init) and forced calls (force, e.g. TweaksAndFixes alliance resets)
//   are left unchanged.
//
// Feature 9 (1.7): battle AI for light ships (tb/dd/cl). Postfix on
//   Ship.AILogic for AI-controlled division leaders of light ships: once
//   the division is within TorpedoChargeStartRange of a visible enemy
//   bb/bc/ca, it charges that target (Division.MoveTo on the enemy's
//   position) until it gets inside its torpedo range + margin, where the
//   game's own torpedo logic fires. A charging division keeps charging
//   while it still has usable torpedo tubes and its structure damage
//   (DamageEstimation) is below LightShipChargeAbortDamage; otherwise it
//   retreats (Division.Retreat) and the normal game AI takes over.
//   Each decision is re-evaluated every BattleAiThinkSeconds.
//   1.8: every retreating AI division whose slowest ship is not faster than
//   the fastest visible pursuer (within PursuitRange) stops retreating
//   (Division.Retreat(false)) and fights; light torpedo ships then charge
//   regardless of the start range.
//   1.8.1: ships slower than the rest of their AI division (speed below
//   SlowShipSpeedRatio x fastest ship, damaged engines etc.) are detached
//   (Ship.DetachShips) into a new division that retreats, so the healthy
//   ships are not slowed down.
//   1.9: national battle doctrines from Mods\battleDoctrines.csv (per nation:
//   distanceMult applied to ai_distance_mod while the game computes an AI
//   ship's preferred range in AILogic/RangeCalc; chargeStartRange,
//   abortDamage, escapeMargin replace the global preference values).
//   Postfix on Ship.FindBestEnemy (focus fire): if another visible enemy
//   in gun range of the same group is already damaged above
//   FocusFireMinDamage, prefer the most damaged one (AI ships only).
//
// Fix 8: stalled AI ship construction. Some AI nations keep ships "under
//   construction" at 0% for years (seen: France, 5 ships from 1922-1924).
//   A Postfix on CampaignController.AdvanceShips records which players had
//   their ships advanced each turn. Once per turn, any AI major with ships
//   under construction that was NOT advanced during the previous turn gets
//   CampaignController.AdvanceShips(player, onlyBuild: true) called for it.
//   Every case is logged ("[DIP GG Fixes] Construction ...").
//
// Feature 4: minor nations get more free port capacity in the game's
//   warship-trade check (Player.GetMinorNationFreePortCapacity), so they
//   pass the capacity test for ordering and buying used ships more often.
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

[assembly: MelonInfo(typeof(DipGgFixes.DipGgFixesMod), "DIP GG Fixes", "1.10.2", "DIP GG Extended")]
[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]

namespace DipGgFixes
{
    public class DipGgFixesMod : MelonMod
    {
        internal static int Caught;
        internal static MelonPreferences_Entry<bool> AutoShare;
        internal static MelonPreferences_Entry<bool> AllNations;
        internal static MelonPreferences_Entry<bool> CruisersWhenPoor;
        internal static MelonPreferences_Entry<float> BigShipFactor;
        internal static MelonPreferences_Entry<float> CruiserFactor;
        internal static MelonPreferences_Entry<float> MinorPortCapacityMult;
        internal static MelonPreferences_Entry<float> AttitudeMult;
        internal static MelonPreferences_Entry<float> NegAttitudeMult;
        internal static MelonPreferences_Entry<bool> FixStalledBuilding;
        internal static MelonPreferences_Entry<int> CollapseGraceMonths;
        internal static MelonPreferences_Entry<bool> LightShipCharge;
        internal static MelonPreferences_Entry<float> ChargeStartRange;
        internal static MelonPreferences_Entry<float> ChargeTorpedoMargin;
        internal static MelonPreferences_Entry<float> ChargeAbortDamage;
        internal static MelonPreferences_Entry<float> BattleThinkSeconds;
        internal static MelonPreferences_Entry<bool> FocusFire;
        internal static MelonPreferences_Entry<float> TorpedoRangeFallback;
        internal static MelonPreferences_Entry<bool> FightWhenCannotEscape;
        internal static MelonPreferences_Entry<bool> DetachSlowShips;
        internal static MelonPreferences_Entry<float> SlowShipSpeedRatio;
        internal static MelonPreferences_Entry<float> DetachCooldownSeconds;
        internal static MelonPreferences_Entry<float> PursuitRange;
        internal static MelonPreferences_Entry<float> EscapeSpeedMargin;
        internal static MelonPreferences_Entry<float> FocusFireMinDamage;
        internal static MelonPreferences_Entry<bool> BattleAiLog;
        internal static MelonPreferences_Entry<bool> BalanceEvents;
        internal static MelonPreferences_Entry<float> BalanceRatio;
        internal static MelonPreferences_Entry<float> BalanceChance;
        internal static MelonPreferences_Entry<float> BalanceMonths;

        public override void OnInitializeMelon()
        {
            var cat = MelonPreferences.CreateCategory("DIP_GG_Fixes");
            AutoShare = cat.CreateEntry("AutoShareDesigns", true, null,
                "Copy the player's campaign designs to shared designs when leaving the constructor");
            AllNations = cat.CreateEntry("ShareToAllNations", true, null,
                "true = every major nation allowed to use the hull; false = only the player's nation");
            CruisersWhenPoor = cat.CreateEntry("CruisersWhenPoor", true, null,
                "AI majors that cannot afford a battleship build more cruisers (CA/CL) instead");
            BigShipFactor = cat.CreateEntry("PoorBigShipRatioFactor", 0.15f, null,
                "Multiplier for the bb/bc build ratio while the AI cannot afford a battleship");
            CruiserFactor = cat.CreateEntry("PoorCruiserRatioFactor", 1.6f, null,
                "Multiplier for the ca/cl build ratio while the AI cannot afford a battleship");
            MinorPortCapacityMult = cat.CreateEntry("MinorPortCapacityMultiplier", 2.0f, null,
                "Multiplier for minor nations' free port capacity in warship trade (orders and used-ship purchases)");
            BalanceEvents = cat.CreateEntry("BalanceOfPowerEvents", true, null,
                "AI majors get emergency naval funding when the player's fleet is much stronger than theirs");
            BalanceRatio = cat.CreateEntry("BalanceTonnageRatio", 1.5f, null,
                "Trigger when player fleet tonnage > this x the strongest AI major's fleet tonnage");
            BalanceChance = cat.CreateEntry("BalanceEventChance", 0.2f, null,
                "Chance per AI major per turn while the trigger holds (0-1)");
            BalanceMonths = cat.CreateEntry("BalanceGrantMonths", 3.0f, null,
                "Cash grant in months of the AI's naval budget (capped at 25% of its yearly state budget)");
            AttitudeMult = cat.CreateEntry("DiplomacyAttitudeMultiplier", 1.5f, null,
                "Multiplier for positive diplomatic attitude changes (improvements; 1 = vanilla)");
            NegAttitudeMult = cat.CreateEntry("DiplomacyNegativeAttitudeMultiplier", 4.0f, null,
                "Multiplier for negative diplomatic attitude changes (worsening, by the player and by the AI; 1 = vanilla)");
            FixStalledBuilding = cat.CreateEntry("FixStalledConstruction", true, null,
                "Advance AI ship construction for nations whose building ships were not advanced during a turn");
            CollapseGraceMonths = cat.CreateEntry("EconomicCollapseGraceMonths", 24, null,
                "An AI nation whose state budget fell to zero is dissolved (\"economic collapse\") only after this many consecutive months at zero (0 = vanilla: immediately). Losing all ports still dissolves at once.");
            LightShipCharge = cat.CreateEntry("LightShipTorpedoCharge", true, null,
                "AI tb/dd/cl divisions charge enemy bb/bc/ca to use torpedoes, then retreat when out of torpedoes or badly damaged");
            ChargeStartRange = cat.CreateEntry("TorpedoChargeStartRange", 12000f, null,
                "Start a charge when a visible enemy capital ship is closer than this (meters)");
            ChargeTorpedoMargin = cat.CreateEntry("TorpedoChargeRangeMargin", 0.7f, null,
                "Charge until distance < torpedo range x this factor (lower = closer)");
            ChargeAbortDamage = cat.CreateEntry("LightShipChargeAbortDamage", 0.6f, null,
                "Abort the charge and retreat when the division's structure/flooding damage estimate exceeds this (0-1)");
            BattleThinkSeconds = cat.CreateEntry("BattleAiThinkSeconds", 4f, null,
                "How often (battle seconds) the add-on re-evaluates each light division");
            DetachSlowShips = cat.CreateEntry("DetachSlowShips", true, null,
                "AI ships that slow their division down (damaged/slow) are detached into a new division that retreats on its own");
            SlowShipSpeedRatio = cat.CreateEntry("SlowShipSpeedRatio", 0.8f, null,
                "A ship counts as slowing the division when its speed < fastest ship's speed x this factor (and at least 2 kn slower)");
            DetachCooldownSeconds = cat.CreateEntry("DetachCooldownSeconds", 30f, null,
                "Minimum battle seconds between two detach operations on the same division");
            FightWhenCannotEscape = cat.CreateEntry("FightWhenCannotEscape", true, null,
                "Any retreating AI division that is slower than a visible pursuing enemy stops retreating and fights (light ships charge with torpedoes)");
            PursuitRange = cat.CreateEntry("PursuitRange", 15000f, null,
                "An enemy counts as pursuing when visible and closer than this (meters)");
            EscapeSpeedMargin = cat.CreateEntry("EscapeSpeedMargin", 1.0f, null,
                "Division cannot escape when its slowest ship's speed <= fastest pursuer speed x this factor");
            TorpedoRangeFallback = cat.CreateEntry("TorpedoRangeFallback", 6000f, null,
                "Assumed torpedo range (m) when the game does not report one for the ship");
            FocusFire = cat.CreateEntry("FocusFire", true, null,
                "AI guns prefer already-damaged enemy ships in range (focus fire)");
            FocusFireMinDamage = cat.CreateEntry("FocusFireMinDamage", 0.15f, null,
                "A target counts as damaged for focus fire above this damage estimate (0-1)");
            BattleAiLog = cat.CreateEntry("BattleAiLog", true, null,
                "Write charge/retreat/focus decisions to the MelonLoader log");
            Doctrines.Load();
            LoggerInstance.Msg($"Battle AI: LightShipTorpedoCharge={LightShipCharge.Value} (start {ChargeStartRange.Value} m, margin {ChargeTorpedoMargin.Value}, abort {ChargeAbortDamage.Value}), FightWhenCannotEscape={FightWhenCannotEscape.Value} (pursuit {PursuitRange.Value} m, margin {EscapeSpeedMargin.Value}), DetachSlowShips={DetachSlowShips.Value} (ratio {SlowShipSpeedRatio.Value}, cooldown {DetachCooldownSeconds.Value} s), FocusFire={FocusFire.Value} (min {FocusFireMinDamage.Value})");
            LoggerInstance.Msg($"Diplomacy attitude multiplier: positive={AttitudeMult.Value}, negative={NegAttitudeMult.Value}, FixStalledConstruction={FixStalledBuilding.Value}, EconomicCollapseGraceMonths={CollapseGraceMonths.Value}");
            LoggerInstance.Msg($"Balance-of-power events={BalanceEvents.Value} (ratio {BalanceRatio.Value}, chance {BalanceChance.Value}, {BalanceMonths.Value} months)");
            LoggerInstance.Msg($"Loaded 1.10.2. CheckMinorNationThreat guard on. AutoShareDesigns={AutoShare.Value}, ShareToAllNations={AllNations.Value}, "
                + $"CruisersWhenPoor={CruisersWhenPoor.Value} (bb/bc x{BigShipFactor.Value}, ca/cl x{CruiserFactor.Value}), MinorPortCapacityMultiplier={MinorPortCapacityMult.Value}");
        }
    }

    [HarmonyPatch(typeof(CampaignController), "CheckMinorNationThreat")]
    internal static class Patch_CheckMinorNationThreat
    {
        // Harmony finalizer: runs after the original method even if it threw.
        // Returning null swallows the exception.
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                DipGgFixesMod.Caught++;
                MelonLogger.Warning($"[DIP GG Fixes] CheckMinorNationThreat threw and was skipped for this turn "
                    + $"(#{DipGgFixesMod.Caught}): {__exception.GetType().Name}: {__exception.Message}");
            }
            BalanceOfPower.CheckOncePerTurn();
            ConstructionWatch.CheckOncePerTurn();
            return null;
        }
    }

    [HarmonyPatch(typeof(ProvinceBattleManager), "TryGenerateSmallerNationsBattle")]
    internal static class Patch_TryGenerateSmallerNationsBattle
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                MelonLogger.Warning("[DIP GG Fixes] TryGenerateSmallerNationsBattle threw and was skipped for this turn: "
                    + __exception.GetType().Name + ": " + __exception.Message);
            return null;
        }
    }

    [HarmonyPatch(typeof(ProvinceBattleManager), "GenerateBattlesForMediumNations")]
    internal static class Patch_GenerateBattlesForMediumNations
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                MelonLogger.Warning("[DIP GG Fixes] GenerateBattlesForMediumNations threw and was skipped for this turn: "
                    + __exception.GetType().Name + ": " + __exception.Message);
            return null;
        }
    }

    internal static class BalanceOfPower
    {
        private static string lastTurn;
        private static readonly System.Random rng = new System.Random();

        internal static void CheckOncePerTurn()
        {
            try
            {
                if (DipGgFixesMod.BalanceEvents == null || !DipGgFixesMod.BalanceEvents.Value) return;
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return;
                string turn = cc.CurrentDate.ToString();
                if (turn == lastTurn) return;
                lastTurn = turn;

                Player main = null;
                var ais = new List<Player>();
                var majors = cc.CampaignData.PlayersMajor;
                for (int i = 0; i < majors.Count; i++)
                {
                    var p = majors[i];
                    if (p == null || p.isDisabled) continue;
                    if (p.isMain) main = p; else if (p.isAi) ais.Add(p);
                }
                if (main == null || ais.Count == 0) return;

                float mainTon = FleetTonnage(main);
                float maxAi = 0f;
                foreach (var p in ais) maxAi = Math.Max(maxAi, FleetTonnage(p));
                if (maxAi <= 0f || mainTon < maxAi * DipGgFixesMod.BalanceRatio.Value) return;

                string mainName = main.data != null ? main.data.nameUi : "player";
                foreach (var p in ais)
                {
                    if (rng.NextDouble() >= DipGgFixesMod.BalanceChance.Value) continue;
                    float grant = p.Budget() * DipGgFixesMod.BalanceMonths.Value;
                    float cap = p.StateBudget() * 0.25f;
                    if (cap > 0f && grant > cap) grant = cap;
                    if (grant <= 0f) continue;
                    p.cash = p.cash + grant;
                    string name = p.data != null ? p.data.nameUi : "?";
                    string text = $"{name}: emergency naval appropriations in response to {mainName}'s naval dominance (+{grant:N0}).";
                    MelonLogger.Msg("[DIP GG Fixes] " + text);
                    try { if (G.ui != null) G.ui.ReportImportant(text, null); } catch { }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DIP GG Fixes] Balance-of-power check failed: " + e.Message);
            }
        }

        private static float FleetTonnage(Player p)
        {
            float t = 0f;
            if (p.fleet == null) return 0f;
            var en = p.fleet.GetEnumerator();
            var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var s = en.Current; if (s != null) t += s.Tonnage(); }
            return t;
        }
    }

    [HarmonyPatch(typeof(Ui), "ExitConstructor")]
    internal static class Patch_ExitConstructor
    {
        private static void Postfix()
        {
            try
            {
                if (DipGgFixesMod.AutoShare == null || !DipGgFixesMod.AutoShare.Value) return;
                if (!GameManager.IsCampaign) return;
                SharedDesignExporter.ExportPlayerDesigns();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DIP GG Fixes] Shared design export failed: " + e);
            }
        }
    }

    internal static class SharedDesignExporter
    {
        internal static void ExportPlayerDesigns()
        {
            var cc = CampaignController.Instance;
            if (cc == null || cc.CampaignData == null) return;

            Player main = null;
            var players = cc.CampaignData.Players;
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].isMain) { main = players[i]; break; }
            if (main == null || main.designs == null) return;

            // Major nations from game data
            var majors = new List<PlayerData>();
            var en = G.GameData.playersMajor.Values.GetEnumerator();
            while (en.MoveNext()) if (en.Current != null) majors.Add(en.Current);

            // Snapshot the design list first (we must not modify game state while enumerating)
            var designs = new List<Ship>();
            var typed = main.designs.GetEnumerator();
            var untyped = typed.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (untyped.MoveNext()) { var s = typed.Current; if (s != null) designs.Add(s); }

            int written = 0, skipped = 0;
            foreach (var design in designs)
            {
                if (design.IsSharedDesign || design.isRefitDesign) continue;
                var hull = design.hull;
                if (hull == null || hull.data == null) continue;
                var allowed = hull.data.countriesx;

                foreach (var pd in majors)
                {
                    bool isOwn = main.data != null && pd.name == main.data.name;
                    if (!DipGgFixesMod.AllNations.Value && !isOwn) continue;
                    if (allowed != null && allowed.Count > 0 && !allowed.Contains(pd)) continue;

                    var store = design.ToStore(false);
                    if (store == null) continue;
                    string origId = store.id.ToString();
                    string shortId = origId.Length >= 8 ? origId.Substring(0, 8) : origId;
                    string fileBase = $"{store.YearCreated} {pd.name} {store.vesselName} [{shortId}]";
                    string fileName;
                    string path = Storage.GetSavedDesignPath(fileBase, out fileName);
                    if (string.IsNullOrEmpty(path)) { skipped++; continue; }
                    // GetSavedDesignPath appends " 2", " 3", ... when the name is taken, so the
                    // File.Exists test on its result never fires. Look for any existing file of
                    // this design (short id) for this nation instead (1.10.2; before that every
                    // turn wrote a new copy: 235 files for 10 designs).
                    if (AlreadyExported(path, pd.name, shortId)) { skipped++; continue; }

                    store.playerName = pd.name;
                    store.playerOriginal = pd.name;
                    store.isSharedDesign = true;
                    store.id = Il2CppSystem.Guid.NewGuid();

                    var bytes = Util.SerializeObjectByte<Ship.Store>(store);
                    Storage.SaveSharedDesignShipByte(path, bytes);
                    written++;
                }
            }
            if (written > 0)
                MelonLogger.Msg($"[DIP GG Fixes] Exported {written} shared design file(s) ({skipped} already existed). They load on the next game start.");
        }

        private static readonly Dictionary<string, HashSet<string>> ExistingCache = new Dictionary<string, HashSet<string>>();
        private static bool AlreadyExported(string path, string nation, string shortId)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                HashSet<string> keys;
                if (!ExistingCache.TryGetValue(dir, out keys))
                {
                    keys = new HashSet<string>();
                    foreach (var f in Directory.GetFiles(dir, "*.bindesign"))
                    {
                        var n = Path.GetFileNameWithoutExtension(f);
                        int lb = n.LastIndexOf('['), rb = n.LastIndexOf(']');
                        if (lb < 0 || rb < lb) continue;
                        var parts = n.Split(' ');
                        if (parts.Length < 3) continue;
                        keys.Add(parts[1] + "|" + n.Substring(lb + 1, rb - lb - 1));
                    }
                    ExistingCache[dir] = keys;
                }
                string key = nation + "|" + shortId;
                if (keys.Contains(key)) return true;
                keys.Add(key);
                return false;
            }
            catch { return File.Exists(path); }
        }
    }

    [HarmonyPatch(typeof(Player), "GetShipBuildRatio")]
    internal static class Patch_GetShipBuildRatio
    {
        private struct Entry { public int Frame; public bool Poor; }
        private static readonly Dictionary<IntPtr, Entry> Cache = new Dictionary<IntPtr, Entry>();

        private static void Postfix(Player __instance, string shipType, ref float __result)
        {
            try
            {
                if (DipGgFixesMod.CruisersWhenPoor == null || !DipGgFixesMod.CruisersWhenPoor.Value) return;
                if (__instance == null || __result <= 0f || string.IsNullOrEmpty(shipType)) return;
                bool big = shipType == "bb" || shipType == "bc";
                bool cruiser = shipType == "ca" || shipType == "cl";
                if (!big && !cruiser) return;
                if (!__instance.isAi || !__instance.isMajor) return;
                if (!IsPoor(__instance)) return;
                __result *= big ? DipGgFixesMod.BigShipFactor.Value : DipGgFixesMod.CruiserFactor.Value;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DIP GG Fixes] GetShipBuildRatio patch error: " + e.Message);
            }
        }

        private static bool IsPoor(Player p)
        {
            int frame = UnityEngine.Time.frameCount;
            if (Cache.TryGetValue(p.Pointer, out var e) && e.Frame == frame) return e.Poor;

            float cheapestBb = float.MaxValue;
            var en = p.designs.GetEnumerator();
            var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext())
            {
                var d = en.Current;
                if (d == null || d.shipType == null || d.shipType.name != "bb") continue;
                float c = d.Cost();
                if (c > 0f && c < cheapestBb) cheapestBb = c;
            }
            bool poor = false;
            if (cheapestBb < float.MaxValue && p.cash < cheapestBb) poor = true;
            if (p.Income() < 0f) poor = true;
            Cache[p.Pointer] = new Entry { Frame = frame, Poor = poor };
            return poor;
        }
    }

    // Fix 12: economic-collapse dissolution grace period.
    //   CampaignController.CheckForCampaignEnd dissolves an AI nation the first turn
    //   Player.StateBudget() <= 0 ("DissolvedDueEconomicCollapse") provided Player.CanDissolve()
    //   returns true. CanDissolve only checks turns since the last dissolution
    //   (turns_last_diissolve_nation). The params.csv key lose_provinces_threshold does not
    //   exist in the game code (no such string literal in GameAssembly) and has no effect.
    //   This prefix makes CanDissolve return false while the nation still owns ports and has
    //   been at zero budget for fewer than EconomicCollapseGraceMonths consecutive months,
    //   so a post-war crisis has to last before it erases a nation. The lost-all-ports branch
    //   (Player.HavePorts == false) is left untouched.
    [HarmonyPatch(typeof(Player), "CanDissolve")]
    internal static class Patch_CanDissolve
    {
        private sealed class St { public int FirstMonth = -1; public int LastMonth = -1; public bool Logged; }
        private static readonly Dictionary<string, St> States = new Dictionary<string, St>();

        private static bool Prefix(Player __instance, ref bool __result)
        {
            try
            {
                var g = DipGgFixesMod.CollapseGraceMonths;
                if (g == null || g.Value <= 0) return true;
                var p = __instance;
                if (p == null || p.isMain || !p.HavePorts) return true;
                var cc = CampaignController.Instance;
                if (cc == null) return true;
                var d = cc.CurrentDate.AsDate();
                int month = d.Year * 12 + d.Month;
                string key = p.data != null ? p.data.name : p.Pointer.ToString();
                if (!States.TryGetValue(key, out var st)) States[key] = st = new St();
                if (st.LastMonth < 0 || month - st.LastMonth > 1)
                {
                    st.FirstMonth = month; st.Logged = false;
                    MelonLogger.Msg($"[DIP GG Fixes] {key}: state budget at zero, dissolution deferred for up to {g.Value} months");
                }
                st.LastMonth = month;
                int elapsed = month - st.FirstMonth;
                if (elapsed < g.Value) { __result = false; return false; }
                if (!st.Logged)
                {
                    st.Logged = true;
                    MelonLogger.Msg($"[DIP GG Fixes] {key}: {elapsed} months at zero budget, grace period over, game may dissolve the nation");
                }
                return true;
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[DIP GG Fixes] CanDissolve prefix failed: {e.Message}");
                return true;
            }
        }
    }

    // Fix 13: guard against a null turretCalibers list in a ship store.
    //   TweaksAndFixes 3.16.7 (Patch_VesselEntityFromStore.Prefix_FromBaseStore -> TAFShipData.LoadTCs)
    //   enumerates store.turretCalibers without a null check. Predefined-design stores saved
    //   without that field make the prefix throw, which kills the battle loading coroutine
    //   (custom battles hang on "building ships"). Later TAF versions added a null check; this
    //   prefix runs before TAF's (Priority.First) and substitutes an empty list, which both TAF
    //   and the game's own FromBaseStore handle as "no turret caliber overrides".
    [HarmonyPatch(typeof(VesselEntity), "FromBaseStore")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_FromBaseStore_NullGuard
    {
        internal static int Fixed;
        private static void Prefix(VesselEntity.VesselEntityStore store)
        {
            try
            {
                if (store == null) return;
                var ss = store.TryCast<Ship.Store>();
                if (ss == null || ss.turretCalibers != null) return;
                ss.turretCalibers = new Il2CppSystem.Collections.Generic.List<Ship.TurretCaliber.Store>();
                Fixed++;
                if (Fixed <= 5) MelonLogger.Msg($"[DIP GG Fixes] ship store '{ss.vesselName}' had no turretCalibers list; substituted an empty one (#{Fixed})");
            }
            catch (Exception e)
            {
                MelonLogger.Warning($"[DIP GG Fixes] FromBaseStore guard failed: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "GetMinorNationFreePortCapacity")]
    internal static class Patch_MinorPortCapacity
    {
        private static void Postfix(ref float __result)
        {
            var m = DipGgFixesMod.MinorPortCapacityMult;
            if (m != null && __result > 0f) __result *= m.Value;
        }
    }

    [HarmonyPatch(typeof(CampaignController), "AdjustAttitude")]
    internal static class Patch_AdjustAttitude
    {
        // AdjustAttitude calls itself to spread a change to allies/enemies with
        // deltas derived from the (already scaled) outer delta; scale only the
        // outermost call so propagated changes are not multiplied twice.
        [ThreadStatic] private static int depth;
        // TweaksAndFixes 3.21+ re-enters AdjustAttitude from its own postfix (alliance tweaks)
        // with Patch_CampaignController._PassThroughAdjustAttitude set; those calls carry
        // attitude values that were already scaled here, so they must not be scaled again.
        private static System.Reflection.FieldInfo tafPassThrough; private static bool tafLookedUp;
        private static bool TafPassThrough()
        {
            try
            {
                if (!tafLookedUp)
                {
                    tafLookedUp = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (asm.GetName().Name != "TweaksAndFixes") continue;
                        var t = asm.GetType("TweaksAndFixes.Patch_CampaignController");
                        tafPassThrough = t?.GetField("_PassThroughAdjustAttitude", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (tafPassThrough != null) MelonLogger.Msg("[DIP GG Fixes] TweaksAndFixes AdjustAttitude pass-through flag found; re-entrant calls will not be rescaled");
                    }
                }
                return tafPassThrough != null && tafPassThrough.FieldType == typeof(bool) && (bool)tafPassThrough.GetValue(null);
            }
            catch { return false; }
        }

        private static void Prefix(ref float attitudeDelta, bool init, bool force)
        {
            depth++;
            if (depth > 1 || init || force || TafPassThrough()) return;
            var m = attitudeDelta < 0f ? DipGgFixesMod.NegAttitudeMult : DipGgFixesMod.AttitudeMult;
            if (m == null) return;
            attitudeDelta *= m.Value;
        }

        private static void Finalizer()
        {
            if (depth > 0) depth--;
        }
    }

    [HarmonyPatch(typeof(CampaignController), "AdvanceShips")]
    internal static class Patch_AdvanceShips
    {
        private static void Postfix(Player player, bool prewarm)
        {
            if (player == null || prewarm) return;
            ConstructionWatch.Advanced.Add(player.Pointer);
        }
    }

    internal static class ConstructionWatch
    {
        internal static readonly HashSet<IntPtr> Advanced = new HashSet<IntPtr>();
        private static string lastTurn;
        private static bool firstTurnSeen;
        private static readonly Dictionary<IntPtr, float> LastProgress = new Dictionary<IntPtr, float>();

        internal static void CheckOncePerTurn()
        {
            try
            {
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return;
                string turn = cc.CurrentDate.ToString();
                if (turn == lastTurn) return;
                lastTurn = turn;
                // The set covers the turn since the previous check; skip the very first check after loading.
                if (!firstTurnSeen) { firstTurnSeen = true; Advanced.Clear(); return; }

                var majors = cc.CampaignData.PlayersMajor;
                for (int i = 0; i < majors.Count; i++)
                {
                    var p = majors[i];
                    if (p == null || !p.isAi || p.isDisabled) continue;
                    int building = 0; float progress = 0f;
                    var en = p.fleet.GetEnumerator();
                    var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var s = en.Current;
                        if (s != null && s.isBuilding && !s.isBuildingPaused) { building++; progress += s.buildingProgress; }
                    }
                    if (building == 0) continue;
                    string name = p.data != null ? p.data.nameUi : "?";
                    float avg = progress / building;
                    if (Advanced.Contains(p.Pointer))
                    {
                        if (LastProgress.TryGetValue(p.Pointer, out var prev) && Math.Abs(prev - avg) < 0.0001f)
                            MelonLogger.Msg($"[DIP GG Fixes] Construction for {name}: advanced last turn but progress did not change ({building} ship(s), avg {avg:F1}%).");
                        LastProgress[p.Pointer] = avg;
                        continue;
                    }
                    LastProgress[p.Pointer] = avg;
                    if (DipGgFixesMod.FixStalledBuilding != null && DipGgFixesMod.FixStalledBuilding.Value)
                    {
                        cc.AdvanceShips(p, true, false);
                        MelonLogger.Msg($"[DIP GG Fixes] Construction for {name} was not advanced last turn ({building} ship(s), avg {progress / building:F1}%) - advanced now.");
                    }
                    else
                        MelonLogger.Msg($"[DIP GG Fixes] Construction for {name} was not advanced last turn ({building} ship(s), avg {progress / building:F1}%).");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DIP GG Fixes] Construction check failed: " + e.Message);
            }
            finally { Advanced.Clear(); }
        }
    }

    // ---------------------------------------------------------------- national battle doctrines (Mods\battleDoctrines.csv)
    internal class Doctrine
    {
        public string nation = "default"; public float distanceMult = 1f; public float chargeStart = 12000f; public float abortDamage = 0.6f; public float escapeMargin = 1f;
    }
    internal static class Doctrines
    {
        internal static readonly Dictionary<string, Doctrine> ByNation = new Dictionary<string, Doctrine>(StringComparer.OrdinalIgnoreCase);
        internal static Doctrine Default = new Doctrine();
        internal static bool Loaded;

        internal static void Load()
        {
            try
            {
                string path = Path.Combine(MelonUtils.GameDirectory, "Mods", "battleDoctrines.csv");
                if (!File.Exists(path)) { MelonLogger.Msg("[DIP GG Fixes] battleDoctrines.csv not found - global battle AI settings apply to all nations"); return; }
                int n = 0;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("nation,")) continue;
                    var c = line.Split(',');
                    if (c.Length < 5) continue;
                    var d = new Doctrine { nation = c[0].Trim().ToLowerInvariant() };
                    d.distanceMult = F(c[1], 1f); d.chargeStart = F(c[2], 12000f); d.abortDamage = F(c[3], 0.6f); d.escapeMargin = F(c[4], 1f);
                    ByNation[d.nation] = d; n++;
                    if (d.nation == "default") Default = d;
                }
                Loaded = n > 0;
                MelonLogger.Msg($"[DIP GG Fixes] Loaded {n} national battle doctrines from battleDoctrines.csv");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] battleDoctrines.csv: " + e.Message); }
        }
        static float F(string v, float def) { return float.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : def; }

        internal static Doctrine For(Player p)
        {
            if (!Loaded) return null;
            try
            {
                var pd = p != null ? p.data : null; var name = pd != null ? pd.name : null;
                if (name != null && ByNation.TryGetValue(name, out var d)) return d;
            }
            catch { }
            return Default;
        }
    }

    // ---------------------------------------------------------------- battle AI
    internal static class BattleAi
    {
        [ThreadStatic] internal static float CurrentDistanceMult;
        internal static float ChargeStart(Doctrine d) { return d != null ? d.chargeStart : DipGgFixesMod.ChargeStartRange.Value; }
        internal static float AbortDamage(Doctrine d) { return d != null ? d.abortDamage : DipGgFixesMod.ChargeAbortDamage.Value; }
        internal static float EscapeMargin(Doctrine d) { return d != null ? d.escapeMargin : DipGgFixesMod.EscapeSpeedMargin.Value; }

        internal class DivState { public float nextThink; public bool charging; public IntPtr target; public string name; public string reason; public bool fighting; public float nextDetach; }
        static void Why(DivState st, string r) { if (st.reason != r) { st.reason = r; Log($"{st.name}: {r}"); } }
        internal static readonly Dictionary<IntPtr, DivState> States = new Dictionary<IntPtr, DivState>();
        internal static int Errors;
        private static bool announced;

        internal static bool IsLight(Ship s)
        {
            var t = s.shipType; if (t == null) return false;
            var n = t.name; return n == "tb" || n == "dd" || n == "cl";
        }
        internal static bool IsCapital(Ship s)
        {
            var t = s.shipType; if (t == null) return false;
            var n = t.name; return n == "bb" || n == "bc" || n == "ca";
        }
        static int rangeSrcLogged;
        internal static float TorpedoRange(Ship s)
        {
            float best = 0f; string src = null; string err = null;
            // 1) the ship's own weapon range cache (filled by Ship.RecalculateWeaponRanges)
            try
            {
                var cache = s.weaponRangesCache;
                if (cache != null)
                    foreach (var kv in cache)
                    {
                        var d = kv.Key; if (d == null || !d.isTorpedo) continue;
                        if (kv.Value > best) { best = kv.Value; src = "weaponRangesCache"; }
                    }
            }
            catch (Exception e) { err = "cache: " + e.Message; }
            // 2) Part.WeaponRange (static, with ship) / instance
            if (best <= 0f)
            {
                var parts = s.torpedoesAll;
                if (parts != null)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var p = parts[i]; if (p == null) continue;
                        try { var r = Part.WeaponRange(p.data, s, null); if (r > best) { best = r; src = "Part.WeaponRange(static)"; } }
                        catch (Exception e) { err = "static: " + e.Message; }
                        if (best <= 0f) { try { var r = p.WeaponRange(null); if (r > best) { best = r; src = "Part.WeaponRange"; } } catch (Exception e) { err = "inst: " + e.Message; } }
                    }
            }
            // 3) fallback
            if (best <= 0f) { best = DipGgFixesMod.TorpedoRangeFallback.Value; src = "fallback"; }
            if (rangeSrcLogged < 3) { rangeSrcLogged++; Log($"{s.name}: torpedo range {best:0} m from {src}{(err != null ? " (" + err + ")" : "")}"); }
            return best;
        }
        internal static bool HasTorpedoAmmo(Ship s)
        {
            var parts = s.torpedoesAll; if (parts == null || parts.Count == 0) return false;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i]; if (p == null) continue;
                try { if (s.GetTotalAmmo(p.data) > 0) return true; } catch { return true; }
            }
            return false;
        }
        internal static float Dist(Ship a, Ship b)
        {
            var ta = a.transform; var tb = b.transform; if (ta == null || tb == null) return float.MaxValue;
            var d = ta.position - tb.position; d.y = 0f; return d.magnitude;
        }
        internal static float Speed(Ship s)
        {
            try { return s.SpeedMax(false, false, false); } catch { return 0f; }
        }
        internal static float DivSpeed(Division d, Ship leader)
        {
            var ships = d.ships; float min = Speed(leader);
            if (ships == null) return min;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || s.isSinking || s.isDead) continue;
                float v = Speed(s); if (v > 0f && v < min) min = v;
            }
            return min;
        }
        internal static float DivDamage(Division d)
        {
            var ships = d.ships; if (ships == null || ships.Count == 0) return 1f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || s.isSinking || s.isDead) { sum += 1f; n++; continue; }
                try { sum += UnityEngine.Mathf.Clamp01(s.DamageEstimation()); } catch { }
                n++;
            }
            return n == 0 ? 1f : sum / n;
        }

        internal static void Think(Ship leader)
        {
            if (!DipGgFixesMod.LightShipCharge.Value && !DipGgFixesMod.FightWhenCannotEscape.Value && !DipGgFixesMod.DetachSlowShips.Value) return;
            if (!GameManager.IsBattle) return;
            if (!announced) { announced = true; Log("AILogic patch active"); }
            if (leader == null || leader.isSinking || leader.isDead) return;
            if (!leader.isAiControlled || !leader.isDivLeader) return;
            var div = leader.division; if (div == null) return;
            var player = leader.player; if (player == null || player.isMain) return;
            bool light = IsLight(leader);
            var doc = Doctrines.For(player);

            var key = div.Pointer;
            if (!States.TryGetValue(key, out var st))
            {
                st = new DivState { name = leader.name ?? "?" }; States[key] = st;
                Log($"AI division leader {st.name} ({leader.shipType.name}), light={light}, torpedoes={leader.haveTorpedoes}, ships={(div.ships != null ? div.ships.Count : 0)}, doctrine={(doc != null ? doc.nation : "global")}");
            }
            float now = UnityEngine.Time.time;
            if (now < st.nextThink) return;
            st.nextThink = now + UnityEngine.Mathf.Max(1f, DipGgFixesMod.BattleThinkSeconds.Value);

            // --- rule for every AI division: ships that slow the division down leave it and retreat alone
            if (DipGgFixesMod.DetachSlowShips.Value && now >= st.nextDetach && TryDetachSlow(div, leader, st)) { st.nextDetach = now + DipGgFixesMod.DetachCooldownSeconds.Value; return; }

            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (battle == null) { Why(st, "no CurrentBattle"); return; }
            var enemies = battle.ShipsOf(battle.OppositePlayer(player), true);
            if (enemies == null) { Why(st, "no enemy list"); return; }

            // --- rule for every AI division: a retreat that cannot succeed becomes a fight
            bool retreating = div.isRetreating || div.retreat;
            if (retreating || st.fighting)
            {
                if (!DipGgFixesMod.FightWhenCannotEscape.Value) { st.fighting = false; st.charging = false; Why(st, "retreating - left to the game"); return; }
                float own = DivSpeed(div, leader);
                Ship pursuer = null; float pv = 0f, pd = 0f;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == null || e.isSinking || e.isDead || !e.IsVisibleFor(leader)) continue;
                    float d = Dist(leader, e); if (d > DipGgFixesMod.PursuitRange.Value) continue;
                    float v = Speed(e); if (v > pv) { pv = v; pursuer = e; pd = d; }
                }
                bool cannotEscape = pursuer != null && own <= pv * EscapeMargin(doc);
                if (cannotEscape)
                {
                    if (retreating) div.Retreat(false);
                    if (!st.fighting) { st.fighting = true; Log($"{st.name} ({leader.shipType.name}) cannot outrun {pursuer.name} ({pursuer.shipType.name}, {pv:0.0} vs own {own:0.0} speed units, {pd:0} m) - retreat cancelled, fighting"); }
                    if (!light || !leader.haveTorpedoes) { Why(st, "fighting (cannot escape)"); return; } // gun ships: the game's own combat AI takes over
                }
                else
                {
                    if (st.fighting) { st.fighting = false; Log($"{st.name}: pursuer gone or division faster - fight state cleared"); }
                    if (retreating)
                    {
                        st.charging = false;
                        Why(st, pursuer == null ? "retreating, no visible pursuer in range - left to the game"
                                               : $"retreating and faster than {pursuer.name} - left to the game");
                        return;
                    }
                }
            }
            if (!light || !leader.haveTorpedoes || !DipGgFixesMod.LightShipCharge.Value) return;

            // --- light torpedo ships: charge the nearest capital ship
            float dmg = DivDamage(div);
            bool ammo = HasTorpedoAmmo(leader);
            if (st.charging && (dmg > AbortDamage(doc) || !ammo))
            {
                st.charging = false;
                if (!st.fighting) div.Retreat(true);
                Log($"{st.name}: charge aborted ({(ammo ? "damage " + dmg.ToString("0.00") : "no torpedoes")}) - {(st.fighting ? "keeps fighting with guns (cannot escape)" : "retreating")}");
                return;
            }
            if (!ammo) { Why(st, "no torpedo ammo"); return; }
            if (dmg > AbortDamage(doc)) { Why(st, "too damaged for a charge"); return; }

            Ship best = null; float bestD = float.MaxValue; int capitals = 0, visible = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || !IsCapital(e)) continue;
                capitals++;
                if (!e.IsVisibleFor(leader)) continue;
                visible++;
                float d = Dist(leader, e);
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best == null) { st.charging = false; Why(st, "no visible capital target"); return; }

            float torp = TorpedoRange(leader);
            if (torp <= 0f) { Why(st, "torpedo range 0"); return; }
            float launchAt = torp * DipGgFixesMod.ChargeTorpedoMargin.Value;
            if (!st.charging)
            {
                if (bestD > ChargeStart(doc) && !st.fighting) { Why(st, $"nearest capital {best.name} beyond start range ({ChargeStart(doc):0} m)"); return; }
                st.charging = true; st.target = best.Pointer;
                Why(st, "charging");
                Log($"{st.name} ({leader.shipType.name}) charges {best.name} ({best.shipType.name}) at {bestD:0} m, torpedo range {torp:0} m");
            }
            if (bestD > launchAt)
            {
                var tt = best.transform; if (tt != null) div.MoveTo(tt.position);
            }
            // inside launch range: leave movement to the game, the torpedo logic fires on its own
        }

        // Detach the ships that slow the division down into a new division and order it to retreat.
        // If the leader itself is the slow one, the fast ships are detached instead and the
        // leader's (old) division retreats. Returns true when a detach was performed.
        internal static bool TryDetachSlow(Division div, Ship leader, DivState st)
        {
            var ships = div.ships; if (ships == null || ships.Count < 2) return false;
            var alive = new List<Ship>(); float fastest = 0f;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || s.isSinking || s.isDead) continue;
                alive.Add(s); float v = Speed(s); if (v > fastest) fastest = v;
            }
            if (alive.Count < 2 || fastest <= 0f) return false;
            float limit = UnityEngine.Mathf.Min(fastest * DipGgFixesMod.SlowShipSpeedRatio.Value, fastest - 2f);
            var slow = new List<Ship>(); var fast = new List<Ship>();
            foreach (var s in alive) { if (Speed(s) < limit) slow.Add(s); else fast.Add(s); }
            if (slow.Count == 0 || fast.Count == 0) return false;
            bool leaderSlow = slow.Exists(x => x.Pointer == leader.Pointer);
            var detach = leaderSlow ? fast : slow;
            var il = new Il2CppSystem.Collections.Generic.List<Ship>();
            foreach (var s in detach) { if (!s.CanDetach()) return false; il.Add(s); }
            Ship.DetachShips(il);
            var newDiv = detach[0].division;
            if (newDiv == null || newDiv.Pointer == div.Pointer) { Log($"{st.name}: detach of {detach.Count} ship(s) had no effect"); return true; }
            var retreatDiv = leaderSlow ? div : newDiv;
            retreatDiv.Retreat(true);
            string names = string.Join(", ", slow.ConvertAll(x => $"{x.name} {Speed(x):0.0}"));
            Log($"{st.name}: slow ship(s) {names} separated from the division (fastest {fastest:0.0} speed units) - slow group retreats on its own{(leaderSlow ? " (leader is slow: fast ships detached instead)" : "")}");
            return true;
        }

        internal static void Log(string m)
        {
            if (DipGgFixesMod.BattleAiLog != null && DipGgFixesMod.BattleAiLog.Value) MelonLogger.Msg("[DIP GG Fixes][Battle] " + m);
        }
    }

    [HarmonyPatch(typeof(Ship), "AILogic")]
    internal static class Patch_AILogic
    {
        private static void Prefix(Ship __instance)
        {
            BattleAi.CurrentDistanceMult = 1f;
            try
            {
                if (__instance == null || !Doctrines.Loaded) return;
                var p = __instance.player; if (p == null || p.isMain) return;
                var d = Doctrines.For(p); if (d != null) BattleAi.CurrentDistanceMult = d.distanceMult;
            }
            catch { }
        }
        private static void Finalizer() { BattleAi.CurrentDistanceMult = 1f; }
        private static void Postfix(Ship __instance)
        {
            try { BattleAi.Think(__instance); }
            catch (Exception e)
            {
                if (BattleAi.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AILogic postfix: " + e.Message);
            }
        }
    }

    // ai_distance_mod (preferred engagement distance) scaled per nation while the game
    // evaluates an AI ship's range inside Ship.AILogic (RangeCalc).
    [HarmonyPatch(typeof(MonoBehaviourExt), "Param", new Type[] { typeof(string), typeof(float) })]
    internal static class Patch_Param
    {
        private static void Postfix(string __0, ref float __result)
        {
            var m = BattleAi.CurrentDistanceMult;
            if (m == 1f || m <= 0f) return;
            if (__0 == "ai_distance_mod") __result *= m;
        }
    }

    [HarmonyPatch(typeof(Ship), "FindBestEnemy")]
    internal static class Patch_FindBestEnemy
    {
        private static void Postfix(Ship __instance, PartData gunGroup, List<Part> guns, ref Ship __result)
        {
            try
            {
                if (!DipGgFixesMod.FocusFire.Value || __result == null || __instance == null) return;
                if (!__instance.isAiControlled) return;
                var player = __instance.player; if (player == null || player.isMain) return;
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (battle == null) return;
                float range = 0f;
                if (guns != null && guns.Count > 0 && guns[0] != null) range = guns[0].WeaponRange(null);
                if (range <= 0f) return;
                float cur = Damage(__result);
                float minD = DipGgFixesMod.FocusFireMinDamage.Value;
                var enemies = battle.ShipsOf(battle.OppositePlayer(player), true);
                if (enemies == null) return;
                Ship pick = null; float pickDmg = UnityEngine.Mathf.Max(cur, minD);
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == null || e.Pointer == __result.Pointer || e.isSinking || e.isDead) continue;
                    if (!e.IsVisibleFor(__instance)) continue;
                    if (BattleAi.Dist(__instance, e) > range) continue;
                    float d = Damage(e);
                    if (d > pickDmg) { pickDmg = d; pick = e; }
                }
                if (pick != null)
                {
                    if (BattleAi.Errors < 1 && DipGgFixesMod.BattleAiLog.Value && UnityEngine.Time.frameCount % 600 == 0)
                        BattleAi.Log($"{__instance.name} focuses {pick.name} (damage {pickDmg:0.00}) instead of {__result.name} ({cur:0.00})");
                    __result = pick;
                }
            }
            catch (Exception e)
            {
                if (BattleAi.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] FindBestEnemy postfix: " + e.Message);
            }
        }
        private static float Damage(Ship s)
        {
            try { return UnityEngine.Mathf.Clamp01(s.DamageEstimation()); } catch { return 0f; }
        }
    }
}

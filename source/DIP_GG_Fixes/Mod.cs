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
//
// 1.11.0 (battle, all ships unless noted):
//   - Charge start depends on torpedo range and speed advantage
//     (torpedo range x margin + speed advantage x ChargeApproachSeconds +
//     ChargeBaseExtra, capped by the national chargeStartRange).
//   - Capital AI ships (bb/bc/ca) charge too when they carry torpedoes and are
//     faster than the target, or close the distance when their guns are
//     outranged by the target or cannot penetrate its belt at the current
//     range ("only option" rule).
//   - Player divisions set as Scout (tb/dd/cl with torpedoes) run the same
//     torpedo charge as the AI (PlayerScoutCharge).
//   - Player ships hold torpedoes while a friendly ship is in the lane towards
//     the torpedo target (TorpedoFriendlyLane).
//   - Target selection for all ships (Ship.AimAndShoot postfix, respects
//     manual targets): most damaged enemy in range; bb/bc main guns prefer the
//     heaviest enemy calibre; secondary guns prefer the nearest torpedo threat.
//     Replaces the 1.9 FindBestEnemy postfix, which never ran because the game
//     inlines FindBestEnemy into its callers.
//   - Crew training levels rescaled non-linearly (GameData.crewTrainingLevels):
//     elite/veteran effects up to +100%, green crews down to -50%.
//   - Surrender threshold depends on crew training (green 70% crew, elite 40%).
//   - Panic: badly damaged ships with inexperienced crews break off and retreat
//     (AI and player).
//   - Surrendered ships are captured by the winner when it still has ships in
//     the battle (BattleManager.DamageShip prefix): transferred, sent to repair,
//     crew training reset to green.
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

[assembly: MelonInfo(typeof(DipGgFixes.DipGgFixesMod), "DIP GG Fixes", "1.11.6", "DIP GG Extended")]
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
        // 1.11
        internal static MelonPreferences_Entry<float> ChargeApproachSeconds;
        internal static MelonPreferences_Entry<float> ChargeBaseExtra;
        internal static MelonPreferences_Entry<bool> CapitalCharge;
        internal static MelonPreferences_Entry<float> CapitalSpeedMargin;
        internal static MelonPreferences_Entry<bool> CapitalCloseIn;
        internal static MelonPreferences_Entry<float> OutrangedFactor;
        internal static MelonPreferences_Entry<float> PenetrationFactor;
        internal static MelonPreferences_Entry<float> CloseInStopRange;
        internal static MelonPreferences_Entry<bool> PlayerScoutCharge;
        internal static MelonPreferences_Entry<bool> TorpedoFriendlyLane;
        internal static MelonPreferences_Entry<float> LaneHalfWidth;
        internal static MelonPreferences_Entry<bool> SmartTargeting;
        internal static MelonPreferences_Entry<bool> SmartTargetingAi;
        internal static MelonPreferences_Entry<float> TorpedoThreatRangeMult;
        internal static MelonPreferences_Entry<bool> CrewLevelsRescale;
        internal static MelonPreferences_Entry<float> CrewPlusAmp;
        internal static MelonPreferences_Entry<float> CrewMinusAmp;
        internal static MelonPreferences_Entry<float> CrewCurvePower;
        internal static MelonPreferences_Entry<bool> SurrenderByTraining;
        internal static MelonPreferences_Entry<float> SurrenderGreen;
        internal static MelonPreferences_Entry<float> SurrenderElite;
        internal static MelonPreferences_Entry<bool> Panic;
        internal static MelonPreferences_Entry<float> PanicMaxRank;
        internal static MelonPreferences_Entry<float> PanicDamage;
        internal static MelonPreferences_Entry<float> PanicCrewLoss;
        internal static MelonPreferences_Entry<float> PanicChance;
        internal static MelonPreferences_Entry<bool> CaptureSurrendered;
        internal static MelonPreferences_Entry<float> CaptureDamage;
        internal static MelonPreferences_Entry<bool> MinorFleetToConqueror;

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
            ChargeApproachSeconds = cat.CreateEntry("ChargeApproachSeconds", 240f, null,
                "Charge start distance = torpedo range x margin + (own speed - target speed, m/s) x this many seconds + ChargeBaseExtra; the national chargeStartRange is the cap");
            ChargeBaseExtra = cat.CreateEntry("ChargeBaseExtra", 2500f, null,
                "Extra meters added to the charge start distance regardless of speed");
            CapitalCharge = cat.CreateEntry("CapitalTorpedoCharge", true, null,
                "AI bb/bc/ca divisions with torpedoes charge an enemy capital ship when they are faster than it");
            CapitalSpeedMargin = cat.CreateEntry("CapitalChargeSpeedMargin", 1.05f, null,
                "Capital ship counts as faster when own speed > target speed x this");
            CapitalCloseIn = cat.CreateEntry("CapitalCloseInWhenOutgunned", true, null,
                "AI bb/bc/ca divisions close the distance when their guns are outranged by the target or cannot penetrate its belt at the current range");
            OutrangedFactor = cat.CreateEntry("OutrangedFactor", 1.1f, null,
                "Outranged when the target's gun range > own gun range x this");
            PenetrationFactor = cat.CreateEntry("PenetrationFactor", 1.0f, null,
                "Cannot penetrate when own main-gun penetration at the current range < target belt armour x this");
            CloseInStopRange = cat.CreateEntry("CloseInStopRangeFactor", 0.7f, null,
                "Stop closing in at own gun range x this (or when penetration becomes possible)");
            PlayerScoutCharge = cat.CreateEntry("PlayerScoutCharge", true, null,
                "Player tb/dd/cl divisions set as Scout run the same torpedo charge as the AI (retreat when out of torpedoes or badly damaged)");
            TorpedoFriendlyLane = cat.CreateEntry("TorpedoFriendlyLane", true, null,
                "Player ships hold their torpedoes while a friendly ship is in the lane towards the torpedo target");
            LaneHalfWidth = cat.CreateEntry("TorpedoLaneHalfWidth", 350f, null,
                "Half width (m) of the lane checked for friendly ships");
            SmartTargeting = cat.CreateEntry("SmartTargeting", true, null,
                "Player ships without a manual target: guns pick the most damaged enemy in range; bb/bc main guns prefer the heaviest enemy calibre; secondary guns prefer the nearest torpedo threat");
            SmartTargetingAi = cat.CreateEntry("SmartTargetingAi", true, null,
                "Apply the same target selection to AI ships (replaces FocusFire)");
            TorpedoThreatRangeMult = cat.CreateEntry("TorpedoThreatRangeMult", 1.5f, null,
                "An enemy with torpedoes counts as a torpedo threat when closer than its torpedo range x this");
            CrewLevelsRescale = cat.CreateEntry("CrewLevelsRescale", true, null,
                "Rescale crew training levels non-linearly (stronger effect of experience)");
            CrewPlusAmp = cat.CreateEntry("CrewLevelsPlusAmplitude", 1.0f, null,
                "Effect of the best training level relative to the baseline level (1.0 = +100%)");
            CrewMinusAmp = cat.CreateEntry("CrewLevelsMinusAmplitude", 0.5f, null,
                "Effect of the worst training level relative to the baseline level (0.5 = -50%)");
            CrewCurvePower = cat.CreateEntry("CrewLevelsCurvePower", 1.6f, null,
                "Curve exponent: >1 = levels near the baseline change little, extreme levels change a lot");
            SurrenderByTraining = cat.CreateEntry("SurrenderByTraining", true, null,
                "Surrender crew threshold depends on crew training (interpolated between the two values below)");
            SurrenderGreen = cat.CreateEntry("SurrenderCrewGreen", 0.70f, null,
                "Worst-trained crews surrender when crew falls below this ratio (vanilla: 0.55 for everybody)");
            SurrenderElite = cat.CreateEntry("SurrenderCrewElite", 0.40f, null,
                "Best-trained crews surrender when crew falls below this ratio");
            Panic = cat.CreateEntry("CrewPanic", true, null,
                "Badly damaged ships with inexperienced crews may break off and retreat (AI and player ships)");
            PanicMaxRank = cat.CreateEntry("PanicMaxTrainingRank", 0.4f, null,
                "Only crews with training rank below this (0 = worst level, 1 = best level) can panic");
            PanicDamage = cat.CreateEntry("PanicDamage", 0.5f, null,
                "Panic check starts above this damage estimate (0-1)");
            PanicCrewLoss = cat.CreateEntry("PanicCrewLoss", 0.25f, null,
                "Panic check also starts when the ship lost this share of the crew it had at the start of the battle (0-1)");
            PanicChance = cat.CreateEntry("PanicChancePerCheck", 0.2f, null,
                "Chance per 3-second check for the worst-trained crew at the far end of the scale (scales with the excess over the thresholds and with rank)");
            CaptureSurrendered = cat.CreateEntry("CaptureSurrenderedShips", true, null,
                "Ships that surrendered in a campaign battle are transferred to the winner if it still has ships in the battle; they go to repair with a green crew");
            CaptureDamage = cat.CreateEntry("CaptureDamage", 0.6f, null,
                "Structural damage (0-1) applied to a captured ship before it enters repair");
            MinorFleetToConqueror = cat.CreateEntry("MinorFleetToConqueror", true, null,
                "When a nation loses a province and has no other port to flee to, or is dissolved, its ships in the lost ports pass to the new controller of the port instead of being scrapped (1.11.4, all nations since 1.11.5)");
            Doctrines.Load();
            LoggerInstance.Msg($"1.11: ChargeApproachSeconds={ChargeApproachSeconds.Value} base {ChargeBaseExtra.Value} m, CapitalTorpedoCharge={CapitalCharge.Value}, CapitalCloseInWhenOutgunned={CapitalCloseIn.Value} (outranged x{OutrangedFactor.Value}, pen x{PenetrationFactor.Value}), PlayerScoutCharge={PlayerScoutCharge.Value}, TorpedoFriendlyLane={TorpedoFriendlyLane.Value} ({LaneHalfWidth.Value} m), SmartTargeting={SmartTargeting.Value}/AI {SmartTargetingAi.Value}, CrewLevelsRescale={CrewLevelsRescale.Value} (+{CrewPlusAmp.Value}/-{CrewMinusAmp.Value}, p={CrewCurvePower.Value}), SurrenderByTraining={SurrenderByTraining.Value} ({SurrenderGreen.Value}-{SurrenderElite.Value}), CrewPanic={Panic.Value} (rank<{PanicMaxRank.Value}, dmg>{PanicDamage.Value}, crew loss>{PanicCrewLoss.Value}, p={PanicChance.Value}), CaptureSurrenderedShips={CaptureSurrendered.Value} (damage {CaptureDamage.Value}), MinorFleetToConqueror={MinorFleetToConqueror.Value}");
            LoggerInstance.Msg($"Battle AI: LightShipTorpedoCharge={LightShipCharge.Value} (start {ChargeStartRange.Value} m, margin {ChargeTorpedoMargin.Value}, abort {ChargeAbortDamage.Value}), FightWhenCannotEscape={FightWhenCannotEscape.Value} (pursuit {PursuitRange.Value} m, margin {EscapeSpeedMargin.Value}), DetachSlowShips={DetachSlowShips.Value} (ratio {SlowShipSpeedRatio.Value}, cooldown {DetachCooldownSeconds.Value} s), FocusFire={FocusFire.Value} (min {FocusFireMinDamage.Value})");
            LoggerInstance.Msg($"Diplomacy attitude multiplier: positive={AttitudeMult.Value}, negative={NegAttitudeMult.Value}, FixStalledConstruction={FixStalledBuilding.Value}, EconomicCollapseGraceMonths={CollapseGraceMonths.Value}");
            LoggerInstance.Msg($"Balance-of-power events={BalanceEvents.Value} (ratio {BalanceRatio.Value}, chance {BalanceChance.Value}, {BalanceMonths.Value} months)");
            LoggerInstance.Msg($"Loaded 1.11.6. CheckMinorNationThreat guard on. AutoShareDesigns={AutoShare.Value}, ShareToAllNations={AllNations.Value}, "
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
                    // 1.11.4: Ui.ReportImportant throws inside the game when the ship is null, so hand it one of the nation's ships
                    try { var any = FirstShip(p); if (G.ui != null && any != null) G.ui.ReportImportant(text, any); } catch { }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DIP GG Fixes] Balance-of-power check failed: " + e.Message);
            }
        }

        private static Ship FirstShip(Player p)
        {
            try
            {
                if (p.fleet == null) return null;
                var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (un.MoveNext()) { var s = en.Current; if (s != null) return s; }
            }
            catch { }
            return null;
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

        internal class DivState { public float nextThink; public bool charging; public bool closing; public IntPtr target; public string name; public string reason; public bool fighting; public float nextDetach; }
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

        // ---- 1.11 helpers -------------------------------------------------------------
        internal static float GunRange(Ship s)
        {
            float best = 0f;
            try
            {
                var cache = s.weaponRangesCache;
                if (cache != null)
                    foreach (var kv in cache)
                    {
                        var d = kv.Key; if (d == null || !d.isGun) continue;
                        if (kv.Value > best) best = kv.Value;
                    }
            }
            catch { }
            return best;
        }
        internal static float MainCaliber(Ship s)
        {
            try { var g = s.mainGunsGroup; return g != null ? g.caliber : 0f; } catch { return 0f; }
        }
        internal static float BeltArmor(Ship s)
        {
            float best = 0f;
            try
            {
                var a = s.armor; if (a == null) return 0f;
                float v;
                if (a.TryGetValue(Ship.A.Belt, out v) && v > best) best = v;
                if (a.TryGetValue(Ship.A.BeltBow, out v) && v > best) best = v;
                if (a.TryGetValue(Ship.A.BeltStern, out v) && v > best) best = v;
            }
            catch { }
            return best;
        }
        static int penLogged;
        // true when the shooter's main guns are estimated unable to penetrate the target's belt at the given distance
        internal static bool CannotPenetrate(Ship shooter, Ship target, float dist, out float pen, out float belt)
        {
            pen = 0f; belt = 0f;
            float beltRaw = BeltArmor(target);
            try
            {
                var g = shooter.mainGunsGroup; if (g == null) return false;
                var noShell = new Il2CppSystem.Nullable<Ship.ShellType>(); var noVec = new Il2CppSystem.Nullable<UnityEngine.Vector3>();
                // 1.11.1 log: the bool=false variant GREW with distance (plunging/deck penetration); belt penetration is the bool=true variant.
                // Units (1.11.2 log): penetration comes back in cm, armour in mm (UI 29.1 vs 330 for an 11" gun at 10.7 km) - hence x10.
                float penBelt = Ship.GetPenetration(g, shooter, dist, true, noShell, noVec);
                pen = penBelt * 10f;
                try { belt = target.GetEffectiveArmor(Ship.A.Belt, null, g, shooter, false); } catch { belt = 0f; }
                if (penLogged < 8)
                {
                    penLogged++;
                    float penDeck = 0f; try { penDeck = Ship.GetPenetration(g, shooter, dist, false, noShell, noVec); } catch { }
                    Log($"penetration check: {shooter.name} ({MainCaliber(shooter):0} mm) vs {target.name} at {dist:0} m: belt pen {penBelt:0.0} cm (deck variant {penDeck:0.0}) vs effective belt {belt:0.0} mm (armour table {beltRaw:0.0})");
                }
            }
            catch (Exception e) { if (penLogged++ < 3) Log("GetPenetration failed: " + e.Message); return false; }
            if (pen <= 0f || belt <= 0f) return false; // units unknown or no data - do not claim "cannot penetrate"
            return pen < belt * DipGgFixesMod.PenetrationFactor.Value;
        }
        // distance at which a torpedo charge starts: torpedo range x margin + speed advantage x approach time + base, capped by doctrine
        internal static float ChargeStartFor(Ship leader, Ship target, Doctrine doc, float torp)
        {
            float adv = UnityEngine.Mathf.Max(0f, Speed(leader) - Speed(target));
            float d = torp * DipGgFixesMod.ChargeTorpedoMargin.Value + adv * DipGgFixesMod.ChargeApproachSeconds.Value + DipGgFixesMod.ChargeBaseExtra.Value;
            return UnityEngine.Mathf.Min(d, ChargeStart(doc));
        }
        internal static Ship NearestVisibleCapital(Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, out float bestD)
        {
            Ship best = null; bestD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered || !IsCapital(e)) continue;
                if (!e.IsVisibleFor(leader)) continue;
                float d = Dist(leader, e);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // Think for AI division leaders (from Ship.AILogic) and for player division leaders (from the per-ship tick).
        internal static void Think(Ship leader) { Think(leader, false); }
        internal static void Think(Ship leader, bool playerSide)
        {
            if (!GameManager.IsBattle) return;
            if (!announced) { announced = true; Log("AILogic patch active"); }
            if (leader == null || leader.isSinking || leader.isDead || leader.IsSurrendered) return;
            if (!leader.isDivLeader) return;
            var div = leader.division; if (div == null) return;
            var player = leader.player; if (player == null) return;
            if (playerSide != player.isMain) return;
            if (!playerSide && !leader.isAiControlled) return;
            bool light = IsLight(leader);
            bool capital = IsCapital(leader);
            var doc = Doctrines.For(player);

            var key = div.Pointer;
            if (!States.TryGetValue(key, out var st))
            {
                st = new DivState { name = leader.name ?? "?" }; States[key] = st;
                Log($"{(playerSide ? "Player" : "AI")} division leader {st.name} ({leader.shipType.name}), light={light}, torpedoes={leader.haveTorpedoes}, ships={(div.ships != null ? div.ships.Count : 0)}, doctrine={(doc != null ? doc.nation : "global")}");
            }
            float now = UnityEngine.Time.time;
            if (now < st.nextThink) return;
            st.nextThink = now + UnityEngine.Mathf.Max(1f, DipGgFixesMod.BattleThinkSeconds.Value);

            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (battle == null) { Why(st, "no CurrentBattle"); return; }
            var enemies = battle.ShipsOf(battle.OppositePlayer(player), true);
            if (enemies == null) { Why(st, "no enemy list"); return; }

            if (playerSide)
            {
                // player: only the scout torpedo charge (manual orders stay in force otherwise)
                if (!DipGgFixesMod.PlayerScoutCharge.Value || !light || !leader.haveTorpedoes) return;
                bool scout = false; try { scout = div.IsScout(); } catch { }
                if (!st.charging && !scout) { Why(st, "not a scout division"); return; }
                TorpedoCharge(leader, div, st, doc, enemies, true);
                return;
            }

            // --- rule for every AI division: ships that slow the division down leave it and retreat alone
            if (DipGgFixesMod.DetachSlowShips.Value && now >= st.nextDetach && TryDetachSlow(div, leader, st)) { st.nextDetach = now + DipGgFixesMod.DetachCooldownSeconds.Value; return; }

            // --- rule for every AI division: a retreat that cannot succeed becomes a fight
            bool retreating = div.isRetreating || div.retreat;
            if (retreating || st.fighting)
            {
                if (!DipGgFixesMod.FightWhenCannotEscape.Value) { st.fighting = false; st.charging = false; st.closing = false; Why(st, "retreating - left to the game"); return; }
                float own = DivSpeed(div, leader);
                Ship pursuer = null; float pv = 0f, pd = 0f;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == null || e.isSinking || e.isDead || e.IsSurrendered || !e.IsVisibleFor(leader)) continue;
                    float d = Dist(leader, e); if (d > DipGgFixesMod.PursuitRange.Value) continue;
                    float v = Speed(e); if (v > pv) { pv = v; pursuer = e; pd = d; }
                }
                bool cannotEscape = pursuer != null && own <= pv * EscapeMargin(doc);
                if (cannotEscape)
                {
                    if (retreating) div.Retreat(false);
                    if (!st.fighting) { st.fighting = true; Log($"{st.name} ({leader.shipType.name}) cannot outrun {pursuer.name} ({pursuer.shipType.name}, {pv:0.0} vs own {own:0.0} speed units, {pd:0} m) - retreat cancelled, fighting"); }
                    if (!leader.haveTorpedoes) { Why(st, "fighting (cannot escape)"); return; } // gun ships: the game's own combat AI takes over
                }
                else
                {
                    if (st.fighting) { st.fighting = false; Log($"{st.name}: pursuer gone or division faster - fight state cleared"); }
                    if (retreating)
                    {
                        st.charging = false; st.closing = false;
                        Why(st, pursuer == null ? "retreating, no visible pursuer in range - left to the game"
                                               : $"retreating and faster than {pursuer.name} - left to the game");
                        return;
                    }
                }
            }

            if (light)
            {
                if (!leader.haveTorpedoes || !DipGgFixesMod.LightShipCharge.Value) return;
                TorpedoCharge(leader, div, st, doc, enemies, false);
                return;
            }
            if (capital) CapitalLogic(leader, div, st, doc, enemies);
        }

        // Torpedo charge of a light (or capital) division against the nearest visible enemy capital ship.
        internal static void TorpedoCharge(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, bool playerSide)
        {
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

            float bestD;
            var best = NearestVisibleCapital(leader, enemies, out bestD);
            if (best == null) { st.charging = false; Why(st, "no visible capital target"); return; }

            float torp = TorpedoRange(leader);
            if (torp <= 0f) { Why(st, "torpedo range 0"); return; }
            float launchAt = torp * DipGgFixesMod.ChargeTorpedoMargin.Value;
            if (!st.charging)
            {
                float start = ChargeStartFor(leader, best, doc, torp);
                if (bestD > start && !st.fighting)
                {
                    if (st.reason != "waiting") Log($"{st.name}: nearest capital {best.name} at {bestD:0} m beyond start distance ({start:0} m: torpedoes {torp:0} m, speed {Speed(leader):0.0} vs {Speed(best):0.0})");
                    Why(st, "waiting"); return;
                }
                st.charging = true; st.target = best.Pointer;
                Why(st, "charging");
                Log($"{(playerSide ? "[player scout] " : "")}{st.name} ({leader.shipType.name}) charges {best.name} ({best.shipType.name}) at {bestD:0} m, torpedo range {torp:0} m, start distance {start:0} m");
            }
            if (bestD > launchAt)
            {
                var tt = best.transform; if (tt != null) div.MoveTo(tt.position);
            }
            // inside launch range: leave movement to the game, the torpedo logic fires on its own
        }

        // Capital AI divisions: torpedo charge when faster and armed with torpedoes; otherwise close in
        // when outranged or unable to penetrate the nearest enemy capital ship ("only option").
        internal static void CapitalLogic(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            float bestD;
            var best = NearestVisibleCapital(leader, enemies, out bestD);
            if (best == null) { st.charging = false; st.closing = false; Why(st, "no visible capital target"); return; }
            float own = Speed(leader), tv = Speed(best);
            if (DipGgFixesMod.CapitalCharge.Value && leader.haveTorpedoes && HasTorpedoAmmo(leader) && own > tv * DipGgFixesMod.CapitalSpeedMargin.Value)
            {
                TorpedoCharge(leader, div, st, doc, enemies, false);
                return;
            }
            if (st.charging) { st.charging = false; Log($"{st.name}: capital charge ended (not faster or no torpedoes)"); }
            if (!DipGgFixesMod.CapitalCloseIn.Value) return;

            float dmg = DivDamage(div);
            if (dmg > AbortDamage(doc)) { if (st.closing) { st.closing = false; Log($"{st.name}: close-in aborted (damage {dmg:0.00})"); } Why(st, "too damaged to close in"); return; }
            float ownR = GunRange(leader), tgtR = GunRange(best);
            bool outranged = ownR > 0f && tgtR > ownR * DipGgFixesMod.OutrangedFactor.Value;
            float pen, belt;
            bool noPen = CannotPenetrate(leader, best, bestD, out pen, out belt);
            float stopAt = ownR > 0f ? ownR * DipGgFixesMod.CloseInStopRange.Value : 0f;
            if ((outranged || noPen) && bestD > stopAt)
            {
                if (!st.closing)
                {
                    st.closing = true;
                    Log($"{st.name} ({leader.shipType.name}) closes in on {best.name} ({best.shipType.name}) at {bestD:0} m: {(outranged ? $"outranged ({ownR:0} vs {tgtR:0} m)" : "")}{(outranged && noPen ? ", " : "")}{(noPen ? $"cannot penetrate (pen {pen:0.0} vs belt {belt:0.0})" : "")}");
                }
                Why(st, "closing in");
                var tt = best.transform; if (tt != null) div.MoveTo(tt.position);
                return;
            }
            if (st.closing) { st.closing = false; Log($"{st.name}: close-in ended at {bestD:0} m ({(noPen ? "still cannot penetrate" : "penetration ok")}, {(outranged ? "still outranged" : "range ok")}) - left to the game"); }
            Why(st, "guns adequate - left to the game");
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

    // =====================================================================================
    // 1.11: per-ship battle tick (Ship.AimAndShoot runs for every ship, player and AI)
    // =====================================================================================
    internal static class ShipTick
    {
        internal class St { public float nextTarget; public float nextLane; public float nextPanic; public bool laneBlocked; public float laneHoldUntil; public Ship.ShootMode savedTorpedoMode; public bool panicked; public float startCrew; public Dictionary<IntPtr, IntPtr> ours = new Dictionary<IntPtr, IntPtr>(); }
        internal static readonly Dictionary<IntPtr, St> States = new Dictionary<IntPtr, St>();
        internal static int Errors; static bool announced;
        internal static int targetChanges, laneHolds, panics;

        internal static St Get(Ship s)
        {
            if (!States.TryGetValue(s.Pointer, out var st)) { st = new St(); States[s.Pointer] = st; }
            return st;
        }

        // called before Ship.AimAndShoot
        internal static void Pre(Ship s)
        {
            if (!GameManager.IsBattle || s == null || s.isSinking || s.isDead) return;
            if (!announced) { announced = true; BattleAi.Log("per-ship tick active (AimAndShoot)"); }
            var player = s.player; if (player == null) return;
            var st = Get(s);
            float now = UnityEngine.Time.time;
            bool isPlayer = player.isMain && !s.isAiControlled;
            if (isPlayer && DipGgFixesMod.TorpedoFriendlyLane.Value && s.haveTorpedoes && now >= st.nextLane) { st.nextLane = now + 0.5f; TorpedoLane(s, st, now); }
            if (DipGgFixesMod.Panic.Value && now >= st.nextPanic) { st.nextPanic = now + 3f; PanicCheck(s, st); }
            if (isPlayer && s.isDivLeader && DipGgFixesMod.PlayerScoutCharge.Value) BattleAi.Think(s, true);
        }
        // called after Ship.AimAndShoot
        internal static void Post(Ship s)
        {
            if (!GameManager.IsBattle || s == null || s.isSinking || s.isDead) return;
            var player = s.player; if (player == null) return;
            bool isPlayer = player.isMain && !s.isAiControlled;
            if (isPlayer ? !DipGgFixesMod.SmartTargeting.Value : !DipGgFixesMod.SmartTargetingAi.Value) return;
            var st = Get(s);
            float now = UnityEngine.Time.time;
            if (now < st.nextTarget) return;
            st.nextTarget = now + 2f;
            SmartTargets(s, player, st);
        }

        // ---- torpedo lane: hold torpedoes while a friendly ship is between us and the torpedo target
        static void TorpedoLane(Ship s, St st, float now)
        {
            var mode = s.torpedoMode;
            if (st.laneBlocked)
            {
                if (mode != Ship.ShootMode.Off) { st.laneBlocked = false; return; } // player changed the mode meanwhile - leave it
                if (now < st.laneHoldUntil) return;
                if (!LaneHasFriend(s)) { s.torpedoMode = st.savedTorpedoMode; st.laneBlocked = false; BattleAi.Log($"{s.name}: torpedo lane clear - torpedoes released ({st.savedTorpedoMode})"); }
                else st.laneHoldUntil = now + 2f;
                return;
            }
            if (mode == Ship.ShootMode.Off || mode == Ship.ShootMode.Invalid) return;
            if (LaneHasFriend(s))
            {
                st.savedTorpedoMode = mode; st.laneBlocked = true; st.laneHoldUntil = now + 2f; s.torpedoMode = Ship.ShootMode.Off; laneHolds++;
                BattleAi.Log($"{s.name}: friendly ship in the torpedo lane - torpedoes held");
            }
        }
        static Ship TorpedoTarget(Ship s)
        {
            try
            {
                var en = s.enemies;
                if (en != null)
                    foreach (var kv in en) { var pd = kv.Key; if (pd == null || !pd.isTorpedo) continue; var a = kv.Value; if (a != null && a.target != null) return a.target; }
                var en2 = s.enemiesLeftSide;
                if (en2 != null)
                    foreach (var kv in en2) { var pd = kv.Key; if (pd == null || !pd.isTorpedo) continue; var a = kv.Value; if (a != null && a.target != null) return a.target; }
            }
            catch { }
            return null;
        }
        static bool LaneHasFriend(Ship s)
        {
            var target = TorpedoTarget(s); if (target == null || target.isDead || target.isSinking) return false;
            var ts = s.transform; var tt = target.transform; if (ts == null || tt == null) return false;
            var p0 = ts.position; var p1 = tt.position; p0.y = 0f; p1.y = 0f;
            var dir = p1 - p0; float len = dir.magnitude; if (len < 1f) return false;
            float torp = BattleAi.TorpedoRange(s); if (torp > 0f && len > torp) len = torp;
            dir /= dir.magnitude;
            float half = DipGgFixesMod.LaneHalfWidth.Value;
            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return false;
            var friends = battle.ShipsOf(s.player, true); if (friends == null) return false;
            for (int i = 0; i < friends.Count; i++)
            {
                var f = friends[i]; if (f == null || f.Pointer == s.Pointer || f.isDead || f.isSinking) continue;
                var tf = f.transform; if (tf == null) continue;
                var p = tf.position; p.y = 0f; var v = p - p0;
                float t = UnityEngine.Vector3.Dot(v, dir);
                if (t < 50f || t > len) continue;
                float perp = (v - dir * t).magnitude;
                if (perp < half) return true;
            }
            return false;
        }

        // ---- target selection (respects manual targets)
        static int tgtLogged;
        // The game re-selects targets for non-manual aims every few hundred ms (1.11.0 test: 1200 overrides
        // in 90 s, each resetting aiming progress). So the mod sets its pick as a "manual" target and
        // remembers it; a manual target the mod did not set (the player's order) is never touched.
        static void SmartTargets(Ship s, Player player, St st)
        {
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                var enemies = battle.ShipsOf(battle.OppositePlayer(player), true); if (enemies == null || enemies.Count == 0) return;
                var cache = s.weaponRangesCache;
                var main = s.mainGunsGroup;
                bool heavy = false; try { var tn = s.shipType != null ? s.shipType.name : null; heavy = tn == "bb" || tn == "bc"; } catch { }
                Apply(s, st, s.enemies, enemies, cache, main, heavy);
                Apply(s, st, s.enemiesLeftSide, enemies, cache, main, heavy);
            }
            catch (Exception e) { if (Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] SmartTargets: " + e.Message); }
        }
        static void Apply(Ship s, St st, Il2CppSystem.Collections.Generic.Dictionary<PartData, Ship.Aim> dict, Il2CppSystem.Collections.Generic.List<Ship> enemies, Il2CppSystem.Collections.Generic.Dictionary<PartData, float> cache, PartData main, bool heavy)
        {
            if (dict == null) return;
            var keys = new List<PartData>();
            foreach (var kv in dict) { if (kv.Key != null && kv.Value != null) keys.Add(kv.Key); }
            foreach (var pd in keys)
            {
                if (!pd.isGun) continue;
                var aim = dict[pd]; if (aim == null) continue;
                var cur = aim.target;
                IntPtr mine; bool haveMine = st.ours.TryGetValue(pd.Pointer, out mine);
                if (aim.manual)
                {
                    // a manual target that is not ours = the player's order: hands off
                    if (!haveMine || cur == null || cur.Pointer != mine) { if (haveMine) st.ours.Remove(pd.Pointer); continue; }
                }
                else if (haveMine) st.ours.Remove(pd.Pointer); // the game or the player cleared our lock
                float range = 0f; if (cache != null) cache.TryGetValue(pd, out range);
                if (range <= 0f) { Release(aim, st, pd); continue; }
                bool isMain = main != null && pd.Pointer == main.Pointer;
                var pick = isMain ? PickMain(s, enemies, range, heavy) : PickSecondary(s, enemies, range);
                if (pick == null) { Release(aim, st, pd); continue; }
                if (cur != null && cur.Pointer == pick.Pointer)
                {
                    // same target: only (re)assert the lock, never touch the aiming progress
                    if (!aim.manual) aim.manual = true;
                    st.ours[pd.Pointer] = pick.Pointer; continue;
                }
                aim.target = pick; aim.manual = true; aim.progress = 0f; st.ours[pd.Pointer] = pick.Pointer; targetChanges++;
                if (tgtLogged < 40 || targetChanges % 200 == 0) { tgtLogged++; BattleAi.Log($"{s.name} {(isMain ? "main" : "secondary")} guns ({pd.caliber:0} mm): target {pick.name} ({pick.shipType.name}, dmg {Dmg(pick):0.00}) instead of {(cur != null ? cur.name : "none")} [{targetChanges} changes]"); }
            }
        }
        // give the group back to the game's own targeting
        static void Release(Ship.Aim aim, St st, PartData pd)
        {
            if (st.ours.ContainsKey(pd.Pointer)) { st.ours.Remove(pd.Pointer); if (aim.manual) aim.manual = false; }
        }
        internal static float Dmg(Ship e) { try { return UnityEngine.Mathf.Clamp01(e.DamageEstimation()); } catch { return 0f; } }
        static bool Usable(Ship s, Ship e) { return e != null && !e.isDead && !e.isSinking && !e.IsSurrendered && e.IsVisibleFor(s); }
        // main guns: most damaged enemy in range; bb/bc: among the enemies with the heaviest calibre (within 25 mm of the max)
        static Ship PickMain(Ship s, Il2CppSystem.Collections.Generic.List<Ship> enemies, float range, bool heavy)
        {
            var inRange = new List<Ship>();
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (Usable(s, e) && BattleAi.Dist(s, e) <= range) inRange.Add(e); }
            if (inRange.Count == 0) return null;
            if (heavy)
            {
                float maxCal = 0f; foreach (var e in inRange) { float c = BattleAi.MainCaliber(e); if (c > maxCal) maxCal = c; }
                if (maxCal > 0f) inRange.RemoveAll(e => BattleAi.MainCaliber(e) < maxCal - 25f);
            }
            Ship best = null; float bestScore = float.MinValue;
            foreach (var e in inRange)
            {
                float score = Dmg(e) * 10f - BattleAi.Dist(s, e) / range; // damage first, then proximity
                if (score > bestScore) { bestScore = score; best = e; }
            }
            return best;
        }
        // secondary guns: nearest torpedo threat (enemy with torpedoes inside its torpedo range x mult), else most damaged in range
        static Ship PickSecondary(Ship s, Il2CppSystem.Collections.Generic.List<Ship> enemies, float range)
        {
            Ship threat = null; float threatD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Usable(s, e) || !e.haveTorpedoes) continue;
                float d = BattleAi.Dist(s, e); if (d > range) continue;
                float tr = BattleAi.TorpedoRange(e) * DipGgFixesMod.TorpedoThreatRangeMult.Value;
                if (d <= tr && d < threatD) { threatD = d; threat = e; }
            }
            if (threat != null) return threat;
            return PickMain(s, enemies, range, false);
        }

        // ---- panic: inexperienced crews on badly damaged ships break off
        static void PanicCheck(Ship s, St st)
        {
            if (st.panicked) return;
            try
            {
                if (s.IsSurrendered) return;
                float crew = 1f; try { crew = s.CrewPercents; } catch { }
                if (st.startCrew <= 0f) st.startCrew = crew; // many ships start a battle below 100% crew: measure the loss, not the level
                var d0 = s.division; if (d0 == null || d0.isRetreating || d0.retreat) return;
                float rank = CrewLevels.Rank(s);
                if (rank > DipGgFixesMod.PanicMaxRank.Value) return;
                float dmg = Dmg(s);
                float crewLoss = st.startCrew > 0f ? (st.startCrew - crew) / st.startCrew : 0f;
                float over = UnityEngine.Mathf.Max((dmg - DipGgFixesMod.PanicDamage.Value) / UnityEngine.Mathf.Max(0.05f, 1f - DipGgFixesMod.PanicDamage.Value),
                                                  (crewLoss - DipGgFixesMod.PanicCrewLoss.Value) / UnityEngine.Mathf.Max(0.05f, 1f - DipGgFixesMod.PanicCrewLoss.Value));
                if (over <= 0.02f) return;
                float rankFactor = 1f - rank / UnityEngine.Mathf.Max(0.01f, DipGgFixesMod.PanicMaxRank.Value); // 1 for the worst crews, 0 at the limit
                float p = DipGgFixesMod.PanicChance.Value * over * (0.25f + 0.75f * rankFactor);
                if (UnityEngine.Random.value > p) return;
                var div = s.division; if (div == null) return;
                st.panicked = true; panics++;
                string how;
                if (s.isDivLeader || div.ships == null || div.ships.Count <= 1) { div.Retreat(true); how = "division retreats"; }
                else if (s.CanDetach())
                {
                    var il = new Il2CppSystem.Collections.Generic.List<Ship>(); il.Add(s); Ship.DetachShips(il);
                    var nd = s.division; if (nd != null) nd.Retreat(true); how = "leaves the division and retreats";
                }
                else { div.Retreat(true); how = "cannot detach - division retreats"; }
                BattleAi.Log($"PANIC: {s.name} ({s.shipType.name}, {(s.player != null && s.player.isMain ? "player" : "AI")}) crew rank {rank:0.00}, damage {dmg:0.00}, crew {crew:0.00} (start {st.startCrew:0.00}, loss {crewLoss:0.00}) - {how}");
            }
            catch (Exception e) { if (Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] PanicCheck: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Ship), "AimAndShoot")]
    internal static class Patch_AimAndShoot
    {
        private static void Prefix(Ship __instance)
        {
            try { ShipTick.Pre(__instance); }
            catch (Exception e) { if (ShipTick.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AimAndShoot prefix: " + e.Message); }
        }
        private static void Postfix(Ship __instance)
        {
            try { ShipTick.Post(__instance); }
            catch (Exception e) { if (ShipTick.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AimAndShoot postfix: " + e.Message); }
        }
    }

    // =====================================================================================
    // 1.11: crew training levels (non-linear rescale) and training-dependent surrender
    // =====================================================================================
    internal static class CrewLevels
    {
        internal static int Count = 0; internal static bool Done;
        internal static float Rank(Ship s)
        {
            int idx = 0; try { idx = s.GetCrewTrainingIndex(); } catch { }
            int n = Count > 1 ? Count : 5;
            return UnityEngine.Mathf.Clamp01(idx / (float)(n - 1));
        }
        internal static void Apply()
        {
            if (Done) return;
            try
            {
                var gd = G.GameData; if (gd == null) return;
                var levels = gd.crewTrainingLevels; if (levels == null || levels.Count == 0) return;
                Done = true; Count = levels.Count;
                var keys = new List<string>(); var vals = new Dictionary<string, CrewTrainingLevels>();
                foreach (var kv in levels) { keys.Add(kv.Key); vals[kv.Key] = kv.Value; }
                // order: numeric key if possible, else by Accuracy ascending
                bool numeric = true; foreach (var k in keys) { int t; if (!int.TryParse(k, out t)) { numeric = false; break; } }
                if (numeric) keys.Sort((a, b) => int.Parse(a).CompareTo(int.Parse(b))); else keys.Sort((a, b) => vals[a].Accuracy.CompareTo(vals[b].Accuracy));
                var sb = new System.Text.StringBuilder("crew training levels (vanilla): ");
                foreach (var k in keys) { var v = vals[k]; sb.Append($"[{k}: acc {v.Accuracy:0.###} aim {v.Aiming:0.###} rel {v.Reload:0.###} dc {v.DamageControl:0.###}] "); }
                MelonLogger.Msg("[DIP GG Fixes] " + sb);
                if (!DipGgFixesMod.CrewLevelsRescale.Value) return;
                int n = keys.Count; if (n < 2) return;
                // baseline = level whose values are closest to 1
                int b = 0; float bd = float.MaxValue;
                for (int i = 0; i < n; i++) { var v = vals[keys[i]]; float d = Math.Abs(v.Accuracy - 1f) + Math.Abs(v.Aiming - 1f) + Math.Abs(v.Reload - 1f) + Math.Abs(v.DamageControl - 1f); if (d < bd) { bd = d; b = i; } }
                var top = vals[keys[n - 1]]; var bot = vals[keys[0]];
                float dirAcc = Dir(top.Accuracy, bot.Accuracy), dirAim = Dir(top.Aiming, bot.Aiming), dirRel = Dir(top.Reload, bot.Reload), dirDc = Dir(top.DamageControl, bot.DamageControl);
                float plus = DipGgFixesMod.CrewPlusAmp.Value, minus = DipGgFixesMod.CrewMinusAmp.Value, pw = DipGgFixesMod.CrewCurvePower.Value;
                var sb2 = new System.Text.StringBuilder("crew training levels (rescaled): ");
                for (int i = 0; i < n; i++)
                {
                    var v = vals[keys[i]];
                    float r = i == b ? 0f : (i < b ? (i - b) / (float)Math.Max(1, b) : (i - b) / (float)Math.Max(1, n - 1 - b));
                    float e = (float)(Math.Sign(r) * Math.Pow(Math.Abs(r), pw) * (r > 0 ? plus : minus));
                    if (dirAcc != 0f) v.Accuracy = Val(e, dirAcc);
                    if (dirAim != 0f) v.Aiming = Val(e, dirAim);
                    if (dirRel != 0f) v.Reload = Val(e, dirRel);
                    if (dirDc != 0f) v.DamageControl = Val(e, dirDc);
                    sb2.Append($"[{keys[i]}: acc {v.Accuracy:0.###} aim {v.Aiming:0.###} rel {v.Reload:0.###} dc {v.DamageControl:0.###}] ");
                }
                MelonLogger.Msg("[DIP GG Fixes] " + sb2 + $"(baseline {keys[b]}, +{plus} / -{minus}, p={pw})");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] CrewLevels.Apply: " + e.Message); }
        }
        // +1 when higher values are better for the top level, -1 when lower is better, 0 when the stat is unused
        static float Dir(float top, float bot) { if (Math.Abs(top - bot) < 1e-4f) return 0f; return top > bot ? 1f : -1f; }
        // e in [-minus, +plus]; positive-direction stats: 1+e; time-like stats (lower is better): 1/(1+e)
        static float Val(float e, float dir) { return dir > 0f ? 1f + e : 1f / Math.Max(0.05f, 1f + e); }
    }

    [HarmonyPatch(typeof(GameData), "DataLoaded")]
    internal static class Patch_DataLoaded
    {
        private static void Postfix() { CrewLevels.Apply(); }
    }

    // surrender threshold per crew training: Ship.CheckForSurrender reads crew_percents_surrender_threshold via Param
    [HarmonyPatch(typeof(Ship), "CheckForSurrender")]
    internal static class Patch_CheckForSurrender
    {
        [ThreadStatic] internal static Ship Current;
        private static void Prefix(Ship __instance) { Current = __instance; if (!CrewLevels.Done) CrewLevels.Apply(); }
        private static void Finalizer() { Current = null; }
        internal static float Threshold(Ship s)
        {
            float r = CrewLevels.Rank(s);
            return UnityEngine.Mathf.Lerp(DipGgFixesMod.SurrenderGreen.Value, DipGgFixesMod.SurrenderElite.Value, r);
        }
    }

    // =====================================================================================
    // 1.11: surrendered ships are captured by the winner (campaign)
    // =====================================================================================
    [HarmonyPatch(typeof(BattleManager), "DamageShip")]
    internal static class Patch_DamageShip
    {
        static int logged;
        private static void Prefix(Ship __0, ref float __1, CampaignBattleBase __2)
        {
            try
            {
                var ship = __0; if (ship == null) return;
                bool surrendered = false; try { surrendered = ship.IsSurrendered; } catch { }
                if (logged < 30) { logged++; MelonLogger.Msg($"[DIP GG Fixes][Capture] post-battle: {ship.name} ({(ship.player != null ? ship.player.data.name : "?")}) damage {__1:0.00} surrendered={surrendered} status={ship.status}"); }
                if (!surrendered || !DipGgFixesMod.CaptureSurrendered.Value) return;
                var battle = __2 as CampaignBattle; if (battle == null) { try { battle = __2.TryCast<CampaignBattle>(); } catch { } }
                if (battle == null) { MelonLogger.Msg("[DIP GG Fixes][Capture] no CampaignBattle - skipped"); return; }
                var loser = ship.player; if (loser == null) return;
                var winner = battle.OppositePlayer(loser); if (winner == null) return;
                // the winner must still have ships in the battle
                var ws = battle.ShipsOf(winner, true); int alive = 0;
                if (ws != null) for (int i = 0; i < ws.Count; i++) { var w = ws[i]; if (w != null && !w.isDead && !w.isSinking && !w.isSunk && !w.IsSurrendered) alive++; }
                if (alive == 0) { MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name} surrendered but {winner.data.name} has no ships left in the battle - not captured"); return; }
                var cc = CampaignController.Instance; if (cc == null) return;
                // 1.11.3: in the first test (6 TBs, 2026-10-15) the transfer went through but the ships were gone
                // from the save: they stayed on the battle's "sunk" lists (loss accounting erases them) and had a
                // crew near zero (LowCrew). Take them off the sunk lists and give them a prize crew first.
                int removed = 0;
                try { var l1 = battle.AttackerShipsSink; if (l1 != null && l1.Remove(ship)) removed++; } catch { }
                try { var l2 = battle.DefenderShipsSink; if (l2 != null && l2.Remove(ship)) removed++; } catch { }
                try { var l3 = battle.sunkShipsCached; if (l3 != null && l3.Remove(ship)) removed++; } catch { }
                float hp = 0f, crew = 0f; try { hp = ship.HP; } catch { } try { crew = ship.CrewPercents; } catch { }
                float minCrew = 0.7f; try { minCrew = UnityEngine.Mathf.Max(0.5f, MonoBehaviourExt.Param("min_crew_threshold", 0.7f)); } catch { }
                var list = new Il2CppSystem.Collections.Generic.List<Ship>();
                cc.TransferShipToNewOwner(winner, ship.id, ref list, null, false);
                PrizeDesign.Ensure(ship, winner, "Capture");
                try { ship.CrewTrainingAmount = 0f; } catch { }
                try { if (crew < minCrew) ship.CrewPercents = minCrew; } catch { }
                __1 = UnityEngine.Mathf.Clamp(DipGgFixesMod.CaptureDamage.Value, 0.05f, 0.95f);
                Captured.Add(ship.Pointer);
                MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name} ({ship.shipType.name}) surrendered to {winner.data.name} and was taken as a prize ({loser.data.name} -> {winner.data.name}); removed from {removed} sunk list(s), HP {hp:0}, crew {crew:0.00} -> {UnityEngine.Mathf.Max(crew, minCrew):0.00}, damage set to {__1:0.00}, crew training reset, status {ship.status}, new owner {(ship.player != null ? ship.player.data.name : "?")}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] DamageShip prefix: " + e.Message); }
        }
        internal static readonly HashSet<IntPtr> Captured = new HashSet<IntPtr>();
        private static void Postfix(Ship __0)
        {
            try
            {
                if (__0 == null || !Captured.Contains(__0.Pointer)) return;
                MelonLogger.Msg($"[DIP GG Fixes][Capture] after DamageShip: {__0.name} status {__0.status}, isSunk {__0.isSunk}, HP {__0.HP:0}, crew {__0.CrewPercents:0.00}, owner {(__0.player != null ? __0.player.data.name : "?")}, port {(__0.PortLocation != null ? __0.PortLocation.name : "none")}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] DamageShip postfix: " + e.Message); }
        }
    }


    // =====================================================================================
    // 1.11.4/1.11.5: a conquered or dissolved nation hands its fleet to the new controller of
    // the port instead of scrapping it. TransferProvinceToNewOwner moves the loser's ships to a
    // random remaining port (SetPortLocation) and only scraps them when the loser has no port
    // left; DisableMediumPlayer / DisablePlayer scrap everything. The ScrapShip prefix catches
    // exactly those scrap calls.
    // =====================================================================================
    internal static class ConquestCtx
    {
        [ThreadStatic] internal static Player NewOwner;
        [ThreadStatic] internal static string ProvinceId;
        [ThreadStatic] internal static Player Dissolving;
        internal static int Transferred, Scrapped;
    }

    [HarmonyPatch(typeof(CampaignController), "TransferProvinceToNewOwner")]
    internal static class Patch_TransferProvinceToNewOwner
    {
        private static void Prefix(Player __0, string __1) { ConquestCtx.NewOwner = __0; ConquestCtx.ProvinceId = __1; }
        private static void Finalizer() { ConquestCtx.NewOwner = null; ConquestCtx.ProvinceId = null; }
    }

    [HarmonyPatch(typeof(CampaignController), "DisableMediumPlayer")]
    internal static class Patch_DisableMediumPlayer
    {
        private static void Prefix(Player __0)
        {
            ConquestCtx.Dissolving = __0; ConquestCtx.Transferred = 0; ConquestCtx.Scrapped = 0;
            try { MelonLogger.Msg($"[DIP GG Fixes][Conquest] {__0?.data?.name ?? "?"} is being dissolved"); } catch { }
        }
        private static void Finalizer()
        {
            try { if (ConquestCtx.Dissolving != null) MelonLogger.Msg($"[DIP GG Fixes][Conquest] {ConquestCtx.Dissolving.data?.name ?? "?"} dissolved: {ConquestCtx.Transferred} ship(s) handed over, {ConquestCtx.Scrapped} scrapped"); } catch { }
            ConquestCtx.Dissolving = null;
        }
    }

    // 1.11.5: the same for a great power that is dissolved
    [HarmonyPatch(typeof(CampaignController), "DisablePlayer", new Type[] { typeof(Player) })]
    internal static class Patch_DisablePlayer
    {
        private static void Prefix(Player __0)
        {
            ConquestCtx.Dissolving = __0; ConquestCtx.Transferred = 0; ConquestCtx.Scrapped = 0;
            try { MelonLogger.Msg($"[DIP GG Fixes][Conquest] {__0?.data?.name ?? "?"} (major) is being dissolved"); } catch { }
        }
        private static void Finalizer()
        {
            try { if (ConquestCtx.Dissolving != null) MelonLogger.Msg($"[DIP GG Fixes][Conquest] {ConquestCtx.Dissolving.data?.name ?? "?"} dissolved: {ConquestCtx.Transferred} ship(s) handed over, {ConquestCtx.Scrapped} scrapped"); } catch { }
            ConquestCtx.Dissolving = null;
        }
    }

    [HarmonyPatch(typeof(CampaignController), "ScrapShip", new Type[] { typeof(Ship), typeof(bool) })]
    internal static class Patch_ScrapShip
    {
        private static Player ByName(CampaignController cc, string name)
        {
            if (string.IsNullOrEmpty(name) || cc.CampaignData == null) return null;
            var ps = cc.CampaignData.Players; if (ps == null) return null;
            for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.data != null && p.data.name == name) return p; }
            return null;
        }

        private static bool Prefix(CampaignController __instance, Ship __0)
        {
            try
            {
                if (DipGgFixesMod.MinorFleetToConqueror == null || !DipGgFixesMod.MinorFleetToConqueror.Value) return true;
                var ship = __0; if (ship == null) return true;
                var loser = ship.player; if (loser == null) return true;
                bool conquest = ConquestCtx.NewOwner != null, dissolving = ConquestCtx.Dissolving != null && ConquestCtx.Dissolving.Pointer == loser.Pointer;
                if (!conquest && !dissolving) return true;
                // only ships that exist: hulls on the slip are scrapped as before
                var st = ship.status;
                if (st == VesselEntity.Status.Building || st == VesselEntity.Status.Commissioning || st == VesselEntity.Status.Sunk || st == VesselEntity.Status.Scrapped || st == VesselEntity.Status.Erased) return true;
                var port = ship.PortLocation;
                Player recipient = null; string why = "";
                if (conquest)
                {
                    // ships in the lost province go to the conqueror; ships elsewhere are left to the game
                    if (port == null || port.ProvinceId != ConquestCtx.ProvinceId) return true;
                    recipient = ConquestCtx.NewOwner; why = "province " + ConquestCtx.ProvinceId + " lost";
                }
                else
                {
                    if (port == null) { ConquestCtx.Scrapped++; return true; }
                    var prov = port.CurrentProvince; string ctrl = prov != null ? prov.Controller : null;
                    recipient = ByName(__instance, ctrl); why = "dissolved, port " + port.name + " controlled by " + (ctrl ?? "?");
                }
                if (recipient == null || recipient.isDisabled || recipient.Pointer == loser.Pointer) { if (dissolving) ConquestCtx.Scrapped++; return true; }
                var list = new Il2CppSystem.Collections.Generic.List<Ship>();
                __instance.TransferShipToNewOwner(recipient, ship.id, ref list, port, false);
                PrizeDesign.Ensure(ship, recipient, "Conquest");
                if (dissolving) ConquestCtx.Transferred++;
                MelonLogger.Msg($"[DIP GG Fixes][Conquest] {ship.name} ({ship.shipType?.name}) {loser.data.name} -> {recipient.data.name} ({why}); status {ship.status}, port {(ship.PortLocation != null ? ship.PortLocation.name : "none")}");
                return false;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Conquest] ScrapShip prefix: " + e.Message); return true; }
        }
    }


    // =====================================================================================
    // 1.11.6: a ship that changes hands brings its design along. If the new owner has no
    // design with the same hull and tonnage, the design is cloned into its design list
    // (PlayerController.CloneDesign) so the ship can be refitted and the class rebuilt.
    // =====================================================================================
    internal static class PrizeDesign
    {
        internal static void Ensure(Ship ship, Player recipient, string tag)
        {
            try
            {
                if (ship == null || recipient == null) return;
                Ship d = null; try { d = ship.design; } catch { }
                if (d == null) d = ship;
                var hull = d.hull; string hullName = hull != null ? hull.name : null;
                float ton = 0f; try { ton = d.Tonnage(); } catch { }
                int count = 0; Ship found = null;
                try
                {
                    var en = recipient.designs.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var x = en.Current; if (x == null) continue; count++;
                        if (x.Pointer == d.Pointer) { found = x; break; }
                        var xh = x.hull; if (xh == null || hullName == null || xh.name != hullName) continue;
                        float xt = 0f; try { xt = x.Tonnage(); } catch { }
                        if (Math.Abs(xt - ton) <= Math.Max(50f, ton * 0.01f)) { found = x; break; }
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] design scan: " + e.Message); }
                if (found != null)
                {
                    MelonLogger.Msg($"[DIP GG Fixes][{tag}] {recipient.data.name} already has a design for {ship.name} ({found.name}, hull {hullName}); {count} design(s) in list");
                    return;
                }
                var pc = PlayerController.Instance; if (pc == null) { MelonLogger.Msg($"[DIP GG Fixes][{tag}] no PlayerController - design not cloned"); return; }
                var clone = pc.CloneDesign(d, recipient);
                int after = 0; try { var en2 = recipient.designs.GetEnumerator(); var un2 = en2.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un2.MoveNext()) after++; } catch { }
                if (clone != null) { try { ship.design = clone; } catch { } }
                MelonLogger.Msg($"[DIP GG Fixes][{tag}] design of {ship.name} (hull {hullName}, {ton:0} t) cloned to {recipient.data.name} as {(clone != null ? clone.name : "null")}; designs {count} -> {after}");
            }
            catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] design clone failed: " + e.Message); }
        }
    }

    // log the fate of captured ships once the game has finished its loss accounting
    [HarmonyPatch(typeof(CampaignController), "CheckResultBattleOnShipLosses", new Type[] { typeof(CampaignBattle) })]
    internal static class Patch_CheckResultBattleOnShipLosses
    {
        private static void Postfix(CampaignBattle __0)
        {
            try
            {
                if (Patch_DamageShip.Captured.Count == 0 || __0 == null) return;
                var all = __0.ActualShips; if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i]; if (s == null || !Patch_DamageShip.Captured.Contains(s.Pointer)) continue;
                    MelonLogger.Msg($"[DIP GG Fixes][Capture] after loss accounting: {s.name} status {s.status}, isSunk {s.isSunk}, owner {(s.player != null ? s.player.data.name : "?")}");
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] CheckResultBattleOnShipLosses postfix: " + e.Message); }
        }
    }

    // ai_distance_mod (preferred engagement distance) scaled per nation while the game
    // evaluates an AI ship's range inside Ship.AILogic (RangeCalc).
    [HarmonyPatch(typeof(MonoBehaviourExt), "Param", new Type[] { typeof(string), typeof(float) })]
    internal static class Patch_Param
    {
        static int surrLogged;
        private static void Postfix(string __0, ref float __result)
        {
            if (__0 == "crew_percents_surrender_threshold" && DipGgFixesMod.SurrenderByTraining.Value)
            {
                var s = Patch_CheckForSurrender.Current;
                if (s != null)
                {
                    float t = Patch_CheckForSurrender.Threshold(s);
                    if (surrLogged < 5) { surrLogged++; BattleAi.Log($"surrender threshold for {s.name}: {t:0.00} (vanilla {__result:0.00}, crew rank {CrewLevels.Rank(s):0.00})"); }
                    __result = t;
                }
                return;
            }
            var m = BattleAi.CurrentDistanceMult;
            if (m == 1f || m <= 0f) return;
            if (__0 == "ai_distance_mod") __result *= m;
        }
    }
}

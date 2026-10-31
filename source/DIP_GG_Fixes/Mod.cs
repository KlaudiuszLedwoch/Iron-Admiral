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

[assembly: MelonInfo(typeof(DipGgFixes.DipGgFixesMod), "DIP GG Fixes", "1.11.489", "DIP GG Extended")]
[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: HarmonyDontPatchAll] // 1.11.134: patches are applied one class at a time (SafePatches)

namespace DipGgFixes
{
    public class DipGgFixesMod : MelonMod
    {
        internal static int Caught;
        internal static MelonPreferences_Entry<bool> AutoShare;
        internal static MelonPreferences_Entry<bool> AllNations;
        internal static MelonPreferences_Entry<bool> CruisersWhenPoor;
        internal static MelonPreferences_Entry<bool> RelationRepairOn; // 1.11.286 (#6)
        internal static MelonPreferences_Entry<float> BigShipFactor;
        internal static MelonPreferences_Entry<float> CruiserFactor;
        internal static MelonPreferences_Entry<float> MinorPortCapacityMult;
        internal static MelonPreferences_Entry<float> AttitudeMult;
        internal static MelonPreferences_Entry<float> NegAttitudeMult, UnscaledFrom;
        internal static MelonPreferences_Entry<bool> FixStalledBuilding;
        internal static MelonPreferences_Entry<int> CollapseGraceMonths;
        internal static MelonPreferences_Entry<bool> FinancialCrisis;
        internal static MelonPreferences_Entry<int> CrisisMonths;
        internal static MelonPreferences_Entry<int> CrisisDebtMonths;
        internal static MelonPreferences_Entry<int> LoanTermMonths;
        internal static MelonPreferences_Entry<float> LoanRepayShare;
        internal static MelonPreferences_Entry<int> LoanGrantMonths;
        internal static MelonPreferences_Entry<int> PrintGrantMonths;
        internal static MelonPreferences_Entry<int> PrintInflation;
        internal static MelonPreferences_Entry<float> InflationDecay;
        internal static MelonPreferences_Entry<float> PrintUnrest;
        internal static MelonPreferences_Entry<int> CutsMonths;
        internal static MelonPreferences_Entry<float> CutsMaintenanceSaving;
        internal static MelonPreferences_Entry<bool> BankruptNoCapitalShips;
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
        internal static MelonPreferences_Entry<bool> BalanceOnlySolvent;
        internal static MelonPreferences_Entry<bool> AiTechBalance;
        internal static MelonPreferences_Entry<float> AiTechBudgetMin;
        internal static MelonPreferences_Entry<float> AiShipyardHeadroom, AiShipyardSurplusMonths, AiShipyardCostCover;
        internal static MelonPreferences_Entry<bool> AiDesignOnlyWhenBuildable;
        internal static MelonPreferences_Entry<float> RefitTreatyAllowance;
        internal static MelonPreferences_Entry<int> BalanceCooldown;
        // 1.11
        internal static MelonPreferences_Entry<float> ChargeApproachSeconds;
        internal static MelonPreferences_Entry<float> ChargeBaseExtra;
        internal static MelonPreferences_Entry<bool> CapitalCharge;
        internal static MelonPreferences_Entry<float> CapitalSpeedMargin;
        internal static MelonPreferences_Entry<bool> CapitalCloseIn;
        internal static MelonPreferences_Entry<float> ChargeReachFactor;
        internal static MelonPreferences_Entry<bool> CloseInOnlyIfPenetrates;
        internal static MelonPreferences_Entry<bool> Evasive;
        internal static MelonPreferences_Entry<float> EvasiveAngle;
        internal static MelonPreferences_Entry<float> EvasiveSeconds;
        internal static MelonPreferences_Entry<bool> TorpedoTurnAway;
        internal static MelonPreferences_Entry<float> TorpedoTurnAwayRange;
        internal static MelonPreferences_Entry<float> TorpedoTurnAwaySeconds;
        internal static MelonPreferences_Entry<bool> FleetTorpedoAttack;
        internal static MelonPreferences_Entry<bool> Ramming;
        internal static MelonPreferences_Entry<bool> TorpedoReloadCycle, CapitalSupportScreen, CoverRetreat, ValueBasedAbort, AiCrossTorpedo, AiApproachSmoke, AiWithdrawLowAmmo;
        internal static MelonPreferences_Entry<bool> NightRules, CruiserRoles, FireDistributionOn; internal static MelonPreferences_Entry<float> NightVisibilityKm;
        internal static MelonPreferences_Entry<float> ReloadReturnFraction, ReloadMaxSeconds, SupportRangeFraction, CoverRetreatStrengthRatio, CoverValueRatio;
        internal static MelonPreferences_Entry<float> RamRange;
        internal static MelonPreferences_Entry<bool> CapitalEscapeWhenHopeless;
        internal static MelonPreferences_Entry<float> HopelessEscapeDelay, HopelessFightOnRatio;
        internal static MelonPreferences_Entry<bool> HopelessEngageLighter; // 1.11.250
        internal static MelonPreferences_Entry<bool> EscapeMissionLogic;
        internal static MelonPreferences_Entry<string> EscapeBattleTypes;
        internal static MelonPreferences_Entry<float> OutrangedFactor;
        internal static MelonPreferences_Entry<float> PenetrationFactor;
        internal static MelonPreferences_Entry<float> CloseInStopRange;
        internal static MelonPreferences_Entry<bool> PlayerScoutCharge;
        internal static MelonPreferences_Entry<bool> TorpedoFriendlyLane;
        internal static MelonPreferences_Entry<float> LaneHalfWidth;
        internal static MelonPreferences_Entry<bool> TorpedoSense, TorpedoSensePlayer; // 1.11.93
        internal static MelonPreferences_Entry<bool> TorpedoSideCheck; // 1.11.94
        internal static MelonPreferences_Entry<float> TorpedoSenseClose, TorpedoSenseSpeed, TorpedoSenseMargin;
        internal static MelonPreferences_Entry<bool> SmartTargeting;
        internal static MelonPreferences_Entry<bool> SmartTargetingAi;
        internal static MelonPreferences_Entry<float> TorpedoThreatRangeMult;
        internal static MelonPreferences_Entry<bool> TransportsAfterEscort; internal static MelonPreferences_Entry<float> TransportLowCaliberMax, TransportEscortRangeMult, TransportEscortMemorySeconds; // 1.11.252
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
        internal static MelonPreferences_Entry<bool> SurrenderReturnToWinner;
        internal static MelonPreferences_Entry<bool> CoreIndependence;
        internal static MelonPreferences_Entry<float> CoreSuppressEscalation, CoreOccupationUnrest, CoreOccupationCost; // 1.11.150
        internal static MelonPreferences_Entry<float> CoreRevoltBonus, CoreRevoltRampMonths, CoreSuppressIncomeMonths, CoreSuppressUnrest, CoreSuppressRelations, CoreSuppressAiMaxUnrest, CoreSuppressCalmMonths;
        internal static MelonPreferences_Entry<float> TorpedoTargetSizeRatio;
        internal static MelonPreferences_Entry<float> MassAttackStrengthRatio;
        internal static MelonPreferences_Entry<float> MainShipSizeRatio;
        internal static MelonPreferences_Entry<float> MassAttackPerTarget;
        internal static MelonPreferences_Entry<bool> WithdrawWhenOutmatched;
        internal static MelonPreferences_Entry<bool> MinorFleetToConqueror;
        internal static MelonPreferences_Entry<bool> NationalTech;
        internal static MelonPreferences_Entry<bool> CheckUpdates;
        internal static MelonPreferences_Entry<bool> UpdateDownload, UpdateAutoInstall;
        internal static MelonPreferences_Entry<float> NationalTechScale;

        public override void OnUpdate()
        {
            if (!migDone) { migDone = true; Migrations(); }
            try { Perf.Frame(); } catch { }
            try { BattleSel.Tick(); } catch { } // 1.11.261
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { Update0(); } finally { if (t0 != 0) Perf.Add(Perf.UPD, t0); }
        }
        void Update0()
        {
            UpdateCheck.Tick();
            FinanceCrisis.Tick();
            EconomyUi.Tick();
            SettingsUi.Tick();
            ResolutionFix.Tick(); RevoltDecision.Tick(); Occupation.Tick(); BattleAi.OrdersTick(); ReShadeSetup.Tick(); Ballistics.Tick(); HistoricalDamage.Tick(); ThinPlatingHE.Tick(); EraAccuracy.Tick(); HistoricalWeights.Tick(); OtherMods.Tick(); TorpedoLog.Tick(); LogShare.TickWindow(); LogShare.Tick();
            SaveCompat.Tick();
            ArmorDiag.Tick(); ShipPartsCheck.Tick(); DesignRecorder.Tick(); FleetAgeReport.Tick(); TechDiffusion.Tick(); DivisionSpacing.Tick(); try { PlayerDivisions.Tick(); } catch { } try { WarScoreReset.Tick(); } catch { } try { SeaRepair.Tick(); } catch { } try { PenReference.Tick(); } catch { } try { Spectacle.Tick(); } catch { } try { SlowProbe.Tick(); } catch { } try { SaveBackup.Tick(); } catch { } try { EraMap.Tick(); } catch { } try { Patch_AiTakeReparation.Tick(); } catch { } MapGhosts.Tick(); Irredenta.Tick(); BattleHour.Tick();
            Fog.Tick();
            Diagnostics.Tick();
            Scenarios.Tick();
            Narrative.Tick();
            try { CustomBattle.Tick(); } catch { }
            try { Portless.Tick(); } catch { }
            try { Dispatch.Tick(); } catch { }
            try { PrizeDesign.SessionMerge(); } catch { } // 1.11.353 (#72)
            try { DesignerText.Tick(); } catch { } // 1.11.357
            try { OtherMods.ReportOverlapsOnce(Harm); } catch { } // 1.11.361
            try { EnemyProfile.Tick(); } catch { } // 1.11.368 (#84)
            try { AirStrike.Tick(); } catch { } // 1.11.372 (#85)
            try { Spotters.Tick(); } catch { } // 1.11.417 (#85)
            try { AirRecon.Tick(); } catch { } // 1.11.425 (#85)
            try { Climate.Tick(); } catch { } // 1.11.431
            try { CampaignTest.Tick(); } catch { } // 1.11.434
            try { Highlights.Tick(); } catch { } // 1.11.422
            try { TorpedoSettings.Tick(); } catch { } try { TorpedoPriority.Tick(); } catch { } // 1.11.251, 1.11.259
            try { HistDesigner.Tick(); } catch { }
            try { AutoTest.Tick(); } catch { } try { RelationRepair.Tick(); } catch { } // 1.11.286
            try { Trade.Tick(); } catch { }
            try { Intel.Tick(); } catch { }
            try { DesignRepair.Tick(); } catch { }
            try { VersionLabel.Tick(); } catch { } // 1.11.111
            try { FleetOrders.Tick(); } catch { } // 1.11.107
        }

        public override void OnLateUpdate()
        {
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { AirStrike.LateTick(); } catch { } try { Spectacle.LateTick(); } catch { } try { Fog.LateTick(); } catch { } try { Narrative.LateTick(); } catch { } try { BarrelElevation.LateTick(); } catch { } // 1.11.209
            if (t0 != 0) Perf.Add(Perf.LATE, t0);
        }

        public override void OnApplicationQuit() { try { Diagnostics.OnQuit(); } catch { } try { LogShare.OnQuit(); } catch { } }

        public override void OnGUI()
        {
            if (AirStrike.Hiding) return;   // 1.11.380: no mod panels on an air strike screenshot
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { UiScale.Begin(); Gui0(); } finally { UiScale.End(); if (t0 != 0) Perf.Add(Perf.GUI, t0); }
        }
        void Gui0()
        {
            EconomyUi.OnGUI();
            try { Trade.OnGUI(); } catch { }
            try { Intel.OnGUI(); } catch { }
            try { PortForces.OnGUI(); } catch { } // 1.11.120
            try { FleetOrders.OnGUI(); } catch { } // 1.11.107
            try { Dispatch.OnGUI(); } catch { }
            try { Dispatch.LogPanelGUI(); } catch { } // 1.11.349
            try { Thanks.OnGUI(); } catch { } // 1.11.370
            try { AirStrike.OnGUI(); } catch { } // 1.11.385
            try { DesignStats.OnGUI(); } catch { } // 1.11.397
            try { Narrative.OnGUI(); } catch { }
            SettingsUi.OnGUI();
            RevoltDecision.OnGUI(); Occupation.OnGUI(); BattleAi.OrdersOnGUI(); try { BattleHour.OnGUI(); } catch { } try { TorpedoSettings.OnGUI(); } catch { } try { TorpedoPriority.OnGUI(); } catch { } try { GunOrders.OnGUI(); } catch { } RangeTags.OnGUI(); OtherMods.OnGUI(); LogShare.OnGUI();
            Diagnostics.OnGUI();
        }

        internal static HarmonyLib.Harmony Harm; // 1.11.96
        // 1.11.99: one-time setting changes run on the first frame - MelonLoader loads the saved values after OnInitializeMelon,
        // so a change made there was overwritten (1.11.96/1.11.98 migrations had no effect on existing installs)
        internal static MelonPreferences_Entry<bool> MigSide, MigOccCost, MigEraOff, MigRangeCap; // the armour diagnostics stay as the player set them
        static bool migDone;
        // 1.11.208: old setting names (EraAccuracy, EraAccuracyRadar, EraAccuracyScale, EraDispersion) - the values the player had are copied
        static void RenameFireControlPrefs()
        {
            try
            {
                var f = System.IO.Path.Combine(MelonUtils.UserDataDirectory, "MelonPreferences.cfg"); if (!System.IO.File.Exists(f)) return;
                var old = new Dictionary<string, string>();
                foreach (var l in System.IO.File.ReadAllLines(f)) { var t = l.Trim(); int q = t.IndexOf('='); if (q > 0) { var k = t.Substring(0, q).Trim(); if (k.StartsWith("EraAccuracy") || k == "EraDispersion") old[k] = t.Substring(q + 1).Trim(); } }
                string v; float x; bool b;
                if (old.TryGetValue("EraAccuracy", out v) && bool.TryParse(v, out b)) EraAccuracy.On.Value = b;
                if (old.TryGetValue("EraDispersion", out v) && bool.TryParse(v, out b)) EraAccuracy.Dispersion.Value = b;
                if (old.TryGetValue("EraAccuracyRadar", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) EraAccuracy.Radar.Value = x;
                if (old.TryGetValue("EraAccuracyScale", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)) EraAccuracy.Scale.Value = x;
                MelonLogger.Msg($"[DIP GG Fixes] settings: hit chance by fire-control technology year - {old.Count} old EraAccuracy/EraDispersion value(s) copied to the FireControlTech* settings (1.11.208)");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] settings rename: " + e.Message); }
        }
        static void Migrations()
        {
            try
            {
                bool save = false;
                if (MigSide != null && !MigSide.Value) { TorpedoSideCheck.Value = false; MigSide.Value = true; save = true; MelonLogger.Msg("[DIP GG Fixes] settings: TorpedoSideCheck switched off once (1.11.99)"); }
                // 1.11.195 (author, 24.10): the money side of occupation was 5-10 times too cheap - old defaults raised once (values the player changed stay)
                if (MigOccCost != null && !MigOccCost.Value)
                {
                    if (CoreOccupationCost != null && Math.Abs(CoreOccupationCost.Value - 3f) < 0.01f) CoreOccupationCost.Value = 20f;
                    if (Occupation.PayShare != null && Math.Abs(Occupation.PayShare.Value - 4f) < 0.01f) Occupation.PayShare.Value = 30f;
                    MigOccCost.Value = true; save = true; MelonLogger.Msg($"[DIP GG Fixes] settings: occupation costs raised once (1.11.195): CoreOccupationCost {CoreOccupationCost?.Value}, OccupationPayShare {Occupation.PayShare?.Value}");
                }
                if (MigEraOff != null && !MigEraOff.Value) { RenameFireControlPrefs(); MigEraOff.Value = true; save = true; } // 1.11.208
                if (MigRangeCap != null && !MigRangeCap.Value) { if (MountElevation.On != null) MountElevation.On.Value = true; MigRangeCap.Value = true; save = true; MelonLogger.Msg("[DIP GG Fixes] settings: RangeByMountElevation switched on once (1.11.225: range from physics - muzzle velocity from the technologies, elevation from the gun generation)"); }
                if (save) MelonPreferences.Save();
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] settings migration: " + e.Message); }
        }
        public override void OnInitializeMelon()
        {
            ConsoleGuard.Apply(); // 1.11.228
            SafePatches.ApplyAll(HarmonyInstance); // 1.11.134
            Harm = HarmonyInstance;
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
            BalanceOnlySolvent = cat.CreateEntry("BalanceGrantOnlyWhenSolvent", true, null,
                "Balance-of-power grants go only to nations with a non-negative monthly income and are sized to lift them just over the AI's shipbuilding reserve (aiShipbuilding x revenue) instead of a flat 3 months of budget");
            BalanceCooldown = cat.CreateEntry("BalanceGrantCooldownMonths", 6, null, "Months between two balance-of-power grants to the same nation");
            AiTechBalance = cat.CreateEntry("AiTechBudgetBalance", true, null,
                "An AI great power spends what it likes on research while its monthly income is positive; with a deficit the research budget is cut by the deficit so that income returns to zero (never below AiTechBudgetMinPercent)");
            AiTechBudgetMin = cat.CreateEntry("AiTechBudgetMinPercent", 10f, null, "Floor for the AI research budget when it is cut to balance the books");
            AiShipyardHeadroom = cat.CreateEntry("AiShipyardHeadroom", 2.0f, null,
                "An AI great power does not start a shipyard expansion while its income is negative or while shipyard capacity already exceeds this multiple of its fleet tonnage. 0 = vanilla");
            AiShipyardSurplusMonths = cat.CreateEntry("AiShipyardSurplusMonths", 6f, null,
                "AI great powers start a shipyard expansion only after this many months of surplus in a row (and with cash for the expected shortfall), or when AiShipyardCostCover holds");
            AiShipyardCostCover = cat.CreateEntry("AiShipyardCostCover", 1.25f, null,
                "...or when the monthly surplus covers this multiple of the expected monthly expansion cost (last expansion of that nation, otherwise half its monthly budget)");
            AiDesignOnlyWhenBuildable = cat.CreateEntry("AiDesignOnlyWhenBuildable", true, null,
                "AI nations skip generating new ship designs in a turn when they cannot build anyway (no ports, cash below zero, or shipyard capacity full); designs at campaign start are not affected");
            RefitTreatyAllowance = cat.CreateEntry("RefitTreatyAllowanceTons", 3000f, null,
                "Refit of an existing ship may exceed the treaty tonnage limit: up to its own tonnage plus this many tons (Washington Treaty 1922, Ch. II Part 3 Sec. I(d): +3,000 t for anti-air, anti-torpedo and deck protection), never above the hull's own maximum. 0 = vanilla");
            AttitudeMult = cat.CreateEntry("DiplomacyAttitudeMultiplier", 1.5f, null,
                "Multiplier for positive diplomatic attitude changes (improvements; 1 = vanilla)");
            NegAttitudeMult = cat.CreateEntry("DiplomacyNegativeAttitudeMultiplier", 4.0f, null,
                "Multiplier for negative diplomatic attitude changes (worsening, by the player and by the AI; 1 = vanilla)");
            UnscaledFrom = cat.CreateEntry("DiplomacyUnscaledFrom", 20f, null, "Diplomatic changes of this size or more (event answers, one-off actions) are not multiplied by the attitude multipliers (0 = all are multiplied)");
            FixStalledBuilding = cat.CreateEntry("FixStalledConstruction", true, null,
                "Advance AI ship construction for nations whose building ships were not advanced during a turn");
            CollapseGraceMonths = cat.CreateEntry("EconomicCollapseGraceMonths", 24, null,
                "An AI nation whose state budget fell to zero is dissolved (\"economic collapse\") only after this many consecutive months at zero (0 = vanilla: immediately). Losing all ports still dissolves at once.");
            FinancialCrisis = cat.CreateEntry("FinancialCrisis", true, null,
                "Treasury crisis for great powers whose cash stays below zero: war loan, money printing (inflation) or fleet cuts. AI chooses by situation, the player gets a dialog.");
            CrisisMonths = cat.CreateEntry("CrisisAfterMonthsInDebt", 6, null, "Consecutive months with cash below zero before the crisis event fires");
            CrisisDebtMonths = cat.CreateEntry("CrisisWhenDebtExceedsMonthsOfBudget", 12, null, "The crisis fires at once when the debt exceeds this many months of naval budget (0 = off)");
            LoanTermMonths = cat.CreateEntry("LoanTermMonths", 120, null, "War loan: repayment period in months");
            LoanRepayShare = cat.CreateEntry("LoanRepaymentShareOfBudget", 0.15f, null, "War loan: monthly repayment as a share of the monthly naval budget");
            LoanGrantMonths = cat.CreateEntry("LoanGrantMonthsOfBudget", 12, null, "War loan: debt is cleared and this many months of naval budget are credited");
            PrintGrantMonths = cat.CreateEntry("PrintGrantMonthsOfBudget", 24, null, "Money printing: debt is cleared and this many months of naval budget are credited");
            PrintInflation = cat.CreateEntry("PrintInflationPercent", 40, null, "Money printing: initial inflation, raises all naval expenses by this percentage");
            InflationDecay = cat.CreateEntry("InflationDecayPerMonth", 1.0f, null, "Inflation falls by this many percentage points each month");
            PrintUnrest = cat.CreateEntry("PrintUnrest", 5f, null, "Money printing: unrest added once");
            CutsMonths = cat.CreateEntry("FleetCutsMonths", 36, null, "Fleet cuts: months without new capital ships (bb/bc)");
            CutsMaintenanceSaving = cat.CreateEntry("FleetCutsMaintenanceSaving", 0.3f, null, "Fleet cuts: share of fleet maintenance saved while the cuts last");
            BankruptNoCapitalShips = cat.CreateEntry("BankruptBuildsCheapShips", true, null,
                "An AI great power with cash below zero (or under fleet cuts) orders no bb/bc and favours ca/cl/dd/tb");
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
            ChargeReachFactor = cat.CreateEntry("ChargeReachFactor", 1.5f, null,
                "A division that cannot escape only starts a torpedo charge when the target is within its charge start distance x this, or when it is faster than the target (otherwise it fights with guns)");
            CloseInOnlyIfPenetrates = cat.CreateEntry("CloseInOnlyIfPenetrates", true, null,
                "AI capital ships do not close in when their guns could not penetrate the target's belt even at the closest planned range");
            Evasive = cat.CreateEntry("EvasiveManeuvers", true, null,
                "AI ships zigzag while approaching (torpedo charge, close-in out of own range or without penetration): target turning lowers enemy accuracy (params acc_turning_target), own turning lowers own accuracy, so it is used only when own guns are not effective");
            EvasiveAngle = cat.CreateEntry("EvasiveAngle", 30f, null, "Zigzag: course offset to each side from the direct course (degrees)");
            EvasiveSeconds = cat.CreateEntry("EvasiveLegSeconds", 25f, null, "Zigzag: average leg length in seconds (randomised +-30%)");
            TorpedoTurnAway = cat.CreateEntry("TorpedoTurnAway", true, null,
                "AI divisions that are not charging turn away from sighted enemy torpedo tracks and steer parallel to them (combing the tracks, as the Grand Fleet did at Jutland); only tracks the leader can see and that would pass close to it count");
            TorpedoTurnAwayRange = cat.CreateEntry("TorpedoTurnAwayRange", 4000f, null, "Turn-away: torpedoes within this distance of the division leader (m) are checked");
            TorpedoTurnAwaySeconds = cat.CreateEntry("TorpedoTurnAwaySeconds", 45f, null, "Turn-away: how long the division keeps the evasive course (s)");
            FleetTorpedoAttack = cat.CreateEntry("FleetTorpedoAttack", true, null,
                "When the enemy has more battleships/battlecruisers than the AI side, all AI torpedo craft attack the enemy capital ships at once, whatever the distance: a single ship would not get through, but part of a massed attack reaches torpedo or ramming range");
            AiCrossTorpedo = cat.CreateEntry("AiCrossTorpedo", true, null, "Battle AI: AI divisions attacking the same target with torpedoes approach 45 degrees either side of its bow so the tracks cross, and a division whose tubes on one side are empty turns to fire the other side before going off to reload (#56)");
            AiApproachSmoke = cat.CreateEntry("AiApproachSmoke", true, null, "Battle AI: an AI torpedo flotilla that is hit during its approach lays a smoke screen at once (every 30 s at most while it keeps being hit)"); // 1.11.344
            AiWithdrawLowAmmo = cat.CreateEntry("AiWithdrawLowAmmo", true, null, "Battle AI: an AI division with no main-battery shells left on any ship and no loaded torpedo tubes withdraws from the battle"); // 1.11.345, 1.11.346
            TorpedoReloadCycle = cat.CreateEntry("TorpedoReloadCycle", true, null,
                "AI torpedo craft press the attack until every tube is empty; ships with reload torpedoes on board then turn away (smoke when available), open the range, reload and return, ships without reloads turn on the enemy light forces");
            ReloadReturnFraction = cat.CreateEntry("ReloadReturnFraction", 0.75f, null, "Attack cycle: share of loaded tubes needed before a flotilla returns to the attack");
            ReloadMaxSeconds = cat.CreateEntry("ReloadMaxSeconds", 1200f, null, "Attack cycle: a reloaded flotilla returns after this many game seconds even when it could not reach a safe distance; with no reload after 1.5 x this the ship counts as having no reloads (test 22.10: reloading took about 950 s)");
            CapitalSupportScreen = cat.CreateEntry("CapitalSupportScreen", true, null,
                "Battle line first: AI bb/bc/ca move up to effective gun range instead of waiting at extreme range (unless the enemy is clearly stronger), AI flotillas hold their torpedoes until the battle lines are engaged, then the line supports them by engaging the enemies nearest to the attacking flotillas");
            SupportRangeFraction = cat.CreateEntry("SupportRangeFraction", 0.6f, null, "Screen support: capital ships close to this share of their gun range");
            CoverRetreat = cat.CreateEntry("CoverRetreat", true, null,
                "When the AI torpedo attack has failed and the enemy is stronger, the most valuable ships (cost and build time) withdraw while the cheaper ones attack the pursuers or lay smoke between them");
            CoverRetreatStrengthRatio = cat.CreateEntry("CoverRetreatStrengthRatio", 1.2f, null, "Covering retreat: enemy combat tonnage must exceed own x this");
            CoverValueRatio = cat.CreateEntry("CoverValueRatio", 0.6f, null, "Covering retreat: ships worth at least this share of the side's most valuable ship withdraw, cheaper ones cover");
            NightRules = cat.CreateEntry("NightRules", true, null,
                "Night battles (War Instructions 1944, chapter 8, and national doctrine): heavy ships do not close the range, flotillas attack by surprise and break off when spotted early by a stronger enemy; nations that avoid night action hold their flotillas back, nations that seek it attack without waiting for the battle lines");
            NightVisibilityKm = cat.CreateEntry("NightVisibilityKm", 10f, null, "Night rules: a battle counts as night when the game's daytime visibility is below this (km)");
            CruiserRoles = cat.CreateEntry("CruiserRoles", true, null,
                "AI cruisers (heavy always, light when the side has destroyers): defend the battle line against enemy flotillas, support own flotilla attacks, pin the enemy line from the opposite flank, keep out of the enemy line's concentrated fire; flying squadron where the doctrine has it");
            FireDistributionOn = cat.CreateEntry("FireDistribution", true, null,
                "Main gun fire distribution (War Instructions 1944, 12164-12166): at most the doctrine's number of ships on one target (default 3), one ship per target when outnumbered, targets combining hitting power with weak protection first");
            ValueBasedAbort = cat.CreateEntry("ValueBasedAbort", true, null, "AI charge abort damage scaled by ship value: cheap ships fight longer (x1.2), the most valuable break off earlier (x0.8)");
            Ramming = cat.CreateEntry("Ramming", true, null,
                "AI light ships without torpedoes (or out of torpedoes) that cannot escape, or take part in a massed attack, steer into the nearest enemy ship within ramming range");
            RamRange = cat.CreateEntry("RamRange", 2000f, null, "Ramming: an enemy ship closer than this (m) is rammed");
            CapitalEscapeWhenHopeless = cat.CreateEntry("CapitalEscapeWhenHopeless", true, null,
                "AI battleships/battlecruisers that cannot penetrate the enemy even at close range retreat when they can survive to the end of the battle: they are faster than every visible enemy, or no visible enemy can penetrate their belt");
            HopelessEscapeDelay = cat.CreateEntry("HopelessEscapeDelay", 120f, null,
                "Seconds of battle time an AI capital ship keeps engaging an identified target it cannot penetrate before it breaks off (it never breaks off from a ship it has not identified)");
            HopelessEngageLighter = cat.CreateEntry("HopelessEngageLighter", true, null,
                "An AI capital ship that cannot pierce the enemy battle line stays in action while it can hurt other enemy ships in range (cruisers, destroyers), and does not break off when no enemy can pierce its own belt either (1.11.250)");
            HopelessFightOnRatio = cat.CreateEntry("HopelessFightOnRatio", 1.5f, null,
                "An AI capital ship that cannot penetrate its target still fights on when its own side's combat tonnage is at least the enemy's x this");
            EscapeMissionLogic = cat.CreateEntry("EscapeMissionLogic", true, null,
                "In battles where the AI side's task is to get away (EscapeBattleTypes): no massed torpedo attack; divisions that are faster than the enemy retreat, divisions that cannot outrun it split up and flee in different directions so that part of the force gets away");
            EscapeBattleTypes = cat.CreateEntry("EscapeBattleTypes", "escape_taskforce", null, "Battle types (battleTypeEx.csv names, comma separated) in which the defending side tries to escape");
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
            TorpedoSense = cat.CreateEntry("TorpedoSense", true, null,
                "AI ships hold torpedoes when the shot is hopeless: the target outruns the torpedo, the run to the intercept point exceeds the torpedo range, or the target is a small ship beyond close range");
            TorpedoSideCheck = cat.CreateEntry("TorpedoSideCheck", false, null,
                "Experimental (off): drop a torpedo aim of one side's launchers whose target lies on the other side of the ship; player-set torpedo targets are never touched");
            MigSide = cat.CreateEntry("MigrationTorpedoSideOff199", false, null, "Internal: 1.11.99 switched TorpedoSideCheck off once (it dropped valid torpedo aims)");
            MigRangeCap = cat.CreateEntry("MigrationRangeCapOn225", false, null, "Internal: 1.11.225 switched RangeByMountElevation on once (author's decision: range from physics and technology)");
            MigEraOff = cat.CreateEntry("MigrationFireControlNames208", false, null, "Internal: 1.11.208 renamed the EraAccuracy* / EraDispersion settings to FireControlTech* and copied the player's values once");
            MigOccCost = cat.CreateEntry("MigrationOccupationCost195", false, null, "Internal: 1.11.195 raised the occupation costs once (CoreOccupationCost 3 -> 20, OccupationPayShare 4 -> 30)");
            // 1.11.335 (author 28.10, 08:36): on by default for everyone - the player's ships under automatic torpedo fire no longer
            // waste torpedoes on hopeless shots either (manual shots are never held)
            TorpedoSensePlayer = cat.CreateEntry("TorpedoSensePlayer", true, null,
                "Battle: your ships under automatic torpedo fire hold hopeless shots too - target beyond torpedo range, outrunning the torpedo, or a small ship far off and not closing (manual shots are never held)");
            TorpedoSenseClose = cat.CreateEntry("TorpedoSenseCloseRange", 1500f, null,
                "Torpedo check: inside this distance (m) the torpedoes are never held");
            TorpedoSenseSpeed = cat.CreateEntry("TorpedoSenseTorpedoSpeed", 20f, null,
                "Torpedo check: assumed torpedo speed (m/s) for the intercept estimate");
            TorpedoSenseMargin = cat.CreateEntry("TorpedoSenseRangeMargin", 0.85f, null,
                "Torpedo check: the run to the intercept point may use at most this share of the torpedo range");
            SmartTargeting = cat.CreateEntry("SmartTargeting", true, null,
                "Player ships without a manual target: guns pick the most damaged enemy in range; bb/bc main guns prefer the heaviest enemy calibre; secondary guns prefer the nearest torpedo threat, then the nearest enemy");
            SmartTargetingAi = cat.CreateEntry("SmartTargetingAi", true, null,
                "Apply the same target selection to AI ships (replaces FocusFire)");
            TorpedoThreatRangeMult = cat.CreateEntry("TorpedoThreatRangeMult", 1.5f, null,
                "An enemy with torpedoes counts as a torpedo threat when closer than its torpedo range x this");
            TransportsAfterEscort = cat.CreateEntry("TransportsAfterEscort", true, null,
                "Escort first, transports after: while enemy warships are present, guns do not fire at transports; only guns up to TransportLowCaliberMax may fire at a transport, and only when no enemy warship is within their range x TransportEscortRangeMult");
            TransportLowCaliberMax = cat.CreateEntry("TransportLowCaliberMax", 155f, null,
                "Escort first: largest gun calibre (mm) that may fire at transports while the escort is still present");
            TransportEscortRangeMult = cat.CreateEntry("TransportEscortRangeMult", 1.3f, null,
                "Escort first: a low-calibre gun may fire at a transport only when no enemy warship is within the gun's range x this");
            TransportEscortMemorySeconds = cat.CreateEntry("TransportEscortMemorySeconds", 30f, null,
                "Escort first: an enemy warship that went out of sight still counts as the escort for this many seconds");
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
            TorpedoTargetSizeRatio = cat.CreateEntry("TorpedoTargetSizeRatio", 1.5f, null,
                "Torpedo craft charge capital ships or any enemy at least this many times larger (tonnage) than themselves");
            MassAttackStrengthRatio = cat.CreateEntry("MassAttackStrengthRatio", 1.0f, null,
                "Massed torpedo attack when the enemy's combat tonnage in the battle exceeds own combat tonnage times this value");
            MassAttackPerTarget = cat.CreateEntry("MassAttackPerTarget", 3f, null,
                "Massed torpedo attack only when the side has at least this many torpedo craft per enemy target (capital ships and ships clearly larger than its torpedo craft)");
            WithdrawWhenOutmatched = cat.CreateEntry("WithdrawWhenOutmatched", true, null,
                "AI divisions withdraw when the enemy is stronger and there are too few torpedo craft for a massed attack - if they are faster than every visible enemy (or no enemy can penetrate them)");
            MainShipSizeRatio = cat.CreateEntry("MainShipSizeRatio", 0.75f, null,
                "A ship counts as a main ship of its side (escape when it cannot hurt the enemy) when its tonnage is at least this share of the largest own ship in the battle");
            CoreIndependence = cat.CreateEntry("CoreProvincesIndependence", true, null,
                "Occupied national core provinces of a great power (historical list) rise for independence after peace is made; the larger the nation's home population compared with the occupier's, the sooner (all nations, the player included)");
            CoreRevoltBonus = cat.CreateEntry("CoreRevoltBonus", 25f, null,
                "Extra revolt chance (game's own rebellion) of an occupied national core province in peacetime, scaled by home population: nation equal to the occupier -> 2/3 of it, a tenth of the occupier -> 1/6 (game's default for home provinces is about 2.5)");
            CoreSuppressIncomeMonths = cat.CreateEntry("CoreSuppressIncomeMonths", 1.0f, null, "Putting down a rising in a national core province costs this many months of income (x0.5-1.5 by the strength of the rising)");
            CoreSuppressUnrest = cat.CreateEntry("CoreSuppressUnrest", 6f, null, "Unrest added at home when a rising is put down (x0.5-1.5 by the strength of the rising)");
            CoreSuppressRelations = cat.CreateEntry("CoreSuppressRelations", 8f, null, "Attitude lost with every other great power when a rising is put down (neighbours of the province and its own nation: x1.5)");
            CoreSuppressAiMaxUnrest = cat.CreateEntry("CoreSuppressAiMaxUnrest", 40f, null, "The AI puts a rising down only if its unrest stays below this value (and it has three times the cost in cash)");
            CoreSuppressCalmMonths = cat.CreateEntry("CoreSuppressCalmMonths", 12f, null, "Months a province stays calm after a rising was put down (game's revolt cooldown)");
            CoreSuppressEscalation = cat.CreateEntry("CoreSuppressEscalation", 0.5f, null, "Every rising already put down in a province makes the next one dearer and angrier by this share (cost and unrest x1+0.5 per earlier rising); the calm that follows gets shorter (12 months, then 6, 4, 3)");
            CoreOccupationUnrest = cat.CreateEntry("CoreOccupationUnrest", 0.5f, null, "Unrest added each month at home for every occupied foreign core province in peacetime (x the strength of the occupied nation, more after risings put down)");
            CoreOccupationCost = cat.CreateEntry("CoreOccupationCost", 20f, null, "Garrison cost each month for every occupied foreign core province in peacetime, % of monthly income (x the strength of the occupied nation, more after risings put down)");
            CoreRevoltRampMonths = cat.CreateEntry("CoreRevoltRampMonths", 12f, null,
                "Months of peace over which the extra revolt chance builds up to its full value");
            SurrenderReturnToWinner = cat.CreateEntry("SurrenderedShipsToBattleWinner", true, null,
                "Surrendered ships go to the side that won the battle: a ship whose own side won returns to its owner (damaged) instead of being lost");
            CaptureDamage = cat.CreateEntry("CaptureDamage", 0.6f, null,
                "Structural damage (0-1) applied to a captured ship before it enters repair");
            MinorFleetToConqueror = cat.CreateEntry("MinorFleetToConqueror", true, null,
                "When a nation loses a province and has no other port to flee to, or is dissolved, its ships in the lost ports pass to the new controller of the port instead of being scrapped (1.11.4, all nations since 1.11.5)");
            NationalTech = cat.CreateEntry("NationalTechBonuses", true, null,
                "Research speed per nation and technology category from Mods/techBonuses.csv, for the player and the AI (1.11.7)");
            NationalTechScale = cat.CreateEntry("NationalTechScale", 1.0f, null,
                "Multiplier applied to every percentage in techBonuses.csv (1 = as in the file, 0.5 = half strength)");
            CheckUpdates = cat.CreateEntry("CheckForUpdates", true, null,
                "At game start fetch the VERSION file of the Iron Admiral GitHub repository (one HTTPS request to raw.githubusercontent.com) and show a message when a newer release exists (1.11.9)");
            UpdateDownload = cat.CreateEntry("UpdateDownloadButton", true, null,
                "Show a Download button in the update message; the download starts only after the player clicks it (1.11.11). false = message with a link only");
            // 1.11.487 (author 31.10 06:24: "the stable line needs automatic installation of updates too"; thunderspirit2014's log 30.10: the
            // update was found and the message with the Download button shown, but never clicked)
            UpdateAutoInstall = cat.CreateEntry("UpdateAutoInstall", true, null,
                "Install a newer Iron Admiral version automatically at game start (the Mods folder of the package, SHA-256 verified) - the game only asks for a restart afterwards; false = the message with the Download button");
            UpdateCheck.CreatePrefs(cat); // 1.11.108: stable / beta line
            EcoPrefs.Create(cat);
            EconomyUi.CreatePrefs(cat);
            SaveCompat.CreatePrefs(cat);
            ArmorDiag.CreatePrefs(cat);
            Fog.CreatePrefs(cat);
            NavalAviation.CreatePrefs(cat); // 1.11.407 (#85)
            NavalAviation.CreateSpottingPrefs(cat); // 1.11.410 (#85 step 2)
            Spotters.CreatePrefs(cat); // 1.11.417 (#85)
            AirRecon.CreatePrefs(cat); // 1.11.425 (#85)
            AirStrike.CreateCampaignPrefs(cat); // 1.11.426 (#85 step 3)
            AirStrike.CreateLandPrefs(cat); // 1.11.428 (#85 step 5)
            CarrierShips.CreatePrefs(cat); // 1.11.484 (#104 stage 2)
            Climate.CreatePrefs(cat); // 1.11.431
            CampaignTest.CreatePrefs(cat); // 1.11.434
            SteamShots.CreatePrefs(cat); // 1.11.436
            TechRank.CreatePrefs(cat); // 1.11.439 (1.11.454: each on its own line - the calls after the first comment never ran)
            AirOnly.CreatePrefs(cat); // 1.11.440 (#96)
            TurretArmorDefault.CreatePrefs(cat); // 1.11.441
            PlayerDivisions.CreatePrefs(cat); // 1.11.442
            WarScoreReset.CreatePrefs(cat); // 1.11.444
            DesignCleanup.CreatePrefs(cat); // 1.11.451
            SeaRepair.CreatePrefs(cat); // 1.11.453
            PenReference.CreatePrefs(cat); // 1.11.459
            Spectacle.CreatePrefs(cat); // 1.11.462
            SlowProbe.CreatePrefs(cat); // 1.11.463
RefitConfirm.CreatePrefs(cat); // 1.11.464
AirBudget.CreatePrefs(cat); // 1.11.467
SaveBackup.CreatePrefs(cat); // 1.11.477
Reparations.CreatePrefs(cat); // 1.11.482
            EraMap.CreatePrefs(cat); // 1.11.485
            Diagnostics.CreatePrefs(cat, Info.Version);
            ModDesigns.CreatePrefs(cat);
            Narrative.CreatePrefs(cat);
            CustomBattle.CreatePrefs(cat);
            Portless.CreatePrefs(cat);
            Dispatch.CreatePrefs(cat); TorpedoSettings.CreatePrefs(cat); TorpedoPriority.CreatePrefs(cat); // 1.11.251
            HistDesigner.CreatePrefs(cat);
            AutoTest.CreatePrefs(cat);
            HistPriority.CreatePrefs(cat);
            PlayerDesigns.CreatePrefs(cat); // 1.11.394
            DesignStats.CreatePrefs(cat); // 1.11.397
            Trade.CreatePrefs(cat);
            TorpedoSizeBest.CreatePrefs(cat); // 1.11.296 (#47)
            TorpedoTubesMin.CreatePrefs(cat); // 1.11.309 (#47, part 2)
            Intel.CreatePrefs(cat);
            Obliquity.CreatePrefs(cat); ArmorQuality.CreatePrefs(cat); // 1.11.250
            ContinuousMap.CreatePrefs(cat);
            ShipGenMemory.CreatePrefs(cat); // 1.11.99
            ShipGenSpeed.CreatePrefs(cat); // 1.11.196
            RangeTags.CreatePrefs(cat); // 1.11.206
            BarrelElevation.CreatePrefs(cat); // 1.11.209
            ThinPlatingHE.CreatePrefs(cat); // 1.11.211
            MountElevation.CreatePrefs(cat); // 1.11.216
            DesignTechFix.CreatePrefs(cat); // 1.11.223
            TechDiffusion.CreatePrefs(cat); // 1.11.228
            DivisionSpacing.CreatePrefs(cat); // 1.11.231
            HullCaliberCap.CreatePrefs(cat); // 1.11.233
            MapGhosts.CreatePrefs(cat); // 1.11.234
            Irredenta.CreatePrefs(cat); // 1.11.235
            ShipPartsCheck.CreatePrefs(cat); // 1.11.243
            BrokenDesigns.CreatePrefs(cat); // 1.11.308 (#32)
            PrizeTech.CreatePrefs(cat); // 1.11.333 (#50)
            SmokeGate.CreatePrefs(cat); // 1.11.341 (#22)
            TorpedoPincer.CreatePrefs(cat); // 1.11.343 (#71)
            PrizeScuttle.CreatePrefs(cat); // 1.11.351
            DesignerText.CreatePrefs(cat); // 1.11.357
            StackedGuns.CreatePrefs(cat); // 1.11.364 (#81)
            TransportSpeed.CreatePrefs(cat); TransportSpeed.CreateArmedPrefs(cat); // 1.11.365, 1.11.366 (#79)
            EnemyProfile.CreatePrefs(cat); // 1.11.368 (#84)
            AirStrike.CreatePrefs(cat); // 1.11.372 (#85)
            Thanks.CreatePrefs(cat); // 1.11.370
            BattleHour.CreatePrefs(cat); // 1.11.334 (#57)
            DesignRecorder.CreatePrefs(cat); // 1.11.310 (#64)
            DeckZoneFix.CreatePrefs(cat); // 1.11.317 (#59)
            UiScale.CreatePrefs(cat); // 1.11.100
            DesignRepair.CreatePrefs(cat); // 1.11.103
            PortForces.CreatePrefs(cat); // 1.11.120
            ForeignPorts.CreatePrefs(cat); // 1.11.134
            Successors.CreatePrefs(cat); Occupation.CreatePrefs(cat); BattleAi.CreateOrderPrefs(cat); ReShadeSetup.CreatePrefs(cat); Ballistics.CreatePrefs(cat); EraAccuracy.CreatePrefs(cat); HistoricalDamage.CreatePrefs(cat); EraAccuracy.CreateDispersionPrefs(cat); HistoricalGuns.CreatePrefs(cat); GunLab.CreatePrefs(cat); HistoricalWeights.CreatePrefs(cat); HistoricalParts.CreatePrefs(cat); // 1.11.151, 1.11.154, 1.11.155, 1.11.161 // 1.11.149 (1.11.146: Auto armour button removed)
            OtherMods.CreatePrefs(cat); OtherMods.Scan(); // 1.11.138
            LogShare.CreatePrefs(cat); // 1.11.139
            FleetOrders.CreatePrefs(cat); // 1.11.107
            Perf.CreatePrefs(cat); // 1.11.96 (after ArmorDiag: switches the diagnostics off once)
            ArmorDiag.ApplyHooks(HarmonyInstance);
            Diagnostics.RepairPreferences();
            ModDesigns.Install();
            SettingsUi.CreatePrefs(cat);
            try { Narrative.Apply(HarmonyInstance); } catch { }
            try { Portless.Apply(HarmonyInstance); } catch { }
            try { Irredenta.Apply(HarmonyInstance); } catch { } // 1.11.235
            try { SlowProbe.Install(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("[SlowProbe] " + e.Message); } // 1.11.463
try { RefitConfirm.Install(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("[RefitConfirm] " + e.Message); } // 1.11.464
try { Patch_AiTakeReparation.Install(HarmonyInstance); } catch { } // 1.11.483
            try { Scenarios.Apply(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("[Scenario] hooks not applied: " + e.Message); }
            try { Fog.Apply(HarmonyInstance); Fog.ReportPatches(); } catch (Exception e) { LoggerInstance.Warning("[Fog] hooks not applied: " + e.Message); }
            Doctrines.Load();
            TechBonuses.Load();
            UpdateCheck.Start();
            LoggerInstance.Msg($"1.11: ChargeApproachSeconds={ChargeApproachSeconds.Value} base {ChargeBaseExtra.Value} m, CapitalTorpedoCharge={CapitalCharge.Value}, CapitalCloseInWhenOutgunned={CapitalCloseIn.Value} (outranged x{OutrangedFactor.Value}, pen x{PenetrationFactor.Value}), PlayerScoutCharge={PlayerScoutCharge.Value}, TorpedoFriendlyLane={TorpedoFriendlyLane.Value} ({LaneHalfWidth.Value} m), SmartTargeting={SmartTargeting.Value}/AI {SmartTargetingAi.Value}, CrewLevelsRescale={CrewLevelsRescale.Value} (+{CrewPlusAmp.Value}/-{CrewMinusAmp.Value}, p={CrewCurvePower.Value}), SurrenderByTraining={SurrenderByTraining.Value} ({SurrenderGreen.Value}-{SurrenderElite.Value}), CrewPanic={Panic.Value} (rank<{PanicMaxRank.Value}, dmg>{PanicDamage.Value}, crew loss>{PanicCrewLoss.Value}, p={PanicChance.Value}), CaptureSurrenderedShips={CaptureSurrendered.Value} (damage {CaptureDamage.Value}), MinorFleetToConqueror={MinorFleetToConqueror.Value}");
            LoggerInstance.Msg($"Battle AI: LightShipTorpedoCharge={LightShipCharge.Value} (start {ChargeStartRange.Value} m, margin {ChargeTorpedoMargin.Value}, abort {ChargeAbortDamage.Value}), FightWhenCannotEscape={FightWhenCannotEscape.Value} (pursuit {PursuitRange.Value} m, margin {EscapeSpeedMargin.Value}), DetachSlowShips={DetachSlowShips.Value} (ratio {SlowShipSpeedRatio.Value}, cooldown {DetachCooldownSeconds.Value} s), FocusFire={FocusFire.Value} (min {FocusFireMinDamage.Value})");
            LoggerInstance.Msg($"Treasury crisis={FinancialCrisis.Value} (after {CrisisMonths.Value} months in debt; loan {LoanGrantMonths.Value} mo budget, repay {LoanRepayShare.Value:P0} x {LoanTermMonths.Value} mo; print {PrintGrantMonths.Value} mo, inflation {PrintInflation.Value}% -{InflationDecay.Value}/mo, unrest +{PrintUnrest.Value}; cuts {CutsMonths.Value} mo, maintenance -{CutsMaintenanceSaving.Value:P0}), BankruptBuildsCheapShips={BankruptNoCapitalShips.Value}");
            LoggerInstance.Msg(EcoPrefs.Summary());
            LoggerInstance.Msg($"Diplomacy attitude multiplier: positive={AttitudeMult.Value}, negative={NegAttitudeMult.Value}, FixStalledConstruction={FixStalledBuilding.Value}, EconomicCollapseGraceMonths={CollapseGraceMonths.Value}");
            LoggerInstance.Msg($"Balance-of-power events={BalanceEvents.Value} (ratio {BalanceRatio.Value}, chance {BalanceChance.Value}, {BalanceMonths.Value} months)");
            LoggerInstance.Msg($"Loaded {Info.Version}. CheckMinorNationThreat guard on. AutoShareDesigns={AutoShare.Value}, ShareToAllNations={AllNations.Value}, "
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
            Economy.MonthStart();          // 1.11.22: inflation, inflows, cuts, research, interest bookkeeping
            BalanceOfPower.CheckOncePerTurn();
            ConstructionWatch.CheckOncePerTurn();
            FinanceCrisis.CheckOncePerTurn();
            Economy.MonthEnd();
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
        private static readonly Dictionary<string, int> lastGrant = new Dictionary<string, int>();

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
                int month = 0; try { var d = cc.CurrentDate.AsDate(); month = d.Year * 12 + d.Month; } catch { }
                foreach (var p in ais)
                {
                    if (rng.NextDouble() >= DipGgFixesMod.BalanceChance.Value) continue;
                    float grant = p.Budget() * DipGgFixesMod.BalanceMonths.Value;
                    float cap = p.StateBudget() * 0.25f;
                    if (cap > 0f && grant > cap) grant = cap;
                    string key = p.data != null ? p.data.name : p.Pointer.ToString();
                    if (DipGgFixesMod.BalanceOnlySolvent != null && DipGgFixesMod.BalanceOnlySolvent.Value)
                    {
                        // 1.11.18: the 17 October log showed grants of 3 months of budget landing every turn or two on nations
                        // with a negative income (Britain -434M/month, USA -158M cash two turns after +1.08 bn). The AI budget
                        // routines scale tech/training/transport/shipyard spending with cash, so each grant inflated expenses,
                        // income went negative and BuildNewShips never passed its reserve gate (cash + income must exceed
                        // aiShipbuilding x revenue). Grants now go only to solvent nations, lift them just over that gate, and
                        // repeat for the same nation no more than once per cooldown.
                        int last; if (lastGrant.TryGetValue(key, out last) && month - last < DipGgFixesMod.BalanceCooldown.Value) continue;
                        float income = 0f, rev = 0f, k = 1.5f;
                        try { income = p.Income(); rev = p.Revenue(); var pe = p.GetAiPersonality(); if (pe != null && pe.aiShipbuilding > 0f) k = pe.aiShipbuilding; } catch { }
                        if (income < 0f) { MelonLogger.Msg($"[DIP GG Fixes] {(p.data != null ? p.data.nameUi : "?")}: no emergency appropriations - monthly income {income / 1e6f:F0}M is negative"); continue; }
                        float need = k * rev + rev - (p.cash + income);
                        if (need <= 0f) continue; // already able to order
                        if (grant > need) grant = need;
                    }
                    if (grant <= 0f) continue;
                    lastGrant[key] = month;
                    p.cash = p.cash + grant;
                    Economy.Inflow(p, grant, false, "balance-of-power grant");
                    string name = p.data != null ? p.data.nameUi : "?";
                    string text = $"{name}: emergency naval appropriations in response to {mainName}'s naval dominance (+{grant:N0}).";
                    MelonLogger.Msg("[DIP GG Fixes] " + text);
                    // 1.11.10: Ui.ReportImportant throws a NullReferenceException inside the game for these messages even with a
                    // ship of the nation attached (6 cases in the 2026-10-16 log after the 1.11.4 attempt). It is a player-ship
                    // report, not a world message; the grant is logged only.
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

            int written = 0, skipped = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var design in designs)
            {
                if (design.IsSharedDesign || design.isRefitDesign) continue;
                var hull = design.hull;
                if (hull == null || hull.data == null) continue;
                var allowed = hull.data.countriesx;
                Ship.Store probe = null;   // 1.11.449: serialised once per design for the file check

                foreach (var pd in majors)
                {
                    bool isOwn = main.data != null && pd.name == main.data.name;
                    if (!DipGgFixesMod.AllNations.Value && !isOwn) continue;
                    if (allowed != null && allowed.Count > 0 && !allowed.Contains(pd)) continue;

                    if (probe == null) { probe = design.ToStore(false); if (probe == null) break; }
                    var store = probe;
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
                    store = design.ToStore(false); if (store == null) continue;   // a fresh copy for the file written

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
            if (sw.ElapsedMilliseconds > 1000) MelonLogger.Msg($"[DIP GG Fixes] shared design export: {designs.Count} design(s), {written} written, {skipped} skipped in {sw.ElapsedMilliseconds} ms");
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
                // 1.11.374 (#84 step b): build mix fitted against the likely enemies (EnemyFitBuildMix)
                if (__instance != null && __result > 0f && !string.IsNullOrEmpty(shipType)) { try { if (__instance.isAi && __instance.isMajor) __result *= EnemyProfile.BuildFactor(__instance, shipType); } catch { } }
                if (DipGgFixesMod.CruisersWhenPoor == null || !DipGgFixesMod.CruisersWhenPoor.Value) return;
                if (__instance == null || __result <= 0f || string.IsNullOrEmpty(shipType)) return;
                bool big = shipType == "bb" || shipType == "bc";
                bool cruiser = shipType == "ca" || shipType == "cl";
                bool light = shipType == "dd" || shipType == "tb";
                if (!big && !cruiser && !light) return;
                if (!__instance.isAi || !__instance.isMajor) return;
                // 1.11.16: a bankrupt great power (cash below zero) or one under fleet cuts orders no capital ships at all
                // and leans on ca/cl/dd/tb, which the current balance makes worthwhile. Before: Britain at -188M kept
                // generating semi-dreadnoughts that failed in the autodesigner, so nothing was built.
                if (DipGgFixesMod.BankruptNoCapitalShips != null && DipGgFixesMod.BankruptNoCapitalShips.Value
                    && (__instance.cash < 0f || (Economy.Get(__instance)?.CutsMonths ?? 0) > 0))
                {
                    if (big) __result = 0f; else __result *= DipGgFixesMod.CruiserFactor.Value;
                    FinanceCrisis.LogBankruptOnce(__instance);
                    return;
                }
                if (light) return;
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
                if (Portless.KeepAlive(__instance)) { __result = false; return false; } // 1.11.72
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

        // 1.11.292 (#39): the game (and TweaksAndFixes' "All other allies declare war") sometimes calls AdjustAttitude for a pair
        // of nations without a relation record; the interop wrapper then throws a NullReferenceException that ends the caller's
        // loop (the remaining allies are skipped). A null relation is now skipped and logged; an exception thrown inside the
        // game's AdjustAttitude is logged with the pair and swallowed, so the caller goes on with the next nation.
        static int nullLogged, errLogged, bigLogged;
        [ThreadStatic] static Relation current; [ThreadStatic] static string currentInfo; [ThreadStatic] static float currentDelta;
        private static bool Prefix(Relation relation, ref float attitudeDelta, bool init, string info, bool force)
        {
            depth++;
            if (relation == null)
            {
                if (nullLogged++ < 20) MelonLogger.Msg($"[DIP GG Fixes][Diplomacy] AdjustAttitude called without a relation (delta {attitudeDelta:F1}, '{info}') - skipped");
                return false;
            }
            if (depth == 1) { current = relation; currentInfo = info; currentDelta = attitudeDelta; }
            if (depth > 1 || init || force || TafPassThrough()) return true;
            var m = attitudeDelta < 0f ? DipGgFixesMod.NegAttitudeMult : DipGgFixesMod.AttitudeMult;
            if (m == null) return true;
            // 1.11.437 (#98, A Simple Tako 30.10: refusing the alliance event, -50, took 99 straight to war - x4 made it -200): the
            // multipliers are for the small month-to-month swings; a one-off change of DiplomacyUnscaledFrom or more (an event
            // answer, a declaration) keeps its own size
            var big = DipGgFixesMod.UnscaledFrom; if (big != null && big.Value > 0f && Math.Abs(attitudeDelta) >= big.Value) { if (bigLogged++ < 20) MelonLogger.Msg($"[DIP GG Fixes][Diplomacy] one-off change {attitudeDelta:F0} ('{info}') not multiplied"); return true; }
            attitudeDelta *= m.Value;
            return true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (depth > 0) depth--;
            if (__exception == null) { if (depth == 0) current = null; return null; }
            if (errLogged++ < 20)
            {
                string pair = "?", state = "";
                try { var r = current; if (r != null) { pair = r.ToString(); state = $", attitude now {r.attitude:F0}, war {r.isWar}"; } } catch { }
                MelonLogger.Warning($"[DIP GG Fixes][Diplomacy] AdjustAttitude failed inside the game for {pair} (delta {currentDelta:F1}, '{currentInfo}'{state}) - {__exception.GetType().Name}: {__exception.Message.Split('\n')[0]}; the caller continues");
            }
            if (depth == 0) current = null;
            return null;
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
        // 1.11.88: period and behaviour (see battleDoctrines.csv header)
        public int yearFrom = 0, yearTo = 9999;
        public bool lineFirst = true;      // battle line engages first, flotillas attack once the lines are engaged (WI 1944, 12124)
        public int night = 0;              // -1 avoids night action, 0 accepts it, 1 seeks it (night torpedo attacks)
        public float heavyProtect = 0f;    // 0..1: how strongly the battle line is kept out of an unfavourable fight
        public bool turnAwayAlways = true; // true: always turn away from torpedoes; false: hold course when the enemy line is nearly beaten
        public bool flyingSquadron = false;// fast cruisers act as a separate manoeuvre group against the enemy flank
        public float commandDelay = 0f;    // extra seconds between AI division decisions (centralised command)
        public bool hitAndRun = false;     // light forces withdraw after their attack instead of fighting the enemy light forces
        public int maxPerTarget = 3;       // most ships firing main guns at one target (WI 1944, 12165)
        public string source = "";
    }
    internal static class Doctrines
    {
        internal static readonly Dictionary<string, Doctrine> ByNation = new Dictionary<string, Doctrine>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, List<Doctrine>> ByNationYears = new Dictionary<string, List<Doctrine>>(StringComparer.OrdinalIgnoreCase);
        internal static Doctrine Default = new Doctrine();
        internal static bool Loaded;

        internal static void Load()
        {
            try
            {
                string path = Path.Combine(MelonUtils.GameDirectory, "Mods", "battleDoctrines.csv");
                if (!File.Exists(path)) { MelonLogger.Msg("[DIP GG Fixes] battleDoctrines.csv not found - global battle AI settings apply to all nations"); return; }
                int n = 0;
                // 1.11.88: columns are read by header name, so old files (nation,distanceMult,chargeStartRange,abortDamage,escapeMargin,comment) still load
                var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var c = line.Split(',');
                    if (line.StartsWith("nation,")) { col.Clear(); for (int i = 0; i < c.Length; i++) col[c[i].Trim()] = i; continue; }
                    if (c.Length < 5) continue;
                    Func<string, string> G = k => { int i; return col.TryGetValue(k, out i) && i < c.Length ? c[i].Trim() : null; };
                    Func<string, float, float> Fv = (k, def) => { var v = G(k); return v == null || v.Length == 0 ? def : F(v, def); };
                    var d = new Doctrine { nation = c[0].Trim().ToLowerInvariant() };
                    if (col.Count == 0) { d.distanceMult = F(c[1], 1f); d.chargeStart = F(c[2], 12000f); d.abortDamage = F(c[3], 0.6f); d.escapeMargin = F(c[4], 1f); }
                    else
                    {
                        d.distanceMult = Fv("distanceMult", 1f); d.chargeStart = Fv("chargeStartRange", 12000f); d.abortDamage = Fv("abortDamage", 0.6f); d.escapeMargin = Fv("escapeMargin", 1f);
                        d.yearFrom = (int)Fv("yearFrom", 0f); d.yearTo = (int)Fv("yearTo", 9999f);
                        d.lineFirst = Fv("lineFirst", 1f) > 0.5f; d.night = (int)Math.Round(Fv("night", 0f)); d.heavyProtect = UnityEngine.Mathf.Clamp01(Fv("heavyProtect", 0f));
                        d.turnAwayAlways = Fv("turnAwayAlways", 1f) > 0.5f; d.flyingSquadron = Fv("flyingSquadron", 0f) > 0.5f; d.commandDelay = Math.Max(0f, Fv("commandDelay", 0f));
                        d.hitAndRun = Fv("hitAndRun", 0f) > 0.5f; d.maxPerTarget = Math.Max(1, (int)Fv("maxPerTarget", 3f)); d.source = G("source") ?? "";
                    }
                    List<Doctrine> lst; if (!ByNationYears.TryGetValue(d.nation, out lst)) { lst = new List<Doctrine>(); ByNationYears[d.nation] = lst; }
                    lst.Add(d);
                    if (!ByNation.ContainsKey(d.nation)) ByNation[d.nation] = d;
                    n++;
                    if (d.nation == "default" && d.yearFrom <= 0 && d.yearTo >= 9999) Default = d;
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
                // 1.11.88: the row whose year range contains the current year; nation rows first, then default rows
                int y = Fog.Year();
                List<Doctrine> lst;
                if (name != null && ByNationYears.TryGetValue(name, out lst)) foreach (var x in lst) if (y >= x.yearFrom && y <= x.yearTo) return x;
                if (ByNationYears.TryGetValue("default", out lst)) foreach (var x in lst) if (y >= x.yearFrom && y <= x.yearTo) return x;
                if (name != null && ByNation.TryGetValue(name, out var d)) return d;
            }
            catch { }
            return Default;
        }
    }

    // ---------------------------------------------------------------- battle AI
    internal static partial class BattleAi
    {
        [ThreadStatic] internal static float CurrentDistanceMult;
        internal static float ChargeStart(Doctrine d) { return d != null ? d.chargeStart : DipGgFixesMod.ChargeStartRange.Value; }
        internal static float AbortDamage(Doctrine d) { return d != null ? d.abortDamage : DipGgFixesMod.ChargeAbortDamage.Value; }
        internal static float EscapeMargin(Doctrine d) { return d != null ? d.escapeMargin : DipGgFixesMod.EscapeSpeedMargin.Value; }

        internal class DivState { public float nextThink; public float nextSightLog; public bool charging; public bool closing; public float closeResumeAt; public IntPtr target; public string name; public string reason; public bool fighting; public float nextDetach; public float nextLeg; public int legSign; public bool zigLogged; public bool reachLogged; public bool hopelessLogged; public float hopelessSince = -1f; public bool hopelessWaitLogged; public bool hopelessSoftLogged; public float evadeUntil; public UnityEngine.Vector3 evadeDir; public bool massLogged; public IntPtr ramTarget; public bool capEscape; public bool scatterLogged; public bool escapeLogged;
            // 1.11.86
            public IntPtr playerPtr; public UnityEngine.Vector3 pos; public float activeAt; public bool reloading; public float reloadStart; public float firstInRange; public bool returnAttack; public bool supporting; public IntPtr supportTarget; public float nextSmoke; public bool covering; public bool holding; public float holdAt; public int holdSide = 1; public bool noReloads; public float approachDmg = -1f; public bool approachSmokeLogged; }
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
            try { var g = MainBatteryGun(s); return g != null ? g.caliber : 0f; } catch { return 0f; }
        }
        // 1.11.284 (#46, merged #44): the gun of the largest calibre among the ship's parts; the game's mainGunsGroup falls back to a
        // secondary gun when the main turrets have no mount (part check, #32), so it is only the fallback
        internal static PartData MainBatteryGun(Ship s)
        {
            if (s == null) return null;
            PartData best = null;
            try
            {
                var parts = s.parts;
                if (parts != null)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var d = parts[i] != null ? parts[i].data : null;
                        if (d == null || !d.isGun) continue;
                        if (best == null || d.caliber > best.caliber) best = d;
                    }
            }
            catch { }
            if (best == null) { try { best = s.mainGunsGroup; } catch { } }
            return best;
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
                // 1.11.284 (#46, merged #44): the main battery is the largest-calibre gun on the ship, also when its turrets failed the mount
                // check (mainGunsGroup then pointed at a secondary gun - Rodney 14 in rated with 203 mm, 25.10)
                var g = MainBatteryGun(shooter); if (g == null) return false;
                var noShell = new Il2CppSystem.Nullable<Ship.ShellType>(); var noVec = new Il2CppSystem.Nullable<UnityEngine.Vector3>();
                // 1.11.284 (#46): the AI's "can I penetrate" uses the same physics as the hit - the arriving AP shell's striking velocity and
                // angle of fall at this distance, the physical belt law and the shell's AP quality - not the game's reading (which held the
                // physical row only for guns already seen in the battle, and otherwise the game table: 0 mm for 5"/6" at 11 km)
                float physPen = -1f; bool physBeyond = false;
                if (PhysPen.On != null && PhysPen.On.Value && PhysGun.Enabled)
                {
                    try
                    {
                        var pg = PhysGun.Get(g, shooter, Ship.ShellType.Ap);
                        if (pg.Ok)
                        {
                            float vs, fall, tt;
                            Ballistics.Impact(pg.Mv, pg.CalMm / 25.4f, pg.Kg, Ballistics.CdOf(shooter), UnityEngine.Mathf.Max(50f, dist), MountElevation.Elev(System.Math.Max(1, pg.Grade)), out vs, out fall, out tt, out physBeyond);
                            physPen = physBeyond ? 0f : PhysPen.BeltPen(pg.Kg, vs, fall, pg.Q, pg.CalMm);
                        }
                    }
                    catch (Exception e) { if (penLogged++ < 3) Log("physical penetration estimate failed: " + e.Message); physPen = -1f; }
                }
                if (physPen >= 0f)
                {
                    pen = physPen;
                    Obliquity.Suspend = true; try { belt = target.GetEffectiveArmor(Ship.A.Belt, null, g, shooter, false); } catch { belt = 0f; } finally { Obliquity.Suspend = false; }
                    if (penLogged < 8)
                    {
                        penLogged++;
                        float gameBelt = 0f; try { gameBelt = Ship.GetPenetration(g, shooter, dist, true, noShell, noVec); } catch { }
                        Log($"penetration check (physics): {shooter.name} ({g.caliber:0} mm main battery) vs {target.name} at {dist:0} m: belt pen {physPen:0.0} mm{(physBeyond ? " (beyond the reach of the mount)" : "")} (game reading {gameBelt:0.0}) vs effective belt {belt:0.0} mm (armour table {beltRaw:0.0})");
                    }
                    if (physBeyond) return false; // out of reach: the range rules decide, not the penetration
                    if (belt <= 0f) return false;
                    return pen < belt * DipGgFixesMod.PenetrationFactor.Value;
                }
                // 1.11.1 log: the bool=false variant GREW with distance (plunging/deck penetration); belt penetration is the bool=true variant.
                // Units (1.11.8): GetPenetration returns mm. Battle log 2026-10-16: a 229 mm gun of 1901 at 19.9 km gave 95.9 (deck
                // variant 58.5) against penetration.csv table values of 182.8 / 107.1 mm for 9" at 20 km - the same ratio (~0.53) for an
                // early-generation gun; read as cm (x10, 1.11.3) it would have been 959 mm, five times the table, and the "cannot penetrate"
                // branch never fired.
                float penBelt = Ship.GetPenetration(g, shooter, dist, true, noShell, noVec);
                pen = penBelt;
                Obliquity.Suspend = true; try { belt = target.GetEffectiveArmor(Ship.A.Belt, null, g, shooter, false); } catch { belt = 0f; } finally { Obliquity.Suspend = false; }
                if (penLogged < 8)
                {
                    penLogged++;
                    float penDeck = 0f; try { penDeck = Ship.GetPenetration(g, shooter, dist, false, noShell, noVec); } catch { }
                    Log($"penetration check: {shooter.name} ({MainCaliber(shooter):0} mm) vs {target.name} at {dist:0} m: belt pen {penBelt:0.0} mm (deck variant {penDeck:0.0}) vs effective belt {belt:0.0} mm (armour table {beltRaw:0.0})");
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
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered || !TorpedoWorthy(leader, e)) continue;
                if (!e.IsVisibleFor(leader)) continue;
                float d = Dist(leader, e);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // 1.11.47: massed torpedo attack - the enemy has more battleships/battlecruisers than this side
        static readonly Dictionary<IntPtr, float> fleetNext = new Dictionary<IntPtr, float>();
        static readonly Dictionary<IntPtr, bool> fleetOn = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<IntPtr, bool> fleetHopeless = new Dictionary<IntPtr, bool>();
        internal static bool Hopeless(Player player, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies) { if (player == null || battle == null) return false; MassAttack(player, battle, enemies); bool h; return fleetHopeless.TryGetValue(player.Pointer, out h) && h; }
        static bool IsLine(Ship s) { var t = s != null ? s.shipType : null; return t != null && (t.name == "bb" || t.name == "bc"); }
        // 1.11.50: AI tactics no longer depend on battleships being present - ship size relative to the fleets in the battle decides
        static readonly Dictionary<IntPtr, float> tonCache = new Dictionary<IntPtr, float>(); static IntPtr tonBattle;
        internal static float Ton(Ship s)
        {
            if (s == null) return 0f; float t;
            IntPtr bp = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) bp = cb.Pointer; } catch { }
            if (bp != tonBattle) { tonBattle = bp; tonCache.Clear(); }
            if (tonCache.TryGetValue(s.Pointer, out t)) return t;
            t = 0f; try { t = s.Tonnage(); } catch { }
            tonCache[s.Pointer] = t; return t;
        }
        static bool Alive(Ship x) { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; }
        static bool Combat(Ship x) { var t = x.shipType; return t != null && t.name != "tr"; }
        // torpedo target: a capital ship, or any enemy clearly larger than the attacker
        internal static bool TorpedoWorthy(Ship leader, Ship e)
        {
            // 1.11.58: fog of war - the AI judges an unidentified enemy by its reported type
            if (Fog.AiActive && leader != null && leader.player != null && !leader.player.isMain)
            {
                var rep = Fog.PType(leader.player, e); if (rep == null) return false;
                if (Fog.PCapital(rep)) return true;
                float tl2 = Ton(leader), te2 = Fog.PTon(leader.player, e, Ton(e));
                if (te2 >= 6000f) return true; // 1.11.204
                return tl2 > 0f && te2 >= tl2 * DipGgFixesMod.TorpedoTargetSizeRatio.Value;
            }
            if (IsCapital(e)) return true;
            float tl = Ton(leader), te = Ton(e);
            if (te >= 6000f) return true; // 1.11.204: anything of cruiser size is worth a torpedo (24.10: a 12,400 t light cruiser was "a small target" for a 13,000 t heavy cruiser)
            return tl > 0f && te >= tl * DipGgFixesMod.TorpedoTargetSizeRatio.Value;
        }
        // 1.11.58: enemy strength as the side sees it - ships never seen do not count, unidentified ones by their reported type
        static float PerceivedStrength(Player obs, Il2CppSystem.Collections.Generic.List<Ship> l)
        {
            float t = 0f; if (l == null) return 0f;
            for (int i = 0; i < l.Count; i++) { var x = l[i]; if (Alive(x) && Combat(x)) t += Fog.PTon(obs, x, Ton(x)); }
            return t;
        }
        static float Strength(Il2CppSystem.Collections.Generic.List<Ship> l)
        {
            float t = 0f; if (l == null) return 0f;
            for (int i = 0; i < l.Count; i++) { var x = l[i]; if (Alive(x) && Combat(x)) t += Ton(x); }
            return t;
        }
        // main ship of its side: not a light ship and close in size to the largest ship of its side still in the battle
        static bool IsMainShip(Ship s, CampaignBattle battle)
        {
            if (s == null || IsLight(s)) return false;
            if (IsLine(s)) return true;
            float mx = 0f;
            try { var own = battle != null ? battle.ShipsOf(s.player, true) : null; if (own != null) for (int i = 0; i < own.Count; i++) { var x = own[i]; if (Alive(x) && Combat(x)) mx = UnityEngine.Mathf.Max(mx, Ton(x)); } } catch { }
            return mx > 0f && Ton(s) >= mx * DipGgFixesMod.MainShipSizeRatio.Value;
        }
        static int CountLine(Il2CppSystem.Collections.Generic.List<Ship> l)
        {
            int n = 0; if (l == null) return 0;
            for (int i = 0; i < l.Count; i++) { var x = l[i]; if (x != null && !x.isSinking && !x.isDead && !x.IsSurrendered && IsLine(x)) n++; }
            return n;
        }
        internal static bool MassAttack(Player player, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            if (!DipGgFixesMod.FleetTorpedoAttack.Value || player == null) return false;
            if (EscapeMission(player, battle)) return false;
            float now = UnityEngine.Time.time; float nx; bool on;
            if (fleetNext.TryGetValue(player.Pointer, out nx) && now < nx && fleetOn.TryGetValue(player.Pointer, out on)) return on;
            Il2CppSystem.Collections.Generic.List<Ship> mine = null; try { mine = battle.ShipsOf(player, true); } catch { }
            float own = Strength(mine);
            bool fog = Fog.AiActive && !player.isMain;
            float foe = fog ? PerceivedStrength(player, enemies) : Strength(enemies);
            // 1.11.51: the attack must be big enough for part of it to get through - torpedo craft counted against their targets
            int attackers = 0; float minTon = float.MaxValue;
            if (mine != null) for (int i = 0; i < mine.Count; i++) { var x = mine[i]; if (Alive(x) && IsLight(x) && x.haveTorpedoes) { attackers++; minTon = UnityEngine.Mathf.Min(minTon, Ton(x)); } }
            int targets = 0;
            if (attackers > 0) for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || !Combat(e)) continue;
                if (fog) { var rep = Fog.PType(player, e); if (rep == null) continue; if (Fog.PCapital(rep) || Fog.PTon(player, e, Ton(e)) >= minTon * DipGgFixesMod.TorpedoTargetSizeRatio.Value) targets++; }
                else if (IsCapital(e) || Ton(e) >= minTon * DipGgFixesMod.TorpedoTargetSizeRatio.Value) targets++;
            }
            bool stronger = foe > own * DipGgFixesMod.MassAttackStrengthRatio.Value;
            bool enough = targets > 0 && attackers >= DipGgFixesMod.MassAttackPerTarget.Value * targets;
            bool was; fleetOn.TryGetValue(player.Pointer, out was);
            // 1.11.91 (War Instructions 1944, 12298: an inferior battle line first weakens the enemy with torpedoes - delay action):
            // with at least one torpedo craft per target the attack goes in even when it is short of the full massed strength
            bool delay = stronger && !enough && targets > 0 && attackers >= targets;
            on = stronger && (enough || delay);
            bool hopeless = stronger && !enough && !delay;
            bool wasH; fleetHopeless.TryGetValue(player.Pointer, out wasH);
            if (on != was || hopeless != wasH) Log($"massed torpedo attack {(on ? "ON" : "off")} for {player.data.name}: enemy combat tonnage {foe:0} t vs own {own:0} t, {attackers} torpedo craft vs {targets} targets (needed {DipGgFixesMod.MassAttackPerTarget.Value} per target){(hopeless ? " - too few to get through, ships faster than the enemy withdraw" : on && !enough ? " - short of the full strength: delaying attack with at least one craft per target (War Instructions 1944, 12298)" : "")}");
            fleetOn[player.Pointer] = on; fleetHopeless[player.Pointer] = hopeless; fleetNext[player.Pointer] = now + 5f;
            return on;
        }
        // 1.11.47: ramming - steer into the nearest enemy ship within RamRange (aim a little ahead of it)
        internal static bool TryRam(Ship leader, Division div, DivState st, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            if (!DipGgFixesMod.Ramming.Value) return false;
            Ship best = null; float bd = DipGgFixesMod.RamRange.Value;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (e == null || e.isSinking || e.isDead || e.IsSurrendered || !e.IsVisibleFor(leader)) continue;
                float d = Dist(leader, e); if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return false;
            var tt = best.transform; if (tt == null) return false;
            var f = tt.forward; f.y = 0f;
            div.MoveTo(tt.position + f.normalized * UnityEngine.Mathf.Min(bd * 0.5f, 600f));
            if (st.ramTarget != best.Pointer) { st.ramTarget = best.Pointer; Log($"{st.name} ({leader.shipType.name}) rams {best.name} ({best.shipType.name}) at {bd:0} m"); }
            Why(st, "ramming");
            return true;
        }
        // 1.11.47: can a capital division survive to the end of the battle if it leaves - faster than every visible enemy, or armour holds
        static bool CanSurvive(Ship leader, Division div, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, out string why)
        {
            float own = DivSpeed(div, leader); float fastest = 0f; bool armour = true; int seen = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (e == null || e.isSinking || e.isDead || e.IsSurrendered || !e.IsVisibleFor(leader)) continue;
                seen++; float v = Speed(e); if (v > fastest) fastest = v;
                if (armour) { float pen, belt; float d = UnityEngine.Mathf.Max(3000f, Dist(leader, e) * 0.5f); if (!CannotPenetrate(e, leader, d, out pen, out belt)) armour = false; }
            }
            bool faster = own > fastest * EscapeMargin(doc);
            // 1.11.294 (#36): the text names why leaving is safe, not why the ship leaves (read as "withdraws because nobody can hurt it")
            why = faster ? $"can get away: faster than every visible enemy ({own:0.0} vs {fastest:0.0})" : armour ? $"can get away: none of the {seen} visible enemy ship(s) can penetrate its belt" : "";
            return seen > 0 && (faster || armour);
        }

        // 1.11.49: escape missions - the defending side's task is to get away
        internal static bool EscapeMission(Player player, CampaignBattle battle)
        {
            try
            {
                if (!DipGgFixesMod.EscapeMissionLogic.Value || battle == null || player == null) return false;
                var t = battle.Type; if (t == null) return false;
                string n = t.name; if (string.IsNullOrEmpty(n)) return false;
                bool listed = false;
                foreach (var x in DipGgFixesMod.EscapeBattleTypes.Value.Split(',')) if (x.Trim().Length > 0 && string.Equals(x.Trim(), n, StringComparison.OrdinalIgnoreCase)) listed = true;
                return listed && battle.IsDefender(player);
            }
            catch { return false; }
        }
        // escape mission logic for one AI division; returns true when it took over the division
        static bool EscapeLogic(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            float own = DivSpeed(div, leader); float fastest = 0f; Ship pursuer = null;
            var c = UnityEngine.Vector3.zero; int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                c += e.transform.position; n++;
                if (!e.IsVisibleFor(leader) || Dist(leader, e) > DipGgFixesMod.PursuitRange.Value) continue;
                float v = Speed(e); if (v > fastest) { fastest = v; pursuer = e; }
            }
            if (n == 0) return false;
            var lp = leader.transform.position; var away = lp - c / n; away.y = 0f; if (away.sqrMagnitude < 1f) away = leader.transform.forward; away.Normalize();
            if (pursuer == null || own > fastest * EscapeMargin(doc))
            {
                if (!div.isRetreating && !div.retreat) div.Retreat(true);
                if (!st.escapeLogged) { st.escapeLogged = true; Log($"{st.name} ({leader.shipType.name}): escape mission - {(pursuer == null ? "no pursuer in range" : $"faster than {pursuer.name} ({own:0.0} vs {fastest:0.0})")}, retreats instead of attacking"); }
                st.fighting = false; st.charging = false; st.closing = false;
                Why(st, "escape mission - retreating"); return true;
            }
            // cannot outrun: split the division and flee in different directions
            var ships = div.ships;
            if (ships != null && ships.Count > 1)
            {
                var il = new Il2CppSystem.Collections.Generic.List<Ship>(); var names = new List<string>();
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (s == null || s.isSinking || s.isDead || s.Pointer == leader.Pointer || !s.CanDetach()) continue; il.Add(s); names.Add(s.name); }
                if (il.Count > 0)
                {
                    foreach (var s in il) { var one = new Il2CppSystem.Collections.Generic.List<Ship>(); one.Add(s); try { Ship.DetachShips(one); } catch { } }
                    Log($"{st.name} ({leader.shipType.name}): escape mission - cannot outrun {pursuer.name} ({own:0.0} vs {fastest:0.0}), the division scatters: {string.Join(", ", names)} flee on their own in different directions");
                }
            }
            // each ship flees on its own bearing, fanned out from the direction away from the enemy
            int h = (int)((leader.Pointer.ToInt64() >> 4) & 0x7fffffff);
            float ang = ((h % 7) - 3) * 25f; // -75..+75 deg
            var dir = UnityEngine.Quaternion.Euler(0f, ang, 0f) * away;
            if (div.isRetreating || div.retreat) div.Retreat(false);
            div.MoveTo(lp + dir * 6000f);
            if (!st.scatterLogged) { st.scatterLogged = true; Log($"{st.name} ({leader.shipType.name}): escape mission - flees alone, {ang:+0;-0;0} deg off the direction away from the enemy"); }
            st.fighting = false; st.charging = false; st.closing = false;
            Why(st, "escape mission - scattering"); return true;
        }

        // Think for AI division leaders (from Ship.AILogic) and for player division leaders (from the per-ship tick).
        internal static void Think(Ship leader) { Think(leader, false); }
        internal static void Think(Ship leader, bool playerSide, bool autonomous = false) // 1.11.154: autonomous = player division under the "Free action" order
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
            st.playerPtr = player.Pointer; st.activeAt = now; try { st.pos = leader.transform.position; } catch { }
            // 1.11.88: centralised command (doctrine) - AI divisions react more slowly
            if (!playerSide && doc != null && doc.commandDelay > 0f) st.nextThink += doc.commandDelay;

            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (battle == null) { Why(st, "no CurrentBattle"); return; }
            var enemies = battle.ShipsOf(battle.OppositePlayer(player), true);
            if (enemies == null) { Why(st, "no enemy list"); return; }
            ArmorDiag.Probe(leader, enemies);

            if (playerSide && !autonomous)
            {
                // player: only the scout torpedo charge (manual orders stay in force otherwise)
                if (!DipGgFixesMod.PlayerScoutCharge.Value || !light || !leader.haveTorpedoes) return;
                bool scout = false; try { scout = div.IsScout(); } catch { }
                if (!st.charging && !scout) { Why(st, "not a scout division"); return; }
                TorpedoCharge(leader, div, st, doc, enemies, true);
                return;
            }

            // 1.11.49: escape missions take precedence over every other AI rule
            if (EscapeMission(player, battle) && EscapeLogic(leader, div, st, doc, enemies)) return;
            // 1.11.51: outnumbered and too few torpedo craft for a massed attack - divisions faster than the enemy withdraw
            if (DipGgFixesMod.WithdrawWhenOutmatched.Value && !st.capEscape && Hopeless(player, battle, enemies))
            {
                string sw;
                if (CanSurvive(leader, div, doc, enemies, out sw))
                {
                    st.capEscape = true; st.charging = false; st.closing = false; div.Retreat(true);
                    Log($"{st.name} ({leader.shipType.name}): enemy force too strong and too few torpedo craft for a massed attack - withdraws ({sw})");
                    Why(st, "withdrawing - outmatched"); return;
                }
            }

            // 1.11.345 (#22, player discussion 26.10: "running out of ammunition is a legitimate reason for an AI ship to withdraw"):
            // a division whose main battery has no shells left on any ship (1.11.346; 1.11.345: under 15 % of all gun ammunition) and
            // that has no loaded torpedo tubes withdraws
            if (DipGgFixesMod.AiWithdrawLowAmmo != null && DipGgFixesMod.AiWithdrawLowAmmo.Value && !st.capEscape && LowAmmo(div, leader, out float ammoAvg))
            {
                float tubes = 0f; try { tubes = leader.haveTorpedoes ? LoadedTubes(div, leader) : 0f; } catch { }
                if (tubes <= 0f)
                {
                    st.capEscape = true; st.charging = false; st.closing = false; div.Retreat(true);
                    Log($"{st.name} ({leader.shipType.name}): main battery out of ammunition (0 shells on every ship) and no loaded torpedo tubes - withdraws");
                    Why(st, "withdrawing - out of ammunition"); return;
                }
            }

            // --- rule for every AI division: ships that slow the division down leave it and retreat alone
            // 1.11.339 (#71, player report and test 28.10 10:05: "CL Dresden: leader is slow: fast ships detached" - a lightly damaged
            // leader split the division in the middle of its torpedo run and the new division started over): a division in a torpedo
            // attack or turning away to reload keeps together; slow ships are separated before or after the attack
            if (DipGgFixesMod.DetachSlowShips.Value && now >= st.nextDetach && !st.charging && !st.reloading && !st.returnAttack && TryDetachSlow(div, leader, st)) { st.nextDetach = now + DipGgFixesMod.DetachCooldownSeconds.Value; return; }

            // 1.11.86: the attack has failed and the enemy is stronger - the most valuable ships withdraw, the cheaper ones cover them
            if (CoverRetreat(leader, div, st, doc, enemies, battle, player)) return;

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
                if (cannotEscape && st.capEscape && retreating)
                {
                    string sw; if (CanSurvive(leader, div, doc, enemies, out sw)) { Why(st, "escaping - " + sw); return; }
                }
                if (cannotEscape)
                {
                    if (retreating) div.Retreat(false);
                    if (!st.fighting) { st.fighting = true; Log($"{st.name} ({leader.shipType.name}) cannot outrun {pursuer.name} ({pursuer.shipType.name}, {pv:0.0} vs own {own:0.0} speed units, {pd:0} m) - retreat cancelled, fighting"); }
                    if (!leader.haveTorpedoes) { if (light && TryRam(leader, div, st, enemies)) return; Why(st, "fighting (cannot escape)"); return; } // gun ships: the game's own combat AI takes over
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

            // 1.11.44: turn away from sighted torpedo tracks (not while charging - a charging flotilla presses on)
            if (!st.charging && DipGgFixesMod.TorpedoTurnAway.Value && !HoldCourseThroughTorpedoes(player, battle, enemies, doc))
            {
                if (now < st.evadeUntil) { try { div.MoveTo(leader.transform.position + st.evadeDir * 3000f); } catch { } Why(st, "turning away from torpedoes"); return; }
                if (TorpedoEvasion.Check(leader, div, st, player, battle, enemies)) return;
            }

            // 1.11.88: cruiser roles (heavy cruisers always, light cruisers when the side has destroyers/torpedo boats)
            if (CruiserRole(leader, div, st, doc, enemies, battle, player)) return;

            if (light)
            {
                if (!leader.haveTorpedoes) { if ((st.fighting || MassAttack(player, battle, enemies)) && TryRam(leader, div, st, enemies)) return; return; }
                if (!DipGgFixesMod.LightShipCharge.Value) return;
                TorpedoCharge(leader, div, st, doc, enemies, false);
                return;
            }
            if (capital) CapitalLogic(leader, div, st, doc, enemies);
        }

        internal static class TorpedoEvasion
        {
            static readonly Dictionary<IntPtr, IntPtr> owner = new Dictionary<IntPtr, IntPtr>();
            static int errors;
            // owner of a torpedo = player of the ship nearest to its launch point (cached per torpedo)
            static IntPtr Owner(Torpedo t, Il2CppSystem.Collections.Generic.List<Ship> a, Il2CppSystem.Collections.Generic.List<Ship> b)
            {
                IntPtr o;
                if (owner.TryGetValue(t.Pointer, out o)) return o;
                var start = t.torpedoStart; float best = float.MaxValue; Ship near = null;
                foreach (var list in new[] { a, b })
                {
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++) { var s = list[i]; if (s == null) continue; float d = (s.transform.position - start).sqrMagnitude; if (d < best) { best = d; near = s; } }
                }
                o = near != null && near.player != null ? near.player.Pointer : IntPtr.Zero;
                if (owner.Count > 2000) owner.Clear();
                owner[t.Pointer] = o; return o;
            }
            // torpedo speed (m/s) from its own track between checks; 20 m/s (~40 kn) until measured
            class TTrack { public UnityEngine.Vector3 pos; public float t, v; }
            static readonly Dictionary<IntPtr, TTrack> ttracks = new Dictionary<IntPtr, TTrack>();
            static float TorpedoSpeed(Torpedo t, UnityEngine.Vector3 p)
            {
                float now = UnityEngine.Time.time; p.y = 0f;
                if (ttracks.Count > 3000) ttracks.Clear();
                TTrack k; if (!ttracks.TryGetValue(t.Pointer, out k)) { ttracks[t.Pointer] = new TTrack { pos = p, t = now, v = 20f }; return 20f; }
                float dt = now - k.t; if (dt >= 0.5f) { float v = (p - k.pos).magnitude / dt; if (v > 3f && v < 60f) k.v = v; k.pos = p; k.t = now; }
                return k.v;
            }
            internal static bool Check(Ship leader, Division div, DivState st, Player player, CampaignBattle battle, Il2CppSystem.Collections.Generic.List<Ship> enemies)
            {
                try
                {
                    var all = Torpedo.AllTorpedo; if (all == null || all.Count == 0) return false;
                    var own = battle.ShipsOf(player, true);
                    var lp = leader.transform.position; float range = DipGgFixesMod.TorpedoTurnAwayRange.Value;
                    int n = 0, sighted = 0; float closest = float.MaxValue, soonest = float.MaxValue; UnityEngine.Vector3 dirSum = UnityEngine.Vector3.zero;
                    // 1.11.205 (author, 24.10): the turn is timed by physics - a sighted torpedo that would pass through the division is answered
                    //           when its time to the closest point is no longer than the time the leader needs to turn out of the track
                    //           (a quarter circle of its turning radius at its speed, plus the helm delay); earlier the division held its course.
                    //           Nothing is evaded before the torpedo is sighted (IsVisibleFor).
                    float shipV = ShipTick.Vel(leader, UnityEngine.Time.time).magnitude; if (shipV < 3f) shipV = UnityEngine.Mathf.Max(3f, BattleAi.Speed(leader) * 0.8f); // m/s
                    float turnR = 0f; try { turnR = leader.turningRadius; } catch { }
                    if (!(turnR > 50f)) turnR = UnityEngine.Mathf.Clamp(200f + BattleAi.Ton(leader) / 50f, 250f, 1000f);
                    float turnTime = (UnityEngine.Mathf.PI * 0.5f * turnR) / shipV + 6f;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var t = all[i]; if (t == null) continue;
                        var tp = t.transform.position; var rel0 = lp - tp; rel0.y = 0f;
                        if (rel0.magnitude > range) continue;
                        if (!t.IsVisibleFor(leader)) continue;
                        if (Owner(t, own, enemies) == player.Pointer) continue;
                        var f = t.transform.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) continue; f.Normalize();
                        // closest approach of the track to any ship of the division still ahead of the torpedo
                        float pass = float.MaxValue, alongMin = float.MaxValue;
                        var ships = div.ships;
                        for (int k = 0; ships != null && k < ships.Count; k++)
                        {
                            var sh = ships[k]; if (sh == null || sh.isSinking || sh.isDead) continue;
                            var rel = sh.transform.position - tp; rel.y = 0f;
                            float along = UnityEngine.Vector3.Dot(rel, f); if (along <= 0f) continue;
                            float p = (rel - f * along).magnitude; if (p < pass) { pass = p; alongMin = along; }
                        }
                        if (pass > 500f) continue;
                        sighted++;
                        float tv = TorpedoSpeed(t, tp); float tcpa = alongMin / UnityEngine.Mathf.Max(5f, tv);
                        if (tcpa < soonest) soonest = tcpa;
                        if (tcpa > turnTime * 1.3f) continue; // sighted, but there is still time - hold the course
                        n++; if (pass < closest) closest = pass; dirSum += f;
                    }
                    if (n == 0)
                    {
                        if (sighted > 0 && UnityEngine.Time.time > st.nextSightLog) { st.nextSightLog = UnityEngine.Time.time + 20f; Log($"{st.name} ({leader.shipType.name}): {sighted} enemy torpedo track(s) sighted, closest point in {soonest:0} s - holding course, the turn needs {turnTime:0} s (turning radius {turnR:0} m at {shipV:0.0} m/s)"); }
                        return false;
                    }
                    var away = dirSum.normalized; // steer parallel to the tracks, running with them: fewer hits, longer torpedo run
                    // 1.11.270 (author 25.10, 15:23: a cruiser combing tracks ran to 600 m of an enemy battleship): of the two courses
                    // parallel to the tracks, the one that opens the range from the nearest enemy ship within 2.5 km
                    string flip = "";
                    try
                    {
                        Ship nearE = null; float nd = 2500f;
                        for (int i = 0; enemies != null && i < enemies.Count; i++) { var e = enemies[i]; if (e == null || e.isSinking || e.isDead) continue; var r = e.transform.position - lp; r.y = 0f; float d = r.magnitude; if (d < nd) { nd = d; nearE = e; } }
                        if (nearE != null) { var toE = nearE.transform.position - lp; toE.y = 0f; toE.Normalize(); if (UnityEngine.Vector3.Dot(away, toE) > 0.2f) { away = -away; flip = $" - against the tracks, away from {nearE.name} at {nd:0} m"; } }
                    }
                    catch { }
                    div.MoveTo(lp + away * 3000f); st.evadeDir = away;
                    st.evadeUntil = UnityEngine.Time.time + DipGgFixesMod.TorpedoTurnAwaySeconds.Value;
                    st.closing = false;
                    Why(st, "turning away from torpedoes");
                    Log($"{st.name} ({leader.shipType.name}): {n} enemy torpedo track(s) sighted, closest pass {closest:0} m - division turns away and combs the tracks for {DipGgFixesMod.TorpedoTurnAwaySeconds.Value:0} s{flip}");
                    return true;
                }
                catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] torpedo evasion failed: " + e.Message); return false; }
            }
        }

        static readonly System.Random zigRnd = new System.Random();
        // 1.11.40: move towards dest with a zigzag (alternating course offset) so the enemy's fire suffers the target-turning penalty
        internal static void MoveEvasive(Division div, Ship leader, DivState st, UnityEngine.Vector3 dest, string why)
        {
            if (!DipGgFixesMod.Evasive.Value) { div.MoveTo(dest); return; }
            var lt = leader.transform; if (lt == null) { div.MoveTo(dest); return; }
            var pos = lt.position; var dir = dest - pos; dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 1500f) { div.MoveTo(dest); return; }
            float now = UnityEngine.Time.time;
            if (st.legSign == 0) st.legSign = zigRnd.Next(2) == 0 ? -1 : 1;
            if (now >= st.nextLeg)
            {
                st.legSign = -st.legSign;
                float leg = UnityEngine.Mathf.Max(8f, DipGgFixesMod.EvasiveSeconds.Value);
                st.nextLeg = now + leg * (0.7f + 0.6f * (float)zigRnd.NextDouble());
                if (!st.zigLogged) { st.zigLogged = true; Log($"{st.name}: evasive zigzag +-{DipGgFixesMod.EvasiveAngle.Value:0} deg while {why}"); }
            }
            var rot = UnityEngine.Quaternion.Euler(0f, st.legSign * DipGgFixesMod.EvasiveAngle.Value, 0f);
            var aim = pos + rot * (dir / dist) * UnityEngine.Mathf.Min(dist, 3000f);
            div.MoveTo(aim);
        }

        // Torpedo charge of a light (or capital) division against the nearest visible enemy capital ship.
        // 1.11.262 (author 25.10, 14:37: "the torpedo attack does not happen"): the player's Torpedo attack order (division orders panel) ran
        // with the AI's rules - it waited until a capital ship came within the AI's start distance (log: "[Torpedo attack]: waiting") and
        // ignored the torpedo target priorities. An ordered charge now goes in at once at the priority target (next class down when the
        // listed class is missing) or the nearest capital ship.
        internal static bool OrderedCharge;
        static readonly HashSet<IntPtr> desperateLogged = new HashSet<IntPtr>();
        internal static void TorpedoCharge(Ship leader, Division div, DivState st, Doctrine doc, Il2CppSystem.Collections.Generic.List<Ship> enemies, bool playerSide)
        {
            float dmg = DivDamage(div);
            bool ammo = HasTorpedoAmmo(leader);
            float abort = playerSide ? AbortDamage(doc) : AbortFor(leader, doc);
            // 1.11.307 (author 26.10, 20:51: "the enemy CL should attack with torpedoes" - CL Town, too slow to escape a battleship at
            // 3.6 km, gave up the charge as "too damaged" and fought on with guns only): a damaged division that cannot get away and
            // has loaded tubes with a capital ship inside torpedo range goes on with the attack - turning away would not save it
            if (!playerSide && ammo && dmg > abort && st.fighting)
            {
                try
                {
                    float tr = TorpedoRange(leader); float dd; var nc = NearestVisibleCapital(leader, enemies, out dd);
                    if (nc != null && tr > 0f && dd <= tr && LoadedTubes(div, leader) > 0f)
                    {
                        if (desperateLogged.Add(div.Pointer)) Log($"{st.name} ({leader.shipType.name}): damage {dmg:0.00} over the charge limit, but it cannot get away and {nc.name} is at {dd:0} m (torpedoes {tr:0} m) - presses the attack to fire the loaded tubes");
                        abort = dmg + 1f;
                    }
                }
                catch { }
            }
            // 1.11.86: AI attack cycle - press until every tube is empty, turn away, reload, come back
            if (!playerSide && ammo && dmg <= abort && ReloadCycle(leader, div, st, enemies)) return;
            // 1.11.87: torpedoes gone and no reloads on board - an AI flotilla that is still fit turns on the enemy light forces instead of retreating
            if (st.charging && !ammo && dmg <= abort && !playerSide && IsLight(leader) && EngageLightForces(leader, div, st, doc, enemies)) { st.charging = false; st.reloading = false; return; }
            if (st.charging && (dmg > abort || !ammo))
            {
                st.charging = false;
                if (!st.fighting) div.Retreat(true);
                Log($"{st.name}: charge aborted ({(ammo ? "damage " + dmg.ToString("0.00") : "no torpedoes")}) - {(st.fighting ? "keeps fighting with guns (cannot escape)" : "retreating")}");
                return;
            }
            bool mass = false;
            if (!playerSide) { var bt = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (bt != null) mass = MassAttack(leader.player, bt, enemies); }
            if (!ammo || dmg > abort) { st.reloading = false; st.returnAttack = false; }
            // 1.11.91: tubes empty and no reload came - fight the light forces with guns instead of charging again (test: endless empty charges)
            if (!playerSide && ammo && st.noReloads && LoadedTubes(div, leader) == 0f) { st.charging = false; if (IsLight(leader) && EngageLightForces(leader, div, st, doc, enemies)) return; if (KeepClearNoTorpedoes(leader, div, st, enemies)) return; Why(st, "torpedoes expended - no reloads"); return; }
            if (!ammo) { if (!playerSide && IsLight(leader) && (st.fighting || mass) && TryRam(leader, div, st, enemies)) return; if (!playerSide && IsLight(leader) && EngageLightForces(leader, div, st, doc, enemies)) return; if (!playerSide && KeepClearNoTorpedoes(leader, div, st, enemies)) return; Why(st, "no torpedo ammo"); return; } // 1.11.330 (#70): keep clear instead of being left to the game
            if (dmg > abort) { if (!playerSide && KeepClearNoTorpedoes(leader, div, st, enemies, "too damaged for a torpedo charge")) return; Why(st, "too damaged for a charge"); return; } // 1.11.330 (#70)

            float bestD;
            // 1.11.251: a player's division with torpedo target priorities charges the nearest enemy of the most important class
            Ship best = null; bestD = float.MaxValue;
            bool ordered = OrderedCharge;
            if (playerSide || ordered) { best = TorpedoPriority.ChargeTarget(leader, enemies, out bestD); if (best != null && st.target != best.Pointer && st.charging) Log($"{st.name}: torpedo target priority - charge shifts to {best.name}"); if (best != null && st.charging) st.target = best.Pointer; } // 1.11.267: the shift is kept (the log repeated it every tick)
            if (best == null) { best = NearestVisibleCapital(leader, enemies, out bestD); if (best != null && !playerSide && IsLight(leader)) best = TorpedoPincer.Target(div, leader, best, enemies, out bestD); } // 1.11.343: the leading ship from ahead or the last one from the quarter
            if (best == null) { st.charging = false; Why(st, "no visible capital target"); return; }

            float torp = TorpedoRange(leader);
            if (torp <= 0f) { Why(st, "torpedo range 0"); return; }
            float launchAt = torp * DipGgFixesMod.ChargeTorpedoMargin.Value;
            // 1.11.300 (author 26.10, 20:16: "the CAs went too close to the BB" - Scharnhorst held off only at 1.7 km and was at 0.9 km
            // later, torpedoes 4.1 km): a player's ordered attack launches from 85 % of the torpedo range, starts holding off at 110 %
            // of that distance (the target closes too), and with the tubes empty opens the range instead of staying at launch distance
            bool pOrder = playerSide || ordered;
            if (pOrder) launchAt = torp * Math.Max(DipGgFixesMod.ChargeTorpedoMargin.Value, 0.85f);
            float holdAt = pOrder ? launchAt * 1.1f : launchAt;
            // 1.11.88: night rules (War Instructions 1944, 827; national doctrine: avoid / accept / seek night action)
            if (!playerSide && !ordered && NightFlotilla(leader, div, st, doc, enemies, bestD, launchAt)) return;
            if (!st.charging)
            {
                float start = ChargeStartFor(leader, best, doc, torp);
                if (ordered) start = float.MaxValue; // 1.11.262: the player ordered the attack - go in now
                // 1.11.40: battle log 2026-10-18 - CL Omaha charged from 14.6 km at 4.3 vs 12.1 speed units (torpedoes 1.6 km):
                // a division that cannot escape charges only when the target is reachable
                if (st.returnAttack && !playerSide)
                {
                    // 1.11.86: back from reloading - the attack resumes at once (it was already under way)
                    start = float.MaxValue; st.returnAttack = false;
                    Log($"{st.name} ({leader.shipType.name}): returns to the attack on {best.name} at {bestD:0} m with reloaded tubes");
                }
                else if (mass)
                {
                    // 1.11.354 (campaign 28.10: Japanese flotillas went in from 32-39 km in daylight - 15 torpedoes, 0 hits; next battle 51
                    // torpedoes, 1 hit; historical data (claude/dane_historyczne/battles.csv): daylight torpedo hit rate about 3 %, launches
                    // beyond 8 km about 3 %): by day a massed attack waits until the target is within twice its own start distance (at
                    // least twice the torpedo range), so the run under the enemy's guns is short; at night it still goes in at once
                    bool night = false; try { night = IsNight(); } catch { }
                    float massStart = night ? float.MaxValue : UnityEngine.Mathf.Max(ChargeStartFor(leader, best, doc, torp) * 2f, torp * 2f);
                    if (!st.massLogged && bestD <= massStart) { st.massLogged = true; Log($"{st.name} ({leader.shipType.name}): massed torpedo attack - charges {best.name} from {bestD:0} m (torpedoes {torp:0} m{(night ? ", night" : $", by day from {massStart:0} m")}); alone it would not get through, part of the attack will"); }
                    start = massStart;
                }
                else if (!playerSide && !ordered && !st.fighting && !st.covering && DipGgFixesMod.CapitalSupportScreen.Value && IsLight(leader)
                         && (doc == null || doc.lineFirst) && !(doc != null && doc.night > 0 && IsNight())
                         && BattleManager.Instance != null && BattleManager.Instance.CurrentBattle != null
                         && LinesEngaged(leader.player, BattleManager.Instance.CurrentBattle, enemies) == 1
                         && LineWillEngage(leader.player, BattleManager.Instance.CurrentBattle, enemies, doc))
                {
                    // 1.11.87 (War Instructions 1944, 12124): own battle line present but not yet engaged - torpedoes are held back
                    Why(st, "holding torpedoes until the battle lines are engaged"); return;
                }
                else if (!ordered && st.fighting && bestD > start * DipGgFixesMod.ChargeReachFactor.Value && Speed(leader) <= Speed(best))
                {
                    if (!st.reachLogged) { st.reachLogged = true; Log($"{st.name}: charge on {best.name} not feasible ({bestD:0} m, start distance {start:0} m, speed {Speed(leader):0.0} vs {Speed(best):0.0}) - fighting with guns"); }
                    Why(st, "charge not feasible - guns"); return;
                }
                if (bestD > start && !st.fighting)
                {
                    if (st.reason != "waiting") Log($"{st.name}: nearest capital {best.name} at {bestD:0} m beyond start distance ({start:0} m: torpedoes {torp:0} m, speed {Speed(leader):0.0} vs {Speed(best):0.0})");
                    Why(st, "waiting"); return;
                }
                st.charging = true; st.target = best.Pointer;
                Why(st, "charging");
                Log($"{(playerSide ? "[player scout] " : "")}{st.name} ({leader.shipType.name}) charges {best.name} ({best.shipType.name}) at {bestD:0} m, torpedo range {torp:0} m, {(mass ? "massed attack" : start >= float.MaxValue ? (ordered ? "ordered - goes in at once" : "goes in at once") : $"start distance {start:0} m")}");
                if (!playerSide && !ordered && IsLight(leader) && DipGgFixesMod.AiCrossTorpedo != null && DipGgFixesMod.AiCrossTorpedo.Value) TorpedoPincer.Split(div, leader, st.name, best); // 1.11.343
            }
            UnityEngine.Vector3 xdir = UnityEngine.Vector3.zero; int xslot = 0, xof = 1;
            bool aiTactics = !pOrder && DipGgFixesMod.AiCrossTorpedo != null && DipGgFixesMod.AiCrossTorpedo.Value; // 1.11.318 (#56)
            bool cross = (pOrder || aiTactics) && CrossSlot(div, best, out xdir, out xslot, out xof);   // 1.11.302; AI since 1.11.318
            // 1.11.343 (author 28.10, 12:39): an AI light division with a two-way role approaches fine on the bow or on the quarter
            UnityEngine.Vector3 pdir = UnityEngine.Vector3.zero;
            // 1.11.360 (author 29.10: the player should see the same attack under the Torpedo attack order): also for a player's
            // division under that order (ordered charge) - before, only the target choice applied to it, not the approach
            bool twoWay = !playerSide && IsLight(leader) && TorpedoPincer.Approach(div, leader, best, out pdir);
            if (twoWay)
            {
                xdir = pdir; cross = false;
                // 1.11.347: the tubes go on the ship the division attacks, not on the one the game picked
                var dsh = div.ships; int dn = dsh != null ? dsh.Count : 0;
                for (int k = -1; k < dn; k++) { var s2 = k < 0 ? leader : dsh[k]; if (s2 == null || s2.isSinking || s2.isDead || (k >= 0 && s2.Pointer == leader.Pointer)) continue; TorpedoPriority.AimAt(s2, best); }
            }
            if (bestD > holdAt)
            {
                var tt = best.transform;
                if (tt != null)
                {
                    var aim = (cross || twoWay) ? tt.position + xdir * launchAt : tt.position;
                    // 1.11.344 (author 28.10, 12:45: "during the approach, when the hits of the defensive fire begin, we lay smoke"): an AI
                    // light division that takes damage or loses a ship during the run-in lays smoke (every 30 s at most, while hit again)
                    if (!playerSide && IsLight(leader) && DipGgFixesMod.AiApproachSmoke != null && DipGgFixesMod.AiApproachSmoke.Value) // 1.11.360: also the player's Torpedo attack order
                    {
                        float dd = DivDamage(div);
                        if (st.approachDmg >= 0f && dd > st.approachDmg + 0.005f)
                        {
                            if (!st.approachSmokeLogged) { st.approachSmokeLogged = true; Log($"{st.name}: hit during the approach to {best.name} at {bestD:0} m (division damage {st.approachDmg:0.00} -> {dd:0.00}) - smoke"); }
                            TrySmoke(div, leader, st);
                        }
                        st.approachDmg = dd;
                    }
                    if (cross && st.reason != "cross attack") { Log($"{st.name}: cross torpedo attack on {best.name} - approach {(xslot % 2 == 0 ? "starboard" : "port")} bow ({xslot + 1} of {xof} divisions), tracks to cross at about 90 deg"); }
                    if (bestD > launchAt * 1.3f) MoveEvasive(div, leader, st, aim, "charging"); else div.MoveTo(aim);
                    if (cross) Why(st, "cross attack");
                }
            }
            // inside launch range: the game's torpedo logic fires on its own.
            // 1.11.86: the AI keeps closing while tubes are still loaded (vanilla 1.7 test: flotillas hovered just outside range after one salvo)
            else if (!playerSide && DipGgFixesMod.TorpedoReloadCycle.Value && bestD > launchAt * 0.6f)
            {
                // 1.11.318 (#56): the AI fires the other side too - one side empty and the other loaded: turn to bring it to bear
                if (aiTactics && TwoSideTurn(div, leader, st, best, bestD)) return;
                var tt2 = best.transform; if (tt2 != null) div.MoveTo((cross || twoWay) ? tt2.position + xdir * (launchAt * 0.7f) : tt2.position);
            }
            // 1.11.267 (author 25.10, 15:04/15:08: CA under a torpedo attack order closed to ramming distance): a player's division
            // inside launch range stops closing - it holds off at the launch distance, running across the target's bearing
            else if (playerSide || ordered)
            {
                var tt3 = best.transform; var lt = leader.transform;
                if (tt3 != null && lt != null)
                {
                    var dir = lt.position - tt3.position; dir.y = 0f;
                    if (dir.sqrMagnitude > 1f)
                    {
                        dir.Normalize(); var side = new UnityEngine.Vector3(-dir.z, 0f, dir.x);
                        float loaded = 1f; try { loaded = LoadedTubes(div, leader); } catch { }
                        // 1.11.302 (author 26.10, 20:23): one side fired - turn so the loaded side bears on the target, fire it, and only
                        // then go off to reload
                        if (loaded > 0f)
                        {
                            LoadedSides(leader, out bool lp, out bool ls);
                            float tx = lt.InverseTransformPoint(tt3.position).x;   // > 0: target on the starboard side
                            bool targetStbd = tx > 0f;
                            if ((targetStbd && !ls && lp) || (!targetStbd && !lp && ls))
                            {
                                var toT = tt3.position - lt.position; toT.y = 0f; toT.Normalize();
                                // heading with the target abeam on the loaded side: starboard abeam = (-dz, dx), port abeam = (dz, -dx)
                                var h = lp ? new UnityEngine.Vector3(toT.z, 0f, -toT.x) : new UnityEngine.Vector3(-toT.z, 0f, toT.x);
                                div.MoveTo(lt.position + h * 2500f);
                                string why2 = lp ? "turning to bring the port tubes to bear" : "turning to bring the starboard tubes to bear";
                                if (st.reason != why2) Log($"{st.name}: {(lp ? "starboard" : "port")} tubes fired at {best.name} ({bestD:0} m) - {why2}");
                                Why(st, why2);
                                return;
                            }
                        }
                        if (cross) { dir = xdir; side = new UnityEngine.Vector3(-dir.z, 0f, dir.x); }
                        if (loaded == 0f && IsCapital(best))
                        {
                            // 1.11.300: salvo away - turn away from the battleship and open the range to 1.6 x the torpedo range
                            div.MoveTo(tt3.position + dir * (torp * 1.6f) + side * (torp * 0.3f));
                            if (st.reason != "tubes empty - opening the range") Log($"{st.name}: torpedoes away at {best.name} ({bestD:0} m) - tubes empty, turns away and opens the range");
                            Why(st, "tubes empty - opening the range");
                            return;
                        }
                        div.MoveTo(tt3.position + dir * launchAt + side * (launchAt * 0.6f));
                        if (st.reason != "in launch range - holding off") Log($"{st.name}: inside torpedo launch range of {best.name} ({bestD:0} m, launch {launchAt:0} m) - stops closing and runs across");
                        Why(st, "in launch range - holding off");
                    }
                }
            }
        }

        // 1.11.318 (#56): the two-side salvo of the player's order (1.11.302) for AI divisions - target on the empty side, other side loaded
        static bool TwoSideTurn(Division div, Ship leader, DivState st, Ship best, float bestD)
        {
            try
            {
                var tt3 = best.transform; var lt = leader.transform; if (tt3 == null || lt == null) return false;
                float loaded = 1f; try { loaded = LoadedTubes(div, leader); } catch { }
                if (loaded <= 0f) return false;
                LoadedSides(leader, out bool lp, out bool ls);
                bool targetStbd = lt.InverseTransformPoint(tt3.position).x > 0f;
                if (!((targetStbd && !ls && lp) || (!targetStbd && !lp && ls))) return false;
                var toT = tt3.position - lt.position; toT.y = 0f; if (toT.sqrMagnitude < 1f) return false; toT.Normalize();
                var h = lp ? new UnityEngine.Vector3(toT.z, 0f, -toT.x) : new UnityEngine.Vector3(-toT.z, 0f, toT.x);
                div.MoveTo(lt.position + h * 2500f);
                string why2 = lp ? "turning to bring the port tubes to bear" : "turning to bring the starboard tubes to bear";
                if (st.reason != why2) Log($"{st.name}: {(lp ? "starboard" : "port")} tubes fired at {best.name} ({bestD:0} m) - {why2} (AI)");
                Why(st, why2);
                return true;
            }
            catch { return false; }
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
            // 1.11.40: battle log 2026-10-18 - cruisers with 76-294 mm penetration closed in 19 times on a 634 mm belt and every
            // close-in ended "still cannot penetrate". Check penetration at the closest planned range before closing in.
            if (noPen && DipGgFixesMod.CloseInOnlyIfPenetrates.Value && stopAt > 0f && !st.closing)
            {
                float penNear, beltNear;
                bool hopeless = CannotPenetrate(leader, best, UnityEngine.Mathf.Min(stopAt, bestD), out penNear, out beltNear);
                if (hopeless)
                {
                    if (!st.hopelessLogged) { st.hopelessLogged = true; Log($"{st.name} ({leader.shipType.name}): cannot penetrate {best.name} even at {UnityEngine.Mathf.Min(stopAt, bestD):0} m (pen {penNear:0.0} vs belt {beltNear:0.0}) - no close-in, left to the game"); }
                    string sw;
                    // 1.11.127: battle log 2026-10-23 - CA Palestro turned away from CA Tonnerre (a ship of the same class) the moment she
                    // was sighted at 17 km, before she was identified and before a shot was fired. The AI now breaks off only after the
                    // target has been identified by its side and the guns have had time to prove the point (HopelessEscapeDelay s of
                    // battle time while the target stays out of reach).
                    if (st.hopelessSince < 0f) st.hopelessSince = UnityEngine.Time.time;
                    bool known = Fog.Identified(leader.player, best);
                    bool proven = UnityEngine.Time.time - st.hopelessSince >= DipGgFixesMod.HopelessEscapeDelay.Value;
                    // 1.11.131: battle log 2026-10-23 - two French CA of an attacking force of 5 CA + 6 CL broke off from 2 German CA because
                    // their 254 mm guns could not pierce the belt; a side this much stronger fights on (superstructure, fires, partial hits)
                    if (DipGgFixesMod.CapitalEscapeWhenHopeless.Value)
                    {
                        float ownS = 0f, foeS = 0f;
                        try
                        {
                            var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                            if (cb != null) { var mine = cb.ShipsOf(leader.player, true); ownS = Strength(mine); bool fog = Fog.AiActive && !leader.player.isMain; foeS = fog ? PerceivedStrength(leader.player, enemies) : Strength(enemies); }
                        }
                        catch { }
                        if (ownS > 0f && foeS > 0f && ownS >= foeS * DipGgFixesMod.HopelessFightOnRatio.Value)
                        {
                            if (!st.hopelessWaitLogged) { st.hopelessWaitLogged = true; Log($"{st.name}: cannot penetrate {best.name} but fights on - own side {ownS:0} t vs enemy {foeS:0} t"); }
                            Why(st, "hopeless penetration - fighting on, own side much stronger"); return;
                        }
                    }
                    if (DipGgFixesMod.CapitalEscapeWhenHopeless.Value && (!known || !proven))
                    {
                        if (!st.hopelessWaitLogged) { st.hopelessWaitLogged = true; Log($"{st.name}: no retreat yet from {best.name} - {(!known ? "target not identified" : "keeps firing first")}"); }
                        Why(st, "hopeless penetration - waiting before breaking off"); return;
                    }
                    // 1.11.250 (film 25.10, B8: an AI battleship with a 16-inch belt left the battle after a few salvoes - 0 % hits, 97 % of
                    //          its ammunition unused): a capital ship that cannot pierce the enemy battle line still fires at the enemy ships it
                    //          can hurt (cruisers, destroyers), and it does not run when no enemy can pierce it either (Jutland: the battle lines
                    //          fought on at ranges where belts held; the damage came from hits on the upper works, turrets and lighter ships)
                    if (DipGgFixesMod.HopelessEngageLighter.Value)
                    {
                        Ship soft = null; float softD = float.MaxValue, sp, sb;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var e = enemies[i]; if (e == null || e.isDead || e.isSinking || e.IsSurrendered) continue;
                            bool vis = false; try { vis = e.IsVisibleFor(leader); } catch { }
                            if (!vis) continue;
                            float d = Dist(leader, e); if (ownR > 0f && d > ownR) continue;
                            if (!CannotPenetrate(leader, e, d, out sp, out sb) && d < softD) { soft = e; softD = d; }
                        }
                        if (soft != null)
                        {
                            if (!st.hopelessSoftLogged) { st.hopelessSoftLogged = true; Log($"{st.name} ({leader.shipType.name}): cannot penetrate {best.name}, but can hurt {soft.name} ({soft.shipType.name}, {softD:0} m) - stays in action"); }
                            Why(st, "hopeless against the line - engages the lighter ships"); return;
                        }
                        bool threatened = false;
                        for (int i = 0; i < enemies.Count && !threatened; i++)
                        {
                            var e = enemies[i]; if (e == null || e.isDead || e.isSinking || e.IsSurrendered) continue;
                            float d = Dist(leader, e); float er = GunRange(e); if (er > 0f && d > er * 1.2f) continue;
                            if (!CannotPenetrate(e, leader, UnityEngine.Mathf.Max(1000f, d * 0.6f), out sp, out sb)) threatened = true;
                        }
                        if (!threatened)
                        {
                            if (!st.hopelessSoftLogged) { st.hopelessSoftLogged = true; Log($"{st.name} ({leader.shipType.name}): neither side can pierce the other's belt - fights on (upper works, turrets, fires)"); }
                            Why(st, "belts hold on both sides - fighting on"); return;
                        }
                    }
                    if (DipGgFixesMod.CapitalEscapeWhenHopeless.Value && IsMainShip(leader, BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null) && !st.capEscape && CanSurvive(leader, div, doc, enemies, out sw))
                    {
                        st.capEscape = true; div.Retreat(true);
                        Log($"{st.name} ({leader.shipType.name}): cannot hurt {best.name} and can survive the battle ({sw}) - retreats to save the ship");
                        Why(st, "escaping - " + sw); return;
                    }
                    Why(st, "hopeless penetration - no close-in"); return;
                }
            }
            // 1.11.8: hysteresis. Battle log 2026-10-16: a CA at 0.7x range flipped "close-in ended" / "closes in" five times
            // around 6.9-7.1 km. Once a close-in has ended it restarts only beyond stop + 15% of own range.
            if ((outranged || noPen) && bestD > stopAt && (st.closing || bestD > st.closeResumeAt))
            {
                if (!st.closing)
                {
                    st.closing = true;
                    Log($"{st.name} ({leader.shipType.name}) closes in on {best.name} ({best.shipType.name}) at {bestD:0} m: {(outranged ? $"outranged ({ownR:0} vs {tgtR:0} m)" : "")}{(outranged && noPen ? ", " : "")}{(noPen ? $"cannot penetrate (pen {pen:0.0} vs belt {belt:0.0})" : "")}");
                }
                Why(st, "closing in");
                var tt = best.transform;
                if (tt != null) { if (noPen || bestD > ownR) MoveEvasive(div, leader, st, tt.position, "closing in"); else div.MoveTo(tt.position); }
                return;
            }
            if (st.closing) { st.closing = false; st.closeResumeAt = ownR * (DipGgFixesMod.CloseInStopRange.Value + 0.15f); Log($"{st.name}: close-in ended at {bestD:0} m ({(noPen ? "still cannot penetrate" : "penetration ok")}, {(outranged ? "still outranged" : "range ok")}) - left to the game"); }
            // 1.11.86: while the own screen attacks, the capital ships move up and engage the enemies fighting it
            if (ScreenSupport(leader, div, st, doc, enemies)) return;
            Why(st, "guns adequate - left to the game");
        }

        // Detach the ships that slow the division down into a new division and order it to retreat.
        // If the leader itself is the slow one, the fast ships are detached instead and the
        // leader's (old) division retreats. Returns true when a detach was performed.
        static int ammoLogged;
        // shells left for the main battery (the guns of the largest calibre on board); -1 when it cannot be read
        internal static int MainBatteryAmmo(Ship s, out float cal)
        {
            cal = 0f; int left = 0;
            try
            {
                var ps = s.parts; if (ps == null) return -1;
                var seen = new HashSet<IntPtr>();
                for (int i = 0; i < ps.Count; i++) { var d = ps[i] != null ? ps[i].data : null; if (d != null && d.isGun && d.caliber > cal) cal = d.caliber; }
                if (cal <= 0f) return -1;
                for (int i = 0; i < ps.Count; i++)
                {
                    var d = ps[i] != null ? ps[i].data : null;
                    if (d == null || !d.isGun || d.caliber < cal - 0.5f || !seen.Add(d.Pointer)) continue;
                    left += s.GetTotalAmmo(d);
                }
                return left;
            }
            catch { return -1; }
        }
        // 1.11.346 (author 28.10, 13:35: "out of ammunition means no main-battery shells at all"): true when every live ship of the
        // division has 0 shells left for its main battery
        internal static bool LowAmmo(Division div, Ship leader, out float avg)
        {
            avg = 0f; int n = 0;
            var ships = div.ships; int cnt = ships != null ? ships.Count : 0;
            for (int k = -1; k < cnt; k++)
            {
                var s = k < 0 ? leader : ships[k];
                if (s == null || s.isSinking || s.isDead || (k >= 0 && s.Pointer == leader.Pointer)) continue;
                int left = MainBatteryAmmo(s, out float cal);
                if (left < 0) return false;
                if (ammoLogged++ < 3 && left > 0) Log($"{s.name}: main battery {cal:0} mm - {left} shells left");
                if (left > 0) return false;
                n++;
            }
            return n > 0;
        }

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
            try { Narrative.FromLog(m); } catch { }
            try { Dispatch.FromLog(m); } catch { }
            if (DipGgFixesMod.BattleAiLog != null && DipGgFixesMod.BattleAiLog.Value) MelonLogger.Msg("[DIP GG Fixes][Battle] " + m);
        }
    }

    [HarmonyPatch(typeof(Ship), "AILogic")]
    internal static class Patch_AILogic
    {
        [ThreadStatic] static bool tweaked;
        private static void Prefix(Ship __instance)
        {
            long t0 = Perf.Enabled ? Perf.T() : 0;
            BattleAi.CurrentDistanceMult = 1f; tweaked = false;
            try
            {
                if (__instance == null || !Doctrines.Loaded) return;
                var p = __instance.player; if (p == null || p.isMain) return;
                var d = Doctrines.For(p); if (d != null) BattleAi.CurrentDistanceMult = d.distanceMult;
                float m = BattleAi.CurrentDistanceMult;
                // 1.11.96: the parameter itself is set for this call (no hook on MonoBehaviourExt.Param)
                if (m != 1f && m > 0f) tweaked = ParamTweak.Begin("ai_distance_mod", ParamTweak.Base("ai_distance_mod", 1f) * m);
            }
            catch { }
            finally { if (t0 != 0) Perf.Add(Perf.AI, t0); }
        }
        private static void Finalizer() { if (tweaked) { tweaked = false; ParamTweak.End("ai_distance_mod"); } BattleAi.CurrentDistanceMult = 1f; }
        private static void Postfix(Ship __instance)
        {
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { BattleAi.Think(__instance); }
            catch (Exception e)
            {
                if (BattleAi.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AILogic postfix: " + e.Message);
            }
            finally { if (t0 != 0) Perf.Add(Perf.AI, t0); }
        }
    }

    // =====================================================================================
    // 1.11.250 (film of 25.10, point B5): the player's target orders. Ship.SetEnemy is what the game calls when the player orders a ship
    // (or its main/secondary battery) to fire at an enemy. A call made while the player is clicking or pressing a key is a player
    // order; the mod's target selection then leaves those guns alone until the target sinks, surrenders or the player gives the ship back
    // to automatic targeting (Ship.SetAutoTargeting). Log: [Battle] player order: ...
    internal static class PlayerTargetOrders
    {
        class O { public IntPtr Target; public string Name; public bool Mains, Secs; }
        static readonly Dictionary<IntPtr, O> orders = new Dictionary<IntPtr, O>();
        static IntPtr battle; static int logged;
        static void Battle()
        {
            IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer; } catch { }
            if (b != battle) { battle = b; orders.Clear(); }
        }
        static bool PlayerInput()
        {
            try
            {
                if (UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetMouseButtonUp(0) || UnityEngine.Input.GetMouseButtonUp(1) || UnityEngine.Input.anyKeyDown || UnityEngine.Input.anyKey) return true;
                return UnityEngine.Time.realtimeSinceStartup - BattleAi.lastInput < 0.6f;
            }
            catch { return false; }
        }
        internal static void Record(Ship s, Ship enemy, bool all, bool mains, bool secs)
        {
            try
            {
                if (s == null || enemy == null || s.player == null || !s.player.isMain || !GameManager.IsBattle || !PlayerInput()) return;
                Battle();
                if (!all && !mains && !secs) return;
                orders[s.Pointer] = new O { Target = enemy.Pointer, Name = enemy.name, Mains = all || mains, Secs = all || secs };
                if (logged++ < 40) BattleAi.Log($"player order: {s.name} {(all ? "all guns" : (mains && secs ? "main and secondary guns" : mains ? "main guns" : "secondary guns"))} fire at {enemy.name} - the mod's target selection leaves them alone until the target is sunk or surrenders");
            }
            catch { }
        }
        internal static void Clear(Ship s)
        {
            try { if (s != null && orders.Remove(s.Pointer) && logged++ < 40) BattleAi.Log($"player order: {s.name} back to automatic targeting"); } catch { }
        }
        internal static bool Holds(Ship s, bool isMain)
        {
            if (orders.Count == 0 || s == null) return false;
            O o; if (!orders.TryGetValue(s.Pointer, out o)) return false;
            if (isMain ? !o.Mains : !o.Secs) return false;
            Ship t = null;
            try
            {
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                var all = cb != null ? cb.Ships : null;
                if (all != null) for (int i = 0; i < all.Count; i++) { var x = all[i]; if (x != null && x.Pointer == o.Target) { t = x; break; } }
            }
            catch { }
            bool gone = true; try { gone = t == null || t.isDead || t.isSinking || t.IsSurrendered; } catch { }
            if (gone) { orders.Remove(s.Pointer); if (logged++ < 40) BattleAi.Log($"player order: {s.name} - {o.Name} is out of the fight, the mod picks targets again"); return false; }
            return true;
        }
    }
    [HarmonyPatch(typeof(Ship), "SetEnemy")]
    internal static class Patch_ShipSetEnemy_PlayerOrder
    {
        static void Prefix(Ship __instance, Ship __0, bool __1, bool __2, bool __3) { PlayerTargetOrders.Record(__instance, __0, __1, __2, __3); }
    }
    [HarmonyPatch(typeof(Ship), "SetAutoTargeting")]
    internal static class Patch_ShipAutoTargeting_PlayerOrder
    {
        static void Prefix(Ship __instance) { if (BattleAi.PlayerInputRecent()) PlayerTargetOrders.Clear(__instance); }
    }

    // 1.11: per-ship battle tick (Ship.AimAndShoot runs for every ship, player and AI)
    // =====================================================================================
    internal static class ShipTick
    {
        internal class St { public float nextTarget; public float nextLane; public float nextPanic; public bool laneBlocked; public float laneHoldUntil; public Ship.ShootMode savedTorpedoMode; public bool panicked; public float startCrew; public Dictionary<IntPtr, IntPtr> ours = new Dictionary<IntPtr, IntPtr>();
            public IntPtr playerPtr; public IntPtr mainTarget; public float mainAt; // 1.11.88: fire distribution
            public float escortSeenAt = -1000f; // 1.11.252: escort first, transports after
            public float nextSense; public bool senseHold, senseApplied; public Ship.ShootMode senseSaved; public string senseWhy; public Ship senseTarget; public float nextSide, nextSideFast, senseChanged = -100f; } // 1.11.93: hopeless torpedo shots
        internal static readonly Dictionary<IntPtr, St> States = new Dictionary<IntPtr, St>();
        internal static int Errors; static bool announced;
        internal static int targetChanges, laneHolds, panics;

        static readonly System.Random jitter = new System.Random();
        static IntPtr stBattle; static int stFrame = -1;
        internal static St Get(Ship s)
        {
            // 1.11.96: per-battle states (they were kept for every ship of the session; the fire distribution scanned them all)
            int fc = UnityEngine.Time.frameCount;
            if (fc != stFrame) try { stFrame = fc; var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; var bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != stBattle) { stBattle = bp; States.Clear(); tracks.Clear(); } } catch { }
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
            AuditTorpedoes(s, now); // 1.11.94
            if (s.haveTorpedoes && now >= st.nextSideFast) { st.nextSideFast = now + 0.2f; bool cal = now >= st.nextSide; if (cal) st.nextSide = now + 1f; TorpedoSides(s, cal); } // 1.11.94 (1.11.96: 5x per second)
            // 1.11.93: hold a hopeless torpedo shot (mode set Off while hopeless, the saved mode restored afterwards)
            if (DipGgFixesMod.TorpedoSense.Value && s.haveTorpedoes && !st.laneBlocked && (!isPlayer || DipGgFixesMod.TorpedoSensePlayer.Value))
            {
                if (now >= st.nextSense)
                {
                    st.nextSense = now + 0.5f; string why = null;
                    bool hold = false;
                    try { hold = TorpedoHopeless(s, st, now, out why); } catch { hold = false; }
                    // 1.11.99: no flicker - a decision stands for at least 4 s (22.10: held/released every second near the limit)
                    if (hold != st.senseHold && now - st.senseChanged < 4f) hold = st.senseHold;
                    if (hold != st.senseHold) st.senseChanged = now;
                    if (hold != st.senseHold && senseLogs < 80) { senseLogs++; BattleAi.Log(hold ? $"{s.name}: torpedoes held - {why}" : $"{s.name}: torpedo shot possible again (was: {st.senseWhy})"); }
                    if (hold) { if (!st.senseHold) senseHolds++; st.senseWhy = why; }
                    st.senseHold = hold;
                }
                var m = s.torpedoMode;
                if (st.senseHold)
                {
                    if (m != Ship.ShootMode.Off && m != Ship.ShootMode.Invalid) { st.senseSaved = m; st.senseApplied = true; s.torpedoMode = Ship.ShootMode.Off; }
                }
                else if (st.senseApplied)
                {
                    st.senseApplied = false;
                    if (m == Ship.ShootMode.Off) s.torpedoMode = st.senseSaved;
                }
            }
            if (DipGgFixesMod.Panic.Value && now >= st.nextPanic) { st.nextPanic = now + 3f; PanicCheck(s, st); }
            bool surrSelf = false; try { surrSelf = s.IsSurrendered; } catch { }
            if (isPlayer && s.isDivLeader && !surrSelf)
            {
                bool handled = false; // 1.11.154: division orders with autonomy (screen, scout, torpedo attack, free action)
                try { handled = BattleAi.OrdersThink(s); } catch (Exception e) { if (BattleAi.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] division orders: " + e.Message); }
                if (!handled && DipGgFixesMod.PlayerScoutCharge.Value) BattleAi.Think(s, true);
            }
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
            st.nextTarget = now + 1.8f + 0.4f * (float)jitter.NextDouble(); // 1.11.96: spread the ships over frames
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
        internal static Ship TorpedoTarget(Ship s)
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
        // ---- 1.11.94: torpedo launch audit (player report 22.10: an enemy CA fired torpedoes to a side with no ships at all).
        // Every new torpedo is checked once: shooter = ship nearest to the launch point; when no enemy within reach lies
        // within 40 deg of the torpedo course the launch is logged with the shooter's torpedo aims (both sides).
        static readonly HashSet<IntPtr> seenTorps = new HashSet<IntPtr>();
        static float nextAudit; internal static int torpsAudited, torpsWasted; static int auditLogs;
        static string AimInfo(Ship s, Il2CppSystem.Collections.Generic.Dictionary<PartData, Ship.Aim> dict)
        {
            if (dict == null) return "none";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in dict)
            {
                var pd = kv.Key; var a = kv.Value; if (pd == null || a == null || !pd.isTorpedo) continue;
                var tg = a.target; string side = "-";
                if (tg != null) { var rel = tg.transform.position - s.transform.position; rel.y = 0f; side = UnityEngine.Vector3.Dot(s.transform.right, rel) >= 0f ? "starboard" : "port"; }
                sb.Append($"[{(tg != null ? tg.name : "no target")} on {side}, manual {a.manual}, angleFine {a.isAngleFine}, bearing {a.lastBearing:0}, hit {a.lastHitChance:0.###}, range {a.lastRange:0}] ");
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }
        static void AuditTorpedoes(Ship any, float now)
        {
            if (now < nextAudit) return; nextAudit = now + 0.5f;
            try
            {
                var all = Torpedo.AllTorpedo; if (all == null || all.Count == 0) return;
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                var pa = any.player; if (pa == null) return; var pb = battle.OppositePlayer(pa); if (pb == null) return;
                var la = battle.ShipsOf(pa, true); var lb = battle.ShipsOf(pb, true);
                if (seenTorps.Count > 5000) seenTorps.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    var t = all[i]; if (t == null || !seenTorps.Add(t.Pointer)) continue;
                    try { AuditOne(t, la, lb); } catch { } // 1.11.98: a torpedo or ship destroyed in the same frame threw here
                }
            }
            catch (Exception e) { if (Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] torpedo audit: " + e.Message); }
        }
        static void AuditOne(Torpedo t, Il2CppSystem.Collections.Generic.List<Ship> la, Il2CppSystem.Collections.Generic.List<Ship> lb)
        {
                {
                    var start = t.torpedoStart; start.y = 0f;
                    Ship sh = null; float bd = float.MaxValue; bool shA = true;
                    foreach (var pair in new[] { new { l = la, a = true }, new { l = lb, a = false } })
                    {
                        if (pair.l == null) continue;
                        for (int k = 0; k < pair.l.Count; k++) { var x = pair.l[k]; if (x == null) continue; var p = x.transform.position; p.y = 0f; float d = (p - start).sqrMagnitude; if (d < bd) { bd = d; sh = x; shA = pair.a; } }
                    }
                    if (sh == null) return;
                    var f = t.transform.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) return; f.Normalize();
                    torpsAudited++;
                    var foes = shA ? lb : la; float range = BattleAi.TorpedoRange(sh);
                    float best = 180f; Ship bestE = null; float bestD = 0f;
                    for (int k = 0; foes != null && k < foes.Count; k++)
                    {
                        var e = foes[k]; if (e == null || e.isDead || e.isSinking) continue;
                        var rel = e.transform.position - start; rel.y = 0f; float d = rel.magnitude; if (d > range * 1.2f) continue;
                        float ang = UnityEngine.Vector3.Angle(f, rel); if (ang < best) { best = ang; bestE = e; bestD = d; }
                    }
                    if (best <= 40f) return;
                    torpsWasted++;
                    if (auditLogs++ >= 40) return;
                    string dir = UnityEngine.Vector3.Dot(sh.transform.right, f) >= 0f ? "starboard" : "port";
                    float relB = UnityEngine.Vector3.SignedAngle(new UnityEngine.Vector3(sh.transform.forward.x, 0f, sh.transform.forward.z), f, UnityEngine.Vector3.up);
                    BattleAi.Log($"torpedo audit: {sh.name} ({(sh.player != null && sh.player.isMain ? "player" : "AI")}) launched a torpedo to {dir} (relative bearing {relB:0} deg, speed {t.speed:0.0}) with no enemy within 40 deg of its course" +
                        $" (nearest by angle: {(bestE != null ? bestE.name + " " + best.ToString("0") + " deg off, " + bestD.ToString("0") + " m" : "none within reach")}); torpedo mode {sh.torpedoMode};" +
                        $" right-side aims {AimInfo(sh, sh.enemies)}; left-side aims {AimInfo(sh, sh.enemiesLeftSide)}");
                }
        }

        // ---- 1.11.94: wrong-side torpedo aims. The game keeps separate aims for the right-side (enemies) and left-side
        // (enemiesLeftSide) weapon groups. A torpedo aim of one side whose target lies on the other side is dropped (the
        // game then picks again). The side of each group is not assumed: it is confirmed per battle from the gun aims of
        // that group (at least 15 samples, 80% on one side); a group with mixed sides (centre-line mounts) is left alone.
        // A torpedo aim set by the player (manual) is never touched.
        static IntPtr calBattle; static readonly int[] calN = new int[2], calStar = new int[2];
        internal static int sideDrops, surrDrops; static int sideLogs, surrLogs;
        static int SideSign(Ship s, Ship tg)
        {
            var rel = tg.transform.position - s.transform.position; rel.y = 0f;
            return UnityEngine.Vector3.Dot(s.transform.right, rel) >= 0f ? 1 : -1;
        }
        // +1 = the group fires to starboard, -1 = to port, 0 = not confirmed
        static int GroupSide(int idx)
        {
            if (calN[idx] < 15) return 0;
            if (calStar[idx] >= 0.8f * calN[idx]) return 1;
            if (calStar[idx] <= 0.2f * calN[idx]) return -1;
            return 0;
        }
        // 1.11.336 (#13): +1 / -1 for the right / left group when every torpedo launcher of the ship is off the centre line, 0 otherwise
        static readonly Dictionary<IntPtr, int> launcherLayout = new Dictionary<IntPtr, int>(); static IntPtr layoutBattle; static int layoutLogs;
        static int LauncherSide(Ship s, int idx)
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; IntPtr bp = b != null ? b.Pointer : IntPtr.Zero;
            if (bp != layoutBattle) { layoutBattle = bp; launcherLayout.Clear(); layoutLogs = 0; }
            int pure;
            if (!launcherLayout.TryGetValue(s.Pointer, out pure))
            {
                int star = 0, port = 0, centre = 0; pure = 0;
                try
                {
                    var ps = s.parts; var st = s.transform;
                    for (int i = 0; ps != null && i < ps.Count; i++)
                    {
                        var p = ps[i]; if (p == null || p.data == null || !p.data.isTorpedo) continue;
                        float x = st.InverseTransformPoint(p.transform.position).x;
                        if (Math.Abs(x) < 0.6f) centre++; else if (x > 0f) star++; else port++;
                    }
                    pure = centre == 0 && star > 0 && port > 0 ? 1 : 0;
                    if (layoutLogs++ < 20) BattleAi.Log($"{s.name}: torpedo launchers {star} starboard, {port} port, {centre} centre line - side check {(pure == 1 ? "on" : "off (centre-line or one-sided launchers)")}");
                }
                catch { pure = 0; }
                launcherLayout[s.Pointer] = pure;
            }
            if (pure != 1) return 0;
            return idx == 0 ? 1 : -1;
        }
        static void TorpedoSides(Ship s, bool calibrate)
        {
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                if (battle.Pointer != calBattle) { calBattle = battle.Pointer; calN[0] = calN[1] = calStar[0] = calStar[1] = 0; }
                var dicts = new[] { s.enemies, s.enemiesLeftSide };
                for (int idx = 0; idx < 2; idx++)
                {
                    var dict = dicts[idx]; if (dict == null) continue;
                    // 1.11.95: never a torpedo at a ship that has struck her colours (the game's own torpedo aim does not skip them)
                    foreach (var kv in dict)
                    {
                        var pd = kv.Key; var a = kv.Value; if (pd == null || a == null || !pd.isTorpedo) continue;
                        var tg = a.target; if (tg == null || a.manual) continue;
                        bool surr = false; try { surr = tg.IsSurrendered; } catch { }
                        if (!surr) continue;
                        a.target = null; surrDrops++;
                        if (surrLogs++ < 30) BattleAi.Log($"{s.name}: torpedo aim on {tg.name} dropped - she has surrendered");
                    }
                    // 1.11.98: the side check is off by default - in the 22.10 battle the gun-aim calibration "confirmed" a side
                    // because all enemies lay on one beam, and valid torpedo aims were dropped again and again
                    if (!DipGgFixesMod.TorpedoSideCheck.Value) continue;
                    if (calibrate && calN[idx] < 400)
                        foreach (var kv in dict)
                        {
                            var pd = kv.Key; var a = kv.Value; if (pd == null || a == null || !pd.isGun) continue;
                            var tg = a.target; if (tg == null || tg.isDead) continue;
                            calN[idx]++; if (SideSign(s, tg) > 0) calStar[idx]++;
                        }
                    // 1.11.336 (#13): the side of the group from the ship's own launchers instead of the gun-aim calibration (which
                    // "confirmed" a side whenever all enemies lay on one beam): a ship whose torpedo launchers all sit off the centre
                    // line has pure side groups - the right-side group fires to starboard, the left-side group to port; a ship with a
                    // centre-line (trainable) launcher is left alone, as is a ship whose launchers cannot be read
                    int side = LauncherSide(s, idx); if (side == 0) continue;
                    foreach (var kv in dict)
                    {
                        var pd = kv.Key; var a = kv.Value; if (pd == null || a == null || !pd.isTorpedo) continue;
                        var tg = a.target; if (tg == null || a.manual) continue;
                        if (SideSign(s, tg) == side) continue;
                        a.target = null; sideDrops++;
                        if (sideLogs++ < 30) BattleAi.Log($"{s.name}: {(idx == 0 ? "right" : "left")}-side torpedo aim on {tg.name} dropped - the target is on the other side ({(side > 0 ? "port" : "starboard")}); side from the launcher layout");
                    }
                }
            }
            catch (Exception e) { if (Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] torpedo sides: " + e.Message); }
        }

        // ---- 1.11.93: hopeless torpedo shot (player report 22.10: an enemy CA kept firing torpedoes without a chance)
        internal static int senseLogs, senseHolds;
        class Track { public UnityEngine.Vector3 pos, vel; public float t; }
        static readonly Dictionary<IntPtr, Track> tracks = new Dictionary<IntPtr, Track>();
        // ground velocity (m/s) of a ship estimated from its position between calls
        internal static UnityEngine.Vector3 Vel(Ship x, float now)
        {
            var tr = x.transform; if (tr == null) return UnityEngine.Vector3.zero;
            var p = tr.position; p.y = 0f;
            if (tracks.Count > 2000) tracks.Clear();
            if (!tracks.TryGetValue(x.Pointer, out var k)) { tracks[x.Pointer] = new Track { pos = p, t = now }; return UnityEngine.Vector3.zero; }
            float dt = now - k.t;
            if (dt > 30f) { k.pos = p; k.t = now; k.vel = UnityEngine.Vector3.zero; return k.vel; }
            if (dt >= 0.25f) { var v = (p - k.pos) / dt; k.vel = k.vel == UnityEngine.Vector3.zero ? v : UnityEngine.Vector3.Lerp(k.vel, v, 0.5f); k.pos = p; k.t = now; TorpedoLog.Velocity(x, k.vel); }
            return k.vel;
        }
        static bool TorpedoHopeless(Ship s, St st, float now, out string why)
        {
            why = null;
            var t = TorpedoTarget(s);
            if (t != null) st.senseTarget = t;
            else if (st.senseHold) t = st.senseTarget; // the game may drop the torpedo aim while the mode is Off
            if (t == null || t.isDead || t.isSinking) return false;
            bool surr = false; try { surr = t.IsSurrendered; } catch { }
            if (surr) { why = $"{t.name} has surrendered"; return true; } // 1.11.95
            float d = BattleAi.Dist(s, t);
            if (d <= DipGgFixesMod.TorpedoSenseClose.Value) return false;
            float range = BattleAi.TorpedoRange(s);
            if (range > 0f && d > range) { why = $"{t.name} at {d:0} m beyond torpedo range {range:0} m"; return true; }
            var ps = s.transform.position; var pt = t.transform.position; ps.y = 0f; pt.y = 0f;
            var P = pt - ps; var V = Vel(t, now); Vel(s, now);
            bool playerShip = false; try { playerShip = s.player != null && s.player.isMain && !s.isAiControlled; } catch { }
            if (!BattleAi.TorpedoWorthy(s, t))
            {
                // 1.11.99: self-defence - a small ship closing in (a torpedo attack on us) may be met with torpedoes inside 60% of
                // their range; only small targets that are far or not closing are not worth a torpedo
                bool closing = UnityEngine.Vector3.Dot(V, P) < -0.5f * P.magnitude; // closing at more than 0.5 m/s
                bool pointBlank = d <= 2500f && BattleAi.Ton(t) >= 1000f; // 1.11.206: anything alongside is worth a torpedo (24.10: a cruiser held its torpedoes with an enemy light cruiser 1.5 km away)
                if (!pointBlank && !(closing && range > 0f && d <= 0.6f * range)) { why = $"{t.name} is a small target at {d:0} m{(closing ? "" : ", not closing")}"; return true; }
            }
            // 1.11.294 (#36): the intercept run in the battle's own units. 1.11.93-293 took a fixed 20 m/s torpedo against the target's
            // velocity measured in world units per second (about 3.2 x m/s, Ship.ModifySpeedTorpedo), so a target moving away looked
            // nearly as fast as the torpedo and the run came out at tens or hundreds of km at 1.5-5 km ("run to the intercept point
            // 781547 m"). Now the same computation as the torpedo-setting choice: the game's velocity of the target and the torpedo's
            // own speed converted by the game (TorpedoSettings.InterceptRun / World); the fixed speed only when the ship reports none.
            // 1.11.301 (author 26.10, 20:21: "from 3 km one could fire boldly at such a large, sluggish target"): a capital ship inside
            // three quarters of the torpedo range is never a hopeless shot - a spread at a battleship or cruiser that size covers its
            // possible turns, so the intercept check is not applied
            bool bigTarget = false; try { bigTarget = BattleAi.IsCapital(t); } catch { }
            if (bigTarget && range > 0f && d <= range * 0.75f) return false;
            // 1.11.305 (author 26.10, 20:51: "the enemy CL should attack with torpedoes" - Gladiator and Wolf held at 4.5-4.7 km against
            // a 30-knot battleship, just past 3/4 of their range): up to 90 % of the range a large target is attacked unless it is
            // clearly running away from the torpedoes (its course opening the range at more than half its speed)
            if (bigTarget && range > 0f && d <= range * 0.9f)
            {
                float vmaxT = BattleAi.Speed(t); var Vt = V; if (vmaxT > 0f && Vt.magnitude > vmaxT * 1.15f) Vt = Vt.normalized * vmaxT * 1.15f;
                float opening = P.sqrMagnitude > 1f ? UnityEngine.Vector3.Dot(Vt, P.normalized) : 0f;
                if (opening < 0.5f * Math.Max(1f, vmaxT)) return false;
            }
            float ms = 0f; try { ms = s.TorpedoSpeed(false); } catch { }
            float vt = ms > 0.5f ? TorpedoSettings.World(ms) : TorpedoSettings.World(UnityEngine.Mathf.Max(5f, DipGgFixesMod.TorpedoSenseSpeed.Value));
            float run = TorpedoSettings.InterceptRun(s, t, vt);
            if (run <= 0f) { why = $"{t.name} at {d:0} m outruns the torpedo"; return true; }
            // 1.11.135: the player's own ships may use the full torpedo range (the 85% margin held shots at 22-50 m over the limit)
            float margin = playerShip ? UnityEngine.Mathf.Max(1f, DipGgFixesMod.TorpedoSenseMargin.Value) : DipGgFixesMod.TorpedoSenseMargin.Value;
            // 1.11.342 (#71, test 28.10 11:05: AI flotillas charging a battle line from 9 km lost 5 of 6 ships in about 4 minutes while
            // holding for a better solution - 0 and 1 torpedo hits): an AI ship that is hit or whose division is losing ships fires at a
            // large target with the full torpedo range instead of waiting under fire for a shorter run
            if (!playerShip && bigTarget)
            {
                float own = 0f; try { own = s.DamageEstimation(); } catch { }
                float div = 0f; try { if (s.division != null) div = BattleAi.DivDamage(s.division); } catch { }
                if (own >= 0.1f || div >= 0.15f) margin = UnityEngine.Mathf.Max(1f, margin);
            }
            float lim = range * margin * (st.senseHold ? 0.95f : 1f); // 1.11.99: hysteresis
            if (range > 0f && run > lim) { why = $"{t.name} at {d:0} m: run to the intercept point {run:0} m > {lim:0} m usable torpedo range"; return true; }
            return false;
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
                var main = BattleAi.MainBatteryGun(s) ?? s.mainGunsGroup;   // 1.11.446 (#89): the game's mainGunsGroup can point at a secondary gun - 432 mm turrets were then handled as secondaries
                bool heavy = false; try { var tn = s.shipType != null ? s.shipType.name : null; heavy = tn == "bb" || tn == "bc"; } catch { }
                MemoReset(); // 1.11.96
                EscortState(s, st, enemies);
                Apply(s, st, s.enemies, enemies, cache, main, heavy, +1);
                Apply(s, st, s.enemiesLeftSide, enemies, cache, main, heavy, -1);
            }
            catch (Exception e) { if (Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] SmartTargets: " + e.Message); }
        }
        static void Apply(Ship s, St st, Il2CppSystem.Collections.Generic.Dictionary<PartData, Ship.Aim> dict, Il2CppSystem.Collections.Generic.List<Ship> enemies, Il2CppSystem.Collections.Generic.Dictionary<PartData, float> cache, PartData main, bool heavy, int side = 0)
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
                // 1.11.250 (film 25.10, B5 "Why are we changing targets?"): a target the player gave (Ship.SetEnemy from a click or key) holds
                // until that ship is sunk or has surrendered - before, a player's order that matched the mod's own pick was taken for the mod's lock
                if (PlayerTargetOrders.Holds(s, IsMainGroup(pd, main))) { if (haveMine) st.ours.Remove(pd.Pointer); continue; }
                if (aim.manual)
                {
                    // a manual target that is not ours = the player's order: hands off
                    if (!haveMine || cur == null || cur.Pointer != mine) { if (haveMine) st.ours.Remove(pd.Pointer); continue; }
                }
                else if (haveMine) st.ours.Remove(pd.Pointer); // the game or the player cleared our lock
                float range = 0f; if (cache != null) cache.TryGetValue(pd, out range);
                if (range <= 0f) { Release(aim, st, pd); continue; }
                bool isMain = IsMainGroup(pd, main);
                // 1.11.252 (author, 25.10): escort first, transports after
                bool trOk = TransportAllowed(pd, range);
                skipTr = !trOk;
                Ship pick;
                // 1.11.259 (#37): the player's gun orders (class priorities, threat, split fire) come first
                var go = GunOrders.Choose(s, pd, isMain, side, enemies, range, cur, trOk);
                try { pick = go ?? (isMain ? PickMain(s, enemies, range, heavy, cur, true) : PickSecondary(s, enemies, range)); }
                finally { skipTr = false; }
                if (pick == null)
                {
                    // the game's own targeting would turn the guns on a transport: keep them on the escort (out of range = no fire)
                    if (!trOk && escortNear != null) pick = escortNear;
                    else { Release(aim, st, pd); continue; }
                }
                if (isMain) { try { st.playerPtr = s.player.Pointer; } catch { } st.mainTarget = pick.Pointer; st.mainAt = UnityEngine.Time.time; }
                if (cur != null && cur.Pointer == pick.Pointer)
                {
                    // same target: only (re)assert the lock, never touch the aiming progress
                    if (!aim.manual) aim.manual = true;
                    st.ours[pd.Pointer] = pick.Pointer; continue;
                }
                aim.target = pick; aim.manual = true; aim.progress = 0f; st.ours[pd.Pointer] = pick.Pointer; targetChanges++;
                if (tgtLogged < 40 || targetChanges % 200 == 0) { tgtLogged++; BattleAi.Log($"{s.name} {(isMain ? "main" : "secondary")} guns ({pd.caliber:0} mm): target {pick.name} ({pick.shipType.name}, dmg {Dmg(pick):0.00}) instead of {(cur != null ? cur.name : "none")} [{targetChanges} changes]"); }
                if (!trOk && cur != null && IsTr(cur) && trLogged++ < 40) BattleAi.Log($"{s.name} {(isMain ? "main" : "secondary")} guns ({pd.caliber:0} mm): escort first - off transport {cur.name}, on {pick.name} ({pick.shipType.name}, {D(s, pick) / 1000f:0.0} km)");
            }
        }
        // ---- 1.11.252 (author, 25.10): escort first, transports after. While enemy warships are present (seen in the last
        //      TransportEscortMemorySeconds), guns do not fire at transports; a gun up to TransportLowCaliberMax may take a transport
        //      only "by the way" - when no enemy warship is within its range x TransportEscortRangeMult. With the escort sunk,
        //      surrendered or long out of sight, every gun may fire at the transports. A player's own target order is not affected.
        static bool skipTr, escortPresent; static Ship escortNear; static float escortNearD; static int trLogged;
        internal static bool IsTr(Ship e) { try { var t = e != null ? e.shipType : null; return t != null && t.name == "tr"; } catch { return false; } }
        static void EscortState(Ship s, St st, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            escortPresent = false; escortNear = null; escortNearD = float.MaxValue;
            if (!DipGgFixesMod.TransportsAfterEscort.Value) return;
            bool alive = false, anyTr = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (e == null) continue;
                if (IsTr(e)) { anyTr = true; continue; }
                bool up = false; try { up = !e.isDead && !e.isSinking && !e.IsSurrendered; } catch { }
                if (!up) continue;
                alive = true;
                if (!U(s, e)) continue;
                float d = D(s, e); if (d < escortNearD) { escortNearD = d; escortNear = e; }
            }
            if (!anyTr || !alive) return;
            float now = UnityEngine.Time.time;
            if (escortNear != null) st.escortSeenAt = now;
            escortPresent = now - st.escortSeenAt <= DipGgFixesMod.TransportEscortMemorySeconds.Value;
        }
        static bool TransportAllowed(PartData pd, float range)
        {
            if (!escortPresent) return true;
            float cal = 0f; try { cal = pd.caliber; } catch { }
            if (cal <= 0f || cal > DipGgFixesMod.TransportLowCaliberMax.Value) return false;
            return escortNear == null || escortNearD > range * DipGgFixesMod.TransportEscortRangeMult.Value;
        }
        static bool UU(Ship s, Ship e) { return U(s, e) && !(skipTr && IsTr(e)); }
        // 1.11.250: the main battery may be split over several PartData (twin and triple turrets of the same gun); all of them are the
        //           main guns. AutoTest 25.10: Koenig's 305 mm in the other turret type were treated as secondary guns, picked the nearest
        //           cruiser and switched between the cruiser and the battleship every few seconds.
        static bool IsMainGroup(PartData pd, PartData main)
        {
            if (main == null || pd == null) return false;
            if (pd.Pointer == main.Pointer) return true;
            try { return main.caliber > 0f && Math.Abs(pd.caliber - main.caliber) < 1f; } catch { return false; }
        }
        // give the group back to the game's own targeting
        static void Release(Ship.Aim aim, St st, PartData pd)
        {
            if (st.ours.ContainsKey(pd.Pointer)) { st.ours.Remove(pd.Pointer); if (aim.manual) aim.manual = false; }
        }
        internal static float Dmg(Ship e) { try { return UnityEngine.Mathf.Clamp01(e.DamageEstimation()); } catch { return 0f; } }
        static bool Usable(Ship s, Ship e) { return e != null && !e.isDead && !e.isSinking && !e.IsSurrendered && e.IsVisibleFor(s); }
        // main guns: most damaged enemy in range; bb/bc: among the enemies with the heaviest calibre (within 25 mm of the max)
        // 1.11.96: per-call memo - the enemy data (visibility, distance, damage, calibre, torpedo range, penetration) is computed
        // once per SmartTargets call and shared by all gun groups of the ship (the 13-ship test spent ~1.8 ms per frame here)
        static readonly Dictionary<IntPtr, float> mDist = new Dictionary<IntPtr, float>(), mDmg = new Dictionary<IntPtr, float>(), mCal = new Dictionary<IntPtr, float>(), mTorp = new Dictionary<IntPtr, float>();
        static readonly Dictionary<IntPtr, bool> mUse = new Dictionary<IntPtr, bool>(), mNoPen = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<IntPtr, int> mShoot = new Dictionary<IntPtr, int>(), mShootLow = new Dictionary<IntPtr, int>();
        static int mLimit = -1;
        static void MemoReset() { mDist.Clear(); mDmg.Clear(); mCal.Clear(); mTorp.Clear(); mUse.Clear(); mNoPen.Clear(); mShoot.Clear(); mShootLow.Clear(); mLimit = -1; }
        static bool U(Ship s, Ship e) { if (e == null) return false; if (!mUse.TryGetValue(e.Pointer, out var v)) { v = Usable(s, e); mUse[e.Pointer] = v; } return v; }
        static float D(Ship s, Ship e) { if (!mDist.TryGetValue(e.Pointer, out var v)) { v = BattleAi.Dist(s, e); mDist[e.Pointer] = v; } return v; }
        static float Dm(Ship e) { if (!mDmg.TryGetValue(e.Pointer, out var v)) { v = Dmg(e); mDmg[e.Pointer] = v; } return v; }
        static float Cal(Ship e) { if (!mCal.TryGetValue(e.Pointer, out var v)) { v = BattleAi.MainCaliber(e); mCal[e.Pointer] = v; } return v; }
        static float Tr(Ship e) { if (!mTorp.TryGetValue(e.Pointer, out var v)) { v = BattleAi.TorpedoRange(e); mTorp[e.Pointer] = v; } return v; }
        static int Lim(Ship s) { if (mLimit < 0) mLimit = FireDistribution.Limit(s); return mLimit; }
        static int Sh(Ship s, Ship e, bool low) { var m = low ? mShootLow : mShoot; if (!m.TryGetValue(e.Pointer, out var v)) { v = FireDistribution.Shooters(s, e, low); m[e.Pointer] = v; } return v; }
        static float Prio(Ship s, Ship e, float d, float maxCal)
        {
            if (!mNoPen.TryGetValue(e.Pointer, out var noPen)) { float pen, belt; noPen = BattleAi.CannotPenetrate(s, e, d, out pen, out belt); mNoPen[e.Pointer] = noPen; }
            float power = maxCal > 0f ? Cal(e) / maxCal : 0.5f;
            return power * (noPen ? 0.3f : 1f);
        }
        static Ship PickMain(Ship s, Il2CppSystem.Collections.Generic.List<Ship> enemies, float range, bool heavy, Ship cur = null, bool distribute = false)
        {
            var inRange = new List<Ship>();
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (UU(s, e) && D(s, e) <= range) inRange.Add(e); }
            if (inRange.Count == 0) return null;
            float maxCal = 0f; foreach (var e in inRange) { float c = Cal(e); if (c > maxCal) maxCal = c; }
            // 1.11.88: fire distribution (War Instructions 1944): at most N own ships on one target, 1 when outnumbered; the
            // current target is kept so the aiming progress is not lost
            bool dist = distribute && DipGgFixesMod.FireDistributionOn.Value;
            // 1.11.92: a close enemy is always a candidate and is preferred (player report 22.10: three CAs fired at a light
            // cruiser over ten km away while an enemy CA 4 km off went untouched - the fire distribution limit had pushed them
            // off the close target because the battleships were already on it)
            float closeD = UnityEngine.Mathf.Max(5000f, 0.3f * range);
            if (dist && inRange.Count > 1)
            {
                int lim = Lim(s);
                // the current target is kept unless more than the limit are on it; then the ships with the lowest ids keep it (no oscillation)
                var free = inRange.FindAll(e => D(s, e) <= closeD || ((cur != null && e.Pointer == cur.Pointer) ? Sh(s, e, true) < lim : Sh(s, e, false) < lim));
                if (free.Count > 0) inRange = free;
            }
            if (heavy)
            {
                // 1.11.91: heaviest calibre among the targets still free under the fire distribution limit (test: 4 BB on one CA)
                float maxFree = 0f; foreach (var e in inRange) { float c = Cal(e); if (c > maxFree) maxFree = c; }
                if (maxFree > 0f) inRange.RemoveAll(e => Cal(e) < maxFree - 25f && D(s, e) > closeD);
            }
            Ship best = null; float bestScore = float.MinValue, curScore = float.MinValue;
            foreach (var e in inRange)
            {
                float d = D(s, e);
                float score = Dm(e) * 10f - 2f * d / range; // damage first ("sink one rather than damage two"), then proximity
                if (dist) score += 2f * Prio(s, e, d, maxCal); // hitting power x weak protection (12166)
                if (d <= closeD) score += 4f * (1f - d / closeD); // 1.11.92: the close enemy is the immediate danger
                if (cur != null && e.Pointer == cur.Pointer) curScore = score;
                if (score > bestScore) { bestScore = score; best = e; }
            }
            // 1.11.92: hysteresis - keep the current target unless the new one is clearly better (stops switching back and forth)
            if (cur != null && best != null && best.Pointer != cur.Pointer && curScore > float.MinValue && bestScore - curScore < 0.75f) return cur;
            return best;
        }
        // secondary guns: nearest torpedo threat (enemy with torpedoes inside its torpedo range x mult), else most damaged in range
        static Ship PickSecondary(Ship s, Il2CppSystem.Collections.Generic.List<Ship> enemies, float range)
        {
            Ship threat = null; float threatD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!UU(s, e) || !e.haveTorpedoes) continue;
                float d = D(s, e); if (d > range) continue;
                float tr = Tr(e) * DipGgFixesMod.TorpedoThreatRangeMult.Value;
                if (d <= tr && d < threatD) { threatD = d; threat = e; }
            }
            if (threat != null) return threat;
            // 1.11.121: no torpedo threat - the nearest enemy in range (secondary guns lose and regain the range fastest and do most
            // at short range; player report 23.10: a close enemy was ignored for a damaged ship far away); damage only decides
            // between enemies at about the same distance (within 15% or 1 km of the nearest)
            Ship near = null; float nearD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (!UU(s, e)) continue; float d = D(s, e); if (d <= range && d < nearD) { nearD = d; near = e; } }
            if (near == null) return null;
            float band = UnityEngine.Mathf.Max(nearD * 1.15f, nearD + 1000f); Ship best = near; float bestDm = Dm(near);
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; if (!UU(s, e)) continue; float d = D(s, e); if (d > band || d > range) continue; float dm = Dm(e); if (dm > bestDm + 0.05f) { bestDm = dm; best = e; } }
            return best;
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
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { ShipTick.Pre(__instance); if (t0 != 0 && __instance != null) Perf.Ship(__instance.Pointer); }
            catch (Exception e) { if (ShipTick.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AimAndShoot prefix: " + e.Message); }
            finally { if (t0 != 0) Perf.Add(Perf.AIM, t0); }
        }
        private static void Postfix(Ship __instance)
        {
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { ShipTick.Post(__instance); }
            catch (Exception e) { if (ShipTick.Errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] AimAndShoot postfix: " + e.Message); }
            finally { if (t0 != 0) Perf.Add(Perf.AIM, t0); }
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
        [ThreadStatic] static bool tweaked;
        static int logged;
        private static void Prefix(Ship __instance)
        {
            Current = __instance; tweaked = false; if (!CrewLevels.Done) CrewLevels.Apply();
            // 1.11.96: the threshold parameter itself is set for this call (no hook on MonoBehaviourExt.Param)
            try
            {
                if (__instance != null && DipGgFixesMod.SurrenderByTraining.Value)
                {
                    float t = Threshold(__instance);
                    tweaked = ParamTweak.Begin("crew_percents_surrender_threshold", t);
                    if (tweaked && logged < 5) { logged++; BattleAi.Log($"surrender threshold for {__instance.name}: {t:0.00} (vanilla {ParamTweak.Base("crew_percents_surrender_threshold", 0f):0.00}, crew rank {CrewLevels.Rank(__instance):0.00})"); }
                }
            }
            catch { }
        }
        private static void Finalizer() { if (tweaked) { tweaked = false; ParamTweak.End("crew_percents_surrender_threshold"); } Current = null; }
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
                var owner = ship.player; if (owner == null) return;
                var opponent = battle.OppositePlayer(owner); if (opponent == null) return;
                // 1.11.186: only ships that really fought in this battle (a player reported ships captured in earlier years and not
                // present in a battle without combat being "taken as a prize" again - the surrendered flag survives repair)
                // 1.11.290 (#35): a ship that surrendered is counted as lost by the game and is already moved from the battle's ship
                // list to its sunk list (CampaignBattle.SunkShips) when DamageShip runs, so all three lists are searched
                string where = null;
                try { if (In(battle.ShipsOf(owner, true), ship)) where = "actual"; } catch { }
                try { if (where == null && In(battle.ShipsOf(owner, false), ship)) where = "ships"; } catch { }
                try { if (where == null && In(battle.SunkShips, ship)) where = "sunk"; } catch { }
                if (where == null) { MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name}: surrendered flag but not in this battle - left as it is"); return; }
                if (logged < 30) MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name}: surrendered in this battle (battle list: {where})");
                // 1.11.50: surrendered ships go to the side that won the battle; a ship whose own side won returns to its owner
                Player victor = null; float vpA = 0f, vpD = 0f; string how = "condition";
                try { victor = battle.ConditionWinner; } catch { }
                if (victor == null)
                {
                    how = "victory points";
                    try { vpA = battle.VictoryPointsAttacker; vpD = battle.VictoryPointsDefender; } catch { }
                    Player att = null, def = null; try { att = battle.Attacker; def = battle.Defender; } catch { }
                    if (vpA > vpD) victor = att; else if (vpD > vpA) victor = def;
                }
                bool ownSideWon = victor != null && (victor.Pointer == owner.Pointer || battle.OppositePlayer(victor) != null && battle.OppositePlayer(victor).Pointer == opponent.Pointer && victor.Pointer != opponent.Pointer);
                // 1.11.186: a battle without a winner (draw, no combat) captures nothing - the ship stays with its owner
                if (victor == null) { ownSideWon = true; how = $"no winner (VP {vpA:0}/{vpD:0}) - not captured"; }
                if (ownSideWon && (victor == null || DipGgFixesMod.SurrenderReturnToWinner.Value))
                {
                    var cc0 = CampaignController.Instance; if (cc0 == null) return;
                    int rem = 0;
                    try { var l1 = battle.AttackerShipsSink; if (l1 != null && l1.Remove(ship)) rem++; } catch { }
                    try { var l2 = battle.DefenderShipsSink; if (l2 != null && l2.Remove(ship)) rem++; } catch { }
                    try { var l3 = battle.sunkShipsCached; if (l3 != null && l3.Remove(ship)) rem++; } catch { }
                    float hp0 = 0f, crew0 = 0f; try { hp0 = ship.HP; } catch { } try { crew0 = ship.CrewPercents; } catch { }
                    float minCrew0 = 0.7f; try { minCrew0 = UnityEngine.Mathf.Max(0.5f, MonoBehaviourExt.Param("min_crew_threshold", 0.7f)); } catch { }
                    try { if (crew0 < minCrew0) ship.CrewPercents = minCrew0; } catch { }
                    // 1.11.95: capped below 1.00 - damage 1.00 sinks the ship (22.10: DD V-2 "returned" with damage 1.00 and was lost)
                    __1 = UnityEngine.Mathf.Min(0.95f, UnityEngine.Mathf.Max(__1, UnityEngine.Mathf.Clamp(DipGgFixesMod.CaptureDamage.Value, 0.05f, 0.95f)));
                    Captured.Add(ship.Pointer);
                    try { Narrative.Record("prize|" + ship.Pointer, 9, $"{ship.name.Split(new[] { " (" }, StringSplitOptions.None)[0]} had surrendered, but her side won - she returns to {owner.data.name}"); } catch { }
                    MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name} ({ship.shipType.name}) surrendered but {owner.data.name} won the battle ({how}) - ship returns to its owner; removed from {rem} sunk list(s), HP {hp0:0}, crew {crew0:0.00} -> {UnityEngine.Mathf.Max(crew0, minCrew0):0.00}, damage set to {__1:0.00}, status {ship.status}");
                    return;
                }
                var loser = owner; var winner = opponent;
                MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name}: battle won by {(victor != null ? victor.data.name : "nobody")} ({how})");
                // the winner must still have ships in the battle
                var ws = battle.ShipsOf(winner, true); int alive = 0;
                if (ws != null) for (int i = 0; i < ws.Count; i++) { var w = ws[i]; if (w != null && !w.isDead && !w.isSinking && !w.isSunk && !w.IsSurrendered) alive++; }
                if (alive == 0) { MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name} surrendered but {winner.data.name} has no ships left in the battle - not captured"); return; }
                // 1.11.351 (author 28.10, 17:44): a wreck, or a ship whose trained crew will not hand her over, is not taken
                if (PrizeScuttle.Lost(ship, __1, winner.data.name)) return;
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
                // 1.11.229 (author, 24.10; promised to hurepoix): the prize crew comes out of the winner's crew pool. The men needed to bring
                //          the crew up to the sailing minimum are taken from the pool; when the pool is short, the prize gets what the pool
                //          has (never below half the minimum - a skeleton crew still brings her in).
                try
                {
                    if (crew < minCrew)
                    {
                        float total = 0f; try { total = ship.GetTotalCrew(); } catch { }
                        float pool = 0f; try { pool = winner.crewPool; } catch { }
                        int need = total > 0f ? (int)Math.Ceiling((minCrew - Math.Max(0f, crew)) * total) : 0;
                        int give = need; float target = minCrew;
                        if (need > 0 && pool < need)
                        {
                            give = Math.Max(0, (int)pool);
                            target = total > 0f ? Math.Max(minCrew * 0.5f, Math.Max(0f, crew) + give / total) : minCrew;
                        }
                        ship.CrewPercents = target;
                        if (give > 0) { try { winner.crewPool = (int)(pool - give); } catch { } }
                        MelonLogger.Msg($"[DIP GG Fixes][Capture] prize crew for {ship.name}: {give} men from {winner.data.name}'s crew pool ({pool:0} available, {need} needed to reach {minCrew:P0}); crew now {target:P0}");
                    }
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] prize crew: " + e.Message); }
                __1 = UnityEngine.Mathf.Clamp(DipGgFixesMod.CaptureDamage.Value, 0.05f, 0.95f);
                Captured.Add(ship.Pointer);
                try { PrizeCrew[ship.Pointer] = UnityEngine.Mathf.Max(crew, ship.CrewPercents); } catch { PrizeCrew[ship.Pointer] = minCrew; }
                try { Narrative.Record("prize|" + ship.Pointer, 9, $"{ship.name.Split(new[] { " (" }, StringSplitOptions.None)[0]} was taken as a prize by {winner.data.name}"); } catch { }
                MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name} ({ship.shipType.name}) surrendered to {winner.data.name} and was taken as a prize ({loser.data.name} -> {winner.data.name}); removed from {removed} sunk list(s), HP {hp:0}, crew {crew:0.00} -> {UnityEngine.Mathf.Max(crew, minCrew):0.00}, damage set to {__1:0.00}, crew training reset, status {ship.status}, new owner {(ship.player != null ? ship.player.data.name : "?")}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] DamageShip prefix: " + e.Message); }
        }
        static bool In(Il2CppSystem.Collections.Generic.List<Ship> l, Ship s)
        {
            if (l == null || s == null) return false;
            for (int i = 0; i < l.Count; i++) { var o = l[i]; if (o != null && o.Pointer == s.Pointer) return true; }
            return false;
        }
        internal static readonly HashSet<IntPtr> Captured = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, float> PrizeCrew = new Dictionary<IntPtr, float>();
        private static void Postfix(Ship __0)
        {
            try
            {
                if (__0 == null || !Captured.Contains(__0.Pointer)) return;
                // 1.11.332 (#61, 1922: BB Caesar taken as a prize with crew 0.70 came out of DamageShip with crew 0.00 and status LowCrew;
                // the torpedo boats lost 0.70 -> 0.52-0.56): DamageShip removes crew in proportion to the damage after the prize crew was
                // put aboard. The prize keeps the crew it was given; a ship left LowCrew goes to Repairing like the other prizes.
                if (PrizeCrew.TryGetValue(__0.Pointer, out var keep))
                {
                    PrizeCrew.Remove(__0.Pointer);
                    float now = 0f; try { now = __0.CrewPercents; } catch { }
                    if (now < keep - 0.005f)
                    {
                        try { __0.CrewPercents = keep; } catch { }
                        bool low = false; try { low = __0.status == VesselEntity.Status.LowCrew; } catch { }
                        if (low) { try { __0.status = VesselEntity.Status.Repairing; } catch { } }
                        MelonLogger.Msg($"[DIP GG Fixes][Capture] {__0.name}: prize crew put back after DamageShip ({now:0.00} -> {keep:0.00}){(low ? ", status LowCrew -> Repairing" : "")}");
                    }
                }
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
        private static void Prefix(Player __0, string __1) { ConquestCtx.NewOwner = __0; ConquestCtx.ProvinceId = __1; Independence.BeforeTransfer(__0, __1); }
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
                // 1.11.72: a great power losing its last port keeps its ships in an allied port
                if (ConquestCtx.NewOwner != null && __0 != null && __0.player != null && Portless.TryRelocate(__0, __0.player, ConquestCtx.ProvinceId)) return false;
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
    // 1.11.15: the scan uses designsAll - Player.designs hides designs the owner cannot
    // build (16:4x log: Russia received three identical clones of a Romanian CL class, the
    // count stayed 3 -> 3), so duplicates were created.
    // =====================================================================================
    internal static class PrizeDesign
    {
        // 1.11.353 (#72, author 28.10, 17:56: "I have the Hibernia design twice" - Caesar and Rodney, both captured Hibernias, each on
        // her own copy): a design copied to a new owner is renamed with the owner's tag ("BB Hibernia [britain]" -> "BB Hibernia
        // [germany]"), so the search for an earlier copy of the class compared different names and cloned again. Names are compared
        // without the nation tag, and the ships on identical copies are moved to one design before the unused copies are removed.
        static string ClassName(string n) { if (n == null) return null; int i = n.LastIndexOf(" ["); return (i > 0 && n.EndsWith("]") ? n.Substring(0, i) : n).Trim(); }
        static int Relink(Player owner, Ship keep, string hullName, float ton)
        {
            int moved = 0;
            try
            {
                string kn = ClassName(keep.name);
                // 1.11.355 (campaign 28.10, 18:29: "design merge: Collection was modified") - setting a ship's design changes the owner's
                // fleet collection, so the ships are listed first and changed after the enumeration
                var fleet = new List<Ship>();
                var ef = owner.fleet.GetEnumerator(); var uf = ef.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (uf.MoveNext()) { var s0 = ef.Current; if (s0 != null) fleet.Add(s0); }
                foreach (var s in fleet)
                {
                    Ship d = null; try { d = s.design; } catch { }
                    if (d == null || d.Pointer == keep.Pointer) continue;
                    var dh = d.hull; if (dh == null || dh.name != hullName || ClassName(d.name) != kn) continue;
                    float dt = 0f; try { dt = d.Tonnage(); } catch { }
                    if (Math.Abs(dt - ton) > Math.Max(50f, ton * 0.01f)) continue;
                    try { s.design = keep; moved++; } catch { }
                }
            }
            catch { }
            return moved;
        }
        // once per campaign session: merge the identical copies of captured classes the player already has (saves from before 1.11.353)
        static bool merged; static int mergeTries; static float mergeAt = -1f;
        internal static void SessionMerge()
        {
            if (merged) return;
            try
            {
                if (!GameManager.IsCampaign || CampaignController.Instance == null) { mergeAt = -1f; return; }
                // 1.11.355 (campaign 28.10, 18:29: "design merge: Collection was modified" - it ran while the campaign was still loading,
                // the game logged an error from Player.designsAll): the merge waits until loading is over and 20 s more, and is tried
                // again up to three times when it fails
                bool loading = false; try { loading = GameManager.IsLoadingAny; } catch { }
                if (loading) { mergeAt = -1f; return; }
                if (mergeAt < 0f) { mergeAt = UnityEngine.Time.unscaledTime + 20f; return; }
                if (UnityEngine.Time.unscaledTime < mergeAt) return;
                Player me = null; try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.isMain) { me = p; break; } } } catch { }
                if (me == null) return;
                merged = true;
                var seen = new Dictionary<string, Ship>(); int moved = 0, removed = 0;
                var all = new List<Ship>();
                var en = me.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var x = en.Current; if (x != null) all.Add(x); }
                foreach (var x in all)
                {
                    var h = x.hull; if (h == null) continue;
                    float t = 0f; try { t = x.Tonnage(); } catch { }
                    string key = h.name + "|" + ClassName(x.name) + "|" + Math.Round(t / Math.Max(50f, t * 0.01f));
                    if (!seen.TryGetValue(key, out var keep)) { seen[key] = x; continue; }
                    int m = Relink(me, keep, h.name, t); moved += m;
                    if (m > 0) removed += DedupePrizeDesigns(me, keep, h.name, t, "Capture");
                }
                MelonLogger.Msg($"[DIP GG Fixes][Capture] design copies merged for {me.data.name}: {moved} ship(s) moved to one design per class, {removed} copy(ies) removed");
            }
            catch (Exception e) { merged = ++mergeTries >= 3; mergeAt = UnityEngine.Time.unscaledTime + 30f; MelonLogger.Warning($"[DIP GG Fixes][Capture] design merge (try {mergeTries}{(merged ? ", giving up" : ", again in 30 s")}): " + e.Message); }
        }
        // 1.11.339: removes designs of the captor that are copies of `keep` (same hull, tonnage within 1 %, same design name) and that
        // no ship of the captor uses; returns how many were removed
        static int DedupePrizeDesigns(Player owner, Ship keep, string hullName, float ton, string tag)
        {
            int removed = 0;
            try
            {
                if (owner == null || keep == null || hullName == null) return 0;
                string keepName = ClassName(keep.name);
                var used = new HashSet<IntPtr>();
                try { var ef = owner.fleet.GetEnumerator(); var uf = ef.Cast<Il2CppSystem.Collections.IEnumerator>(); while (uf.MoveNext()) { var s = ef.Current; try { if (s != null && s.design != null) used.Add(s.design.Pointer); } catch { } } } catch { }
                var dups = new List<Ship>();
                var en = owner.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (un.MoveNext())
                {
                    var x = en.Current; if (x == null || x.Pointer == keep.Pointer) continue;
                    var xh = x.hull; if (xh == null || xh.name != hullName) continue;
                    if (ClassName(x.name) != keepName) continue;
                    float xt = 0f; try { xt = x.Tonnage(); } catch { }
                    if (Math.Abs(xt - ton) > Math.Max(50f, ton * 0.01f)) continue;
                    if (used.Contains(x.Pointer)) continue;
                    dups.Add(x);
                }
                foreach (var x in dups) { try { VesselEntity.TryToEraseVessel(x); removed++; } catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] removing a copy of {keepName}: " + e.Message); } }
            }
            catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] design dedupe: " + e.Message); }
            return removed;
        }
        internal static void Ensure(Ship ship, Player recipient, string tag)
        {
            try
            {
                if (ship == null || recipient == null) return;
                Ship d = null; try { d = ship.design; } catch { }
                if (d == null) d = ship;
                PrizeTech.Note(d, recipient); // 1.11.333 (#50)
                var hull = d.hull; string hullName = hull != null ? hull.name : null;
                float ton = 0f; try { ton = d.Tonnage(); } catch { }
                int count = 0; Ship found = null, self = null;
                try
                {
                    var en = recipient.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var x = en.Current; if (x == null) continue; count++;
                        if (x.Pointer == d.Pointer) { self = x; continue; }   // 1.11.339: an earlier copy of the class is preferred
                        if (found != null) continue;
                        var xh = x.hull; if (xh == null || hullName == null || xh.name != hullName) continue;
                        if (ClassName(x.name) != ClassName(d.name)) continue;   // 1.11.339: the same class (design name), not another design on the same hull; 1.11.353: without the nation tag
                        float xt = 0f; try { xt = x.Tonnage(); } catch { }
                        if (Math.Abs(xt - ton) <= Math.Max(50f, ton * 0.01f)) found = x;
                    }
                }
                catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] design scan: " + e.Message); }
                if (found == null) found = self;
                if (found != null)
                {
                    // 1.11.339 (player report 28.10: 3 captured CL Boleslaw came with 3 identical designs, each linked to one ship, so
                    // every refit had to be designed 3 times; 10 TB Orel came as 1/1/8): the prize is linked to the design the captor
                    // already has for her class, and the identical copy the transfer left in the design list is removed when no ship
                    // uses it any more
                    Ship before = null; try { before = ship.design; } catch { }
                    if (before == null || before.Pointer != found.Pointer) { try { ship.design = found; } catch { } }
                    Relink(recipient, found, hullName, ton); // 1.11.353
                    int removed = DedupePrizeDesigns(recipient, found, hullName, ton, tag);
                    MelonLogger.Msg($"[DIP GG Fixes][{tag}] {recipient.data.name} already has a design for {ship.name} ({found.name}, hull {hullName}) - the ship is linked to it{(removed > 0 ? $", {removed} identical copy(ies) removed" : "")}; {count} design(s) in list");
                    return;
                }
                var pc = PlayerController.Instance; if (pc == null) { MelonLogger.Msg($"[DIP GG Fixes][{tag}] no PlayerController - design not cloned"); return; }
                var clone = pc.CloneDesign(d, recipient);
                int after = 0; try { var en2 = recipient.designsAll.GetEnumerator(); var un2 = en2.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un2.MoveNext()) after++; } catch { }
                if (clone != null) { try { ship.design = clone; } catch { } }
                MelonLogger.Msg($"[DIP GG Fixes][{tag}] design of {ship.name} (hull {hullName}, {ton:0} t) cloned to {recipient.data.name} as {(clone != null ? clone.name : "null")}; designs {count} -> {after}");
            }
            catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][{tag}] design clone failed: " + e.Message); }
        }
    }


    // =====================================================================================
    // 1.11.7: national research profiles. Mods/techBonuses.csv holds, per nation and
    // technology category (technologies.csv column "type"), a percentage added to the
    // research speed. CampaignController.GetResearchSpeed(Player, Technology) is used both
    // by the monthly research update (UpdatePlayerTech) and by the research window, so the
    // player sees the modified speed and the AI gets it too.
    // =====================================================================================
    internal static class TechBonuses
    {
        internal static readonly Dictionary<string, Dictionary<string, float>> ByNation = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        internal static bool Loaded;
        static readonly HashSet<string> logged = new HashSet<string>();

        internal static void Load()
        {
            try
            {
                string path = Path.Combine(MelonUtils.GameDirectory, "Mods", "techBonuses.csv");
                if (!File.Exists(path)) { MelonLogger.Msg("[DIP GG Fixes] techBonuses.csv not found - no national research profiles"); return; }
                int n = 0;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("nation,")) continue;
                    var c = line.Split(',');
                    if (c.Length < 3) continue;
                    string nation = c[0].Trim().ToLowerInvariant(), cat = c[1].Trim().ToLowerInvariant();
                    if (!float.TryParse(c[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct)) continue;
                    if (!ByNation.TryGetValue(nation, out var d)) { d = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase); ByNation[nation] = d; }
                    d[cat] = pct; n++;
                }
                Loaded = n > 0;
                MelonLogger.Msg($"[DIP GG Fixes] Loaded {n} national research modifiers for {ByNation.Count} nation(s) from techBonuses.csv");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes] techBonuses.csv: " + e.Message); }
        }

        internal static float Factor(string nation, string category)
        {
            if (!Loaded || nation == null || category == null) return 1f;
            if (!ByNation.TryGetValue(nation, out var d) || !d.TryGetValue(category, out var pct)) return 1f;
            float scale = DipGgFixesMod.NationalTechScale != null ? DipGgFixesMod.NationalTechScale.Value : 1f;
            return Math.Max(0.1f, 1f + pct * scale / 100f);
        }

        internal static void LogOnce(string nation, string category, float before, float after)
        {
            string key = nation + "|" + category;
            if (logged.Count > 200 || logged.Contains(key)) return;
            logged.Add(key);
            MelonLogger.Msg($"[DIP GG Fixes][Tech] {nation} {category}: research speed {before:0.###} -> {after:0.###}");
        }
    }

    [HarmonyPatch(typeof(CampaignController), "GetResearchSpeed", new Type[] { typeof(Player), typeof(Technology) })]
    internal static class Patch_GetResearchSpeed
    {
        private static void Postfix(Player __0, Technology __1, ref float __result)
        {
            try
            {
                if (DipGgFixesMod.NationalTech == null || !DipGgFixesMod.NationalTech.Value || !TechBonuses.Loaded) return;
                if (__0 == null || __1 == null || __0.data == null || __1.data == null) return;
                string nation = __0.data.name, cat = __1.data.type;
                float f = TechBonuses.Factor(nation, cat);
                if (f == 1f) return;
                float before = __result; __result *= f;
                TechBonuses.LogOnce(nation, cat, before, __result);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Tech] GetResearchSpeed postfix: " + e.Message); }
        }
    }


    // =====================================================================================
    // 1.11.9: update check. Reads Mods/IRON_ADMIRAL_VERSION.txt (installed package version,
    // written by the release script) and fetches
    // https://raw.githubusercontent.com/KlaudiuszLedwoch/Iron-Admiral/main/VERSION in the
    // background. When the remote version is newer, a message box is shown once the game UI
    // exists (Ui.ReportMessage), and the log gets a line either way. Opt-out: CheckForUpdates.
    // =====================================================================================
    internal static class UpdateCheck
    {
        // 1.11.108: two release lines - stable (branch main) and beta (branch beta, versions "1.2.N-beta.K").
        // UpdateChannel: "auto" follows the installed package (a beta package checks the beta line), "stable" or "beta".
        internal static MelonPreferences_Entry<string> Channel;
        // 1.11.123: switch in the F10 panel (the panel shows on/off settings only): on = the update check offers beta versions,
        // off = stable versions (also back from a beta to the latest stable). Set once to "on" when a beta package is installed.
        internal static MelonPreferences_Entry<bool> Beta, BetaInit;
        internal static MelonPreferences_Entry<bool> BetaSwitch => Beta;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Channel = cat.CreateEntry("UpdateChannel", "auto", null, "Release line the update check follows: auto (the Beta updates switch decides), stable or beta");
            Beta = cat.CreateEntry("BetaUpdates", false, null, "Beta updates (for testers - needs Share logs on): the update check offers beta versions (new features under test); off = stable versions, also back from a beta to the latest stable. Takes effect at the next start");
            BetaInit = cat.CreateEntry("BetaUpdatesInitialised", false, null, "internal: the Beta updates switch was set from the installed package once", true);
            DipGgFixesMod.RelationRepairOn = cat.CreateEntry("RelationRepair", true, null, "Restore missing relation records between great powers (a neutral relation is added for a pair with no record; existing relations are never changed)");
        }
        static bool LocalIsBeta => local != null && local.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) >= 0;
        static bool BetaLine
        {
            get
            {
                string c = "auto"; try { c = (Channel != null ? Channel.Value : "auto").Trim().ToLowerInvariant(); } catch { }
                if (c == "beta") return true; if (c == "stable") return false;
                // 1.11.139: Beta updates are the reward for log sharing (testers)
                bool tester = !LogShare.Available || (LogShare.On != null && LogShare.On.Value); // until the log reception runs, betas stay open to everyone
                try { if (Beta != null) return Beta.Value && tester; } catch { }
                return LocalIsBeta && tester;
            }
        }
        static string Url => "https://raw.githubusercontent.com/KlaudiuszLedwoch/Iron-Admiral/" + (BetaLine ? "beta" : "main") + "/VERSION";
        const string Page = "https://github.com/KlaudiuszLedwoch/Iron-Admiral/releases";
        static volatile string remote, remoteDll, remoteSha, remoteZipUrl; static volatile bool done, shown;
        static volatile string resultText; static volatile bool resultShown;
        static string local = "?", localDll = "?", loadedDll = null;
        static float firstTry, settledAt;
        static string ModsDir => Path.Combine(MelonUtils.GameDirectory, "Mods");

        internal static void Start()
        {
            try
            {
                // 1.11.11: remove the previous DLL left behind by a self-update
                try { foreach (var old in Directory.GetFiles(ModsDir, "*.old", SearchOption.AllDirectories)) { try { File.Delete(old); MelonLogger.Msg("[DIP GG Fixes][Update] removed " + Path.GetFileName(old) + " from the previous update"); } catch (Exception e) { MelonLogger.Msg("[DIP GG Fixes][Update] could not remove " + Path.GetFileName(old) + ": " + e.Message); } } } catch { }
                if (DipGgFixesMod.CheckUpdates == null || !DipGgFixesMod.CheckUpdates.Value) { MelonLogger.Msg("[DIP GG Fixes][Update] check disabled"); done = true; return; }
                try
                {
                    string vf = Path.Combine(ModsDir, "IRON_ADMIRAL_VERSION.txt");
                    if (File.Exists(vf)) { var l = File.ReadAllLines(vf); if (l.Length > 0) local = l[0].Trim(); if (l.Length > 1) localDll = l[1].Trim(); }
                }
                catch { }
                // 1.11.295 (#55): the marker names the package, not the DLL that is running - compare with the loaded assembly
                try
                {
                    var v = typeof(DipGgFixesMod).Assembly.GetName().Version;
                    loadedDll = $"DIP_GG_Fixes {v.Major}.{v.Minor}.{v.Build}";
                    if (!string.IsNullOrEmpty(localDll) && localDll != loadedDll)
                        MelonLogger.Warning($"[DIP GG Fixes][Update] installed package {local} names {localDll}, but the loaded DLL is {loadedDll} - the Mods folder holds a different DLL than the package; reinstall the package");
                }
                catch { }
                // a beta package switches the Beta updates option on once (later the player decides in F10)
                try { if (Beta != null && BetaInit != null && !BetaInit.Value) { BetaInit.Value = true; if (LocalIsBeta) Beta.Value = true; MelonPreferences.Save(); } } catch { }
                MelonLogger.Msg($"[DIP GG Fixes][Update] release line: {(BetaLine ? "beta" : "stable")}");
                System.Threading.Tasks.Task.Run(async () =>
                {
                    // 1.11.12: the first HTTPS request from the game process can take well over 8 s (TLS and DNS
                    // warm-up; 2026-10-16 log: timeout at 8 s), so 30 s and one retry after 20 s.
                    for (int attempt = 1; attempt <= 2 && remote == null; attempt++)
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        using (var hc = new System.Net.Http.HttpClient())
                        {
                            hc.Timeout = TimeSpan.FromSeconds(30);
                            hc.DefaultRequestHeaders.UserAgent.ParseAdd("IronAdmiral-DIP_GG_Fixes");
                            string s = await hc.GetStringAsync(Url);
                            MelonLogger.Msg($"[DIP GG Fixes][Update] VERSION fetched in {sw.ElapsedMilliseconds} ms (attempt {attempt})");
                            var lines = s.Replace("\r", "").Split('\n');
                            remote = lines.Length > 0 ? lines[0].Trim() : null;
                            remoteDll = lines.Length > 1 ? lines[1].Trim() : "";
                            // VERSION lines 4+: "sha256_mods_only <hex>" and "url_mods_only <url>" (written by the release script)
                            foreach (var ln in lines)
                            {
                                var p = ln.Trim().Split(new[] { ' ' }, 2);
                                if (p.Length == 2 && p[0] == "sha256_mods_only") remoteSha = p[1].Trim().ToLowerInvariant();
                                if (p.Length == 2 && p[0] == "url_mods_only") remoteZipUrl = p[1].Trim();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        MelonLogger.Msg($"[DIP GG Fixes][Update] check failed (attempt {attempt}): " + e.Message);
                        if (attempt == 1) await System.Threading.Tasks.Task.Delay(20000);
                    }
                    done = true;
                });
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Update] " + e.Message); done = true; }
        }

        // "1.2.66" > "1.2.66-beta.3" > "1.2.66-beta.2" > "1.2.65" (a release is newer than its own betas)
        internal static bool Newer(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            a = Num(a); b = Num(b); // 1.11.152: a version written as "Iron Admiral 1.2.67-beta.2" counted as 0 - "update available" for the installed version
            string ca = a, cb = b; int ba = int.MaxValue, bb = int.MaxValue;
            int ia = a.IndexOf("-beta", StringComparison.OrdinalIgnoreCase); if (ia >= 0) { ca = a.Substring(0, ia); var t = a.Substring(ia + 5).TrimStart('.'); if (!int.TryParse(t, out ba)) ba = 0; }
            int ib = b.IndexOf("-beta", StringComparison.OrdinalIgnoreCase); if (ib >= 0) { cb = b.Substring(0, ib); var t = b.Substring(ib + 5).TrimStart('.'); if (!int.TryParse(t, out bb)) bb = 0; }
            var pa = ca.Split('.'); var pb = cb.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = 0, y = 0;
                if (i < pa.Length) int.TryParse(pa[i], out x);
                if (i < pb.Length) int.TryParse(pb[i], out y);
                if (x != y) return x > y;
            }
            return ba > bb;
        }

        static string Num(string v) { int i = 0; while (i < v.Length && !char.IsDigit(v[i])) i++; return v.Substring(i).Trim(); }
        internal static Il2CppSystem.Action Act(Action a) { return Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(a); }

        internal static void Tick()
        {
            // second message: result of a download started by the player
            if (resultText != null && !resultShown)
            {
                try { var ui2 = G.ui; if (ui2 != null) { ui2.ReportMessage("Iron Admiral - update", resultText, "OK", null, Act(() => { }), null); resultShown = true; } }
                catch (Exception e) { resultShown = true; MelonLogger.Warning("[DIP GG Fixes][Update] result message failed: " + e.Message); }
            }
            if (shown || !done) return;
            try
            {
                if (remote == null) { shown = true; return; }
                bool newer = Newer(remote, local);
                // 1.11.123: back from a beta to the stable line - the latest stable is offered even if its number is lower
                if (!newer && !BetaLine && LocalIsBeta && !string.IsNullOrEmpty(remote) && remote != local && remote.IndexOf("-beta", StringComparison.OrdinalIgnoreCase) < 0) newer = true;
                // 1.11.295 (#55): same package number, but the running DLL is not the one GitHub ships with it - offer the package again
                bool dllDiffers = !newer && remote == local && !string.IsNullOrEmpty(remoteDll) && !string.IsNullOrEmpty(loadedDll) && remoteDll != loadedDll;
                if (dllDiffers) newer = true;
                if (firstTry == 0f) { firstTry = UnityEngine.Time.realtimeSinceStartup; MelonLogger.Msg($"[DIP GG Fixes][Update] installed {local} ({localDll}; loaded {loadedDll ?? "?"}), latest on GitHub ({(BetaLine ? "beta" : "stable")} line) {remote} ({remoteDll}) - {(dllDiffers ? "LOADED DLL DIFFERS FROM THE PACKAGE - reinstall offered" : newer ? "UPDATE AVAILABLE" : "up to date")}{(remoteZipUrl != null ? ", package " + remoteZipUrl : "")}"); }
                if (!newer) { shown = true; return; }
                if (UnityEngine.Time.realtimeSinceStartup - firstTry < 15f) return; // let the main menu appear first
                // 1.11.13: never during a loading screen - the 15:44 log showed the message raised while a save was loading
                // (LoadingCustom) and swallowed by the loading screen. Wait for MainMenu or World with no loading in progress,
                // then another 3 s so the scene has settled.
                bool loading = true; try { loading = GameManager.IsLoadingAny || GameManager.IsLoadingScreenActive; } catch { loading = false; }
                if (loading) { settledAt = 0f; return; }
                var state = GameManager.GameState.MainMenu; try { state = G.GameManager.CurrentState; } catch { }
                if (state != GameManager.GameState.MainMenu && state != GameManager.GameState.World) { settledAt = 0f; return; }
                if (settledAt == 0f) { settledAt = UnityEngine.Time.realtimeSinceStartup; return; }
                if (UnityEngine.Time.realtimeSinceStartup - settledAt < 3f) return;
                var ui = G.ui; if (ui == null) return;
                bool canDownload = DipGgFixesMod.UpdateDownload != null && DipGgFixesMod.UpdateDownload.Value && !string.IsNullOrEmpty(remoteZipUrl) && !string.IsNullOrEmpty(remoteSha);
                string text = $"Iron Admiral {remote}{(BetaLine ? " (beta)" : "")} is available on GitHub (installed: {local}).\n{remoteDll}\n{Page}";
                if (canDownload && DipGgFixesMod.UpdateAutoInstall != null && DipGgFixesMod.UpdateAutoInstall.Value)
                {
                    shown = true;
                    MelonLogger.Msg("[DIP GG Fixes][Update] automatic installation (UpdateAutoInstall) of " + remote);
                    StartDownload();
                    ui.ReportMessage("Iron Admiral - update", text + "\n\nThe new version is being downloaded and installed now (SHA-256 verified). A message will tell you when it is done - then restart the game. Switch UpdateAutoInstall (F10) turns automatic installation off.", "OK", null, Act(() => { }), null);
                    return;
                }
                if (canDownload)
                {
                    text += "\n\nDownload installs the Mods folder of the new version (SHA-256 verified); the game must be restarted afterwards. Nothing is downloaded unless you click.";
                    ui.ReportMessage("Iron Admiral - update", text, "Download", "Later", Act(() => StartDownload()), Act(() => { MelonLogger.Msg("[DIP GG Fixes][Update] download declined"); }));
                }
                else
                {
                    ui.ReportMessage("Iron Admiral - update", text, "OK", null, Act(() => { }), null);
                }
                shown = true;
                MelonLogger.Msg("[DIP GG Fixes][Update] message shown" + (canDownload ? " with Download button" : ""));
            }
            catch (Exception e) { shown = true; MelonLogger.Warning("[DIP GG Fixes][Update] message failed: " + e.Message); }
        }

        static void StartDownload()
        {
            MelonLogger.Msg("[DIP GG Fixes][Update] download confirmed by the player: " + remoteZipUrl);
            string url = remoteZipUrl, sha = remoteSha, ver = remote;
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    byte[] data;
                    using (var hc = new System.Net.Http.HttpClient())
                    {
                        // 1.11.360 (#82, player 28.10 on a slow connection: the update to 1.2.75 did not happen): the whole ~7-8 MB
                        // package had to arrive within 120 s (about 60 KB/s); now 15 minutes
                        hc.Timeout = TimeSpan.FromMinutes(15);
                        hc.DefaultRequestHeaders.UserAgent.ParseAdd("IronAdmiral-DIP_GG_Fixes");
                        data = await hc.GetByteArrayAsync(url);
                    }
                    string hex;
                    using (var h = System.Security.Cryptography.SHA256.Create()) hex = BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                    if (hex != sha) { Fail($"checksum mismatch (expected {sha}, got {hex}) - nothing installed"); return; }
                    string tmp = Path.Combine(Path.GetTempPath(), "IronAdmiral_update_" + ver);
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    Directory.CreateDirectory(tmp);
                    string zipPath = Path.Combine(tmp, "mods_only.zip");
                    File.WriteAllBytes(zipPath, data);
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tmp);
                    string srcMods = Path.Combine(tmp, "Mods");
                    if (!Directory.Exists(srcMods)) { Fail("the package has no Mods folder - nothing installed"); return; }
                    string mods = ModsDir;
                    int copied = 0, same = 0, swapped = 0; var failed = new List<string>();
                    foreach (var f in Directory.GetFiles(srcMods, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(srcMods.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string dst = Path.Combine(mods, rel);
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dst));
                            if (File.Exists(dst) && SameContent(f, dst)) { same++; continue; }
                            try { File.Copy(f, dst, true); copied++; }
                            catch (IOException)
                            {
                                // 1.11.14: a loaded DLL (DIP_GG_Fixes.dll, TweaksAndFixes.dll - the 16:05 log) cannot be
                                // overwritten, but it can be renamed; the .old copies are deleted at the next start
                                string old = dst + ".old";
                                if (File.Exists(old)) File.Delete(old);
                                File.Move(dst, old);
                                File.Copy(f, dst); copied++; swapped++;
                            }
                        }
                        catch (Exception fe) { failed.Add(rel + " (" + fe.GetType().Name + ")"); }
                    }
                    try { Directory.Delete(tmp, true); } catch { }
                    string summary = $"{copied} file(s) updated, {same} unchanged, {swapped} loaded DLL(s) swapped for the next start";
                    if (failed.Count > 0) { MelonLogger.Warning("[DIP GG Fixes][Update] not updated: " + string.Join(", ", failed)); }
                    MelonLogger.Msg($"[DIP GG Fixes][Update] installed Iron Admiral {ver}: {summary}; restart the game");
                    try { File.WriteAllText(Path.Combine(mods, "IRON_ADMIRAL_VERSION.txt"), ver + "\n" + (remoteDll ?? "") + "\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n"); } catch { }
                    resultText = failed.Count == 0
                        ? $"Iron Admiral {ver} installed ({summary}). Restart the game to use it."
                        : $"Iron Admiral {ver} installed with {failed.Count} file(s) skipped ({summary}). Restart the game; see the MelonLoader log for the skipped files.";
                }
                catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
            });
        }

        static bool SameContent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                using (var h = System.Security.Cryptography.SHA256.Create())
                using (var sa = File.OpenRead(a)) using (var sb = File.OpenRead(b))
                    return BitConverter.ToString(h.ComputeHash(sa)) == BitConverter.ToString(h.ComputeHash(sb));
            }
            catch { return false; }
        }

        static void Fail(string why)
        {
            MelonLogger.Warning("[DIP GG Fixes][Update] update failed: " + why);
            resultText = $"Update failed: {why}\nDownload manually: {Page}";
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
    // 1.11.96: no longer an attribute patch - installed only as a fallback by ParamTweak (see Perf.cs)
    internal static class Patch_Param
    {
        static int surrLogged;
        internal static void Postfix(string __0, ref float __result)
        {
            long t0 = Perf.Enabled ? Perf.T() : 0; try { Body(__0, ref __result); } finally { if (t0 != 0) Perf.Add(Perf.PARAM, t0); }
        }
        static void Body(string __0, ref float __result)
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

    // ---------------------------------------------------------------- treasury crisis (1.11.16)
    //   Vanilla keeps a Player.inflation float in the save (Player.Store.inflation) that nothing reads any more:
    //   Player.ExpensesInflation() is "cash * 0", Inflation()/DesiredInflation() have no callers and only
    //   InitPlayerCash/FromStore write the field. It is used here as the persisted crisis state, packed as
    //   inflation% + 100 * loanMonthsLeft + 100000 * cutsMonthsLeft (all small integers, exact in a float).
    //   Each turn CampaignController.NextTurn does cash += Player.Income(), Income() = Budget() - Expenses(),
    //   so the loan repayment, inflation surcharge and cuts saving are applied as a postfix on Expenses().
    internal struct CrisisState { public int Inflation; public int Loan; public int Cuts; public bool Active { get { return Inflation > 0 || Loan > 0 || Cuts > 0; } } }

    internal static class FinanceCrisis
    {
        private static string lastTurn;
        private static readonly Dictionary<string, int> monthsInDebt = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> lastEventMonth = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> lastChoice = new Dictionary<string, int>(); // 1 loan, 2 print, 3 cuts
        private static readonly HashSet<string> bankruptLogged = new HashSet<string>();
        private static readonly Dictionary<string, float> written = new Dictionary<string, float>();
        private static Player pendingPlayer; private static int pendingMonth; private static bool dialogOpen; private static float pendingSince;
        internal static bool Busy { get { return dialogOpen || pendingPlayer != null; } }
        [ThreadStatic] private static bool inExpenses;

        internal static CrisisState Decode(float v)
        {
            var st = new CrisisState();
            if (float.IsNaN(v) || v < 0.5f) return st;
            int n = (int)Math.Round(v);
            st.Inflation = n % 100; st.Loan = (n / 100) % 1000; st.Cuts = (n / 100000) % 100;
            return st;
        }
        static void Write(Player p, CrisisState st)
        {
            float v = Encode(st); p.inflation = v;
            try { written[p.data != null ? p.data.name : p.Pointer.ToString()] = v; } catch { }
        }
        internal static float Encode(CrisisState st)
        {
            int i = Math.Max(0, Math.Min(99, st.Inflation)), l = Math.Max(0, Math.Min(999, st.Loan)), c = Math.Max(0, Math.Min(99, st.Cuts));
            return i + 100 * l + 100000 * c;
        }
        static bool On { get { return DipGgFixesMod.FinancialCrisis != null && DipGgFixesMod.FinancialCrisis.Value; } }
        static string Key(Player p) { try { return p.data != null ? p.data.name : p.Pointer.ToString(); } catch { return "?"; } }
        static string NameUi(Player p) { try { return p.data != null ? p.data.nameUi : Key(p); } catch { return "?"; } }

        internal static void LogBankruptOnce(Player p)
        {
            string k = Key(p);
            if (bankruptLogged.Contains(k)) return;
            bankruptLogged.Add(k);
            MelonLogger.Msg($"[DIP GG Fixes][Treasury] {NameUi(p)}: cash {p.cash / 1e6f:F0}M - no bb/bc orders, ca/cl/dd/tb favoured x{DipGgFixesMod.CruiserFactor.Value}");
        }

        // monthly surcharge / saving on Player.Expenses()
        internal static void AdjustExpenses(Player p, ref float result)
        {
            if (!On || p == null || inExpenses) return;
            var st = Decode(p.inflation);
            if (!st.Active) return;
            inExpenses = true;
            try
            {
                if (st.Inflation > 0) result *= 1f + st.Inflation / 100f;
                if (st.Loan > 0)
                {
                    float b = p.Budget(); if (b > 0f) result += b * DipGgFixesMod.LoanRepayShare.Value;
                }
                if (st.Cuts > 0)
                {
                    float m = p.ExpensesShipMaintenance(); if (m > 0f) result -= m * DipGgFixesMod.CutsMaintenanceSaving.Value;
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Treasury] Expenses adjust failed: " + e.Message); }
            finally { inExpenses = false; }
        }

        internal static void CheckOncePerTurn()
        {
            if (!On) return;
            try
            {
                var cc = CampaignController.Instance;
                if (cc == null || cc.CampaignData == null) return;
                string turn = cc.CurrentDate.ToString();
                if (turn == lastTurn) return;
                lastTurn = turn;
                var d = cc.CurrentDate.AsDate(); int month = d.Year * 12 + d.Month;
                var majors = cc.CampaignData.PlayersMajor;
                for (int i = 0; i < majors.Count; i++)
                {
                    var p = majors[i];
                    if (p == null || p.isDisabled || !p.HavePorts) continue;
                    string k = Key(p);
                    // 1.11.22: decay, repayment and cuts countdown live in Economy (state saved next to the game save)
                    var st = Economy.Get(p);
                    if (p.cash >= 0f) { monthsInDebt[k] = 0; bankruptLogged.Remove(k); continue; }
                    int n; monthsInDebt.TryGetValue(k, out n); n++; monthsInDebt[k] = n;
                    bool deep = false;
                    try { int dm = DipGgFixesMod.CrisisDebtMonths.Value; float b0 = p.Budget(); deep = dm > 0 && b0 > 0f && -p.cash > dm * b0; } catch { }
                    if (n < Math.Max(1, DipGgFixesMod.CrisisMonths.Value) && !deep) continue;
                    // 1.11.20: a remedy already running no longer blocks the next one when the debt is deep again - Britain took a
                    // war loan (-1,059M -> +2,389M) and fifteen months later sat at -4,501M with the loan still "active", so nothing
                    // fired. A second remedy needs the deep-debt condition and at least 12 months since the previous one.
                    int last; bool hasLast = lastEventMonth.TryGetValue(k, out last);
                    if (st.Active && !deep) continue;
                    if (hasLast && month - last < 12) continue;
                    if (p.isMain)
                    {
                        if (pendingPlayer == null) { pendingPlayer = p; pendingMonth = month; pendingSince = 0f; MelonLogger.Msg($"[DIP GG Fixes][Treasury] {NameUi(p)}: {n} months in debt (cash {p.cash / 1e6f:F0}M, budget {b1(p) / 1e6f:F1}M/month) - asking the player"); }
                        continue;
                    }
                    int choice = ChooseForAi(p, k);
                    Apply(p, choice, month, "AI");
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Treasury] turn check failed: " + e.Message); }
        }

        static float b1(Player p) { try { return p.Budget(); } catch { return 0f; } }
        static int ChooseForAi(Player p, string k)
        {
            int prev; lastChoice.TryGetValue(k, out prev);
            bool war = false; try { war = p.IsAtWar(); } catch { }
            var st = Economy.Get(p);
            if (war) return (prev == 1 || st.LoanMonths > 0) ? 2 : 1;   // at war: loan; with a loan still running -> print money
            if (st.CutsMonths > 0 || prev == 3) return 1;                // peace: cuts did not help -> loan
            return 3;                                              // peace: fleet cuts first
        }

        internal static void Apply(Player p, int choice, int month, string who)
        {
            try
            {
                string k = Key(p); float cash = p.cash;
                float b = 0f; try { b = p.Budget(); } catch { }
                if (b <= 0f) b = Math.Abs(cash) / 12f;
                var st = Economy.Get(p);
                string what;
                switch (choice)
                {
                    case 1:
                    {
                        p.cash = DipGgFixesMod.LoanGrantMonths.Value * b;
                        float amount = p.cash - cash;
                        st.LoanBalance += amount; st.LoanMonths = DipGgFixesMod.LoanTermMonths.Value;
                        Economy.MarkBankrupt(p);
                        what = $"war loan: cash {cash / 1e6f:F0}M -> {p.cash / 1e6f:F0}M, debt {st.LoanBalance / 1e6f:F0}M over {st.LoanMonths} months at {Economy.LoanRate(p, st):F1}% a year";
                        MelonLogger.Msg($"[DIP GG Fixes][Treasury] {NameUi(p)} ({who}) - {what}");
                        Economy.Inflow(p, amount, false, "war loan");
                        break;
                    }
                    case 2:
                    {
                        p.cash = DipGgFixesMod.PrintGrantMonths.Value * b;
                        float amount = p.cash - cash;
                        Economy.MarkBankrupt(p);
                        try { if (DipGgFixesMod.PrintUnrest.Value > 0f) p.AddUnrest(DipGgFixesMod.PrintUnrest.Value); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Treasury] AddUnrest: " + e.Message); }
                        what = $"money printing: cash {cash / 1e6f:F0}M -> {p.cash / 1e6f:F0}M, unrest +{DipGgFixesMod.PrintUnrest.Value}";
                        MelonLogger.Msg($"[DIP GG Fixes][Treasury] {NameUi(p)} ({who}) - {what}");
                        Economy.Inflow(p, amount, true, "printed money");
                        break;
                    }
                    default:
                        st.CutsMonths = DipGgFixesMod.CutsMonths.Value;
                        Economy.MarkBankrupt(p);
                        what = $"fleet cuts: no bb/bc for {st.CutsMonths} months, maintenance -{DipGgFixesMod.CutsMaintenanceSaving.Value:P0}, cash stays {cash / 1e6f:F0}M";
                        MelonLogger.Msg($"[DIP GG Fixes][Treasury] {NameUi(p)} ({who}) - {what}");
                        break;
                }
                lastEventMonth[k] = month; lastChoice[k] = choice; monthsInDebt[k] = 0;
                try { var est = Economy.Get(p); if (est != null) est.Events.Add("Treasury crisis - " + what); } catch { }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Treasury] apply failed: " + e.Message); }
        }

        // player dialog, shown on the map when nothing is loading
        internal static void Tick()
        {
            if (pendingPlayer == null || dialogOpen) return;
            try
            {
                bool loading = true; try { loading = GameManager.IsLoadingAny || GameManager.IsLoadingScreenActive; } catch { loading = false; }
                if (loading) { pendingSince = 0f; return; }
                bool onMap = false; try { onMap = GameManager.IsWorld || GameManager.IsWorldMap; } catch { }
                if (!onMap) { pendingSince = 0f; return; }
                if (pendingSince == 0f) { pendingSince = UnityEngine.Time.realtimeSinceStartup; return; }
                if (UnityEngine.Time.realtimeSinceStartup - pendingSince < 1.5f) return;
                var ui = G.ui; if (ui == null) return;
                var p = pendingPlayer; int month = pendingMonth;
                if (p.cash >= 0f) { pendingPlayer = null; return; }
                float b = 0f; try { b = p.Budget(); } catch { }
                if (b <= 0f) b = Math.Abs(p.cash) / 12f;
                string text = $"The treasury has been in debt for months (cash {p.cash / 1e6f:F0}M, monthly naval budget {b / 1e6f:F1}M). The government must act.\n\n"
                    + $"War loan: debt cleared and {DipGgFixesMod.LoanGrantMonths.Value} months of budget credited; the loan is repaid over {DipGgFixesMod.LoanTermMonths.Value} months with interest of inflation plus a risk margin set by your credit rating - a bankruptcy gives the worst rating for {EcoPrefs.BankruptYears.Value} years. Borrowing raises inflation in relation to the size of the economy.\n"
                    + $"Other options: money printing ({DipGgFixesMod.PrintGrantMonths.Value} months of budget now, inflation rises three times as much as with a loan, unrest +{DipGgFixesMod.PrintUnrest.Value}) or fleet cuts (no new capital ships for {DipGgFixesMod.CutsMonths.Value} months, maintenance -{DipGgFixesMod.CutsMaintenanceSaving.Value:P0}).";
                dialogOpen = true;
                ui.ReportMessage("Treasury crisis", text, "War loan", "Other options",
                    UpdateCheck.Act(() => { dialogOpen = false; pendingPlayer = null; Apply(p, 1, month, "player"); }),
                    UpdateCheck.Act(() => { ShowSecond(ui, p, month); }));
            }
            catch (Exception e) { dialogOpen = false; pendingPlayer = null; MelonLogger.Warning("[DIP GG Fixes][Treasury] dialog failed: " + e.Message); }
        }

        static void ShowSecond(Ui ui, Player p, int month)
        {
            try
            {
                string text = $"Money printing: debt cleared, {DipGgFixesMod.PrintGrantMonths.Value} months of naval budget credited now; inflation rises by 1.2 points per 1% of GDP printed and raises all prices; high inflation adds unrest; unrest +{DipGgFixesMod.PrintUnrest.Value} at once.\n\n"
                    + $"Fleet cuts: no money, no new battleships or battlecruisers for {DipGgFixesMod.CutsMonths.Value} months, fleet maintenance -{DipGgFixesMod.CutsMaintenanceSaving.Value:P0}.";
                ui.ReportMessage("Treasury crisis", text, "Print money", "Fleet cuts",
                    UpdateCheck.Act(() => { dialogOpen = false; pendingPlayer = null; Apply(p, 2, month, "player"); }),
                    UpdateCheck.Act(() => { dialogOpen = false; pendingPlayer = null; Apply(p, 3, month, "player"); }));
            }
            catch (Exception e) { dialogOpen = false; pendingPlayer = null; MelonLogger.Warning("[DIP GG Fixes][Treasury] second dialog failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Player), "Expenses")]
    internal static class Patch_Expenses
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            Economy.AdjustExpenses(__instance, ref __result);
        }
    }

    // ---------------------------------------------------------------- AI shipbuilding diagnostics (1.11.17)
    //   CampaignController.BuildNewShips(Player, float) returns without ordering when one of these holds (native code):
    //   aiShipbuilding x Revenue() > money argument; crewPool < build_ships_min_crew_pool_amount; tonnage under
    //   construction >= override_shipybuilding_limit x ShipbuildingCapacityLimit(); StateBudget() / (ai_ship_gdp_ratio x
    //   fleet tonnage) < 1; Revenue() x year factor(fleet_generation_money_min..max) > Income(). The 17 October save had
    //   the USA with 1.2 bn cash, 19 provinces, 5 designs and not a single ship or order, so the inputs are logged for
    //   every AI great power that ends a turn with nothing under construction.
    [HarmonyPatch(typeof(CampaignController), "BuildNewShips", new Type[] { typeof(Player), typeof(float) })]
    internal static class Patch_BuildNewShips
    {
        private static string lastTurn; private static readonly HashSet<string> logged = new HashSet<string>();
        private static int Building(Player p)
        {
            int n = 0; var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var s = en.Current; if (s != null && s.isBuilding) n++; }
            return n;
        }
        private static float Tonnage(Player p)
        {
            float t = 0f; var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var s = en.Current; if (s != null && !s.isBuilding) t += s.Tonnage(); }
            return t;
        }
        private static string Treasury(Player p)
        {
            try
            {
                var st = Economy.Get(p);
                string t = "";
                if (st.LoanMonths > 0) t += $", loan {st.LoanBalance / 1e6f:F0}M ({st.LoanMonths} months left, {Economy.LoanRate(p, st):F1}%)";
                if (Math.Abs(st.Inflation) >= 0.1f) t += $", inflation {st.Inflation:F1}%";
                if (st.CutsMonths > 0) t += $", fleet cuts {st.CutsMonths} months left";
                float listed = p.ExpensesShipMaintenance() + p.ExpensesShipBuilding() + p.ExpensesShipRepairing() + p.ExpensesShipRefit() + p.ExpensesShipCommissioning() + p.ExpensesShipyardBudget() + p.ExpensesTechBudget() + p.ExpensesTrainingBudget() + p.ExpensesIntelBudget() + p.ExpensesTransportCapacity(false) + p.TrLosesMoney();
                t += $", other {(p.Expenses() - listed) / 1e6f:F0}M";
                return t;
            }
            catch { return ""; }
        }
        private static void Prefix(Player player, float __1, ref int __state)
        {
            __state = -1;
            try { if (player != null && player.isAi && player.isMajor) __state = Building(player); } catch { }
        }
        private static void Postfix(Player player, float __1, int __state)
        {
            try
            {
                if (__state < 0) return;
                var cc = CampaignController.Instance; if (cc == null) return;
                string turn = cc.CurrentDate.ToString();
                if (turn != lastTurn) { lastTurn = turn; logged.Clear(); }
                string k = player.data != null ? player.data.name : player.Pointer.ToString();
                if (logged.Contains(k)) return;
                int after = Building(player);
                if (after > 0) return; // something is being built - nothing to explain
                logged.Add(k);
                float rev = 0f, inc = 0f, sb = 0f, cap = 0f, uc = 0f, ton = 0f, ship = 0f; int crew = 0, designs = 0;
                try { rev = player.Revenue(); } catch { }
                try { inc = player.Income(); } catch { }
                try { sb = player.StateBudget(); } catch { }
                try { cap = player.ShipbuildingCapacityLimit(); } catch { }
                try { uc = player.ShipTonnageUnderConstruction(); } catch { }
                try { ton = Tonnage(player); } catch { }
                try { crew = player.crewPool; } catch { }
                try { var pe = player.GetAiPersonality(); if (pe != null) ship = pe.aiShipbuilding; } catch { }
                try { var en = player.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) designs++; } catch { }
                float gdpRatio = 40000f; try { gdpRatio = CampaignController.Param("ai_ship_gdp_ratio", 40000f); } catch { }
                string ratio = ton > 0f ? (sb / (gdpRatio * ton)).ToString("F2") : "inf";
                string br = "";
                if (inc < 0f)
                {
                    try
                    {
                        br = $", expenses: maintenance {player.ExpensesShipMaintenance() / 1e6f:F0}M, building {player.ExpensesShipBuilding() / 1e6f:F0}M, repair {player.ExpensesShipRepairing() / 1e6f:F0}M, refit {player.ExpensesShipRefit() / 1e6f:F0}M, commissioning {player.ExpensesShipCommissioning() / 1e6f:F0}M, shipyard {player.ExpensesShipyardBudget() / 1e6f:F0}M, tech {player.ExpensesTechBudget() / 1e6f:F0}M, training {player.ExpensesTrainingBudget() / 1e6f:F0}M, intel {player.ExpensesIntelBudget() / 1e6f:F0}M, transport {player.ExpensesTransportCapacity(false) / 1e6f:F0}M, transport losses {player.TrLosesMoney() / 1e6f:F0}M, total {player.Expenses() / 1e6f:F0}M, budget {player.Budget() / 1e6f:F0}M{Treasury(player)}";
                    }
                    catch (Exception e) { br = ", expenses breakdown failed: " + e.Message; }
                }
                MelonLogger.Msg($"[DIP GG Fixes][Shipyard] {player.data?.nameUi}: nothing under construction after BuildNewShips - cash {player.cash / 1e6f:F0}M, money arg {__1 / 1e6f:F1}M, revenue {rev / 1e6f:F1}M, income {inc / 1e6f:F1}M, state budget {sb / 1e6f:F0}M, fleet {ton:F0} t, budget/(ratio*tonnage) {ratio}, under construction {uc:F0} t of capacity {cap:F0} t, crew pool {crew}, aiShipbuilding {ship:F2} (x revenue {ship * rev / 1e6f:F1}M), designs {designs}{br}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Shipyard] diagnostics failed: " + e.Message); }
        }
    }

    // ---------------------------------------------------------------- AI budget caps (1.11.19)
    //   The 17 October [Shipyard] breakdown: USA revenue 422M/month, tech 406M + shipyard expansion 317M -> income -384M,
    //   no orders (reserve gate needs cash + income > 2.93 x revenue). Britain: 211k t of shipyard capacity and no ship.
    //   CampaignController.AiTechBudget sets Player.techBudget (percent of the monthly budget) from CashAndIncome, and DIP
    //   raised tech_budget_max to 100, so a cash-rich AI pours the whole naval budget into research. Rule: the AI may spend
    //   what it likes on research while it has a surplus; with a deficit the research budget is cut by the deficit so that the
    //   books balance. AiTryBuildShipyard starts expansions from CashAndIncome as well; no new expansion with a deficit.
    [HarmonyPatch(typeof(CampaignController), "AiTechBudget", new Type[] { typeof(Player) })]
    internal static class Patch_AiTechBudget
    {
        private static readonly Dictionary<string, bool> wasCut = new Dictionary<string, bool>();
        private static void Postfix(Player player)
        {
            try
            {
                var on = DipGgFixesMod.AiTechBalance; if (on == null || !on.Value) return;
                if (player == null || !player.isAi || !player.isMajor) return;
                string k = player.data != null ? player.data.name : "?";
                float income = player.Income();
                bool prev; wasCut.TryGetValue(k, out prev);
                if (income >= 0f)
                {
                    if (prev) { wasCut[k] = false; MelonLogger.Msg($"[DIP GG Fixes][Budget] {player.data?.nameUi}: income {income / 1e6f:F0}M/month is positive again, research budget {player.techBudget:F0}% left as the AI set it"); }
                    return;
                }
                float techExp = player.ExpensesTechBudget();
                if (techExp <= 0f) return;
                float before = player.techBudget;
                float keep = Math.Max(0f, (techExp + income) / techExp); // income < 0: cut the budget by the deficit
                float floor = Math.Max(0f, DipGgFixesMod.AiTechBudgetMin.Value);
                float after = Math.Max(floor, before * keep);
                if (after >= before) return;
                player.techBudget = after;
                float incomeAfter = 0f; try { incomeAfter = player.Income(); } catch { }
                if (!prev || Math.Abs(after - before) > 10f)
                {
                    wasCut[k] = true;
                    MelonLogger.Msg($"[DIP GG Fixes][Budget] {player.data?.nameUi}: deficit {-income / 1e6f:F0}M/month - research budget cut {before:F0}% -> {after:F0}% ({techExp / 1e6f:F0}M -> {player.ExpensesTechBudget() / 1e6f:F0}M), income now {incomeAfter / 1e6f:F0}M");
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Budget] AiTechBudget postfix: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CampaignController), "AiTryBuildShipyard", new Type[] { typeof(Player) })]
    internal static class Patch_AiTryBuildShipyard
    {
        private static readonly Dictionary<string, string> lastReason = new Dictionary<string, string>();
        // 1.11.32: a single positive month is not enough (Britain, Oct 1911: +86M once, expansion started, -310M the month after)
        private class Track { public int Month = -1; public int Streak; public float Cost; public int Months = 12; }
        private static readonly Dictionary<string, Track> track = new Dictionary<string, Track>();
        private static float Tonnage(Player p)
        {
            float t = 0f; var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var s = en.Current; if (s != null && !s.isBuilding) t += s.Tonnage(); }
            return t;
        }
        private static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }
        private static bool Prefix(Player player)
        {
            try
            {
                var h = DipGgFixesMod.AiShipyardHeadroom; if (h == null || h.Value <= 0f) return true;
                if (player == null || !player.isAi || !player.isMajor) return true;
                string k2 = player.data != null ? player.data.name : "?";
                Track t; if (!track.TryGetValue(k2, out t)) { t = new Track(); track[k2] = t; }
                float income = player.Income();
                int m = Month();
                if (m != t.Month) { t.Month = m; t.Streak = income >= 0f ? t.Streak + 1 : 0; }
                if (player.shipyardBuildMonthLeft > 0)
                {
                    // an expansion already runs - let the game finish it, and remember what it costs
                    try { float c = player.ExpensesShipyardBudget(); if (c > 0f) t.Cost = c; int tot = player.shipyardBuildMonthTotal; if (tot > 0) t.Months = tot; } catch { }
                    return true;
                }
                string why = null;
                if (income < 0f) why = $"income {income / 1e6f:F0}M/month is negative";
                else
                {
                    float ton = Tonnage(player), cap = player.ShipbuildingCapacityLimit();
                    if (cap > h.Value * Math.Max(ton, 1f)) why = $"shipyard capacity {cap:F0} t already exceeds {h.Value:F1} x fleet {ton:F0} t";
                    else
                    {
                        float cost = t.Cost; if (cost <= 0f) { try { cost = 0.5f * player.Budget(); } catch { cost = 0f; } }
                        float cover = DipGgFixesMod.AiShipyardCostCover != null ? DipGgFixesMod.AiShipyardCostCover.Value : 1.25f;
                        int need = DipGgFixesMod.AiShipyardSurplusMonths != null ? (int)DipGgFixesMod.AiShipyardSurplusMonths.Value : 6;
                        bool covered = cost > 0f && income >= cover * cost;
                        float shortfall = Math.Max(0f, cost - income) * t.Months;
                        bool steady = t.Streak >= need && player.cash >= shortfall;
                        if (!covered && !steady)
                            why = $"surplus {income / 1e6f:F0}M/month for {t.Streak} month(s), expected expansion cost {cost / 1e6f:F0}M/month - needs {need} months of surplus with {shortfall / 1e6f:F0}M cash (has {player.cash / 1e6f:F0}M) or a surplus of {cover * cost / 1e6f:F0}M";
                    }
                }
                if (why == null) return true;
                string prev; if (!lastReason.TryGetValue(k2, out prev) || prev != why.Substring(0, Math.Min(12, why.Length)))
                {
                    lastReason[k2] = why.Substring(0, Math.Min(12, why.Length));
                    MelonLogger.Msg($"[DIP GG Fixes][Budget] {player.data?.nameUi}: no shipyard expansion - {why}");
                }
                return false;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Budget] AiTryBuildShipyard prefix: " + e.Message); return true; }
        }
    }

    // 1.11.38: no AI ship design generation while the nation cannot build (debt, or yard capacity full)
    [HarmonyPatch(typeof(CampaignController._GenerateRandomDesigns_d__202), "MoveNext")]
    internal static class Patch_GenerateRandomDesigns
    {
        private static readonly Dictionary<string, string> last = new Dictionary<string, string>();
        private static void Postfix() { ShipGenSpeed.Active = false; } // 1.11.196
        private static bool Prefix(CampaignController._GenerateRandomDesigns_d__202 __instance, ref bool __result)
        {
            // 1.11.196: newest-hulls filter active while the AI chooses hulls and generates its designs
            try { var pa = __instance != null ? __instance.player : null; if (pa != null && pa.isAi && !__instance.prewarming) { ShipGenSpeed.Active = true; ShipGenSpeed.ActivePlayer = pa.Pointer; } } catch { }
            try
            {
                var on = DipGgFixesMod.AiDesignOnlyWhenBuildable; if (on == null || !on.Value || __instance == null) return true;
                if (__instance.__1__state != 0 || __instance.prewarming) return true;
                var p = __instance.player; if (p == null || !p.isAi) return true;
                string why = null;
                bool ports = true; try { ports = p.HavePorts; } catch { }
                if (!ports) why = "no ports";
                else if (p.cash < 0f) why = $"cash {p.cash / 1e6f:F0}M is below zero";
                else
                {
                    float cap = 0f, uc = 0f; try { cap = p.ShipbuildingCapacityLimit(); uc = p.ShipTonnageUnderConstruction(); } catch { }
                    if (cap <= 0f) why = "no shipbuilding capacity";
                    else if (uc >= cap) why = $"shipyards full ({uc:F0} of {cap:F0} t under construction)";
                }
                if (why == null) return true;
                string k = p.data != null ? p.data.name : "?";
                string pv; if (!last.TryGetValue(k, out pv) || pv != why.Substring(0, Math.Min(10, why.Length)))
                {
                    last[k] = why.Substring(0, Math.Min(10, why.Length));
                    MelonLogger.Msg($"[DIP GG Fixes][Design] {p.data?.nameUi}: no new ship designs - {why}");
                }
                __result = false;
                return false;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Design] GenerateRandomDesigns: " + e.Message); return true; }
        }
    }

    // 1.11.39: treaty limits must not block the refit of ships that already exceed them
    internal static class RefitTreaty
    {
        private static readonly Dictionary<IntPtr, float> origin = new Dictionary<IntPtr, float>();
        private static readonly HashSet<IntPtr> logged = new HashSet<IntPtr>();
        internal static Ship lastRefit; internal static float lastAllowed;
        static readonly HashSet<string> limitLogged = new HashSet<string>();
        // 1.11.53: the save check of the refit screen compares the design with the player's treaty limit
        // (Player.TonnageLimit), not with Ship.TonnageMax - a 55,690 t battleship could not be refitted under a 35,000 t limit
        internal static void ApplyLimit(Player p, ShipType t, ref float result)
        {
            try
            {
                var a = DipGgFixesMod.RefitTreatyAllowance; if (a == null || a.Value <= 0f || t == null) return;
                bool designer = false; try { designer = GameManager.IsConstructor; } catch { }
                if (!designer) return;
                // 1.11.54: the ship open in the designer (the refit screen re-creates the refit design, so a remembered ship is not enough)
                Ship s = null; try { var pc = PlayerController.Instance; if (pc != null) s = pc.Ship; } catch { }
                if (s == null) s = lastRefit;
                if (s == null) return;
                bool refit = false; try { refit = s.isRefitDesign; } catch { }
                if (!refit) return;
                var st = s.shipType; if (st == null || st.name != t.name) return;
                if (p != null && s.player != null && s.player.Pointer != p.Pointer) return;
                if (!Eligible(s, result)) return; // 1.11.160: P6 - only from 1922, battleships/battlecruisers, ships already above the limit
                float allowed = Allowed(s, a.Value); if (allowed <= 0f) return;
                if (allowed > result)
                {
                    string key = s.Pointer.ToString() + "|" + result.ToString("F0");
                    if (limitLogged.Add(key)) MelonLogger.Msg($"[DIP GG Fixes][Refit] {s.name}: treaty limit for {t.name} {result:F0} t -> {allowed:F0} t for this refit");
                    result = allowed;
                }
            }
            catch { }
        }
        internal static float AllowedFor(Ship s, float allowance) { return Allowed(s, allowance); }
        // the displacement of the refit design when first seen (= the ship before the refit); 0 = unknown
        internal static float Origin(Ship s)
        {
            float o; if (origin.TryGetValue(s.Pointer, out o)) return o;
            o = 0f; try { o = s.Tonnage(); } catch { } if (o <= 0f) return 0f; origin[s.Pointer] = o; return o;
        }
        static float Allowed(Ship s, float allowance)
        {
            float o; if (!origin.TryGetValue(s.Pointer, out o)) { o = 0f; try { o = s.Tonnage(); } catch { } if (o <= 0f) return 0f; origin[s.Pointer] = o; }
            float allowed = o + allowance;
            float hullMax = 0f; try { var h = s.hull; if (h != null && h.data != null) hullMax = h.data.tonnageMax; } catch { }
            if (hullMax > 0f) allowed = Math.Min(allowed, hullMax);
            float tm = 0f; try { tm = s.TonnageMax(); } catch { } if (tm > 0f) allowed = Math.Min(allowed, tm); // 1.11.160: never above the game's own maximum
            return allowed;
        }
        // 1.11.160 (P6, decision 22.10): the treaty allowance only from 1922, only for battleships and battlecruisers, and only when
        // the ship before the refit is already above the treaty limit (23.10: Ship.TonnageMax of every refit design was raised up to
        // the hull maximum - refitted ships changed size, deck equipment was rescaled, torpedo tubes had to be removed)
        internal static bool Eligible(Ship s, float limit)
        {
            try
            {
                int year = 0; try { year = CampaignController.Instance.CurrentDate.AsDate().Year; } catch { }
                if (year < 1922) return false;
                var st = s.shipType; if (st == null || (st.name != "bb" && st.name != "bc")) return false;
                float o; if (!origin.TryGetValue(s.Pointer, out o)) { o = 0f; try { o = s.Tonnage(); } catch { } if (o <= 0f) return false; origin[s.Pointer] = o; }
                return limit <= 0f || o > limit;
            }
            catch { return false; }
        }
        internal static void Apply(Ship s, ref float result)
        {
            try
            {
                var a = DipGgFixesMod.RefitTreatyAllowance; if (a == null || a.Value <= 0f || s == null) return;
                if (!s.isRefitDesign) return;
                float o; if (!origin.TryGetValue(s.Pointer, out o)) { o = 0f; try { o = s.Tonnage(); } catch { } if (o <= 0f) return; origin[s.Pointer] = o; }
                float allowed = o + a.Value;
                float hullMax = 0f; try { var h = s.hull; if (h != null && h.data != null) hullMax = h.data.tonnageMax; } catch { }
                if (hullMax > 0f) allowed = Math.Min(allowed, hullMax);
                lastRefit = s; lastAllowed = allowed;
                if (allowed > result)
                {
                    if (logged.Add(s.Pointer)) MelonLogger.Msg($"[DIP GG Fixes][Refit] {s.name}: refit tonnage limit {result:F0} t -> {allowed:F0} t (ship {o:F0} t + treaty modernisation allowance, hull max {(hullMax > 0 ? hullMax.ToString("F0") : "-")} t)");
                    result = allowed;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Ship), "TonnageMax", new Type[] { })]
    internal static class Patch_TonnageMax_Refit
    {
        static bool Prepare() { return false; } // 1.11.160: Ship.TonnageMax is no longer raised (P6)
        private static void Postfix(Ship __instance, ref float __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitTreaty.Apply(__instance, ref __result); if (t0 != 0) Perf.Add(Perf.TON, t0); }
    }

    [HarmonyPatch(typeof(Ship), "TonnageMax", new Type[] { typeof(PartData), typeof(Ship) })]
    internal static class Patch_TonnageMaxStatic_Refit
    {
        static bool Prepare() { return false; } // 1.11.160: Ship.TonnageMax is no longer raised (P6)
        private static void Postfix(Ship __1, ref float __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitTreaty.Apply(__1, ref __result); if (t0 != 0) Perf.Add(Perf.TON, t0); }
    }

    [HarmonyPatch(typeof(Player), "TonnageLimit", new Type[] { typeof(ShipType) })]
    internal static class Patch_TonnageLimit_Refit
    {
        private static void Postfix(Player __instance, ShipType __0, ref float __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitTreaty.ApplyLimit(__instance, __0, ref __result); if (t0 != 0) Perf.Add(Perf.TON, t0); }
    }

    // 1.11.55: the "can not build" check of the designer (PlayerController.CanBuildShipsFromDesign) does not go through
    // Player.TonnageLimit; a refit above the treaty limit failed with "Above displacement limit for BB: 35,000 t".
    // When that is the only reason and the refit stays within the ship's tonnage plus the modernisation allowance, it passes.
    internal static class RefitBuildCheck
    {
        static int logged; static readonly HashSet<IntPtr> ownLogged = new HashSet<IntPtr>();
        internal static void Fix(Ship s, ref bool result, ref string reason, string where)
        {
            try
            {
                if (result || s == null) return;
                var a = DipGgFixesMod.RefitTreatyAllowance; if (a == null || a.Value <= 0f) return;
                bool refit = false; try { refit = s.isRefitDesign; } catch { }
                string r = reason ?? "";
                if (logged < 40 && (where == "save button" || logged < 10) && !string.Equals(StripTags(reason ?? "").Trim(), "valid", StringComparison.OrdinalIgnoreCase)) { logged++; MelonLogger.Msg($"[DIP GG Fixes][Refit] {where}: {s.name} refit={refit} tonnage {SafeTon(s):F0} t - cannot build: {StripTags(r)}"); }
                if (!refit) return;
                string plain = StripTags(r).Trim();
                if (plain.Contains("\n")) return;
                string low = plain.ToLowerInvariant();
                bool displacementOnly = low == "tonnage" || low.Contains("above displacement limit for") && plain.Split(':').Length <= 4 && low.TrimEnd('.', ' ').EndsWith("t");
                if (!displacementOnly) return;
                // 1.11.193: a ship that already exists above the game's displacement maximum (built while refit designs could grow
                //           up to the hull maximum, before 1.11.160) can always be refitted at its own displacement - no growth
                //           (24.10: BB Hildebrand 55,690 t, 1921 - "cannot build: tonnage" on every refit)
                float own = RefitTreaty.Origin(s); float tonNow = SafeTon(s);
                if (own > 0f && tonNow <= own + 0.5f)
                {
                    float tmax = 0f, lim = 0f; try { tmax = s.TonnageMax(); } catch { } try { var pl = s.player; var st = s.shipType; if (pl != null && st != null) lim = pl.TonnageLimit(st); } catch { }
                    result = true; reason = "";
                    if (ownLogged.Add(s.Pointer)) MelonLogger.Msg($"[DIP GG Fixes][Refit] {s.name}: refit at the ship's own displacement {tonNow:F0} t allowed (game maximum {tmax:F0} t, treaty limit {lim:F0} t) - {where}");
                    return;
                }
                if (!RefitTreaty.Eligible(s, 0f)) return; // 1.11.160: P6
                float allowed = RefitTreaty.AllowedFor(s, a.Value); float ton = SafeTon(s);
                if (allowed <= 0f || ton > allowed + 0.5f) { MelonLogger.Msg($"[DIP GG Fixes][Refit] {s.name}: {ton:F0} t is above the refit allowance ({allowed:F0} t) - stays blocked"); return; }
                result = true; reason = "";
                MelonLogger.Msg($"[DIP GG Fixes][Refit] {s.name}: refit of {ton:F0} t allowed above the treaty limit (allowance up to {allowed:F0} t) - {where}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Refit] build check: " + e.Message); }
        }
        static float SafeTon(Ship s) { try { return s.Tonnage(); } catch { return 0f; } }
        static string StripTags(string r) { var sb = new System.Text.StringBuilder(); bool tag = false; foreach (char c in r ?? "") { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) sb.Append(c); } return sb.ToString(); }
    }

    [HarmonyPatch(typeof(PlayerController), "CanBuildShipsFromDesign", new Type[] { typeof(Ship), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Ref })]
    internal static class Patch_CanBuild2_Refit
    {
        private static void Postfix(Ship __0, ref string __1, ref bool __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitBuildCheck.Fix(__0, ref __result, ref __1, "CanBuildShipsFromDesign"); BrokenDesigns.Check(__0, ref __result, ref __1); if (t0 != 0) Perf.Add(Perf.CANB, t0); }
    }

    [HarmonyPatch(typeof(PlayerController), "CanBuildShipsFromDesign", new Type[] { typeof(Ship), typeof(int), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
    internal static class Patch_CanBuild3_Refit
    {
        private static void Postfix(Ship __0, ref string __2, ref bool __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitBuildCheck.Fix(__0, ref __result, ref __2, "CanBuildShipsFromDesign(n)"); BrokenDesigns.Check(__0, ref __result, ref __2); if (t0 != 0) Perf.Add(Perf.CANB, t0); }
    }

    [HarmonyPatch(typeof(PlayerController), "CanBuildFromDesignOnInit", new Type[] { typeof(Ship), typeof(int), typeof(bool), typeof(string) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref })]
    internal static class Patch_CanBuildInit_Refit
    {
        private static void Postfix(Ship __0, ref string __3, ref bool __result) { long t0 = Perf.Enabled ? Perf.T() : 0; RefitBuildCheck.Fix(__0, ref __result, ref __3, "CanBuildFromDesignOnInit"); BrokenDesigns.Check(__0, ref __result, ref __3); if (t0 != 0) Perf.Add(Perf.CANB, t0); }
    }

    // 1.11.56: the save button of the designer (Ui.RefreshConstructorInfo, closure 489_21) reads canBuild / canNotBuildText,
    // computed inside RefreshConstructorInfo itself; TweaksAndFixes blocks the save on it ("Saving ship: ... | False / Design invalid").
    // A refit whose only problem is the treaty displacement limit and which stays within the allowance is let through.
    [HarmonyPatch(typeof(Ui.__c__DisplayClass489_21), "_RefreshConstructorInfo_b__89")]
    internal static class Patch_SaveButton_Refit
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Ui.__c__DisplayClass489_21 __instance)
        {
            try
            {
                if (__instance == null || __instance.canBuild) return;
                Ship s = null; try { var pc = PlayerController.Instance; if (pc != null) s = pc.Ship; } catch { }
                if (s == null) return;
                string reason = __instance.canNotBuildText; bool ok = false;
                RefitBuildCheck.Fix(s, ref ok, ref reason, "save button");
                if (ok) { __instance.canBuild = true; __instance.canNotBuildText = ""; }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Refit] save button: " + e.Message); }
        }
    }
}

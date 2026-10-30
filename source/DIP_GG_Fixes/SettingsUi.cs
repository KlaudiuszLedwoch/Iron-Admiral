// 1.11.41: in-game settings panel (IMGUI, toggled with F10 by default).
// Every on/off option of the mod (bool entries of the [DIP_GG_Fixes] preferences category) in groups, with its description.
// A click changes the value at once and saves UserData/MelonPreferences.cfg. Drawing uses only GUI.Box/GUI.Label (as the
// treasury panel, confirmed in game); clicks are read with Input.GetMouseButtonDown in OnUpdate and hit-tested against the
// rectangles of the last frame, so the panel does not depend on IMGUI event handling.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class SettingsUi
    {
        internal static MelonPreferences_Entry<string> Key;
        static KeyCode key = KeyCode.F10;
        static MelonPreferences_Category cat;
        static bool open;
        static int tab, page;
        static GUIStyle sTitle, sName, sDesc, sTab, sTabOn, sBtn;
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>();
        static readonly List<KeyValuePair<Rect, Action>> next = new List<KeyValuePair<Rect, Action>>();
        static string status = "";
        static float statusUntil;
        static int errors;

        // 1.11.329: 13 tabs, 5 per row. Every on/off switch has its tab in TabOf (generated from the switch list together with
        // the Help chapters "Settings: <tab>"); "-" = author's tools and one-time migration flags, not shown. Switches missing
        // from TabOf fall back to GroupOf (old group names mapped to the new tabs), then to "Other".
        static readonly string[] Groups = { "Battle AI", "Gunnery & damage", "Fog of war", "Crews & prizes", "Campaign AI", "Ship design", "Economy", "Diplomacy", "Display", "Logs", "Info & updates", "Tools", "Other" };
        static readonly Dictionary<string, string> OldGroup = new Dictionary<string, string> { {"Crews and prizes","Crews & prizes"},{"Information and updates","Info & updates"},{"Battle","Gunnery & damage"},{"Interface","Display"} };
        static readonly Dictionary<string, string> TabOf = new Dictionary<string, string>
        {
            {"DamageByTonnage","Gunnery & damage"},{"DeckHitsMeetDeck","Gunnery & damage"},{"DesignRecorder","Ship design"},{"ImpactAngleFromFall","Gunnery & damage"},
            {"AiCrossTorpedo","Battle AI"},{"ArmorDiagnostics","Logs"},{"BallisticsLog","Logs"},{"BattleAiLog","Logs"},{"BattleDispatches","Display"},{"BattleReport","Display"},
            {"CapitalCloseInWhenOutgunned","Battle AI"},{"CapitalEscapeWhenHopeless","Battle AI"},{"CapitalSupportScreen","Battle AI"},{"CapitalTorpedoCharge","Battle AI"},
            {"CloseInOnlyIfPenetrates","Battle AI"},{"CoverRetreat","Battle AI"},{"CruiserRoles","Battle AI"},{"DetachSlowShips","Battle AI"},{"DispatchNationalLanguages","Display"},
            {"DispatchPlayerOriginalLanguage","Display"},{"DivisionOrders","Battle AI"},{"DivisionOrdersVanilla","Battle AI"},{"EscapeMissionLogic","Battle AI"},{"EvasiveManeuvers","Battle AI"},
            {"FightWhenCannotEscape","Battle AI"},{"FireControlTechAccuracy","Gunnery & damage"},{"FireControlTechDispersion","Gunnery & damage"},{"FireDistribution","Battle AI"},
            {"FleetTorpedoAttack","Battle AI"},{"FocusFire","Battle AI"},{"HistoricalDamage","Gunnery & damage"},{"HistoricalGuns","Gunnery & damage"},{"HistoricalParts","Ship design"},
            {"HistoricalWeights","Ship design"},{"LightShipTorpedoCharge","Battle AI"},{"NightRules","Battle AI"},{"PlayerScoutCharge","Battle AI"},{"Ramming","Battle AI"},
            {"RealisticBallistics","Gunnery & damage"},{"ScoutTowardsSmoke","Battle AI"},{"SmartTargeting","Battle AI"},{"SmartTargetingAi","Battle AI"},{"TorpedoFriendlyLane","Battle AI"},
            {"TorpedoReloadCycle","Battle AI"},{"TorpedoSettings","Battle AI"},{"TorpedoTargetPriority","Battle AI"},{"TorpedoTurnAway","Battle AI"},{"TransportsAfterEscort","Battle AI"},
            {"ValueBasedAbort","Battle AI"},{"WithdrawWhenOutmatched","Battle AI"},{"FogAiPerception","Fog of war"},{"FogBattleReports","Fog of war"},{"FogHideEnemyDetails","Fog of war"},
            {"FogInterceptions","Fog of war"},{"FogNightFactors","Fog of war"},{"FogOfWar","Fog of war"},{"FogStaleContacts","Fog of war"},{"FogStrategicMap","Fog of war"},
            {"FogTacticalIdentification","Fog of war"},{"MapPortForces","Fog of war"},{"CaptureSurrenderedShips","Crews & prizes"},{"CrewLevelsRescale","Crews & prizes"},
            {"CrewPanic","Crews & prizes"},{"MinorFleetToConqueror","Crews & prizes"},{"SurrenderByTraining","Crews & prizes"},{"SurrenderedShipsToBattleWinner","Crews & prizes"},
            {"AiBestTorpedoDiameter","Ship design"},{"AiDesignOnlyWhenBuildable","Ship design"},{"AiMinTorpedoTubes","Ship design"},{"AiTechBudgetBalance","Campaign AI"},
            {"AutoShareDesigns","Ship design"},{"BankruptBuildsCheapShips","Campaign AI"},{"BrokenDesignsObsolete","Ship design"},{"CoreProvincesIndependence","Campaign AI"},
            {"CruisersWhenPoor","Campaign AI"},{"FixStalledConstruction","Campaign AI"},{"InstallModDesigns","Ship design"},{"InstallShippackDesigns","Ship design"},
            {"NationalTechBonuses","Campaign AI"},{"OccupationEvents","Campaign AI"},{"PortlessPowersWait","Campaign AI"},{"ShareToAllNations","Ship design"},{"UngovernedToSuccessors","Campaign AI"},
            {"FinancialCrisis","Economy"},{"InflationEconomy","Economy"},{"SeriesProduction","Economy"},{"BalanceGrantOnlyWhenSolvent","Diplomacy"},{"BalanceOfPowerEvents","Diplomacy"},
            {"CoalitionBalanceOfPower","Diplomacy"},{"CoalitionWar","Diplomacy"},{"BetaUpdates","Info & updates"},{"CheckForUpdates","Info & updates"},{"CheckOtherMods","Info & updates"},
            {"SaveCompatibilityFix","Info & updates"},{"ShareLogs","Info & updates"},{"TreasuryInFinancesWindow","Economy"},{"TreasuryMonthlyReport","Economy"},{"TreasuryPanel","Economy"},
            {"TreasuryWarnings","Economy"},{"UpdateDownloadButton","Info & updates"},{"UsageStatistics","Info & updates"},{"HistBuilderRun","-"},{"AiDivisionSpacing","Battle AI"},
            {"AiHullCaliberCap","Ship design"},{"AimPoint","Battle AI"},{"AlwaysShowSplashes","Display"},{"ArmorObliquity","Gunnery & damage"},{"ArmorQualityHistorical","Gunnery & damage"},
            {"AutoTestLab","-"},{"AutoTestScreenshots","-"},{"BarrelElevation","Display"},{"BattleDispatchBubbles","Display"},{"BattleHitDispatches","Display"},{"BetaUpdatesInitialised","-"},
            {"ContinuousMap","Campaign AI"},{"CustomBattleNoSharedDesigns","-"},{"CustomBattlePregenerated","Ship design"},{"DamageDiagLog","Logs"},{"DamageModelHistorical","Gunnery & damage"},
            {"EnemyRangeTags","Display"},{"FleetOrders","Campaign AI"},{"FleetOrdersAI","Campaign AI"},{"GunOrders","Battle AI"},{"HeBlastThinPlating","Gunnery & damage"},
            {"HistBuilderRetryFailed","-"},{"HistoricalCasemates","Ship design"},{"HistoricalDesignsFirst","Ship design"},{"HistoricalShells","Gunnery & damage"},{"HitLog","Logs"},
            {"HopelessEngageLighter","Battle AI"},{"IntelReports","Fog of war"},{"LostTerritoryReconquest","Campaign AI"},{"MapGhostParts","Campaign AI"},{"MigrationFireControlNames208","-"},
            {"MigrationOccupationCost195","-"},{"MigrationRangeCapOn225","-"},{"MigrationTorpedoSideOff199","-"},{"ModUiScaleAuto","Display"},{"ObliquityOnTopOfGame","Gunnery & damage"},
            {"OverPenBySection","Gunnery & damage"},{"OverPenDamage","Gunnery & damage"},{"PerfDefaults196","-"},{"PerformanceLog","Logs"},{"PhysicalGun","Gunnery & damage"},
            {"PhysicalGunHistoricalBase","Gunnery & damage"},{"PhysicalPenetration","Gunnery & damage"},{"PhysicalPenetrationByZoneTest","Gunnery & damage"},{"PhysicalPenetrationNormalIncidence","Gunnery & damage"},
            {"PhysicalPenetrationRows","Gunnery & damage"},{"PhysicalShellHistoricalWeight","Gunnery & damage"},{"RangeByMountElevation","Gunnery & damage"},{"RelationRepair","Diplomacy"},
            {"RepairMissingDesigns","Ship design"},{"RepairUnmountedParts","Gunnery & damage"},{"ReShadeNavalLook","Display"},{"SharedDesignsSkipUnknownTech","Ship design"},
            {"ShareLogsAsked","-"},{"ShipGenAsymmetryTolerance","Ship design"},{"ShipGenDiagnosis","-"},{"ShipGenEarlyStop","Ship design"},{"ShipGenMemory","Ship design"},
            {"ShipGenNewestHulls","Ship design"},{"ShipGenRelaxTurrets","Ship design"},{"ShipGenShareDesigns","Ship design"},{"ShipGenTrimOverweight","Ship design"},
            {"ShipTrade","Economy"},{"ShipTradeAiSells","Economy"},{"ShipTradeRefits","Economy"},{"TechCatchUp","Campaign AI"},{"TechDiffusion","Campaign AI"},{"TorpedoSense","Battle AI"},
            {"TorpedoSensePlayer","Battle AI"},{"TorpedoSideCheck","Battle AI"},{"ResetSwitchesOnNewVersion","-"},{"DivisionOrdersPanelFolded","-"},{"AiTorpedoCapitalsFirst","Battle AI"},
            {"CapturedTechNotCopied","Ship design"},{"BattleHourChoice","Campaign AI"},{"AiSmokeOnlyWhenNeeded","Battle AI"},{"AiTorpedoPincer","Battle AI"},{"AiApproachSmoke","Battle AI"},
            {"AiWithdrawLowAmmo","Battle AI"},{"BattleDispatchPanel","Display"},{"PrizeScuttle","Crews & prizes"},{"DivisionTorpedoSplit","Battle AI"},{"StackedGunsPenalty","Gunnery & damage"},
            {"TransportSpeedByYear","Battle AI"},{"EnemyProfileLog","Campaign AI"},{"ThanksOnMainMenu","Display"},{"AirStrikeTest","Battle AI"},{"EnemyFitBuildMix","Campaign AI"},
            {"CustomBattleCarrier","Battle AI"},{"PlayerDesignsFirst","Ship design"},{"CustomBattleSaveDesigns","Ship design"},{"DesignStats","Ship design"},{"DesignStatsPreferBest","Ship design"},
            {"NavalAviationRecon","Fog of war"},{"NavalAviationSpotting","Battle AI"},{"ShipGenSecondaryTower","Other"},{"NavalAviationSpotterPlanes","Battle AI"},
        };
        // 1.11.329 (author 28.10: "number the settings buttons with tiny digits so one knows to use button 65"): the number shown in
        // small grey before the name of every switch - the same number as in the Help chapters "Settings #a-b: <tab>"; rows are
        // listed in this order. Generated together with TabOf and the Help texts.
        static readonly Dictionary<string, int> NumOf = new Dictionary<string, int>
        {
            {"AiCrossTorpedo",1},{"CapitalCloseInWhenOutgunned",2},{"CapitalEscapeWhenHopeless",3},{"CapitalSupportScreen",4},{"CapitalTorpedoCharge",5},{"CloseInOnlyIfPenetrates",6},
            {"CoverRetreat",7},{"CruiserRoles",8},{"DetachSlowShips",9},{"DivisionOrders",10},{"DivisionOrdersVanilla",11},{"EscapeMissionLogic",12},{"EvasiveManeuvers",13},
            {"FightWhenCannotEscape",14},{"FireDistribution",15},{"FleetTorpedoAttack",16},{"FocusFire",17},{"LightShipTorpedoCharge",18},{"NightRules",19},{"PlayerScoutCharge",20},
            {"Ramming",21},{"ScoutTowardsSmoke",22},{"SmartTargeting",23},{"SmartTargetingAi",24},{"TorpedoFriendlyLane",25},{"TorpedoReloadCycle",26},{"TorpedoSettings",27},
            {"TorpedoTargetPriority",28},{"TorpedoTurnAway",29},{"TransportsAfterEscort",30},{"ValueBasedAbort",31},{"WithdrawWhenOutmatched",32},{"AiDivisionSpacing",33},
            {"AimPoint",34},{"GunOrders",35},{"HopelessEngageLighter",36},{"TorpedoSense",37},{"TorpedoSensePlayer",38},{"TorpedoSideCheck",39},{"DamageByTonnage",40},
            {"DeckHitsMeetDeck",41},{"ImpactAngleFromFall",42},{"FireControlTechAccuracy",43},{"FireControlTechDispersion",44},{"HistoricalDamage",45},{"HistoricalGuns",46},
            {"RealisticBallistics",47},{"ArmorObliquity",48},{"ArmorQualityHistorical",49},{"DamageModelHistorical",50},{"HeBlastThinPlating",51},{"HistoricalShells",52},
            {"ObliquityOnTopOfGame",53},{"OverPenBySection",54},{"OverPenDamage",55},{"PhysicalGun",56},{"PhysicalGunHistoricalBase",57},{"PhysicalPenetration",58},
            {"PhysicalPenetrationByZoneTest",59},{"PhysicalPenetrationNormalIncidence",60},{"PhysicalPenetrationRows",61},{"PhysicalShellHistoricalWeight",62},{"RangeByMountElevation",63},
            {"RepairUnmountedParts",64},{"FogAiPerception",65},{"FogBattleReports",66},{"FogHideEnemyDetails",67},{"FogInterceptions",68},{"FogNightFactors",69},{"FogOfWar",70},
            {"FogStaleContacts",71},{"FogStrategicMap",72},{"FogTacticalIdentification",73},{"MapPortForces",74},{"IntelReports",75},{"CaptureSurrenderedShips",76},
            {"CrewLevelsRescale",77},{"CrewPanic",78},{"MinorFleetToConqueror",79},{"SurrenderByTraining",80},{"SurrenderedShipsToBattleWinner",81},{"AiTechBudgetBalance",82},
            {"BankruptBuildsCheapShips",83},{"CoreProvincesIndependence",84},{"CruisersWhenPoor",85},{"FixStalledConstruction",86},{"NationalTechBonuses",87},{"OccupationEvents",88},
            {"PortlessPowersWait",89},{"UngovernedToSuccessors",90},{"ContinuousMap",91},{"FleetOrders",92},{"FleetOrdersAI",93},{"LostTerritoryReconquest",94},{"MapGhostParts",95},
            {"TechCatchUp",96},{"TechDiffusion",97},{"DesignRecorder",98},{"HistoricalParts",99},{"HistoricalWeights",100},{"AiBestTorpedoDiameter",101},{"AiDesignOnlyWhenBuildable",102},
            {"AiMinTorpedoTubes",103},{"AutoShareDesigns",104},{"BrokenDesignsObsolete",105},{"InstallModDesigns",106},{"InstallShippackDesigns",107},{"ShareToAllNations",108},
            {"AiHullCaliberCap",109},{"CustomBattlePregenerated",110},{"HistoricalCasemates",111},{"HistoricalDesignsFirst",112},{"RepairMissingDesigns",113},{"SharedDesignsSkipUnknownTech",114},
            {"ShipGenAsymmetryTolerance",115},{"ShipGenEarlyStop",116},{"ShipGenMemory",117},{"ShipGenNewestHulls",118},{"ShipGenRelaxTurrets",119},{"ShipGenShareDesigns",120},
            {"ShipGenTrimOverweight",121},{"FinancialCrisis",122},{"InflationEconomy",123},{"SeriesProduction",124},{"TreasuryInFinancesWindow",125},{"TreasuryMonthlyReport",126},
            {"TreasuryPanel",127},{"TreasuryWarnings",128},{"ShipTrade",129},{"ShipTradeAiSells",130},{"ShipTradeRefits",131},{"BalanceGrantOnlyWhenSolvent",132},{"BalanceOfPowerEvents",133},
            {"CoalitionBalanceOfPower",134},{"CoalitionWar",135},{"RelationRepair",136},{"BattleDispatches",137},{"BattleReport",138},{"DispatchNationalLanguages",139},
            {"DispatchPlayerOriginalLanguage",140},{"AlwaysShowSplashes",141},{"BarrelElevation",142},{"BattleDispatchBubbles",143},{"BattleHitDispatches",144},{"EnemyRangeTags",145},
            {"ModUiScaleAuto",146},{"ReShadeNavalLook",147},{"ArmorDiagnostics",148},{"BallisticsLog",149},{"BattleAiLog",150},{"DamageDiagLog",151},{"HitLog",152},
            {"PerformanceLog",153},{"BetaUpdates",154},{"CheckForUpdates",155},{"CheckOtherMods",156},{"SaveCompatibilityFix",157},{"ShareLogs",158},{"UpdateDownloadButton",159},
            {"UsageStatistics",160},{"AiTorpedoCapitalsFirst",161},{"CapturedTechNotCopied",162},{"BattleHourChoice",163},{"AiSmokeOnlyWhenNeeded",164},{"AiTorpedoPincer",165},
            {"AiApproachSmoke",166},{"AiWithdrawLowAmmo",167},{"BattleDispatchPanel",168},{"PrizeScuttle",169},{"DivisionTorpedoSplit",170},{"StackedGunsPenalty",171},
            {"TransportSpeedByYear",172},{"EnemyProfileLog",173},{"ThanksOnMainMenu",174},{"AirStrikeTest",175},{"EnemyFitBuildMix",176},{"CustomBattleCarrier",177},
            {"PlayerDesignsFirst",178},{"CustomBattleSaveDesigns",179},{"DesignStats",180},{"DesignStatsPreferBest",181},{"NavalAviationRecon",182},{"NavalAviationSpotting",183},
            {"ShipGenSecondaryTower",184},{"NavalAviationSpotterPlanes",185},
        };
        static readonly Dictionary<string, string> GroupOf = new Dictionary<string, string>
        {
            {"LightShipTorpedoCharge","Battle AI"},{"CapitalTorpedoCharge","Battle AI"},{"CapitalCloseInWhenOutgunned","Battle AI"},
            {"CloseInOnlyIfPenetrates","Battle AI"},{"EvasiveManeuvers","Battle AI"},{"TorpedoTurnAway","Battle AI"},{"FleetTorpedoAttack","Battle AI"},{"Ramming","Battle AI"},{"TorpedoReloadCycle","Battle AI"},{"ReloadReturnFraction","Battle AI"},{"ReloadMaxSeconds","Battle AI"},{"CapitalSupportScreen","Battle AI"},{"SupportRangeFraction","Battle AI"},{"CoverRetreat","Battle AI"},{"CoverRetreatStrengthRatio","Battle AI"},{"CoverValueRatio","Battle AI"},{"ValueBasedAbort","Battle AI"},{"NightRules","Battle AI"},{"NightVisibilityKm","Battle AI"},{"CruiserRoles","Battle AI"},{"FireDistribution","Battle AI"},{"CapitalEscapeWhenHopeless","Battle AI"},{"HopelessEscapeDelay","Battle AI"},{"HopelessFightOnRatio","Battle AI"},{"EscapeMissionLogic","Battle AI"},{"FightWhenCannotEscape","Battle AI"},
            {"DetachSlowShips","Battle AI"},{"FocusFire","Battle AI"},{"SmartTargeting","Battle AI"},{"SmartTargetingAi","Battle AI"},{"TransportsAfterEscort","Battle AI"},{"TransportLowCaliberMax","Battle AI"},{"TransportEscortRangeMult","Battle AI"},{"TransportEscortMemorySeconds","Battle AI"},
            {"PlayerScoutCharge","Battle AI"},{"TorpedoFriendlyLane","Battle AI"},{"BattleReport","Battle AI"},{"BattleAiLog","Battle AI"},{"ArmorDiagnostics","Battle AI"},
            {"CrewLevelsRescale","Crews and prizes"},{"SurrenderByTraining","Crews and prizes"},{"CrewPanic","Crews and prizes"},
            {"CaptureSurrenderedShips","Crews and prizes"},{"SurrenderedShipsToBattleWinner","Crews and prizes"},{"CoreProvincesIndependence","Campaign AI"},{"UngovernedToSuccessors","Campaign AI"},{"CoreSuppressEscalation","Campaign AI"},{"CoreOccupationUnrest","Campaign AI"},{"CoreOccupationCost","Campaign AI"},{"ReunificationChance","Campaign AI"},{"CoreRevoltBonus","Campaign AI"},{"CoreRevoltRampMonths","Campaign AI"},{"TorpedoTargetSizeRatio","Battle AI"},{"MassAttackStrengthRatio","Battle AI"},{"MainShipSizeRatio","Battle AI"},{"MassAttackPerTarget","Battle AI"},{"WithdrawWhenOutmatched","Battle AI"},{"MinorFleetToConqueror","Crews and prizes"},
            {"FogOfWar","Fog of war"},{"FogStrategicMap","Fog of war"},{"FogBattleReports","Fog of war"},{"FogTacticalIdentification","Fog of war"},{"FogHideEnemyDetails","Fog of war"},{"MapPortForces","Fog of war"},{"FogAiPerception","Fog of war"},{"FogInterceptions","Fog of war"},{"FogStrength","Fog of war"},{"FogStaleContacts","Fog of war"},{"FogNightFactors","Fog of war"},
            {"InstallModDesigns","Campaign AI"},{"InstallShippackDesigns","Campaign AI"},{"AutoShareDesigns","Campaign AI"},{"ShareToAllNations","Campaign AI"},{"CruisersWhenPoor","Campaign AI"},
            {"AiTechBudgetBalance","Campaign AI"},{"AiBestTorpedoDiameter","Campaign AI"},{"AiMinTorpedoTubes","Campaign AI"},{"AiDestroyerTubesTonnage","Campaign AI"},{"AiDesignOnlyWhenBuildable","Campaign AI"},{"FixStalledConstruction","Campaign AI"},
            {"BankruptBuildsCheapShips","Campaign AI"},{"NationalTechBonuses","Campaign AI"},
            {"SeriesProduction","Economy"},{"ForeignPortCapacity","Economy"},{"CheckOtherMods","Information and updates"},{"ShareLogs","Information and updates"},{"InflationEconomy","Economy"},{"FinancialCrisis","Economy"},
            {"BalanceOfPowerEvents","Diplomacy"},{"BalanceGrantOnlyWhenSolvent","Diplomacy"},{"CoalitionWar","Diplomacy"},{"CoalitionBalanceOfPower","Diplomacy"},{"BrokenDesignsObsolete","Campaign AI"},{"DesignRecorder","Interface"},{"DeckHitsMeetDeck","Battle"},{"DamageByTonnage","Battle"},{"DamageByTonnageMin","Battle"},{"DamageByTonnageMax","Battle"},{"DamageByTonnageTypes","Battle"},{"StructureSinkPercent","Battle"},{"DestroyedSectionDamagePenalty","Battle"},{"DamageByTonnageRefHpPerTon","Tools"},{"ImpactAngleFromFall","Battle"},{"AiCrossTorpedo","Battle AI"},{"PortlessPowersWait","Campaign AI"},{"BattleDispatches","Battle AI"},{"DispatchNationalLanguages","Battle AI"},{"DispatchPlayerOriginalLanguage","Battle AI"},{"HistBuilderRun","Tools"},{"HistBuilderFilter","Tools"},{"HistBuilderLimit","Tools"},{"HistBuilderTries","Tools"},
            {"OccupationEvents","Campaign AI"},{"OccupationRelations","Campaign AI"},{"OccupationPayShare","Campaign AI"},{"OccupationHardUnrest","Campaign AI"},{"OccupationHardRevolt","Campaign AI"},{"OccupationAutonomyRelief","Campaign AI"},{"OccupationAutonomySteps","Campaign AI"}, // 1.11.151
            {"DivisionOrders","Battle AI"},{"TorpedoSettings","Battle AI"},{"TorpedoTargetPriority","Battle AI"},{"TorpedoTargetPriorityTest","Tools"},{"DivisionOrdersVanilla","Battle AI"},{"DivisionLeash","Battle AI"},{"ScoutTowardsSmoke","Battle AI"},{"DivisionBreakOffDamage","Battle AI"},{"ReShadeNavalLook","Other"},{"RealisticBallistics","Battle AI"},{"BallisticsLog","Battle AI"},{"FireControlTechAccuracy","Battle AI"},{"FireControlTechScale","Battle AI"},{"FireControlTechRadar","Battle AI"},{"HistoricalDamage","Battle AI"},{"FireControlTechDispersion","Battle AI"},{"HistoricalGuns","Battle AI"},{"HistoricalWeights","Battle AI"},{"HistoricalParts","Battle AI"}, // 1.11.154, 1.11.155
            {"UsageStatistics","Information and updates"},{"TreasuryPanel","Information and updates"},{"TreasuryMonthlyReport","Information and updates"},{"TreasuryWarnings","Information and updates"},
            {"TreasuryInFinancesWindow","Information and updates"},{"SaveCompatibilityFix","Information and updates"},{"CheckForUpdates","Information and updates"},{"BetaUpdates","Information and updates"},{"UpdateChannel","Information and updates"},{"UpdateDownloadButton","Information and updates"},
        };
        // options that only take effect at the next game start
        static readonly HashSet<string> RestartOnly = new HashSet<string> { "CheckForUpdates", "UpdateDownloadButton", "BetaUpdates", "UpdateChannel", "ReShadeNavalLook" };

        internal static void CreatePrefs(MelonPreferences_Category c)
        {
            cat = c;
            Key = c.CreateEntry("SettingsPanelKey", "F10", null, "Key that opens / closes the in-game settings panel of the mod (Unity KeyCode name)");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
            LastVersion = c.CreateEntry("SwitchesVersion", "", null, "Mod version the on/off switches were last put back to their defaults for (written by the mod)");
            ResetOnNew = c.CreateEntry("ResetSwitchesOnNewVersion", true, null, "Each new mod version puts every on/off switch back to its default once, so new and changed mechanics are tried (update and problem-report options are kept)");
        }

        // 1.11.329 (author 28.10: "with every new version reset the settings to defaults so people test the new functions - once they
        // block something they never use it again"): the first time a new DLL version runs, every switch of the visible tabs except
        // Info & updates and Tools goes back to its default. Numbers, keys and the author's flags ("-") are not touched.
        internal static MelonPreferences_Entry<string> LastVersion;
        internal static MelonPreferences_Entry<bool> ResetOnNew;
        static bool resetChecked;
        static string resetNote = "";
        static void ResetOnNewVersion()
        {
            resetChecked = true;
            try
            {
                if (cat == null || LastVersion == null) return;
                var v = typeof(SettingsUi).Assembly.GetName().Version; string ver = v.Major + "." + v.Minor + "." + v.Build;
                if (LastVersion.Value == ver) return;
                string was = LastVersion.Value;
                LastVersion.Value = ver;
                if (ResetOnNew == null || !ResetOnNew.Value) { MelonPreferences.Save(); return; }
                var changed = new List<string>();
                foreach (var e in cat.Entries)
                {
                    var b = e as MelonPreferences_Entry<bool>; if (b == null) continue;
                    string g; if (!TabOf.TryGetValue(b.Identifier, out g)) continue;
                    if (g == "-" || g == "Info & updates" || g == "Tools") continue;
                    if (b.Value == b.DefaultValue) continue;
                    b.Value = b.DefaultValue; changed.Add("#" + NumberOf(b.Identifier) + " " + b.Identifier + " = " + (b.Value ? "on" : "off"));
                }
                MelonPreferences.Save();
                MelonLogger.Msg("[DIP GG Fixes][Settings] new version " + ver + " (was " + (was == "" ? "none" : was) + "): " + changed.Count + " switches put back to defaults" + (changed.Count > 0 ? ": " + string.Join(", ", changed) : ""));
                if (changed.Count > 0) { resetNote = "New version " + ver + ": " + changed.Count + " switches back at defaults"; status = resetNote; statusUntil = float.MaxValue; }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Settings] reset on new version: " + e.Message); }
        }

        // 1.11.251: a number that belongs to a switch, changed with - / + at the right of its row
        class Num { public Func<MelonPreferences_Entry<float>> E; public float Step, Min, Max; public string Unit; }
        static readonly Dictionary<string, Num> Nums = new Dictionary<string, Num>
        {
            {"TorpedoSettings", new Num { E = () => TorpedoSettings.ChangeSeconds, Step = 10f, Min = 0f, Max = 300f, Unit = "s tube reset" } },
            {"AirStrikeTest", new Num { E = () => AirStrike.Scale, Step = 0.5f, Min = 0.5f, Max = 5f, Unit = "x aircraft size" } },   // 1.11.385
            {"CustomBattleCarrier", new Num { E = () => AirStrike.Scale, Step = 0.5f, Min = 0.5f, Max = 5f, Unit = "x aircraft size" } },
        };
        static int NumberOf(string id) { int n; return NumOf.TryGetValue(id, out n) ? n : 10000; }
        static List<MelonPreferences_Entry<bool>> Entries(string group)
        {
            var list = new List<MelonPreferences_Entry<bool>>();
            if (cat == null) return list;
            foreach (var e in cat.Entries)
            {
                var b = e as MelonPreferences_Entry<bool>; if (b == null) continue;
                string g;
                if (!TabOf.TryGetValue(b.Identifier, out g))
                {
                    if (!GroupOf.TryGetValue(b.Identifier, out g)) g = "Other";
                    string ng; if (OldGroup.TryGetValue(g, out ng)) g = ng;
                    if (Array.IndexOf(Groups, g) < 0) g = "Other";
                }
                if (g == group) list.Add(b);
            }
            list.Sort((a, c) => NumberOf(a.Identifier).CompareTo(NumberOf(c.Identifier)));
            return list;
        }

        // 1.11.47: the game's EventSystem is no longer disabled while the panel is open. GameManager.CanHandleMouseInput (called
        // every frame by the TweaksAndFixes campaign camera) reads EventSystem.current and threw a NullReferenceException every
        // frame (about 4500 errors in 20 s in 1.11.46). A click on the panel may now also reach the map behind it.
        internal static void SetOpen(bool v)
        {
            open = v;
            MelonLogger.Msg("[DIP GG Fixes][Settings] panel " + (v ? "opened" : "closed"));
        }

        internal static void Tick()
        {
            try
            {
                if (!resetChecked) ResetOnNewVersion();
                if (Input.GetKeyDown(key)) SetOpen(!open);
                if (!open) return;
                if (Input.GetKeyDown(KeyCode.Escape)) { SetOpen(false); return; }
                if (Input.GetMouseButtonDown(0))
                {
                    var p = UiScale.Mouse();
                    foreach (var kv in clicks) if (kv.Key.Contains(p)) { kv.Value(); break; }
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Settings] tick: " + e.Message); }
        }

        static void Styles()
        {
            if (sTitle != null) return;
            sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
            sName = new GUIStyle(); sName.fontSize = 14; sName.richText = true; sName.normal.textColor = Color.white;
            sDesc = new GUIStyle(); sDesc.fontSize = 11; sDesc.wordWrap = true; sDesc.normal.textColor = new Color(0.78f, 0.78f, 0.78f);
            sTab = new GUIStyle(); sTab.fontSize = 13; sTab.alignment = TextAnchor.MiddleCenter; sTab.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            sTabOn = new GUIStyle(); sTabOn.fontSize = 13; sTabOn.alignment = TextAnchor.MiddleCenter; sTabOn.normal.textColor = new Color(1f, 0.85f, 0.35f);
            sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.normal.textColor = Color.white;
        }

        static void Button(Rect r, string label, GUIStyle st, Action a)
        {
            GUI.Box(r, ""); GUI.Label(r, label, st); next.Add(new KeyValuePair<Rect, Action>(r, a));
        }

        static void Save(string what)
        {
            try { MelonPreferences.Save(); status = what + " - saved"; }
            catch (Exception e) { status = what + " - save failed: " + e.Message; }
            statusUntil = Time.realtimeSinceStartup + 4f;
            MelonLogger.Msg("[DIP GG Fixes][Settings] " + status);
        }

        // Tools tab: problem report (1.11.60); scenarios are added here later
        static void DrawTools(Rect r, float top, float w)
        {
            float y = top;
            GUI.Label(new Rect(r.x + 14f, y, w - 28f, 22f), "<b>Problem report</b>", sName);
            GUI.Label(new Rect(r.x + 14f, y + 22f, w - 28f, 64f), "Writes one zip file to your desktop with the game logs, the mod's settings, the version file, a list of your Mods folder, system info and an error summary" + (Diagnostics.Usage != null && Diagnostics.Usage.Value ? " plus your usage counters" : "") + ". Nothing is sent - attach the file yourself to a post on Nexus, Steam or GitHub and describe what happened. Your Windows user and PC names are replaced in the files.", sDesc);
            Button(new Rect(r.x + 14f, y + 90f, 260f, 28f), "<b>Create problem report</b>", sBtn, () =>
            {
                var f = Diagnostics.CreateReport();
                status = f != null ? "Report written: " + System.IO.Path.GetFileName(f) + " (desktop)" : "Report failed - see Latest.log";
                statusUntil = Time.realtimeSinceStartup + 12f;
            });
            y += 136f;
            bool u = Diagnostics.Usage != null && Diagnostics.Usage.Value;
            GUI.Label(new Rect(r.x + 14f, y, w - 28f, 22f), "<b>Usage statistics</b>", sName);
            Button(new Rect(r.x + 14f, y + 24f, 64f, 24f), u ? "<color=#7CFC7C><b>ON</b></color>" : "<color=#FF8080><b>OFF</b></color>", sBtn, () =>
            {
                Diagnostics.Usage.Value = !Diagnostics.Usage.Value; if (Diagnostics.Consent != null) Diagnostics.Consent.Value = Diagnostics.Usage.Value ? "yes" : "no";
                Save("UsageStatistics = " + (Diagnostics.Usage.Value ? "on" : "off"));
            });
            GUI.Label(new Rect(r.x + 90f, y + 24f, w - 110f, 60f), "Simple counters kept on this PC only (sessions per mod version, campaign turns, battles and their length, nation and year). They help the author balance the mod when you include them in a problem report.", sDesc);
            float fy = r.yMax - 40f;
            string tip = Time.realtimeSinceStartup < statusUntil ? status : "";
            GUI.Label(new Rect(r.x + 14f, fy + 5f, w - 28f, 22f), tip, sDesc);
        }

        internal static void OnGUI()
        {
            if (!open) { if (clicks.Count > 0) clicks.Clear(); return; }
            try
            {
                Styles();
                next.Clear();
                float w = Math.Min(760f, UiScale.W - 40f), h = Math.Min(640f, UiScale.H - 60f);
                var r = new Rect((UiScale.W - w) / 2f, (UiScale.H - h) / 2f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 120f, 24f), "<b>Iron Admiral - mod settings</b>   (" + Key.Value + " or Esc closes)", sTitle);
                Button(new Rect(r.xMax - 40f, r.y + 8f, 30f, 26f), "X", sBtn, () => SetOpen(false));

                // tabs, 5 per row (three rows with all 13 tabs)
                float tx = r.x + 10f, ty = r.y + 42f, tw = (w - 20f) / 5f;
                for (int i = 0; i < Groups.Length; i++)
                {
                    int gi = i;
                    if (Groups[i] != "Tools" && Entries(Groups[i]).Count == 0) continue;
                    var tr = new Rect(tx, ty, tw - 4f, 26f);
                    Button(tr, Groups[i], i == tab ? sTabOn : sTab, () => { tab = gi; page = 0; });
                    tx += tw; if (tx + tw > r.xMax) { tx = r.x + 10f; ty += 30f; }
                }
                float top = ty + 38f;

                if (Groups[tab] == "Tools") { DrawTools(r, top, w); clicks.Clear(); clicks.AddRange(next); return; }
                var list = Entries(Groups[tab]);
                float rowH = 60f;   // 1.11.330: room for three lines of description (46 let them run into the next row - seen in 1.11.329)
                int per = Math.Max(1, (int)((r.yMax - 70f - top) / rowH));
                int pages = Math.Max(1, (list.Count + per - 1) / per);
                if (page >= pages) page = pages - 1;
                float y = top;
                for (int i = page * per; i < Math.Min(list.Count, (page + 1) * per); i++)
                {
                    var e = list[i];
                    bool v = e.Value;
                    var box = new Rect(r.x + 14f, y + 2f, 64f, 24f);
                    Button(box, v ? "<color=#7CFC7C><b>ON</b></color>" : "<color=#FF8080><b>OFF</b></color>", sBtn, () =>
                    {
                        e.Value = !e.Value;
                        Save(e.Identifier + " = " + (e.Value ? "on" : "off") + (RestartOnly.Contains(e.Identifier) ? " (applies after a game restart)" : ""));
                    });
                    float descW = w - 110f;
                    if (Nums.TryGetValue(e.Identifier, out var nm) && nm.E() != null)
                    {
                        var fe = nm.E(); descW = w - 290f;
                        float nx = r.x + w - 190f;
                        Button(new Rect(nx, y + 2f, 26f, 24f), "-", sBtn, () => { fe.Value = Mathf.Max(nm.Min, fe.Value - nm.Step); Save(fe.Identifier + " = " + fe.Value.ToString("0.##")); });
                        GUI.Label(new Rect(nx + 30f, y + 5f, 104f, 20f), "<b>" + fe.Value.ToString("0.##") + "</b> " + nm.Unit, sName);
                        Button(new Rect(nx + 138f, y + 2f, 26f, 24f), "+", sBtn, () => { fe.Value = Mathf.Min(nm.Max, fe.Value + nm.Step); Save(fe.Identifier + " = " + fe.Value.ToString("0.##")); });
                    }
                    string name = e.DisplayName != null && e.DisplayName != e.Identifier ? e.DisplayName : e.Identifier;
                    int no = NumberOf(e.Identifier);
                    GUI.Label(new Rect(r.x + 90f, y + 3f, descW, 20f), (no < 10000 ? "<size=10><color=#9A9A9A>#" + no + "</color></size>  " : "") + "<b>" + name + "</b>" + (RestartOnly.Contains(e.Identifier) ? "  <color=#C0C0C0>(after restart)</color>" : ""), sName);
                    GUI.Label(new Rect(r.x + 90f, y + 21f, descW, rowH - 20f), e.Description ?? "", sDesc);
                    y += rowH;
                }

                // footer
                float fy = r.yMax - 40f;
                if (pages > 1)
                {
                    Button(new Rect(r.x + 14f, fy, 40f, 26f), "<", sBtn, () => { if (page > 0) page--; });
                    GUI.Label(new Rect(r.x + 60f, fy + 4f, 90f, 22f), $"page {page + 1}/{pages}", sName);
                    Button(new Rect(r.x + 150f, fy, 40f, 26f), ">", sBtn, () => { if (page < pages - 1) page++; });
                }
                Button(new Rect(r.xMax - 250f, fy, 236f, 26f), "All in this group: defaults", sBtn, () =>
                {
                    foreach (var e in Entries(Groups[tab])) e.Value = e.DefaultValue;
                    Save(Groups[tab] + ": defaults restored");
                });
                string tip = Time.realtimeSinceStartup < statusUntil ? status : "Changes apply at once (battle options from the next AI decision, campaign options from the next turn).";
                GUI.Label(new Rect(r.x + 200f, fy + 5f, w - 470f, 22f), tip, sDesc);

                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Settings] draw failed: " + e); }
        }
    }
}

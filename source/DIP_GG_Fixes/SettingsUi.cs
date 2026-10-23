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

        // group order and membership; anything not listed lands in "Other"
        static readonly string[] Groups = { "Battle AI", "Fog of war", "Crews and prizes", "Campaign AI", "Economy", "Diplomacy", "Information and updates", "Tools", "Other" };
        static readonly Dictionary<string, string> GroupOf = new Dictionary<string, string>
        {
            {"LightShipTorpedoCharge","Battle AI"},{"CapitalTorpedoCharge","Battle AI"},{"CapitalCloseInWhenOutgunned","Battle AI"},
            {"CloseInOnlyIfPenetrates","Battle AI"},{"EvasiveManeuvers","Battle AI"},{"TorpedoTurnAway","Battle AI"},{"FleetTorpedoAttack","Battle AI"},{"Ramming","Battle AI"},{"TorpedoReloadCycle","Battle AI"},{"ReloadReturnFraction","Battle AI"},{"ReloadMaxSeconds","Battle AI"},{"CapitalSupportScreen","Battle AI"},{"SupportRangeFraction","Battle AI"},{"CoverRetreat","Battle AI"},{"CoverRetreatStrengthRatio","Battle AI"},{"CoverValueRatio","Battle AI"},{"ValueBasedAbort","Battle AI"},{"NightRules","Battle AI"},{"NightVisibilityKm","Battle AI"},{"CruiserRoles","Battle AI"},{"FireDistribution","Battle AI"},{"CapitalEscapeWhenHopeless","Battle AI"},{"HopelessEscapeDelay","Battle AI"},{"HopelessFightOnRatio","Battle AI"},{"EscapeMissionLogic","Battle AI"},{"FightWhenCannotEscape","Battle AI"},
            {"DetachSlowShips","Battle AI"},{"FocusFire","Battle AI"},{"SmartTargeting","Battle AI"},{"SmartTargetingAi","Battle AI"},
            {"PlayerScoutCharge","Battle AI"},{"TorpedoFriendlyLane","Battle AI"},{"BattleReport","Battle AI"},{"BattleAiLog","Battle AI"},{"ArmorDiagnostics","Battle AI"},
            {"CrewLevelsRescale","Crews and prizes"},{"SurrenderByTraining","Crews and prizes"},{"CrewPanic","Crews and prizes"},
            {"CaptureSurrenderedShips","Crews and prizes"},{"SurrenderedShipsToBattleWinner","Crews and prizes"},{"CoreProvincesIndependence","Campaign AI"},{"CoreRevoltBonus","Campaign AI"},{"CoreRevoltRampMonths","Campaign AI"},{"TorpedoTargetSizeRatio","Battle AI"},{"MassAttackStrengthRatio","Battle AI"},{"MainShipSizeRatio","Battle AI"},{"MassAttackPerTarget","Battle AI"},{"WithdrawWhenOutmatched","Battle AI"},{"MinorFleetToConqueror","Crews and prizes"},
            {"FogOfWar","Fog of war"},{"FogStrategicMap","Fog of war"},{"FogBattleReports","Fog of war"},{"FogTacticalIdentification","Fog of war"},{"FogHideEnemyDetails","Fog of war"},{"MapPortForces","Fog of war"},{"FogAiPerception","Fog of war"},{"FogInterceptions","Fog of war"},{"FogStrength","Fog of war"},{"FogStaleContacts","Fog of war"},{"FogNightFactors","Fog of war"},
            {"InstallModDesigns","Campaign AI"},{"InstallShippackDesigns","Campaign AI"},{"AutoShareDesigns","Campaign AI"},{"ShareToAllNations","Campaign AI"},{"CruisersWhenPoor","Campaign AI"},
            {"AiTechBudgetBalance","Campaign AI"},{"AiDesignOnlyWhenBuildable","Campaign AI"},{"FixStalledConstruction","Campaign AI"},
            {"BankruptBuildsCheapShips","Campaign AI"},{"NationalTechBonuses","Campaign AI"},
            {"SeriesProduction","Economy"},{"ForeignPortCapacity","Economy"},{"CheckOtherMods","Information and updates"},{"ShareLogs","Information and updates"},{"AutoArmorButton","Tools"},{"InflationEconomy","Economy"},{"FinancialCrisis","Economy"},
            {"BalanceOfPowerEvents","Diplomacy"},{"BalanceGrantOnlyWhenSolvent","Diplomacy"},{"CoalitionWar","Diplomacy"},{"PortlessPowersWait","Campaign AI"},{"BattleDispatches","Battle AI"},{"HistBuilderRun","Tools"},{"HistBuilderFilter","Tools"},{"HistBuilderLimit","Tools"},{"HistBuilderTries","Tools"},
            {"UsageStatistics","Information and updates"},{"TreasuryPanel","Information and updates"},{"TreasuryMonthlyReport","Information and updates"},{"TreasuryWarnings","Information and updates"},
            {"TreasuryInFinancesWindow","Information and updates"},{"SaveCompatibilityFix","Information and updates"},{"CheckForUpdates","Information and updates"},{"BetaUpdates","Information and updates"},{"UpdateChannel","Information and updates"},{"UpdateDownloadButton","Information and updates"},
        };
        // options that only take effect at the next game start
        static readonly HashSet<string> RestartOnly = new HashSet<string> { "CheckForUpdates", "UpdateDownloadButton", "BetaUpdates", "UpdateChannel" };

        internal static void CreatePrefs(MelonPreferences_Category c)
        {
            cat = c;
            Key = c.CreateEntry("SettingsPanelKey", "F10", null, "Key that opens / closes the in-game settings panel of the mod (Unity KeyCode name)");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
        }

        static List<MelonPreferences_Entry<bool>> Entries(string group)
        {
            var list = new List<MelonPreferences_Entry<bool>>();
            if (cat == null) return list;
            foreach (var e in cat.Entries)
            {
                var b = e as MelonPreferences_Entry<bool>; if (b == null) continue;
                string g; if (!GroupOf.TryGetValue(b.Identifier, out g)) g = "Other";
                if (g == group) list.Add(b);
            }
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

                // tabs (two rows when narrow)
                float tx = r.x + 10f, ty = r.y + 42f, tw = (w - 20f) / 4f;
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
                float rowH = 46f;
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
                    string name = e.DisplayName != null && e.DisplayName != e.Identifier ? e.DisplayName : e.Identifier;
                    GUI.Label(new Rect(r.x + 90f, y + 3f, w - 110f, 20f), "<b>" + name + "</b>" + (RestartOnly.Contains(e.Identifier) ? "  <color=#C0C0C0>(after restart)</color>" : ""), sName);
                    GUI.Label(new Rect(r.x + 90f, y + 21f, w - 110f, rowH - 20f), e.Description ?? "", sDesc);
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

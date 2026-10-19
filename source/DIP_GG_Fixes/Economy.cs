// Iron Admiral - economy add-on (DIP_GG_Fixes 1.11.22)
//
// 1. Series production (all nations, construction and refits)
//    A ship's cost and building time depend on its position in the series of its design:
//      cost  = prototype +15 %, then a Wright learning curve of 94 % per doubling (floor 80 %)
//      time  = prototype +10 %, then 90 % per doubling, never below 2 months; ships of one design laid down
//              together that fit into the largest dock together (m x tonnage <= Player.shipyard) get x m^log2(0.8)
//    Calibration: Royal Navy dreadnought classes (Bellerophon 1,763k / Temeraire 1,744k / Superb 1,677k GBP),
//    US Navy shipyard learning study 1978 (85-95 % labour curves, 90 % typical, materials 98 %), FFG(X) lead ship
//    1.28 bn vs second 1.10 bn, multiyear/block buys 5-10 % cheaper, Liberty ships 6+ months -> 30 days.
//      - learning, batch discount and dock bonus start only for ships laid down after the first ship of the design
//        was completed; sisters laid down while the prototype is still on the slip are built at base cost and time
//      - ships of one design laid down within 6 months of each other: -5 % (batch orders)
//      - maintenance -3 % per active sister ship, max -15 %
//      - a gap of 3 years without a new ship of the design resets the learning
//      - shipyard occupancy (tonnage under construction / capacity): below 30 % cost -10 % and time -5 %,
//        above 90 % cost +10 % and time +10 % (idle yards bid low, full yards charge more and deliver later)
//    Hooks: Ship.BuildingTime / BuildingCostPerMonth (construction), Ship.RefitTime / RefitCostPerMonth (refit),
//    Ship.MaintenanceCost. Ship.Cost() is left alone, so victory points, sale and scrap values do not change.
//    RefitTime, RepairingTime, CommissioningTime and their cost methods call BuildingTime internally; a depth
//    counter keeps the construction factors out of them.
//
// 2. Inflation, credit and growth (great powers)
//    Inflation is an annual rate in percent; it raises (deflation lowers) all expenses by that percentage.
//      - extraordinary inflows raise it by k x inflow / annual GDP x 100 (k 0.4 for loans, grants, reparations,
//        loot, sales; 1.2 for money printing). GDP = Player.StateBudget(), the value the game itself compares with
//        fleet tonnage (ai_ship_gdp_ratio).
//      - cuts in spending lower it by 0.4 x 12 x monthly cut / GDP x 100
//      - research lowers it and adds growth with diminishing returns: ln(1 + research share of GDP / 0.2 %)
//      - shipyard expansion adds growth in proportion to the expansion spending relative to GDP, while it runs
//        and 12 months after
//      - it decays by 6 % of its value per month (towards zero from both sides); deflation is possible
//      - loan interest max(3 %, 3 x inflation) a year, max(8 %, 5 x inflation) for 15 years after a bankruptcy;
//        unspent cash earns 1.5 x inflation a year; all of it is booked in Player.Expenses()
//      - high inflation and sharp rises in inflation add unrest
//    The state is saved next to the game's save (UserData/IronAdmiral_Economy, one small file per save, found by
//    the campaign date and the cash of every great power). Old saves fall back to the 1.11.16-1.11.21 encoding
//    in Player.inflation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class EcoPrefs
    {
        internal static MelonPreferences_Entry<bool> SeriesOn, EconomyOn;
        internal static MelonPreferences_Entry<float> ProtoCost, CostCurve, CostFloor, ProtoTime, TimeCurve, MinMonths, BatchDiscount, BatchMonths;
        internal static MelonPreferences_Entry<float> SisterMaint, SisterMaintMax, ResetMonths, YardLow, YardHigh, YardLowCost, YardLowTime, YardHighCost, YardHighTime;
        internal static MelonPreferences_Entry<float> KLoan, KPrint, KCut, Decay, InflMin, InflMax, InflowThresholdMonths, InflowThresholdGdp;
        internal static MelonPreferences_Entry<float> SpreadMin, SpreadMax, SpreadDebtGdp, SpreadDeficit, SpreadWar, HighInflFrom, HighInflSpread, RateFloor, SavingsMult, InflBaseline, CoalRatio, CoalStart, CoalJoin, CoalAttitude, CoalMaxWars;
        internal static MelonPreferences_Entry<bool> CoalOn;
        internal static MelonPreferences_Entry<float> ResearchR0, ResearchGrowth, ResearchDisinflation, ShipyardGrowth, ShipyardAfterMonths;
        internal static MelonPreferences_Entry<float> UnrestFrom, UnrestPerPoint, UnrestJump, UnrestJumpFactor;
        internal static MelonPreferences_Entry<int> BankruptYears;
        internal static MelonPreferences_Entry<float> DockCurve;

        internal static void Create(MelonPreferences_Category cat)
        {
            SeriesOn = cat.CreateEntry("SeriesProduction", true, null, "Series production: prototypes cost more and take longer, later ships of the same design get cheaper and faster (construction and refits, all nations)");
            ProtoCost = cat.CreateEntry("SeriesPrototypeCost", 1.15f, null, "Cost factor of the first ship of a design (prototype)");
            CostCurve = cat.CreateEntry("SeriesCostCurve", 0.94f, null, "Learning curve for cost: factor per doubling of the series (Wright curve)");
            CostFloor = cat.CreateEntry("SeriesCostFloor", 0.80f, null, "Lowest cost factor reachable through the learning curve");
            ProtoTime = cat.CreateEntry("SeriesPrototypeTime", 1.10f, null, "Building-time factor of the prototype");
            TimeCurve = cat.CreateEntry("SeriesTimeCurve", 0.90f, null, "Learning curve for building time: factor per doubling of the series");
            MinMonths = cat.CreateEntry("SeriesMinBuildMonths", 2f, null, "Building time never falls below this many months through series effects");
            BatchDiscount = cat.CreateEntry("SeriesBatchDiscount", 0.05f, null, "Cost discount when two or more ships of the design are laid down within SeriesBatchMonths (refits: refitted at the same time)");
            BatchMonths = cat.CreateEntry("SeriesBatchMonths", 6f, null, "Window for the batch discount in months");
            DockCurve = cat.CreateEntry("SeriesDockTimeCurve", 0.80f, null, "Extra building-time curve for ships of one design built together in one dock: when m ships laid down within SeriesBatchMonths fit together into the largest dock (m x tonnage <= shipyard size), time x m^log2(this). Small ships gain a lot, battleships that fill the dock nothing");
            SisterMaint = cat.CreateEntry("SeriesSisterMaintenance", 0.03f, null, "Maintenance discount per active sister ship of the same design");
            SisterMaintMax = cat.CreateEntry("SeriesSisterMaintenanceMax", 0.15f, null, "Maximum maintenance discount from sister ships");
            ResetMonths = cat.CreateEntry("SeriesResetMonths", 36f, null, "A gap of this many months without a new ship of the design resets the learning (next ship counts as a prototype)");
            YardLow = cat.CreateEntry("ShipyardLowOccupancy", 0.30f, null, "Shipyard occupancy (tonnage under construction / capacity) below which orders are cheaper and faster");
            YardHigh = cat.CreateEntry("ShipyardHighOccupancy", 0.90f, null, "Shipyard occupancy above which orders cost more and take longer (on top of the game's own overload penalty)");
            YardLowCost = cat.CreateEntry("ShipyardLowCostFactor", 0.90f, null, "Cost factor with an idle shipyard");
            YardLowTime = cat.CreateEntry("ShipyardLowTimeFactor", 0.95f, null, "Time factor with an idle shipyard");
            YardHighCost = cat.CreateEntry("ShipyardHighCostFactor", 1.10f, null, "Cost factor with a full shipyard");
            YardHighTime = cat.CreateEntry("ShipyardHighTimeFactor", 1.10f, null, "Time factor with a full shipyard");

            EconomyOn = cat.CreateEntry("InflationEconomy", true, null, "Inflation, credit interest, savings interest and growth effects for great powers");
            KLoan = cat.CreateEntry("InflationPerGdpLoan", 0.4f, null, "Inflation points per 1 % of annual GDP of extraordinary inflow (loans, grants, reparations, loot, sales)");
            KPrint = cat.CreateEntry("InflationPerGdpPrint", 1.2f, null, "Inflation points per 1 % of annual GDP of printed money");
            KCut = cat.CreateEntry("DisinflationPerGdpCut", 0.4f, null, "Inflation points removed per 1 % of annual GDP of spending cut, and added per 1 % of spending increase (monthly change against annual GDP)");
            Decay = cat.CreateEntry("InflationDecayShare", 0.06f, null, "Share of the current inflation (or deflation) that fades every month");
            InflMin = cat.CreateEntry("InflationMin", -15f, null, "Lowest inflation (deflation) in percent");
            InflMax = cat.CreateEntry("InflationMax", 300f, null, "Highest inflation in percent");
            InflowThresholdMonths = cat.CreateEntry("InflowThresholdMonthsOfBudget", 1f, null, "Unexplained cash gains count as extraordinary inflow only above this many months of naval budget...");
            InflowThresholdGdp = cat.CreateEntry("InflowThresholdGdpShare", 0.002f, null, "...and above this share of annual GDP");
            SpreadMin = cat.CreateEntry("CreditSpreadMin", 2f, null, "Loan interest = inflation + a credit spread; spread in points for the best credit rating");
            SpreadMax = cat.CreateEntry("CreditSpreadMax", 5f, null, "Spread for the worst credit rating (and for 15 years after a bankruptcy)");
            SpreadDebtGdp = cat.CreateEntry("CreditSpreadDebtGdpForMax", 0.5f, null, "Debt / annual GDP at which the debt part of the rating reaches the worst spread");
            SpreadDeficit = cat.CreateEntry("CreditSpreadDeficit", 0.5f, null, "Extra spread points while the monthly balance is negative");
            SpreadWar = cat.CreateEntry("CreditSpreadWar", 0.5f, null, "Extra spread points while at war");
            HighInflFrom = cat.CreateEntry("CreditHighInflationFrom", 15f, null, "Above this inflation lenders add an extra risk premium");
            HighInflSpread = cat.CreateEntry("CreditHighInflationSpreadPerPoint", 0.1f, null, "Extra spread points per inflation point above CreditHighInflationFrom");
            RateFloor = cat.CreateEntry("CreditRateFloor", 0.5f, null, "Lowest possible annual loan interest (with deflation)");
            BankruptYears = cat.CreateEntry("BankruptMarkYears", 15, null, "Years a bankruptcy raises the loan interest");
            SavingsMult = cat.CreateEntry("SavingsRateBelowInflation", 1.5f, null, "Unspent cash earns inflation minus this many points a year (never below 0)");
            InflBaseline = cat.CreateEntry("InflationBaseline", 1f, null, "Inflation returns to this level when nothing pushes it; research only slows inflation down to this level, deflation comes from large spending cuts");
            CoalOn = cat.CreateEntry("CoalitionWar", true, null, "Coalitions against a dominant power: AI great powers close to war with a nation (attitude at or below CoalitionAttitude, 'almost war') go to war together when their fleets, with the nation's current enemies, are strong enough (player and AI targets alike)");
            CoalRatio = cat.CreateEntry("CoalitionPowerRatio", 1.5f, null, "Combined fleet power of the hostile powers (and the target's current enemies) needed, as a multiple of the target's fleet power");
            CoalStart = cat.CreateEntry("CoalitionStartChance", 0.25f, null, "Chance per turn that the strongest member declares war when the coalition is strong enough and nobody fights the target yet");
            CoalJoin = cat.CreateEntry("CoalitionJoinChance", 0.5f, null, "Chance per turn for each other member to join once a member is at war with the target");
            CoalAttitude = cat.CreateEntry("CoalitionAttitude", -90f, null, "Attitude at or below which an AI great power counts as hostile");
            CoalMaxWars = cat.CreateEntry("CoalitionMaxWars", 3f, null, "An AI great power already fighting this many wars does not join a coalition");
            ResearchR0 = cat.CreateEntry("ResearchGdpScale", 0.002f, null, "Research effect = ln(1 + research spending / GDP / this)");
            ResearchGrowth = cat.CreateEntry("ResearchGrowthPerLog", 0.15f, null, "Annual GDP growth points per unit of the research effect");
            ResearchDisinflation = cat.CreateEntry("ResearchDisinflationPerLog", 0.05f, null, "Inflation points removed per month per unit of the research effect");
            ShipyardGrowth = cat.CreateEntry("ShipyardGrowthPerGdpPercent", 0.1f, null, "Annual GDP growth points per 1 % of GDP spent a year on shipyard expansion");
            ShipyardAfterMonths = cat.CreateEntry("ShipyardGrowthAfterMonths", 12f, null, "The expansion growth bonus lasts this many months after the expansion");
            UnrestFrom = cat.CreateEntry("InflationUnrestFrom", 10f, null, "Inflation above this adds unrest every month");
            UnrestPerPoint = cat.CreateEntry("InflationUnrestPerPoint", 0.05f, null, "Monthly unrest per inflation point above InflationUnrestFrom");
            UnrestJump = cat.CreateEntry("InflationJumpUnrestFrom", 5f, null, "A rise of inflation by more than this many points at once adds unrest");
            UnrestJumpFactor = cat.CreateEntry("InflationJumpUnrestFactor", 0.3f, null, "Unrest per point of such a rise");
        }

        internal static string Summary()
        {
            return $"Series production={SeriesOn.Value} (prototype cost x{ProtoCost.Value} time x{ProtoTime.Value}, curves {CostCurve.Value}/{TimeCurve.Value}, floor {CostFloor.Value}, min {MinMonths.Value} mo, dock curve {DockCurve.Value}, batch -{BatchDiscount.Value:P0} within {BatchMonths.Value} mo, sisters -{SisterMaint.Value:P0} max -{SisterMaintMax.Value:P0}, reset {ResetMonths.Value} mo, yard <{YardLow.Value:P0} x{YardLowCost.Value}/x{YardLowTime.Value}, >{YardHigh.Value:P0} x{YardHighCost.Value}/x{YardHighTime.Value}); "
                + $"inflation economy={EconomyOn.Value} (k loan {KLoan.Value}, print {KPrint.Value}, cut {KCut.Value}, decay {Decay.Value:P0}/mo, loan = inflation + spread {SpreadMin.Value}-{SpreadMax.Value} pts by rating (debt/GDP, bankruptcy, deficit, war; +{HighInflSpread.Value}/pt above {HighInflFrom.Value}%), debt eroded by inflation {BankruptYears.Value} y, savings inflation -{SavingsMult.Value} pts, baseline {InflBaseline.Value}%, research {ResearchGrowth.Value}/{ResearchDisinflation.Value} per ln, shipyard {ShipyardGrowth.Value} per % GDP, unrest above {UnrestFrom.Value}%)";
        }
    }

    // ------------------------------------------------------------------------------------------------ series
    internal static class Series
    {
        internal struct Info { public bool Learn; public int N; public int Dock; public bool Batch; public int Sisters; public float Cost; public float Time; public float Maint; public float Occupancy; }
        private static readonly Dictionary<IntPtr, Info> cache = new Dictionary<IntPtr, Info>();
        private static readonly Dictionary<IntPtr, float> occ = new Dictionary<IntPtr, float>();
        private static readonly Dictionary<string, string> logged = new Dictionary<string, string>();
        private static float cacheTime = -100f; private static string cacheTurn;
        [ThreadStatic] internal static int Depth;

        internal static bool On { get { return EcoPrefs.SeriesOn != null && EcoPrefs.SeriesOn.Value; } }

        static int MonthOf(GameDate g) { try { var d = g.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }
        static int Now() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }

        static void Expire()
        {
            float t = UnityEngine.Time.realtimeSinceStartup;
            string turn = null; try { var ccx = CampaignController.Instance; if (ccx != null) turn = ccx.CurrentDate.ToString(); } catch { }
            if (t - cacheTime > 2f || turn != cacheTurn) { cache.Clear(); occ.Clear(); cacheTime = t; cacheTurn = turn; }
        }

        static float Occupancy(Player p)
        {
            if (p == null) return 0.5f;
            float v; if (occ.TryGetValue(p.Pointer, out v)) return v;
            v = 0.5f;
            try { float cap = p.ShipbuildingCapacityLimit(); float uc = p.ShipTonnageUnderConstruction(); if (cap > 0f) v = uc / cap; } catch { }
            occ[p.Pointer] = v; return v;
        }

        static bool Same(Ship a, Ship b) { return a != null && b != null && a.Pointer == b.Pointer; }

        // 1.11.30: the laying-down month and whether a ship of the design was already completed at that time are recorded
        // when the order is placed (the game's own ship dates are not the laying-down date); saved with the economy state
        internal struct Laid { public int Month; public bool Proven; }
        internal static readonly Dictionary<string, Laid> laidDown = new Dictionary<string, Laid>();
        static readonly HashSet<string> diagDesigns = new HashSet<string>();
        // laying-down month of a ship under construction: own record, otherwise estimated from the building progress
        // 1.11.34: no game calls on other ships while the ship designer / refit screen is open (parts left on the world map)
        internal static bool InDesigner() { try { return GameManager.IsConstructor || GameManager.IsSharedDesignConstructor; } catch { return false; } }
        static int LaidMonth(Ship o, int now, int fallback, bool noGameCalls)
        {
            try
            {
                Laid r; if (laidDown.TryGetValue(o.id.ToString(), out r)) return r.Month;
                if (noGameCalls) return fallback;
                float pr = o.buildingProgress; float f = pr > 1.001f ? pr / 100f : pr;
                if (f <= 0.0001f) return now;
                int bm = 0; Depth++; try { bm = o.BuildingTime(false); } catch { } finally { Depth--; }
                if (bm > 0) return now - (int)Math.Round(f * bm);
            }
            catch { }
            return fallback;
        }
        internal static string SerializeLaid(string id, Laid l) { return "S|" + id + "|" + l.Month + "|" + (l.Proven ? 1 : 0); }
        internal static bool ParseLaid(string line)
        {
            if (line == null || !line.StartsWith("S|")) return false;
            var a = line.Split('|'); int m;
            if (a.Length >= 4 && int.TryParse(a[2], out m)) laidDown[a[1]] = new Laid { Month = m, Proven = a[3] == "1" };
            return true;
        }

        internal static Info Get(Ship s)
        {
            Expire();
            Info inf;
            if (cache.TryGetValue(s.Pointer, out inf)) return inf;
            inf = Compute(s);
            cache[s.Pointer] = inf;
            return inf;
        }

        static Info Compute(Ship s)
        {
            var inf = new Info { N = 1, Cost = 1f, Time = 1f, Maint = 1f, Occupancy = 0.5f };
            try
            {
                Player p = null; try { p = s.player; } catch { }
                if (p == null) return inf;
                Ship design = null; try { design = s.design; } catch { }
                bool preview = design == null || Same(design, s);
                if (design == null) design = s;
                bool refit = false; try { refit = !preview && s.isRefit; } catch { }
                bool building = false; try { building = !preview && s.isBuilding; } catch { }
                int now = Now();
                int start = preview ? now : MonthOf(s.dateCreated);
                float progress = 0f; try { progress = refit ? s.refitProgress : (building ? s.buildingProgress : 1f); } catch { }

                var earlierStarts = new List<int>();
                int earlierRefits = 0, sisters = 0, together = 1; bool batch = false, proven = false, completedNow = false;
                string sid = null; try { sid = building && !refit ? s.id.ToString() : null; } catch { }
                Laid rec = default(Laid); bool haveRec = sid != null && laidDown.TryGetValue(sid, out rec);
                if (haveRec) start = rec.Month;
                else if (building && !refit)
                {
                    // ship without a record (laid down before 1.11.30): estimate the laying-down month from the building progress
                    float f = progress > 1.001f ? progress / 100f : progress;
                    int baseMonths = 0; if (!InDesigner()) { Depth++; try { baseMonths = s.BuildingTime(false); } catch { } finally { Depth--; } }
                    if (f > 0.0001f && baseMonths > 0) start = now - (int)Math.Round(f * baseMonths);
                }
                var finishes = new List<int>();
                var en = p.fleetAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (un.MoveNext())
                {
                    var o = en.Current;
                    if (o == null || Same(o, s)) continue;
                    Ship od = null; try { od = o.design; } catch { }
                    if (!Same(od, design)) continue;
                    bool oBuild = false, oRefit = false, gone = false;
                    try { oBuild = o.isBuilding; oRefit = o.isRefit; gone = o.isSunk || o.isScrapped; } catch { }
                    int oStart = MonthOf(o.dateCreated);
                    // 1.11.50: a ship scrapped or sunk before completion is not part of the series
                    if (gone) { int gf = MonthOf(o.dateFinished); if (gf <= 0 || gf > now || gf < oStart) continue; }
                    // 1.11.35: dateCreated is the design date; for a completed sister its completion month marks the last activity of the series
                    if (!oBuild && !oRefit) { int fin = MonthOf(o.dateFinished); if (fin > oStart && fin <= now) oStart = preview ? fin : Math.Min(fin, start); }
                    if (oBuild) oStart = LaidMonth(o, now, oStart, preview || InDesigner());
                    if (!oBuild && !oRefit && !gone) sisters++;
                    // series effects start only once a ship of the design has been completed (before this one was laid down)
                    if (!oBuild && !oRefit)
                    {
                        completedNow = true;
                        int fin = MonthOf(o.dateFinished); finishes.Add(fin);
                        if (preview || refit) proven = true; else if (fin <= start) proven = true;
                    }
                    if (refit)
                    {
                        if (oRefit) { float op = 0f; try { op = o.refitProgress; } catch { } if (op > progress || (op == progress && o.Pointer.ToInt64() < s.Pointer.ToInt64())) earlierRefits++; batch = true; }
                        else if (!oBuild) earlierRefits++;
                        continue;
                    }
                    if (preview) { earlierStarts.Add(oStart); if (oBuild && now - oStart <= EcoPrefs.BatchMonths.Value) { batch = true; together++; } continue; }
                    bool earlier = !oBuild ? oStart <= start : (oStart < start || (oStart == start && o.Pointer.ToInt64() < s.Pointer.ToInt64()));
                    if (earlier) earlierStarts.Add(oStart);
                    if (building && oBuild && Math.Abs(oStart - start) <= EcoPrefs.BatchMonths.Value) { batch = true; together++; }
                }
                if (sid != null)
                {
                    if (haveRec) proven = rec.Proven;
                    else if (progress <= 0.0001f)
                    {
                        // order just placed: remember the month and whether the prototype is already in service
                        laidDown[sid] = new Laid { Month = now, Proven = completedNow };
                        start = now; proven = completedNow;
                    }
                    else if (completedNow && !proven && diagDesigns.Add(design.name + "|" + p.data?.nameUi))
                        MelonLogger.Msg($"[DIP GG Fixes][Series] {p.data?.nameUi}: {design.name} - date check: ship {s.name} created {MonthOf(s.dateCreated) / 12}/{MonthOf(s.dateCreated) % 12}, estimated laid down {start / 12}/{start % 12}, progress {progress:0.###}, sisters finished {string.Join(",", finishes.Select(x => (x / 12) + "/" + (x % 12)))}, now {now / 12}/{now % 12}");
                }
                int n;
                if (refit) n = 1 + earlierRefits;
                else
                {
                    earlierStarts.Sort(); earlierStarts.Reverse();
                    int chain = 0, prev = start;
                    foreach (var m in earlierStarts) { if (prev - m > EcoPrefs.ResetMonths.Value) break; chain++; prev = m; }
                    n = 1 + chain;
                }
                inf.N = n; inf.Sisters = sisters;
                bool learn = n > 1 && proven; inf.Learn = learn;      // sisters laid down before the prototype was finished are built at base cost and time
                if (!learn) batch = false;
                inf.Batch = batch;
                inf.Cost = n == 1 ? EcoPrefs.ProtoCost.Value : (learn ? Math.Max(EcoPrefs.CostFloor.Value, (float)Math.Pow(n, Math.Log(EcoPrefs.CostCurve.Value, 2))) : 1f);
                inf.Time = n == 1 ? EcoPrefs.ProtoTime.Value : (learn ? (float)Math.Pow(n, Math.Log(EcoPrefs.TimeCurve.Value, 2)) : 1f);
                // the prototype (first ship of the series) is built and paid for as a one-off; learning, batch discount and dock bonus
                // apply only to ships laid down after a ship of the design was completed
                if (batch && learn) inf.Cost *= 1f - EcoPrefs.BatchDiscount.Value;
                // ships of one design built together in one dock (largest dock = Player.shipyard, the maximum ship size)
                if (!refit && together >= 2 && learn)
                {
                    float dock = 0f, ton = 0f; try { dock = p.shipyard; ton = s.Tonnage(); } catch { }
                    int fit = ton > 0f && dock > 0f ? (int)Math.Floor(dock / ton) : 1;
                    int m = Math.Max(1, Math.Min(together, fit));
                    inf.Dock = m;
                    if (m >= 2) inf.Time *= (float)Math.Pow(m, Math.Log(EcoPrefs.DockCurve.Value, 2));
                }
                float u = Occupancy(p); inf.Occupancy = u;
                if (u < EcoPrefs.YardLow.Value) { inf.Cost *= EcoPrefs.YardLowCost.Value; inf.Time *= EcoPrefs.YardLowTime.Value; }
                else if (u > EcoPrefs.YardHigh.Value) { inf.Cost *= EcoPrefs.YardHighCost.Value; inf.Time *= EcoPrefs.YardHighTime.Value; }
                inf.Maint = 1f - Math.Min(EcoPrefs.SisterMaintMax.Value, EcoPrefs.SisterMaint.Value * sisters);

                if ((building || refit) && !preview)
                {
                    string id = null; try { id = s.id.ToString() + (refit ? "r" : "b"); } catch { }
                    string sig = n + "|" + batch + "|" + inf.Dock + "|" + (u < EcoPrefs.YardLow.Value ? 0 : u > EcoPrefs.YardHigh.Value ? 2 : 1); string prevSig;
                    // 1.11.50: logged again when the series number, batch, dock or occupancy band changes (orders placed in one session are counted at turn end)
                    if (id != null && (!logged.TryGetValue(id, out prevSig) || prevSig != sig) && (logged[id] = sig) != null)
                        MelonLogger.Msg($"[DIP GG Fixes][Series] {p.data?.nameUi}: {s.name} ({design.name}) {(refit ? "refit" : "construction")} #{n} of the series{(n > 1 && !learn ? " (prototype not finished yet - base cost and time)" : "")}{(batch ? ", batch" : "")}{(inf.Dock >= 2 ? $", {inf.Dock} in one dock" : "")}, shipyard occupancy {u:P0} - cost x{inf.Cost:F2}, time x{inf.Time:F2}; {sisters} active sisters, maintenance x{inf.Maint:F2}");
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] compute failed: " + e.Message); }
            return inf;
        }
    }

    [HarmonyPatch(typeof(Ship), "BuildingTime", new Type[] { typeof(bool) })]
    internal static class Patch_BuildingTime
    {
        private static void Postfix(Ship __instance, ref int __result)
        {
            try
            {
                if (!Series.On || Series.Depth > 0 || __instance == null || __result <= 0) return;
                var inf = Series.Get(__instance);
                int t = (int)Math.Round(__result * inf.Time);
                int min = (int)Math.Min(__result, Math.Ceiling(EcoPrefs.MinMonths.Value));
                __result = Math.Max(min, t);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] BuildingTime: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Ship), "BuildingCostPerMonth")]
    internal static class Patch_BuildingCostPerMonth
    {
        // vanilla: Cost x k x TimePenalty / BuildingTime; BuildingTime is already shortened, so the total equals Cost x cost factor
        private static void Postfix(Ship __instance, ref float __result)
        {
            try
            {
                if (!Series.On || Series.Depth > 0 || __instance == null || __result <= 0f) return;
                __result *= Series.Get(__instance).Cost;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] BuildingCostPerMonth: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Ship), "RefitTime", new Type[] { typeof(bool), typeof(bool) })]
    internal static class Patch_RefitTime
    {
        private static void Prefix() { Series.Depth++; }
        private static void Postfix(Ship __instance, ref float __result)
        {
            try
            {
                if (!Series.On || Series.Depth > 1 || __instance == null || __result <= 0f) return;
                __result = Math.Max(Math.Min(__result, 1f), __result * Series.Get(__instance).Time);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] RefitTime: " + e.Message); }
        }
        private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; }
    }

    [HarmonyPatch(typeof(Ship), "RefitCostPerMonth")]
    internal static class Patch_RefitCostPerMonth
    {
        // vanilla: Cost x k x TimePenalty / BuildingTime (not RefitTime) -> total refit = monthly x RefitTime;
        // divide by the time factor so that the total refit cost changes by the cost factor only
        private static void Prefix() { Series.Depth++; }
        private static void Postfix(Ship __instance, ref float __result)
        {
            try
            {
                if (!Series.On || Series.Depth > 1 || __instance == null || __result <= 0f) return;
                var inf = Series.Get(__instance);
                if (inf.Time > 0f) __result *= inf.Cost / inf.Time;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] RefitCostPerMonth: " + e.Message); }
        }
        private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; }
    }

    // keep the construction factors out of methods that call BuildingTime internally
    [HarmonyPatch(typeof(Ship), "RepairingTime")]
    internal static class Patch_RepairingTime_Depth { private static void Prefix() { Series.Depth++; } private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; } }
    [HarmonyPatch(typeof(Ship), "RepairingCostPerMonth")]
    internal static class Patch_RepairingCost_Depth { private static void Prefix() { Series.Depth++; } private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; } }
    [HarmonyPatch(typeof(Ship), "CommissioningTime")]
    internal static class Patch_CommissioningTime_Depth { private static void Prefix() { Series.Depth++; } private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; } }
    [HarmonyPatch(typeof(Ship), "CommissioningCostPerMonth")]
    internal static class Patch_CommissioningCost_Depth { private static void Prefix() { Series.Depth++; } private static Exception Finalizer(Exception __exception) { Series.Depth--; return __exception; } }

    [HarmonyPatch(typeof(Ship), "MaintenanceCost")]
    internal static class Patch_MaintenanceCost
    {
        private static void Postfix(Ship __instance, ref float __result)
        {
            try
            {
                if (!Series.On || __instance == null || __result <= 0f) return;
                bool b = false; try { b = __instance.isBuilding; } catch { }
                if (b) return;
                __result *= Series.Get(__instance).Maint;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Series] MaintenanceCost: " + e.Message); }
        }
    }

    // ------------------------------------------------------------------------------------------------ economy
    internal class EconState
    {
        public float Inflation; public int LoanMonths; public float LoanBalance; public int CutsMonths;
        public int BankruptUntil; public int ExpandMonths; public float ExpandPp; public float ResearchPp;
        public float PrevCash, PrevIncome, PrevExpenses; public bool HavePrev; public int LastMonth; public int LastLog;
        public bool Active { get { return LoanMonths > 0 || CutsMonths > 0; } }
        // transient, for the in-game panel and the monthly report (not saved)
        public float Spread = -1f; public float MonthStartInflation; public readonly List<string> Notes = new List<string>(); public readonly List<string> Events = new List<string>();

        internal string Serialize(string key)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|", new string[] { key.Replace("|", "/"), Inflation.ToString("R", c), LoanMonths.ToString(c), LoanBalance.ToString("R", c), CutsMonths.ToString(c),
                BankruptUntil.ToString(c), ExpandMonths.ToString(c), ExpandPp.ToString("R", c), PrevCash.ToString("R", c), PrevIncome.ToString("R", c), PrevExpenses.ToString("R", c),
                HavePrev ? "1" : "0", LastMonth.ToString(c) });
        }
        internal static bool Parse(string line, out string key, out EconState st)
        {
            key = null; st = null;
            var a = line.Split('|'); if (a.Length < 13) return false;
            var c = CultureInfo.InvariantCulture;
            try
            {
                key = a[0];
                st = new EconState
                {
                    Inflation = float.Parse(a[1], c), LoanMonths = int.Parse(a[2], c), LoanBalance = float.Parse(a[3], c), CutsMonths = int.Parse(a[4], c),
                    BankruptUntil = int.Parse(a[5], c), ExpandMonths = int.Parse(a[6], c), ExpandPp = float.Parse(a[7], c), PrevCash = float.Parse(a[8], c),
                    PrevIncome = float.Parse(a[9], c), PrevExpenses = float.Parse(a[10], c), HavePrev = a[11] == "1", LastMonth = int.Parse(a[12], c)
                };
                return true;
            }
            catch { return false; }
        }
    }

    internal static class Economy
    {
        private static readonly Dictionary<string, EconState> states = new Dictionary<string, EconState>();
        private static IntPtr session = IntPtr.Zero; private static bool loaded; private static float lastTry = -100f; private static int tries;
        [ThreadStatic] internal static bool Raw;

        internal static bool On { get { return EcoPrefs.EconomyOn != null && EcoPrefs.EconomyOn.Value; } }
        internal static string Key(Player p) { try { return p.data != null ? p.data.name : p.Pointer.ToString(); } catch { return "?"; } }
        static string NameUi(Player p) { try { return p.data != null ? p.data.nameUi : Key(p); } catch { return "?"; } }
        internal static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }
        static string Dir() { return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "IronAdmiral_Economy"); }
        internal static float Gdp(Player p) { try { return p.StateBudget(); } catch { return 0f; } }

        static List<Player> Majors()
        {
            var l = new List<Player>();
            try { var m = CampaignController.Instance.CampaignData.PlayersMajor; for (int i = 0; i < m.Count; i++) if (m[i] != null) l.Add(m[i]); } catch { }
            return l;
        }

        static string Fingerprint()
        {
            var sb = new StringBuilder();
            try { sb.Append(CampaignController.Instance.CurrentDate.ToString()); } catch { }
            foreach (var p in Majors().OrderBy(x => Key(x), StringComparer.Ordinal))
            {
                int bits = 0; try { bits = BitConverter.SingleToInt32Bits(p.cash); } catch { }
                sb.Append('|').Append(Key(p)).Append(':').Append(bits);
            }
            ulong h = 1469598103934665603UL;
            foreach (char ch in sb.ToString()) { h ^= ch; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        static void EnsureSession()
        {
            var cc = CampaignController.Instance;
            if (cc == null || cc.CampaignData == null) return;
            IntPtr ptr = cc.CampaignData.Pointer;
            if (ptr != session) { session = ptr; states.Clear(); Series.laidDown.Clear(); loaded = false; tries = 0; lastTry = -100f; }
            if (loaded) return;
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (t - lastTry < 1f) return;
            lastTry = t; tries++;
            if (TryLoad()) { loaded = true; return; }
            if (tries >= 30) { loaded = true; MelonLogger.Msg("[DIP GG Fixes][Economy] no saved economy state for this campaign - starting from the 1.11.21 treasury fields"); }
        }

        static bool TryLoad()
        {
            try
            {
                string f = Path.Combine(Dir(), "eco_" + Fingerprint() + ".txt");
                if (!File.Exists(f)) return false;
                int n = 0;
                foreach (var line in File.ReadAllLines(f))
                {
                    string k; EconState st;
                    if (Series.ParseLaid(line)) continue;
                    if (EconState.Parse(line, out k, out st)) { states[k] = st; n++; }
                }
                MelonLogger.Msg($"[DIP GG Fixes][Economy] loaded the economy state of {n} great powers ({Path.GetFileName(f)})");
                foreach (var p in Majors()) { try { if (p.inflation >= 0.5f) p.inflation = 0f; } catch { } }
                return true;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] load failed: " + e.Message); return false; }
        }

        internal static void Save()
        {
            if (!On) return;
            try
            {
                if (CampaignController.Instance == null || CampaignController.Instance.CampaignData == null) return;
                EnsureSession(); loaded = true;
                var dir = Dir(); Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var p in Majors()) { var st = Get(p); if (st != null) lines.Add(st.Serialize(Key(p))); }
                int keepFrom = Month() - 120;
                foreach (var kv in Series.laidDown) if (kv.Value.Month >= keepFrom) lines.Add(Series.SerializeLaid(kv.Key, kv.Value));
                string f = Path.Combine(dir, "eco_" + Fingerprint() + ".txt");
                File.WriteAllLines(f, lines);
                var files = new DirectoryInfo(dir).GetFiles("eco_*.txt").OrderByDescending(x => x.LastWriteTimeUtc).ToList();
                for (int i = 600; i < files.Count; i++) { try { files[i].Delete(); } catch { } }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] save failed: " + e.Message); }
        }

        internal static EconState Get(Player p)
        {
            if (p == null) return null;
            EnsureSession();
            string k = Key(p);
            EconState st;
            if (states.TryGetValue(k, out st)) return st;
            st = new EconState();
            try
            {
                var old = FinanceCrisis.Decode(p.inflation);
                if (old.Active)
                {
                    st.Inflation = old.Inflation; st.LoanMonths = old.Loan; st.CutsMonths = old.Cuts;
                    float b = 0f; try { b = p.Budget(); } catch { }
                    if (old.Loan > 0 && b > 0f) st.LoanBalance = b * DipGgFixesMod.LoanRepayShare.Value * old.Loan;
                    st.BankruptUntil = Month() + EcoPrefs.BankruptYears.Value * 12;
                    MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: 1.11.21 treasury state taken over - inflation {st.Inflation}%, loan {st.LoanMonths} months ({st.LoanBalance / 1e6f:F0}M left), cuts {st.CutsMonths} months");
                }
            }
            catch { }
            states[k] = st;
            return st;
        }

        // credit rating -> spread over inflation (1.11.27): debt relative to GDP, bankruptcy, deficit, war; extra premium at high inflation
        internal static float ComputeSpread(Player p, EconState st)
        {
            float y = Gdp(p);
            float debt = Math.Max(0f, -p.cash) + st.LoanBalance;
            float ratio = y > 0f ? debt / y : 1f;
            float lo = EcoPrefs.SpreadMin.Value, hi = EcoPrefs.SpreadMax.Value;
            float sp = lo + (hi - lo) * Math.Min(1f, Math.Max(0f, ratio / Math.Max(0.01f, EcoPrefs.SpreadDebtGdp.Value)));
            bool prevRaw = Raw; Raw = true;
            try { if (p.Income() < 0f) sp += EcoPrefs.SpreadDeficit.Value; } catch { } finally { Raw = prevRaw; }
            try { if (p.IsAtWar()) sp += EcoPrefs.SpreadWar.Value; } catch { }
            sp = Math.Min(hi, sp);
            if (st.BankruptUntil > Month()) sp = hi;
            return sp;
        }
        static float SpreadOf(Player p, EconState st) { if (st.Spread < 0f) st.Spread = ComputeSpread(p, st); return st.Spread; }
        internal static float TotalSpread(Player p, EconState st)
        {
            return SpreadOf(p, st) + EcoPrefs.HighInflSpread.Value * Math.Max(0f, st.Inflation - EcoPrefs.HighInflFrom.Value);
        }
        internal static float LoanRate(Player p, EconState st)
        {
            return Math.Max(EcoPrefs.RateFloor.Value, st.Inflation + TotalSpread(p, st));
        }
        internal static string Rating(Player p, EconState st)
        {
            if (st.BankruptUntil > Month()) return "CCC";
            float sp = SpreadOf(p, st), lo = EcoPrefs.SpreadMin.Value, hi = EcoPrefs.SpreadMax.Value;
            float f = hi > lo ? (sp - lo) / (hi - lo) : 0f;
            string[] r = { "AAA", "AA", "A", "BBB", "BB", "B", "CCC" };
            return r[Math.Max(0, Math.Min(6, (int)Math.Round(f * 6)))];
        }

        internal struct Breakdown { public float Raw, Surcharge, DebtInterest, LoanPayment, Savings, Cuts, Rate, SavingsRate, Debt, Erosion; public float Net { get { return Surcharge + DebtInterest + LoanPayment - Savings - Cuts - Erosion; } } }

        internal static Breakdown Parts(Player p, EconState st, float raw)
        {
            var b = new Breakdown { Raw = raw };
            float i = Math.Max(EcoPrefs.InflMin.Value, Math.Min(EcoPrefs.InflMax.Value, st.Inflation));
            b.Surcharge = raw * i / 100f;
            float cash = p.cash;
            b.Debt = Math.Max(0f, -cash) + st.LoanBalance;
            b.Rate = LoanRate(p, st);
            if (b.Debt > 0f) b.DebtInterest = b.Debt * b.Rate / 1200f;
            // inflation eats into the real value of debt: the overdraft shrinks by the inflation rate (the loan balance is eroded in MonthStart)
            if (cash < 0f) b.Erosion = -cash * i / 1200f;
            if (st.LoanMonths > 0 && st.LoanBalance > 0f) b.LoanPayment = st.LoanBalance / st.LoanMonths;
            b.SavingsRate = Math.Max(0f, i - EcoPrefs.SavingsMult.Value);
            if (cash > 0f && b.SavingsRate > 0f) b.Savings = cash * b.SavingsRate / 1200f;
            if (st.CutsMonths > 0) { float m = p.ExpensesShipMaintenance(); if (m > 0f) b.Cuts = m * DipGgFixesMod.CutsMaintenanceSaving.Value; }
            return b;
        }

        // Player.Expenses() postfix: prices, interest, repayment, savings, cuts
        internal static void AdjustExpenses(Player p, ref float result)
        {
            if (!On || Raw || p == null) return;
            bool major = false; try { major = p.isMajor; } catch { }
            if (!major) return;
            Raw = true;
            try
            {
                var st = Get(p); if (st == null) return;
                result += Parts(p, st, result).Net;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] Expenses adjust failed: " + e.Message); }
            finally { Raw = false; }
        }

        internal static Breakdown PartsFor(Player p)
        {
            var st = Get(p); if (st == null) return new Breakdown();
            bool was = Raw; Raw = true;
            try { return Parts(p, st, p.Expenses()); } catch { return new Breakdown(); } finally { Raw = was; }
        }

        internal static float RawExpenses(Player p)
        {
            bool was = Raw; Raw = true;
            try { return p.Expenses(); } catch { return 0f; } finally { Raw = was; }
        }

        internal static void AddUnrest(Player p, float v, string why)
        {
            if (v <= 0.01f) return;
            try { p.AddUnrest(v); MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: unrest +{v:F1} ({why})"); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] AddUnrest: " + e.Message); }
        }

        // extraordinary inflow: k = KLoan (loans, grants, reparations, loot, sales) or KPrint (printed money)
        internal static void Inflow(Player p, float amount, bool printed, string what)
        {
            if (!On || p == null || amount <= 0f) return;
            var st = Get(p); float y = Gdp(p); if (st == null || y <= 0f) return;
            float k = printed ? EcoPrefs.KPrint.Value : EcoPrefs.KLoan.Value;
            float d = k * amount / y * 100f;
            float before = st.Inflation;
            st.Inflation = Math.Min(EcoPrefs.InflMax.Value, st.Inflation + d);
            MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: {what} {amount / 1e6f:F0}M = {amount / y:P1} of GDP -> inflation {before:F1}% -> {st.Inflation:F1}%");
            st.Notes.Add($"{what}: {amount / 1e6f:F0}M ({amount / y:P1} of GDP) +{d:F1}");
            if (d > EcoPrefs.UnrestJump.Value) st.Events.Add($"Inflation jumped by {d:F1} points ({what}).");
            if (d > EcoPrefs.UnrestJump.Value) AddUnrest(p, EcoPrefs.UnrestJumpFactor.Value * d, $"inflation jump +{d:F1} points");
        }

        internal static void MarkBankrupt(Player p)
        {
            var st = Get(p); if (st == null) return;
            st.BankruptUntil = Month() + EcoPrefs.BankruptYears.Value * 12;
        }

        // start of the turn check (before balance-of-power grants and the treasury crisis)
        internal static void MonthStart()
        {
            if (!On) return;
            try
            {
                if (CampaignController.Instance == null || CampaignController.Instance.CampaignData == null) return;
                EnsureSession(); loaded = true;
                int month = Month();
                foreach (var p in Majors())
                {
                    try
                    {
                        if (p.isDisabled) continue;
                        var st = Get(p); if (st == null || st.LastMonth == month) continue;
                        st.LastMonth = month;
                        float y = Gdp(p); if (y <= 0f) continue;
                        float before = st.Inflation;
                        st.MonthStartInflation = before; st.Notes.Clear(); st.Events.Clear();
                        { float bl = EcoPrefs.InflBaseline.Value; st.Inflation = bl + (st.Inflation - bl) * (1f - EcoPrefs.Decay.Value); }
                        if (Math.Abs(before) >= 0.05f) st.Notes.Add($"natural fading {st.Inflation - before:+0.00;-0.00}");
                        string notes = "";
                        if (st.HavePrev)
                        {
                            float b = 0f; try { b = p.Budget(); } catch { }
                            float unexplained = p.cash - st.PrevCash - st.PrevIncome;
                            float thr = Math.Max(Math.Abs(b) * EcoPrefs.InflowThresholdMonths.Value, y * EcoPrefs.InflowThresholdGdp.Value);
                            if (unexplained > thr) Inflow(p, unexplained, false, "extraordinary inflow (reparations, loot, sales or other one-off income)");
                            // 1.11.25: symmetric - a spending cut lowers inflation, a spending increase raises it (both relative to GDP)
                            float cut = st.PrevExpenses - RawExpenses(p);
                            if (cut != 0f)
                            {
                                float dd = EcoPrefs.KCut.Value * cut / y * 100f; st.Inflation -= dd;   // the naval budget is a small part of the state: a one-month change counts against annual GDP, not annualised
                                if (Math.Abs(dd) >= 0.05f) { string t = cut > 0f ? $"spending cut {cut / 1e6f:F0}M/month -{dd:F2}" : $"spending up {-cut / 1e6f:F0}M/month +{-dd:F2}"; notes += ", " + t; st.Notes.Add(t); }
                            }
                        }
                        float tech = 0f; try { tech = p.ExpensesTechBudget(); } catch { }
                        float lr = (float)Math.Log(1.0 + Math.Max(0f, 12f * tech / y) / EcoPrefs.ResearchR0.Value);
                        st.ResearchPp = EcoPrefs.ResearchGrowth.Value * lr;
                        float rd = Math.Min(EcoPrefs.ResearchDisinflation.Value * lr, Math.Max(0f, st.Inflation - EcoPrefs.InflBaseline.Value)); st.Inflation -= rd;
                        if (rd >= 0.01f) st.Notes.Add($"research -{rd:F2}");
                        float sy = 0f; try { sy = p.shipyardBuildMonthLeft > 0 ? p.ExpensesShipyardBudget() : 0f; } catch { }
                        if (sy > 0f) { st.ExpandPp = EcoPrefs.ShipyardGrowth.Value * (12f * sy / y * 100f); st.ExpandMonths = (int)EcoPrefs.ShipyardAfterMonths.Value; }
                        else if (st.ExpandMonths > 0) { st.ExpandMonths--; if (st.ExpandMonths == 0) st.ExpandPp = 0f; }
                        else st.ExpandPp = 0f;
                        st.Spread = ComputeSpread(p, st);
                        if (st.LoanBalance > 0f && st.Inflation != 0f) st.LoanBalance = Math.Max(0f, st.LoanBalance * (1f - st.Inflation / 1200f));
                        if (st.LoanMonths > 0)
                        {
                            float pay = st.LoanBalance / st.LoanMonths;
                            st.LoanBalance = Math.Max(0f, st.LoanBalance - pay); st.LoanMonths--;
                            if (st.LoanMonths == 0) { st.LoanBalance = 0f; MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: war loan repaid"); st.Events.Add("The war loan has been repaid."); }
                        }
                        if (st.CutsMonths > 0) { st.CutsMonths--; if (st.CutsMonths == 0) { MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: fleet cuts ended"); st.Events.Add("The fleet cuts have ended."); } }
                        st.Inflation = Math.Max(EcoPrefs.InflMin.Value, Math.Min(EcoPrefs.InflMax.Value, st.Inflation));
                        if (st.Inflation > EcoPrefs.UnrestFrom.Value) AddUnrest(p, EcoPrefs.UnrestPerPoint.Value * (st.Inflation - EcoPrefs.UnrestFrom.Value), $"inflation {st.Inflation:F1}%");
                        bool notable = Math.Abs(st.Inflation - before) >= 0.5f || st.LoanMonths > 0 || Math.Abs(st.Inflation) >= 1f || st.ExpandPp > 0f;
                        if (notable && (Math.Abs(st.Inflation - before) >= 0.5f || month - st.LastLog >= 6))
                        {
                            st.LastLog = month;
                            MelonLogger.Msg($"[DIP GG Fixes][Economy] {NameUi(p)}: inflation {st.Inflation:F1}% ({st.Inflation - before:+0.0;-0.0}{notes}, research -{rd:F2}), GDP {y / 1e6f:F0}M, growth bonus +{st.ResearchPp + st.ExpandPp:F2} pts (research {st.ResearchPp:F2}, shipyard {st.ExpandPp:F2}), cash {p.cash / 1e6f:F0}M, loan {st.LoanBalance / 1e6f:F0}M/{st.LoanMonths} mo at {LoanRate(p, st):F1}% (rating {Rating(p, st)}){(st.BankruptUntil > month ? $" (bankrupt mark until {st.BankruptUntil / 12}/{st.BankruptUntil % 12 + 1})" : "")}, savings rate {Math.Max(0f, st.Inflation - EcoPrefs.SavingsMult.Value):F1}%");
                        }
                    }
                    catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] month start " + Key(p) + ": " + e.Message); }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] month start failed: " + e.Message); }
        }

        // end of the turn check: remember cash, income and raw expenses for next month's comparison
        internal static void MonthEnd()
        {
            if (!On) return;
            try
            {
                if (CampaignController.Instance == null || CampaignController.Instance.CampaignData == null) return;
                foreach (var p in Majors())
                {
                    try
                    {
                        if (p.isDisabled) continue;
                        var st = Get(p); if (st == null) continue;
                        st.PrevCash = p.cash; st.PrevIncome = p.Income(); st.PrevExpenses = RawExpenses(p); st.HavePrev = true;
                        if (p.isMain) { EconomyUi.QueueReport(p, st); WarDiagnostics(p); }
                    }
                    catch { }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] month end failed: " + e.Message); }
            Coalitions();
        }

        // 1.11.36: why the AI does not declare war on the player - attitude, tension, war, power and money per AI great power
        internal static void WarDiagnostics(Player me)
        {
            try
            {
                var cd = CampaignController.Instance.CampaignData; if (cd == null || me == null) return;
                float myPower = 0f, myProj = 0f; try { myPower = me.NationPower(); myProj = me.TotalPowerProjection(); } catch { }
                var sb = new StringBuilder();
                int n = 0;
                foreach (var kv in cd.Relations)
                {
                    var r = kv.Value; if (r == null) continue;
                    Player o = null; try { if (!r.AnyOnePlayerIs(me)) continue; o = r.OppositePlayer(me); } catch { continue; }
                    if (o == null || !o.isMajor || o.isDisabled) continue;
                    bool war = false, tension = false, almost = false, ally = false; float att = 0f;
                    try { war = r.isWar; tension = r.HighTension(); almost = r.AlmostWar(); ally = r.isAlliance; att = r.attitude; } catch { }
                    float p = 0f, pp = 0f; try { p = o.NationPower(); pp = o.TotalPowerProjection(); } catch { }
                    float inc = 0f; try { inc = o.Income(); } catch { }
                    sb.Append($"\n   {NameUi(o)}: attitude {att:F0}, {(war ? "WAR" : ally ? "alliance" : almost ? "almost war" : tension ? "high tension" : "peace")}, nation power x{(myPower > 0 ? p / myPower : 0):F2}, fleet power x{(myProj > 0 ? pp / myProj : 0):F2} of yours, cash {o.cash / 1e6f:F0}M, income {inc / 1e6f:F0}M/month, wars in total {Wars(o)}");
                    n++;
                }
                if (n > 0) MelonLogger.Msg($"[DIP GG Fixes][War] relations of the great powers with {NameUi(me)}:{sb}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][War] diagnostics: " + e.Message); }
        }
        static int Wars(Player p)
        {
            int c = 0;
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.isWar && r.AnyOnePlayerIs(p)) c++; } } catch { }
            return Math.Max(0, c - 0);
        }

        // 1.11.37: coalition wars against a dominant power (player or AI)
        static readonly System.Random rng = new System.Random();
        static Relation RelationOf(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b) && a.Pointer != b.Pointer) return r; } } catch { }
            return null;
        }
        static float Fleet(Player p) { try { return p.TotalPowerProjection(); } catch { return 0f; } }
        internal static void Coalitions()
        {
            if (EcoPrefs.CoalOn == null || !EcoPrefs.CoalOn.Value) return;
            try
            {
                var majors = Majors().Where(x => { try { return !x.isDisabled; } catch { return false; } }).ToList();
                int month = Month();
                foreach (var t in majors)
                {
                    float tf = Fleet(t); if (tf <= 0f) continue;
                    var hostile = new List<Player>(); var atWar = new List<Player>(); float sum = 0f;
                    foreach (var o in majors)
                    {
                        if (o.Pointer == t.Pointer) continue;
                        var r = RelationOf(t, o); if (r == null) continue;
                        bool war = false, almost = false; float att = 0f;
                        try { war = r.isWar; almost = r.AlmostWar(); att = r.attitude; } catch { continue; }
                        if (war) { atWar.Add(o); sum += Fleet(o); }
                        else if (almost && att <= EcoPrefs.CoalAttitude.Value) { hostile.Add(o); sum += Fleet(o); }
                    }
                    if (hostile.Count == 0 || sum < EcoPrefs.CoalRatio.Value * tf) continue;
                    // 1.11.38: only against a dominant power - one that is stronger at sea than every single hostile power
                    float strongest = hostile.Concat(atWar).Max(Fleet);
                    if (tf <= strongest) continue;
                    var cands = hostile.Where(o =>
                    {
                        try
                        {
                            if (o.isMain) return false; // the player decides his own wars
                            var st = Get(o);
                            if (o.cash < 0f || (st != null && st.BankruptUntil > month)) return false;
                            return Wars(o) < (int)EcoPrefs.CoalMaxWars.Value;
                        }
                        catch { return false; }
                    }).OrderByDescending(Fleet).ToList();
                    if (cands.Count == 0) continue;
                    string coal = string.Join(", ", hostile.Concat(atWar).Select(NameUi));
                    if (atWar.Count == 0)
                    {
                        if (rng.NextDouble() < EcoPrefs.CoalStart.Value) DeclareWar(cands[0], t, $"leads the coalition ({coal}: fleet power x{sum / tf:F2} of {NameUi(t)})");
                        else MelonLogger.Msg($"[DIP GG Fixes][War] coalition against {NameUi(t)} ready ({coal}: fleet power x{sum / tf:F2}) - {NameUi(cands[0])} did not declare war this turn");
                    }
                    else
                    {
                        foreach (var c in cands)
                            if (rng.NextDouble() < EcoPrefs.CoalJoin.Value) DeclareWar(c, t, $"joins the war against {NameUi(t)} ({string.Join(", ", atWar.Select(NameUi))} already fighting; coalition fleet power x{sum / tf:F2})");
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][War] coalitions: " + e.Message); }
        }
        static void DeclareWar(Player a, Player t, string why)
        {
            var r = RelationOf(a, t); if (r == null) return;
            float before = 0f; try { before = r.attitude; } catch { }
            string how = "AdjustAttitude";
            try { CampaignController.Instance.AdjustAttitude(r, -101f - before, false, true, "coalition war", false, false, false); } catch (Exception e) { how = "AdjustAttitude failed: " + e.Message; }
            bool war = false; try { war = r.isWar; } catch { }
            if (!war)
            {
                try { r.attitude = -100f; try { r.recentWarStartDate = CampaignController.Instance.CurrentDate; } catch { } how += " + attitude set"; } catch (Exception e) { how += ", set failed: " + e.Message; }
                try { war = r.isWar; } catch { }
            }
            MelonLogger.Msg($"[DIP GG Fixes][War] {NameUi(a)} {why} - attitude {before:F0} -> {SafeAtt(r):F0}, war: {war} ({how})");
            if (war && t.isMain) { try { EconomyUi.Notice("War declared", $"{NameUi(a)} has declared war on you: {why}."); } catch { } }
        }
        static float SafeAtt(Relation r) { try { return r.attitude; } catch { return 0f; } }

        internal static List<Player> MajorsPublic() { return Majors(); }
        internal static string Name(Player p) { return NameUi(p); }

        internal static float GrowthBonusMonthly(Player p)
        {
            if (!On || p == null) return 0f;
            bool major = false; try { major = p.isMajor; } catch { }
            if (!major) return 0f;
            var st = Get(p); if (st == null) return 0f;
            return (st.ResearchPp + st.ExpandPp) / 1200f;
        }
    }

    [HarmonyPatch(typeof(Player), "WealthGrowthEffective")]
    internal static class Patch_WealthGrowthEffective
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            try { __result += Economy.GrowthBonusMonthly(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(GameManager), "SaveInternal", new Type[] { typeof(bool), typeof(bool) })]
    internal static class Patch_SaveInternal
    {
        private static void Postfix() { Economy.Save(); }
    }
}

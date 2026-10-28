// 1.11.77: ship trade.
// (1) Used ships for sale instead of the scrapyard: the player lists a ship in the trade window (F9); states at peace with
//     him send offers over the following months (price from the ship's value, age, the buyer's money and relations); the
//     player accepts one or waits. A sale improves the buyer's attitude and worsens the attitude of the buyer's enemies.
//     AI great powers sell obsolete ships the same way (one at a time, to whoever can pay).
// (2) Orders abroad: the player orders a ship from any great power he is at peace with - one of its designs, or his own
//     design built in its yard. It costs more than at home (premium by relations), is paid month by month to the builder,
//     takes no slip at home, and the builder may requisition it on completion if relations have turned hostile or there
//     is war between them.
// 1.11.84 (review): no instalments to a builder at war with the buyer; AI sales never charge the player without his consent.
// State per campaign: UserData\IronAdmiral_Economy\trade_<fingerprint>.txt (saved with the game). Log: "[Trade]".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Trade
    {
        internal static MelonPreferences_Entry<bool> On, AiSells;
        internal static MelonPreferences_Entry<string> Key;
        internal static MelonPreferences_Entry<float> PremiumMin, PremiumMax;
        static KeyCode key = KeyCode.F9;
        static bool visible; static int tab; static int sellerIdx;
        static readonly System.Random rng = new System.Random();

        class Offer { public string Buyer; public float Price; public int Month; }
        class Listing { public string ShipId; public int Month; public List<Offer> Offers = new List<Offer>(); }
        class Order { public string ShipId, Buyer, Seller, Name; public float Price, Paid; public int Start, Months, Missed; }
        static readonly Dictionary<string, Listing> listings = new Dictionary<string, Listing>();
        static readonly List<Order> orders = new List<Order>();
        // 1.11.338 (#16): a ship sold while at sea is handed over when she is back in port (the price is paid then)
        class Pending { public string ShipId, Seller, Buyer; public float Price; public int Month; }
        static readonly List<Pending> pending = new List<Pending>();
        // 1.11.236 (author's decision 24.10): a navy that bought one of the player's ships comes back for a paid refit when the
        // ship ages - a refit licence (the buyer's own yards do the work, the player sells the drawings) or the work in the
        // player's yards at a premium, paid over the months of the refit. A request waits three months; unanswered, the buyer
        // manages on its own and thinks a little worse of the seller.
        class Sold { public string ShipId, Buyer; public int Month, LastRefit; }
        class RefitReq { public string ShipId, Buyer; public int Month; public float Cost; public int Months; }
        class YardJob { public string ShipId, Buyer, Name; public int Start, Months; public float Total, Paid; }
        static readonly Dictionary<string, Sold> sold = new Dictionary<string, Sold>();
        static readonly List<RefitReq> refits = new List<RefitReq>();
        static readonly List<YardJob> yardJobs = new List<YardJob>();
        internal static MelonPreferences_Entry<bool> Refits;
        internal static MelonPreferences_Entry<float> RefitShare, RefitLicence, RefitPremium;
        internal static MelonPreferences_Entry<int> RefitAfter;
        static IntPtr session = IntPtr.Zero; static bool loaded; static int lastMonth;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ShipTrade", true, null, "Ship trade: used ships for sale instead of scrapping, and ships ordered from foreign shipyards (trade window, key below)");
            AiSells = cat.CreateEntry("ShipTradeAiSells", true, null, "AI great powers sell obsolete ships to other states instead of only scrapping them");
            Key = cat.CreateEntry("ShipTradeKey", "F9", null, "Key that opens / closes the ship trade window (Unity KeyCode name)");
            PremiumMin = cat.CreateEntry("ForeignOrderPremiumMin", 1.25f, null, "Price of a ship ordered abroad compared with the builder's cost, with the best relations");
            PremiumMax = cat.CreateEntry("ForeignOrderPremiumMax", 1.5f, null, "Price of a ship ordered abroad compared with the builder's cost, with cool relations");
            Refits = cat.CreateEntry("ShipTradeRefits", true, null, "Ship trade: navies that bought your ships come back for a paid refit as the ship ages - a refit licence, or the work in your yards at a premium (trade window)");
            RefitShare = cat.CreateEntry("ShipTradeRefitShare", 0.25f, null, "Cost of a refit as a share of the ship's cost");
            RefitLicence = cat.CreateEntry("ShipTradeRefitLicence", 0.2f, null, "Refit licence fee as a share of the refit cost (the buyer's own yards do the work)");
            RefitPremium = cat.CreateEntry("ShipTradeRefitPremium", 1.4f, null, "Price of a refit done in your yards compared with its cost (paid over the months of the refit)");
            RefitAfter = cat.CreateEntry("ShipTradeRefitAfterYears", 6, null, "Years after the sale (or the last refit) before the buyer asks for a refit");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Trade] " + m); }
        static void Notice(string h, string m) { try { EconomyUi.Notice(h, m); } catch { } }

        // ------------------------------------------------------------------ helpers
        static CampaignController CC { get { return CampaignController.Instance; } }
        static IEnumerable<Player> AllPlayers()
        {
            var l = new List<Player>();
            try { var ps = CC.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && !p.isDisabled) l.Add(p); } } catch { }
            return l;
        }
        static Player Main() { foreach (var p in AllPlayers()) { try { if (p.isMain) return p; } catch { } } return null; }
        static string K(Player p) { try { return p.data.name; } catch { return "?"; } }
        static string N(Player p) { try { return p.data.nameUi; } catch { return K(p); } }
        static Player ByKey(string k) { foreach (var p in AllPlayers()) if (K(p) == k) return p; return null; }
        static Relation Rel(Player a, Player b)
        {
            try { foreach (var kv in CC.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return r; } } catch { }
            return null;
        }
        static bool AtWar(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isWar; } catch { return false; } }
        static float Att(Player a, Player b) { var r = Rel(a, b); try { return r != null ? r.attitude : 0f; } catch { return 0f; } }
        static void Adjust(Player a, Player b, float d, string why)
        {
            var r = Rel(a, b); if (r == null || Math.Abs(d) < 0.01f) return;
            try { CC.AdjustAttitude(r, d, false, true, why, false, false, false); } catch { try { r.attitude = Mathf.Clamp(r.attitude + d, -100f, 100f); } catch { } }
        }
        static List<Ship> Fleet(Player p)
        {
            var r = new List<Ship>();
            try { var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var s = en.Current; if (s != null) r.Add(s); } } catch { }
            return r;
        }
        static Ship FindShip(string id)
        {
            foreach (var p in AllPlayers()) foreach (var s in Fleet(p)) { try { if (s.id.ToString() == id) return s; } catch { } }
            return null;
        }
        static bool Sellable(Ship s)
        {
            try
            {
                if (s.isSunk || s.isBuilding) return false;
                string st = s.status.ToString();
                return !(st.Contains("Build") || st.Contains("Refit") || st.Contains("Sunk") || st.Contains("Scrap"));
            }
            catch { return false; }
        }
        static int YearNow() { try { return CC.CurrentDate.AsDate().Year; } catch { return 1900; } }
        static int Age(Ship s) { try { return Math.Max(0, YearNow() - s.dateFinished.AsDate().Year); } catch { try { return Math.Max(0, YearNow() - s.dateCreated.AsDate().Year); } catch { return 10; } } }
        static float Value(Ship s)
        {
            float cost = 0f; try { cost = s.Cost(); } catch { }
            float age = Mathf.Clamp01(1f - 0.045f * Age(s)); age = Mathf.Max(0.15f, age);
            float hp = 1f; try { hp = Mathf.Clamp01(s.HP / 100f); } catch { }
            return cost * age * Mathf.Lerp(0.6f, 1f, hp);
        }
        static string M(float v) { return (v / 1e6f).ToString("0.0", CultureInfo.InvariantCulture) + "M"; }
        static string TypeName(Ship s) { try { return s.shipType.name.ToUpperInvariant(); } catch { return "?"; } }

        // ------------------------------------------------------------------ persistence (same fingerprint as the economy file)
        static string FilePath() { return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "IronAdmiral_Economy", "trade_" + Economy.Fingerprint() + ".txt"); }
        static void EnsureSession()
        {
            var cc = CC; if (cc == null || cc.CampaignData == null) return;
            if (cc.CampaignData.Pointer != session) { session = cc.CampaignData.Pointer; listings.Clear(); orders.Clear(); pending.Clear(); sold.Clear(); refits.Clear(); yardJobs.Clear(); loaded = false; lastMonth = 0; }
            if (loaded) return;
            loaded = true;
            try
            {
                var f = FilePath(); if (!File.Exists(f)) return;
                foreach (var line in File.ReadAllLines(f))
                {
                    var c = line.Split('|');
                    if (c[0] == "sale" && c.Length >= 3)
                    {
                        var l = new Listing { ShipId = c[1], Month = int.Parse(c[2]) };
                        for (int i = 3; i + 2 < c.Length; i += 3) l.Offers.Add(new Offer { Buyer = c[i], Price = float.Parse(c[i + 1], CultureInfo.InvariantCulture), Month = int.Parse(c[i + 2]) });
                        listings[l.ShipId] = l;
                    }
                    else if (c[0] == "order" && c.Length >= 10)
                        orders.Add(new Order { ShipId = c[1], Buyer = c[2], Seller = c[3], Price = float.Parse(c[4], CultureInfo.InvariantCulture), Paid = float.Parse(c[5], CultureInfo.InvariantCulture), Start = int.Parse(c[6]), Months = int.Parse(c[7]), Missed = int.Parse(c[8]), Name = c[9] });
                    else if (c[0] == "pending" && c.Length >= 6)
                        pending.Add(new Pending { ShipId = c[1], Seller = c[2], Buyer = c[3], Price = float.Parse(c[4], CultureInfo.InvariantCulture), Month = int.Parse(c[5]) });
                    else if (c[0] == "sold" && c.Length >= 5)
                        sold[c[1]] = new Sold { ShipId = c[1], Buyer = c[2], Month = int.Parse(c[3]), LastRefit = int.Parse(c[4]) };
                    else if (c[0] == "refit" && c.Length >= 6)
                        refits.Add(new RefitReq { ShipId = c[1], Buyer = c[2], Month = int.Parse(c[3]), Cost = float.Parse(c[4], CultureInfo.InvariantCulture), Months = int.Parse(c[5]) });
                    else if (c[0] == "yard" && c.Length >= 8)
                        yardJobs.Add(new YardJob { ShipId = c[1], Buyer = c[2], Start = int.Parse(c[3]), Months = int.Parse(c[4]), Total = float.Parse(c[5], CultureInfo.InvariantCulture), Paid = float.Parse(c[6], CultureInfo.InvariantCulture), Name = c[7] });
                }
                Log($"loaded {listings.Count} listing(s) and {orders.Count} foreign order(s)");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Trade] load failed: " + e.Message); }
        }
        internal static void Save()
        {
            if (On == null || !On.Value) return;
            try
            {
                if (CC == null || CC.CampaignData == null) return;
                EnsureSession();
                var lines = new List<string>();
                foreach (var l in listings.Values)
                {
                    var sb = new StringBuilder($"sale|{l.ShipId}|{l.Month}");
                    foreach (var o in l.Offers) sb.Append($"|{o.Buyer}|{o.Price.ToString(CultureInfo.InvariantCulture)}|{o.Month}");
                    lines.Add(sb.ToString());
                }
                foreach (var o in orders) lines.Add($"order|{o.ShipId}|{o.Buyer}|{o.Seller}|{o.Price.ToString(CultureInfo.InvariantCulture)}|{o.Paid.ToString(CultureInfo.InvariantCulture)}|{o.Start}|{o.Months}|{o.Missed}|{(o.Name ?? "").Replace('|', '/')}");
                foreach (var pn in pending) lines.Add($"pending|{pn.ShipId}|{pn.Seller}|{pn.Buyer}|{pn.Price.ToString(CultureInfo.InvariantCulture)}|{pn.Month}");
                foreach (var s in sold.Values) lines.Add($"sold|{s.ShipId}|{s.Buyer}|{s.Month}|{s.LastRefit}");
                foreach (var r in refits) lines.Add($"refit|{r.ShipId}|{r.Buyer}|{r.Month}|{r.Cost.ToString(CultureInfo.InvariantCulture)}|{r.Months}");
                foreach (var j in yardJobs) lines.Add($"yard|{j.ShipId}|{j.Buyer}|{j.Start}|{j.Months}|{j.Total.ToString(CultureInfo.InvariantCulture)}|{j.Paid.ToString(CultureInfo.InvariantCulture)}|{(j.Name ?? "").Replace('|', '/')}");
                var f = FilePath(); Directory.CreateDirectory(Path.GetDirectoryName(f));
                if (lines.Count > 0 || File.Exists(f)) File.WriteAllLines(f, lines);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Trade] save failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ monthly work
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                if (Input.GetKeyDown(key)) { visible = !visible; rowsBuilt = -10f; }
                Clicks();
                bool map = false; try { map = GameManager.IsCampaign && !GameManager.IsLoadingAny && CC != null && CC.CampaignData != null; } catch { }
                if (!map) { if (visible && !GameManager.IsCampaign) visible = false; return; }
                EnsureSession();
                int m = Economy.Month(); if (m <= 0) return;
                if (lastMonth == 0) { lastMonth = m; return; }
                if (m == lastMonth) return;
                lastMonth = m;
                Monthly(m);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Trade] tick: " + e.Message); }
        }

        static bool handingOver;
        static void Monthly(int month)
        {
            var main = Main(); if (main == null) return;
            // 1.11.338 (#16): ships sold at sea are handed over once in port; the sale lapses if she was lost, or after a year,
            // or when buyer and seller are at war
            foreach (var pn in pending.ToList())
            {
                var sh = FindShip(pn.ShipId); var se = ByKey(pn.Seller); var bu = ByKey(pn.Buyer);
                bool gone = sh == null || se == null || bu == null; try { gone = gone || sh.isSunk || sh.player == null || sh.player.Pointer != se.Pointer; } catch { gone = true; }
                if (gone || month - pn.Month > 12 || AtWar(se, bu)) { pending.Remove(pn); Log($"sale of {(sh != null ? sh.name : pn.ShipId)} to {pn.Buyer} lapsed ({(gone ? "ship lost or no longer the seller's" : AtWar(se, bu) ? "war between them" : "not back in port within a year")})"); if (se != null && se.isMain && sh != null) Notice("Sale cancelled", $"The sale of {sh.name} to {N(bu)} has lapsed."); continue; }
                bool sea = true; try { sea = sh.IsInSea; } catch { }
                if (sea) continue;
                pending.Remove(pn);
                handingOver = true; try { Sell(se, sh, bu, pn.Price); } finally { handingOver = false; }
            }
            // 1. offers for the player's listed ships
            foreach (var l in listings.Values.ToList())
            {
                var s = FindShip(l.ShipId);
                if (s == null || !Sellable(s) || s.player == null || s.player.Pointer != main.Pointer) { listings.Remove(l.ShipId); continue; }
                l.Offers.RemoveAll(o => month - o.Month > 3 || ByKey(o.Buyer) == null || AtWar(main, ByKey(o.Buyer)));
                if (month - l.Month > 12 && l.Offers.Count == 0) { listings.Remove(l.ShipId); Notice("Ship sale withdrawn", $"Nobody bought {s.name} within a year; it is no longer for sale."); continue; }
                if (l.Offers.Count >= 3 || rng.NextDouble() > 0.6) continue;
                var o2 = MakeOffer(main, s, l.Offers.Select(x => x.Buyer));
                if (o2 != null) { o2.Month = month; l.Offers.Add(o2); Notice("Offer for a ship", $"{N(ByKey(o2.Buyer))} offers {M(o2.Price)} for {s.name}. Open the trade window ({Key.Value}) to accept."); Log($"offer: {o2.Buyer} {M(o2.Price)} for {s.name} ({TypeName(s)}, {Age(s)} years)"); }
            }
            // 2. foreign orders: payments, completion, requisition
            foreach (var o in orders.ToList())
            {
                var buyer = ByKey(o.Buyer); var seller = ByKey(o.Seller); var s = FindShip(o.ShipId);
                if (buyer == null || seller == null || s == null) { orders.Remove(o); continue; }
                bool building = false; try { building = s.isBuilding; } catch { }
                float due = o.Months > 0 ? o.Price / o.Months : o.Price;
                // 1.11.84: no instalments go to a builder the buyer is at war with (the ship will be seized on completion anyway)
                if (building && AtWar(buyer, seller)) continue;
                if (building && o.Paid < o.Price)
                {
                    float pay = Math.Min(due, o.Price - o.Paid);
                    if (buyer.cash >= pay) { buyer.cash -= pay; seller.cash += pay; o.Paid += pay; o.Missed = 0; }
                    else if (++o.Missed >= 3) { orders.Remove(o); Notice("Foreign order cancelled", $"Payments for {o.Name} stopped; {N(seller)} keeps the ship."); Log($"order {o.Name} cancelled - {o.Buyer} could not pay"); continue; }
                }
                if (building) continue;
                // completed
                bool hostile = AtWar(buyer, seller) || Att(buyer, seller) < -60f;
                if (hostile)
                {
                    orders.Remove(o);
                    Notice("Ship requisitioned", $"{N(seller)} has seized {o.Name}, completed in its yard for your navy. The money paid is lost.");
                    Log($"requisition: {o.Seller} keeps {o.Name} ordered by {o.Buyer} (paid {M(o.Paid)})");
                    continue;
                }
                float rest = o.Price - o.Paid;
                if (rest > 0f) { buyer.cash -= rest; seller.cash += rest; o.Paid += rest; }
                var list = new Il2CppSystem.Collections.Generic.List<Ship>();
                try { CC.TransferShipToNewOwner(buyer, s.id, ref list, null, false); PrizeDesign.Ensure(s, buyer, "Trade"); } catch (Exception e) { Log("delivery failed: " + e.Message); continue; }
                orders.Remove(o);
                Adjust(buyer, seller, 5f, "ship delivered");
                if (buyer.isMain) Notice("Ship delivered", $"{o.Name}, built in {N(seller)}, has been handed over to your navy.");
                Log($"delivered: {o.Name} from {o.Seller} to {o.Buyer}, total {M(o.Paid)}");
            }
            // 3. AI great powers sell obsolete ships
            if (AiSells.Value) foreach (var p in AllPlayers())
            {
                if (p.isMain || !p.isMajor || rng.NextDouble() > 0.08) continue;
                var old = Fleet(p).Where(x => Sellable(x) && Age(x) >= (TypeName(x) == "BB" || TypeName(x) == "BC" || TypeName(x) == "CA" ? 20 : 15)).OrderByDescending(x => Age(x)).FirstOrDefault();
                if (old == null) continue;
                var of = MakeOffer(p, old, new string[0]);
                if (of == null) continue;
                Sell(p, old, ByKey(of.Buyer), of.Price);
            }
            // 4. refits for the navies that bought the player's ships
            RefitsMonthly(main, month);
        }

        static void RefitsMonthly(Player main, int month)
        {
            if (Refits == null || !Refits.Value) return;
            int after = Math.Max(1, RefitAfter != null ? RefitAfter.Value : 6) * 12;
            // work in the player's yards: paid month by month, finished after its months
            foreach (var j in yardJobs.ToList())
            {
                var buyer = ByKey(j.Buyer); var s = FindShip(j.ShipId);
                if (buyer == null || s == null || s.player == null || s.player.Pointer != buyer.Pointer) { yardJobs.Remove(j); continue; }
                float due = Math.Min(j.Total / Math.Max(1, j.Months), j.Total - j.Paid);
                if (due > 0f) { if (AtWar(buyer, main)) { yardJobs.Remove(j); Log($"refit of {j.Name} for {j.Buyer} broken off - war"); continue; } buyer.cash -= due; main.cash += due; j.Paid += due; }
                if (month - j.Start < j.Months) continue;
                yardJobs.Remove(j);
                Sold so; if (sold.TryGetValue(j.ShipId, out so)) so.LastRefit = month;
                Adjust(buyer, main, 5f, "refit in your yards");
                Notice("Refit completed", $"{j.Name} ({N(buyer)}) leaves your yards after its refit; {M(j.Paid)} earned in total.");
                Log($"refit in the player's yards finished: {j.Name} for {j.Buyer}, {M(j.Paid)}");
            }
            // pending requests: three months, then the buyer manages alone
            foreach (var r in refits.ToList())
            {
                var buyer = ByKey(r.Buyer); var s = FindShip(r.ShipId);
                if (buyer == null || s == null || s.player == null || s.player.Pointer != buyer.Pointer || AtWar(buyer, main)) { refits.Remove(r); continue; }
                if (month - r.Month < 3) continue;
                refits.Remove(r);
                Sold so; if (sold.TryGetValue(r.ShipId, out so)) so.LastRefit = month;
                Adjust(buyer, main, -2f, "refit request unanswered");
                Notice("Refit request withdrawn", $"{N(buyer)} waited three months for an answer about {s.name} and has the refit done at home.");
                Log($"refit request for {s.name} ({r.Buyer}) expired");
            }
            // new requests
            foreach (var so in sold.Values.ToList())
            {
                var s = FindShip(so.ShipId); var buyer = ByKey(so.Buyer);
                if (s == null || buyer == null || s.player == null || s.player.Pointer != buyer.Pointer || !Sellable(s)) { if (s == null || buyer == null || (s.player != null && s.player.Pointer != buyer.Pointer)) sold.Remove(so.ShipId); continue; }
                if (month - Math.Max(so.Month, so.LastRefit) < after) continue;
                if (refits.Any(x => x.ShipId == so.ShipId) || yardJobs.Any(x => x.ShipId == so.ShipId)) continue;
                if (AtWar(buyer, main) || Att(buyer, main) < -20f || rng.NextDouble() > 0.15) continue;
                float cost = 0f; try { cost = s.Cost() * (RefitShare != null ? RefitShare.Value : 0.25f); } catch { }
                if (cost <= 0f || buyer.cash < cost * 0.5f) continue;
                int months = 6; try { months = Math.Max(3, s.BuildingTime(false) / 3); } catch { }
                refits.Add(new RefitReq { ShipId = so.ShipId, Buyer = so.Buyer, Month = month, Cost = cost, Months = months });
                Notice("Refit requested", $"{N(buyer)} asks for a refit of {s.name}, bought from you: a refit licence ({M(cost * (RefitLicence != null ? RefitLicence.Value : 0.2f))}) or the work in your yards ({M(cost * (RefitPremium != null ? RefitPremium.Value : 1.4f))} over {months} months). Open the trade window ({Key.Value}).");
                Log($"refit request: {so.Buyer} for {s.name} ({TypeName(s)}, {Age(s)} years), refit cost {M(cost)}, {months} months");
            }
        }
        static void RefitLicenceSale(Player main, RefitReq r)
        {
            var buyer = ByKey(r.Buyer); var s = FindShip(r.ShipId); if (buyer == null || s == null) { refits.Remove(r); return; }
            float fee = r.Cost * (RefitLicence != null ? RefitLicence.Value : 0.2f);
            buyer.cash -= fee; main.cash += fee;
            refits.Remove(r);
            Sold so; if (sold.TryGetValue(r.ShipId, out so)) so.LastRefit = Economy.Month();
            Adjust(buyer, main, 3f, "refit licence");
            Notice("Refit licence sold", $"{N(buyer)} pays {M(fee)} for the refit licence of {s.name} and refits the ship in its own yards.");
            Log($"refit licence: {s.name} to {r.Buyer} for {M(fee)}");
            Save();
        }
        static void RefitInYards(Player main, RefitReq r)
        {
            var buyer = ByKey(r.Buyer); var s = FindShip(r.ShipId); if (buyer == null || s == null) { refits.Remove(r); return; }
            float total = r.Cost * (RefitPremium != null ? RefitPremium.Value : 1.4f);
            refits.Remove(r);
            yardJobs.Add(new YardJob { ShipId = r.ShipId, Buyer = r.Buyer, Name = s.name, Start = Economy.Month(), Months = r.Months, Total = total, Paid = 0f });
            Adjust(buyer, main, 2f, "refit ordered in your yards");
            Notice("Refit in your yards", $"{s.name} ({N(buyer)}) enters your yards for {r.Months} months; {M(total)} will be paid month by month.");
            Log($"refit in the player's yards: {s.name} for {r.Buyer}, {M(total)} over {r.Months} months");
            Save();
        }
        static void RefitDecline(Player main, RefitReq r)
        {
            var buyer = ByKey(r.Buyer); refits.Remove(r);
            Sold so; if (sold.TryGetValue(r.ShipId, out so)) so.LastRefit = Economy.Month();
            if (buyer != null) Adjust(buyer, main, -1f, "refit declined");
            Log($"refit request of {r.Buyer} for {r.ShipId} declined");
            Save();
        }

        static Offer MakeOffer(Player seller, Ship s, IEnumerable<string> already)
        {
            float v = Value(s); if (v <= 0f) return null;
            var taken = new HashSet<string>(already);
            var cands = new List<Player>();
            foreach (var b in AllPlayers())
            {
                if (b.Pointer == seller.Pointer || taken.Contains(K(b)) || AtWar(seller, b)) continue;
                // 1.11.84: an AI sale is completed at once, so the player is never chosen as its buyer (he would pay without being asked)
                try { if (!seller.isMain && b.isMain) continue; } catch { }
                if (Att(seller, b) < -50f) continue;
                if (b.cash < v * 0.5f) continue;
                cands.Add(b);
            }
            if (cands.Count == 0) return null;
            var buyer = cands[rng.Next(cands.Count)];
            float rel = Mathf.Lerp(0.85f, 1.1f, (Att(seller, buyer) + 100f) / 200f);
            float price = v * rel * (float)(0.85 + rng.NextDouble() * 0.25);
            price = Math.Min(price, buyer.cash * 0.9f);
            if (price < v * 0.4f) return null;
            return new Offer { Buyer = K(buyer), Price = price };
        }

        static void Sell(Player seller, Ship s, Player buyer, float price)
        {
            if (seller == null || buyer == null || s == null) return;
            string name = s.name, type = TypeName(s);
            bool atSea = false; try { atSea = s.IsInSea; } catch { }
            if (atSea && !handingOver)
            {
                string sid = s.id.ToString();
                if (!pending.Exists(x => x.ShipId == sid)) pending.Add(new Pending { ShipId = sid, Seller = K(seller), Buyer = K(buyer), Price = price, Month = Economy.Month() });
                listings.Remove(sid);
                Log($"sale agreed: {N(seller)} sells {name} ({type}) to {N(buyer)} for {M(price)} - she is at sea and will be handed over in port");
                if (seller.isMain) Notice("Ship sold", $"{name} is sold to {N(buyer)} for {M(price)}. She is at sea and will be handed over when she is back in port; the price is paid then.");
                return;
            }
            var list = new Il2CppSystem.Collections.Generic.List<Ship>();
            try { CC.TransferShipToNewOwner(buyer, s.id, ref list, null, false); PrizeDesign.Ensure(s, buyer, "Trade"); }
            catch (Exception e) { Log("sale failed: " + e.Message); return; }
            buyer.cash -= price; seller.cash += price;
            float weight = Mathf.Clamp(price / Math.Max(1f, Economy.Gdp(buyer)) * 20f, 0.5f, 2f);
            Adjust(buyer, seller, 6f * weight, "ship sale");
            foreach (var e in AllPlayers())
            {
                if (e.Pointer == buyer.Pointer || e.Pointer == seller.Pointer) continue;
                if (AtWar(buyer, e) || Att(buyer, e) <= -50f) Adjust(seller, e, -4f * weight, "sold a ship to its enemy");
            }
            listings.Remove(s.id.ToString());
            if (seller.isMain) sold[s.id.ToString()] = new Sold { ShipId = s.id.ToString(), Buyer = K(buyer), Month = Economy.Month(), LastRefit = Economy.Month() };
            Log($"sale: {N(seller)} sold {name} ({type}) to {N(buyer)} for {M(price)}");
            if (seller.isMain) Notice("Ship sold", $"{name} has been sold to {N(buyer)} for {M(price)}.");
            else if (buyer.isMain) Notice("Ship bought", $"Your navy has bought {name} from {N(seller)}.");
        }

        static void PlaceOrder(Player buyer, Player seller, Ship design, bool own)
        {
            var pc = PlayerController.Instance; if (pc == null || design == null) return;
            Ship d = design;
            if (own) { try { d = pc.CloneDesign(design, seller); } catch (Exception e) { Log("design copy failed: " + e.Message); return; } }
            Il2CppSystem.Collections.Generic.List<Ship> built = null;
            try { built = pc.BuildShipsFromDesign(d, 1, false, seller); } catch (Exception e) { Log("order failed: " + e.Message); }
            if (built == null || built.Count == 0) { Notice("Order not accepted", $"{N(seller)}'s yards cannot build {design.name} now."); return; }
            var s = built[0];
            float cost = 0f; try { cost = s.Cost(); } catch { }
            float prem = Mathf.Lerp(PremiumMax.Value, PremiumMin.Value, Mathf.Clamp01((Att(buyer, seller) + 20f) / 120f));
            int months = 12; try { months = Math.Max(2, s.BuildingTime(false)); } catch { }
            var o = new Order { ShipId = s.id.ToString(), Buyer = K(buyer), Seller = K(seller), Name = s.name, Price = cost * prem, Start = Economy.Month(), Months = months };
            orders.Add(o);
            Adjust(buyer, seller, 3f, "shipbuilding order");
            Notice("Ship ordered abroad", $"{N(seller)} lays down {s.name} for your navy: {M(o.Price)} paid over about {months} months.");
            Log($"order: {o.Buyer} ordered {s.name} ({(own ? "own design" : "builder's design")}) from {o.Seller}, {M(o.Price)} (x{prem:0.00}) over {months} months");
            Save();
        }

        // ------------------------------------------------------------------ window
        // Drawn with GUI.Box / GUI.Label only (GUILayout is stripped from the game build); clicks are read in Tick with
        // Input.GetMouseButtonDown and hit-tested against the rectangles of the last frame (same method as the F10 panel).
        class Row { public string Text; public string Btn; public Action Act; public bool Head; }
        static GUIStyle sTxt, sHead, sBtn, sTitle;
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>();
        static readonly List<KeyValuePair<Rect, Action>> next = new List<KeyValuePair<Rect, Action>>();
        static int page; static List<Row> rows = new List<Row>(); static float rowsBuilt = -10f; static int guiErr;

        internal static void Clicks()
        {
            if (!visible || !Input.GetMouseButtonDown(0)) return;
            var p = UiScale.Mouse();
            foreach (var kv in clicks.ToList()) if (kv.Key.Contains(p)) { try { kv.Value(); } catch (Exception e) { Log("click: " + e.Message); } rowsBuilt = -10f; break; }
        }
        static void Styles()
        {
            if (sTxt != null) return;
            sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
            sHead = new GUIStyle(); sHead.fontSize = 14; sHead.richText = true; sHead.normal.textColor = new Color(1f, 0.85f, 0.35f);
            sTxt = new GUIStyle(); sTxt.fontSize = 13; sTxt.richText = true; sTxt.wordWrap = true; sTxt.normal.textColor = Color.white;
            sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.richText = true; sBtn.normal.textColor = Color.white;
        }
        static void Button(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }

        internal static void OnGUI()
        {
            if (On == null || !On.Value || !visible) return;
            try
            {
                if (!GameManager.IsCampaign || GameManager.IsLoadingAny) return;
                var main = Main(); if (main == null) return;
                Styles();
                next.Clear();
                float w = Math.Min(760f, UiScale.W - 40f), h = Math.Min(640f, UiScale.H - 120f);
                var r = new Rect((UiScale.W - w) / 2f, 80f, w, h);
                GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 120f, 24f), $"<b>Ship trade</b>   ({Key.Value} closes)", sTitle);
                Button(new Rect(r.xMax - 40f, r.y + 8f, 30f, 26f), "X", () => visible = false);
                string[] tabs = { "Sell ships", "Order abroad", $"My orders ({orders.Count(o => o.Buyer == K(main))})", $"Refits ({refits.Count + yardJobs.Count})" };
                float tw = (w - 28f) / 4f;
                for (int i = 0; i < 4; i++) { int ti = i; Button(new Rect(r.x + 14f + i * tw, r.y + 40f, tw - 8f, 26f), (tab == i ? "<color=#FFD95A><b>" : "") + tabs[i] + (tab == i ? "</b></color>" : ""), () => { tab = ti; page = 0; }); }
                if (Time.realtimeSinceStartup - rowsBuilt > 1f) { rows = BuildRows(main); rowsBuilt = Time.realtimeSinceStartup; }
                float y0 = r.y + 76f, rowH = 26f; int per = Math.Max(1, (int)((h - 76f - 44f) / rowH));
                int pages = Math.Max(1, (rows.Count + per - 1) / per); page = Mathf.Clamp(page, 0, pages - 1);
                float y = y0;
                foreach (var row in rows.Skip(page * per).Take(per))
                {
                    if (row.Head) GUI.Label(new Rect(r.x + 14f, y + 3f, w - 28f, rowH), row.Text, sHead);
                    else
                    {
                        GUI.Label(new Rect(r.x + 14f, y + 3f, w - (row.Btn != null ? 200f : 28f), rowH), row.Text, sTxt);
                        if (row.Btn != null) Button(new Rect(r.xMax - 180f, y + 1f, 166f, rowH - 3f), row.Btn, row.Act);
                    }
                    y += rowH;
                }
                float fy = r.yMax - 36f;
                if (pages > 1)
                {
                    Button(new Rect(r.x + 14f, fy, 40f, 26f), "<", () => { if (page > 0) page--; });
                    GUI.Label(new Rect(r.x + 60f, fy + 4f, 120f, 22f), $"page {page + 1}/{pages}", sTxt);
                    Button(new Rect(r.x + 180f, fy, 40f, 26f), ">", () => { page++; });
                }
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (guiErr++ < 5) MelonLogger.Warning("[DIP GG Fixes][Trade] window: " + e); }
        }

        static List<Row> BuildRows(Player main)
        {
            var l = new List<Row>();
            if (tab == 0)
            {
                l.Add(new Row { Head = true, Text = "Put ships up for sale - states at peace with you send offers in the following months" });
                foreach (var s in Fleet(main).Where(Sellable).OrderBy(x => TypeName(x)).ThenByDescending(x => Age(x)))
                {
                    string id = s.id.ToString(); var ship = s;
                    Listing li; listings.TryGetValue(id, out li);
                    if (li == null) l.Add(new Row { Text = $"{TypeName(s)} <b>{s.name}</b> - {Age(s)} years, value about {M(Value(s))}", Btn = "Put up for sale", Act = () => { listings[id] = new Listing { ShipId = id, Month = Economy.Month() }; Log($"listed {ship.name}"); Save(); } });
                    else
                    {
                        l.Add(new Row { Text = $"{TypeName(s)} <b>{s.name}</b> - <color=#FFD95A>for sale</color>{(li.Offers.Count == 0 ? ", waiting for offers" : "")}", Btn = "Withdraw", Act = () => { listings.Remove(id); Save(); } });
                        foreach (var o in li.Offers.ToList())
                        {
                            var b = ByKey(o.Buyer); if (b == null) continue; float price = o.Price; var buyer = b;
                            l.Add(new Row { Text = $"      {N(b)} offers <b>{M(o.Price)}</b>", Btn = "Accept", Act = () => { Sell(main, ship, buyer, price); Save(); } });
                        }
                    }
                }
                if (l.Count == 1) l.Add(new Row { Text = "No ship can be sold now (ships in service, not building or in refit)." });
            }
            else if (tab == 1)
            {
                var sellers = AllPlayers().Where(p => p.isMajor && !p.isMain && !AtWar(main, p)).OrderBy(N).ToList();
                if (sellers.Count == 0) { l.Add(new Row { Text = "No great power at peace with you can take orders." }); return l; }
                sellerIdx = ((sellerIdx % sellers.Count) + sellers.Count) % sellers.Count;
                var sel = sellers[sellerIdx];
                float prem = Mathf.Lerp(PremiumMax.Value, PremiumMin.Value, Mathf.Clamp01((Att(main, sel) + 20f) / 120f));
                l.Add(new Row { Text = $"Builder: <b>{N(sel)}</b> (attitude {Att(main, sel):0}, price x{prem:0.00} of its cost)", Btn = "Next builder >", Act = () => { sellerIdx++; page = 0; } });
                l.Add(new Row { Head = true, Text = $"{N(sel)}'s designs" });
                DesignRows(l, main, sel, sel, false, prem);
                l.Add(new Row { Head = true, Text = $"Your designs, built in {N(sel)}" });
                DesignRows(l, main, sel, main, true, prem);
            }
            else if (tab == 3)
            {
                l.Add(new Row { Head = true, Text = "Refit requests from navies that bought your ships (a request waits three months)" });
                foreach (var r in refits.ToList())
                {
                    var b = ByKey(r.Buyer); var s = FindShip(r.ShipId); if (b == null || s == null) continue; var rr = r;
                    float lic = r.Cost * (RefitLicence != null ? RefitLicence.Value : 0.2f), yard = r.Cost * (RefitPremium != null ? RefitPremium.Value : 1.4f);
                    l.Add(new Row { Text = $"{TypeName(s)} <b>{s.name}</b> ({N(b)}, {Age(s)} years) - refit worth {M(r.Cost)}, asked {Economy.Month() - r.Month} month(s) ago" });
                    l.Add(new Row { Text = $"      Sell the refit licence: <b>{M(lic)}</b> at once, their yards do the work", Btn = "Sell licence", Act = () => RefitLicenceSale(main, rr) });
                    l.Add(new Row { Text = $"      Refit in your yards: <b>{M(yard)}</b> over {r.Months} months", Btn = "Our yards", Act = () => RefitInYards(main, rr) });
                    l.Add(new Row { Text = "      Decline", Btn = "Decline", Act = () => RefitDecline(main, rr) });
                }
                if (refits.Count == 0) l.Add(new Row { Text = "No refit requests now." });
                if (yardJobs.Count > 0)
                {
                    l.Add(new Row { Head = true, Text = "Foreign ships in your yards" });
                    foreach (var j in yardJobs) l.Add(new Row { Text = $"<b>{j.Name}</b> ({N(ByKey(j.Buyer))}): {Math.Max(0, j.Months - (Economy.Month() - j.Start))} month(s) left, paid {M(j.Paid)} of {M(j.Total)}" });
                }
                int soldN = sold.Count(kv => FindShip(kv.Key) != null);
                l.Add(new Row { Text = $"Ships sold by you still in foreign service: {soldN}" });
            }
            else
            {
                var mine = orders.Where(o => o.Buyer == K(main)).ToList();
                if (mine.Count == 0) l.Add(new Row { Text = "No ships on order abroad." });
                foreach (var o in mine)
                {
                    var s = FindShip(o.ShipId);
                    string st = s == null ? "?" : (s.isBuilding ? "building" : "completed");
                    l.Add(new Row { Text = $"<b>{o.Name}</b> in {N(ByKey(o.Seller))}: {st}, paid {M(o.Paid)} of {M(o.Price)}{(o.Missed > 0 ? $" - {o.Missed} payment(s) missed" : "")}" });
                }
            }
            return l;
        }

        static void DesignRows(List<Row> l, Player buyer, Player seller, Player owner, bool own, float prem)
        {
            int n = 0;
            try
            {
                var en = owner.designs.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (un.MoveNext())
                {
                    var d = en.Current; if (d == null) continue;
                    bool refit = false; try { refit = d.isRefitDesign; } catch { }
                    if (refit) continue;
                    float cost = 0f; try { cost = d.Cost(); } catch { }
                    var dd = d;
                    l.Add(new Row { Text = $"    {TypeName(d)} {d.name} - about {M(cost * prem)}", Btn = "Order", Act = () => PlaceOrder(buyer, seller, dd, own) });
                    if (++n >= 60) break;
                }
            }
            catch { }
            if (n == 0) l.Add(new Row { Text = "    none" });
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(GameManager), "SaveInternal", new Type[] { typeof(bool), typeof(bool) })]
    internal static class Patch_TradeSave
    {
        static void Postfix() { Trade.Save(); }
    }
}

# Iron Admiral – Campaign Guide

_Guide as of 25 October 2026. Latest version: Iron Admiral 1.2.69. Updated with every release._

> **Iron Admiral** is a free mod for Ultimate Admiral: Dreadnoughts, built on DIP The Great Game: a battle and campaign AI that fights back, a deeper economy and historical data. Download, source code and full changelog: **https://github.com/KlaudiuszLedwoch/Iron-Admiral** (latest packages in [releases](https://github.com/KlaudiuszLedwoch/Iron-Admiral/tree/main/releases)).

This guide explains how the mechanics added by Iron Admiral change a campaign and how to use them. It does not list formulas – it shows what happens in practice, with examples from the author's test campaign (Germany, 1906–1910) and from player reports. The complete list of changes with reasons is in `CHANGELOG_EN.txt`; the in-game Help has a short chapter "Iron Admiral – new mechanics".

---

## 1. Battles: the enemy no longer waits to be picked off

In the base game the AI tends to stay clumped and lets you shoot it apart. In Iron Admiral its light forces attack:

- Torpedo boats, destroyers and light cruisers charge your battleships and heavy cruisers to get into torpedo range, and pull back when they are out of torpedoes or badly hit.
- A division that cannot outrun you turns and fights instead of being chased down.
- Guns finish off damaged ships first; heavy guns go for heavy targets, secondaries for torpedo boats.
- Each navy has its own doctrine – some hold the line at long range, others charge and accept losses.
- The enemy picks its fights. A torpedo boat that cannot catch your battleship no longer throws itself at it from 14 km, and a cruiser whose guns cannot hurt your belt stays out of your effective range instead of sailing into it.
- Ships on the approach zigzag. Firing at a turning ship is harder, so a charging flotilla is harder to stop – but turning spoils the ship's own aim even more, so the enemy straightens up once its own guns can do damage.

A player compared the same 1892 quick battle with and without the mod (even fleets: 1 BB, 2 CA, 4 CL, 6 TB):

> "Your mod, cl and tb charged my line resulting in their destruction and taking out most of my escorts and inflicting damage on my capital ships." … "The whole battle was definitely a bloody and intense fight though and forces a change in the usual tactics."
>
> "Vanilla. ai stayed clumped together and I was able to pick them off with minimal losses."
> – Minion of Speed, Steam

Another early tester, thunderspirit2014, singled out exactly this: the base-game AI that sails parallel to your battle line instead of attacking was the most frustrating part, and the battle AI fixes address it. Minion of Speed's first battle with the mod ended with the AI's torpedo boats and light cruisers charging his battleship line – and winning.

**What to do:**
- Never send capital ships out without a screen. Your destroyers are there to break up the torpedo attack before it reaches the line.
- Use your own light divisions on *Scout*: they run the same charge logic and can hurt enemy battleships.
- Expect a beaten enemy to fight on when it cannot escape – finish it, or let it go before it costs you a cruiser.

**From the test campaign (February 1916).** Three German *Hildebrand*-class battleships met 40 US ships – 2 battleships, 5 heavy and 15 light cruisers, 11 destroyers and 7 torpedo boats. The log shows why numbers did not decide it: the German main guns reached 25 km against 12–15 km for the US ships, and no American gun in the fight could penetrate the German belt at any range in the log. The only real danger was torpedoes – 20 charges by US destroyers, torpedo boats and light cruisers. This battle is also where the old AI's mistakes showed: some charges started far beyond any chance of reaching the target, and cruisers kept closing in although their guns could not do any harm. Version 1.2.21 fixes both and adds the zigzag on the approach.

**When you bring the stronger force.** The enemy no longer feeds its destroyers in one by one. If your force in the battle is stronger than its own – battleships or not – and it has enough torpedo craft for your ships, its whole torpedo force goes in together – a single ship would be sunk on the way, but a flotilla attacking at once gets some of its torpedoes into the water. In a battle from the test campaign (19 October 1916) four German battleships met nine US light cruisers: the cruisers went in division after division, two of them surrendered and were taken as prizes, and one German battleship came out of it with a quarter of its hull damaged. Light ships that have spent their torpedoes and cannot get away will try to ram. Screen your line, and keep your secondaries free for the boats that get close. A handful of boats will not charge you – if the enemy is outmatched and too weak in torpedo craft, its faster ships withdraw instead, so a decisive victory needs ships fast enough to catch them.

**Main ships that cannot hurt you, and task forces that only want to get away.** The enemy's largest ships – its battleships, or its armoured cruisers when it has no battleships – withdraw when their guns cannot penetrate your armour even at close range when they can survive the battle – they will not wait to be sunk. Enemy torpedo craft now go for anything clearly bigger than themselves, so in a cruiser action your cruisers and transports draw the torpedo runs that battleships used to. A task force whose mission is to escape no longer attacks: if it is faster it simply runs, and if it is not it splits up and the ships run in different directions. Catching all of them takes several divisions; decide early which ones are worth chasing. Enemy divisions that see torpedo tracks coming also turn to run along them, so long-range spreads from your own destroyers hit less often than before.

**Refits after the treaties.** If your battleships are already above the Washington Treaty limit, you can still refit them: a refit may add a limited amount of tonnage above the ship's present size, as the treaty itself allowed for anti-aircraft guns, torpedo bulges and deck armour. Since version 1.2.29 such a refit can also be saved. New construction keeps the treaty limit.

## 2. Crews, surrender and prizes

- Crew experience matters much more than in the base game. A veteran crew is a different ship; a green one is a liability. Training money is well spent.
- Green crews break off when badly damaged and surrender sooner; good crews fight on.
- A ship that surrenders goes to the side that wins the battle. An enemy ship taken as a prize arrives in repair with a green crew and brings its design, so you can build more of the class. One of your own ships that surrendered comes back to you, damaged, when you win – a lost destroyer is not lost if the battle is won.

## 3. Building ships: think in classes, not in single ships

Ships are cheaper when you build them in series – but only after the first one is finished.

- **The prototype** – the first ship of a new design – costs more and takes longer.
- **Sisters laid down after the prototype is in service** get cheaper and faster the longer the series runs.
- **Sisters ordered while the prototype is still on the slip** are built at normal price and time: nobody has built the type yet.
- **Order the series together.** A batch is cheaper, and small ships that fit side by side into your largest dock are built much faster as a batch. Torpedo boats and destroyers gain a lot; a battleship that fills the dock gains nothing.
- **Sister ships are cheaper to keep in service.**
- **Do not pause too long.** After years without a new ship of a design the yards forget the experience.
- **Watch shipyard workload.** An idle yard takes orders on better terms; an overloaded one charges more and delivers late. Refits follow the same rules.

**Example – 30 destroyers of 850 t (author's campaign, 1910–1911):**
- *All 30 at once:* one expensive prototype plus 29 ships at full price and full time.
- *One prototype first, then 29 together once it is commissioned* – what the author actually did with the destroyer V-1. The prototype took about 7 months. The 29 sisters, ordered together after V-1 entered service, fitted side by side into the largest dock: every one of them was scheduled for 2 months instead of 7 – the shortest time any ship can take – and cost about a third less than V-1 would have.

Ships already on the slips also move: when the yard learns, the remaining work on ships ordered after the prototype was in service shrinks with it.

**From the logs:** the AI pays for impatience too. In the 1910 test the Byzantine Empire and Scandinavia had loaded their yards beyond capacity, so every new prototype cost them noticeably more and took longer. Russia and Japan had ordered whole classes at once – their later ships got no series discount, although sisters were already in service.

**What to do:**
- Design carefully, build one ship, test it in battle, then order the class.
- Plan light forces in big batches – that is where series production pays most.
- Keep some free shipyard capacity before a big programme.

## 4. Money: the AI saves up – and so should you

In the base game an AI power could pour its whole naval budget into research and never order a ship. The test campaign showed it: the USA sat on 1.2 billion in cash with not a single ship or order, spending almost the whole naval budget on research and a shipyard expansion. Iron Admiral fixes that for the AI – research is free while there is a surplus and is cut when the books go red, and no yards are expanded in deficit. The AI navies therefore build fleets again, and you will meet them at sea.

**What to do:** treat research and shipyard expansion the same way. They pay off (they also add economic growth), but only while your income covers them.

## 5. Treasury crisis, inflation and credit

A great power that stays in debt must choose:

- **War loan** – the debt is cleared and money arrives at once; it is repaid over years with interest. Lenders remember a bankruptcy for a long time.
- **Money printing** – cash at once, but prices rise sharply and the public grows restless.
- **Fleet cuts** – no new money and no new capital ships for a while, but cheaper upkeep.

Broke AI powers stop ordering battleships and build cruisers, destroyers and torpedo boats instead.

**Inflation** raises the price of everything you pay for. It is driven up by borrowing, printing, grants, reparations and loot (the larger compared with your economy, the stronger), and by rising spending. Without new shocks it settles back to a normal low level; research slows a rising inflation down, and only large spending cuts push prices below zero.

**What inflation means here.** Inflation works as a surcharge at the current yearly rate on everything you pay: at 30% you pay 30% more for building, upkeep and research for as long as the rate stays there. It does not pile up into a permanent price level – when inflation falls back, your costs fall back with it, and your budget is not inflated either: every figure stays in today's money. A spell of high inflation therefore costs you while it lasts (and the debt you carried through it shrinks in real terms), but it never leaves ship prices and budgets permanently out of scale. A large swing in a nation's budget comes from the game itself – war, peace, lost or gained territory, recovery after a crisis – not from inflation.

**Credit** costs the inflation rate plus a margin set by your **credit rating**: the more debt you carry compared with your economy, the worse the rating, and deficits, war and a bankruptcy in the last years make it worse still. When inflation runs very high, lenders add an extra premium. Inflation also eats into the real value of what you owe. Unspent cash earns interest a little below inflation – it keeps its value, but printing money to sit on it does not pay.

**The British example (test campaign 1908–1910).** Britain lost colony after colony to France and Spain and paid heavily for lost transports and blockades. It took a war loan – cash jumped from about −1.1 billion to +2.4 billion and it started ordering ships again. Losses continued; two years later it was 4.5 billion in debt and printed money. Printing cleared the debt but drove inflation above 50% and added unrest every month; its loan, rated CCC after the bankruptcy, cost over 50% a year. Inflation then fell turn by turn (51% → 48% → 45% → 37% in the 1911 test), and with it the real value of the old loan – but its fleet still paid inflated prices all the way down. Lesson: a loan buys time, it does not fix a lost war; printing is the last resort.

**What to do:**
- Keep a cash reserve – it holds its value when prices rise, and it keeps you away from lenders.
- Borrow early and moderately rather than late and desperately – a good credit rating is cheap money, a bankruptcy makes every loan expensive for years.
- If inflation runs away, cut spending for a while; research helps too.
- Press **F8** on the campaign map for the treasury panel (inflation and its causes, credit rating, interest, loan, savings, growth, ships under construction with their series discounts, other powers). The Finances window shows inflation and what it costs you each month; after every turn a short treasury report sums up the month (it can be switched off from the report itself).

**From the logs:** with no shocks the healthy powers sit near zero. In the 1911 test Germany and France were at about −1% after earlier spending cuts and drifting back up a tenth of a point per turn, both rated AA, borrowing at under 1.5%.

## 6. Diplomacy, expansion and minor nations

**Coalitions.** Being the strongest navy has a price. Nations that hate you sit at the brink of war, but none of them alone dares to attack a much stronger fleet. Once together they are clearly stronger than you at sea, one of them strikes and the others follow over the next turns. The same happens to an AI empire that grows too strong. In the author's 1914 test every great power except France stood at the brink with Germany; one at a time each had at most half the German fleet, together about 1.5 times – and the coalition was declared ready.

**What to do:** watch how many powers are hostile and how strong they are together, not just the strongest rival. Keeping one or two of them friendly, or busy elsewhere, breaks the coalition.


- Relations worsen much faster than they improve. Aggression and expansion build friction quickly, and wars can follow one another. A player put it this way:

  > "…the snowball effect of wars being declared in rapid succession."
  > – Minion of Speed, Steam

  If you prefer calmer diplomacy, lower `DiplomacyNegativeAttitudeMultiplier` in `UserData\MelonPreferences.cfg`.
- Sovereign minor nations are back with their historical territories and are stronger. Watch whom they lean towards.
- Naval invasions decide colonial wars. In the test campaign Germany took Guam, the Northern Marianas, the Caroline Islands and the eastern Philippines from Spain by landings, while France took the Seychelles and Mauritius from Britain the same way.
- When a nation loses the port its ships are in and has nowhere to flee, its ships pass to whoever takes the port.

**Holding what you conquer.** Taking the heartland of another great power is one thing, keeping it is another. Once peace is made, its people rise – the larger their nation compared with yours, the harder it gets. Germany can hold the Netherlands; holding France is a different story. When a rising breaks out you choose: put it down, paying in money, unrest at home and goodwill among your neighbours and rivals, or let it run and defend the province with your fleet. Provinces that break free go back to their nation, and a great power you destroyed can return. Colonies and lands of other peoples inside the old empires are not affected – only the nations' own heartlands.

## 7. Choosing what you play with

Press **F10** anywhere in the game – main menu, campaign map or battle – to open the settings panel. Every feature of the mod is there with an ON/OFF switch and a one-line description, grouped by battle AI, crews and prizes, campaign AI, economy, diplomacy and information. A click applies and saves at once.

A player on the Steam thread asked for the battle AI, crew quality and surrendered ships without the economic layer. That is one panel away: open the *Economy* group and switch off series production, inflation and the treasury crisis, then the treasury panel and reports under *Information and updates*. Nothing else changes – the battle AI, crews, panic, surrender and prizes keep working. The economy never asked for micromanagement in the first place: it runs in the background and mostly decides what the AI can afford.

**Something looks wrong?** Open F10, go to the *Tools* tab and click *Create problem report*. One zip appears on your desktop with the logs, your mod settings and a check of your Mods folder - attach it to your post on Nexus, Steam or GitHub and say what happened. The mod sends nothing by itself.

## 8. A short checklist for a new campaign

1. Design your first classes carefully – every new design starts with an expensive prototype.
2. Build one of each new class, then order the rest in batches; leave room in the yards.
3. Keep light forces with every battle line.
4. Spend on research and shipyards from surplus, not from debt.
5. Keep a reserve; check the treasury panel (F8) after every few turns.
6. Expect the AI to fight back – at sea and in its budget.

## 9. Fog of war: you see what your scouts see

Enemy fleets on the campaign map are no longer drawn exactly. Each foreign task force is seen as well as your reconnaissance allows that month: the era (wireless, direction finding, aircraft, radar), your reconnaissance technology and intelligence budget compared with the other powers, how far it is from your nearest port, and whether one of your task forces - especially one with cruisers and destroyers, or one lying at anchor as a patrol - is nearby. A force you know well is drawn where it is. One you know poorly is drawn in roughly the right area with no route. One nobody has found is not drawn at all.

What to do with it:

1. Keep light ships with your main fleet. They improve the contact report you get before a battle and the picture of the sea around the fleet.
2. Station patrols on the sea lanes that matter. A stationary group watches a wider area than a moving one.
3. Do not trust the battle window blindly. "2 battleships (reported)" may be two heavy cruisers - or there may be a third ship nobody reported. A report only names ship types the enemy navy actually has. That is how 1916 felt: four of seven Zeppelin reports on 19 August were wrong.
4. In battle, an enemy you have only detected is an "Unidentified ship (large)". Closer, it becomes a "Probable battlecruiser?" - which can still be wrong. Its name, class and details appear once one of your ships closes to identification range. At night, in rain and behind smoke that range is short.
5. The enemy has the same problem. It judges your force by what it has seen, so a fast scouting screen can make it commit - or withdraw - on a wrong picture.

Everything can be switched off in the F10 panel ("Fog of war"). FogStrength in MelonPreferences.cfg makes it lighter or harsher.

## 10. Angle your armour

Until now the game resolved every hit on the belt as if it struck square on. A battleship turned 60 degrees away presented the same belt as one lying broadside on. The battle logs showed it plainly: identical penetration at every aspect, while the real hits came from every angle.

Now the belt counts thicker along the shell's path the more obliquely it is hit. Capped shells keep their bite up to moderate angles but lose it at steep ones, and the early uncapped shells of the 1890s suffer more.

**What to do:**
- In a gunnery duel, turn your ships some 30-50 degrees away from the enemy's line of fire. You give up part of your broadside, but your belt becomes much harder to break.
- Close in only when you can hit his belt square on, and expect the AI to angle too.
- In early campaigns armour wins more often: uncapped shells lose more against angled plates.

## 11. Buying and selling ships

The trade window (F9) has two uses.

**Selling.** Old ships can go on sale instead of to the scrapyard. Over the following months states at peace with you make offers: the price follows the ship's value, its age and condition, the buyer's money and relations. A sale wins you the buyer's goodwill and annoys his enemies. AI great powers sell their old ships the same way, so a minor navy may suddenly field a pre-dreadnought.

**Ordering abroad.** You can order a ship from any great power at peace with you, using its design or your own. It costs more than at home and is paid month by month, but it takes no slip of your own. If relations turn hostile before delivery, the builder may keep the ship, as Britain did with the Turkish battleships in 1914.

**What to do:**
- When your yards are full, order a prototype or a few escorts abroad. Pick a builder you trust.
- Sell obsolete ships to minors you want as friends. Selling to a power's enemy annoys that power.

## 12. Custom battles: where the ships come from

For every ship of a custom battle the game looks in three places, in this order:

1. **Shared designs** – your Designs folder. Iron Admiral puts its historical classes there (real ships rebuilt from historical data and saved only when the game accepts them), together with the Shippack designs; your own designs count too. When a historical class of the nation exists for the battle year, it is taken first.
2. **Pregenerated designs** – predefined design files. Iron Admiral ships none. The base game's built-in ones were made for technologies that DIP removed, so not one of them can load: the game used to spend several seconds on them, log a stream of errors and then fall back to step 3 anyway.
3. **The ship generator** – builds a new ship for the nation and year.

That is why the **Pregenerated** button in the custom battle screen is **Off** when the game starts: the game skips step 2 and goes from your shared designs straight to the generator.

**What to do:**
- Keep **Shared Designs** on to fight with historical classes. Their number grows with every release, so more nations and years get real ships.
- Turn **Pregenerated** on only if you have installed predefined design files made for DIP yourself. The button holds until you quit the game; to keep it on for good, switch on *CustomBattlePregenerated* in the F10 panel.
- Why not ship DIP's ready-made designs? They load, but they are generator output with historical names: in the British file the 1906 "Bellerophon" carries five twin 14-inch turrets (the real ship: ten 12-inch guns in five twin turrets) and the 1912 "Warspite" six twin 14-inch turrets (the real Queen Elizabeth class: eight 15-inch guns in four twin turrets).


## 13. Fleet battle doctrine: who attacks first, and how each navy fights

Since 1.2.54 the battle AI follows the fleet battle instructions of the period (the US Navy War Instructions of 1944 as the default) and a doctrine for each navy and period.

- **The battle line goes in first.** Enemy battleships move up to fighting range instead of waiting at the edge of the map; their destroyers hold their torpedoes until the lines are in action. Expect the destroyer attack when your battleships are busy with theirs - keep a few ships ready to meet it.
- **A weaker enemy attacks with torpedoes, not with guns.** If you are clearly stronger, the enemy sends its destroyers in first (a delaying action) and keeps its battle line back. Kill the flotillas and the rest will try to get away - the expensive ships first, covered by smoke and by the cheaper ones.
- **Destroyers come back.** A flotilla that has fired everything turns away behind smoke. If it carries reload torpedoes it waits out of range and returns - do not forget it. Without reloads it turns its guns on your destroyers.
- **Cruisers have jobs:** guarding their battleships against your destroyers, supporting their own torpedo attack, appearing on the other flank to pin your line while the torpedoes come from the opposite side.
- **Fire is spread.** No more than three enemy ships usually fire at one of yours; when the enemy is outnumbered each of its ships takes a different target.
- **Night.** Battleships keep their distance; destroyers try to surprise you and turn away if spotted too early by a stronger force.
- **Each navy differs:** the Grand Fleet always turns away from torpedoes and avoids night action until the 1930s; Japan attacks with torpedoes at night before the main battle; Austria-Hungary raids and runs; the French Jeune Ecole relies on torpedo boats. The full table with sources is `Mods\battleDoctrines.csv`.

Every rule can be switched off in the F10 panel (group Battle AI).

---

### Guide history
- **18 October 2026:** first edition – battles, crews and prizes, series production, AI budgets, treasury crisis, inflation and credit, diplomacy.
- **18 October 2026, Iron Admiral 1.2.16:** series production and the economy are live – real result of the 30-destroyer example, ships on the slips, treasury panel, Finances window and monthly report, savings that keep value rather than make money, inflation returning to normal, Britain's inflation and loan in 1911.
- **19 October 2026, Iron Admiral 1.2.22:** in-game settings panel (F10) and how to play with only the battle AI, crews and prizes.
- **18 October 2026, Iron Admiral 1.2.17:** what inflation means in the mod – a temporary surcharge, not a growing price level; budgets stay in today's money.
- **18 October 2026, Iron Admiral 1.2.20:** coalitions against the dominant power.
- **18 October 2026, Iron Admiral 1.2.21:** refits of ships above the treaty limits; a 1916 battle from the test campaign (3 battleships against 40 ships); the enemy charges only what it can reach, does not close in without a chance to penetrate, and zigzags on the approach.
- **19 October 2026, Iron Admiral 1.2.23:** massed torpedo attacks when you outnumber the enemy in battleships, ramming, enemy battleships that withdraw when they cannot hurt you, task forces that run or scatter instead of attacking, turning away from torpedo tracks.
- **19 October 2026, Iron Admiral 1.2.24:** enemy tactics by ship size, not by battleships; surrendered ships go to the battle winner.
- **19 October 2026, Iron Admiral 1.2.25:** massed attacks only with enough torpedo craft; outmatched enemies withdraw.
- **19 October 2026, Iron Admiral 1.2.26:** series savings apply right from loading a campaign.
- **19 October 2026, Iron Admiral 1.2.27-1.2.29:** refits above the treaty limit can be saved (confirmed in 1.2.29).
- **19 October 2026, Iron Admiral 1.2.30:** risings in conquered heartlands and the decision to put them down.
- **20 October 2026, Iron Admiral 1.2.33:** problem report from the F10 panel.
- **20 October 2026, Iron Admiral 1.2.31:** fog of war - what you see of enemy fleets, contact reports and identification in battle.
- **21 October 2026, Iron Admiral 1.2.48:** angled armour; buying and selling ships (F9).
- **21 October 2026, Iron Admiral 1.2.49:** trade window fixed; armour angle measured as the game does it.
- **21 October 2026, Iron Admiral 1.2.50:** battle report in its own panel beside the result window.
- **21 October 2026, Iron Admiral 1.2.51:** the 920 Shippack designs load again.
- **21 October 2026, Iron Admiral 1.2.52:** custom battles – where the ships come from (chapter 12): historical classes first, the "Pregenerated" button off by default.
- **22 October 2026, Iron Admiral 1.2.53:** 147 historical classes (battleships, battlecruisers, cruisers).
- **22 October 2026, Iron Admiral 1.2.54:** fleet battle doctrine - who attacks first and how each navy fights (chapter 13); dispatches above the ships.
- **22 October 2026, Iron Admiral 1.2.55:** intelligence window and treasury panel display fixes; screenshots of the new features in the mod's image gallery.
- **22 October 2026, Iron Admiral 1.2.56:** sixteen towers load again (and the shared designs using them); doctrines for Japan 1906-1921, France 1906-1918, Russia and the Soviet navy, Spain, the Netherlands, Scandinavia and Greece.
- **22 October 2026, Iron Admiral 1.2.57:** main-gun target choice prefers the close enemy and keeps the current target; no more switching between a distant light cruiser and a close armoured cruiser.
- **22 October 2026, Iron Admiral 1.2.58:** AI ships hold their torpedoes while the shot is hopeless (out of reach, small target, target outrunning the torpedo).
- **22 October 2026, Iron Admiral 1.2.59:** torpedo aims whose target lies on the other side of the ship are dropped; torpedo launch audit in the battle log.
- **22 October 2026, Iron Admiral 1.2.60:** no torpedoes at surrendered ships; a surrendered ship returned to the winning side is no longer sunk by a damage value of 1.00.
- **22 October 2026, Iron Admiral 1.2.61:** the torpedo side check is off (it blocked valid shots); faster target selection in large battles; performance log.
- **22 October 2026, Iron Admiral 1.2.62:** the mod's panels scale with the game's interface; the AI remembers failed ship designs and spends less time on them at the end of a turn; ships may answer a closing torpedo attack with torpedoes.
- **22 October 2026, Iron Admiral 1.2.63:** the dispatch bubbles above the ships have their own, smaller scale in battle.
- **22 October 2026, Iron Admiral 1.2.64:** the AI's ship generator memory survives a restart; own ships without a design in the list get one back for refits.
- **22 October 2026, Iron Admiral 1.2.65:** the experimental continuous map can no longer freeze the game after a battle.
- **23 October 2026, Iron Admiral 1.2.66:** the battle AI no longer breaks off before it knows its target and fights on when clearly stronger; your ships use the full torpedo range; captured ships appear in the designer.
- **25 October 2026, Iron Admiral 1.2.69:** the beta line of 23-25 October promoted to stable - ballistics from physics and technology, hit chance by fire-control technology, HE through thin plating, division orders in battle, occupied lands decided province by province, lost territory, technology diffusion, historical weights, an AI designer that limits its guns by displacement and trims overweight attempts, refits for the buyers of your ships, eight new Help chapters.

---

> **Get Iron Admiral:** https://github.com/KlaudiuszLedwoch/Iron-Admiral – every new version is published there first, with both download packages, the one-file setup script, the full changelog and the source code. Feedback, logs (`MelonLoader\Latest.log`) and campaign stories are welcome in the Steam discussion thread or on GitHub.

# Iron Admiral – Campaign Guide

_Guide as of 18 October 2026. Latest version: Iron Admiral 1.2.18. Updated with every release._

> **Iron Admiral** is a free mod for Ultimate Admiral: Dreadnoughts, built on DIP The Great Game: a battle and campaign AI that fights back, a deeper economy and historical data. Download, source code and full changelog: **https://github.com/KlaudiuszLedwoch/Iron-Admiral** (latest packages in [releases](https://github.com/KlaudiuszLedwoch/Iron-Admiral/tree/main/releases)).

This guide explains how the mechanics added by Iron Admiral change a campaign and how to use them. It does not list formulas – it shows what happens in practice, with examples from the author's test campaign (Germany, 1906–1910) and from player reports. The complete list of changes with reasons is in `CHANGELOG_EN.txt`; the in-game Help has a short chapter "Iron Admiral – new mechanics".

---

## 1. Battles: the enemy no longer waits to be picked off

In the base game the AI tends to stay clumped and lets you shoot it apart. In Iron Admiral its light forces attack:

- Torpedo boats, destroyers and light cruisers charge your battleships and heavy cruisers to get into torpedo range, and pull back when they are out of torpedoes or badly hit.
- A division that cannot outrun you turns and fights instead of being chased down.
- Guns finish off damaged ships first; heavy guns go for heavy targets, secondaries for torpedo boats.
- Each navy has its own doctrine – some hold the line at long range, others charge and accept losses.

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

## 2. Crews, surrender and prizes

- Crew experience matters much more than in the base game. A veteran crew is a different ship; a green one is a liability. Training money is well spent.
- Green crews break off when badly damaged and surrender sooner; good crews fight on.
- A ship that surrenders is taken as a prize if you still have ships in the battle. It arrives in repair with a green crew and brings its design, so you can build more of the class.

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

- Relations worsen much faster than they improve. Aggression and expansion build friction quickly, and wars can follow one another. A player put it this way:

  > "…the snowball effect of wars being declared in rapid succession."
  > – Minion of Speed, Steam

  If you prefer calmer diplomacy, lower `DiplomacyNegativeAttitudeMultiplier` in `UserData\MelonPreferences.cfg`.
- Sovereign minor nations are back with their historical territories and are stronger. Watch whom they lean towards.
- Naval invasions decide colonial wars. In the test campaign Germany took Guam, the Northern Marianas, the Caroline Islands and the eastern Philippines from Spain by landings, while France took the Seychelles and Mauritius from Britain the same way.
- When a nation loses the port its ships are in and has nowhere to flee, its ships pass to whoever takes the port.

## 7. A short checklist for a new campaign

1. Design your first classes carefully – every new design starts with an expensive prototype.
2. Build one of each new class, then order the rest in batches; leave room in the yards.
3. Keep light forces with every battle line.
4. Spend on research and shipyards from surplus, not from debt.
5. Keep a reserve; check the treasury panel (F8) after every few turns.
6. Expect the AI to fight back – at sea and in its budget.

---

### Guide history
- **18 October 2026:** first edition – battles, crews and prizes, series production, AI budgets, treasury crisis, inflation and credit, diplomacy.
- **18 October 2026, Iron Admiral 1.2.18:** series production and the economy are live – real result of the 30-destroyer example, ships on the slips, treasury panel, Finances window and monthly report, savings that keep value rather than make money, inflation returning to normal, Britain's inflation and loan in 1911.
- **18 October 2026, Iron Admiral 1.2.18:** what inflation means in the mod – a temporary surcharge, not a growing price level; budgets stay in today's money.

---

> **Get Iron Admiral:** https://github.com/KlaudiuszLedwoch/Iron-Admiral – every new version is published there first, with both download packages, the one-file setup script, the full changelog and the source code. Feedback, logs (`MelonLoader\Latest.log`) and campaign stories are welcome in the Steam discussion thread or on GitHub.

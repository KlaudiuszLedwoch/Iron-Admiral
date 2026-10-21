# Historical naval data (Iron Admiral research set)

Collected 21 October 2026 for historical ship designs, scenarios and naval intelligence. One row per class (major rebuilds as extra rows). Every row names its source page(s) in `source_url` - mainly English Wikipedia, also navypedia.org, naval-encyclopedia.com and jutland1916.com. Values are taken only from the cited page; blanks mean the page did not give the figure; conflicts between sources, conversions and assumptions are explained in `notes`. Column definitions: SCHEMA.md.

| File | Rows | Content |
|---|---|---|
| capital_ships.csv | 271 | Ironclads/pre-dreadnoughts, dreadnoughts and fast battleships, battlecruisers, coastal defence ships of all navies, 1880-1950 |
| cruisers.csv | 437 | Armoured, protected, scout, light, heavy and AA cruisers of all navies, 1880-1950 |
| destroyers_torpedo_boats.csv | 691 | Torpedo boats, torpedo gunboats, destroyers, leaders and destroyer escorts of all navies, 1880-1950 |
| carriers.csv | 93 | Seaplane, fleet, light and escort carriers, 1911-1950 (incl. unfinished) |
| naval_aircraft.csv | 124 | Carrier-borne, shipboard and patrol aircraft, 1911-1950 |
| submarines.csv | 380 | Submarine classes of 28 navies, 1888-1950 |

Conventions worth knowing before analysis:
- Displacement is mostly in long tons as the source gives it. Pre-1922 ships usually have only a "normal" displacement; it is stored in `displacement_std_t` and the row's notes say so.
- Where navypedia gives power only as "hp", the unit was set to ihp (reciprocating engines) or shp (turbines) and the notes say it was inferred.
- Commissioning years are often completion years (noted per row).
- Ships transferred between navies are usually not separate rows; the transfer is in the original class's fate.

Known gaps: some pages were refused by rate limiting during collection (listed in the collection notes: Russian torpedo boats on navypedia, a few Swedish cruiser pages, some Norwegian 2nd-class torpedo boats, Estonia/Latvia destroyers); rebuild rows often carry only partial figures.

Wikipedia text is available under CC BY-SA 4.0; the figures here are facts compiled from the cited pages.

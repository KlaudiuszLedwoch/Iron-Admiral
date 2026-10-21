# Historical warship data - common schema (CSV, UTF-8, comma-separated, quote fields containing commas)

Columns (ships):
nation,class,type,ships_built,ship_names,first_laid_down,first_commissioned,last_commissioned,displacement_std_t,displacement_full_t,length_oa_m,beam_m,draught_m,speed_kn,range_nmi,range_at_kn,power,power_unit,propulsion,main_guns,secondary_guns,light_aa_guns,torpedo_tubes,belt_mm,deck_mm,turret_face_mm,barbette_mm,conning_tower_mm,aircraft,complement,fate_summary,notes,source_url

Rules
- One row per CLASS (sister ships together). Major rebuilds/modernisations that changed the ship substantially = an extra row with class "X (as rebuilt YYYY)".
- type codes: BB (dreadnought/fast battleship), PDBB (pre-dreadnought/ironclad battleship), BC (battlecruiser), CDS (coastal defence ship/monitor-type armoured ship), CA (armoured/heavy cruiser), CL, DD, TB, SS (submarine), CV, CVL, CVE, AV (seaplane carrier).
- Numbers ONLY from the cited source page. Do not fill gaps from memory: leave empty if the source does not give it.
- Convert inches to mm (x25.4, round to whole mm) and feet to metres (x0.3048, 2 decimals); keep long tons as tonnes value given (note "long tons" in notes if source states it). Note any conversion in notes only if ambiguous.
- Guns as "count x calibre mm (description)", e.g. "8 x 305 mm (4x2)"; several groups separated by "; ".
- Armour: maximum value of the zone; if a range, take the max and put the range in notes.
- power: number; power_unit: ihp or shp.
- source_url: the page actually used (Wikipedia class article, navypedia page, etc.). Several URLs separated by " | ".
- Dates: year (YYYY) is enough.
- Write the CSV with a header row to the file path given in the task. Use Python csv module to write it (avoids quoting errors).

Additional rules for cruisers, destroyers and torpedo boats (batch 2)
- type codes: CA = armoured cruiser or heavy cruiser (8in-gun treaty cruiser); CL = protected cruiser, scout cruiser, light cruiser, AA cruiser; DD = destroyer (incl. torpedo-boat destroyers, flotilla leaders - note "leader"); TB = torpedo boat (incl. 1880s-1900s torpedo boats, torpedo gunboats/catchers - note which, and WWII "torpedo boats"/escort destroyers such as Italian Spica, German Type 35/39, Hunt class -> use DD with note if source calls them destroyers); DE = destroyer escort/frigate-type escorts only if source calls them destroyer escorts.
- Put the subtype word used by the source in notes (e.g. "protected cruiser", "scout cruiser", "torpedo gunboat").
- Large destroyer programmes: one row per class (e.g. British A-I classes 1930s each a row; US Clemson one row; Fletcher one row).

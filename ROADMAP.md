# Iron Admiral roadmap

_Updated 22 November 2026 - latest beta 1.2.78-beta.516, stable 1.2.77.9. Bug fixes always go first. The same list is kept on Discord (#roadmap), in the wiki (Development plan) and in GitHub issues._

## Now (beta)
- **Battle AI as my own layer.** The game's battle AI freezes on conflicting decisions ("duck shooting"); I am covering it completely with my own decision layer: torpedo charges by the whole group, launch range and salvo timing by losses, firing on the last good chance, counter-attack when the enemy closes, no idling under fire.
- **Player tactical profiles.** Battle logs map how real players fight; the AI is shaped on those maps (started with my own ~200 battles).
- **Air strike performance** - large strikes (150+ aircraft): lighter logic and simple shapes for distant aircraft.
- **Carriers, submarines and mines** in testing (submarines are not yet handled in battle).

## Next, in this order
1. **Carriers, submarines and mines finished** - submarines diving and fighting in battle, then building them in the campaign (#470).
2. **Admirals** - characters with traits and AI personalities taken from player profiles; stylised portraits for players who want them; tactics evolve with new logs. Together with governments that set goals and decide on war.
3. **Citadel and armour layers** - plate by plate instead of fixed multipliers (#116), buoyancy, longitudinal bulkheads.
4. **Economy that follows history** - coal and oil fields apart (#197), oil logistics, faster campaign start after 1890 (#194).
5. **Technology tree as one piece** (#363).

## Later
- **PBEM multiplayer** - each player fights an AI with the other player's personality; the two results are averaged into one "as if" battle between them.
- War phases and a variable time scale on the campaign map (#191).
- Fleet orders on the campaign map, sea control and blockades.
- Naval intelligence: silhouettes, intent, intelligence network (#17).
- Historical scenarios and campaign starts; Ottoman Empire splitting into Turkey and Greece.
- Converting one existing battleship or cruiser into a carrier.
- "Do not show again today" on every pop-up window, with a default answer.

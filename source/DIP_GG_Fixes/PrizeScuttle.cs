// 1.11.351 (author 28.10, 17:44): a surrendered ship that is wrecked does not reach port as a prize. Campaign battle 28.10: BB Rodney
// struck her colours with damage 1.00 (her whole structure gone) and was still brought in as a prize. Now, before a surrendered ship
// is taken by the winner:
//  - damage at or above PrizeScuttleDamage (0.90): she does not make port - a trained crew scuttles her, an untrained one leaves her
//    to founder on the way;
//  - below that, her crew may still scuttle her rather than hand her over: the chance grows with the crew's training (author: "the
//    better the crew, the sooner it scuttles her when it cannot fight") - PrizeScuttleChance at full training, nothing for a green crew.
// A ship that is lost this way stays on the battle's sunk list (the game's own accounting of the loss). Not applied when her own
// side won the battle (she returns to her owner). The flooding of the ship is not readable from the game, so only the damage counts.
using System;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PrizeScuttle
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Damage, Chance;
        static readonly Random rnd = new Random();
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PrizeScuttle", true, null, "Crews & prizes: a surrendered ship with damage of 90 % or more does not reach port as a prize (scuttled by her crew or founders on the way); below that her crew may scuttle her, the more likely the better trained it is");
            Damage = cat.CreateEntry("PrizeScuttleDamage", 0.9f, null, "Prize scuttling: damage at which a surrendered ship never reaches port (0-1)");
            Chance = cat.CreateEntry("PrizeScuttleChance", 0.6f, null, "Prize scuttling: chance that a fully trained crew scuttles its surrendered ship below that damage (scaled down with training)");
        }
        static float Training(Ship s) { try { float t = s.CrewTrainingAmount; if (t > 1.5f) t /= 100f; return UnityEngine.Mathf.Clamp01(t); } catch { return 0.5f; } }

        // true = the ship is lost (not taken as a prize); the message goes to the log, the dispatches and the battle report
        internal static bool Lost(Ship ship, float damage, string winner)
        {
            if (On == null || !On.Value || ship == null) return false;
            try
            {
                float train = Training(ship);
                string name = ship.name.Split(new[] { " (" }, StringSplitOptions.None)[0];
                string text = null;
                if (damage >= Damage.Value)
                {
                    text = train >= 0.4f
                        ? $"{name} had struck her colours, but she was a wreck - her crew opened the sea cocks and set the scuttling charges before the {winner} boarding party could take her in tow. She went down."
                        : $"{name} had struck her colours, but she was a wreck - she foundered under tow before she could be brought in.";
                    MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name}: damage {damage:0.00} >= {Damage.Value:0.00} - not taken as a prize ({(train >= 0.4f ? "scuttled" : "foundered")}, crew training {train:0.00})");
                }
                else
                {
                    float p = Math.Max(0f, Chance.Value) * train;
                    double roll = rnd.NextDouble();
                    MelonLogger.Msg($"[DIP GG Fixes][Capture] {ship.name}: scuttling check - damage {damage:0.00}, crew training {train:0.00}, chance {p:0.00}, roll {roll:0.00}");
                    if (roll < p) text = $"{name} had struck her colours, but her crew would not hand her over - they scuttled her before the {winner} prize crew came aboard.";
                }
                if (text == null) return false;
                try { Narrative.Record("prize|" + ship.Pointer, 9, text); } catch { }
                try { Dispatch.Prize(text); } catch { }
                return true;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] scuttling: " + e.Message); return false; }
        }
    }
}

// 1.11.168 (beta): historical damage values (HistoricalDamage, F10 Battle AI, on by default).
// Values from the 24.10.2026 review of DIP against vanilla and historical sources (US Navy War Damage Reports No. 29 and 57,
// Campbell's Jutland damage tables, navweaps.com); the list with sources is public in DIP_CHANGES.md on GitHub.
//   over_penetration 0.002 (DIP) / 0.32 (vanilla) -> 0.10: shells passing through without exploding did real damage
//     (South Dakota 1942, Hoel and Gambier Bay at Samar, Bismarck's bow - 1000-2000 t of water from one 14" shell);
//   over_penetration_threshold 3.5 / 2 -> 3.0;  armor_damage (shell breaking up on armour) 0.05 / 0.42 -> 0.15 (South Dakota; 1.11.199: 0.10 -> 0.15, upper end of the sourced range - author 24.10, damage too low);
//   fire_damage 0.33 / 1.6 -> 0.30 (fires rarely destroyed structure; the danger was ammunition - Seydlitz 1915, Astoria 1942);
//   section_damage_torpedo and torpedo_damage_ammo_detonation 5200 / 1750 & 690 -> by year: 2000 in 1900 rising to 5200 by 1940
//     (warheads 54 kg in 1894, 145 kg in 1910, 127-234 kg in WWI, 224-373 kg Mk 15, ~490 kg Type 93).
// The parameters are changed in the game data while the switch is on; the game's values come back when it is switched off.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HistoricalDamage
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> GunMult;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            // 1.11.204 (author, 24.10): base shell damage x4 - a full penetration by a heavy shell took 1-2 % of a cruiser's structure while
            //           a torpedo took 15 %; history: seven 11-in hits put Exeter out of action, ~20 heavy hits sank a battlecruiser (Luetzow, Bluecher)
            GunMult = cat.CreateEntry("ShellDamageMult", 4f, null, "Battle: multiplier of the base damage of gun shells (section_damage_gun) - heavy hits wreck a cruiser in a handful of hits as in real battles; 1 = the game's value");
            On = cat.CreateEntry("HistoricalDamage", true, null, "Battle: historical damage values - shells passing through a ship without exploding still do damage, shells breaking up on armour damage the structure behind it, torpedo damage grows with the era (weak early warheads, very strong WWII torpedoes)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Damage] " + m); }

        static readonly Dictionary<string, float> orig = new Dictionary<string, float>();
        static IntPtr gdPtr; static int applied = -1, appliedYear; static int errors;
        static ParamData P(string name)
        {
            try { var gd = G.GameData; if (gd == null || gd.paramsRaw == null) return null; ParamData p; return gd.paramsRaw.TryGetValue(name, out p) ? p : null; } catch { return null; }
        }
        static string Set(string name, float v)
        {
            var p = P(name); if (p == null) return name + " (not found)";
            if (!orig.ContainsKey(name)) orig[name] = p.value;
            p.value = v; return $"{name} {orig[name]} -> {v}";
        }
        static string SetMul(string name, float m) { var p = P(name); if (p == null) return name + " (not found)"; if (!orig.ContainsKey(name)) orig[name] = p.value; p.value = orig[name] * Math.Max(0.1f, m); return $"{name} {orig[name]} -> {p.value} (x{m})"; }
        static void Restore(string name) { var p = P(name); float v; if (p != null && orig.TryGetValue(name, out v)) p.value = v; }
        static readonly string[] Names = { "over_penetration", "over_penetration_threshold", "armor_damage", "fire_damage", "section_damage_torpedo", "torpedo_damage_ammo_detonation", "section_damage_gun" };

        // 1.11.180: torpedo damage by technology, never by the calendar year. The game already scales each torpedo by the torpedo
        // technologies of the ship that fires it (torpedo_size_* up to +310%, torpedo_prop_* and torpedo_tubes marks add more), which
        // matches the historical growth of warheads (~54 kg in 1894 -> ~490 kg Type 93). DIP raised the base to the WWII value (5200)
        // on top of those technologies; the base goes back to the game's own early-torpedo value and the technologies do the rest.
        const float TorpBase = 1750f, TorpAmmoBase = 690f;

        static float next;
        internal static void Tick()
        {
            if (UnityEngine.Time.unscaledTime < next) return; next = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                var gd = G.GameData; if (gd == null) return;
                if (gd.Pointer != gdPtr) { gdPtr = gd.Pointer; orig.Clear(); applied = -1; }
                bool on = On != null && On.Value;
                int year = 0;
                if (on && (applied != 1 || year != appliedYear))
                {
                    
                    var parts = new List<string> { Set("over_penetration", 0.10f), Set("over_penetration_threshold", 3.0f), Set("armor_damage", 0.15f), Set("fire_damage", 0.30f), Set("section_damage_torpedo", TorpBase), Set("torpedo_damage_ammo_detonation", TorpAmmoBase), SetMul("section_damage_gun", GunMult != null ? GunMult.Value : 1f) };
                    if (applied != 1 || year != appliedYear) Log($"historical damage values on (torpedo damage from the torpedo technologies of each ship): " + string.Join(", ", parts));
                    applied = 1; appliedYear = year;
                }
                else if (!on && applied != 0)
                {
                    if (applied == 1) { foreach (var n in Names) Restore(n); Log("historical damage values off: game values restored"); }
                    applied = 0;
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
    }
}

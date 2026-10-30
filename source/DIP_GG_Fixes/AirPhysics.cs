// 1.11.419 (#85, author 30.10 06:34: "as the torpedo leaves the aircraft there should be a clear jump - it suddenly weighs much
// less - and it should speed up; work on the flight physics: a simple model, but based on aerodynamics and the physical
// features of the aircraft we have - find their technical data and map them into the game"): a point-mass flight model.
//
// Forces along the flight path: propeller thrust T = eta * P / V (no more than at half the top speed), parasite drag
// D0 = q S CD0, induced drag Di = L^2 / (q S pi e AR), the weight component m g sin(gamma). dV/dt = (T*throttle - D0 - Di)/m
// - g sin(gamma). The steepest climb the excess power allows limits the climb angle. The pilot works the throttle to hold the
// speed of the phase: cruise 80 % of the top speed on the approach, the release speed on the torpedo run, full power away.
// CD0 is solved from the published top speed at the loaded weight (level flight at full power). At the drop the mass falls by
// the torpedo's weight, the wing still carries the old weight for a moment (lift excess m_old g - m_new g, the pilot takes it
// off in about a second): the aircraft jumps up, its nose rises, and with less induced drag and without the torpedo's own drag
// it speeds up.
//
// Data (Wikipedia unless noted; historyofwar.org, warbirdsresourcegroup.org/BARC, armedconflicts.com, ingeniumcanada.org as
// cross-checks, gathered 30.10): loaded mass (kg), wing area (m2), span (m), power (kW), top speed (km/h), torpedo (kg)
//   Short 184        2433  63.9  19.36   194  142   367 (14 in, 810 lb)
//   Sopwith 2F.1     694   21.5*  8.20   112  196     0   (* wing area of the F.1 - the 2F.1's was slightly smaller)
//   Swordfish I      3438  56.4  13.87   510  230   760 (torpedo weight given on the aircraft's page)
//   Albacore         4745  57.9  15.24   840  259   760
//   Beaufort I       9629  46.7  17.63  1680  423   728 (top speed with the Mk XII torpedo)
//   Hampden (bomber) 8508  62.1  21.08  1500  398   702 (Mk XII; no TB.I figures found - the bomber's, BARC loaded weight)
//   Beaufighter TF.X 11521 46.7  17.63  2386  488   817 (Mk XV; top speed at 396 m, historyofwar)
// Not from a source (assumptions): propeller efficiency 0.8, Oswald factor 0.75, sea-level air 1.225 kg/m3, torpedo drag area
// 0.05 m2. No source gave lift-drag figures; the CD0 values come out at 0.03-0.05 for the biplanes and lower for the monoplanes.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DipGgFixes
{
    internal static class AirPhysics
    {
        internal class Type
        {
            public string Name; public float Mass, S, Span, P, Vmax, Torp, Cd0, AR;
        }
        const float Rho = 1.225f, Eta = 0.8f, E = 0.75f, G = 9.81f, TorpCdA = 0.05f;
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static Type Def(string n, float m, float s, float b, float pkw, float vkmh, float torp)
        {
            var t = new Type { Name = n, Mass = m, S = s, Span = b, P = pkw * 1000f, Vmax = vkmh / 3.6f, Torp = torp, AR = b * b / s };
            // CD0 from level flight at full power and top speed, loaded (with the torpedo)
            float q = 0.5f * Rho * t.Vmax * t.Vmax, W = m * G;
            float D = Eta * t.P / t.Vmax, Di = W * W / (q * s * Mathf.PI * E * t.AR), Dt = torp > 0f ? q * TorpCdA : 0f;
            t.Cd0 = Mathf.Max(0.012f, (D - Di - Dt) / (q * s));
            types[n] = t; return t;
        }
        static bool init;
        static void Init()
        {
            if (init) return; init = true;
            Def("short_184", 2433f, 63.9f, 19.36f, 194f, 142f, 367f);
            Def("sopwith_camel", 694f, 21.5f, 8.20f, 112f, 196f, 0f);
            Def("swordfish_i", 3438f, 56.4f, 13.87f, 510f, 230f, 760f);
            Def("albacore", 4745f, 57.9f, 15.24f, 840f, 259f, 760f);
            Def("beaufort", 9629f, 46.7f, 17.63f, 1680f, 423f, 728f);
            Def("hampden_tb", 8508f, 62.1f, 21.08f, 1500f, 398f, 702f);
            Def("beaufighter_tf", 11521f, 46.7f, 17.63f, 2386f, 488f, 817f);
        }
        internal static Type Of(string model) { Init(); return model != null && types.TryGetValue(model, out var t) ? t : null; }
        internal static string Describe()
        {
            Init(); var sb = new System.Text.StringBuilder();
            foreach (var t in types.Values) sb.Append($"{t.Name}: {t.Mass:0} kg, {t.S:0.0} m2, AR {t.AR:0.0}, {t.P / 1000f:0} kW, {t.Vmax * 3.6f:0} km/h, CD0 {t.Cd0:0.000}, torpedo {t.Torp:0} kg; ");
            return sb.ToString();
        }

        // state of one aircraft
        internal class State { public Type T; public float Mass, V, Lex, Vy, Hop; public bool Torp; }
        internal static State Start(string model, bool torpedo)
        {
            var t = Of(model); if (t == null) return null;
            return new State { T = t, Mass = t.Mass - (torpedo ? 0f : t.Torp), V = 0.8f * t.Vmax, Torp = torpedo && t.Torp > 0f };
        }
        static float Drag(State s, float v, float lift)
        {
            float q = 0.5f * Rho * Mathf.Max(5f, v * v);
            return q * s.T.S * s.T.Cd0 + lift * lift / (q * s.T.S * Mathf.PI * E * s.T.AR) + (s.Torp ? q * TorpCdA : 0f);
        }
        static float Thrust(State s, float v) { return Eta * s.T.P / Mathf.Max(v, 0.5f * s.T.Vmax); }
        // the steepest climb (sin gamma) the excess power allows at the present speed
        internal static float MaxClimb(State s)
        {
            float W = s.Mass * G; return Mathf.Clamp((Thrust(s, s.V) - Drag(s, s.V, W)) / W, -0.4f, 0.5f);
        }
        // one step: sinGamma = the flight path's vertical component, vWant = the speed the pilot holds
        internal static void Step(State s, float sinGamma, float vWant, float dt)
        {
            float W = s.Mass * G, cosG = Mathf.Sqrt(Mathf.Max(0f, 1f - sinGamma * sinGamma));
            float th = Mathf.Clamp01(0.6f + (vWant - s.V) * 0.25f);
            float a = (Thrust(s, s.V) * th - Drag(s, s.V, W * cosG)) / s.Mass - G * sinGamma;
            // above the speed it should hold the pilot throttles back (th 0) - the drag slows it
            s.V = Mathf.Max(0.55f * s.T.Vmax, s.V + a * dt);
            // the lift excess after the drop: taken off in about a second; it lifts the aircraft
            if (s.Lex > 0f)
            {
                s.Vy += s.Lex / s.Mass * dt;
                s.Lex *= Mathf.Exp(-dt / 0.9f); if (s.Lex < 1f) s.Lex = 0f;
            }
            s.Vy *= Mathf.Exp(-dt / 1.6f);            // the pilot levels off again
            s.Hop += s.Vy * dt;
            s.Hop *= Mathf.Exp(-dt / 6f);             // and the gained height becomes part of the path over a few seconds
        }
        // the torpedo leaves the aircraft
        internal static void Release(State s)
        {
            if (s == null || !s.Torp) return;
            float W0 = s.Mass * G; s.Mass -= s.T.Torp; s.Torp = false;
            s.Lex = W0 - s.Mass * G;                  // the wing still carries the old weight
        }
    }
}

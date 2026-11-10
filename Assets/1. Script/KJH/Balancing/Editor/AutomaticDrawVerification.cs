using System;
using UnityEngine;

namespace KJH.Balancing
{
    public static class AutomaticDrawVerification
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        public static void Run()
        {
            var t = new PhysicalDrawTracker();
            Check(!t.Observe(1, true, false, true, 0), "Outside-only card counted");
            Check(!t.Observe(1, true, true, false, 1), "Deck sighting counted");
            Check(!t.Observe(1, true, true, false, 1.2f), "Stationary deck card counted");
            Check(!t.Observe(1, true, false, true, 2), "Occlusion counted without stable outside sighting");
            Check(t.Observe(1, true, false, true, 2.2f), "Stable deck exit not counted");
            t.Confirm(1);
            Check(!t.Observe(1, false, false, true, 2.4f), "Front/back repeat counted twice");
            Check(!t.Observe(1, true, true, false, 3), "Returned card counted twice");
            Check(!t.Observe(2, true, true, false, 3), "Second deck top counted early");
            t.Observe(2, true, true, false, 3.2f);
            Check(!t.Observe(2, true, false, false, 3.3f), "Boundary jitter counted");
            Check(!t.Observe(2, true, false, true, 3.4f), "Single outside frame counted");
            Check(!t.Observe(2, true, false, true, 4), "Stale outside sighting reused");
            Check(t.Observe(2, true, false, true, 4.2f), "Second physical card missed");
            t.Reset();
            Check(!t.Observe(1, true, false, true, 5), "Reset retained old deck sightings");
            t.Observe(1, true, true, false, 5.1f); t.Observe(1, true, true, false, 5.3f);
            t.Observe(1, false, false, true, 5.4f);
            Check(t.Observe(1, false, false, true, 5.6f), "New match cannot draw same card");
            PhysicalDrawVerification.Run();
            Debug.Log("AUTOMATIC_DRAW_VERIFICATION_PASS: positive deck-exit sightings; repeat/front-back/jitter/stale rejection; rematch reset; draw/fatigue integration");
        }
    }
}

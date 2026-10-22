using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the juice layer: particles and floating texts die out instead
/// of leaking, stay finite, respect their caps — and the camera shake
/// accumulates, decays, and washes all the way back out of the view matrix.
///
/// The failure mode this guards against is the quiet one: a particle system
/// that never removes anything runs at 60fps in a test and at 12fps in front
/// of a player three bursts later, and a shake that doesn't decay leaves the
/// game permanently vibrating after one tackle.
/// </summary>
public static class FxChecks
{
    /// <summary>Runs every juice check.</summary>
    public static void Run()
    {
        Check.Section("Juice: particles, popups and camera shake");

        CheckParticles();
        CheckTexts();
        CheckCaps();
        CheckShake();
    }

    private static void CheckParticles()
    {
        var fx = new FxSystem();
        var rng = new Random(1);
        var origin = new Vector3(1f, 0.5f, 2f);

        fx.Burst(origin, Color.Red, count: 20, speed: 2f, life: 1f, rng);
        Check.That("a burst spawns the specks asked for",
            fx.Particles.Count == 20, $"{fx.Particles.Count} particles");

        Check.That("every speck starts whole",
            fx.Particles.All(p => p.Life > 0f && p.Life <= p.MaxLife && p.Age >= 0f),
            string.Join(", ", fx.Particles.Select(p => $"{p.Life:F2}/{p.MaxLife:F2}")));

        // A burst spreads out from where it happened rather than teleporting.
        fx.Update(0.1f);
        Check.That("specks move away from the burst",
            fx.Particles.All(p => (p.Position - origin).Length() < 2f &&
                                  (p.Position - origin).Length() > 0.01f),
            "bursts did not travel");

        // Everything dies: long past the longest life, nothing is left.
        for (int i = 0; i < 120; i++) fx.Update(0.1f);
        Check.That("every speck dies out (no leak)",
            fx.Particles.Count == 0, $"{fx.Particles.Count} survivors");

        // And over a life of updates nothing went non-finite (a NaN position
        // would render as a full-screen smear or vanish silently).
        fx.Burst(origin, Color.White, count: 30, speed: 3f, life: 0.8f, rng);
        bool finite = true;
        for (int i = 0; i < 30 && finite; i++)
        {
            fx.Update(0.05f);
            finite = fx.Particles.All(p =>
                float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) &&
                float.IsFinite(p.Position.Z) && float.IsFinite(p.Velocity.Length()));
        }
        Check.That("positions stay finite for a whole life", finite, "NaN found");

        // And Clear() (round restart) drops everything at once.
        fx.Clear();
        Check.That("Clear() empties the field",
            fx.Particles.Count == 0 && fx.Texts.Count == 0, "not empty");
    }

    private static void CheckTexts()
    {
        var fx = new FxSystem();
        fx.Text(new Vector3(2f, 1f, 2f), "+1 secured", Color.Lime, life: 1f);
        var start = fx.Texts[0].WorldPos;

        Check.That("a popup starts where it was placed",
            fx.Texts.Count == 1 && start == new Vector3(2f, 1f, 2f),
            $"at {start}");

        fx.Update(0.25f);
        Check.That("a popup rises as it fades",
            fx.Texts[0].WorldPos.Y > start.Y && fx.Texts[0].Life < 1f &&
            fx.Texts[0].Strength > 0f && fx.Texts[0].Strength < 1f,
            $"pos {fx.Texts[0].WorldPos}, strength {fx.Texts[0].Strength:F2}");

        fx.Update(1.0f);
        Check.That("a popup expires", fx.Texts.Count == 0, $"{fx.Texts.Count} left");
    }

    private static void CheckCaps()
    {
        var fx = new FxSystem();
        var rng = new Random(2);

        for (int i = 0; i < 30; i++)
            fx.Burst(Vector3.Zero, Color.White, count: 40, speed: 2f, life: 5f, rng);
        Check.That("particles are capped",
            fx.Particles.Count <= 400, $"{fx.Particles.Count} particles");

        for (int i = 0; i < 60; i++) fx.Text(Vector3.Zero, "x", Color.White, life: 9f);
        Check.That("popups are capped (newest survive)",
            fx.Texts.Count <= 40, $"{fx.Texts.Count} texts");
    }

    // The camera's shake: shove it, watch it burn off, and confirm that the
    // framing maths underneath never moved — and that once the shake is gone
    // the view matrix is exactly what it was before, so jitter can't leave a
    // permanent offset behind.
    private static void CheckShake()
    {
        const float cellSize = 2f;
        var camera = new Camera(16f / 9f, 36f, 24f, cellSize);
        var input = InputState.Neutral;
        var frame = new GameTime(TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0));
        var playerPos = new Vector3(10f, 0f, 8f);

        camera.Update(frame, input, playerPos, 0f);
        var steadyView = camera.View;
        var focusBefore = camera.FocusPoint;

        camera.AddShake(0.6f);
        Check.That("a shove registers", camera.Shake > 0.5f, $"shake {camera.Shake:F2}");

        camera.AddShake(0.6f);
        Check.That("shake accumulates but is capped",
            Math.Abs(camera.Shake - Camera.MaxShake) < 0.001f, $"shake {camera.Shake:F2}");

        // One shaken frame: the matrix must actually differ (jitter is on),
        // while the focus point — what the follow/clamp maths measures —
        // must not have budged.
        camera.Update(frame, input, playerPos, 0f);
        Check.That("a shaken frame moves the view",
            camera.View != steadyView, "view unchanged");
        Check.That("the shake does not move the framing",
            camera.FocusPoint == focusBefore,
            $"{camera.FocusPoint} vs {focusBefore}");

        // Burn the rest off: ShakeDecay units/sec, so one second at MaxShake
        // is more than enough.
        for (int i = 0; i < 70; i++) camera.Update(frame, input, playerPos, 0f);
        Check.That("the shake burns off",
            camera.Shake <= 0f, $"shake {camera.Shake:F3}");

        camera.Update(frame, input, playerPos, 0f);
        Check.That("the view is exactly steady again once the shake is gone",
            camera.View == steadyView, "leftover offset");

        // No shake ever added: a camera is deterministic frame to frame,
        // which is what lets the framing checks compare matrices at all.
        var calm = new Camera(16f / 9f, 36f, 24f, cellSize);
        calm.Update(frame, input, playerPos, 0f);
        var first = calm.View;
        calm.Update(frame, input, playerPos, 0f);
        Check.That("an unshaken camera is deterministic",
            calm.View == first, "view drifted without input");
    }
}

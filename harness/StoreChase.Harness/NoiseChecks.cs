using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the noise the player makes and what the cats do about it.
///
/// The stealth loop is only real if every link holds: standing still is
/// silence, movement pops once per cell, loudness buys range, ears decay
/// with age, species differ, and SharedAwareness folds a hearing into the
/// same vagueness a shouted warning carries — without ever overriding a
/// sighting.
///
/// The last one matters most for the balance contract: the headless round
/// simulation calls Update(playerCell, map) with no noise at all, so every
/// check here that passes no noise must reproduce exactly the old behaviour.
/// </summary>
public static class NoiseChecks
{
    /// <summary>Runs every noise check.</summary>
    public static void Run()
    {
        CheckSystem();
        CheckPipeline();
    }

    private static void CheckSystem()
    {
        Check.Section("Noise: footsteps and ears");

        var noise = new NoiseSystem();

        // A player standing still makes no sound at all.
        Check.That("silence is really silent",
            noise.LoudestHeardBy(new GridPos(5, 7), 10f) == null && noise.CurrentLoudness == 0f);

        // One footstep at unit loudness is heard by a Shop Cat within its
        // five-cell radius, and the fix is the cell it came from.
        var stepCell = new GridPos(5, 4);
        noise.Footstep(stepCell, NoiseSystem.StepLoudness);
        var heard = noise.LoudestHeardBy(new GridPos(5, 7), hearRadius: 5f);
        Check.That("a footstep in range is heard",
            heard?.Cell == stepCell, $"heard {heard?.Cell.X},{heard?.Cell.Y}");
        Check.That("a heard footstep is a fix, never an exact bearing",
            heard is { } h && h.Vagueness >= 1f, $"vagueness {heard?.Vagueness:F2}");

        Check.That("a footstep out of range is not heard",
            noise.LoudestHeardBy(new GridPos(16, 10), 5f) == null);

        // Loudness buys range: the same cat position hears a clatter it
        // would never hear as a footstep.
        var clatter = new NoiseSystem();
        clatter.Clatter(stepCell, Game1.SlipClatterLoudness);
        Check.That("a plain footstep does not carry to mid-store",
            noise.LoudestHeardBy(new GridPos(13, 4), 5f) == null);
        Check.That("a clatter carries twice as far",
            clatter.LoudestHeardBy(new GridPos(13, 4), 5f) != null);

        // Ears fade with age, and forget entirely when the pop dies.
        var fading = new NoiseSystem();
        var nearCat = new GridPos(5, 6);
        fading.Footstep(stepCell, NoiseSystem.StepLoudness);
        fading.Update(NoiseSystem.PopLife * 0.5f);
        Check.That("a half-faded footstep is still audible next door",
            fading.LoudestHeardBy(nearCat, 5f) != null);
        fading.Update(NoiseSystem.PopLife * 0.5f);
        Check.That("a footstep fades out rather than stopping",
            fading.LoudestHeardBy(nearCat, 5f) == null
            && fading.ActivePops == 0 && fading.CurrentLoudness == 0f);

        // Every pop forgets what it was; Clear starts the round in silence.
        fading.Footstep(stepCell, 1f);
        fading.Clear();
        Check.That("Clear drops every pop",
            fading.LoudestHeardBy(nearCat, 50f) == null);

        // Per-species ears: the napping species sleep through a walk-by,
        // the working ones do not. Distances chosen between the radii.
        var pop = new NoiseSystem();
        pop.Footstep(stepCell, NoiseSystem.StepLoudness);
        var catAtFour = new GridPos(stepCell.X + 4, stepCell.Y);
        Check.That("a Moggy does not hear a footstep four cells away",
            pop.LoudestHeardBy(catAtFour, CritterProfile.For(CritterKind.Moggy).HearingRadius) == null);
        Check.That("a Lounge Lizard does not either",
            pop.LoudestHeardBy(catAtFour, CritterProfile.For(CritterKind.LoungeLizard).HearingRadius) == null);
        Check.That("a Shop Cat does",
            pop.LoudestHeardBy(catAtFour, CritterProfile.For(CritterKind.ShopCat).HearingRadius) != null);
        Check.That("a Mouser does, furthest of all",
            pop.LoudestHeardBy(catAtFour, CritterProfile.For(CritterKind.Mouser).HearingRadius) != null);

        float Moggy = CritterProfile.For(CritterKind.Moggy).HearingRadius;
        float Lizard = CritterProfile.For(CritterKind.LoungeLizard).HearingRadius;
        float Shop = CritterProfile.For(CritterKind.ShopCat).HearingRadius;
        float Kitten = CritterProfile.For(CritterKind.Kitten).HearingRadius;
        float Mouser = CritterProfile.For(CritterKind.Mouser).HearingRadius;
        Check.That("hearing ranks the species from deaf to sharp",
            Moggy < Lizard && Lizard < Shop && Shop < Kitten && Kitten < Mouser,
            $"moggy {Moggy}, lizard {Lizard}, shop {Shop}, kitten {Kitten}, mouser {Mouser}");
    }

    private static void CheckPipeline()
    {
        Check.Section("Noise: what the cats do about it");

        var map = new GridMap(StoreLayouts.Standard);
        var playerCell = new GridPos(5, 4);

        // A cat that cannot see the player: no sightings, just footsteps.
        var ears = MakeCritter(new GridPos(5, 7), map);
        ears.CanSeePlayer = false;

        var noise = new NoiseSystem();
        noise.Footstep(playerCell, NoiseSystem.StepLoudness);
        var awareness = new SharedAwareness();
        awareness.Reset(new[] { ears });
        awareness.Update(playerCell, map, noise);

        Check.That("a cat with no line of sight still hears the player",
            ears.PlayerAwareness > 0f, $"awareness {ears.PlayerAwareness:F2}");
        Check.That("the heard fix points at the player's cell",
            ears.LastSeenPlayerCell == playerCell,
            $"last seen {ears.LastSeenPlayerCell?.X},{ears.LastSeenPlayerCell?.Y}");

        // The balance contract: no noise means the old behaviour, exactly —
        // this is the call shape the round simulation makes.
        var silent = MakeCritter(new GridPos(5, 7), map);
        silent.CanSeePlayer = false;
        var awarenessNull = new SharedAwareness();
        awarenessNull.Reset(new[] { silent });
        awarenessNull.Update(playerCell, map);
        Check.That("passing no noise plays a silent player (round-sim parity)",
            silent.PlayerAwareness == 0f && !silent.LastSeenPlayerCell.HasValue);

        // Sight still wins: a cat that can see the player keeps its exact
        // fix even with a louder noise somewhere else.
        var spotter = MakeCritter(new GridPos(5, 5), map);
        spotter.CanSeePlayer = map.HasLineOfSight(spotter.Cell, playerCell);
        Check.That("the spotter can see the player", spotter.CanSeePlayer);

        var loudElsewhere = new NoiseSystem();
        loudElsewhere.Clatter(new GridPos(16, 10), 3f);
        var awarenessSight = new SharedAwareness();
        awarenessSight.Reset(new[] { spotter });
        awarenessSight.Update(playerCell, map, loudElsewhere);
        Check.That("a sighting beats a louder noise",
            spotter.PlayerAwareness == 0f && spotter.LastSeenPlayerCell == playerCell,
            $"awareness {spotter.PlayerAwareness:F2}, last seen {spotter.LastSeenPlayerCell?.X},{spotter.LastSeenPlayerCell?.Y}");

        // A better fix replaces an old one; a worse one does not.
        var tracker = MakeCritter(new GridPos(5, 7), map);
        tracker.CanSeePlayer = false;
        var tracking = new SharedAwareness();
        tracking.Reset(new[] { tracker });

        tracking.Update(playerCell, map, Pop(new NoiseSystem(), new GridPos(5, 4)));
        float first = tracker.PlayerAwareness;
        tracking.Update(playerCell, map, Pop(new NoiseSystem(), new GridPos(5, 6)));
        float second = tracker.PlayerAwareness;
        tracking.Update(playerCell, map, Pop(new NoiseSystem(), new GridPos(5, 4)));
        float third = tracker.PlayerAwareness;

        Check.That("a closer footstep improves the fix",
            second < first, $"first {first:F2}, then {second:F2}");
        Check.That("a worse footstep does not undo it",
            third == second, $"kept {third:F2} against {first:F2}");

        // End to end: the AI takes a cat that only ever heard the player and
        // sends it towards the sound.
        int stepsToward = 0, steps = 40;
        for (int i = 0; i < steps; i++)
        {
            var cat = MakeCritter(new GridPos(5, 7), map);
            cat.CanSeePlayer = false;
            var aware = new SharedAwareness();
            aware.Reset(new[] { cat });
            aware.Update(playerCell, map, new NoiseSystem());
            var heard = new NoiseSystem();
            heard.Footstep(playerCell, NoiseSystem.StepLoudness);
            aware.Update(playerCell, map, heard);

            var ai = new SmartAnimalAI(cat.Profile, new Random(i));
            var ctx = new CritterContext(
                CurrentCell: cat.Cell, PlayerCell: new GridPos(16, 10), Map: map,
                DeltaTime: 0.1f, CanSeePlayer: false, BeingWatched: false, Ball: null,
                LootUnderfoot: null, LootTarget: null, LootTargetItem: null,
                Carrying: false, StolenCount: 0, AlarmStage: AlarmStage.Calm,
                PlayerAwareness: cat.PlayerAwareness, LastSeenPlayerCell: cat.LastSeenPlayerCell);
            var next = ai.DecideNextMove(ctx);
            if (next.HasValue && Distance(next.Value, playerCell) < Distance(cat.Cell, playerCell))
                stepsToward++;
        }
        Check.That("a cat that only heard the player heads for the sound",
            stepsToward > steps / 2, $"{stepsToward}/{steps} steps toward the noise");
    }

    private static Critter MakeCritter(GridPos cell, GridMap map)
    {
        var profile = CritterProfile.For(CritterKind.ShopCat);
        var ai = new SmartAnimalAI(profile, new Random(0));
        return new Critter(cell, 2f, ai, profile);
    }

    private static NoiseSystem Pop(NoiseSystem noise, GridPos cell)
    {
        noise.Footstep(cell, NoiseSystem.StepLoudness);
        return noise;
    }

    private static int Distance(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}

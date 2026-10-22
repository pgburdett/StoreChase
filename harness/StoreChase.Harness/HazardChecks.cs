using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the hazards: spill and fire-alarm placement, the player's skid
/// (which reinstates the old "releasing the keys doesn't stop you" bug as a
/// bounded status effect), the cat trip that drops loot, and the fire
/// alarm's one-shot scatter.
///
/// The skid is the one worth guarding carefully: it is deliberately a
/// re-introduced bug, so nothing in the code will ever flag it as wrong —
/// except a test that walks, lets go, and insists the player keeps going.
/// </summary>
public static class HazardChecks
{
    private const float CellSize = 2f;

    /// <summary>Runs every hazard check.</summary>
    public static void Run()
    {
        Check.Section("Hazards: spills, skids and the fire alarm");

        var map = new GridMap(StoreLayouts.Standard);
        var start = StoreLayouts.Standard.PlayerStart;
        var avoid = new[] { new GridPos(1, 1) };

        var hazards = new HazardField(CellSize);
        hazards.Spawn(map, start, new Random(11), spillCount: 2, alsoAvoid: avoid);

        CheckSpawn(map, start, avoid, hazards);
        CheckSkid(map, start);
        CheckSpeedWhileSlipping(map, start);
        CheckCatTrip(map, start);
        CheckFireAlarm(map, start);
    }

    // Two spills and one alarm, all on floor, off the counter, away from the
    // start, and never on a cell the caller asked to avoid.
    private static void CheckSpawn(GridMap map, GridPos start, GridPos[] avoid, HazardField hazards)
    {
        Check.That("a round gets two spills", hazards.Spills.Count == 2,
            $"{hazards.Spills.Count} spills");
        Check.That("a round gets a fire alarm", hazards.FireAlarmCell != null,
            hazards.FireAlarmCell?.ToString() ?? "none");

        bool floorOK = hazards.Spills.All(c =>
            map.IsWalkable(c.X, c.Y) &&
            !map.IsCounter(c.X, c.Y) &&
            Math.Abs(c.X - start.X) + Math.Abs(c.Y - start.Y) > 3 &&
            !avoid.Contains(c));
        Check.That("spills land on open floor, away from the start and off the counter",
            floorOK, string.Join(", ", hazards.Spills));

        var alarm = hazards.FireAlarmCell!.Value;
        Check.That("the fire alarm sits on a shelf edge (wall-mounted), not a spill",
            hazards.IsFireAlarm(alarm) &&
            !hazards.IsSpill(alarm) &&
            map.ShelfEdgeCells().Contains(alarm) &&
            !map.IsCounter(alarm.X, alarm.Y),
            $"alarm at {alarm.X},{alarm.Y}");

        Check.That("spills do not stack on each other",
            hazards.Spills[0] != hazards.Spills[1],
            $"{hazards.Spills[0]} vs {hazards.Spills[1]}");
    }

    // The skid: walk, let go of the keys, and the player must keep walking
    // until the slip runs out — then stop dead.
    private static void CheckSkid(GridMap map, GridPos start)
    {
        var player = new Player(start, CellSize);
        var frame = new GameTime(TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0));

        // One frame of eastward input sets the direction, then the keys go
        // away and the skid begins.
        player.Update(frame, map, new Vector2(1f, 0f), freeMove: false);
        var cellAfterInput = player.Cell;
        bool startedSlip = player.ApplySlip(1.0f);
        Check.That("hitting a spill starts a slip", startedSlip && player.Slipping,
            $"slip? {player.Slipping}");

        // A second application while already slipping refreshes rather than
        // re-triggering (no sound spam for standing on one).
        Check.That("re-entering a slip doesn't re-trigger it",
            !player.ApplySlip(1.0f) && player.Slipping, "re-triggered");

        // Keys released: glide anyway. (The old bug, on purpose.)
        for (int i = 0; i < 20; i++) player.Update(frame, map, Vector2.Zero, freeMove: false);
        Check.That("a skidding player keeps moving after the keys are released",
            player.Cell != cellAfterInput,
            $"glided from {cellAfterInput.X},{cellAfterInput.Y} to {player.Cell.X},{player.Cell.Y} with no input");

        // Wait the slip out, then move to zero again — must stop.
        for (int i = 0; i < 70; i++) player.Update(frame, map, Vector2.Zero, freeMove: false);
        Check.That("the slip wears off", !player.Slipping, "still slipping");
        var settled = player.Cell;
        for (int i = 0; i < 20; i++) player.Update(frame, map, Vector2.Zero, freeMove: false);
        Check.That("a dry player stops when the keys are released",
            player.Cell == settled,
            $"glided on to {player.Cell.X},{player.Cell.Y} from {settled.X},{settled.Y}");
    }

    // The skid is also faster (you're sliding, not walking). Measured as
    // frames for the first tile, plain vs slipping.
    private static void CheckSpeedWhileSlipping(GridMap map, GridPos start)
    {
        int plain = FramesToCrossFirstTile(map, start, slip: false);
        int slipping = FramesToCrossFirstTile(map, start, slip: true);

        Check.That("slipping is faster than walking", slipping > 0 && slipping < plain,
            $"plain {plain} frames, slipping {slipping} frames");
    }

    private static int FramesToCrossFirstTile(GridMap map, GridPos start, bool slip)
    {
        var player = new Player(start, CellSize);
        var frame = new GameTime(TimeSpan.FromSeconds(1.0 / 60.0), TimeSpan.FromSeconds(1.0 / 60.0));
        var target = new GridPos(start.X + 1, start.Y);

        for (int i = 1; i <= 120; i++)
        {
            player.Update(frame, map, new Vector2(1f, 0f), freeMove: false);
            if (i == 1 && slip) player.ApplySlip(1.0f);
            if (player.Cell == target) return i;
        }
        return -1;
    }

    // A cat landing on a spill drops the goods and sits it out — and the
    // tank just walks through.
    private static void CheckCatTrip(GridMap map, GridPos start)
    {
        var hazards = new HazardField(CellSize);
        hazards.Spawn(map, start, new Random(4));
        var spill = hazards.Spills[0];

        // A Kitten carrying loot, standing on the spill.
        var items = new ItemField(map, CellSize, new Random(5));
        items.Spawn(3, start);
        var loot = items.Items.First();
        var kittenProfile = CritterProfile.For(CritterKind.Kitten);
        var kitten = new Critter(spill, CellSize, new SmartAnimalAI(kittenProfile, new Random(1)), kittenProfile);
        items.TakeByCritter(loot, kitten);
        kitten.Carrying = true;

        bool tripped = hazards.TripCritter(kitten, items);
        Check.That("a cat slips on a spill",
            tripped && kitten.Stunned, $"tripped {tripped}, stunned {kitten.Stunned}");
        Check.That("a slipping cat drops its loot where it fell",
            !kitten.Carrying && loot.IsLoose && loot.Cell == spill,
            $"state {loot.State} at {loot.Cell.X},{loot.Cell.Y}");

        // Off the spill: nothing happens.
        var dry = new GridPos(start.X, start.Y);
        var walker = new Critter(dry, CellSize, new SmartAnimalAI(kittenProfile, new Random(1)), kittenProfile);
        Check.That("a cat on dry floor keeps its footing",
            !hazards.TripCritter(walker, items), "tripped anyway");

        // The Moggy's tolerance is physics, not just balls: it plows through.
        var moggyProfile = CritterProfile.For(CritterKind.Moggy);
        var moggy = new Critter(spill, CellSize, new SmartAnimalAI(moggyProfile, new Random(2)), moggyProfile);
        var loot2 = items.Items.First(i => i.IsLoose);
        items.TakeByCritter(loot2, moggy);
        moggy.Carrying = true;

        Check.That("a Moggy plows straight through a spill",
            !hazards.TripCritter(moggy, items) && !moggy.Stunned && moggy.Carrying,
            $"stunned {moggy.Stunned}, carrying {moggy.Carrying}");
    }

    // The fire alarm: standing on the wrong cell does nothing, the real pull
    // scatters and dumps and spikes, and it is one-shot.
    private static void CheckFireAlarm(GridMap map, GridPos start)
    {
        var hazards = new HazardField(CellSize);
        hazards.Spawn(map, start, new Random(6));
        var alarmCell = hazards.FireAlarmCell!.Value;

        var items = new ItemField(map, CellSize, new Random(7));
        items.Spawn(3, start);
        var alarm = new AlarmSystem();

        var carrierProfile = CritterProfile.For(CritterKind.ShopCat);
        var carrier = new Critter(alarmCell, CellSize, new SmartAnimalAI(carrierProfile, new Random(1)), carrierProfile);
        var looted = items.Items.First();
        items.TakeByCritter(looted, carrier);
        carrier.Carrying = true;

        var bystanderProfile = CritterProfile.For(CritterKind.Mouser);
        var bystander = new Critter(new GridPos(1, 1), CellSize, new SmartAnimalAI(bystanderProfile, new Random(2)), bystanderProfile);
        var critters = new[] { carrier, bystander };

        // Not on the alarm cell: refused (and the caller falls through to the
        // power-up pocket on the same key).
        bool wrongSpot = hazards.TryPullFireAlarm(new GridPos(5, 4), critters, items, alarm, 0.15f, out _);
        Check.That("pressing Use away from the alarm does nothing",
            !wrongSpot && !hazards.FireAlarmPulled, "pulled from afar");

        // The real pull.
        bool pulled = hazards.TryPullFireAlarm(alarmCell, critters, items, alarm, 0.15f, out int scattered);
        Check.That("pulling the alarm reports the cats it caught",
            pulled && scattered == 2 && hazards.FireAlarmPulled,
            $"pulled {pulled}, scattered {scattered}");

        Check.That("the alarm panics every cat",
            critters.All(c => c.PanicSeconds > 0f && c.Startled),
            string.Join(", ", critters.Select(c => $"{c.PanicSeconds:F1}s/{c.Startled}")));

        Check.That("the panic makes a carrier drop the goods",
            !carrier.Carrying && looted.IsLoose && looted.Cell == alarmCell,
            $"state {looted.State} at {looted.Cell.X},{looted.Cell.Y}");

        Check.That("pulling the alarm costs a fixed chunk of the meter",
            Math.Abs(alarm.Level - 0.15f) < 0.001f, $"level {alarm.Level:F3}");

        // One-shot.
        bool again = hazards.TryPullFireAlarm(alarmCell, critters, items, alarm, 0.15f, out _);
        Check.That("the fire alarm is one-shot per round",
            !again && Math.Abs(alarm.Level - 0.15f) < 0.001f, $"level {alarm.Level:F3}");

        // A fresh round re-arms it.
        hazards.Spawn(map, start, new Random(8));
        Check.That("a new round re-arms the alarm", !hazards.FireAlarmPulled,
            "still pulled");
    }
}

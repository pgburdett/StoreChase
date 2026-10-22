using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// Checks on the customers and the shoplifter: pure movement, the brain's
/// decisions, and the ItemField's customer possession rules.
/// </summary>
public static class CustomerChecks
{
    public static void Run()
    {
        Check.Section("Customers: movement");
        CheckMovement();

        Check.Section("Customers: the brain");
        CheckBrain();

        Check.Section("Customers: possession");
        CheckPossession();
    }

    private static void CheckMovement()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var cells = map.WalkableCells().ToList();
        var start = cells[0];
        var target = cells[1];
        var rng = new Random(1);
        var c = new Customer(start, 2f, shoplifter: false, rng);

        c.SetTarget(target, map);
        Check.That("a customer accepts a walkable target", c.Target == target);

        c.Update(10f, map); // enough to finish
        Check.That("a customer arrives at its target", c.Cell == target && c.JustArrived);
        Check.That("JustArrived clears on the next frame", !JustArrivedAfterUpdate(c, map));

        // An invalid (wall) target is ignored.
        var c2 = new Customer(start, 2f, false, new Random(2));
        var wall = new GridPos(0, 0); // Standard row 0 is all wall
        c2.SetTarget(wall, map);
        Check.That("a wall cell is not a target", c2.Target == null);
    }

    private static void CheckBrain()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var rng = new Random(3);
        var items = new ItemField(map, 2f, new Random(4));
        items.Spawn(3, new GridPos(16, 10));

        // Shoplifter with no loot and a loose item far from the player: it
        // heads for the item.
        var loose = items.Items.First(i => i.IsLoose);
        var shoplifter = new Customer(new GridPos(3, 3), 2f, shoplifter: true, new Random(5));
        // make sure it's far from the player so stealing is allowed
        CustomerBrain.Decide(shoplifter, map, new GridPos(16, 10), items, rng, new[] { new GridPos(1, 1) });
        var walks = map.DistanceMapFrom(loose.Cell);
        Check.That("a shoplifter heads for loose loot when unwatched",
            shoplifter.Target.HasValue
            && walks[shoplifter.Target.Value.X, shoplifter.Target.Value.Y] < walks[3, 3]);

        // A shoplifter that is already carrying makes for an exit.
        var carrying = new Customer(new GridPos(3, 3), 2f, shoplifter: true, new Random(6));
        carrying.Carrying = loose;
        var exits = new[] { new GridPos(1, 1) };
        CustomerBrain.Decide(carrying, map, new GridPos(16, 10), items, new Random(7), exits);
        Check.That("a carrying shoplifter heads for an exit",
            carrying.Target == exits[0]);

        // A caught shoplifter (Leaving) flees the player.
        var fleeing = new Customer(new GridPos(5, 5), 2f, shoplifter: true, new Random(8));
        fleeing.Leaving = true;
        CustomerBrain.Decide(fleeing, map, new GridPos(5, 6), items, new Random(9), exits);
        Check.That("a fleeing shoplifter steps away from the player",
            fleeing.Target.HasValue
            && Manhattan(fleeing.Target.Value, new GridPos(5, 6)) > Manhattan(new GridPos(5, 5), new GridPos(5, 6)));

        // Escaped customers do nothing.
        var gone = new Customer(new GridPos(3, 3), 2f, shoplifter: true, new Random(10));
        gone.MarkEscaped();
        CustomerBrain.Decide(gone, map, new GridPos(16, 10), items, rng, exits);
        Check.That("an escaped customer is inert", gone.Target == null);
    }

    private static void CheckPossession()
    {
        var map = new GridMap(StoreLayouts.Standard);
        var items = new ItemField(map, 2f, new Random(11));
        items.Spawn(2, new GridPos(16, 10));
        var item = items.Items.First(i => i.IsLoose);

        var customer = new Customer(new GridPos(5, 5), 2f, shoplifter: true, new Random(12));

        items.TakeByCustomer(item, customer);
        customer.Carrying = item;
        Check.That("TakeByCustomer marks the item carried",
            item.State == LootState.Carried && item.CustomerCarrier == customer);
        Check.That("a shoplifted item counts as stolen",
            items.StolenCount >= 1);

        // Drop on a non-counter cell.
        var dropCell = new GridPos(6, 6);
        // ensure walkable
        items.DropFromCustomer(customer, dropCell);
        customer.Carrying = null;
        Check.That("DropFromCustomer puts it back loose",
            item.State == LootState.Loose && item.CustomerCarrier == null);

        // Drop on a counter cell secures it.
        // find a counter cell
        GridPos counterCell = default;
        bool found = false;
        for (int x = 0; x < map.Width && !found; x++)
            for (int y = 0; y < map.Height && !found; y++)
                if (map.IsCounter(x, y)) { counterCell = new GridPos(x, y); found = true; }
        var item2 = items.Items.First(i => i.IsLoose);
        items.TakeByCustomer(item2, customer);
        customer.Carrying = item2;
        items.DropFromCustomer(customer, counterCell);
        customer.Carrying = null;
        Check.That("a shoplifter caught at the counter is stopped",
            item2.IsSecured);
    }

    // Helper: update then read JustArrived (which clears on next Update call).
    private static bool JustArrivedAfterUpdate(Customer c, GridMap map)
    {
        c.Update(0.016f, map);
        return c.JustArrived;
    }

    private static int Manhattan(GridPos a, GridPos b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
}

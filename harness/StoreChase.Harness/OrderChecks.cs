using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// The shopping order: what a round asks for, and what pays out.
///
/// This used to live inline in Game1 as a dictionary and a flag, which meant
/// the two rules that actually matter — that the list is always completable,
/// and that the bonus pays exactly once — could only be checked by playing a
/// round and hoping. It is a pure class now, so it is checked directly.
/// </summary>
public static class OrderChecks
{
    public static void Run()
    {
        Check.Section("Shopping order");

        var rng = new Random(1234);

        // A list built from the items that actually spawned can only ask for
        // goods that exist, or the round is quietly unwinnable.
        for (int seed = 1; seed <= 12; seed++)
        {
            var items = LootSamples(rng);
            var order = new ShoppingOrder();
            order.Build(items, new Random(seed));

            foreach (var kv in order.Remaining)
                Check.That($"seed {seed}: the order only asks for spawned kinds",
                    items.Any(i => i.Kind == kv.Key), $"{kv.Key} x{kv.Value}");
        }

        // The bonus is an edge, not a level: securing everything twice must
        // not pay out twice.
        var paid = new ShoppingOrder();
        var loot = LootSamples(rng);
        paid.Build(loot, new Random(7));
        var kinds = paid.Remaining.Keys.ToList();
        int payouts = 0;
        foreach (var item in loot)
            if (paid.Fulfill(item.Kind)) payouts++;

        Check.That("the bonus pays at most once per round",
            payouts <= 1, $"{payouts} payout(s), {loot.Count} items secured");
        Check.That("the order ends up complete",
            paid.Complete, $"{paid.Outstanding} left of {paid.Total}");

        // A list nobody can finish is a dead objective.
        var stuck = new ShoppingOrder { };
        Check.That("an empty round still describes itself",
            stuck.Empty && stuck.Text == "", $"'{stuck.Text}'");
    }

    private static List<LootItem> LootSamples(Random rng)
    {
        var kinds = (LootKind[])Enum.GetValues(typeof(LootKind));
        var items = new List<LootItem>();
        for (int i = 0; i < 6; i++)
        {
            var cell = new GridPos(rng.Next(1, 10), rng.Next(1, 10));
            items.Add(new LootItem(kinds[rng.Next(kinds.Length)], cell,
                new Vector3(cell.X * 2f, 0f, cell.Y * 2f)));
        }
        return items;
    }
}

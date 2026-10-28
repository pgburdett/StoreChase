using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

/// <summary>
/// A round's shopping list: the kinds of goods the store asked for, and how
/// many of each is still outstanding. Fulfilling the whole list pays a bonus.
///
/// <para>Pure state — no clock, no graphics, and an RNG handed in rather than
/// owned, so the bonus rules (what it asks for, when it pays out, that it pays
/// exactly once) can be simulated headlessly instead of being inferred from a
/// playthrough. The rules themselves live here rather than in Game1 so the HUD
/// only ever reads a finished string.</para>
/// </summary>
public sealed class ShoppingOrder
{
    private readonly Dictionary<LootKind, int> _remaining = new();
    private string _text = "";

    /// <summary>How many goods were asked for in total.</summary>
    public int Total { get; private set; }

    /// <summary>How many are still outstanding.</summary>
    public int Outstanding => _remaining.Values.Where(v => v > 0).Sum();

    /// <summary>True once every kind on the list has been fulfilled.</summary>
    public bool Complete { get; private set; }

    /// <summary>True when this round has no list at all.</summary>
    public bool Empty => _remaining.Count == 0;

    /// <summary>
    /// Picks up to <paramref name="maxKinds"/> kinds from what actually
    /// spawned, asking for about half of each. Built from the live item set
    /// so the order is always something the round can deliver.
    /// </summary>
    public void Build(IEnumerable<LootItem> items, Random rng, int maxKinds = 2, double acceptChance = 0.6)
    {
        _remaining.Clear();

        // Sorted before shuffling: a Dictionary's enumeration order is an
        // implementation detail, so two runs that got the same items should
        // not ask for different lists by accident of insertion order.
        var available = items.GroupBy(i => i.Kind)
                             .ToDictionary(g => g.Key, g => g.Count());
        foreach (var k in available.Keys.OrderBy(k => k.ToString(), StringComparer.Ordinal)
                                        .OrderBy(_ => rng.Next()))
        {
            if (_remaining.Count >= maxKinds) break;
            if (rng.NextDouble() < acceptChance) continue;
            _remaining[k] = Math.Max(1, available[k] / 2);
        }

        // Always ask for something: an empty list reads as a broken feature.
        if (_remaining.Count == 0 && available.Count > 0)
        {
            var first = available.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal).First();
            _remaining[first.Key] = Math.Max(1, first.Value / 2);
        }

        Total = _remaining.Values.Sum();
        Complete = false;
        RefreshText();
    }

    /// <summary>
    /// Counts one secured item towards the list.
    /// Returns true when this is the fulfilment that completed it — the
    /// caller pays the bonus once, on that edge and no other.
    /// </summary>
    public bool Fulfill(LootKind kind)
    {
        if (Complete || !_remaining.TryGetValue(kind, out int rem) || rem <= 0)
            return false;

        _remaining[kind] = rem - 1;
        if (_remaining.Values.Any(v => v > 0))
            return false;

        Complete = true;
        RefreshText();
        return true;
    }

    /// <summary>The HUD line, rebuilt only when the list changes.</summary>
    public string Text => _text;

    /// <summary>What is left to bring in, by kind. Read-only by convention.</summary>
    public IReadOnlyDictionary<LootKind, int> Remaining => _remaining;

    private void RefreshText()
    {
        if (_remaining.Count == 0) { _text = ""; return; }

        var parts = _remaining.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
                              .Select(kv => $"{kv.Key.ToString().ToLowerInvariant()} x{Math.Max(0, kv.Value)}");
        string tail = Complete ? "  done!" : $"  ({Outstanding} left)";
        _text = "Order: " + string.Join("  ", parts) + tail;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreChase;

public abstract class BaseEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
}

// Published once when a run begins — on initial startup and on every restart.
public sealed class GameStartedEvent : BaseEvent
{
}

// Published when the player wins the round — every item back behind the
// counter before the alarm filled. Carries the final time so subscribers
// (e.g. RunStats) don't have to reach back into Game1 for it.
public sealed class GameWonEvent : BaseEvent
{
    public float ElapsedTime { get; }

    public GameWonEvent(float elapsedTime)
    {
        ElapsedTime = elapsedTime;
    }
}

// Published when the alarm fills. Carries how much got secured, so a failed
// round can report "you got 4 of 6 back" rather than just a loss.
public sealed class GameLostEvent : BaseEvent
{
    public float ElapsedTime { get; }
    public int SecuredCount { get; }

    public GameLostEvent(float elapsedTime, int securedCount)
    {
        ElapsedTime = elapsedTime;
        SecuredCount = securedCount;
    }
}

// Published when a critter picks up an item. Carries the species so subscribers
// can tell whether one profile is doing all the thieving.
public sealed class ItemStolenEvent : BaseEvent
{
    public string? Species { get; }

    public ItemStolenEvent(string? species)
    {
        Species = species;
    }
}

// Published when an item makes it behind the counter.
public sealed class ItemSecuredEvent : BaseEvent
{
}

public class EventSystem
{
// 1. Thread-safe Singleton using Lazy<T>
    private static readonly Lazy<EventSystem> _instance =
        new Lazy<EventSystem>(() => new EventSystem());

    public static EventSystem Instance => _instance.Value;

// Internal interface to allow storing generic handlers in a non-generic list
    private interface ISubscription
    {
        void Invoke(BaseEvent e);
        object OriginalHandler { get; }
    }

// Concrete implementation of a subscription
    private class Subscription<TEvent> : ISubscription where TEvent : BaseEvent
    {
        public Action<TEvent> Handler { get; }
        public object OriginalHandler => Handler;

        public Subscription(Action<TEvent> handler)
        {
            Handler = handler;
        }

        public void Invoke(BaseEvent e)
        {
            Handler((TEvent)e);
        }
    }

    private readonly Dictionary<Type, List<ISubscription>> _subscriptions = new();
    private readonly object _lock = new object();

    private EventSystem()
    {
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : BaseEvent
    {
        lock (_lock)
        {
            Type eventType = typeof(TEvent);
            if (!_subscriptions.TryGetValue(eventType, out var handlers))
            {
                handlers = new List<ISubscription>();
                _subscriptions[eventType] = handlers;
            }

            handlers.Add(new Subscription<TEvent>(handler));
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : BaseEvent
    {
        lock (_lock)
        {
            Type eventType = typeof(TEvent);
            if (_subscriptions.TryGetValue(eventType, out var handlers))
            {
                // Find the subscription wrapper that holds the original handler reference.
                // Cast to the concrete delegate type so this is a value comparison
                // (delegate equality) rather than a reference comparison.
                var subscriptionToRemove = handlers.FirstOrDefault(s => (s.OriginalHandler as Action<TEvent>) == handler);
                if (subscriptionToRemove != null)
                {
                    handlers.Remove(subscriptionToRemove);
                }
            }
        }
    }

    public void Publish<TEvent>(TEvent eventData) where TEvent : BaseEvent
    {
        List<ISubscription> handlersToRun;

        lock (_lock)
        {
            Type eventType = typeof(TEvent);
            if (!_subscriptions.TryGetValue(eventType, out var handlers)) return;

        // Create a copy to allow handlers to unsubscribe themselves during the loop
        // without throwing "Collection Modified" exceptions.
            handlersToRun = handlers.ToList();
        }

        foreach (var subscription in handlersToRun)
        {
            try
            {
                subscription.Invoke(eventData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EventSystem] Error in {typeof(TEvent).Name}: {ex.Message}");
            }
        }
    }
}
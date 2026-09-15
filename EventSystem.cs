using System;
using System.Collections.Generic;
using System.Linq;

public abstract class BaseEvent
{
    public DateTime Timestamp { get; } = DateTime.Now;
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
// Find the subscription wrapper that holds the original handler reference
                var subscriptionToRemove = handlers.FirstOrDefault(s => s.OriginalHandler == handler);
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
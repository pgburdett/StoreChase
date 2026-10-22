using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase;

// A scrolling feed of recent gameplay events. New messages appear at the
// bottom and flash briefly to draw attention; old ones scroll up and off
// the top. Driven by the EventSystem, so it needs no per-frame input from
// the game — it just listens.
public sealed class MessageFeed
{
    private sealed class Entry
    {
        public string Text { get; }
        public float Age { get; set; }
        public Color Color { get; }

        public Entry(string text, float age, Color color)
        {
            Text = text;
            Age = age;
            Color = color;
        }
    }

    private readonly List<Entry> _entries = new();

    private const int MaxVisible = 4;
    private const float LineHeight = 20f;
    private const float FlashSeconds = 1.5f;
    private const float FadeSeconds = 6f;

    /// <summary>Subscribes to gameplay events and starts the message feed.</summary>
    public MessageFeed(EventSystem events)
    {
        events.Subscribe<ItemStolenEvent>(e => Add(
            $"{e.Species} stole something!", new Color(226, 84, 66)));
        events.Subscribe<ItemSecuredEvent>(_ => Add(
            "Item secured!", new Color(96, 176, 112)));
        events.Subscribe<GameStartedEvent>(_ => Add(
            "New round!", Color.White));
        events.Subscribe<PowerUpCollectedEvent>(e => Add(
            $"Picked up {PowerUpItem.Name(e.Kind)}", new Color(150, 200, 240)));
        events.Subscribe<PowerUpUsedEvent>(e => Add(
            $"Used {PowerUpItem.Name(e.Kind)}!", new Color(200, 160, 240)));
        events.Subscribe<PlayerSlippedEvent>(_ => Add(
            "Slippery!", new Color(140, 190, 240)));
        events.Subscribe<FireAlarmPulledEvent>(e => Add(
            $"Fire alarm! {e.Scattered} cat(s) scattered!", new Color(255, 140, 90)));
        events.Subscribe<CatFedEvent>(e => Add(
            $"{e.Species} gobbled the snack", new Color(245, 205, 130)));
        events.Subscribe<CatBefriendedEvent>(e => Add(
            $"{e.Species} is your friend now!", new Color(150, 220, 160)));
        events.Subscribe<ShiftEventFiredEvent>(e => Add(e.Kind switch
        {
            ShiftEventKind.Delivery => "Delivery! Fresh stock on the shelves",
            ShiftEventKind.PowerCut => "Power cut - the cats can't see you",
            ShiftEventKind.AlarmAudit => "Manager's audit - the alarm drained",
            _ => "Rat! Every cat for itself",
        }, e.Kind switch
        {
            ShiftEventKind.Delivery => new Color(180, 210, 240),
            ShiftEventKind.PowerCut => new Color(150, 160, 190),
            ShiftEventKind.AlarmAudit => new Color(150, 220, 160),
            _ => new Color(220, 140, 120),
        }));
        events.Subscribe<ShopPurchaseEvent>(e =>
        {
            switch (e.Result)
            {
                case BuyResult.Bought:
                    Add($"Bought {e.ItemName} (-{e.Cost}c)", new Color(240, 200, 90));
                    break;
                case BuyResult.Maxed:
                    Add($"{e.ItemName} is maxed out", new Color(170, 170, 180));
                    break;
                default:
                    Add($"Not enough coins for {e.ItemName}", new Color(170, 170, 180));
                    break;
            }
        });
    }

    /// <summary>Adds a new message to the bottom of the feed.</summary>
    public void Add(string text, Color color)
    {
        _entries.Add(new Entry(text, 0f, color));
    }

    /// <summary>Ages all messages and removes any that have expired.</summary>
    public void Update(float dt)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            _entries[i].Age += dt;
        }
        _entries.RemoveAll(e => e.Age > FadeSeconds);
    }

    /// <summary>
    /// Draws the message feed with the newest message at the bottom.
    /// The newest entry flashes briefly; all entries fade out over the last
    /// second and a half of life.
    /// </summary>
    public void Draw(SpriteBatch batch, SpriteFont font, Vector2 topRight)
    {
        // Newest at the bottom, older ones above. Only the last MaxVisible
        // are shown, so a new message pushes the oldest off the top.
        var visible = _entries.TakeLast(MaxVisible).ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            var entry = visible[i];
            bool isNewest = i == visible.Count - 1;
            float y = topRight.Y + (visible.Count - 1 - i) * LineHeight;

            Color color = entry.Color;

            // Flash the newest entry: blink toward white for a moment.
            if (isNewest && entry.Age < FlashSeconds)
            {
                float blink = MathF.Sin(entry.Age * 14f) * 0.5f + 0.5f;
                color = Color.Lerp(color, Color.White, blink * 0.8f);
            }

            // Fade out over the last second and a half of life.
            if (entry.Age > FadeSeconds - 1.5f)
            {
                float alpha = 1f - (entry.Age - (FadeSeconds - 1.5f)) / 1.5f;
                color = new Color(color.R, color.G, color.B, MathHelper.Clamp(alpha, 0f, 1f));
            }

            batch.DrawString(font, entry.Text, new Vector2(topRight.X, y), color);
        }
    }
}

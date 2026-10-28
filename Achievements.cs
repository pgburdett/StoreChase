using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StoreChase;

/// <summary>
/// Persistent achievements and a fastest-win leaderboard.
///
/// Stored as JSON in the user's app-data folder, so progress survives
/// between launches. Pure-ish (no GraphicsDevice); the file I/O is isolated
/// here and only touched on unlock / win, so the game thread never blocks.
/// </summary>
public sealed class AchievementTracker
{
    private readonly string _path;
    private readonly HashSet<string> _unlocked = new();
    private readonly List<float> _fastest = new();

    public AchievementTracker(EventSystem events)
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StoreChase", "save.json");
        Load();
        events.Subscribe<GameWonEvent>(OnWin);
        events.Subscribe<GameLostEvent>(_ => { });
        events.Subscribe<CatBefriendedEvent>(e => TryUnlock("befriend_one"));
    }

    public IReadOnlyCollection<string> Unlocked => _unlocked;
    public IReadOnlyList<float> FastestWins => _fastest;

    /// <summary>All achievement ids the game can award.</summary>
    public static IReadOnlyList<string> AllIds { get; } = new[] { "befriend_one", "first_win", "win" };

    /// <summary>Total number of achievements available.</summary>
    public int Total => AllIds.Count;

    /// <summary>Fastest winning time, or MaxValue if none.</summary>
    public float FastestWin => _fastest.Count > 0 ? _fastest[0] : float.MaxValue;

    public bool IsUnlocked(string id) => _unlocked.Contains(id);

    public void TryUnlock(string id)
    {
        if (_unlocked.Add(id)) { Save(); EventSystem.Instance.Publish(new AchievementUnlockedEvent(id)); }
    }

    private void OnWin(GameWonEvent e)
    {
        TryUnlock("first_win");
        // Track fastest.
        _fastest.Add(e.ElapsedTime);
        _fastest.Sort();
        if (_fastest.Count > 5) _fastest.RemoveRange(5, _fastest.Count - 5);
        Save();
        EventSystem.Instance.Publish(new AchievementUnlockedEvent("win"));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<SaveData>(json);
            if (data == null) return;
            foreach (var a in data.Achievements ?? new()) _unlocked.Add(a);
            if (data.FastestWins != null) _fastest.AddRange(data.FastestWins);
            _fastest.Sort();
            if (_fastest.Count > 5) _fastest.RemoveRange(5, _fastest.Count - 5);
        }
        catch { /* corrupt save: start fresh */ }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var data = new SaveData { Achievements = _unlocked.ToList(), FastestWins = _fastest.ToList() };
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
        }
        catch { /* best effort */ }
    }

    private sealed class SaveData
    {
        public List<string>? Achievements { get; set; }
        public List<float>? FastestWins { get; set; }
    }
}

/// <summary>Published when an achievement unlocks.</summary>
public sealed class AchievementUnlockedEvent : BaseEvent
{
    public string Id { get; }
    public AchievementUnlockedEvent(string id) => Id = id;
}

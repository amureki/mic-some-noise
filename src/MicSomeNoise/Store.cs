using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MicSomeNoise;

public sealed record Track(string Id, string Name, string File, string? SourceUrl = null, string? OriginalTitle = null)
{
    public override string ToString() => Name;
}

public sealed class Settings
{
    public string? Microphone { get; set; }
    public string? Headphones { get; set; }
    public string? Cable { get; set; }
    public float TrackVolume { get; set; } = .5f;
    public bool IncludeMicrophoneDuringPlayback { get; set; }
    public string MicrophoneHoldKey { get; set; } = "F6";
    public float MicrophoneVolume { get; set; } = 1f;
    public string OpenHotkey { get; set; } = "Ctrl+Alt+Space";
    public string PreviousHotkey { get; set; } = "F7";
    public string PlayHotkey { get; set; } = "F8";
    public string NextHotkey { get; set; } = "F9";
    public string VolumeDownHotkey { get; set; } = "F10";
    public string VolumeUpHotkey { get; set; } = "F11";
    public bool PushToTalkEnabled { get; set; }
    public int PushToTalkDelayMs { get; set; } = 150;
    public string PushToTalkKey { get; set; } = "V";
    public string? SelectedTrackId { get; set; }
    public bool ShowGameOverlay { get; set; } = true;
}

public sealed class Store
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicSomeNoise");
    public static readonly string Audio = Path.Combine(Root, "Audio");
    public Settings Settings { get; }
    public List<Track> Tracks { get; }
    public Store()
    {
        Directory.CreateDirectory(Audio);
        var savedSettings = Read<JsonElement>("settings.json");
        Settings = savedSettings.ValueKind == JsonValueKind.Object ? savedSettings.Deserialize<Settings>() ?? new() : new();
        Tracks = Read<List<Track>>("library.json") ?? new();
    }
    private static T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
    public void SaveSettings() => Write("settings.json", Settings);
    public void SaveLibrary() => Write("library.json", Tracks);
    private static void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}

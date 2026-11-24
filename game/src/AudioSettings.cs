using System;
using System.IO;
using System.Text.Json;

namespace DeskCiv;

/// <summary>Volume levels shared by the tray thread and the game thread, persisted between runs.</summary>
public sealed class AudioSettings
{
    /// <summary>The portable launcher keeps saves beside the app. Otherwise they live in the user's app data.</summary>
    public static string DataFolder =>
        Environment.GetEnvironmentVariable("DESKCIV_DATA") is { Length: > 0 } portable
            ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskCiv");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    public int Master { get; set; } = 15;
    public int Ambient { get; set; } = 100;
    public int Music { get; set; } = 60;
    public bool Muted { get; set; }
    public bool ShowOverlay { get; set; } = true;

    public static AudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(FilePath)) ?? new AudioSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Godot.GD.PushWarning($"Could not read settings: {e.Message}");
        }
        return new AudioSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Godot.GD.PushWarning($"Could not save settings: {e.Message}");
        }
    }
}

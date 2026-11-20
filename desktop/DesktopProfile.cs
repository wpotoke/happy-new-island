using System.Text.Json;

namespace WinterIsland;

internal enum WinterMode { Holiday, Cozy, Focus }

internal sealed record AppShortcut(string Name, string Target, string? Icon = null);

internal sealed class DesktopProfile
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string DataFolder { get; } = ProfileFolder();

    public WinterMode Mode { get; set; }
    public bool Garland { get; set; } = true;
    public bool Snow { get; set; } = true;
    public bool Leopard { get; set; } = true;
    public bool AutoHide { get; set; } = true;
    public int GrowthDays { get; set; }
    public DateOnly LastMeal { get; set; }
    public int FavoriteFood { get; set; } = Random.Shared.Next(3);
    public List<AppShortcut> Pins { get; set; } = [];

    private static string ProfileFolder()
    {
        string? temporary = Environment.GetEnvironmentVariable("HAPPY_NEW_ISLAND_PROFILE");
        if (!string.IsNullOrWhiteSpace(temporary)) return Path.GetFullPath(temporary);
        string folder = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(folder, "Happy New Island");
    }

    public static DesktopProfile Load()
    {
        try
        {
            string path = Path.Combine(DataFolder, "settings.json");
            if (File.Exists(path))
            {
                DesktopProfile? settings = JsonSerializer.Deserialize<DesktopProfile>(File.ReadAllText(path));
                if (settings != null)
                {
                    settings.Pins ??= [];
                    settings.Pins = settings.Pins
                        .Where(pin => pin != null && !string.IsNullOrWhiteSpace(pin.Name) && !string.IsNullOrWhiteSpace(pin.Target))
                        .DistinctBy(pin => pin.Target).Take(12).ToList();
                    settings.GrowthDays = Math.Clamp(settings.GrowthDays, 0, 7);
                    return settings;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new DesktopProfile { Pins = DesktopApps.InitialPins() };
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            string path = Path.Combine(DataFolder, "settings.json");
            string pending = path + ".tmp";
            File.WriteAllText(pending, JsonSerializer.Serialize(this, Json));
            File.Move(pending, path, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Apply(WinterMode selected)
    {
        Mode = selected;
        Garland = Snow = Leopard = selected != WinterMode.Focus;
    }

    public void Feed(DateOnly today)
    {
        if (today <= LastMeal) return;
        GrowthDays = Math.Min(7, GrowthDays + 1);
        LastMeal = today;
    }
}

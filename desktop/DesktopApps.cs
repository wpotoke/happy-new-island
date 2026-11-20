using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WinterIsland;

internal static class DesktopApps
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
    private static readonly string[] LinuxAppFolders =
    [
        Path.Combine(Home, ".local/share/applications"),
        Path.Combine(Home, ".local/share/flatpak/exports/share/applications"),
        "/var/lib/flatpak/exports/share/applications", "/usr/local/share/applications", "/usr/share/applications"
    ];

    public static List<AppShortcut> InitialPins()
    {
        var pins = new List<AppShortcut> { new(Russian ? "Файлы" : "Files", Home) };
        if (OperatingSystem.IsWindows()) return pins;
        var applications = Discover();
        foreach (string[] names in new[] { new[] { "safari", "firefox", "chrome", "chromium" }, new[] { "telegram" } })
        {
            AppShortcut? pin = applications.FirstOrDefault(app =>
                names.Any(name => Path.GetFileName(app.Target).Contains(name, StringComparison.OrdinalIgnoreCase)));
            if (pin != null) pins.Add(pin);
        }
        return pins;
    }

    public static List<AppShortcut> Discover()
    {
        if (OperatingSystem.IsWindows()) return [];
        try
        {
            IEnumerable<AppShortcut> apps = OperatingSystem.IsMacOS()
                ? new[] { "/Applications", "/System/Applications", "/System/Applications/Utilities", Path.Combine(Home, "Applications") }
                    .Where(Directory.Exists).SelectMany(folder => Directory.EnumerateDirectories(folder, "*.app"))
                    .Select(path => new AppShortcut(Path.GetFileNameWithoutExtension(path), path))
                : LinuxAppFolders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*.desktop"))
                    .DistinctBy(Path.GetFileName).Select(DesktopEntry).OfType<AppShortcut>();
            return apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static AppShortcut? DesktopEntry(string path)
    {
        try
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            bool entry = false;
            foreach (string line in File.ReadLines(path).Take(250))
            {
                if (line.StartsWith('['))
                {
                    if (entry) break;
                    entry = line == "[Desktop Entry]";
                }
                else if (entry && line.IndexOf('=') is int split && split > 0)
                    data.TryAdd(line[..split], line[(split + 1)..]);
            }
            if (data.GetValueOrDefault("Type") != "Application" || data.GetValueOrDefault("NoDisplay") == "true" ||
                data.GetValueOrDefault("Hidden") == "true") return null;
            string? name = (Russian ? data.GetValueOrDefault("Name[ru]") : null) ?? data.GetValueOrDefault("Name");
            return string.IsNullOrWhiteSpace(name) ? null : new AppShortcut(name, path, data.GetValueOrDefault("Icon"));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static AppShortcut ShortcutFor(string path) =>
        path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) && DesktopEntry(path) is { } entry
            ? entry : new AppShortcut(Path.GetFileNameWithoutExtension(path), path);

    public static bool Open(AppShortcut app, IReadOnlyList<string>? files = null)
    {
        string[] arguments = files?.ToArray() ?? [];
        if (OperatingSystem.IsMacOS())
        {
            if (app.Target.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return DesktopPlatform.Launch("/usr/bin/open", ["-a", app.Target, .. arguments]);
            return DesktopPlatform.Launch("/usr/bin/open", app.Target);
        }
        if (OperatingSystem.IsLinux())
        {
            if (app.Target.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
                return DesktopPlatform.Launch("gio", ["launch", app.Target, .. arguments]);
            if (File.Exists(app.Target) && !app.Target.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                return DesktopPlatform.Launch("xdg-open", app.Target);
            if (File.Exists(app.Target)) return DesktopPlatform.Launch(app.Target, arguments);
            return DesktopPlatform.Launch("xdg-open", app.Target);
        }
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(app.Target) { UseShellExecute = true };
            foreach (string value in arguments) info.ArgumentList.Add(value);
            using var process = System.Diagnostics.Process.Start(info);
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public static async Task<string?> IconFileAsync(AppShortcut shortcut)
    {
        if (OperatingSystem.IsMacOS() && shortcut.Target.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string resources = Path.Combine(shortcut.Target, "Contents/Resources");
                if (!Directory.Exists(resources)) return null;
                string? icon = Directory.EnumerateFiles(resources, "*.icns")
                    .OrderByDescending(file => Path.GetFileNameWithoutExtension(file).Equals("AppIcon", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (icon == null) return null;
                string save = Path.Combine(DesktopProfile.DataFolder, "icons");
                Directory.CreateDirectory(save);
                string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(icon + File.GetLastWriteTimeUtc(icon).Ticks)));
                string image = Path.Combine(save, id + ".png");
                if (!File.Exists(image))
                    await DesktopPlatform.Capture("/usr/bin/sips", "-s", "format", "png", "-Z", "64", icon, "--out", image);
                return File.Exists(image) ? image : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(shortcut.Icon)) return null;
        string key = shortcut.Icon;
        if (Path.IsPathRooted(key)) return File.Exists(key) && key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? key : null;
        foreach (string dir in new[] { Path.Combine(Home, ".local/share/icons/hicolor"), "/usr/share/icons/hicolor", "/usr/share/icons/Adwaita" })
            foreach (string size in new[] { "48x48", "64x64", "32x32", "128x128", "256x256" })
            {
                string path = Path.Combine(dir, size, "apps", key + ".png");
                if (File.Exists(path)) return path;
            }
        string pixmap = Path.Combine("/usr/share/pixmaps", key + ".png");
        return File.Exists(pixmap) ? pixmap : null;
    }
}

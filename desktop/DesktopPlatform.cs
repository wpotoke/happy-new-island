using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;

namespace WinterIsland;

internal sealed class DesktopPlatform : IDisposable
{
    private const string X11 = "libX11.so.6";
    private const string XFixes = "libXfixes.so.3";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private readonly byte[] keys = new byte[32];
    private readonly IntPtr display;
    private readonly IntPtr root;
    private readonly int gKey;
    private readonly bool supportsMedia = OperatingSystem.IsLinux() && FindCommand("playerctl");
    private uint modifiers;

    public DesktopPlatform()
    {
        if (!OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;
            root = XDefaultRootWindow(display);
            gKey = XKeysymToKeycode(display, XStringToKeysym("g"));
        }
        catch (DllNotFoundException) { }
    }

    public bool SupportsEdge => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || display != IntPtr.Zero;
    public bool SupportsMedia => supportsMedia;

    public bool TryPointer(double scale, out PixelPoint pointer)
    {
        pointer = default;
        if (OperatingSystem.IsWindows())
        {
            if (!GetCursorPos(out NativePoint win)) return false;
            pointer = new PixelPoint(win.X, win.Y);
            return true;
        }
        if (OperatingSystem.IsMacOS())
        {
            IntPtr evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return false;
            try
            {
                MacPoint p = CGEventGetLocation(evt);
                pointer = new PixelPoint((int)Math.Round(p.X * scale), (int)Math.Round(p.Y * scale));
                return true;
            }
            finally { CFRelease(evt); }
        }
        if (display == IntPtr.Zero || XQueryPointer(display, root, out _, out _, out int x, out int y,
            out _, out _, out modifiers) == 0) return false;
        pointer = new PixelPoint(x, y);
        return true;
    }

    public bool RevealChord()
    {
        if (OperatingSystem.IsWindows())
            return WindowsKey(0x11) && WindowsKey(0x12) && WindowsKey(0x47);
        if (OperatingSystem.IsMacOS())
            return (MacKey(59) || MacKey(62)) && (MacKey(58) || MacKey(61)) && MacKey(5);
        if (display == IntPtr.Zero || gKey == 0 || (modifiers & 12) != 12) return false;
        XQueryKeymap(display, keys);
        return (keys[gKey / 8] & (1 << (gKey % 8))) != 0;
    }

    public bool AllowInputThrough(Window overlay)
    {
        var platformHandle = overlay.TryGetPlatformHandle();
        if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return false;
        IntPtr handle = platformHandle.Handle;
        if (OperatingSystem.IsWindows())
        {
            int style = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, style | 0x20 | 0x08000000);
            return (GetWindowLong(handle, -20) & 0x20) != 0;
        }
        if (OperatingSystem.IsMacOS())
        {
            if (platformHandle.HandleDescriptor == "NSView")
                handle = ObjectMessage(handle, Selector("window"));
            else if (platformHandle.HandleDescriptor != "NSWindow") return false;
            if (handle == IntPtr.Zero) return false;
            BoolMessage(handle, Selector("setIgnoresMouseEvents:"), true);
            return true;
        }
        if (display == IntPtr.Zero || platformHandle.HandleDescriptor != "XID") return false;
        try
        {
            if (XFixesQueryExtension(display, out _, out _) == 0) return false;
            IntPtr region = XFixesCreateRegion(display, IntPtr.Zero, 0);
            XFixesSetWindowShapeRegion(display, handle, 2, 0, 0, region);
            XFixesDestroyRegion(display, region);
            XFlush(display);
            return true;
        }
        catch (DllNotFoundException) { return false; }
    }

    public async Task<int?> BatteryAsync()
    {
        if (OperatingSystem.IsMacOS())
        {
            string? report = await Capture("/usr/bin/pmset", "-g", "batt").ConfigureAwait(false);
            Match match = Regex.Match(report ?? "", @"\b(\d{1,3})%");
            return match.Success && int.TryParse(match.Groups[1].Value, out int percent) ? Math.Clamp(percent, 0, 100) : null;
        }
        if (OperatingSystem.IsWindows())
            return GetSystemPowerStatus(out PowerState power) && power.Percent <= 100 ? power.Percent : null;
        try
        {
            int[] batteries = Directory.EnumerateDirectories("/sys/class/power_supply")
                .Where(path => ReadSmallFile(Path.Combine(path, "type")) == "Battery")
                .Select(path => int.TryParse(ReadSmallFile(Path.Combine(path, "capacity")), out int capacity) ? capacity : -1)
                .Where(capacity => capacity is >= 0 and <= 100).ToArray();
            return batteries.Length == 0 ? null : (int)Math.Round(batteries.Average());
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool FindCommand(string name)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(directory => directory.Length > 0 && File.Exists(Path.Combine(directory, name)));
    }

    public static async Task<string?> Capture(string executable, params string[] arguments)
    {
        using var process = new Process { StartInfo = StartInfo(executable, arguments) };
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        try
        {
            if (!process.Start()) return null;
            using var limit = new CancellationTokenSource(1800);
            Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
                string result = (await output.ConfigureAwait(false)).Trim();
                return process.ExitCode == 0 ? result : null;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    public static bool Launch(string executable, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(StartInfo(executable, arguments));
            return process != null;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static ProcessStartInfo StartInfo(string executable, string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static string? ReadSmallFile(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool WindowsKey(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool MacKey(ushort key) => CGEventSourceKeyState(0, key);

    public void Dispose()
    {
        if (display != IntPtr.Zero) XCloseDisplay(display);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MacPoint { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerState { public byte Ac, Flags, Percent, Reserved; public uint Seconds, Full; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int style);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerState state);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern MacPoint CGEventGetLocation(IntPtr evt);
    [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceKeyState(int state, ushort key);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern IntPtr Selector(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr ObjectMessage(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void BoolMessage(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XStringToKeysym(string name);
    [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr display, IntPtr symbol);
    [DllImport(X11)] private static extern int XQueryKeymap(IntPtr display, [Out] byte[] keymap);
    [DllImport(X11)] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
        out int x, out int y, out int windowX, out int windowY, out uint mask);
    [DllImport(X11)] private static extern int XFlush(IntPtr display);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(XFixes)] private static extern int XFixesQueryExtension(IntPtr display, out int events, out int errors);
    [DllImport(XFixes)] private static extern IntPtr XFixesCreateRegion(IntPtr display, IntPtr rectangles, int count);
    [DllImport(XFixes)] private static extern void XFixesSetWindowShapeRegion(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr region);
    [DllImport(XFixes)] private static extern void XFixesDestroyRegion(IntPtr display, IntPtr region);
}

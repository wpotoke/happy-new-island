using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WinterIsland
{
    internal static class ShellProfile
    {
        private const uint IslandColor = 0x0017140E; // RGB 14, 20, 23 in COLORREF order
        private static readonly string StateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterIsland", "shell-state.txt");

        private sealed class Snapshot
        {
            public uint Color;
            public bool Wallpaper;
            public bool AutoHide;
            public bool Animations;

            public static Snapshot Read()
            {
                string[] row = File.ReadAllLines(StateFile);
                if (row.Length != 5 || row[0] != "WinterIsland/1")
                    throw new InvalidDataException("Файл восстановления имеет неизвестный формат.");
                return new Snapshot
                {
                    Color = uint.Parse(row[1], CultureInfo.InvariantCulture),
                    Wallpaper = bool.Parse(row[2]),
                    AutoHide = bool.Parse(row[3]),
                    Animations = bool.Parse(row[4])
                };
            }
        }

        public static void Recover()
        {
            if (!File.Exists(StateFile)) return;
            Snapshot saved = Snapshot.Read();
            var errors = new List<Exception>();
            try
            {
                if (WindowsShell.TaskbarAutoHide()) WindowsShell.SetTaskbarAutoHide(saved.AutoHide);
            }
            catch (Exception error) { errors.Add(error); }

            try
            {
                if (!WindowsShell.AnimationsEnabled()) WindowsShell.SetAnimations(saved.Animations);
            }
            catch (Exception error) { errors.Add(error); }

            try
            {
                WithWallpaper(delegate(DesktopWallpaper wallpaper)
                {
                    uint color, status;
                    Check(wallpaper.GetBackgroundColor(out color));
                    Check(wallpaper.GetStatus(out status));
                    bool stillApplied = color == IslandColor;
                    if (stillApplied) Check(wallpaper.SetBackgroundColor(saved.Color));
                    if (stillApplied && (status & 1) == 0 && saved.Wallpaper)
                        Check(wallpaper.Enable(true));
                });
            }
            catch (Exception error) { errors.Add(error); }

            if (errors.Count != 0)
                throw new AggregateException("Windows не восстановила часть оформления.", errors);
            File.Delete(StateFile);
        }

        private static void WithWallpaper(Action<DesktopWallpaper> action)
        {
            Type type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            object instance = Activator.CreateInstance(type);
            try { action((DesktopWallpaper)instance); }
            finally { Marshal.FinalReleaseComObject(instance); }
        }

        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }

        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface DesktopWallpaper
        {
            [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor,
                [MarshalAs(UnmanagedType.LPWStr)] string file);
            [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, out IntPtr file);
            [PreserveSig] int GetMonitorDevicePathAt(uint index, out IntPtr monitor);
            [PreserveSig] int GetMonitorDevicePathCount(out uint count);
            [PreserveSig] int GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitor,
                out WindowsShell.Rect rect);
            [PreserveSig] int SetBackgroundColor(uint color);
            [PreserveSig] int GetBackgroundColor(out uint color);
            [PreserveSig] int SetPosition(int position);
            [PreserveSig] int GetPosition(out int position);
            [PreserveSig] int SetSlideshow(IntPtr items);
            [PreserveSig] int GetSlideshow(out IntPtr items);
            [PreserveSig] int SetSlideshowOptions(uint options, uint tick);
            [PreserveSig] int GetSlideshowOptions(out uint options, out uint tick);
            [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitor, int direction);
            [PreserveSig] int GetStatus(out uint status);
            [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enabled);
        }
    }

    internal sealed class WindowEntry
    {
        public IntPtr Handle;
        public int ProcessId;
        public string Title;
        public string Executable;
    }

    internal struct SnowSurface
    {
        public IntPtr Handle;
        public uint ProcessId;
        public Rectangle Bounds;
        public bool CollectsSnow;
    }

    internal static class WindowsShell
    {
        private const uint GetAutoHide = 4, SetAutoHide = 10;
        private const uint GetClientAnimations = 0x1042, SetClientAnimations = 0x1043;
        private const uint WindowClose = 0x10;
        private static readonly int OwnProcess = Process.GetCurrentProcess().Id;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint Callback;
            public uint Edge;
            public Rect Area;
            public IntPtr Argument;
        }

        private delegate bool WindowVisitor(IntPtr window, IntPtr data);
        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint command, ref AppBarData data);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string windowClass, string title);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool GetSetting(uint action, uint param, out int value, uint flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        private static extern bool SetSetting(uint action, uint param, IntPtr value, uint flags);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(
            IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint relation);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern uint GetWindowStyle(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")]
        private static extern IntPtr CopyIcon(IntPtr icon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey { public Guid Format; public uint Id; }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropertyValue
        {
            [FieldOffset(0)] public ushort Kind;
            [FieldOffset(8)] public IntPtr Text;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface WindowProperties
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropertyValue value);
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out WindowProperties properties);
        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropertyValue value);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder appId);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int X, Y;
            public uint Buttons, Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint Type;
            public InputData Data;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileIcon
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern UIntPtr SHGetFileInfo(string path, uint attr, out FileIcon icon, uint size, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private static AppBarData BarData(IntPtr window)
        {
            return new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)), Window = window };
        }

        public static bool TaskbarAutoHide()
        {
            var bar = BarData(Taskbar());
            return (SHAppBarMessage(GetAutoHide, ref bar).ToUInt64() & 1) != 0;
        }

        private static IntPtr Taskbar()
        {
            IntPtr handle = FindWindow("Shell_TrayWnd", null);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Панель задач Windows пока недоступна.");
            return handle;
        }

        public static void SetTaskbarAutoHide(bool on)
        {
            if (TaskbarAutoHide() == on) return;
            var bar = BarData(Taskbar());
            bar.Argument = new IntPtr(on ? 1 : 2);
            SHAppBarMessage(SetAutoHide, ref bar);
            if (TaskbarAutoHide() != on)
                throw new InvalidOperationException("Windows не применила автоскрытие панели задач.");
        }

        public static bool AnimationsEnabled()
        {
            int value;
            if (!GetSetting(GetClientAnimations, 0, out value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return value != 0;
        }

        public static void SetAnimations(bool enabled)
        {
            if (AnimationsEnabled() == enabled) return;
            if (!SetSetting(SetClientAnimations, 0, new IntPtr(enabled ? 1 : 0), 3))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static IList<WindowEntry> OpenWindows()
        {
            var entries = new List<WindowEntry>();
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                if (!IsWindowVisible(window)) return true;
                uint id;
                GetWindowThreadProcessId(window, out id);
                if (id == OwnProcess || id == 0) return true;
                uint ex = GetWindowStyle(window, -20);
                if ((ex & 0x80) != 0 || (GetWindow(window, 4) != IntPtr.Zero && (ex & 0x40000) == 0))
                    return true;

                uint cloak;
                if (DwmGetWindowAttribute(window, 14, out cloak, 4) == 0 && cloak != 0) return true;
                var cls = new StringBuilder(64);
                GetClassName(window, cls, cls.Capacity);
                if (cls.ToString() == "Progman" || cls.ToString() == "WorkerW" || cls.ToString() == "Shell_TrayWnd")
                    return true;
                string caption = WindowTitle(window);
                if (caption.Length == 0) return true;

                var row = new WindowEntry { Handle = window, ProcessId = (int)id, Title = caption };
                try
                {
                    using (Process process = Process.GetProcessById(row.ProcessId))
                        row.Executable = process.MainModule.FileName;
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { return true; }
                entries.Add(row);
                return true;
            }, IntPtr.Zero);
            return entries;
        }

        public static string WindowTitle(IntPtr window)
        {
            var caption = new StringBuilder(512);
            return GetWindowText(window, caption, caption.Capacity) == 0 ? String.Empty : caption.ToString();
        }

        public static uint WindowProcess(IntPtr window)
        {
            uint process;
            return GetWindowThreadProcessId(window, out process) == 0 ? 0 : process;
        }

        public static IntPtr FindTelegramWindow(out uint process)
        {
            var telegramProcesses = new HashSet<uint>();
            foreach (Process candidate in Process.GetProcessesByName("Telegram"))
                using (candidate) telegramProcesses.Add((uint)candidate.Id);

            IntPtr best = IntPtr.Zero;
            uint owner = 0;
            int bestScore = 0;
            if (telegramProcesses.Count > 0)
            {
                EnumWindows(delegate(IntPtr window, IntPtr unused)
                {
                    uint pid = WindowProcess(window);
                    if (!telegramProcesses.Contains(pid) || (GetWindowStyle(window, -20) & 0x80) != 0) return true;
                    string title = WindowTitle(window);
                    if (title.Length == 0) return true;

                    // Telegram keeps the main window and its counter when it hides to the tray.
                    int score = title == "Telegram" || title.StartsWith("Telegram (", StringComparison.Ordinal) ? 8 : 1;
                    if (IsWindowVisible(window)) score += 2;
                    if (GetWindow(window, 4) == IntPtr.Zero) score++;
                    if (score > bestScore)
                    {
                        best = window;
                        owner = pid;
                        bestScore = score;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            process = owner;
            return best;
        }

        public static Bitmap SmallWindowIcon(IntPtr window, int dpi)
        {
            const uint getIcon = 0x007f, bounded = 0x23;
            IntPtr icon;
            if (SendMessageTimeout(window, getIcon, IntPtr.Zero, new IntPtr(dpi), bounded, 45, out icon) == IntPtr.Zero)
                return null;
            if (icon == IntPtr.Zero)
                SendMessageTimeout(window, getIcon, new IntPtr(2), new IntPtr(dpi), bounded, 45, out icon);
            IntPtr copy = icon == IntPtr.Zero ? IntPtr.Zero : CopyIcon(icon);
            return copy == IntPtr.Zero ? null : BitmapFromIcon(copy);
        }

        public static void SnowSurfaces(Rectangle area, List<SnowSurface> surfaces, bool stopOnFirst = false)
        {
            // Follow the z-order directly. The cap also bounds races with windows
            // opening, closing or moving above us during the snapshot.
            IntPtr window = GetTopWindow(IntPtr.Zero);
            var cls = new StringBuilder(64);
            for (int seen = 0; seen < 512 && window != IntPtr.Zero; seen++)
            {
                IntPtr item = window;
                window = GetWindow(item, 2);
                if (window == item) break;
                if (!IsWindowVisible(item) || IsIconic(item)) continue;

                uint pid;
                GetWindowThreadProcessId(item, out pid);
                if (pid == 0 || pid == OwnProcess) continue;
                uint style = GetWindowStyle(item, -20);
                if ((style & 0x80020) == 0x80020) continue;

                Rect frame;
                if (!GetWindowRect(item, out frame)) continue;
                Rectangle bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
                if (bounds.Width < 2 || bounds.Height < 2 || !area.IntersectsWith(bounds)) continue;
                uint cloak;
                if (DwmGetWindowAttribute(item, 14, out cloak, 4) == 0 && cloak != 0) continue;
                cls.Clear();
                GetClassName(item, cls, cls.Capacity);
                string kind = cls.ToString();
                if (kind == "Progman" || kind == "WorkerW" || kind == "Shell_TrayWnd" || kind == "Shell_SecondaryTrayWnd")
                    continue;
                if (DwmGetWindowAttribute(item, 9, out frame, 16) == 0)
                    bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
                if (!area.IntersectsWith(bounds)) continue;
                surfaces.Add(new SnowSurface
                {
                    Handle = item, ProcessId = pid, Bounds = bounds,
                    CollectsSnow = bounds.Width >= 80 && bounds.Height >= 58 && (style & 0x80) == 0
                });
                if (stopOnFirst) return;
            }
        }

        public static bool ForegroundIsFullScreen(Rectangle screen)
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return false;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid == 0 || pid == OwnProcess) return false;

            Rect frame;
            if (!GetWindowRect(window, out frame) || !CoversScreen(frame, screen)) return false;
            Rect visible;
            if (DwmGetWindowAttribute(window, 9, out visible, 16) == 0 && !CoversScreen(visible, screen)) return false;
            var cls = new StringBuilder(48);
            GetClassName(window, cls, cls.Capacity);
            string kind = cls.ToString();
            if (kind == "Progman" || kind == "WorkerW") return false;

            // Maximizing with a hidden taskbar can also report QUNS_BUSY. Fullscreen browsers
            // can retain WS_MAXIMIZE but remove the caption and resizing frame.
            if (!IsZoomed(window)) return true;
            const uint captionAndFrame = 0x00C40000;
            int state;
            return SHQueryUserNotificationState(out state) == 0 &&
                (state == 3 || (state == 2 && (GetWindowStyle(window, -16) & captionAndFrame) == 0));
        }

        private static bool CoversScreen(Rect window, Rectangle screen)
        {
            return window.Left <= screen.Left + 1 && window.Top <= screen.Top + 1 &&
                window.Right >= screen.Right - 1 && window.Bottom >= screen.Bottom - 1;
        }

        public static IList<WindowEntry> VisibleTaskbars()
        {
            var bars = new List<WindowEntry>();
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                if (!IsWindowVisible(window)) return true;
                var name = new StringBuilder(48);
                GetClassName(window, name, name.Capacity);
                string cls = name.ToString();
                if (cls != "Shell_TrayWnd" && cls != "Shell_SecondaryTrayWnd") return true;
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid != 0) bars.Add(new WindowEntry { Handle = window, ProcessId = (int)pid });
                return true;
            }, IntPtr.Zero);
            return bars;
        }

        public static void ShowTaskbar(WindowEntry bar, bool visible)
        {
            if (IsSameWindow(bar)) ShowWindowAsync(bar.Handle, visible ? 4 : 0);
        }

        public static string AppId(WindowEntry window)
        {
            var iid = typeof(WindowProperties).GUID;
            WindowProperties store;
            if (SHGetPropertyStoreForWindow(window.Handle, ref iid, out store) >= 0 && store != null)
            {
                try
                {
                    var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
                    PropertyValue value;
                    if (store.GetValue(ref key, out value) >= 0)
                    {
                        try
                        {
                            if (value.Kind == 31 && value.Text != IntPtr.Zero)
                                return Marshal.PtrToStringUni(value.Text);
                        }
                        finally { PropVariantClear(ref value); }
                    }
                }
                finally { Marshal.ReleaseComObject(store); }
            }

            IntPtr process = OpenProcess(0x1000, false, window.ProcessId);
            if (process == IntPtr.Zero) return null;
            try
            {
                var id = new StringBuilder(260);
                uint size = (uint)id.Capacity;
                return GetApplicationUserModelId(process, ref size, id) == 0 ? id.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        public static bool FocusMedia(MediaCard card)
        {
            WindowEntry best = null;
            int bestRank = 0;
            string executable = MediaExecutable(card.SourceId ?? "");
            foreach (WindowEntry window in OpenWindows())
            {
                bool title = card.Title.Length >= 4 && window.Title.IndexOf(card.Title, StringComparison.OrdinalIgnoreCase) >= 0;
                bool identity = !String.IsNullOrEmpty(card.SourceId) &&
                    String.Equals(AppId(window), card.SourceId, StringComparison.OrdinalIgnoreCase);
                bool process = executable != null && String.Equals(Path.GetFileName(window.Executable), executable, StringComparison.OrdinalIgnoreCase);
                int rank = (identity ? 30 : 0) + (process ? 20 : 0) + (title ? 10 : 0);
                if (rank <= bestRank) continue;
                bestRank = rank;
                best = window;
            }

            if (best == null || !IsSameWindow(best)) return false;
            return BringForward(best);
        }

        private static string MediaExecutable(string source)
        {
            if (source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(source);
            if (source.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (source.Length >= 16 && source.Substring(0, 16).Trim("0123456789ABCDEFabcdef".ToCharArray()).Length == 0)) return "firefox.exe";
            if (source.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) return "chrome.exe";
            if (source.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) >= 0) return "msedge.exe";
            if (source.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return "spotify.exe";
            if (source.IndexOf("telegram", StringComparison.OrdinalIgnoreCase) >= 0) return "Telegram.exe";
            return null;
        }

        public static bool IsSameWindow(WindowEntry item)
        {
            uint pid;
            GetWindowThreadProcessId(item.Handle, out pid);
            return pid != 0 && pid == item.ProcessId;
        }

        public static void Focus(WindowEntry item)
        {
            if (!IsSameWindow(item)) throw new InvalidOperationException("Это окно уже закрылось.");
            BringForward(item);
        }

        public static bool RevealForDrag(WindowEntry item)
        {
            if (!IsSameWindow(item)) return false;
            if (BringForward(item)) return true;
            const uint moveOnlyInZOrder = 0x4000 | 0x0200 | 0x0010 | 0x0002 | 0x0001;
            return SetWindowPos(item.Handle, IntPtr.Zero, 0, 0, 0, 0, moveOnlyInZOrder);
        }

        public static void ShowDesktop()
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            try { shell.MinimizeAll(); }
            finally { Marshal.ReleaseComObject((object)shell); }
        }

        public static bool DesktopIsForeground(IntPtr island)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == island) return true;
            var name = new StringBuilder(48);
            GetClassName(foreground, name, name.Capacity);
            string cls = name.ToString();
            return cls == "Progman" || cls == "WorkerW";
        }

        private static bool BringForward(WindowEntry item)
        {
            if (IsIconic(item.Handle)) ShowWindowAsync(item.Handle, 9);
            return SetForegroundWindow(item.Handle) || GetForegroundWindow() == item.Handle;
        }

        public static void CloseWindow(WindowEntry item)
        {
            if (!IsSameWindow(item)) return;
            if (!PostMessage(item.Handle, WindowClose, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public static void Launch(string target)
        {
            using (Process process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })) { }
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetImageList(int kind, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out object imageList);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string path, IntPtr bind, out IntPtr item, uint filter, out uint info);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        private static extern UIntPtr IconIndexFromPidl(IntPtr pidl, uint attr, out FileIcon icon, uint size, uint flags);

        [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ShellImageList
        {
            [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
            [PreserveSig] int ReplaceIcon(int index, IntPtr icon, out int result);
            [PreserveSig] int SetOverlayImage(int index, int overlay);
            [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
            [PreserveSig] int AddMasked(IntPtr image, uint mask, out int index);
            [PreserveSig] int Draw(IntPtr options);
            [PreserveSig] int Remove(int index);
            [PreserveSig] int GetIcon(int index, uint flags, out IntPtr icon);
        }

        public static Bitmap IconBitmap(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            IntPtr pidl = IntPtr.Zero;
            uint size = (uint)Marshal.SizeOf(typeof(FileIcon));
            FileIcon info;
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    uint attrs;
                    if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out attrs) < 0) return null;
                }

                UIntPtr index = pidl == IntPtr.Zero
                    ? SHGetFileInfo(path, 0, out info, size, 0x4000)
                    : IconIndexFromPidl(pidl, 0, out info, size, 0x4008);
                if (index != UIntPtr.Zero)
                {
                    Guid iid = typeof(ShellImageList).GUID;
                    object list;
                    if (SHGetImageList(2, ref iid, out list) >= 0 && list != null)
                    {
                        try
                        {
                            IntPtr icon;
                            if (((ShellImageList)list).GetIcon(info.IconIndex, 1, out icon) >= 0 && icon != IntPtr.Zero)
                                return BitmapFromIcon(icon);
                        }
                        finally { Marshal.ReleaseComObject(list); }
                    }
                }

                if (pidl == IntPtr.Zero) SHGetFileInfo(path, 0, out info, size, 0x100);
                else IconIndexFromPidl(pidl, 0, out info, size, 0x108);
                return info.Icon == IntPtr.Zero ? null : BitmapFromIcon(info.Icon);
            }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        private static Bitmap BitmapFromIcon(IntPtr icon)
        {
            try
            {
                using (Icon view = Icon.FromHandle(icon))
                    return view.ToBitmap();
            }
            finally { DestroyIcon(icon); }
        }

        public static void Shortcut(Keys modifier, Keys key)
        {
            var sequence = new[]
            {
                KeyEvent(modifier, false), KeyEvent(key, false),
                KeyEvent(key, true), KeyEvent(modifier, true)
            };
            if (SendInput(4, sequence, Marshal.SizeOf(typeof(Input))) != 4)
            {
                var release = new[] { KeyEvent(key, true), KeyEvent(modifier, true) };
                SendInput(2, release, Marshal.SizeOf(typeof(Input)));
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static Input KeyEvent(Keys key, bool release)
        {
            return new Input
            {
                Type = 1,
                Data = new InputData
                {
                    Keyboard = new KeyboardInput { Key = (ushort)key, Flags = release ? 2u : 0u }
                }
            };
        }
    }

    internal sealed class BackgroundPriority
    {
        private sealed class SavedPriority
        {
            public DateTime Started;
            public ProcessPriorityClass Priority;
        }

        private readonly Dictionary<int, SavedPriority> applied = new Dictionary<int, SavedPriority>();

        public bool IsReduced(WindowEntry window)
        {
            if (!applied.ContainsKey(window.ProcessId)) return false;
            try
            {
                using (Process process = Process.GetProcessById(window.ProcessId))
                    return WindowsShell.IsSameWindow(window) && process.StartTime == applied[window.ProcessId].Started;
            }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
            catch (Win32Exception) { return false; }
        }

        public void Toggle(WindowEntry window)
        {
            if (!WindowsShell.IsSameWindow(window)) throw new InvalidOperationException("Это окно уже закрылось.");
            using (Process process = Process.GetProcessById(window.ProcessId))
            {
                SavedPriority previous;
                if (applied.TryGetValue(window.ProcessId, out previous) && previous.Started == process.StartTime)
                {
                    process.PriorityClass = previous.Priority;
                    applied.Remove(window.ProcessId);
                    return;
                }
                if (process.PriorityClass == ProcessPriorityClass.Idle ||
                    process.PriorityClass == ProcessPriorityClass.BelowNormal)
                    return;
                var entry = new SavedPriority { Started = process.StartTime, Priority = process.PriorityClass };
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                applied[window.ProcessId] = entry;
            }
        }

        public void Restore()
        {
            foreach (var entry in applied)
            {
                try
                {
                    using (Process process = Process.GetProcessById(entry.Key))
                    {
                        if (process.StartTime == entry.Value.Started &&
                            process.PriorityClass == ProcessPriorityClass.BelowNormal)
                            process.PriorityClass = entry.Value.Priority;
                    }
                }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
                catch (Win32Exception) { }
            }
            applied.Clear();
        }
    }
}

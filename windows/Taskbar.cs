using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class TaskbarVisibility : IDisposable
    {
        public const int Hotkey = 0x39FB;
        private static readonly string RestoreFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterIsland", "taskbar-autohide.restore");
        private Process guard;
        private EventWaitHandle release;
        private IntPtr hotkeyWindow;
        private IList<WindowEntry> original;

        public bool Hidden { get { return guard != null && !guard.HasExited; } }
        public bool HasHotkey { get { return hotkeyWindow != IntPtr.Zero; } }

        public void Hide(IntPtr owner)
        {
            if (Hidden) return;
            Restore();
            string token = Guid.NewGuid().ToString("N");
            original = WindowsShell.VisibleTaskbars();
            try
            {
                release = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token, "release"));
                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token, "ready")))
                using (var main = Process.GetCurrentProcess())
                {
                    guard = Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--taskbar-guard " + main.Id + " " + token,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (guard == null || !ready.WaitOne(2300) || guard.HasExited)
                        throw new InvalidOperationException("Не удалось скрыть панель задач Windows.");
                }
                if (RegisterHotKey(owner, Hotkey, 0x4007, (uint)Keys.T)) hotkeyWindow = owner;
            }
            catch { Restore(); throw; }
        }

        public void Restore()
        {
            if (hotkeyWindow != IntPtr.Zero)
            {
                UnregisterHotKey(hotkeyWindow, Hotkey);
                hotkeyWindow = IntPtr.Zero;
            }
            if (release != null) release.Set();
            if (guard != null)
            {
                guard.WaitForExit(850);
                guard.Dispose();
                guard = null;
            }
            if (release != null) { release.Dispose(); release = null; }
            if (original != null)
            {
                foreach (WindowEntry bar in original) WindowsShell.ShowTaskbar(bar, true);
                original = null;
            }
        }

        public static void Watch(int ownerId, string token)
        {
            var hidden = new Dictionary<IntPtr, WindowEntry>();
            bool changedAutoHide = false;
            try
            {
                using (var owner = Process.GetProcessById(ownerId))
                using (var ready = EventWaitHandle.OpenExisting(EventName(token, "ready")))
                using (var release = EventWaitHandle.OpenExisting(EventName(token, "release")))
                {
                    if (!owner.HasExited && !release.WaitOne(0))
                    {
                        foreach (WindowEntry bar in WindowsShell.VisibleTaskbars()) hidden[bar.Handle] = bar;
                        changedAutoHide = EnableAutoHideForWorkArea();
                    }
                    while (!owner.HasExited && !release.WaitOne(0))
                    {
                        foreach (WindowEntry bar in WindowsShell.VisibleTaskbars())
                        {
                            hidden[bar.Handle] = bar;
                            WindowsShell.ShowTaskbar(bar, false);
                        }
                        ready.Set();
                        if (release.WaitOne(450)) break;
                    }
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (WaitHandleCannotBeOpenedException) { }
            finally
            {
                if (changedAutoHide) RecoverAutoHide();
                foreach (WindowEntry bar in hidden.Values) WindowsShell.ShowTaskbar(bar, true);
            }
        }

        private static bool EnableAutoHideForWorkArea()
        {
            bool recover = false;
            try
            {
                if (WindowsShell.TaskbarAutoHide()) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(RestoreFile));
                File.WriteAllText(RestoreFile, "Windows taskbar was visible");
                recover = true;
                WindowsShell.SetTaskbarAutoHide(true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            return recover;
        }

        public static void RecoverAutoHide()
        {
            if (!File.Exists(RestoreFile)) return;
            try
            {
                WindowsShell.SetTaskbarAutoHide(false);
                File.Delete(RestoreFile);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
        }

        private static string EventName(string token, string purpose) { return @"Local\WinterIsland.Taskbar." + token + "." + purpose; }

        public void Dispose() { Restore(); }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}

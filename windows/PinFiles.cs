using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace WinterIsland
{
    internal sealed class PinFileTarget
    {
        private const string ShellPrefix = @"shell:AppsFolder\";
        private readonly AppPin pin;
        private string executable, appId, arguments, directory;

        public PinFileTarget(AppPin selected)
        {
            pin = selected;
            string target = pin.Target;
            if (target.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase)) appId = target.Substring(ShellPrefix.Length);
            else if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) ReadShortcut(target);
            else if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable = target;
        }

        private void ReadShortcut(string path)
        {
            dynamic shell = null, shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                shortcut = shell.CreateShortcut(path);
                string target = shortcut.TargetPath;
                arguments = shortcut.Arguments;
                directory = shortcut.WorkingDirectory;
                if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable = target;
                int appIndex = (arguments ?? "").IndexOf(ShellPrefix, StringComparison.OrdinalIgnoreCase);
                if (appIndex >= 0)
                {
                    appId = arguments.Substring(appIndex + ShellPrefix.Length).Trim().Trim('"');
                    executable = null;
                }
            }
            catch (COMException) { }
            finally
            {
                if (shortcut != null) Marshal.ReleaseComObject((object)shortcut);
                if (shell != null) Marshal.ReleaseComObject((object)shell);
            }
        }

        public bool CanDropOnIcon
        {
            get { return String.Equals(Path.GetFileName(executable), "Telegram.exe", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsTelegram
        {
            get { return CanDropOnIcon || (appId != null && appId.StartsWith("Telegram", StringComparison.OrdinalIgnoreCase)); }
        }

        public void Reveal()
        {
            WindowEntry window = FindWindow();
            if (window == null || !WindowsShell.RevealForDrag(window)) WindowsShell.Launch(pin.Target);
        }

        private WindowEntry FindWindow()
        {
            string fallback = null;
            if (appId != null)
            {
                if (appId.IndexOf("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) == 0 ||
                    appId.IndexOf("OpenAI.ChatGPT", StringComparison.OrdinalIgnoreCase) == 0) fallback = "ChatGPT.exe";
                else if (appId.IndexOf("Telegram", StringComparison.OrdinalIgnoreCase) == 0) fallback = "Telegram.exe";
            }
            IList<WindowEntry> windows = WindowsShell.OpenWindows();
            if (!String.IsNullOrEmpty(appId))
            {
                WindowEntry matchingId = windows.FirstOrDefault(window =>
                    String.Equals(WindowsShell.AppId(window), appId, StringComparison.OrdinalIgnoreCase));
                if (matchingId != null) return matchingId;
            }
            if (!String.IsNullOrEmpty(executable))
            {
                WindowEntry matchingPath = windows.FirstOrDefault(window =>
                    String.Equals(window.Executable, executable, StringComparison.OrdinalIgnoreCase));
                if (matchingPath != null) return matchingPath;
            }
            return fallback == null ? null : windows.FirstOrDefault(window =>
                String.Equals(Path.GetFileName(window.Executable), fallback, StringComparison.OrdinalIgnoreCase));
        }

        public void OfferTelegramFiles(string[] files)
        {
            if (!CanDropOnIcon || !File.Exists(executable)) return;
            string quoted = String.Join(" ", files.Where(File.Exists).Select(file => "\"" + file + "\""));
            if (quoted.Length == 0) return;
            var info = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = String.IsNullOrWhiteSpace(arguments) ? "-sendpath " + quoted : arguments + " -sendpath " + quoted,
                WorkingDirectory = Directory.Exists(directory) ? directory : Path.GetDirectoryName(executable),
                UseShellExecute = false
            };
            using (Process.Start(info)) { }
        }
    }

    internal static class PinOrder
    {
        public static List<AppPin> Move(IList<AppPin> pins, AppPin moved, int slot)
        {
            int from = pins.IndexOf(moved);
            if (from < 0 || slot < 0 || slot > pins.Count) return null;
            if (slot > from) slot--;
            if (from == slot) return null;
            var result = new List<AppPin>(pins);
            result.RemoveAt(from);
            result.Insert(slot, moved);
            return result;
        }
    }
}

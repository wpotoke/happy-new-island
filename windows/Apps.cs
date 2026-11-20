using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace WinterIsland
{
    internal sealed class AppPin
    {
        public string Name;
        public string Target;
        public string IconPath;
        public override string ToString() { return Name; }
    }

    internal static class AppPins
    {
        private static readonly string Store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterIsland", "pins.xml");

        public static List<AppPin> Load()
        {
            var pins = new List<AppPin>();
            if (File.Exists(Store))
            {
                try
                {
                    XElement saved = XElement.Load(Store);
                    if (saved.Name == "pins")
                    {
                        foreach (XElement row in saved.Elements("app"))
                        {
                            string name = (string)row.Attribute("name"), target = (string)row.Attribute("target");
                            if (!String.IsNullOrWhiteSpace(name) && !String.IsNullOrWhiteSpace(target))
                                pins.Add(new AppPin { Name = name, Target = target, IconPath = (string)row.Attribute("icon") ?? target });
                        }
                        return pins;
                    }
                }
                catch (XmlException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(folder))
                foreach (string path in Directory.EnumerateFiles(folder, "*.lnk"))
                    pins.Add(FromFile(path));
            IList<WindowEntry> windows = WindowsShell.OpenWindows();
            AddRunning(pins, windows, "ChatGPT", "ChatGPT.exe", @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App");
            AddRunning(pins, windows, "FlClashX", "FlClashX.exe",
                @"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\FlClashX\FlClashX.exe");
            string[] preferred = { "Firefox", "ChatGPT", "Telegram", "File Explorer", "Microsoft Edge",
                "Figma", "GitHub", "FlClashX", "WireGuard" };
            return pins.OrderBy(pin =>
            {
                int position = Array.FindIndex(preferred, name => String.Equals(name, pin.Name, StringComparison.OrdinalIgnoreCase));
                return position < 0 ? preferred.Length : position;
            }).ThenBy(pin => pin.Name).ToList();
        }

        public static void Save(IEnumerable<AppPin> pins)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Store));
            string pending = Store + ".pending";
            try
            {
                new XElement("pins", pins.Select(pin => new XElement("app", new XAttribute("name", pin.Name),
                    new XAttribute("target", pin.Target), new XAttribute("icon", pin.IconPath ?? pin.Target)))).Save(pending);
                if (File.Exists(Store)) File.Replace(pending, Store, null);
                else File.Move(pending, Store);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        public static AppPin FromFile(string path)
        {
            return new AppPin { Name = Path.GetFileNameWithoutExtension(path), Target = path, IconPath = path };
        }

        public static AppPin FromWindow(WindowEntry window)
        {
            string id = WindowsShell.AppId(window);
            if (!String.IsNullOrEmpty(id) && (id.Contains("!") || String.IsNullOrEmpty(window.Executable)))
            {
                string target = @"shell:AppsFolder\" + id;
                return new AppPin { Name = window.Title, Target = target, IconPath = target };
            }
            return String.IsNullOrEmpty(window.Executable) ? null : FromFile(window.Executable);
        }

        public static List<AppPin> Installed()
        {
            var apps = new List<AppPin>();
            dynamic shell = null, directory = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                directory = shell.NameSpace("shell:AppsFolder");
                if (directory == null) return apps;
                items = directory.Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);
                    if (item == null) continue;
                    try
                    {
                        string name = item.Name, path = item.Path;
                        if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(path)) continue;
                        string target = Path.IsPathRooted(path) ? path : @"shell:AppsFolder\" + path;
                        apps.Add(new AppPin { Name = name, Target = target, IconPath = target });
                    }
                    finally { ReleaseCom((object)item); }
                }
            }
            catch (COMException) { }
            finally { ReleaseCom((object)items); ReleaseCom((object)directory); ReleaseCom((object)shell); }
            return apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void ReleaseCom(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private static void AddRunning(List<AppPin> pins, IList<WindowEntry> windows, string name, string executable, string target)
        {
            if (pins.Any(pin => String.Equals(pin.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            if (windows.Any(window => String.Equals(Path.GetFileName(window.Executable), executable, StringComparison.OrdinalIgnoreCase)))
                pins.Add(new AppPin { Name = name, IconPath = target, Target = target });
        }
    }

    internal sealed class AppPicker : Form
    {
        private readonly List<AppPin> installed = AppPins.Installed();
        private readonly TextBox search;
        private readonly ListBox results;
        private readonly Button add;

        public AppPin Selected { get; private set; }

        public AppPicker()
        {
            Text = "Добавить приложение";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            ClientSize = new Size(426, 372);
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.FromArgb(17, 19, 21);
            ForeColor = Color.FromArgb(236, 243, 240);

            var label = new Label { Text = "Поиск по приложениям Windows", AutoSize = true, Location = new Point(17, 14) };
            search = new TextBox { AccessibleName = label.Text, BackColor = Color.FromArgb(29, 31, 34), ForeColor = ForeColor };
            search.SetBounds(17, 35, 392, 26);
            results = new ListBox { BackColor = Color.FromArgb(23, 25, 27), ForeColor = ForeColor,
                IntegralHeight = false, AccessibleName = "Установленные приложения" };
            results.SetBounds(17, 70, 392, 248);
            var browse = MakeButton("Выбрать файл…", 17, 136);
            add = MakeButton("Закрепить", 308, 101);
            add.ForeColor = Color.FromArgb(247, 226, 188);
            var cancel = MakeButton("Отмена", 215, 86);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { label, search, results, browse, cancel, add });
            AcceptButton = add;
            CancelButton = cancel;
            search.TextChanged += delegate { Filter(); };
            results.SelectedIndexChanged += delegate { add.Enabled = results.SelectedIndex >= 0; };
            results.DoubleClick += delegate { AcceptSelected(); };
            add.Click += delegate { AcceptSelected(); };
            browse.Click += delegate { Browse(); };
            Filter();
            ActiveControl = search;
        }

        private Button MakeButton(string text, int left, int width)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(29, 31, 34) };
            button.FlatAppearance.BorderColor = Color.FromArgb(57, 64, 66);
            button.SetBounds(left, 330, width, 29);
            return button;
        }

        private void Filter()
        {
            string query = search.Text.Trim();
            results.BeginUpdate();
            results.Items.Clear();
            foreach (AppPin app in installed)
                if (app.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) results.Items.Add(app);
            if (results.Items.Count != 0) results.SelectedIndex = 0;
            results.EndUpdate();
            add.Enabled = results.SelectedIndex >= 0;
        }

        private void AcceptSelected()
        {
            Selected = results.SelectedItem as AppPin;
            if (Selected != null) DialogResult = DialogResult.OK;
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Выбрать приложение или ярлык", CheckFileExists = true, DereferenceLinks = false,
                Filter = "Программы и ярлыки|*.exe;*.lnk;*.url;*.appref-ms|Все файлы|*.*"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Selected = AppPins.FromFile(dialog.FileName);
                DialogResult = DialogResult.OK;
            }
        }
    }
}

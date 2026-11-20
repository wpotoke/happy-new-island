using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace WinterIsland
{
    internal sealed class IslandMode
    {
        public string Name;
        public int Snow, Speed, Glow;
        public bool Garland, Drifts, Leopard;

        public IslandMode Copy() { return (IslandMode)MemberwiseClone(); }
        public override string ToString() { return Name; }
    }

    internal sealed class IslandModes
    {
        public const int DaysToGrow = 7;
        private static readonly string PathToModes = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinterIsland", "modes.xml");

        public readonly List<IslandMode> Items = new List<IslandMode>();
        public int Active, Best, Feedings, GrowthDays;
        public int FavoriteFood = -1;
        public bool FavoriteKnown;
        public DateTime LastGrowthDay;
        public IslandMode Current { get { return Items[Active]; } }

        public static IslandModes Load()
        {
            var modes = new IslandModes();
            try
            {
                if (File.Exists(PathToModes))
                {
                    XElement source = XElement.Load(PathToModes);
                    foreach (XElement row in source.Elements("mode"))
                    {
                        string name = ((string)row.Attribute("name") ?? "").Trim();
                        if (name.Length == 0) continue;
                        bool seasonal = name == "Праздник" || name == "Уют";
                        var saved = new IslandMode
                        {
                            Name = name, Snow = Value(row, "snow", 65, 0, 100), Speed = Value(row, "speed", 100, 40, 160),
                            Glow = Value(row, "glow", 46, 0, 100), Garland = Flag(row, "garland", true),
                            Drifts = Flag(row, "drifts", Flag(row, "frost", seasonal)), Leopard = Flag(row, "leopard", seasonal)
                        };
                        modes.Items.Add(UpdatePreset(saved));
                    }
                    if (modes.Items.Count > 0)
                    {
                        modes.Active = Value(source, "active", 0, 0, modes.Items.Count - 1);
                        modes.Best = Value(source, "best", 0, 0, Int32.MaxValue);
                        modes.Feedings = Value(source, "feedings", 0, 0, Int32.MaxValue);
                        modes.GrowthDays = Value(source, "growthDays", 0, 0, DaysToGrow);
                        modes.FavoriteFood = Value(source, "favoriteFood", -1, -1, (int)LeopardFood.Treat);
                        modes.FavoriteKnown = modes.FavoriteFood >= 0 && Flag(source, "favoriteKnown", false);
                        DateTime.TryParseExact((string)source.Attribute("lastGrowthDay"), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out modes.LastGrowthDay);
                        return modes;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }

            modes.Items.Add(Festive());
            modes.Items.Add(Cozy());
            modes.Items.Add(new IslandMode { Name = "Фокус", Snow = 0, Speed = 100, Glow = 0, Garland = false });
            return modes;
        }

        private static IslandMode Festive() { return new IslandMode { Name = "Праздник", Snow = 94, Speed = 118, Glow = 74, Garland = true, Drifts = true, Leopard = true }; }
        private static IslandMode Cozy() { return new IslandMode { Name = "Уют", Snow = 9, Speed = 53, Glow = 0, Garland = true, Drifts = true, Leopard = true }; }

        private static IslandMode UpdatePreset(IslandMode saved)
        {
            IslandMode preset = null;
            if (saved.Garland && saved.Name == "Праздник" && saved.Snow == 68 && saved.Speed == 105 && saved.Glow == 48) preset = Festive();
            if (saved.Garland && saved.Name == "Уют" && saved.Snow == 27 && saved.Speed == 67 && saved.Glow == 25) preset = Cozy();
            if (preset != null)
            {
                saved.Snow = preset.Snow;
                saved.Speed = preset.Speed;
                saved.Glow = preset.Glow;
            }
            return saved;
        }

        private static bool Flag(XElement element, string attribute, bool fallback)
        {
            bool value;
            return Boolean.TryParse((string)element.Attribute(attribute), out value) ? value : fallback;
        }

        private static int Value(XElement element, string attribute, int fallback, int min, int max)
        {
            int number;
            return Int32.TryParse((string)element.Attribute(attribute), out number) ? Math.Max(min, Math.Min(max, number)) : fallback;
        }

        public IslandModes Copy()
        {
            var copy = new IslandModes
            {
                Active = Active, Best = Best, Feedings = Feedings, GrowthDays = GrowthDays, LastGrowthDay = LastGrowthDay,
                FavoriteFood = FavoriteFood, FavoriteKnown = FavoriteKnown
            };
            copy.Items.AddRange(Items.Select(mode => mode.Copy()));
            return copy;
        }

        public void RecordMeal(DateTime date)
        {
            if (Feedings < Int32.MaxValue) Feedings++;
            if (GrowthDays >= DaysToGrow || date.Date <= LastGrowthDay) return;
            GrowthDays++;
            LastGrowthDay = date.Date;
        }

        public bool RecordTaste(LeopardFood food)
        {
            if (FavoriteFood < 0) FavoriteFood = new Random().Next((int)LeopardFood.Treat + 1);
            if (FavoriteFood != (int)food) return false;
            FavoriteKnown = true;
            return true;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathToModes));
            string pending = PathToModes + ".pending";
            try
            {
                new XElement("modes", new XAttribute("active", Active), new XAttribute("best", Best),
                    new XAttribute("feedings", Feedings), new XAttribute("growthDays", GrowthDays),
                    new XAttribute("favoriteFood", FavoriteFood), new XAttribute("favoriteKnown", FavoriteKnown),
                    new XAttribute("lastGrowthDay", LastGrowthDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Items.Select(mode => new XElement("mode",
                    new XAttribute("name", mode.Name), new XAttribute("snow", mode.Snow), new XAttribute("speed", mode.Speed),
                    new XAttribute("glow", mode.Glow), new XAttribute("garland", mode.Garland),
                    new XAttribute("drifts", mode.Drifts), new XAttribute("leopard", mode.Leopard)))).Save(pending);
                if (File.Exists(PathToModes)) File.Replace(pending, PathToModes, null);
                else File.Move(pending, PathToModes);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    internal sealed class ModeEditor : Form
    {
        private static readonly Color Ground = Color.FromArgb(17, 21, 22);
        private static readonly Color Field = Color.FromArgb(25, 32, 32);
        private static readonly Color TextColor = Color.FromArgb(234, 242, 237);
        private static readonly Color Accent = Color.FromArgb(246, 221, 177);
        private static readonly Color Secondary = Color.FromArgb(162, 187, 178);

        private readonly ListBox list;
        private readonly TextBox name;
        private readonly TrackBar snow, speed, glow;
        private readonly CheckBox garland, drifts, leopard;
        private readonly Button remove;
        private bool changing;

        public IslandModes Result { get; private set; }

        public ModeEditor(IslandModes current)
        {
            Result = current.Copy();
            Text = Program.AppName + " · Режимы";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(522, 357);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            BackColor = Ground;
            ForeColor = TextColor;
            Font = SystemFonts.MessageBoxFont;

            AddLabel("Режимы", 18, 16);
            list = new ListBox
            {
                AccessibleName = "Режим оформления", BackColor = Field, ForeColor = TextColor,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 25, IntegralHeight = false
            };
            list.SetBounds(18, 39, 158, 189);
            list.DrawItem += PaintRow;
            Controls.Add(list);
            MakeButton("Новый", 18, 237, 76, NewMode);
            remove = MakeButton("Удалить", 100, 237, 76, RemoveMode);

            AddLabel("Название", 199, 16);
            name = new TextBox { AccessibleName = "Название режима", MaxLength = 30, BackColor = Field, ForeColor = TextColor };
            name.SetBounds(199, 39, 304, 24);
            Controls.Add(name);
            snow = AddSlider("Снег", 77, 0, 100);
            speed = AddSlider("Скорость снега", 126, 40, 160);
            glow = AddSlider("Мерцание огоньков", 175, 0, 100);
            garland = new CheckBox { Text = "Показывать гирлянду", AutoSize = true, Location = new Point(199, 232) };
            Controls.Add(garland);
            drifts = new CheckBox { Text = "Сугробы на острове", AutoSize = true, Location = new Point(199, 256) };
            Controls.Add(drifts);
            leopard = new CheckBox { Text = "Снежный барс у острова", AutoSize = true, Location = new Point(199, 280) };
            Controls.Add(leopard);

            Button cancel = MakeButton("Отмена", 307, 314, 88, null);
            cancel.DialogResult = DialogResult.Cancel;
            Button apply = MakeButton("Применить", 404, 314, 99, Accept);
            apply.ForeColor = Accent;
            AcceptButton = apply;
            CancelButton = cancel;

            list.SelectedIndexChanged += delegate { ShowMode(); };
            name.TextChanged += delegate { UpdateMode(); };
            snow.ValueChanged += delegate { UpdateMode(); };
            speed.ValueChanged += delegate { UpdateMode(); };
            glow.ValueChanged += delegate { UpdateMode(); };
            garland.CheckedChanged += delegate { UpdateMode(); };
            drifts.CheckedChanged += delegate { UpdateMode(); };
            leopard.CheckedChanged += delegate { UpdateMode(); };
            RefreshList(Result.Active);
            ActiveControl = list;
        }

        private Label AddLabel(string text, int x, int y)
        {
            var label = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            Controls.Add(label);
            return label;
        }

        private TrackBar AddSlider(string title, int top, int min, int max)
        {
            AddLabel(title, 199, top);
            Label value = AddLabel("", 466, top);
            value.ForeColor = Secondary;
            var slider = new TrackBar
            {
                AccessibleName = title, BackColor = Ground, Minimum = min, Maximum = max,
                TickStyle = TickStyle.None, AutoSize = false, SmallChange = 1, LargeChange = 10
            };
            slider.SetBounds(194, top + 18, 314, 25);
            slider.ValueChanged += delegate { value.Text = slider.Value + "%"; };
            value.Text = slider.Value + "%";
            Controls.Add(slider);
            return slider;
        }

        private Button MakeButton(string text, int x, int y, int width, Action action)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Field, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = Color.FromArgb(58, 72, 68);
            button.SetBounds(x, y, width, 27);
            if (action != null) button.Click += delegate { action(); };
            Controls.Add(button);
            return button;
        }

        private void PaintRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var mode = (IslandMode)list.Items[e.Index];
            bool active = (e.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(active ? Color.FromArgb(44, 58, 52) : Field))
                e.Graphics.FillRectangle(background, e.Bounds);
            var line = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(e.Graphics, mode.Name, Font, line, active ? Accent : TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private void RefreshList(int selected)
        {
            changing = true;
            list.Items.Clear();
            list.Items.AddRange(Result.Items.Cast<object>().ToArray());
            list.SelectedIndex = selected;
            changing = false;
            ShowMode();
        }

        private void ShowMode()
        {
            if (changing || list.SelectedIndex < 0) return;
            changing = true;
            IslandMode mode = (IslandMode)list.SelectedItem;
            name.Text = mode.Name;
            snow.Value = mode.Snow;
            speed.Value = mode.Speed;
            glow.Value = mode.Glow;
            garland.Checked = mode.Garland;
            drifts.Checked = mode.Drifts;
            leopard.Checked = mode.Leopard;
            glow.Enabled = mode.Garland;
            speed.Enabled = mode.Snow != 0;
            remove.Enabled = Result.Items.Count > 1;
            changing = false;
        }

        private void UpdateMode()
        {
            if (changing || list.SelectedIndex < 0) return;
            IslandMode mode = (IslandMode)list.SelectedItem;
            mode.Name = name.Text.Trim();
            mode.Snow = snow.Value;
            mode.Speed = speed.Value;
            mode.Glow = glow.Value;
            mode.Garland = garland.Checked;
            mode.Drifts = drifts.Checked;
            mode.Leopard = leopard.Checked;
            glow.Enabled = mode.Garland;
            speed.Enabled = mode.Snow != 0;
            list.Invalidate();
        }

        private void NewMode()
        {
            IslandMode mode = ((IslandMode)list.SelectedItem).Copy();
            mode.Name = "Мой режим";
            for (int n = 2; Result.Items.Any(item => String.Equals(item.Name, mode.Name, StringComparison.CurrentCultureIgnoreCase)); n++)
                mode.Name = "Мой режим " + n;
            Result.Items.Add(mode);
            RefreshList(Result.Items.Count - 1);
            name.Focus();
            name.SelectAll();
        }

        private void RemoveMode()
        {
            if (Result.Items.Count <= 1) return;
            int selected = list.SelectedIndex;
            Result.Items.RemoveAt(selected);
            RefreshList(Math.Min(selected, Result.Items.Count - 1));
        }

        private void Accept()
        {
            int missing = Result.Items.FindIndex(mode => String.IsNullOrWhiteSpace(mode.Name));
            if (missing >= 0)
            {
                list.SelectedIndex = missing;
                name.Focus();
                return;
            }
            Result.Active = list.SelectedIndex;
            DialogResult = DialogResult.OK;
        }
    }
}

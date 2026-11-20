using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class FocusSection : Panel
    {
        private static readonly Color Field = Color.FromArgb(23, 27, 31);
        private readonly float dpi;
        private readonly Font digits;
        private readonly Label countdown, status;
        private readonly Panel progress;
        private readonly TextBox hours, minutes, seconds;
        private readonly IslandSectionTile action, reset;
        private readonly Action<int> start;
        private readonly Action pauseOrContinue;
        private FocusStage state;
        private float elapsed;

        public FocusSection(int width, float scale, Font title, Font body, Font small,
            Action<int> start, Action pauseOrContinue, Action cancel)
        {
            dpi = scale;
            this.start = start;
            this.pauseOrContinue = pauseOrContinue;
            BackColor = IslandSectionTile.Ground;
            Size = new Size(width, Px(177));
            digits = new Font(title.FontFamily, 22f, FontStyle.Regular);
            countdown = new Label { Font = digits, ForeColor = IslandSectionTile.Ink, TextAlign = ContentAlignment.MiddleLeft };
            countdown.SetBounds(Px(5), 0, Math.Max(Px(88), width - Px(154)), Px(49));
            Controls.Add(countdown);
            status = new Label { Font = small, ForeColor = IslandSectionTile.Muted, TextAlign = ContentAlignment.MiddleRight };
            status.SetBounds(Math.Max(Px(84), width - Px(150)), Px(9), Math.Min(Px(146), width - Px(88)), Px(34));
            Controls.Add(status);
            progress = new Panel { BackColor = BackColor };
            progress.SetBounds(Px(5), Px(51), width - Px(10), Px(6));
            progress.Paint += PaintProgress;
            Controls.Add(progress);

            int gap = Px(8), available = width - 2 * gap;
            int[] sizes = { available / 3, 2 * available / 3 - available / 3, available - 2 * available / 3 };
            hours = TimeField("Часы", "00", 0, sizes[0], body, small);
            minutes = TimeField("Минуты", "25", sizes[0] + gap, sizes[1], body, small);
            seconds = TimeField("Секунды", "00", sizes[0] + sizes[1] + 2 * gap, sizes[2], body, small);

            action = new IslandSectionTile(dpi, body, small, false);
            action.SetBounds(0, Px(140), width * 2 / 3 - gap, Px(35));
            action.Click += delegate { PrimaryAction(); };
            Controls.Add(action);
            reset = new IslandSectionTile(dpi, body, small, false) { Text = "Сбросить", AccessibleName = "Сбросить таймер" };
            reset.SetBounds(width * 2 / 3, action.Top, width - width * 2 / 3, action.Height);
            reset.Click += delegate { cancel(); };
            Controls.Add(reset);
            Present(FocusStage.Idle, 0, 0);
        }

        public void Present(FocusStage stage, int remaining, float fraction)
        {
            if (Math.Abs(elapsed - fraction) > .0004f || state != stage)
            {
                elapsed = fraction;
                progress.Invalidate();
            }
            state = stage;
            bool counting = stage == FocusStage.Running || stage == FocusStage.Paused;
            hours.ReadOnly = minutes.ReadOnly = seconds.ReadOnly = counting;
            string label = stage == FocusStage.Running ? "Пауза" : (stage == FocusStage.Paused ? "Продолжить" : "Запустить");
            if (action.Text != label) { action.Text = label; action.AccessibleName = label; }
            reset.Enabled = stage != FocusStage.Idle;
            if (stage == FocusStage.Idle)
            {
                RefreshInput();
                return;
            }
            int duration;
            action.Enabled = counting || ReadTime(out duration);
            countdown.Text = FormatDuration(remaining);
            status.Text = stage == FocusStage.Completed ? "Фокус завершён" : (stage == FocusStage.Paused ? "На паузе" : "Осталось");
            status.ForeColor = stage == FocusStage.Completed ? Color.FromArgb(171, 224, 196) : IslandSectionTile.Muted;
        }

        public static string FormatDuration(int time)
        {
            if (time >= 3600) return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", time / 3600, time / 60 % 60, time % 60);
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", time / 60, time % 60);
        }

        private TextBox TimeField(string name, string initial, int x, int width, Font body, Font small)
        {
            var field = new Panel { BackColor = Field };
            field.SetBounds(x, Px(70), width, Px(38));
            using (GraphicsPath shape = Island.Shape(field.ClientRectangle, Px(8))) field.Region = new Region(shape);
            var input = new TextBox
            {
                Text = initial, Font = body, AccessibleName = name, BackColor = Field, ForeColor = IslandSectionTile.Ink,
                BorderStyle = BorderStyle.None, MaxLength = 2, TextAlign = HorizontalAlignment.Center
            };
            input.SetBounds(Px(4), Px(10), width - Px(8), input.PreferredHeight);
            input.Enter += delegate { input.SelectAll(); };
            input.KeyPress += delegate(object sender, KeyPressEventArgs e)
            {
                if (!Char.IsControl(e.KeyChar) && (e.KeyChar < '0' || e.KeyChar > '9')) e.Handled = true;
            };
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || input.ReadOnly) return;
                e.SuppressKeyPress = true;
                PrimaryAction();
            };
            input.TextChanged += delegate
            {
                if (action == null) return;
                if (state == FocusStage.Idle) RefreshInput();
                else if (state == FocusStage.Completed)
                {
                    int duration;
                    action.Enabled = ReadTime(out duration);
                }
            };
            field.Controls.Add(input);
            Controls.Add(field);
            var unit = new Label { Text = name, Font = small, ForeColor = IslandSectionTile.Muted, TextAlign = ContentAlignment.MiddleCenter };
            unit.SetBounds(x, Px(109), width, Px(20));
            Controls.Add(unit);
            return input;
        }

        private void PrimaryAction()
        {
            if (state == FocusStage.Running || state == FocusStage.Paused)
            {
                pauseOrContinue();
                return;
            }
            int value;
            if (ReadTime(out value)) start(value);
            else RefreshInput();
        }

        private bool ReadTime(out int time)
        {
            time = 0;
            int h, m, s;
            if (!Int32.TryParse(hours.Text, out h) || !Int32.TryParse(minutes.Text, out m) || !Int32.TryParse(seconds.Text, out s) ||
                h < 0 || h > 99 || m < 0 || m > 59 || s < 0 || s > 59) return false;
            time = h * 3600 + m * 60 + s;
            return time > 0;
        }

        private void RefreshInput()
        {
            int duration;
            bool valid = ReadTime(out duration);
            countdown.Text = valid ? FormatDuration(duration) : "00:00";
            status.Text = valid ? "Готов к запуску" : "Укажи время";
            status.ForeColor = IslandSectionTile.Muted;
            action.Enabled = valid;
        }

        private void PaintProgress(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath track = Island.Shape(progress.ClientRectangle, Px(3)))
            using (var empty = new SolidBrush(Color.FromArgb(43, 48, 51)))
            using (var filled = new SolidBrush(state == FocusStage.Completed ? Color.FromArgb(171, 224, 196) : IslandSectionTile.Accent))
            {
                e.Graphics.FillPath(empty, track);
                if (elapsed > 0)
                {
                    e.Graphics.SetClip(track);
                    e.Graphics.FillRectangle(filled, 0, 0, elapsed * progress.Width, progress.Height);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) digits.Dispose();
        }

        private int Px(int value) { return (int)Math.Round(value * dpi); }
    }
}

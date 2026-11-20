using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class CompanionSection : Panel
    {
        private static readonly string[] MealNames = { "Мясо", "Рыба", "Лакомство" };
        private readonly Label age, progress;
        private readonly Panel meter;
        private readonly IslandSectionTile play, snowball;
        private readonly IslandSectionTile[] meals = new IslandSectionTile[3];
        private readonly Bitmap[] foodIcons = new Bitmap[3];
        private readonly Bitmap playIcon, stopIcon, snowballIcon;
        private readonly float scale;
        private int days = -1;
        private int knownFavorite = -1;
        private bool leopardEnabled;

        public CompanionSection(int width, float dpi, Font title, Font text, Font detail,
            Action<LeopardFood> feed, Action playGame, Action toggleSnowball)
        {
            scale = dpi;
            BackColor = IslandSectionTile.Ground;
            Size = new Size(width, Px(181));
            age = new Label { Font = title, ForeColor = IslandSectionTile.Ink, TextAlign = ContentAlignment.MiddleLeft };
            age.SetBounds(Px(4), Px(1), Math.Max(Px(74), width - Px(106)), Px(30));
            Controls.Add(age);
            progress = new Label { Font = detail, ForeColor = IslandSectionTile.Muted, TextAlign = ContentAlignment.MiddleRight };
            progress.SetBounds(Math.Max(Px(76), width - Px(104)), Px(3), Math.Min(Px(100), Math.Max(Px(40), width - Px(80))), Px(26));
            Controls.Add(progress);
            meter = new Panel();
            meter.SetBounds(Px(4), Px(34), width - Px(8), Px(5));
            meter.Paint += PaintMeter;
            Controls.Add(meter);

            int gap = Px(8), available = width - gap * 2;
            for (int i = 0; i < meals.Length; i++)
            {
                LeopardFood food = (LeopardFood)i;
                foodIcons[i] = IslandSectionIcons.Food(food, Px(39));
                var tile = new IslandSectionTile(dpi, text, detail, true)
                {
                    Text = MealNames[i], Icon = foodIcons[i], AccessibleName = "Кормление · " + MealNames[i]
                };
                int left = available * i / 3 + i * gap;
                tile.SetBounds(left, Px(55), available * (i + 1) / 3 + i * gap - left, Px(78));
                tile.Click += delegate { feed(food); };
                meals[i] = tile;
                Controls.Add(tile);
            }
            playIcon = IslandSectionIcons.Glyph(SectionGlyph.Play, Px(20));
            stopIcon = IslandSectionIcons.Glyph(SectionGlyph.Stop, Px(20));
            snowballIcon = DesktopGame.MakeSnowball(Px(8));
            play = new IslandSectionTile(dpi, text, detail, false) { Icon = playIcon };
            int toyWidth = Math.Min(Px(142), width / 3);
            play.SetBounds(0, Px(145), width - toyWidth - gap, Px(35));
            play.Click += delegate { playGame(); };
            Controls.Add(play);
            snowball = new IslandSectionTile(dpi, text, detail, false)
            {
                Text = "Снежок", AccessibleName = "Игра со снежком",
                AccessibleDescription = "Потяни снежок на рабочем столе и отпусти", Icon = snowballIcon
            };
            snowball.SetBounds(width - toyWidth, play.Top, toyWidth, play.Height);
            snowball.Click += delegate { toggleSnowball(); };
            Controls.Add(snowball);
        }

        public void Present(int growthDays, bool leopardVisible, bool canFeed, bool canPlay, bool playing, bool playingToy,
            int favorite)
        {
            if (days != growthDays || leopardEnabled != leopardVisible)
            {
                days = growthDays;
                leopardEnabled = leopardVisible;
                age.Text = leopardVisible
                    ? (days >= IslandModes.DaysToGrow ? "Взрослый барс" : "Барсёнок")
                    : "Барс выключен";
                progress.Text = days + " / " + IslandModes.DaysToGrow + " дней";
                meter.Invalidate();
            }
            foreach (IslandSectionTile meal in meals) meal.Enabled = canFeed;
            if (knownFavorite != favorite)
            {
                knownFavorite = favorite;
                for (int i = 0; i < meals.Length; i++)
                {
                    bool liked = i == favorite;
                    meals[i].Text = MealNames[i] + (liked ? "  \u2665" : "");
                    meals[i].AccessibleName = (liked ? "Любимая еда · " : "Кормление · ") + MealNames[i];
                    meals[i].Selected = liked;
                }
            }
            string action = playing ? "Закончить игру" : "Поиграть с курсором";
            if (play.Text != action)
            {
                play.Text = action;
                play.AccessibleName = action;
                play.Icon = playing ? stopIcon : playIcon;
            }
            play.Selected = playing;
            play.Enabled = playing || canPlay;
            snowball.Enabled = playing || canPlay;
            snowball.Selected = playingToy;
        }

        private void PaintMeter(object sender, PaintEventArgs e)
        {
            int count = IslandModes.DaysToGrow;
            int gap = Px(4), available = meter.Width - gap * (count - 1);
            if (available < count) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var ready = new SolidBrush(IslandSectionTile.Accent))
            using (var pending = new SolidBrush(Color.FromArgb(42, 46, 50)))
                for (int i = 0; i < count; i++)
                {
                    int left = i * available / count + i * gap;
                    int width = (i + 1) * available / count + i * gap - left;
                    using (GraphicsPath shape = Island.Shape(new Rectangle(left, 0, width, meter.Height), meter.Height / 2))
                        e.Graphics.FillPath(i < days ? ready : pending, shape);
                }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            foreach (Bitmap icon in foodIcons) if (icon != null) icon.Dispose();
            if (playIcon != null) playIcon.Dispose();
            if (stopIcon != null) stopIcon.Dispose();
            if (snowballIcon != null) snowballIcon.Dispose();
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }
    }

    internal sealed class ModesSection : Panel
    {
        private const int VisibleModes = 3;
        private readonly Panel list;
        private readonly List<IslandSectionTile> rows = new List<IslandSectionTile>();
        private readonly Bitmap snow, cozy, focus, edit, up, down;
        private readonly IslandSectionTile settings, previous, next;
        private readonly float scale;
        private readonly Font text, detail;
        private readonly Action<int> select;
        private int firstVisible;

        public ModesSection(int width, float dpi, Font textFont, Font detailFont, Action<int> selectMode, Action editModes)
        {
            scale = dpi;
            text = textFont;
            detail = detailFont;
            select = selectMode;
            BackColor = IslandSectionTile.Ground;
            Size = new Size(width, Px(192));
            snow = IslandSectionIcons.Glyph(SectionGlyph.Snow, Px(24));
            cozy = IslandSectionIcons.Glyph(SectionGlyph.Cozy, Px(24));
            focus = IslandSectionIcons.Glyph(SectionGlyph.Focus, Px(24));
            edit = IslandSectionIcons.Glyph(SectionGlyph.Settings, Px(20));
            up = IslandSectionIcons.Glyph(SectionGlyph.Up, Px(16));
            down = IslandSectionIcons.Glyph(SectionGlyph.Down, Px(16));
            list = new Panel { BackColor = BackColor };
            list.SetBounds(0, 0, width, Px(148));
            list.MouseWheel += ScrollWheel;
            Controls.Add(list);
            settings = new IslandSectionTile(dpi, text, detail, false)
            {
                Text = "Настроить режимы", AccessibleName = "Настроить режимы", Icon = edit
            };
            settings.SetBounds(0, Px(156), width, Px(35));
            settings.Click += delegate { editModes(); };
            Controls.Add(settings);
            previous = new IslandSectionTile(dpi, text, detail, false)
                { AccessibleName = "Предыдущие режимы", Icon = up };
            previous.SetBounds(width - Px(76), settings.Top, Px(34), settings.Height);
            previous.Click += delegate { MoveRows(-1); };
            next = new IslandSectionTile(dpi, text, detail, false)
                { AccessibleName = "Следующие режимы", Icon = down };
            next.SetBounds(width - Px(34), settings.Top, Px(34), settings.Height);
            next.Click += delegate { MoveRows(1); };
            Controls.Add(previous);
            Controls.Add(next);
        }

        public void Present(IslandModes current)
        {
            list.SuspendLayout();
            while (rows.Count > current.Items.Count)
            {
                int last = rows.Count - 1;
                rows[last].Dispose();
                rows.RemoveAt(last);
            }
            for (int i = 0; i < current.Items.Count; i++)
            {
                int index = i;
                if (i >= rows.Count)
                {
                    var created = new IslandSectionTile(scale, text, detail, false);
                    created.Click += delegate { select(index); };
                    created.MouseWheel += ScrollWheel;
                    rows.Add(created);
                    list.Controls.Add(created);
                }
                IslandMode mode = current.Items[i];
                IslandSectionTile row = rows[i];
                row.Text = mode.Name;
                row.AccessibleName = mode.Name;
                row.Selected = i == current.Active;
                row.Caption = Description(mode);
                row.Icon = mode.Snow >= 24 ? snow : (mode.Garland || mode.Drifts ? cozy : focus);
                row.Invalidate();
            }
            if (current.Active < firstVisible) firstVisible = current.Active;
            else if (current.Active >= firstVisible + VisibleModes) firstVisible = current.Active - VisibleModes + 1;
            MoveRows(0);
            list.ResumeLayout(true);
        }

        private void ScrollWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta == 0 || rows.Count <= VisibleModes) return;
            MoveRows(e.Delta > 0 ? -1 : 1);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        private void MoveRows(int delta)
        {
            int lastPage = Math.Max(0, rows.Count - VisibleModes);
            firstVisible = Math.Max(0, Math.Min(lastPage, firstVisible + delta));
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i - firstVisible;
                IslandSectionTile row = rows[i];
                row.Visible = index >= 0 && index < VisibleModes;
                if (index >= 0 && index < VisibleModes)
                    row.SetBounds(0, index * Px(50), list.Width, Px(46));
            }
            previous.Visible = next.Visible = lastPage > 0;
            previous.Enabled = firstVisible > 0;
            next.Enabled = firstVisible < lastPage;
            settings.Width = Width - (lastPage > 0 ? Px(84) : 0);
        }

        private static string Description(IslandMode mode)
        {
            var details = new List<string>(4);
            if (mode.Snow > 0) details.Add("Снег " + mode.Snow + "%");
            if (mode.Garland) details.Add("Гирлянда");
            if (mode.Drifts && !mode.Garland) details.Add("Сугробы");
            if (mode.Leopard) details.Add("Барс");
            return details.Count == 0 ? "Без зимних эффектов" : String.Join("  ·  ", details.ToArray());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            if (snow != null) snow.Dispose();
            if (cozy != null) cozy.Dispose();
            if (focus != null) focus.Dispose();
            if (edit != null) edit.Dispose();
            if (up != null) up.Dispose();
            if (down != null) down.Dispose();
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }
    }

    internal sealed class IslandSectionTile : Button
    {
        internal static readonly Color Ground = Color.FromArgb(7, 7, 8);
        internal static readonly Color Ink = Color.FromArgb(238, 245, 246);
        internal static readonly Color Muted = Color.FromArgb(159, 172, 179);
        internal static readonly Color Accent = Color.FromArgb(247, 219, 170);
        private readonly float scale;
        private readonly Font detail;
        private readonly bool vertical;
        private bool hovered, selected;
        public string Caption;
        public Bitmap Icon;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                Invalidate();
            }
        }

        public IslandSectionTile(float dpi, Font font, Font detailFont, bool verticalTile)
        {
            scale = dpi;
            detail = detailFont;
            vertical = verticalTile;
            Font = font;
            BackColor = Ground;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics canvas = e.Graphics;
            canvas.Clear(BackColor);
            if (Width < 3 || Height < 3) return;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            bool hot = hovered && Enabled;
            Color background = selected ? Color.FromArgb(41, 33, 24) :
                (hot ? Color.FromArgb(31, 35, 40) : Color.FromArgb(19, 22, 25));
            Color border = selected ? Color.FromArgb(133, 103, 65) :
                (hot || (Focused && ShowFocusCues) ? Color.FromArgb(91, 90, 86) : Color.FromArgb(35, 40, 45));
            using (GraphicsPath shape = Island.Shape(new Rectangle(1, 1, Width - 2, Height - 2), Px(10)))
            using (var fill = new SolidBrush(background))
            using (var edge = new Pen(border))
            {
                canvas.FillPath(fill, shape);
                canvas.DrawPath(edge, shape);
            }
            int iconLeft = Icon != null && (vertical || String.IsNullOrEmpty(Text)) ? (Width - Icon.Width) / 2 : Px(10);
            int iconTop = vertical ? Px(hot ? 6 : 8) : (Height - (Icon == null ? 0 : Icon.Height)) / 2;
            if (Icon != null)
            {
                if (Enabled) canvas.DrawImageUnscaled(Icon, iconLeft, iconTop);
                else using (var muted = new ImageAttributes())
                {
                    muted.SetColorMatrix(new ColorMatrix { Matrix33 = .43f });
                    canvas.DrawImage(Icon, new Rectangle(iconLeft, iconTop, Icon.Width, Icon.Height),
                        0, 0, Icon.Width, Icon.Height, GraphicsUnit.Pixel, muted);
                }
            }
            Color ink = Enabled ? (selected ? Accent : Ink) : Muted;
            const TextFormatFlags layout = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (vertical)
            {
                TextRenderer.DrawText(canvas, Text, Font, new Rectangle(Px(4), Height - Px(27), Width - Px(8), Px(21)),
                    ink, layout | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int left = Icon == null ? Px(12) : iconLeft + Icon.Width + Px(10);
                int textWidth = Math.Max(1, Width - left - Px(selected ? 38 : 12));
                if (String.IsNullOrEmpty(Caption))
                    TextRenderer.DrawText(canvas, Text, Font, new Rectangle(left, 0, textWidth, Height),
                        ink, layout | TextFormatFlags.VerticalCenter);
                else
                {
                    TextRenderer.DrawText(canvas, Text, Font, new Rectangle(left, Px(5), textWidth, Px(17)), ink, layout);
                    TextRenderer.DrawText(canvas, Caption, detail, new Rectangle(left, Px(24), textWidth, Px(16)), Muted, layout);
                }
                if (selected)
                {
                    int dotX = Width - Px(26), dotY = Height / 2;
                    using (var mark = new Pen(Accent, Math.Max(1, Px(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        canvas.DrawLines(mark, new[] { new Point(dotX, dotY), new Point(dotX + Px(4), dotY + Px(4)),
                            new Point(dotX + Px(11), dotY - Px(4)) });
                }
            }
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }
    }

    internal enum SectionGlyph { Snow, Cozy, Focus, Settings, Play, Stop, Up, Down }

    internal static class IslandSectionIcons
    {
        public static Bitmap Food(LeopardFood food, int size)
        {
            var icon = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics canvas = Graphics.FromImage(icon))
            {
                canvas.SmoothingMode = SmoothingMode.AntiAlias;
                canvas.ScaleTransform(size / 48f, size / 48f);
                using (var shadow = new SolidBrush(Color.FromArgb(85, 0, 0, 0))) canvas.FillEllipse(shadow, 5, 32, 38, 11);
                if (food == LeopardFood.Meat) Meat(canvas);
                else if (food == LeopardFood.Fish) Fish(canvas);
                else Treat(canvas);
            }
            return icon;
        }

        private static void Meat(Graphics canvas)
        {
            using (var shape = new GraphicsPath())
            {
                shape.AddBezier(8, 19, 15, 11, 24, 6, 35, 12);
                shape.AddBezier(35, 12, 44, 15, 44, 23, 37, 27);
                shape.AddBezier(37, 27, 29, 31, 32, 36, 18, 36);
                shape.AddBezier(18, 36, 8, 36, 4, 32, 6, 24);
                shape.AddLine(6, 24, 8, 19);
                shape.CloseFigure();
                using (var side = new SolidBrush(Color.FromArgb(112, 40, 47)))
                using (var filling = new LinearGradientBrush(new Point(0, 11), new Point(0, 36),
                    Color.FromArgb(226, 124, 120), Color.FromArgb(157, 51, 75)))
                using (var rind = new Pen(Color.FromArgb(239, 174, 136), 1.7f))
                using (var marble = new Pen(Color.FromArgb(182, 247, 190, 161), 1.32f)
                    { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    canvas.TranslateTransform(0, 3);
                    canvas.FillPath(side, shape);
                    canvas.TranslateTransform(0, -3);
                    canvas.FillPath(filling, shape);
                    canvas.DrawPath(rind, shape);
                    canvas.DrawBezier(marble, 13, 28, 25, 30, 23, 17, 34, 18);
                    canvas.DrawBezier(marble, 12, 22, 18, 20, 24, 23, 23, 31);
                    canvas.DrawBezier(marble, 22, 15, 27, 14, 30, 16, 33, 15);
                }
            }
        }

        private static void Fish(Graphics canvas)
        {
            using (var tail = new SolidBrush(Color.FromArgb(103, 147, 167)))
            using (var body = new LinearGradientBrush(new Point(0, 15), new Point(0, 34),
                Color.FromArgb(97, 151, 171), Color.FromArgb(197, 226, 229)))
            using (var rim = new Pen(Color.FromArgb(171, 211, 219), 1.25f))
            using (var eye = new SolidBrush(Color.FromArgb(25, 46, 55)))
            using (var scales = new Pen(Color.FromArgb(85, 225, 246, 248), 1))
            {
                canvas.RotateTransform(-13);
                canvas.TranslateTransform(-4, 5);
                canvas.FillPolygon(tail, new[] { new Point(32, 24), new Point(42, 16), new Point(41, 24),
                    new Point(43, 31), new Point(33, 26) });
                canvas.FillPolygon(tail, new[] { new Point(17, 18), new Point(25, 12), new Point(29, 20) });
                canvas.FillEllipse(body, 5, 17, 30, 16);
                canvas.DrawEllipse(rim, 5, 17, 30, 16);
                canvas.FillEllipse(eye, 9.5f, 22, 2.9f, 2.9f);
                canvas.DrawArc(scales, 12, 19, 9, 12, -76, 140);
                canvas.DrawArc(scales, 18, 21, 10, 8, -54, 116);
                canvas.DrawArc(scales, 23, 21, 9, 8, -64, 124);
            }
        }

        private static void Treat(Graphics canvas)
        {
            using (var shade = new SolidBrush(Color.FromArgb(116, 67, 45)))
            using (var cookie = new LinearGradientBrush(new Point(0, 10), new Point(0, 37),
                Color.FromArgb(232, 170, 113), Color.FromArgb(154, 86, 56)))
            using (var edge = new Pen(Color.FromArgb(220, 146, 93), 1.55f))
            using (var spots = new SolidBrush(Color.FromArgb(135, 101, 56, 39)))
            {
                canvas.FillEllipse(shade, 10, 15, 29, 25);
                canvas.FillEllipse(cookie, 9, 10, 30, 26);
                canvas.DrawEllipse(edge, 9, 10, 30, 26);
                canvas.FillEllipse(spots, 16, 17, 4, 3);
                canvas.FillEllipse(spots, 24, 24, 4, 3.5f);
                canvas.FillEllipse(spots, 29, 15, 3, 3);
                canvas.FillEllipse(spots, 17, 28, 2.5f, 3);
                canvas.FillEllipse(spots, 32, 25, 2.7f, 3);
            }
        }

        public static Bitmap Glyph(SectionGlyph kind, int size)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics canvas = Graphics.FromImage(bitmap))
            {
                canvas.SmoothingMode = SmoothingMode.AntiAlias;
                canvas.ScaleTransform(size / 24f, size / 24f);
                var tint = kind == SectionGlyph.Snow ? Color.FromArgb(173, 215, 234) : IslandSectionTile.Accent;
                using (var line = new Pen(tint, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var fill = new SolidBrush(tint))
                {
                    if (kind == SectionGlyph.Play)
                        canvas.FillPolygon(fill, new[] { new PointF(7, 4), new PointF(20, 12), new PointF(7, 20) });
                    else if (kind == SectionGlyph.Stop)
                        canvas.FillRectangle(fill, 6, 6, 12, 12);
                    else if (kind == SectionGlyph.Up || kind == SectionGlyph.Down)
                    {
                        float tip = kind == SectionGlyph.Up ? 8 : 16, ends = kind == SectionGlyph.Up ? 15 : 9;
                        canvas.DrawLines(line, new[] { new PointF(5, ends), new PointF(12, tip), new PointF(19, ends) });
                    }
                    else if (kind == SectionGlyph.Focus)
                        canvas.DrawArc(line, 5, 4, 14, 15, 14, 253);
                    else if (kind == SectionGlyph.Cozy)
                    {
                        canvas.DrawEllipse(line, 8, 7, 8, 8);
                        canvas.DrawLine(line, 12, 3, 12, 5);
                        canvas.DrawLine(line, 12, 17, 12, 20);
                        canvas.DrawLine(line, 4, 11, 6, 11);
                        canvas.DrawLine(line, 18, 11, 20, 11);
                        canvas.DrawLine(line, 6, 5, 8, 7);
                        canvas.DrawLine(line, 17, 16, 19, 18);
                    }
                    else if (kind == SectionGlyph.Settings)
                    {
                        canvas.DrawLine(line, 5, 6, 19, 6);
                        canvas.DrawLine(line, 5, 12, 19, 12);
                        canvas.DrawLine(line, 5, 18, 19, 18);
                        canvas.FillEllipse(fill, 8, 4, 4, 4);
                        canvas.FillEllipse(fill, 14, 10, 4, 4);
                        canvas.FillEllipse(fill, 9, 16, 4, 4);
                    }
                    else
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            canvas.DrawLine(line, 12, 12, 12, 3.1f);
                            canvas.DrawLines(line, new[] { new PointF(9.6f, 5.7f), new PointF(12, 8.1f), new PointF(14.4f, 5.7f) });
                            canvas.TranslateTransform(12, 12);
                            canvas.RotateTransform(60);
                            canvas.TranslateTransform(-12, -12);
                        }
                    }
                }
            }
            return bitmap;
        }
    }
}

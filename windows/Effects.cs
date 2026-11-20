using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class WinterScene : IDisposable
    {
        private const int FlakeLimit = 46, LightFlakeLimit = 16;
        private readonly Form owner;
        private readonly float scale;
        private readonly Timer animation = new Timer();
        private readonly Stopwatch age = new Stopwatch();
        private readonly AlphaOverlay garland;
        private readonly AlphaOverlay snow;
        private readonly WindowSnow settledSnow;
        private readonly Leopard leopard;
        private readonly SnowLanding landings;
        private readonly SnowballToy toy;
        private Stream textureStream, driftStream;
        private Bitmap texture, driftAtlas, trimFrame;
        private Rectangle firArea;
        private Point trimPosition;
        private readonly Random weather = new Random();
        private readonly List<Flake> flakes = new List<Flake>(FlakeLimit);
        private readonly GraphicsPath flakeShape = CreateFlake();
        private readonly Pen flakeInk = new Pen(Color.AliceBlue) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        private readonly Pen flakeShade = new Pen(Color.SlateGray);
        private double lastFrame, snowTime, nextFlake, nextGlow, nextWeather, nextDesktopCheck;
        private double rippleAt = Double.NegativeInfinity;
        private float rippleOrigin;
        private bool desktopActive, ripplePainted;
        private Point snowOrigin = new Point(Int32.MinValue, Int32.MinValue);
        private Rectangle capsule, panel;
        private IslandMode mode;

        private static readonly Point[] Lamps =
        {
            new Point(228, 263), new Point(1912, 260), new Point(231, 461), new Point(430, 474),
            new Point(765, 474), new Point(1464, 467), new Point(1694, 469), new Point(1946, 456)
        };

        private static readonly Rectangle LeftDrift = new Rectangle(13, 282, 614, 164);
        private static readonly Rectangle MiddleDrift = new Rectangle(647, 267, 890, 182);
        private static readonly Rectangle RightDrift = new Rectangle(1546, 283, 616, 164);

        private sealed class Flake
        {
            public double Born, Life, X, Drop, Drift, Lean, Phase, Sway, Turn, Angle;
            public float Radius;
            public int Brightness;
            public PointF Previous;
            public bool Tracked;
        }

        public event Action Frame;
        public bool LightSnow { get; set; }

        public WinterScene(Form form, float screenScale, IslandMode initial, int growthDays,
            Func<bool> allowFeeding, Action requestFeed)
        {
            owner = form;
            scale = screenScale;
            garland = new AlphaOverlay(form, "WinterIsland.Garland");
            snow = new AlphaOverlay(form, "WinterIsland.Snow");
            settledSnow = new WindowSnow(form, scale);
            landings = new SnowLanding(form, scale);
            toy = new SnowballToy(form, scale);
            leopard = new Leopard(form, scale, allowFeeding, requestFeed);
            leopard.Landed += landings.Leave;
            leopard.SetGrowth(growthDays);
            Apply(initial);
            animation.Tick += delegate
            {
                double now = age.Elapsed.TotalSeconds;
                double step = Math.Min(.09, now - lastFrame);
                lastFrame = now;
                if (owner.Visible && owner.WindowState != FormWindowState.Minimized)
                {
                    if (leopard.Playing && now >= nextDesktopCheck)
                    {
                        desktopActive = allowFeeding() && WindowsShell.DesktopIsForeground(owner.Handle);
                        nextDesktopCheck = now + .10;
                    }
                    Rectangle walkway = panel.IsEmpty ? capsule : owner.Bounds;
                    Point? toyTarget = AdvanceToy((float)step);
                    leopard.Animate(walkway, capsule, now, panel.IsEmpty, desktopActive, toyTarget);
                    if (toyTarget.HasValue) toy.KeepBehind(leopard.SpriteLayer);
                    if (toyTarget.HasValue && toy.Thrown && !toy.Dragging && !toy.Airborne)
                    {
                        PointF push;
                        if (leopard.TryTapSnowball(toy.Position, now, out push)) toy.Tap(push);
                    }
                    if (desktopActive)
                    {
                        landings.Draw(now);
                        landings.KeepBehind(leopard.SpriteLayer);
                    }
                    else landings.Hide();
                    float brush;
                    if (mode.Garland && leopard.TryBrushGarland(capsule, now, out brush))
                    {
                        rippleAt = now;
                        rippleOrigin = brush;
                        nextGlow = 0;
                    }
                    snowTime += step;
                    if (now >= nextWeather)
                    {
                        PaintSnow(now);
                        Action frame = Frame;
                        if (frame != null) frame();
                        nextWeather = Math.Max(now, nextWeather + 1.0 / 30);
                    }
                    if (mode.Garland && trimFrame != null && now >= nextGlow &&
                        (mode.Glow > 0 || now - rippleAt < 1.13 || ripplePainted))
                    {
                        PaintGarland(now);
                        nextGlow = now + .084;
                    }
                }
                else HideLayers();
            };

            textureStream = typeof(WinterScene).Assembly.GetManifestResourceStream("WinterIsland.Garland");
            if (textureStream != null)
                texture = new Bitmap(textureStream);
            driftStream = typeof(WinterScene).Assembly.GetManifestResourceStream("WinterIsland.SnowDrifts");
            if (driftStream != null)
                driftAtlas = new Bitmap(driftStream);
        }

        public void Start()
        {
            age.Start();
            owner.VisibleChanged += VisibilityChanged;
            VisibilityChanged(owner, EventArgs.Empty);
        }

        private void VisibilityChanged(object sender, EventArgs e)
        {
            if (owner.Visible)
            {
                lastFrame = age.Elapsed.TotalSeconds;
                animation.Start();
            }
            else
            {
                animation.Stop();
                HideLayers();
            }
        }

        private void HideLayers()
        {
            garland.Hide();
            leopard.Hide();
            toy.Hide();
            landings.Hide();
            snow.Hide();
            settledSnow.Hide();
            flakes.Clear();
            nextFlake = snowTime;
        }

        public void Apply(IslandMode selected)
        {
            bool restartingSnow = mode == null || mode.Snow != selected.Snow || mode.Speed != selected.Speed;
            bool rebuildingTrim = mode == null || mode.Garland != selected.Garland || mode.Drifts != selected.Drifts;
            mode = selected;
            animation.Interval = mode.Leopard ? 25 : (mode.Snow > 0 ? 34 : 90);
            double now = age.Elapsed.TotalSeconds;
            leopard.SetEnabled(mode.Leopard, now);
            if (!mode.Leopard)
            {
                toy.Hide();
                landings.Hide();
            }
            if (!mode.Garland) { rippleAt = Double.NegativeInfinity; ripplePainted = false; }
            if (restartingSnow)
            {
                flakes.Clear();
                snow.Hide();
                nextFlake = snowTime;
                if (mode.Snow == 0)
                {
                    settledSnow.Clear();
                    leopard.ClearWeatherSnow();
                }
            }
            if (rebuildingTrim && trimFrame != null)
            {
                trimFrame.Dispose();
                trimFrame = null;
            }
            if (!mode.Garland && !mode.Drifts) garland.Hide();
            else if (capsule.Width > 0 && owner.IsHandleCreated && owner.Visible)
            {
                if (trimFrame == null) Align(capsule.Width, capsule.Height, panel);
                else PaintGarland(now);
            }
        }

        public void Align(int width, int capsuleHeight, Rectangle expandedPanel)
        {
            capsule = new Rectangle(owner.Left + (owner.Width - width) / 2, owner.Top, width, capsuleHeight);
            panel = expandedPanel;
            if (((!mode.Drifts || driftAtlas == null) && (!mode.Garland || texture == null)) || !owner.IsHandleCreated || !owner.Visible) return;
            int margin = Px(7);
            int overhead = mode.Drifts ? Px(19) : margin;
            var point = new Point(capsule.Left - margin, capsule.Top - overhead);
            var size = new Size(width + margin * 2, capsuleHeight + overhead + margin);
            firArea = new Rectangle(0, overhead - margin, width + 2 * margin, capsuleHeight + 2 * margin);
            bool sameSize = trimFrame != null && size == trimFrame.Size;
            if (sameSize && point == trimPosition && garland.Visible) return;
            trimPosition = point;
            if (!sameSize)
            {
                if (trimFrame != null) trimFrame.Dispose();
                trimFrame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (Graphics canvas = Graphics.FromImage(trimFrame))
                {
                    if (mode.Garland && texture != null) PaintTrim(canvas, firArea);
                    if (mode.Drifts && driftAtlas != null) PaintSnowDrifts(canvas, new Rectangle(margin, overhead, width, capsuleHeight));
                }
            }
            PaintGarland(age.Elapsed.TotalSeconds);
        }

        private void PaintGarland(double time)
        {
            Size size = trimFrame.Size;
            Graphics canvas = garland.Begin(size);
            canvas.DrawImageUnscaled(trimFrame, 0, 0);
            ripplePainted = mode.Garland && time - rippleAt < 1.13;
            if (mode.Garland && (mode.Glow > 0 || ripplePainted) && texture != null)
            {
                int band = Math.Max(8, firArea.Height / 5);
                canvas.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < Lamps.Length; i++)
                {
                    Point lamp = Lamps[i];
                    float fraction = (lamp.X - 62) / 2048f;
                    float x = fraction * firArea.Width;
                    float y = lamp.Y < 299 ? firArea.Top + (lamp.Y - 195) * band / 104f : firArea.Bottom - band + (lamp.Y - 432) * band / 96f;
                    double phase = time * (1.27 + i * .074) + i * 1.9;
                    double wave = .5 + .5 * Math.Sin(phase);
                    float level = (float)((.12 + .88 * wave * wave) * mode.Glow / 100);
                    level = Math.Min(1, level + RippleStrength(time - rippleAt, rippleOrigin, fraction));
                    if (level < .012f) continue;
                    Color light = i == 3 || i == 6 ? Color.FromArgb(255, 105, 110) : Color.FromArgb(255, 175, 73);
                    Glow(canvas, x, y, light, level);
                }
            }
            garland.Show(trimPosition);
        }

        internal static float RippleStrength(double elapsed, float origin, float lamp)
        {
            double phase = elapsed - Math.Abs(origin - lamp) * .63;
            if (phase <= 0 || phase >= .46) return 0;
            return (float)(.86 * Ease(phase / .10) * Ease((.46 - phase) / .31));
        }

        private void Glow(Graphics canvas, float x, float y, Color light, float level)
        {
            float radius = (4.8f + level * 6.5f) * scale;
            var halo = new RectangleF(x - radius, y - radius, 2 * radius, 2 * radius);
            using (var circle = new GraphicsPath())
            {
                circle.AddEllipse(halo);
                using (var glow = new PathGradientBrush(circle))
                {
                    glow.CenterColor = Color.FromArgb((int)(187 * level), light);
                    glow.SurroundColors = new[] { Color.FromArgb(0, light) };
                    canvas.FillEllipse(glow, halo);
                }
            }
            using (var glint = new SolidBrush(Color.FromArgb((int)(130 * level), 255, 238, 184)))
                canvas.FillEllipse(glint, x - scale, y - scale, 2 * scale, 2 * scale);
        }

        private void PaintTrim(Graphics canvas, Rectangle area)
        {
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int band = Math.Max(8, area.Height / 5);
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                DrawBand(canvas, new Rectangle(area.Left, area.Top, area.Width, band), 195, 104, attributes);
                DrawBand(canvas, new Rectangle(area.Left, area.Top + band, area.Width, area.Height - band * 2), 299, 133, attributes);
                DrawBand(canvas, new Rectangle(area.Left, area.Bottom - band, area.Width, band), 432, 96, attributes);
            }
        }

        private void DrawBand(Graphics canvas, Rectangle area, int sourceY, int sourceHeight, ImageAttributes attributes)
        {
            canvas.DrawImage(texture, area, 62, sourceY, 2048, sourceHeight, GraphicsUnit.Pixel, attributes);
        }

        private void PaintSnowDrifts(Graphics canvas, Rectangle edge)
        {
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                PlaceDrift(canvas, edge, LeftDrift, .161f, .239f, Px(8), attributes);
                PlaceDrift(canvas, edge, MiddleDrift, .478f, .319f, Px(6), attributes);
                PlaceDrift(canvas, edge, RightDrift, .832f, .247f, Px(8), attributes);
            }
        }

        private void PlaceDrift(Graphics canvas, Rectangle edge, Rectangle source, float at, float proportion,
            int overlap, ImageAttributes attributes)
        {
            int width = (int)Math.Round(edge.Width * proportion);
            int height = Math.Min(Px(20), (int)Math.Round((float)source.Height * width / source.Width));
            var cap = new Rectangle(edge.Left + (int)Math.Round(edge.Width * at) - width / 2,
                edge.Top + overlap - height, width, height);
            canvas.DrawImage(driftAtlas, cap, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        public bool HitLeopard(PointF from, PointF to, float radius)
        {
            return leopard.TryHit(from, to, radius, age.Elapsed.TotalSeconds);
        }

        public bool CanFeed { get { return leopard.CanFeed; } }
        public bool CanPlay { get { return leopard.CanPlay; } }
        public bool Playing { get { return leopard.Playing; } }
        public bool PlayingToy { get { return leopard.Playing && toy.Enabled; } }

        public bool IsPointerOverLeopard(Point point) { return leopard.IsPointerOver(point); }

        public void Feed(int growthDays, LeopardFood food, bool favorite)
        {
            leopard.Feed(growthDays, food, favorite, age.Elapsed.TotalSeconds);
        }

        public void StartPlaying(bool snowball = false)
        {
            nextDesktopCheck = 0;
            if (!leopard.Playing) leopard.StartPlaying();
            if (!leopard.Playing) return;
            if (snowball) toy.Start();
            else toy.Stop();
        }

        public void StopPlaying()
        {
            leopard.StopPlaying();
            toy.Stop();
        }

        public void FocusEnded() { leopard.FocusEnded(); }

        private Point? AdvanceToy(float dt)
        {
            if (!PlayingToy || !desktopActive || !mode.Leopard || capsule.IsEmpty)
            {
                toy.Hide();
                leopard.ClearToyTap();
                return null;
            }
            var spawn = new Point(capsule.Left + capsule.Width / 4, capsule.Bottom + Px(107));
            return toy.Move(Screen.FromRectangle(capsule).WorkingArea, spawn, Control.MousePosition,
                (Control.MouseButtons & MouseButtons.Left) != 0, dt);
        }

        private void PaintSnow(double now)
        {
            if (mode.Snow == 0 || capsule.Height <= 0) return;
            leopard.SettleFineSnow(mode.Snow, now);
            var size = new Size(Px(468), Px(325));
            var at = new Point(capsule.Left + (capsule.Width - size.Width) / 2, capsule.Bottom - Px(1));
            settledSnow.Prepare(new Rectangle(at, size), panel, Control.MousePosition, now);
            bool originMoved = at != snowOrigin;
            snowOrigin = at;
            for (int i = flakes.Count - 1; i >= 0; i--)
                if (snowTime >= flakes[i].Born + flakes[i].Life) flakes.RemoveAt(i);
            if (snowTime >= nextFlake)
            {
                int limit = LightSnow ? LightFlakeLimit : FlakeLimit;
                if (flakes.Count < limit) flakes.Add(NewFlake());
                double rate = Math.Max(.25, mode.Snow * .053);
                if (LightSnow) rate = Math.Min(rate, Math.Min(2.4, mode.Speed * .021));
                nextFlake = snowTime + Range(.68, 1.32) / rate;
            }

            if (flakes.Count == 0) { snow.Hide(); settledSnow.Paint(now); return; }
            Graphics canvas = snow.Begin(size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = flakes.Count - 1; i >= 0; i--)
            {
                Flake flake = flakes[i];
                double time = snowTime - flake.Born;
                double phase = time / flake.Life;
                double fade = Ease(time / .28) * Ease((flake.Life - time) / .9);
                double sway = (Math.Sin(time * flake.Sway + flake.Phase) - Math.Sin(flake.Phase)) * flake.Drift;
                float x = size.Width / 2f + (float)(flake.X + sway + phase * flake.Lean) * scale;
                float y = (float)(1.8 + phase * flake.Drop) * scale;
                float radius = flake.Radius * scale;
                var point = new PointF(at.X + x, at.Y + y);
                if (flake.Tracked && !originMoved)
                {
                    WindowSnow.Landing landed;
                    bool touchedWindow = settledSnow.FirstLanding(flake.Previous, point, out landed);
                    if (leopard.TryCatchSnow(flake.Previous, touchedWindow ? landed.Point : point, radius, now))
                    {
                        flakes.RemoveAt(i);
                        continue;
                    }
                    if (touchedWindow)
                    {
                        settledSnow.Deposit(landed, radius, now);
                        flakes.RemoveAt(i);
                        continue;
                    }
                }
                flake.Previous = point;
                flake.Tracked = true;
                int alpha = (int)(flake.Brightness * fade);
                if (alpha < 2) continue;
                GraphicsState state = canvas.Save();
                canvas.TranslateTransform(x, y);
                canvas.RotateTransform((float)(flake.Angle + time * flake.Turn));
                canvas.ScaleTransform(radius, radius);
                flakeShade.Color = Color.FromArgb(alpha / 3, 32, 54, 77);
                flakeShade.Width = 1.65f / radius;
                flakeInk.Color = Color.FromArgb(alpha, 235, 248, 255);
                flakeInk.Width = .91f / radius;
                canvas.DrawPath(flakeShade, flakeShape);
                canvas.DrawPath(flakeInk, flakeShape);
                canvas.Restore(state);
            }
            if (flakes.Count > 0) snow.Show(at);
            else snow.Hide();
            settledSnow.Paint(now);
        }

        private Flake NewFlake()
        {
            double sides = Math.Min(185, Math.Max(30, capsule.Width / (2 * scale) - 16));
            double drop = LightSnow ? Range(150, 215) : Range(255, 312);
            return new Flake
            {
                Born = snowTime, Life = drop * 100 / (Range(24, 40) * mode.Speed), X = Range(-sides, sides),
                Drop = drop, Drift = Range(5, 14), Lean = Range(-27, 27), Phase = Range(0, 2 * Math.PI),
                Sway = Range(.65, 1.8), Turn = Range(-36, 36), Angle = Range(0, 60),
                Radius = (float)Range(2.15, LightSnow ? 4.35 : 5), Brightness = (int)Range(153, 239)
            };
        }

        private static GraphicsPath CreateFlake()
        {
            var crystal = new GraphicsPath();
            for (int i = 0; i < 6; i++)
            {
                float x = (float)Math.Cos(i * Math.PI / 3), y = (float)Math.Sin(i * Math.PI / 3);
                crystal.StartFigure();
                crystal.AddLine(0, 0, x, y);
                crystal.StartFigure();
                crystal.AddLines(new[]
                {
                    new PointF(.78f * x - .23f * y, .78f * y + .23f * x),
                    new PointF(.54f * x, .54f * y),
                    new PointF(.78f * x + .23f * y, .78f * y - .23f * x)
                });
            }
            return crystal;
        }

        private double Range(double minimum, double maximum) { return minimum + weather.NextDouble() * (maximum - minimum); }

        private static double Ease(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        private int Px(int logical) { return (int)Math.Round(logical * scale); }

        public void Dispose()
        {
            owner.VisibleChanged -= VisibilityChanged;
            animation.Stop();
            animation.Dispose();
            garland.Dispose();
            snow.Dispose();
            settledSnow.Dispose();
            landings.Dispose();
            toy.Dispose();
            leopard.Dispose();
            flakeShape.Dispose();
            flakeInk.Dispose();
            flakeShade.Dispose();
            if (trimFrame != null) trimFrame.Dispose();
            if (texture != null) texture.Dispose();
            if (textureStream != null) textureStream.Dispose();
            if (driftAtlas != null) driftAtlas.Dispose();
            if (driftStream != null) driftStream.Dispose();
        }
    }

    internal sealed class AlphaOverlay : NativeWindow, IDisposable
    {
        private readonly Form owner;
        private readonly string caption;
        private readonly Func<Point, bool> hitTest;
        private readonly Action click;
        private readonly Action<Point> press;
        private IntPtr dc, pixels, nativeBitmap, previousBitmap;
        private Bitmap bitmap;
        private Graphics canvas;
        private Size size;
        private IntPtr below;
        private Point location;
        private Point? pressed;

        public bool Visible { get; private set; }

        public AlphaOverlay(Form form, string windowCaption, Func<Point, bool> hitTest = null, Action click = null,
            Action<Point> press = null)
        {
            owner = form;
            caption = windowCaption;
            this.hitTest = hitTest;
            this.click = click;
            this.press = press;
        }

        public Graphics Begin(Size requested)
        {
            EnsureSurface(requested);
            canvas.CompositingMode = CompositingMode.SourceCopy;
            canvas.Clear(Color.Transparent);
            canvas.CompositingMode = CompositingMode.SourceOver;
            return canvas;
        }

        public void Show(Point location)
        {
            if (canvas == null) return;
            if (Handle == IntPtr.Zero)
            {
                CreateHandle(new CreateParams
                {
                    Caption = caption,
                    Parent = owner.Handle,
                    Style = unchecked((int)0x80000000),
                    ExStyle = 0x00080000 | 0x08000000 | 0x80 | 0x08 | (hitTest == null ? 0x20 : 0)
                });
            }

            canvas.Flush(FlushIntention.Sync);
            var source = new NativePoint();
            var target = new NativePoint { X = location.X, Y = location.Y };
            var dimensions = new NativeSize { Width = size.Width, Height = size.Height };
            var blend = new Blend { Alpha = 255, Format = 1 };
            if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref target, ref dimensions, dc,
                    ref source, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            this.location = location;

            if (!Visible)
            {
                ShowWindow(Handle, 4);
                Visible = true;
            }
        }

        public void Hide()
        {
            if (Visible && Handle != IntPtr.Zero) ShowWindow(Handle, 0);
            Visible = false;
            pressed = null;
            below = IntPtr.Zero;
        }

        public void KeepBehind(AlphaOverlay foreground)
        {
            if (!Visible || !foreground.Visible || below == foreground.Handle) return;
            const uint keepSizePlaceAndOwner = 0x0001 | 0x0002 | 0x0010 | 0x0200;
            if (SetWindowPos(Handle, foreground.Handle, 0, 0, 0, 0, keepSizePlaceAndOwner))
                below = foreground.Handle;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84)
            {
                message.Result = new IntPtr(hitTest != null && hitTest(MousePoint(message)) ? 1 : -1);
                return;
            }
            if (message.Msg == 0x21)
            {
                message.Result = new IntPtr(3);
                return;
            }
            if (hitTest != null && message.Msg == 0x201)
            {
                Point cursor = Point.Add(MousePoint(message), (Size)location);
                pressed = hitTest(cursor) ? (Point?)cursor : null;
                if (pressed.HasValue && press != null) press(pressed.Value);
                return;
            }
            if (hitTest != null && message.Msg == 0x202)
            {
                Point cursor = Point.Add(MousePoint(message), (Size)location);
                Point? origin = pressed;
                pressed = null;
                Size slop = SystemInformation.DragSize;
                if (click != null && origin.HasValue && Math.Abs(cursor.X - origin.Value.X) <= slop.Width &&
                    Math.Abs(cursor.Y - origin.Value.Y) <= slop.Height && hitTest(cursor))
                    click();
                return;
            }
            if (hitTest != null && message.Msg == 0x20 && (message.LParam.ToInt64() & 0xffff) == 1)
            {
                Cursor.Current = Cursors.Hand;
                message.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref message);
        }

        private static Point MousePoint(Message message)
        {
            long coordinates = message.LParam.ToInt64();
            return new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16)));
        }

        private void EnsureSurface(Size requested)
        {
            if (requested == size && canvas != null) return;
            ReleaseSurface();
            size = requested;
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo
            {
                Header = 40,
                Width = size.Width,
                Height = -size.Height,
                Planes = 1,
                Bits = 32
            };
            try
            {
                nativeBitmap = CreateDIBSection(dc, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (nativeBitmap == IntPtr.Zero || pixels == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                previousBitmap = SelectObject(dc, nativeBitmap);
                if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                {
                    previousBitmap = IntPtr.Zero;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, pixels);
                canvas = Graphics.FromImage(bitmap);
            }
            catch
            {
                ReleaseSurface();
                throw;
            }
        }

        private void ReleaseSurface()
        {
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (bitmap != null) { bitmap.Dispose(); bitmap = null; }
            if (dc != IntPtr.Zero && previousBitmap != IntPtr.Zero)
                SelectObject(dc, previousBitmap);
            if (nativeBitmap != IntPtr.Zero) DeleteObject(nativeBitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            previousBitmap = nativeBitmap = pixels = dc = IntPtr.Zero;
            size = Size.Empty;
        }

        public void Dispose()
        {
            Hide();
            if (Handle != IntPtr.Zero) DestroyHandle();
            ReleaseSurface();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int Header, Width, Height;
            public ushort Planes, Bits;
            public uint Compression, ImageSize;
            public int XPixelsPerMeter, YPixelsPerMeter;
            public uint ColorsUsed, ImportantColors, ColorEntry;
        }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr screen, ref NativePoint target, ref NativeSize size, IntPtr image,
            ref NativePoint source, uint key, ref Blend blend, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr src);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(
            IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}

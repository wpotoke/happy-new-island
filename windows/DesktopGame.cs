using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class DesktopGame : IDisposable
    {
        private const int EscapeKey = 0x5B41, SpaceKey = 0x5B42;
        private const uint NoRepeat = 0x4000;
        private static readonly Color Ice = Color.FromArgb(205, 249, 244);
        private static readonly Color Gold = Color.FromArgb(248, 211, 144);

        private readonly Form owner;
        private readonly Func<Rectangle> target;
        private readonly Func<PointF, PointF, float, bool> hitLeopard;
        private readonly float scale;
        private readonly Timer timer = new Timer { Interval = 20 };
        private readonly Stopwatch time = new Stopwatch();
        private readonly AlphaOverlay rim, paddle, puck, hud;
        private readonly Font font = new Font("Segoe UI Semibold", 9f);
        private readonly Bitmap snowball;
        private readonly Random direction = new Random();
        private Rectangle area, lastPaddle;
        private PointF ball, velocity;
        private float paddleX;
        private int score, best, lives;
        private double lastTick, serveAt, hitUntil, nextFocusCheck;
        private bool paused, hasForeground, escapeRegistered, spaceRegistered;
        private string lastStatus;

        public event Action<int> Finished;
        public bool Active { get { return timer.Enabled; } }

        public DesktopGame(Form form, float dpi, Func<Rectangle> islandBounds, Func<PointF, PointF, float, bool> leopardCollision)
        {
            owner = form;
            scale = dpi;
            target = islandBounds;
            hitLeopard = leopardCollision;
            rim = new AlphaOverlay(form, "HappyNewIsland.GameHit");
            paddle = new AlphaOverlay(form, "HappyNewIsland.GamePaddle");
            puck = new AlphaOverlay(form, "HappyNewIsland.GameBall");
            hud = new AlphaOverlay(form, "HappyNewIsland.GameScore");
            snowball = MakeSnowball((int)Radius);
            timer.Tick += delegate { Tick(); };
        }

        public void Start(int record)
        {
            if (Active) return;
            area = Screen.FromHandle(owner.Handle).WorkingArea;
            best = record;
            time.Restart();
            lastTick = nextFocusCheck = hitUntil = 0;
            hasForeground = false;
            lastStatus = null;
            lastPaddle = Rectangle.Empty;
            NewRound();
            timer.Start();
        }

        public bool HandleKey(Keys key)
        {
            if (!Active) return false;
            if (key == Keys.Escape) { Stop(); return true; }
            if (key != Keys.Space) return false;
            if (lives == 0) NewRound();
            else
            {
                paused = !paused;
                if (!paused && serveAt >= 0) serveAt = time.Elapsed.TotalSeconds + .65;
            }
            return true;
        }

        public bool HandleHotkey(int id)
        {
            if (!Active || !WindowsShell.DesktopIsForeground(owner.Handle)) return false;
            if (id == EscapeKey) return HandleKey(Keys.Escape);
            if (id == SpaceKey) return HandleKey(Keys.Space);
            return false;
        }

        private void NewRound()
        {
            score = 0;
            lives = 3;
            paused = false;
            paddleX = PointerX();
            ServeAfter(.8);
        }

        private void ServeAfter(double seconds)
        {
            serveAt = time.Elapsed.TotalSeconds + seconds;
            SetBallOnPaddle();
            velocity = PointF.Empty;
        }

        private void SetBallOnPaddle() { ball = new PointF(paddleX, Paddle.Top - Radius - Px(4)); }

        private void Tick()
        {
            double now = time.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(.033, now - lastTick);
            lastTick = now;
            if (now >= nextFocusCheck)
            {
                bool foreground = WindowsShell.DesktopIsForeground(owner.Handle);
                if (hasForeground && !foreground) paused = true;
                if (!hasForeground && foreground && serveAt >= 0 && !paused) serveAt = now + .65;
                hasForeground = foreground;
                BindKeys(foreground);
                nextFocusCheck = now + .16;
            }
            if (!hasForeground || !owner.Visible)
            {
                HideSprites();
                return;
            }

            paddleX += (PointerX() - paddleX) * Math.Min(1, 24 * dt);
            if (!paused && lives > 0)
            {
                if (serveAt >= 0)
                {
                    SetBallOnPaddle();
                    if (now >= serveAt)
                    {
                        Rectangle island = target();
                        float towards = (island.Left + island.Width / 2f - ball.X) * .63f;
                        velocity = new PointF(Math.Max(-Px(240), Math.Min(Px(240), towards)) + direction.Next(-Px(36), Px(36)), -Px(453));
                        serveAt = -1;
                    }
                }
                else Advance(dt);
            }
            if (paused && serveAt >= 0) SetBallOnPaddle();
            DrawPaddle();
            DrawBall();
            DrawHit(now);
            DrawStatus();
        }

        private void Advance(float step)
        {
            if (step <= 0) return;
            PointF previous = ball;
            ball = new PointF(ball.X + velocity.X * step, ball.Y + velocity.Y * step);
            float leftWall = area.Left + Radius, rightWall = area.Right - Radius;
            if (ball.X < leftWall && velocity.X < 0)
            {
                ball.X = 2 * leftWall - ball.X;
                velocity.X = -velocity.X;
            }
            else if (ball.X > rightWall && velocity.X > 0)
            {
                ball.X = 2 * rightWall - ball.X;
                velocity.X = -velocity.X;
            }

            if (hitLeopard != null && hitLeopard(previous, ball, Radius))
            {
                ball = previous;
                Rectangle island = target();
                float away = ball.X < island.Left + island.Width / 2f ? -1 : 1;
                velocity = new PointF(velocity.X * .69f + away * Px(116), -velocity.Y);
                return;
            }

            if (velocity.Y < 0)
            {
                Rectangle island = target();
                float edge = island.Bottom + Radius;
                if (previous.Y >= edge && ball.Y <= edge)
                {
                    float x = CrossingX(previous, edge);
                    if (x >= island.Left - Radius && x <= island.Right + Radius)
                    {
                        score++;
                        best = Math.Max(score, best);
                        hitUntil = time.Elapsed.TotalSeconds + .46;
                        ball.Y = 2 * edge - ball.Y;
                        velocity.Y = Math.Min(Px(665), -velocity.Y + Px(14));
                        velocity.X += (x - island.Left - island.Width / 2f) * .35f;
                    }
                }
                float ceiling = area.Top + Radius;
                if (ball.Y < ceiling && velocity.Y < 0)
                {
                    ball.Y = 2 * ceiling - ball.Y;
                    velocity.Y = -velocity.Y;
                }
            }
            else if (velocity.Y > 0)
            {
                Rectangle bat = Paddle;
                float edge = bat.Top - Radius;
                if (previous.Y <= edge && ball.Y >= edge)
                {
                    float x = CrossingX(previous, edge);
                    if (x >= bat.Left - Radius && x <= bat.Right + Radius)
                    {
                        float bias = (x - paddleX) / (bat.Width / 2f + Radius);
                        ball.Y = 2 * edge - ball.Y;
                        velocity = new PointF(bias * Px(380) + velocity.X * .14f, -Px(453 + Math.Min(180, score * 13)));
                    }
                }
                if (ball.Y - Radius > area.Bottom)
                {
                    lives--;
                    ServeAfter(.95);
                }
            }
        }

        private float CrossingX(PointF previous, float y)
        {
            float distance = ball.Y - previous.Y;
            return distance == 0 ? ball.X : previous.X + (ball.X - previous.X) * (y - previous.Y) / distance;
        }

        private float Radius { get { return Px(8); } }
        private int PaddleWidth { get { return Math.Min(Px(132), Math.Max(Px(76), area.Width / 5)); } }
        private Rectangle Paddle
        {
            get { return new Rectangle((int)Math.Round(paddleX) - PaddleWidth / 2, area.Bottom - Px(61), PaddleWidth, Px(16)); }
        }

        private float PointerX()
        {
            return Math.Max(area.Left + PaddleWidth / 2f, Math.Min(area.Right - PaddleWidth / 2f, Cursor.Position.X));
        }

        private void DrawPaddle()
        {
            Rectangle at = Paddle;
            if (paddle.Visible && at == lastPaddle) return;
            lastPaddle = at;
            var size = new Size(at.Width + Px(12), at.Height + Px(12));
            Graphics canvas = paddle.Begin(size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            var bar = new Rectangle(Px(6), Px(6), at.Width, at.Height);
            using (var fill = new LinearGradientBrush(bar, Color.FromArgb(22, 22, 24), Color.FromArgb(3, 3, 4), 90f))
            using (var edge = new Pen(Color.FromArgb(171, 170, 181, 187), Math.Max(1, scale)))
            using (var curve = Island.Shape(bar, Px(8)))
            {
                canvas.FillPath(fill, curve);
                canvas.DrawPath(edge, curve);
            }
            using (var shine = new Pen(Color.FromArgb(61, 233, 243, 246), scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                canvas.DrawLine(shine, bar.Left + Px(10), bar.Top + Px(4), bar.Right - Px(10), bar.Top + Px(4));
            paddle.Show(new Point(at.X - Px(6), at.Y - Px(6)));
        }

        private void DrawBall()
        {
            if (lives == 0) { puck.Hide(); return; }
            int size = Px(92);
            float center = size * .5f;
            Graphics canvas = puck.Begin(new Size(size, size));
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            if (!paused && serveAt < 0)
            {
                for (int i = 5; i >= 1; i--)
                {
                    float r = scale * (2.7f - i * .33f);
                    float drift = (float)Math.Sin(ball.Y * .041 + i * 2.25) * scale * 2.2f;
                    float x = center - velocity.X * i * .008f + drift, y = center - velocity.Y * i * .008f;
                    using (var trace = new SolidBrush(Color.FromArgb(142 - 19 * i, 219, 236, 247)))
                        canvas.FillEllipse(trace, x - r, y - r, r * 2, r * 2);
                }
            }
            canvas.DrawImageUnscaled(snowball, (int)Math.Round(center - snowball.Width / 2f),
                (int)Math.Round(center - snowball.Height / 2f));
            puck.Show(new Point((int)Math.Round(ball.X - center), (int)Math.Round(ball.Y - center)));
        }

        internal static Bitmap MakeSnowball(int size)
        {
            const int detail = 4;
            int pixels = (size * 2 + 4) * detail;
            float center = pixels / 2f;
            float radius = size * detail;
            var rim = new PointF[22];
            for (int i = 0; i < rim.Length; i++)
            {
                double angle = i * (2 * Math.PI / rim.Length);
                float shape = radius * (float)(.968 + .028 * Math.Sin(angle * 5 + .25) + .025 * Math.Sin(angle * 9 - .47));
                rim[i] = new PointF(center + shape * (float)Math.Cos(angle), center + shape * (float)Math.Sin(angle));
            }

            using (var high = new Bitmap(pixels, pixels, PixelFormat.Format32bppPArgb))
            using (Graphics paint = Graphics.FromImage(high))
            using (var silhouette = new GraphicsPath())
            {
                paint.SmoothingMode = SmoothingMode.AntiAlias;
                silhouette.AddClosedCurve(rim, .49f);
                using (var shadow = new Pen(Color.FromArgb(118, 39, 51, 67), detail * 1.5f)) paint.DrawPath(shadow, silhouette);
                using (var surface = new PathGradientBrush(silhouette))
                {
                    surface.CenterPoint = new PointF(center - radius * .27f, center - radius * .34f);
                    surface.CenterColor = Color.FromArgb(255, 255, 255, 253);
                    surface.SurroundColors = new[] { Color.FromArgb(255, 166, 189, 210) };
                    paint.FillPath(surface, silhouette);
                }

                var powder = new Random(29);
                var state = paint.Save();
                paint.SetClip(silhouette);
                using (var grain = new SolidBrush(Color.White))
                {
                    for (int i = 0; i < 230; i++)
                    {
                        double angle = powder.NextDouble() * 2 * Math.PI;
                        float distance = (float)Math.Sqrt(powder.NextDouble()) * radius;
                        float x = center + distance * (float)Math.Cos(angle), y = center + distance * (float)Math.Sin(angle);
                        float speck = (float)(.035 + .069 * powder.NextDouble()) * radius;
                        grain.Color = i % 3 == 0 ? Color.FromArgb(91, 118, 149, 178) : Color.FromArgb(170, 255, 255, 255);
                        paint.FillEllipse(grain, x, y, speck, speck * .75f);
                    }
                }
                paint.Restore(state);
                var scaled = new Bitmap(pixels / detail, pixels / detail, PixelFormat.Format32bppPArgb);
                using (Graphics finish = Graphics.FromImage(scaled))
                {
                    finish.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    finish.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    finish.DrawImage(high, new Rectangle(Point.Empty, scaled.Size));
                }
                return scaled;
            }
        }

        private void DrawHit(double now)
        {
            if (hitUntil <= now) { rim.Hide(); return; }
            Rectangle at = Rectangle.Inflate(target(), Px(9), Px(9));
            Graphics canvas = rim.Begin(at.Size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            double strength = Math.Min(1, (hitUntil - now) / .46);
            using (var glow = new Pen(Color.FromArgb((int)(226 * strength), Gold), Px(2)))
            using (var shape = Island.Shape(new Rectangle(Px(4), Px(4), at.Width - Px(8), at.Height - Px(8)), at.Height / 2))
                canvas.DrawPath(glow, shape);
            rim.Show(at.Location);
        }

        private void DrawStatus()
        {
            string label = score + "   " + new string('●', lives) + new string('○', 3 - lives) + "   Рекорд " + best;
            string hint = lives == 0 ? "Пробел — заново · Esc — выход" : paused ? "Пауза · Пробел — играть" : "Пробел — пауза · Esc — выход";
            string status = label + "\n" + hint;
            if (hud.Visible && status == lastStatus) return;
            lastStatus = status;
            var size = new Size(Px(326), Px(37));
            Graphics canvas = hud.Begin(size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var fill = new SolidBrush(Color.FromArgb(230, 11, 19, 21)))
            using (var ink = new SolidBrush(Ice))
            using (var shade = new SolidBrush(Color.FromArgb(156, 191, 186)))
            using (var path = Island.Shape(new Rectangle(0, 0, size.Width - 1, size.Height - 1), Px(11)))
            using (var centered = new StringFormat { Alignment = StringAlignment.Center })
            {
                canvas.FillPath(fill, path);
                canvas.DrawString(label, font, ink, new RectangleF(0, Px(1), size.Width, Px(17)), centered);
                canvas.DrawString(hint, font, shade, new RectangleF(0, Px(18), size.Width, Px(17)), centered);
            }
            hud.Show(new Point(area.Left + (area.Width - size.Width) / 2, area.Bottom - size.Height - Px(5)));
        }

        private void BindKeys(bool enable)
        {
            if (enable)
            {
                if (!escapeRegistered) escapeRegistered = RegisterHotKey(owner.Handle, EscapeKey, NoRepeat, (uint)Keys.Escape);
                if (!spaceRegistered) spaceRegistered = RegisterHotKey(owner.Handle, SpaceKey, NoRepeat, (uint)Keys.Space);
            }
            else
            {
                if (escapeRegistered) UnregisterHotKey(owner.Handle, EscapeKey);
                if (spaceRegistered) UnregisterHotKey(owner.Handle, SpaceKey);
                escapeRegistered = spaceRegistered = false;
            }
        }

        private void HideSprites()
        {
            paddle.Hide(); puck.Hide(); rim.Hide(); hud.Hide();
        }

        public void Stop()
        {
            if (!Active) return;
            timer.Stop();
            time.Stop();
            BindKeys(false);
            HideSprites();
            Action<int> handler = Finished;
            if (handler != null) handler(best);
        }

        private int Px(int value) { return Math.Max(1, (int)Math.Round(value * scale)); }

        public void Dispose()
        {
            Stop();
            timer.Dispose();
            rim.Dispose(); paddle.Dispose(); puck.Dispose(); hud.Dispose();
            snowball.Dispose();
            font.Dispose();
        }

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}

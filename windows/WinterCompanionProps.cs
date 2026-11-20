using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class SnowballToy : IDisposable
    {
        private readonly AlphaOverlay overlay;
        private readonly float dpi;
        private Bitmap sprite;
        private Vector2 position, velocity, lastPointer, dragOffset, throwVelocity;
        private bool placed, dragging;
        private float angle, lastAngle = Single.NaN, lift, liftSpeed, lastLift;
        private Point lastLocation;

        public SnowballToy(Form owner, float scale)
        {
            dpi = scale;
            overlay = new AlphaOverlay(owner, "HappyNewIsland.SnowballToy", IsOver, press: Grab);
        }

        public bool Enabled { get; private set; }
        public bool Thrown { get; private set; }
        public bool Dragging { get { return dragging; } }
        public bool Airborne { get { return lift > .25f; } }
        public PointF Position { get { return new PointF(position.X, position.Y); } }

        public void Start()
        {
            Enabled = true;
            placed = false;
            Thrown = false;
            velocity = Vector2.Zero;
            angle = lift = liftSpeed = 0;
        }

        public void Stop()
        {
            Enabled = false;
            Hide();
        }

        public Point? Move(Rectangle area, Point spawn, Point cursor, bool held, float dt)
        {
            if (!Enabled || area.Width < Px(30) || area.Height < Px(30)) return null;
            if (!placed)
            {
                position = new Vector2(spawn.X, spawn.Y);
                placed = true;
            }
            var pointer = new Vector2(cursor.X, cursor.Y);
            dt = Math.Max(.002f, Math.Min(.065f, dt));
            Rectangle limits = Rectangle.Inflate(area, -Px(15), -Px(15));
            Vector2 before = position;
            if (dragging && held)
            {
                position = pointer + dragOffset;
                Vector2 motion = Limit((pointer - lastPointer) / dt, Px(860));
                throwVelocity += (motion - throwVelocity) * (1 - (float)Math.Exp(-19 * dt));
                lastPointer = pointer;
            }
            else
            {
                if (dragging)
                {
                    dragging = false;
                    Vector2 lastStroke = Limit((pointer - lastPointer) / dt, Px(860));
                    velocity = Limit(Vector2.Lerp(throwVelocity, lastStroke, .27f), Px(710));
                    Thrown = true;
                }
                position += velocity * dt;
                if (position.X < limits.Left || position.X > limits.Right) velocity.X *= -.61f;
                if (position.Y < limits.Top || position.Y > limits.Bottom) velocity.Y *= -.61f;
                velocity *= (float)Math.Exp(-1.7 * dt);
                if (velocity.LengthSquared() < Px(7) * Px(7)) velocity = Vector2.Zero;
                if (lift > 0 || liftSpeed > 0)
                {
                    liftSpeed -= Px(740) * dt;
                    lift += liftSpeed * dt;
                    if (lift < 0) lift = liftSpeed = 0;
                }
            }
            position = Clamp(position, limits);
            angle = (angle + (position.X - before.X) * 57.3f / Px(11)) % 360;
            Draw();
            Vector2 ahead = Clamp(position + velocity * .17f, limits);
            return Point.Round(new PointF(ahead.X, ahead.Y));
        }

        public void Tap(PointF direction)
        {
            if (dragging) return;
            var push = new Vector2(direction.X, direction.Y);
            if (push.LengthSquared() < .01f) push = Vector2.UnitX;
            velocity = Limit(velocity * .65f + Vector2.Normalize(push) * Px(198), Px(430));
            liftSpeed = Px(153);
        }

        public void Hide()
        {
            dragging = false;
            overlay.Hide();
        }

        public void KeepBehind(AlphaOverlay foreground) { overlay.KeepBehind(foreground); }

        private bool IsOver(Point pointer)
        {
            if (!overlay.Visible || !Enabled) return false;
            var offset = new Vector2(pointer.X, pointer.Y + lift) - position;
            return offset.LengthSquared() < Px(14) * Px(14);
        }

        private void Grab(Point cursor)
        {
            if (!IsOver(cursor)) return;
            dragging = true;
            lastPointer = new Vector2(cursor.X, cursor.Y);
            position.Y -= lift;
            lift = liftSpeed = 0;
            dragOffset = position - lastPointer;
            throwVelocity = Vector2.Zero;
            velocity = Vector2.Zero;
        }

        private void Draw()
        {
            if (sprite == null) sprite = DesktopGame.MakeSnowball(Px(10));
            int size = sprite.Width + Px(6);
            int headroom = Px(24);
            var at = new Point((int)Math.Round(position.X - size / 2f), (int)Math.Round(position.Y - size / 2f) - headroom);
            if (overlay.Visible && at == lastLocation && Math.Abs(lastAngle - angle) < .4f && Math.Abs(lastLift - lift) < .24f) return;
            Graphics canvas = overlay.Begin(new Size(size, size + headroom));
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var shadow = new SolidBrush(Color.FromArgb(84, 8, 15, 24)))
                canvas.FillEllipse(shadow, size * .19f, headroom + size * .70f, size * .62f, size * .19f);
            canvas.TranslateTransform(size / 2f, headroom + size / 2f - lift);
            canvas.RotateTransform(angle);
            canvas.DrawImage(sprite, -sprite.Width / 2f, -sprite.Height / 2f, sprite.Width, sprite.Height);
            canvas.ResetTransform();
            overlay.Show(at);
            lastLocation = at;
            lastAngle = angle;
            lastLift = lift;
        }

        private static Vector2 Clamp(Vector2 point, Rectangle bounds)
        {
            return new Vector2(Math.Max(bounds.Left, Math.Min(bounds.Right, point.X)),
                Math.Max(bounds.Top, Math.Min(bounds.Bottom, point.Y)));
        }

        private static Vector2 Limit(Vector2 vector, float top)
        {
            float speed = vector.Length();
            return speed > top ? vector * (top / speed) : vector;
        }

        public void Dispose()
        {
            overlay.Dispose();
            if (sprite != null) sprite.Dispose();
        }

        private int Px(int value) { return Math.Max(1, (int)Math.Round(value * dpi)); }
    }

    internal sealed class SnowLanding : IDisposable
    {
        private sealed class Burst
        {
            public PointF Point;
            public double Started;
            public float Heading;
            public float[] Drift;
        }

        private readonly List<Burst> marks = new List<Burst>(4);
        private readonly AlphaOverlay overlay;
        private readonly Random random = new Random();
        private readonly float dpi;
        private const double Life = 3.4;
        private const float PowderLife = .94f;

        public SnowLanding(Form owner, float scale)
        {
            dpi = scale;
            overlay = new AlphaOverlay(owner, "HappyNewIsland.SnowLanding");
        }

        public void Leave(PointF point, float orientation, double now)
        {
            if (marks.Count >= 4) marks.RemoveAt(0);
            var wind = new float[28];
            for (int i = 0; i < wind.Length; i++) wind[i] = (float)random.NextDouble() * 2 - 1;
            marks.Add(new Burst { Point = point, Started = now, Heading = orientation, Drift = wind });
        }

        public void Draw(double now)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
                if (now - marks[i].Started >= Life) marks.RemoveAt(i);
            if (marks.Count == 0) { overlay.Hide(); return; }
            int spread = (int)Math.Round(81 * dpi);
            Rectangle bounds = Rectangle.Empty;
            foreach (Burst burst in marks)
            {
                var at = new Rectangle((int)burst.Point.X - spread, (int)burst.Point.Y - spread, spread * 2, spread * 2);
                bounds = bounds.IsEmpty ? at : Rectangle.Union(bounds, at);
            }
            Graphics canvas = overlay.Begin(bounds.Size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var powder = new SolidBrush(Color.White))
            using (var shade = new SolidBrush(Color.Black))
                foreach (Burst burst in marks)
                {
                    float age = (float)(now - burst.Started);
                    float x = burst.Point.X - bounds.X, y = burst.Point.Y - bounds.Y;
                    float clear = (float)Math.Max(0, Math.Min(1, (Life - age) / 1.15));
                    powder.Color = Color.FromArgb((int)(173 * clear), 223, 241, 253);
                    shade.Color = Color.FromArgb((int)(92 * clear), 34, 63, 83);
                    GraphicsState state = canvas.Save();
                    canvas.TranslateTransform(x, y);
                    canvas.RotateTransform(burst.Heading * 32);
                    canvas.ScaleTransform(dpi, dpi);
                    Paw(canvas, shade, -14, -2.6f);
                    Paw(canvas, shade, 10, 4.1f);
                    Paw(canvas, powder, -14, -4);
                    Paw(canvas, powder, 10, 2.7f);
                    canvas.Restore(state);
                    if (age >= PowderLife) continue;
                    float sweep = age / PowderLife;
                    float cloud = Math.Min(1, age / .07f) * (1 - sweep) * (1 - sweep);
                    for (int side = -1; side <= 1; side += 2)
                        PaintPowderCloud(canvas, x + side * (18 + 43 * sweep) * dpi, y - (1 + 14 * sweep) * dpi,
                            (19 + 34 * sweep) * dpi, (13 + 14 * sweep) * dpi, (int)(159 * cloud));
                    for (int i = 0; i < burst.Drift.Length; i++)
                    {
                        float drift = burst.Drift[i];
                        float px = x + (drift * 20 + drift * 63 * age) * dpi;
                        float py = y + (i % 5 - (38 + i % 12 * 3) * age + 27 * age * age) * dpi;
                        float grain = (1.35f + 2.75f * Math.Abs(drift)) * dpi;
                        powder.Color = Color.FromArgb((int)(219 * (1 - sweep)), 231, 245, 255);
                        canvas.FillEllipse(powder, px, py, grain, grain * .75f);
                    }
                }
            overlay.Show(bounds.Location);
        }

        private static void PaintPowderCloud(Graphics canvas, float x, float y, float width, float height, int opacity)
        {
            if (opacity < 4) return;
            using (var shape = new GraphicsPath())
            {
                shape.AddEllipse(x - width / 2, y - height / 2, width, height);
                using (var powder = new PathGradientBrush(shape))
                {
                    powder.CenterColor = Color.FromArgb(opacity, 232, 246, 255);
                    powder.SurroundColors = new[] { Color.FromArgb(0, 232, 246, 255) };
                    canvas.FillPath(powder, shape);
                }
            }
        }

        private static void Paw(Graphics canvas, Brush ink, float x, float y)
        {
            canvas.FillEllipse(ink, x - 4.8f, y - 1.4f, 9.6f, 5.6f);
            canvas.FillEllipse(ink, x - 6.1f, y - 6, 3.0f, 3.0f);
            canvas.FillEllipse(ink, x - 2.95f, y - 7.4f, 3.15f, 3.5f);
            canvas.FillEllipse(ink, x + .62f, y - 7.2f, 3.13f, 3.35f);
            canvas.FillEllipse(ink, x + 3.75f, y - 5.55f, 2.8f, 2.7f);
        }

        public void KeepBehind(AlphaOverlay foreground) { overlay.KeepBehind(foreground); }
        public void Hide() { marks.Clear(); overlay.Hide(); }
        public void Dispose() { overlay.Dispose(); }
    }
}

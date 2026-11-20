using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class WindowSnow : IDisposable
    {
        private readonly Form owner;
        private readonly float scale;
        private readonly int cell, corner, stripTop, stripHeight;
        private readonly Random wind = new Random();
        private List<SnowSurface> surfaces = new List<SnowSurface>(24);
        private List<SnowSurface> snapshot = new List<SnowSurface>(24);
        private readonly Dictionary<IntPtr, Bank> banks = new Dictionary<IntPtr, Bank>();
        private readonly List<IntPtr> expired = new List<IntPtr>(5);
        private Rectangle previousSky, previousPanel;
        private double snapshotAt;
        private bool trackingPointer;
        private Point previousPointer;

        internal struct Landing
        {
            public SnowSurface Surface;
            public PointF Point;
        }

        private sealed class Bank : IDisposable
        {
            public readonly AlphaOverlay Overlay;
            public readonly uint ProcessId;
            public readonly List<Powder> Puffs = new List<Powder>(46);
            public Rectangle Frame;
            public float[] Amount = new float[0];
            public int Order = -1;
            public double Seen, LastPuff;
            public bool Dirty;
            public Bank(Form owner, SnowSurface surface)
            {
                Overlay = new AlphaOverlay(owner, "HappyNewIsland.WindowSnow");
                ProcessId = surface.ProcessId;
            }
            public void Dispose() { Overlay.Dispose(); }
        }

        private struct Powder
        {
            public float X, Y, Vx, Vy, Size, Life;
            public double Born;
        }

        public WindowSnow(Form form, float dpi)
        {
            owner = form;
            scale = dpi;
            cell = Px(5);
            corner = Px(9);
            stripTop = Px(35);
            stripHeight = Px(89);
        }

        public void Prepare(Rectangle sky, Rectangle panel, Point cursor, double now)
        {
            if (sky != previousSky || panel != previousPanel || now >= snapshotAt)
            {
                previousSky = sky;
                previousPanel = panel;
                snapshotAt = now + .18;
                snapshot.Clear();
                if (!panel.IsEmpty)
                    snapshot.Add(new SnowSurface { Handle = owner.Handle, Bounds = panel, CollectsSnow = true });

                // Keep sensing nearby windows for a while when a snowy edge is dragged
                // away from the narrow column where the flakes originally landed.
                WindowsShell.SnowSurfaces(Rectangle.Inflate(sky, Px(690), Px(480)), snapshot);
                bool changed = !SameSurfaces(surfaces, snapshot);
                if (changed)
                {
                    List<SnowSurface> previous = surfaces;
                    surfaces = snapshot;
                    snapshot = previous;
                }
                UpdateBanks(now, changed);
            }

            if (trackingPointer && cursor != previousPointer)
            {
                Point from = previousPointer;
                int dx = cursor.X - from.X, dy = cursor.Y - from.Y;
                if (Math.Abs(dx) <= Px(184) && Math.Abs(dy) <= Px(117))
                    Brush(from, cursor, now);
            }
            previousPointer = cursor;
            trackingPointer = true;
        }

        private static bool SameSurfaces(List<SnowSurface> first, List<SnowSurface> second)
        {
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
            {
                SnowSurface a = first[i], b = second[i];
                if (a.Handle != b.Handle || a.ProcessId != b.ProcessId || a.Bounds != b.Bounds || a.CollectsSnow != b.CollectsSnow)
                    return false;
            }
            return true;
        }

        private void UpdateBanks(double now, bool changed)
        {
            foreach (Bank bank in banks.Values) bank.Order = -1;
            for (int order = 0; order < surfaces.Count; order++)
            {
                SnowSurface surface = surfaces[order];
                Bank bank;
                if (!banks.TryGetValue(surface.Handle, out bank) || bank.ProcessId != surface.ProcessId ||
                    !surface.CollectsSnow) continue;
                SetFrame(bank, surface.Bounds);
                bank.Order = order;
                bank.Seen = now;
                bank.Dirty |= changed;
            }

            expired.Clear();
            foreach (var entry in banks)
            {
                if (entry.Value.Order >= 0) continue;
                entry.Value.Overlay.Hide();
                if (now - entry.Value.Seen > 18) expired.Add(entry.Key);
            }
            foreach (IntPtr key in expired)
            {
                banks[key].Dispose();
                banks.Remove(key);
            }
        }

        private void SetFrame(Bank bank, Rectangle bounds)
        {
            if (bank.Frame == bounds) return;
            bank.Frame = bounds;
            int capacity = Math.Min(1250, (bounds.Width + cell - 1) / cell);
            if (bank.Amount.Length != capacity)
                Array.Resize(ref bank.Amount, capacity);
        }

        public bool FirstLanding(PointF from, PointF to, out Landing landing)
        {
            landing = new Landing();
            float fall = to.Y - from.Y;
            if (fall <= 0) return false;
            float first = 1.001f;
            for (int index = 0; index < surfaces.Count; index++)
            {
                SnowSurface surface = surfaces[index];
                Rectangle edge = surface.Bounds;
                if (!surface.CollectsSnow || edge.Top < previousSky.Top || edge.Top < from.Y - scale ||
                    edge.Top > to.Y + HeightLimit(surface.ProcessId) + scale) continue;

                float guess = Math.Max(0, Math.Min(1, (edge.Top - from.Y) / fall));
                float x = from.X + (to.X - from.X) * guess;
                if (x < edge.Left + corner || x >= edge.Right - corner) continue;
                float y = edge.Top - HeightAt(surface, x);
                float cross = Math.Max(0, (y - from.Y) / fall);
                if (cross > 1 || cross >= first) continue;
                x = from.X + (to.X - from.X) * cross;
                if (x < edge.Left + corner || x >= edge.Right - corner || Covered(index, x, edge.Top + 1)) continue;
                first = cross;
                landing = new Landing { Surface = surface, Point = new PointF(x, Math.Max(from.Y, y)) };
            }
            return first <= 1;
        }

        public void Deposit(Landing strike, float radius, double now)
        {
            SnowSurface surface = strike.Surface;
            Bank bank;
            if (banks.TryGetValue(surface.Handle, out bank) && bank.ProcessId != surface.ProcessId)
            {
                bank.Dispose();
                banks.Remove(surface.Handle);
                bank = null;
            }
            if (bank == null)
            {
                if (banks.Count >= 8 && !EvictOne()) return;
                bank = new Bank(owner, surface);
                banks.Add(surface.Handle, bank);
            }
            SetFrame(bank, surface.Bounds);
            bank.Seen = now;
            bank.Order = surfaces.FindIndex(item => item.Handle == surface.Handle);
            bank.Dirty = true;
            float center = (strike.Point.X - bank.Frame.Left) / cell - .5f;
            int at = (int)Math.Floor(center);
            float volume = (1.12f + radius / scale * .34f) * scale;
            float limit = HeightLimit(surface.ProcessId);
            for (int i = Math.Max(0, at - 7); i < Math.Min(bank.Amount.Length, at + 9); i++)
            {
                float slot = (i + .5f) * cell;
                if (slot < corner || slot > bank.Frame.Width - corner) continue;
                float offset = (i - center) / 2.6f;
                float profile = (float)Math.Exp(-offset * offset) * .86f +
                    (float)Math.Exp(-offset * offset * .34f) * .14f;
                bank.Amount[i] = Math.Min(limit, bank.Amount[i] + volume * profile);
            }
        }

        private float HeightLimit(uint processId)
        {
            return Px(processId == 0 ? 16 : 26);
        }

        private bool EvictOne()
        {
            IntPtr key = IntPtr.Zero;
            double oldest = Double.PositiveInfinity;
            foreach (var pair in banks)
                if (pair.Value.Order < 0 && pair.Value.Seen < oldest)
                {
                    key = pair.Key;
                    oldest = pair.Value.Seen;
                }
            if (key == IntPtr.Zero) return false;
            banks[key].Dispose();
            return banks.Remove(key);
        }

        private float HeightAt(SnowSurface surface, float screenX)
        {
            Bank bank;
            if (!banks.TryGetValue(surface.Handle, out bank) || bank.ProcessId != surface.ProcessId) return 0;
            int bin = (int)((screenX - surface.Bounds.X) / cell);
            return bin >= 0 && bin < bank.Amount.Length ? bank.Amount[bin] : 0;
        }

        private bool Covered(int order, float x, int y)
        {
            for (int i = 0; i < order; i++)
            {
                Rectangle mask = surfaces[i].Bounds;
                if (y >= mask.Top && y < mask.Bottom && x >= mask.Left && x < mask.Right) return true;
            }
            return false;
        }

        private void Brush(Point from, Point to, double now)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float distance = dx * dx + dy * dy;
            if (distance < 1) return;
            float inverse = 1 / distance, radius = Px(21);
            float travel = (float)Math.Sqrt(distance);
            foreach (Bank bank in banks.Values)
            {
                int top = bank.Frame.Top;
                if (bank.Order < 0 || top < Math.Min(from.Y, to.Y) - radius ||
                    top - HeightLimit(bank.ProcessId) > Math.Max(from.Y, to.Y) + radius) continue;
                int width = (int)(radius + HeightLimit(bank.ProcessId) * .36f);
                int start = Math.Max(0, (Math.Min(from.X, to.X) - width - bank.Frame.Left) / cell);
                int end = Math.Min(bank.Amount.Length, (Math.Max(from.X, to.X) + width - bank.Frame.Left) / cell + 1);
                for (int j = start; j < end; j++)
                {
                    float old = bank.Amount[j];
                    if (old < .22f) continue;
                    float x = (j + .5f) * cell;
                    float worldX = bank.Frame.Left + x;
                    float worldY = top - old * .56f;
                    if (Covered(bank.Order, worldX, top + 1) || Covered(bank.Order, worldX, (int)worldY)) continue;
                    float projection = Math.Max(0, Math.Min(1, ((worldX - from.X) * dx + (worldY - from.Y) * dy) * inverse));
                    float sx = from.X + projection * dx - worldX;
                    float sy = from.Y + projection * dy - worldY;
                    float away = sx * sx + sy * sy;
                    float reach = radius + old * .36f;
                    if (away >= reach * reach) continue;

                    float force = Math.Min(1, (1 - (float)Math.Sqrt(away) / reach) * Math.Min(2.14f, .65f + travel / Px(12)));
                    float brushed = Math.Max(0, old - force * (old + .55f * scale));
                    bank.Amount[j] = brushed < .22f ? 0 : brushed;
                    bank.Dirty = true;
                    if (old - brushed >= .55f * scale && now - bank.LastPuff >= .034)
                    {
                        bank.LastPuff = now;
                        int puffs = 5 + Math.Min(7, (int)((old - brushed) / Px(2)));
                        for (int i = 0; i < puffs && bank.Puffs.Count < 46; i++)
                            bank.Puffs.Add(new Powder
                            {
                                Born = now, X = x + Range(-2, 2) * scale, Y = -old * .54f,
                                Vx = (dx / travel * Range(27, 65) + Range(-15, 15)) * scale,
                                Vy = -Range(12, 48) * scale, Size = Range(1.02f, old > Px(13) ? 3.81f : 2.7f) * scale,
                                Life = Range(.38f, .72f)
                            });
                    }
                }
            }
        }

        public void Paint(double now)
        {
            foreach (Bank bank in banks.Values)
            {
                if (bank.Order < 0 || (!bank.Dirty && bank.Puffs.Count == 0)) continue;
                bank.Dirty = false;
                for (int i = bank.Puffs.Count - 1; i >= 0; i--)
                    if (now - bank.Puffs[i].Born >= bank.Puffs[i].Life) bank.Puffs.RemoveAt(i);

                int first = bank.Amount.Length, last = -1;
                for (int i = 0; i < bank.Amount.Length; i++)
                    if (bank.Amount[i] > .26f)
                    {
                        first = Math.Min(first, i);
                        last = i;
                    }
                int left = first * cell, right = (last + 1) * cell;
                foreach (Powder puff in bank.Puffs)
                {
                    left = Math.Min(left, (int)(puff.X - Px(56)));
                    right = Math.Max(right, (int)(puff.X + Px(56)));
                }
                if (right <= left) { bank.Overlay.Hide(); continue; }

                left = Math.Max(-Px(11), left - cell);
                right = Math.Min(bank.Frame.Width + Px(11), right + cell);
                if (right <= left) { bank.Overlay.Hide(); continue; }
                var at = new Point(bank.Frame.Left + left, bank.Frame.Top - stripTop);
                var extent = new Size(right - left, stripHeight);
                Graphics canvas = bank.Overlay.Begin(extent);
                canvas.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < bank.Order; i++)
                {
                    Rectangle hidden = surfaces[i].Bounds;
                    hidden.Offset(-at.X, -at.Y);
                    canvas.ExcludeClip(hidden);
                }
                DrawRidge(canvas, bank, left, first, last);
                DrawPowder(canvas, bank, left, now);
                canvas.ResetClip();
                bank.Overlay.Show(at);
            }
        }

        private void DrawRidge(Graphics canvas, Bank bank, int left, int first, int last)
        {
            if (last < first) return;
            float baseline = stripTop + 1.34f * scale;
            float depth = HeightLimit(bank.ProcessId);
            RectangleF band = new RectangleF(0, baseline - depth - Px(2), canvas.VisibleClipBounds.Width + 1, depth + Px(6));
            if (band.Width < 1) return;
            using (var coat = new LinearGradientBrush(band, Color.FromArgb(249, 253, 255),
                Color.FromArgb(224, 177, 202, 225), 90f))
            using (var highlight = new Pen(Color.FromArgb(169, 244, 251, 255), Math.Max(.7f, .77f * scale)))
            {
                for (int i = first; i <= last; i++)
                {
                    if (bank.Amount[i] <= .26f) continue;
                    int start = i;
                    while (i < last && bank.Amount[i + 1] > .26f) i++;
                    var ridge = new PointF[i - start + 3];
                    ridge[0] = new PointF(start * cell - left, baseline);
                    for (int bin = start; bin <= i; bin++)
                    {
                        float folds = .93f + .07f * (float)Math.Sin(bin * .79 + .67);
                        ridge[bin - start + 1] = new PointF((bin + .5f) * cell - left,
                            baseline - .55f * scale - bank.Amount[bin] * folds);
                    }
                    ridge[ridge.Length - 1] = new PointF((i + 1) * cell - left, baseline);
                    using (var shape = new GraphicsPath())
                    {
                        shape.AddCurve(ridge, .37f);
                        shape.CloseFigure();
                        canvas.FillPath(coat, shape);
                    }
                    canvas.DrawCurve(highlight, ridge, .37f);
                }
            }
        }

        private void DrawPowder(Graphics canvas, Bank bank, int left, double now)
        {
            using (var brush = new SolidBrush(Color.AliceBlue))
                foreach (Powder puff in bank.Puffs)
                {
                    float t = (float)(now - puff.Born);
                    float x = puff.X - left + puff.Vx * t;
                    float y = stripTop + puff.Y + puff.Vy * t + 105 * scale * t * t;
                    brush.Color = Color.FromArgb((int)(218 * Math.Max(0, 1 - t / puff.Life)), 232, 247, 255);
                    canvas.FillEllipse(brush, x, y, puff.Size, puff.Size * .78f);
                }
        }

        public void Hide()
        {
            trackingPointer = false;
            foreach (Bank bank in banks.Values)
            {
                bank.Overlay.Hide();
                bank.Dirty = true;
            }
        }

        public void Clear()
        {
            foreach (Bank bank in banks.Values) bank.Dispose();
            banks.Clear();
            surfaces.Clear();
            snapshot.Clear();
            trackingPointer = false;
            snapshotAt = 0;
        }

        public void Dispose() { Clear(); }

        private int Px(int logical) { return Math.Max(1, (int)Math.Round(logical * scale)); }
        private float Range(float low, float high) { return low + (float)wind.NextDouble() * (high - low); }
    }
}

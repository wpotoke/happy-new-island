using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WinterIsland
{
    internal sealed class LeopardRig : IDisposable
    {
        // Landmarks in the first, standing pose of the existing atlas.
        private static readonly Rectangle Standing = new Rectangle(0, 90, 362, 202);
        private static readonly float[] PawEdges = { 120, 151, 211, 262, 322 };
        private static readonly double[] PawPhases = { .50, .0, .27, .77 };
        private const float Heel = 272;
        private readonly float scale;
        private readonly int[] source, torso, rest, result;
        private readonly PawLayer[] paws = new PawLayer[4];
        private readonly int[][] views = new int[5][];
        private readonly ProfileRow[][] profiles = new ProfileRow[5][];
        private readonly Influence[] weights;
        private readonly PointF neck;
        private readonly float ankle, joint;

        private struct Influence
        {
            public float Tail, Head, Shoulders, Hips;
        }

        private sealed class PawLayer
        {
            public int[] Pixels;
            public Rectangle Area;
        }

        private struct ProfileRow
        {
            public float Left, Middle, Right;
        }

        public Bitmap Frame { get; private set; }
        public float Ground { get; private set; }
        public float Nose { get; private set; }
        public float Pivot { get; private set; }

        private LeopardRig(Bitmap atlas, float dpi)
        {
            scale = dpi;
            int top = (int)Math.Round(21 * scale);
            int width = (int)Math.Round(117 * scale);
            float ratio = width / (float)Standing.Width;
            int height = (int)Math.Round(Standing.Height * ratio);
            int frameWidth = (int)Math.Round(163 * scale / 2) * 2;
            Pivot = frameWidth / 2f;
            var art = new Rectangle((int)Math.Round(Pivot - 205 * ratio), top, width, height);
            Frame = new Bitmap(frameWidth, height + top + (int)Math.Round(10 * scale), PixelFormat.Format32bppPArgb);
            Ground = art.Top + (Heel - Standing.Top) * ratio;
            Nose = art.Left + 341 * ratio;
            neck = new PointF(art.Left + 273 * ratio, art.Top + (210 - Standing.Top) * ratio);
            joint = art.Top + (219 - Standing.Top) * ratio;
            ankle = art.Top + (243 - Standing.Top) * ratio;

            int pixels = Frame.Width * Frame.Height;
            source = ReadPose(atlas, Standing, art);
            torso = (int[])source.Clone();
            SplitPaws(art, ratio);
            LoadTurnViews((Heel - 126) * ratio);
            using (Stream stream = typeof(LeopardRig).Assembly.GetManifestResourceStream("WinterIsland.LeopardSeated"))
            {
                if (stream == null) rest = source;
                else
                {
                    using (var seated = new Bitmap(stream))
                    {
                        var crop = new Rectangle(80, 100, 1286, 801);
                        int seatWidth = (int)Math.Round(width * .96);
                        float seatScale = seatWidth / (float)crop.Width;
                        var spot = new Rectangle((int)Math.Round(Nose - (1349 - crop.X) * seatScale),
                            (int)Math.Round(Ground - (877 - crop.Y) * seatScale), seatWidth,
                            (int)Math.Round(crop.Height * seatScale));
                        rest = ReadPose(seated, crop, spot);
                    }
                }
            }
            result = new int[pixels];
            weights = new Influence[pixels];

            for (int y = 0; y < Frame.Height; y++)
                for (int x = 0; x < Frame.Width; x++)
                    weights[y * Frame.Width + x] = At((x - art.Left) / ratio, Standing.Top + (y - art.Top) / ratio);
        }

        private void SplitPaws(Rectangle art, float ratio)
        {
            int width = Frame.Width;
            int top = (int)Math.Floor(joint);
            int bottom = Math.Min(Frame.Height - 1, (int)Math.Ceiling(Ground + scale * 3));
            for (int i = 0; i < paws.Length; i++)
            {
                int left = art.Left + (int)Math.Round(PawEdges[i] * ratio);
                int right = art.Left + (int)Math.Round(PawEdges[i + 1] * ratio);
                var pixels = new int[source.Length];
                paws[i] = new PawLayer { Pixels = pixels, Area = Rectangle.FromLTRB(left, top, right, bottom) };
                for (int y = top; y < bottom; y++)
                {
                    int amount = (int)Math.Round(256 * Ease((y - joint) / (ratio * 12)));
                    for (int x = left; x < right; x++)
                    {
                        int index = y * width + x;
                        pixels[index] = unchecked((int)Blend(0, (uint)source[index], amount));
                        if (amount == 256) torso[index] = 0;
                    }
                }
            }
        }

        private void LoadTurnViews(float standingHeight)
        {
            views[0] = source;
            views[4] = new int[source.Length];
            int width = Frame.Width;
            for (int i = 0; i < source.Length; i++)
                views[4][i] = source[i - i % width + width - 1 - i % width];

            using (Stream stream = typeof(LeopardRig).Assembly.GetManifestResourceStream("WinterIsland.LeopardViews"))
            {
                if (stream == null) throw new InvalidDataException("WinterIsland.LeopardViews");
                using (var atlas = new Bitmap(stream))
                {
                    views[1] = ReadTurnPose(atlas, new Rectangle(1049, 95, 461, 349), 1301, 109, 427, standingHeight);
                    views[2] = ReadTurnPose(atlas, new Rectangle(39, 97, 287, 343), 238, 109, 428, standingHeight);
                    views[3] = ReadTurnPose(atlas, new Rectangle(1067, 557, 454, 370), 1243, 576, 902, standingHeight);
                }
            }
            for (int i = 0; i < views.Length; i++)
                profiles[i] = MeasureProfile(views[i]);
        }

        private ProfileRow[] MeasureProfile(int[] pixels)
        {
            int width = Frame.Width, height = Frame.Height, first = height, last = 0;
            var measured = new ProfileRow[height];
            for (int y = 0; y < height; y++)
            {
                int weight = 0, weightedX = 0, left = width, right = 0;
                for (int x = 0; x < width; x++)
                {
                    int opacity = (int)((uint)pixels[y * width + x] >> 24);
                    if (opacity < 100) continue;
                    left = Math.Min(left, x);
                    right = x;
                    weight += opacity;
                    weightedX += opacity * x;
                }
                if (weight == 0) continue;
                measured[y] = new ProfileRow { Left = left - 2, Middle = weightedX / (float)weight, Right = right + 2 };
                first = Math.Min(first, y);
                last = y;
            }
            if (first == height)
                throw new InvalidDataException("Empty leopard pose.");
            for (int y = 0; y < first; y++) measured[y] = measured[first];
            for (int y = first + 1; y <= last; y++)
                if (measured[y].Right == 0) measured[y] = measured[y - 1];
            for (int y = last + 1; y < height; y++) measured[y] = measured[last];

            // Smooth neighbouring rows so whiskers and gaps between the paws cannot bend the outline sharply.
            var rows = new ProfileRow[height];
            int window = Math.Max(1, (int)Math.Round(scale * 2));
            for (int y = 0; y < height; y++)
            {
                ProfileRow low = measured[Math.Max(0, y - window)], high = measured[Math.Min(height - 1, y + window)];
                ProfileRow mid = measured[y];
                rows[y] = new ProfileRow
                {
                    Left = (low.Left + 2 * mid.Left + high.Left) / 4,
                    Middle = (low.Middle + 2 * mid.Middle + high.Middle) / 4,
                    Right = (low.Right + 2 * mid.Right + high.Right) / 4
                };
            }
            return rows;
        }

        private int[] ReadTurnPose(Bitmap atlas, Rectangle crop, int hips, int ears, int floor, float height)
        {
            float ratio = height / (floor - ears);
            var position = new Rectangle((int)Math.Round(Pivot + (crop.Left - hips) * ratio),
                (int)Math.Round(Ground + (crop.Top - floor) * ratio), (int)Math.Round(crop.Width * ratio),
                (int)Math.Round(crop.Height * ratio));
            return ReadPose(atlas, crop, position);
        }

        private int[] ReadPose(Bitmap atlas, Rectangle crop, Rectangle position)
        {
            using (Graphics paint = Graphics.FromImage(Frame))
            {
                paint.CompositingMode = CompositingMode.SourceCopy;
                paint.Clear(Color.Transparent);
                paint.InterpolationMode = InterpolationMode.HighQualityBicubic;
                paint.PixelOffsetMode = PixelOffsetMode.HighQuality;
                paint.DrawImage(atlas, position, crop, GraphicsUnit.Pixel);
            }
            var pixels = new int[Frame.Width * Frame.Height];
            BitmapData data = Frame.LockBits(new Rectangle(Point.Empty, Frame.Size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { Frame.UnlockBits(data); }
            StripMatte(pixels, Frame.Width, Frame.Height);
            return pixels;
        }

        private static void StripMatte(int[] pixels, int width, int height)
        {
            var distance = new int[pixels.Length];
            for (int i = 0; i < distance.Length; i++) distance[i] = ((uint)pixels[i] >> 24) >= 150 ? 0 : 9;
            for (int i = 0; i < distance.Length; i++)
            {
                if (i % width != 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
                if (i >= width) distance[i] = Math.Min(distance[i], distance[i - width] + 1);
            }
            for (int i = distance.Length - 1; i >= 0; i--)
            {
                if (i % width != width - 1) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
                if (i + width < distance.Length) distance[i] = Math.Min(distance[i], distance[i + width] + 1);
                int alpha = (int)((uint)pixels[i] >> 24);
                if (alpha < 19 || distance[i] > 2 || i % width == 0 || i % width == width - 1 || i < width || i >= width * (height - 1))
                    pixels[i] = 0;
                else if (alpha < 92)
                    pixels[i] = unchecked((int)Blend(0, (uint)pixels[i], (int)(256 * Ease((alpha - 19) / 73f))));
            }
        }

        public static LeopardRig Load(float scale)
        {
            using (Stream stream = typeof(LeopardRig).Assembly.GetManifestResourceStream("WinterIsland.Leopard"))
            {
                if (stream == null) return null;
                using (var atlas = new Bitmap(stream)) return new LeopardRig(atlas, scale);
            }
        }

        private static Influence At(float x, float y)
        {
            float back = Ease((231 - y) / 36) * Ease((x - 111) / 49) * Ease((297 - x) / 42);
            float upper = Ease((x - 171) / 82);
            return new Influence
            {
                Tail = Ease((122 - x) / 98) * Ease((y - 184) / 45),
                Head = Ease((x - 253) / 68) * Ease((237 - y) / 53),
                Shoulders = back * upper,
                Hips = back * (1 - upper)
            };
        }

        private static float Ripple(double age, double delay, double length, double hz)
        {
            double t = age - delay;
            if (t <= 0 || t >= length) return 0;
            return (float)Math.Sin(t * hz * 2 * Math.PI) *
                Ease((float)(t / .072)) * Ease((float)((length - t) / (length * .69)));
        }

        public void Render(double step, float walking, float nuzzle, double time, float subX, float subY,
            double shakeAge, float sitting, float eating)
        {
            float head = Ripple(shakeAge, .05, .70, 5.1);
            float shoulders = Ripple(shakeAge, .125, .68, 5.1);
            float hips = Ripple(shakeAge, .198, .64, 5.1);
            float tailFlick = Ripple(shakeAge, .265, .64, 4.5);
            float tuck = Ease((float)(shakeAge / .065)) * Ease((float)((.22 - shakeAge) / .15));
            float tail = ((float)Math.Sin(step * 2 * Math.PI + time * 1.7) *
                (1.1f + walking * 2.05f) + 7.2f * tailFlick) * scale;
            float breathe = (float)Math.Sin(time * 2.1) * .27f * scale;
            float back = breathe - (float)Math.Sin(step * 4 * Math.PI) * .40f * scale * walking + sitting * 4.5f * scale;
            float headUp = scale * (-4.1f * nuzzle + 4.8f * eating + 2.95f * tuck + head * 2.3f);
            float headForward = scale * (3.6f * nuzzle + eating * 2.6f - 2.8f * tuck + head * 3.2f);
            float tilt = -.135f * nuzzle + .245f * eating + .21f * head;
            float shoulderUp = back + scale * (2.8f * shoulders + 1.9f * tuck);
            float hipUp = back + scale * 2.5f * hips;
            int seated = Object.ReferenceEquals(rest, source) ? 0 : (int)Math.Round(sitting * 256);
            int width = Frame.Width, height = Frame.Height;
            bool stepping = walking > .015f && seated < 256;
            if (stepping)
            {
                Array.Clear(result, 0, result.Length);
                for (int i = 0; i < paws.Length; i++)
                    DrawPaw(paws[i], Paw(step + PawPhases[i], walking), subX, subY);
            }
            int[] standing = stepping ? torso : source;
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                float reach = headForward - (y - neck.Y) * tilt;
                for (int x = 0; x < width; x++, index++)
                {
                    Influence w = weights[index];
                    float dx = subX + w.Head * reach + scale * (w.Shoulders * shoulders - w.Hips * hips);
                    float dy = subY + w.Tail * tail + w.Shoulders * shoulderUp + w.Hips * hipUp +
                        w.Head * (headUp + (x - neck.X) * tilt);
                    int stand = seated == 256 ? 0 : Sample(standing, x - dx, y - dy, width, height);
                    if (stepping) stand = Over(stand, result[index]);
                    if (seated == 0) result[index] = stand;
                    else
                    {
                        int restPixel = Sample(rest, x - dx,
                            y - subY - (w.Shoulders + w.Hips) * breathe - w.Tail * tail * .35f -
                            w.Head * (headUp + (x - neck.X) * tilt), width, height);
                        result[index] = unchecked((int)Blend((uint)stand, (uint)restPixel, seated));
                    }
                }
            }
            WriteFrame();
        }

        public void RenderTurn(double progress, bool rightToLeft, float subX, float subY, double shakeAge)
        {
            float angle = (float)(4 * (rightToLeft ? progress : 1 - progress));
            int start = Math.Max(0, Math.Min(3, (int)angle));
            float fraction = angle - start;
            int mix = (int)Math.Round(256 * Ease(fraction));
            int[] from = views[start], to = views[start + 1];
            float recoil = scale * Ripple(shakeAge, .05, .68, 5.0);
            float follow = scale * Ripple(shakeAge, .14, .65, 5.0);
            int width = Frame.Width, height = Frame.Height;
            for (int y = 0, i = 0; y < height; y++)
            {
                float upper = Ease((Ground - y - scale * 12) / (scale * 28));
                float sy = y - subY - upper * (2.9f * recoil + .8f * follow);
                float dx = subX + upper * recoil * 2.4f;
                int row = Math.Max(0, Math.Min(height - 1, (int)Math.Round(sy)));
                ProfileRow a = profiles[start][row], b = profiles[start + 1][row];
                float left = a.Left + (b.Left - a.Left) * fraction;
                float middle = a.Middle + (b.Middle - a.Middle) * fraction;
                float right = a.Right + (b.Right - a.Right) * fraction;
                float aLeft = (a.Middle - a.Left) / (middle - left);
                float bLeft = (b.Middle - b.Left) / (middle - left);
                float aRight = (a.Right - a.Middle) / (right - middle);
                float bRight = (b.Right - b.Middle) / (right - middle);
                for (int x = 0; x < width; x++, i++)
                {
                    float local = x - dx - middle;
                    float ax = a.Middle + local * (local < 0 ? aLeft : aRight);
                    float bx = b.Middle + local * (local < 0 ? bLeft : bRight);
                    result[i] = unchecked((int)Blend((uint)Sample(from, ax, sy, width, height),
                        (uint)Sample(to, bx, sy, width, height), mix));
                }
            }
            WriteFrame();
        }

        private void DrawPaw(PawLayer paw, PointF offset, float subX, float subY)
        {
            int width = Frame.Width, height = Frame.Height;
            Rectangle clip = paw.Area;
            clip.Inflate((int)Math.Ceiling(Math.Abs(offset.X) + 2), (int)Math.Ceiling(Math.Abs(offset.Y) + 2));
            clip.Intersect(new Rectangle(0, 0, width, height));
            for (int y = clip.Top; y < clip.Bottom; y++)
            {
                // Only the attachment flexes. The foot below the ankle moves as one piece.
                float flex = Ease((y - subY - joint) / (ankle - joint));
                float dx = subX + offset.X * flex, sy = y - subY - offset.Y * flex;
                for (int x = clip.Left; x < clip.Right; x++)
                {
                    int i = y * width + x;
                    result[i] = Over(Sample(paw.Pixels, x - dx, sy, width, height), result[i]);
                }
            }
        }

        private void WriteFrame()
        {
            BitmapData bitmap = Frame.LockBits(new Rectangle(Point.Empty, Frame.Size), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(result, 0, bitmap.Scan0, result.Length); }
            finally { Frame.UnlockBits(bitmap); }
        }

        public bool OpaqueAt(int x, int y)
        {
            return x >= 0 && x < Frame.Width && y >= 0 && y < Frame.Height &&
                ((uint)result[y * Frame.Width + x] >> 24) >= 126;
        }

        private PointF Paw(double offset, float walking)
        {
            const float stance = .59f;
            const float reach = 4.9f;
            float phase = (float)(offset - Math.Floor(offset));
            float x, y;
            if (phase < stance)
            {
                x = reach * (1 - 2 * phase / stance);
                y = 0;
            }
            else
            {
                float progress = (phase - stance) / (1 - stance);
                x = reach * (2 * Ease(progress) - 1);
                y = -3.7f * (float)Math.Pow(Math.Sin(progress * Math.PI), 1.35);
            }
            return new PointF(x * scale * walking, y * scale * walking);
        }

        private static int Over(int front, int back)
        {
            if (front == 0) return back;
            int keep = 256 - (int)((uint)front >> 24);
            return keep <= 1 || back == 0 ? front : unchecked(front + (int)Blend(0, (uint)back, keep));
        }

        private static int Sample(int[] pixels, float x, float y, int width, int height)
        {
            if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1) return 0;
            int ix = (int)x, iy = (int)y;
            int fractionX = (int)((x - ix) * 256), fractionY = (int)((y - iy) * 256);
            int offset = iy * width + ix;
            uint upper = Blend((uint)pixels[offset], (uint)pixels[offset + 1], fractionX);
            uint lower = Blend((uint)pixels[offset + width], (uint)pixels[offset + width + 1], fractionX);
            return unchecked((int)Blend(upper, lower, fractionY));
        }

        private static uint Blend(uint start, uint end, int fraction)
        {
            const uint alternating = 0x00ff00ff;
            uint next = (uint)fraction, old = 256 - next;
            uint redBlue = (((start & alternating) * old + (end & alternating) * next) >> 8) & alternating;
            uint alphaGreen = ((((start >> 8) & alternating) * old + ((end >> 8) & alternating) * next) >> 8) & alternating;
            return redBlue | (alphaGreen << 8);
        }

        private static float Ease(float value)
        {
            float t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        public void Dispose() { Frame.Dispose(); }
    }
}

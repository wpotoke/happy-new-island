using System.Drawing;

namespace WinterIsland
{
    internal sealed class GarlandLayout
    {
        internal const float SeamWidth = 4.8f;
        internal static readonly Color BranchBase = Color.FromArgb(38, 49, 26);

        // The ends retain their proportions as the island grows for media playback.
        private static readonly int[] AtlasX = { 62, 353, 1819, 2110 };
        private static readonly int[] AtlasY = { 195, 299, 432, 528 };
        private static readonly Point[] AtlasBulbs =
        {
            new Point(228, 263), new Point(1912, 260), new Point(231, 461), new Point(430, 474),
            new Point(765, 474), new Point(1464, 467), new Point(1694, 469), new Point(1946, 456)
        };

        internal struct Slice
        {
            internal readonly Rectangle Source;
            internal readonly RectangleF Target;

            internal Slice(Rectangle source, RectangleF target)
            {
                Source = source;
                Target = target;
            }
        }

        internal readonly RectangleF Capsule;
        internal readonly Slice[] Branches = new Slice[8];
        internal readonly PointF[] Bulbs = new PointF[AtlasBulbs.Length];

        internal GarlandLayout(RectangleF capsule, float scale)
        {
            Capsule = capsule;
            float padding = 7 * scale;
            float end = 27 * scale;
            float[] x = { capsule.Left - padding, capsule.Left + end, capsule.Right - end, capsule.Right + padding };
            float[] y = { capsule.Top - padding, capsule.Top + 5.5f * scale, capsule.Bottom - padding, capsule.Bottom + padding };
            int index = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    if (row == 1 && column == 1) continue;
                    bool crown = row == 0 && column == 1;
                    // Keep the fine needles prominent along the otherwise thin top strand.
                    int top = crown ? 230 : AtlasY[row], bottom = crown ? 280 : AtlasY[row + 1];
                    float upper = crown ? capsule.Top - 5 * scale : y[row];
                    Branches[index++] = new Slice(Rectangle.FromLTRB(AtlasX[column], top, AtlasX[column + 1], bottom),
                        RectangleF.FromLTRB(x[column], upper, x[column + 1], y[row + 1]));
                }
            }

            for (int i = 0; i < Bulbs.Length; i++)
            {
                Point bulb = AtlasBulbs[i];
                int column = bulb.X < AtlasX[1] ? 0 : bulb.X < AtlasX[2] ? 1 : 2;
                int row = bulb.Y < AtlasY[1] ? 0 : 2;
                Bulbs[i] = new PointF(
                    Map(bulb.X, AtlasX[column], AtlasX[column + 1], x[column], x[column + 1]),
                    Map(bulb.Y, AtlasY[row], AtlasY[row + 1], y[row], y[row + 1]));
            }
        }

        private static float Map(int value, int first, int last, float start, float end)
        {
            return start + (end - start) * (value - first) / (last - first);
        }
    }
}

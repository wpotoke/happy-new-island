using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using System.Windows.Forms;

namespace WinterIsland
{
    internal enum LeopardFood { Meat, Fish, Treat }

    internal sealed class Leopard : IDisposable
    {
        private const double ShakeTime = .94, Recovery = .22, FeedTime = 1.6;
        private const double HeartLifetime = 1.24;
        internal const int FloorBelowIsland = 85;
        private const float CubScale = .64f, MaxGrowth = 1.32f;
        private const float BrushSnowLoad = 11, AutomaticSnowLoad = 33, MaximumSnowLoad = 108, TuftLimit = 5.2f;
        private const double PettingGap = 3.3;
        private enum ShakeCause { Brushed, Weather, Snowball, Waking }
        private readonly float scale;
        private readonly AlphaOverlay overlay, snowDust;
        private readonly Func<bool> feedingAllowed;
        private readonly Random random = new Random();
        private readonly LeopardLife life;
        private readonly ImageAttributes fade = new ImageAttributes();
        private readonly GraphicsPath heartShape = MakeHeart();
        private readonly List<Heart> hearts = new List<Heart>(6);
        private readonly Font[] sleepLetters;
        private readonly StringFormat sleepFormat = StringFormat.GenericTypographic;
        private readonly SnowGrain[] grains = new SnowGrain[76];
        private readonly SnowOnFur[] coatSnow = new SnowOnFur[42];
        private LeopardModel rig;
        private RectangleF smoothIsland;
        private Rectangle frameAt, dustAt, islandHitArea, workArea;
        private bool requested, missingAsset, facingRight, positioned;
        private float alpha;
        private float bodyScale = CubScale, growthStart = CubScale, growthTarget = CubScale;
        private LeopardFood feedingFood;
        private bool favoriteMeal, wasAirborne;
        private PointF floorPoint;
        private float tailBrush;
        private double groomingAt = Double.NegativeInfinity, pawAt = Double.NegativeInfinity, nextPaw, nextGarland;
        private double nextHeart;
        private double born, previousFrame, hitAt = Double.NegativeInfinity, dustBorn = Double.NegativeInfinity;
        private double fedAt = Double.NegativeInfinity;
        private double strokedAt = Double.NegativeInfinity, lastStrokeAt = Double.NegativeInfinity;
        private double weatherShakeAt = Double.PositiveInfinity, snowHoldUntil, firstSnowAt, nextFineSnow, dustUntil;
        private Point lastTouch;
        private float affection, stroke, touchSide, touchLift, coatMass, shakeStrength;
        private bool touching, petAfterShake, brushedThisPetting, mealPending;
        private int coatCount, grainCount;

        public event Action<PointF, float, double> Landed;
        public AlphaOverlay SpriteLayer { get { return overlay; } }

        private struct Heart
        {
            public double Born;
            public float Drift, Size;
        }

        private struct SnowGrain
        {
            public float X, Y, Vx, Vy, Size, Lifetime, Delay;
        }

        private struct SnowOnFur
        {
            public int Vertex;
            public float Mass, Phase, Visibility;
        }

        public Leopard(Form owner, float dpi, Func<bool> allowFeeding, Action requestFeed)
        {
            scale = dpi;
            feedingAllowed = allowFeeding;
            life = new LeopardLife(random);
            sleepLetters = new[]
            {
                new Font("Segoe UI Semibold", 8.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel),
                new Font("Segoe UI Semibold", 10.7f * scale, FontStyle.Bold, GraphicsUnit.Pixel),
                new Font("Segoe UI Semibold", 12.7f * scale, FontStyle.Bold, GraphicsUnit.Pixel)
            };
            overlay = new AlphaOverlay(owner, "HappyNewIsland.SnowLeopard",
                point => CanFeed && feedingAllowed() && IsPointerOver(point), requestFeed);
            snowDust = new AlphaOverlay(owner, "HappyNewIsland.LeopardSnow");
        }

        public bool CanFeed
        {
            get { return CanPlay && !Playing && !life.AtPlayPosition; }
        }

        public bool CanPlay
        {
            get
            {
                return requested && rig != null && overlay.Visible && alpha > .5f && !mealPending && Double.IsNegativeInfinity(groomingAt) &&
                    Double.IsNegativeInfinity(fedAt) && Double.IsNegativeInfinity(hitAt);
            }
        }

        public bool Playing { get; private set; }

        private bool MealWaitsForPose { get { return life.Asleep || life.Rising || life.Turning || life.Hopping; } }

        public void StartPlaying()
        {
            if (CanPlay) Playing = true;
        }

        public void StopPlaying() { Playing = false; }

        public void SetGrowth(int growthDays)
        {
            growthStart = growthTarget = bodyScale = Growth(growthDays);
        }

        public void Feed(int growthDays, LeopardFood food, bool favorite, double now)
        {
            growthStart = bodyScale;
            growthTarget = Growth(growthDays);
            feedingFood = food;
            favoriteMeal = favorite;
            if (MealWaitsForPose)
            {
                mealPending = true;
                life.WakeSoon();
            }
            else fedAt = now;
        }

        private static float Growth(int days)
        {
            float progress = Math.Max(0, Math.Min(IslandModes.DaysToGrow, days)) / (float)IslandModes.DaysToGrow;
            return CubScale + (MaxGrowth - CubScale) * progress;
        }

        public void SetEnabled(bool on, double now)
        {
            if (on == requested) return;
            requested = on;
            Suspend(now);
        }

        public void Animate(Rectangle island, Rectangle capsule, double now, bool compact, bool desktopActive, Point? toyTarget = null)
        {
            if (!requested || missingAsset || island.Width < Px(50)) return;
            if (rig == null)
            {
                rig = LeopardModel.Load(scale);
                if (rig == null) { missingAsset = true; return; }
            }

            DrawDust(now);
            if (islandHitArea != island || workArea.IsEmpty)
                workArea = Screen.FromRectangle(island).WorkingArea;
            islandHitArea = island;
            bool playing = Playing && desktopActive;
            bool useCapsule = playing || life.AtPlayPosition;
            float dt = positioned ? (float)Math.Max(0, Math.Min(.105, now - previousFrame)) : 0;
            MoveAnchor(useCapsule ? capsule : island, now);
            double impact = now - hitAt;
            if (impact >= ShakeTime + Recovery && !Double.IsNegativeInfinity(hitAt))
            {
                hitAt = Double.NegativeInfinity;
                if (petAfterShake) strokedAt = now;
                petAfterShake = false;
            }
            if (mealPending && !MealWaitsForPose && Double.IsNegativeInfinity(hitAt))
            {
                mealPending = false;
                fedAt = now;
            }
            double meal = now - fedAt;
            if (meal >= FeedTime && !Double.IsNegativeInfinity(fedAt))
            {
                fedAt = Double.NegativeInfinity;
                bodyScale = growthTarget;
                if (favoriteMeal)
                {
                    AddHeart(now);
                    groomingAt = now;
                }
                else if (random.NextDouble() < .51) groomingAt = now;
            }
            double wash = now - groomingAt;
            if (wash >= 1.36) groomingAt = Double.NegativeInfinity;
            bool reacting = !Double.IsNegativeInfinity(hitAt), feeding = !Double.IsNegativeInfinity(fedAt);
            bool grooming = !Double.IsNegativeInfinity(groomingAt);
            bool acceptsTouch = !feeding && !grooming && !life.Hopping && feedingAllowed();
            Point cursor = playing || acceptsTouch ? Control.MousePosition : Point.Empty;
            TrackTouch(cursor, dt, now, acceptsTouch);
            if (!playing && !feeding && !reacting && !life.Hopping && ShedWeatherSnow(cursor, now, acceptsTouch))
            {
                impact = 0;
                reacting = true;
            }

            var pose = new LeopardPose
            {
                Time = now,
                ShakeAge = reacting ? impact : -1,
                ShakeStrength = shakeStrength,
                Pet = affection, PetSide = touchSide, PetLift = touchLift
            };
            if (feeding)
            {
                pose.Eat = (float)(Ease((meal - .12) / .34) * Ease((1.19 - meal) / .29) *
                    (.92 + .08 * Math.Sin((meal - .5) * 26)));
                if (favoriteMeal) pose.Delight = (float)(Ease(meal / .25) * Ease((FeedTime - meal) / .45));
                bodyScale = growthStart + (growthTarget - growthStart) * (float)Ease((meal - .60) / .91);
            }
            if (grooming)
                pose.Groom = (float)(Ease(wash / .29) * Ease((1.29 - wash) / .33) * (.84 + .16 * Math.Sin(wash * 17)));
            double tap = now - pawAt;
            if (toyTarget.HasValue && tap < .75)
                pose.ToyTap = (float)(Ease(tap / .19) * Ease((.65 - tap) / .31));
            else pawAt = Double.NegativeInfinity;

            double held = feeding ? meal : (reacting ? impact : 0);
            double release = feeding ? FeedTime : ShakeTime + Recovery;
            float grounding = (float)(Ease(held / .17) * Ease((release - held) / .24));
            float edge = smoothIsland.Width * .5f / rig.PixelsPerUnit;
            float room = Math.Min(3.3f, edge + Px(84) / rig.PixelsPerUnit);
            float depthScale = LeopardModel.ViewSin * rig.PixelsPerUnit;
            float cornerDepth = (Px(38) * bodyScale - Px(FloorBelowIsland)) / depthScale;
            float center = smoothIsland.Left + smoothIsland.Width * .5f;
            float floor = smoothIsland.Bottom + Px(FloorBelowIsland);
            float clearance = .66f * bodyScale;
            float rightRoom = Math.Max(0, (workArea.Right - Px(9) - center) / rig.PixelsPerUnit - clearance);
            var restSpot = new Vector3(-1.56f, 0, Math.Min(edge + clearance + Px(18) / rig.PixelsPerUnit, rightRoom));
            Point goal = toyTarget.HasValue ? ApproachSnowball(toyTarget.Value) : cursor;
            Vector2? target = playing ? (Vector2?)PlayTarget(goal, capsule, center, floor) : null;
            Vector3 root = life.Update(dt, room, bodyScale, affection, grounding, feeding || reacting || grooming,
                compact, edge, cornerDepth, restSpot, target, ref pose);
            alpha = (float)Ease((now - born) / .41);
            if (alpha < .008f) { overlay.Hide(); return; }

            // The floor is shared by the path and the renderer so a planted 3D
            // contact stays at the same pixel while the body walks over it.
            center += root.Z * rig.PixelsPerUnit;
            float restGround = floor + (compact || useCapsule ? root.X : Math.Max(-.34f, root.X)) * depthScale;
            floorPoint = new PointF(center, restGround);
            bool airborne = root.Y > .018f;
            if (playing && wasAirborne && !airborne && Landed != null) Landed(floorPoint, pose.Orientation, now);
            wasAirborne = playing && airborne;
            tailBrush = pose.TailBrush;
            float ground = restGround - root.Y * LeopardModel.ViewLevel * rig.PixelsPerUnit;
            Size frame = rig.LogicalSize;
            float x = center - rig.Pivot * bodyScale;
            float top = ground - rig.Ground * bodyScale;
            int left = (int)Math.Floor(x), y = (int)Math.Floor(top);
            frameAt = new Rectangle(left, y, (int)Math.Ceiling(frame.Width * MaxGrowth), (int)Math.Ceiling(frame.Height * MaxGrowth));
            rig.Render(pose, (x - left) / bodyScale, (top - y) / bodyScale);
            facingRight = rig.Muzzle.X >= rig.Pivot;
            if (life.WokeUp && coatCount > 0)
                StartShake(frameAt.X + rig.Pivot * bodyScale, frameAt.Y + Px(63), now, ShakeCause.Waking);
            DrawBody(meal, dt, pose.Sleep, pose.Yawn, now);
        }

        public void FocusEnded() { life.RequestFocusTap(); }

        public bool TryTapSnowball(PointF ball, double now, out PointF push)
        {
            push = new PointF(ball.X - floorPoint.X, (ball.Y - floorPoint.Y) * .70f);
            if (!overlay.Visible || !Playing || !life.AtPlayPosition || life.Hopping || now < nextPaw) return false;
            if (push.X * push.X + push.Y * push.Y > Px(54) * Px(54))
            {
                pawAt = Double.NegativeInfinity;
                return false;
            }
            if (Double.IsNegativeInfinity(pawAt)) pawAt = now;
            if (now - pawAt < .21) return false;
            nextPaw = now + 1.9;
            if (Math.Abs(push.X) + Math.Abs(push.Y) < Px(4))
                push = new PointF(facingRight ? 1 : -1, -.2f);
            return true;
        }

        public void ClearToyTap() { pawAt = Double.NegativeInfinity; }

        private Point ApproachSnowball(Point ball)
        {
            if (!overlay.Visible) return ball;
            float x = ball.X - floorPoint.X, y = ball.Y - floorPoint.Y;
            float distance = (float)Math.Sqrt(x * x + y * y);
            float space = distance > 1 ? Math.Min(Px(16) / distance, .34f) : 0;
            return new Point((int)Math.Round(ball.X - x * space), (int)Math.Round(ball.Y - y * space * .54f));
        }

        public bool TryBrushGarland(Rectangle capsule, double now, out float origin)
        {
            origin = .5f;
            if (!overlay.Visible || tailBrush < .57f || now < nextGarland || capsule.Width < 2) return false;
            PointF tail = new PointF(frameAt.X + rig.TailPoint.X * bodyScale, frameAt.Y + rig.TailPoint.Y * bodyScale);
            if (tail.X < capsule.Left - Px(7) || tail.X > capsule.Right + Px(7) ||
                tail.Y < capsule.Bottom - Px(12) || tail.Y > capsule.Bottom + Px(10)) return false;
            origin = Math.Max(0, Math.Min(1, (tail.X - capsule.Left) / capsule.Width));
            nextGarland = now + 5.6;
            return true;
        }

        private Vector2 PlayTarget(Point cursor, Rectangle island, float center, float floor)
        {
            Rectangle work = Screen.FromRectangle(island).WorkingArea;
            float side = Math.Min(work.Width * .31f, rig.Pivot * bodyScale * .77f + Px(6));
            float top = work.Top + Math.Min(work.Height / 3f, rig.Ground * bodyScale * .86f + Px(33));
            float x = Math.Max(work.Left + side, Math.Min(work.Right - side, cursor.X));
            float y = Math.Max(top, Math.Min(work.Bottom - Px(7), cursor.Y + Px(16)));
            return new Vector2((x - center) / rig.PixelsPerUnit, (y - floor) / rig.PixelsPerUnit);
        }

        public bool IsPointerOver(Point pointer)
        {
            return overlay.Visible && alpha > .38f && frameAt.Contains(pointer) &&
                !islandHitArea.Contains(pointer) && ContainsFur(pointer.X, pointer.Y);
        }

        private void TrackTouch(Point pointer, float dt, double now, bool allowed)
        {
            bool inside = allowed && IsPointerOver(pointer);
            stroke = Math.Max(0, stroke - dt * .78f);
            if (inside && touching)
            {
                float dx = pointer.X - lastTouch.X, dy = pointer.Y - lastTouch.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance >= 1 && distance <= Px(58))
                {
                    stroke = Math.Min(1, stroke + distance / Px(31));
                    if (stroke >= .19f)
                    {
                        if (now - lastStrokeAt > PettingGap) brushedThisPetting = false;
                        lastStrokeAt = now;
                        strokedAt = now;
                        if (now >= nextHeart && hearts.Count < 6)
                        {
                            AddHeart(now);
                        }
                    }
                }
            }
            if (inside)
            {
                float nose = frameAt.X + rig.Muzzle.X * bodyScale;
                float face = Math.Max(-1, Math.Min(1, (pointer.X - nose) / Px(49)));
                float height = Math.Max(-1, Math.Min(1, (frameAt.Y + rig.Muzzle.Y * bodyScale - pointer.Y) / Px(29)));
                float follow = (float)(1 - Math.Exp(-dt * 7));
                touchSide += (face - touchSide) * follow;
                touchLift += (height - touchLift) * follow;
            }
            lastTouch = pointer;
            touching = inside;
            float target = allowed && Double.IsNegativeInfinity(hitAt) ? (float)Ease((1.9 - (now - strokedAt)) / .9) : 0;
            affection += (target - affection) * (float)(1 - Math.Exp(-dt * (target > affection ? 7.5 : 3.9)));
        }

        private void MoveAnchor(Rectangle island, double now)
        {
            if (!positioned || now - previousFrame > .28)
            {
                smoothIsland = island;
                positioned = true;
            }
            else
            {
                float weight = (float)(1 - Math.Exp(-(now - previousFrame) * 12));
                smoothIsland = new RectangleF(smoothIsland.X + (island.X - smoothIsland.X) * weight,
                    smoothIsland.Y + (island.Y - smoothIsland.Y) * weight,
                    smoothIsland.Width + (island.Width - smoothIsland.Width) * weight,
                    smoothIsland.Height + (island.Height - smoothIsland.Height) * weight);
            }
            previousFrame = now;
        }

        private void AddHeart(double now)
        {
            if (hearts.Count >= 6) return;
            hearts.Add(new Heart { Born = now, Drift = Range(-15, 15), Size = Range(3.5f, 5.5f) });
            nextHeart = now + .24;
        }

        private void DrawBody(double meal, float dt, float sleep, float yawn, double now)
        {
            Bitmap frame = rig.Frame;
            for (int i = hearts.Count - 1; i >= 0; i--)
                if (now - hearts[i].Born >= HeartLifetime) hearts.RemoveAt(i);
            int headroom = hearts.Count > 0 ? Px(42) : 0;
            Graphics canvas = overlay.Begin(new Size(frameAt.Width, frameAt.Height + headroom));
            canvas.InterpolationMode = InterpolationMode.HighQualityBilinear;
            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
            GraphicsState state = canvas.Save();
            canvas.TranslateTransform(0, headroom);
            canvas.ScaleTransform(bodyScale, bodyScale);
            var area = new Rectangle(Point.Empty, rig.LogicalSize);
            if (alpha > .998f)
                canvas.DrawImage(frame, area, new Rectangle(Point.Empty, frame.Size), GraphicsUnit.Pixel);
            else
            {
                fade.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                canvas.DrawImage(frame, area, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, fade);
            }
            DrawWeatherSnow(canvas, dt);
            DrawSleepLetters(canvas, sleep, now);
            if (yawn > .07f) DrawYawn(canvas, yawn);
            DrawTreat(canvas, meal);
            canvas.Restore(state);
            if (hearts.Count > 0) DrawHearts(canvas, headroom, now);
            overlay.Show(new Point(frameAt.X, frameAt.Y - headroom));
        }

        private void DrawYawn(Graphics canvas, float open)
        {
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            float side = facingRight ? 1 : -1;
            GraphicsState state = canvas.Save();
            canvas.TranslateTransform(rig.Muzzle.X - scale * side * .45f, rig.Muzzle.Y + scale * .67f);
            canvas.ScaleTransform(side * scale, scale);
            using (var shape = new GraphicsPath())
            using (var jaw = new SolidBrush(Color.FromArgb((int)(187 * open * alpha), 31, 22, 28)))
            using (var tongue = new SolidBrush(Color.FromArgb((int)(106 * open * alpha), 180, 110, 112)))
            {
                shape.AddBezier(-3.2f, .19f, -1.5f, -.38f, -.17f, -.34f, 2.0f, .21f);
                shape.AddBezier(2.0f, .21f, 1.65f, 3.6f * open, .55f, 4.72f * open, -1.03f, 4.35f * open);
                shape.AddBezier(-1.03f, 4.35f * open, -2.43f, 3.98f * open, -2.92f, 1.33f * open, -3.2f, .19f);
                shape.CloseFigure();
                canvas.FillPath(jaw, shape);
                canvas.FillEllipse(tongue, -1.7f, 2.96f * open, 2.61f, .8f * open);
            }
            canvas.Restore(state);
        }

        private static GraphicsPath MakeHeart()
        {
            var shape = new GraphicsPath();
            shape.AddBezier(0, .85f, -.23f, .59f, -1.08f, .08f, -.86f, -.41f);
            shape.AddBezier(-.86f, -.41f, -.66f, -.91f, -.23f, -.84f, 0, -.46f);
            shape.AddBezier(0, -.46f, .23f, -.84f, .66f, -.91f, .86f, -.41f);
            shape.AddBezier(.86f, -.41f, 1.08f, .08f, .23f, .59f, 0, .85f);
            shape.CloseFigure();
            return shape;
        }

        private void DrawHearts(Graphics canvas, int headroom, double now)
        {
            float x = bodyScale * rig.Muzzle.X;
            float y = headroom + bodyScale * rig.Muzzle.Y;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Color.Empty))
            using (var edge = new Pen(Color.Empty, .16f))
                foreach (Heart heart in hearts)
                {
                    double time = now - heart.Born;
                    float progress = (float)(time / HeartLifetime);
                    float fade = (float)(Ease(time / .15) * Ease((1 - progress) / .37f)) * alpha;
                    if (fade < .01f) continue;
                    float size = scale * heart.Size * (.82f + .23f * (float)Ease(progress / .29));
                    GraphicsState state = canvas.Save();
                    canvas.TranslateTransform(x + scale * (heart.Drift * (.32f + progress) + 2.2f * (float)Math.Sin(time * 4.1)),
                        y - scale * (7 + 35 * progress));
                    canvas.RotateTransform(heart.Drift * .72f);
                    canvas.ScaleTransform(size, size);
                    fill.Color = Color.FromArgb((int)(228 * fade), 248, 121, 158);
                    edge.Color = Color.FromArgb((int)(195 * fade), 255, 205, 216);
                    canvas.FillPath(fill, heartShape);
                    canvas.DrawPath(edge, heartShape);
                    canvas.Restore(state);
                }
        }

        private void DrawSleepLetters(Graphics canvas, float sleep, double now)
        {
            if (sleep < .52f) return;
            float quiet = LeopardSkeleton.Ease((sleep - .52f) / .41f) * alpha;
            const double cycle = 4.6;
            float direction = facingRight ? 1 : -1;
            canvas.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var ink = new SolidBrush(Color.White))
                for (int i = 0; i < sleepLetters.Length; i++)
                {
                    float phase = (float)((now + i * (cycle / sleepLetters.Length)) % cycle / cycle);
                    float visible = quiet * LeopardSkeleton.Ease(phase / .16f) *
                        LeopardSkeleton.Ease((1 - phase) / .29f);
                    if (visible < .02f) continue;
                    float x = rig.Muzzle.X + direction * scale * (2 + 16f * phase + 3.6f * (float)Math.Sin(phase * 4.7 + i)) -
                        sleepLetters[i].Size * .28f;
                    float y = Math.Max(scale * 1.3f, rig.Muzzle.Y - scale * (14 + 31 * phase));
                    ink.Color = Color.FromArgb((int)(165 * visible), 13, 24, 37);
                    canvas.DrawString("Z", sleepLetters[i], ink, x + .68f * scale, y + .91f * scale, sleepFormat);
                    ink.Color = Color.FromArgb((int)(235 * visible), 221, 244, 255);
                    canvas.DrawString("Z", sleepLetters[i], ink, x, y, sleepFormat);
                }
        }

        private void DrawTreat(Graphics canvas, double meal)
        {
            if (meal >= .69) return;
            double opacity = Ease(meal / .14) * Ease((.69 - meal) / .18);
            float direction = facingRight ? 1 : -1;
            float unit = scale * 1.22f;
            float x = rig.Muzzle.X + direction * unit * (5.4f - 3.3f * (float)Ease(meal / .54));
            float y = rig.Muzzle.Y + unit * (2.7f - 2.1f * (float)Ease(meal / .45));
            Color crust = feedingFood == LeopardFood.Fish ? Color.FromArgb(112, 174, 188) :
                (feedingFood == LeopardFood.Treat ? Color.FromArgb(163, 110, 78) : Color.FromArgb(199, 101, 98));
            Color shine = feedingFood == LeopardFood.Fish ? Color.FromArgb(195, 224, 228) :
                (feedingFood == LeopardFood.Treat ? Color.FromArgb(226, 174, 117) : Color.FromArgb(251, 165, 155));
            GraphicsState state = canvas.Save();
            canvas.TranslateTransform(x, y);
            canvas.ScaleTransform(direction * unit, unit);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var food = new SolidBrush(Color.FromArgb((int)(236 * opacity), crust)))
            using (var glaze = new SolidBrush(Color.FromArgb((int)(185 * opacity), shine)))
            {
                if (feedingFood == LeopardFood.Fish)
                {
                    canvas.FillEllipse(food, 0, 0, 6.8f, 3.5f);
                    canvas.FillPolygon(food, new[] { new PointF(5.6f, 1.75f), new PointF(8.8f, -.15f), new PointF(8.8f, 3.65f) });
                }
                else canvas.FillEllipse(food, 0, 0, feedingFood == LeopardFood.Treat ? 4.3f : 6.1f, 4.1f);
                canvas.FillEllipse(glaze, .91f, .2f, 2.55f, 1.4f);
            }
            canvas.Restore(state);
        }

        public bool TryHit(PointF from, PointF to, float radius, double now)
        {
            if (!requested || !overlay.Visible || alpha < .22f || rig == null ||
                !Double.IsNegativeInfinity(hitAt) || !Double.IsNegativeInfinity(fedAt)) return false;
            float left = Math.Min(from.X, to.X) - radius, right = Math.Max(from.X, to.X) + radius;
            float top = Math.Min(from.Y, to.Y) - radius, bottom = Math.Max(from.Y, to.Y) + radius;
            if (right < frameAt.Left || left > frameAt.Right || bottom < frameAt.Top || top > frameAt.Bottom) return false;
            float dx = to.X - from.X, dy = to.Y - from.Y;
            int probes = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / Math.Max(2, radius * .53)));
            float near = radius * .86f;
            for (int i = 0; i <= probes; i++)
            {
                float x = from.X + dx * i / probes, y = from.Y + dy * i / probes;
                if (!ContainsFur(x, y) && !ContainsFur(x + near, y) && !ContainsFur(x - near, y) &&
                    !ContainsFur(x, y + near) && !ContainsFur(x, y - near)) continue;
                life.WakeSoon();
                StartShake(x, y, now, ShakeCause.Snowball);
                return true;
            }
            return false;
        }

        public bool TryCatchSnow(PointF from, PointF to, float radius, double now)
        {
            if (!requested || !overlay.Visible || alpha < .47f || rig == null ||
                !Double.IsNegativeInfinity(hitAt) || Math.Max(from.Y, to.Y) < frameAt.Top - radius ||
                Math.Min(from.Y, to.Y) > frameAt.Bottom || Math.Min(from.X, to.X) > frameAt.Right + radius ||
                Math.Max(from.X, to.X) < frameAt.Left - radius) return false;

            float dx = to.X - from.X, dy = to.Y - from.Y;
            int steps = Math.Max(1, Math.Min(12, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / Math.Max(1, radius * .65f))));
            float fringe = radius * .52f;
            for (int step = 0; step <= steps; step++)
            {
                float x = from.X + step * dx / steps, y = from.Y + step * dy / steps;
                if (!ContainsFur(x, y) && !ContainsFur(x, y + fringe) &&
                    !ContainsFur(x - fringe, y) && !ContainsFur(x + fringe, y)) continue;
                var point = new PointF((x - frameAt.X) / bodyScale, (y - frameAt.Y) / bodyScale);
                int vertex = rig.FindSnowAnchor(point, (radius + Px(5)) / bodyScale);
                if (vertex < 0) continue;
                AddWeatherSnow(vertex, radius, now);
                return true;
            }
            return false;
        }

        private void AddWeatherSnow(int vertex, float radius, double now)
        {
            float room = MaximumSnowLoad - coatMass;
            if (room < .04f) return;
            vertex = rig.SnowRidge(vertex);
            float mass = Math.Min(room, Math.Max(.59f, Math.Min(1.63f, radius / (Px(2) + .5f)) *
                (life.Asleep ? 1.26f : 1)));
            const float clusterRadius = .041f;
            float closest = clusterRadius * clusterRadius;
            int index = -1;
            for (int i = 0; i < coatCount; i++)
            {
                if (coatSnow[i].Mass >= TuftLimit) continue;
                float distance = rig.SnowSiteDistanceSquared(coatSnow[i].Vertex, vertex);
                if (distance <= closest)
                {
                    closest = distance;
                    index = i;
                }
            }
            if (index < 0)
            {
                if (coatCount >= coatSnow.Length) return;
                if (coatCount == 0)
                {
                    firstSnowAt = now;
                    weatherShakeAt = now + Range(81, 119);
                }
                index = coatCount++;
                coatSnow[index] = new SnowOnFur { Vertex = vertex, Phase = Range(0, 6.28f) };
            }
            float added = Math.Min(mass, TuftLimit - coatSnow[index].Mass);
            coatSnow[index].Mass += added;
            coatMass += added;
            if (coatMass >= 59)
                weatherShakeAt = Math.Min(weatherShakeAt, Math.Max(firstSnowAt + 64, now + Range(7, 13)));
        }

        public void SettleFineSnow(int snowfall, double now)
        {
            if (!life.Asleep || snowfall <= 0 || rig == null || !overlay.Visible ||
                coatMass >= MaximumSnowLoad || now < nextFineSnow) return;
            nextFineSnow = now + Range(2.45f, 4.81f) * (1.69f - .007f * snowfall);

            // Fine powder drifts below the canopy even when a window catches the large flakes.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int x = (int)(rig.Pivot + rig.PixelsPerUnit * Range(-.46f, .42f));
                for (int y = Px(10); y < rig.Ground - Px(4); y += 2)
                {
                    if (!rig.OpaqueAt(x, y)) continue;
                    int vertex = rig.FindSnowAnchor(new PointF(x, y), Px(8));
                    if (vertex < 0) continue;
                    AddWeatherSnow(vertex, Range(1.9f, 3.2f) * scale, now);
                    return;
                }
            }
        }

        public void ClearWeatherSnow()
        {
            coatCount = 0;
            coatMass = 0;
            weatherShakeAt = Double.PositiveInfinity;
        }

        private bool ShedWeatherSnow(Point cursor, double now, bool acceptsTouch)
        {
            if (coatCount == 0 || !overlay.Visible || life.RestingForNap || now < snowHoldUntil) return false;
            bool brushed = acceptsTouch && lastStrokeAt == now && !brushedThisPetting && coatMass >= BrushSnowLoad;
            bool due = coatMass >= AutomaticSnowLoad && now >= weatherShakeAt && now - lastStrokeAt > PettingGap;
            if (!brushed && !due) return false;
            if (brushed) brushedThisPetting = true;
            StartShake(brushed ? cursor.X : frameAt.X + frameAt.Width * .46f,
                brushed ? cursor.Y : frameAt.Y + Px(65), now, brushed ? ShakeCause.Brushed : ShakeCause.Weather);
            return true;
        }

        private void DrawWeatherSnow(Graphics canvas, float dt)
        {
            if (coatCount == 0) return;
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            float reveal = (float)(1 - Math.Exp(-dt * 21));
            float hide = (float)(1 - Math.Exp(-dt * 11));
            using (var frost = new SolidBrush(Color.AliceBlue))
            {
                for (int i = 0; i < coatCount; i++)
                {
                    SnowOnFur tuft = coatSnow[i];
                    PointF point;
                    float target = rig.SnowVisibility(tuft.Vertex, out point);
                    tuft.Visibility += (target - tuft.Visibility) * (target > tuft.Visibility ? reveal : hide);
                    coatSnow[i].Visibility = tuft.Visibility;
                    float opacity = alpha * tuft.Visibility;
                    if (opacity < .02f) continue;
                    float fullness = (float)Math.Sqrt(tuft.Mass / TuftLimit);
                    float radius = scale * (1.05f + 3.85f * fullness);
                    if (tuft.Mass > 1.35f)
                    {
                        float pad = radius * .71f;
                        float height = scale * (1.12f + 2.31f * fullness);
                        frost.Color = Color.FromArgb((int)((112 + 117 * fullness) * opacity), 207, 225, 243);
                        canvas.FillEllipse(frost, point.X - pad, point.Y - height * .68f, pad * 2, height);
                        frost.Color = Color.FromArgb((int)((143 + 105 * fullness) * opacity), 248, 251, 255);
                        canvas.FillEllipse(frost, point.X - pad * .81f, point.Y - height * .73f,
                            pad * 1.68f, height * .66f);
                    }
                    int grainsOnTuft = 2 + (int)(4 * fullness);
                    for (int grain = 0; grain < grainsOnTuft; grain++)
                    {
                        float angle = tuft.Phase + grain * 2.399f;
                        float spread = (float)Math.Sqrt((float)grain / grainsOnTuft);
                        float x = point.X + radius * (float)Math.Cos(angle) * spread;
                        float y = point.Y + radius * .58f * (float)Math.Sin(angle) * spread;
                        float coverage = rig.CoatCoverage(x, y);
                        if (coverage < .04f) continue;
                        float size = scale * (grain == 0 ? 1.42f + fullness : .84f + .72f * fullness + .08f * (grain % 3));
                        frost.Color = Color.FromArgb((int)((grain == 0 ? 245 : 184 + 9 * grain) * opacity * coverage),
                            234 + grain * 3, 246 + grain, 255);
                        canvas.FillEllipse(frost, x - size * .5f, y - size * .43f, size, size * .78f);
                    }
                }
            }
        }

        private void StartShake(float x, float y, double now, ShakeCause cause)
        {
            hitAt = now;
            petAfterShake = cause == ShakeCause.Brushed;
            shakeStrength = cause == ShakeCause.Waking ? 1.24f : (petAfterShake ? .91f : 1);
            snowHoldUntil = now + (cause == ShakeCause.Waking ? 69 : (petAfterShake ? 31 : 51));
            ShakeOffSnow(x, y, now, cause);
            ClearWeatherSnow();
        }

        private bool ContainsFur(float x, float y)
        {
            int column = (int)Math.Floor((x - frameAt.X) / bodyScale);
            int row = (int)Math.Floor((y - frameAt.Y) / bodyScale);
            return rig.OpaqueAt(column, row);
        }

        private void ShakeOffSnow(float contactX, float contactY, double now, ShakeCause cause)
        {
            bool heavy = cause == ShakeCause.Waking && coatMass >= 24;
            dustAt = Rectangle.Inflate(frameAt, Px(heavy ? 156 : 104), Px(heavy ? 111 : 79));
            dustBorn = now;
            dustUntil = now;
            int baseGrains = cause == ShakeCause.Snowball ? 20 : (heavy ? 32 : 9);
            grainCount = Math.Min(grains.Length, baseGrains + (int)(coatMass * (heavy ? 1.27f : .61f)));
            for (int i = 0; i < grainCount; i++)
            {
                float x = contactX, y = contactY;
                PointF attached = PointF.Empty;
                bool onCoat = coatCount > 0 && rig.SnowPoint(coatSnow[i % coatCount].Vertex, out attached);
                if (onCoat)
                {
                    x = frameAt.X + attached.X * bodyScale + Range(-2, 2) * scale;
                    y = frameAt.Y + attached.Y * bodyScale + Range(-1.5f, 1.5f) * scale;
                }
                for (int attempt = 0; !onCoat && attempt < 6; attempt++)
                {
                    float along = Range(.32f, .89f);
                    float pickX = frameAt.X + rig.LogicalSize.Width * bodyScale * (facingRight ? along : 1 - along);
                    float pickY = frameAt.Y + (rig.Ground - scale * Range(13, 47)) * bodyScale;
                    if (!ContainsFur(pickX, pickY)) continue;
                    x = pickX;
                    y = pickY;
                    break;
                }
                float local = Math.Max(0, Math.Min(1, (x - frameAt.Left) / (rig.LogicalSize.Width * bodyScale)));
                float delay = .044f + .20f * (facingRight ? 1 - local : local) + Range(0, heavy ? .18f : .084f);
                float lifetime = heavy ? Range(.76f, 1.30f) : Range(.49f, .99f);
                float spread = heavy ? 146 : 91;
                float direction = local - .5f;
                grains[i] = new SnowGrain
                {
                    X = x - dustAt.X, Y = y - dustAt.Y,
                    Vx = (Range(-spread, spread) + direction * (heavy ? 77f : 30f)) * scale,
                    Vy = -Range(heavy ? 21 : 25, heavy ? 126 : 99) * scale,
                    Size = Range(.86f, heavy ? 4.04f : 2.84f) * scale, Lifetime = lifetime, Delay = delay
                };
                dustUntil = Math.Max(dustUntil, now + lifetime + delay);
            }
        }

        private void DrawDust(double now)
        {
            float age = (float)(now - dustBorn);
            if (now >= dustUntil) { snowDust.Hide(); return; }
            Graphics canvas = snowDust.Begin(dustAt.Size);
            canvas.SmoothingMode = SmoothingMode.AntiAlias;
            using (var powder = new SolidBrush(Color.White))
            {
                for (int i = 0; i < grainCount; i++)
                {
                    SnowGrain grain = grains[i];
                    float time = age - grain.Delay;
                    if (time <= 0 || time >= grain.Lifetime) continue;
                    float alpha = (float)(Ease(time / .075) * Ease((grain.Lifetime - time) / .4));
                    powder.Color = Color.FromArgb((int)(239 * alpha), 239, 248, 255);
                    float x = grain.X + grain.Vx * time, y = grain.Y + grain.Vy * time + Px(104) * time * time;
                    canvas.FillEllipse(powder, x, y, grain.Size, grain.Size * .83f);
                }
            }
            snowDust.Show(dustAt.Location);
        }

        public void Suspend(double now)
        {
            born = now;
            positioned = false;
            alpha = 0;
            hitAt = dustBorn = fedAt = strokedAt = lastStrokeAt = Double.NegativeInfinity;
            groomingAt = pawAt = Double.NegativeInfinity;
            snowHoldUntil = nextFineSnow = dustUntil = 0;
            life.Reset();
            touching = false;
            petAfterShake = brushedThisPetting = mealPending = false;
            ClearWeatherSnow();
            affection = stroke = touchSide = touchLift = 0;
            bodyScale = growthTarget;
            Hide();
        }

        public void Hide()
        {
            touching = false;
            wasAirborne = false;
            ClearToyTap();
            hearts.Clear();
            overlay.Hide();
            snowDust.Hide();
        }

        private float Range(float min, float max) { return min + (float)random.NextDouble() * (max - min); }

        private static double Ease(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        public void Dispose()
        {
            overlay.Dispose();
            snowDust.Dispose();
            fade.Dispose();
            heartShape.Dispose();
            sleepFormat.Dispose();
            foreach (Font letter in sleepLetters) letter.Dispose();
            if (rig != null) rig.Dispose();
        }

        private int Px(int logical) { return (int)Math.Round(logical * scale); }
    }
}

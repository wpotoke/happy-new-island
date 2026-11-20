using System;
using System.Collections.Generic;
using System.Numerics;

namespace WinterIsland
{
    internal struct LeopardFoot
    {
        public Vector3 Point;
        public float Pitch, Yaw, Air;
    }

    internal sealed class LeopardPath
    {
        private static readonly int[] Order = { 2, 0, 3, 1 };
        private readonly Vector3 origin, target, tangentStart, tangentEnd;
        private readonly float startAngle, angleChange, radius, scale, ramp;
        private readonly List<Step>[] steps = new List<Step>[4];
        private readonly LeopardFoot[] feet = new LeopardFoot[4];
        private float cycleTime;

        private struct Step
        {
            public float Lift, Land, FromAngle, Angle;
            public Vector3 From, To;
        }

        public float Duration { get; private set; }
        public bool IsTurn { get; private set; }
        public Vector3 End { get { return target; } }
        public float EndAngle { get { return startAngle + angleChange; } }

        private LeopardPath(Vector3 from, Vector3 to, float heading, float nextHeading,
            float seconds, float bodyScale, float turnRadius, int lead)
        {
            origin = from;
            startAngle = heading;
            angleChange = AngleDifference(nextHeading, heading);
            Duration = seconds;
            scale = bodyScale;
            radius = turnRadius;
            IsTurn = radius > 0;
            ramp = Math.Min(.33f, (IsTurn ? .35f : .55f) / seconds);
            target = IsTurn ? Arc(1) : to;
            float length = Vector3.Distance(origin, target);
            tangentStart = Forward(heading) * (.76f * length);
            tangentEnd = Forward(EndAngle) * (.76f * length);
            MakeFootprints(lead);
        }

        public static LeopardPath Walk(Vector3 from, Vector3 to, float heading, float nextHeading,
            float speed, float bodyScale, int lead)
        {
            float seconds = Math.Max(1.13f, .62f + Vector3.Distance(from, to) / speed);
            return new LeopardPath(from, to, heading, nextHeading, seconds, bodyScale, 0, lead);
        }

        public static LeopardPath Turn(Vector3 from, float heading, float nextHeading, float bodyScale, int lead)
        {
            float angle = Math.Abs(AngleDifference(nextHeading, heading));
            return new LeopardPath(from, from, heading, nextHeading, .92f + angle * .37f,
                bodyScale, .165f * bodyScale, lead);
        }

        public void Sample(float time, float liveScale, float stand, out Vector3 position, ref LeopardPose pose)
        {
            float yaw;
            Locate(time, out position, out yaw);
            float progress = time / Duration;
            float ahead;
            Vector3 ignored;
            Locate(Math.Min(time + (IsTurn ? .31f : .22f), Duration), out ignored, out ahead);
            pose.Orientation = yaw;
            pose.Feet = feet;
            pose.Guide = AngleDifference(ahead, yaw);
            pose.Cycle = (time - .065f) / cycleTime;
            pose.Walk = LeopardSkeleton.Ease(progress / .10f) * LeopardSkeleton.Ease((1 - progress) / .14f);
            pose.Arc = IsTurn ? pose.Guide * 1.45f : 0;
            if (IsTurn) pose.Walk *= .49f;

            float support = 0;
            Quaternion toLocal = Rotation(-yaw);
            for (int leg = 0; leg < feet.Length; leg++)
            {
                List<Step> track = steps[leg];
                Step contact = track[track.Count - 1];
                for (int i = 0; i < track.Count; i++)
                {
                    if (time >= track[i].Land) continue;
                    contact = track[i];
                    break;
                }
                float swing = Math.Max(0, Math.Min(1, (time - contact.Lift) / (contact.Land - contact.Lift)));
                float advance = Smooth(swing);
                float air = (float)Math.Pow(Math.Max(0, Math.Sin(Math.PI * swing)), 1.43) * (1 - stand);
                Vector3 footprint = Vector3.Lerp(contact.From, contact.To, advance);
                float rise = leg < 2 ? .031f : .039f;
                if (IsTurn) rise *= .92f;
                footprint.Y += air * rise * scale;
                float footAngle = contact.FromAngle + advance * AngleDifference(contact.Angle, contact.FromAngle);
                float toe = .34f * air * (float)Math.Cos(Math.PI * (swing + .08f));
                feet[leg] = new LeopardFoot
                {
                    Point = Vector3.Transform(footprint - position, toLocal) / liveScale,
                    Pitch = toe,
                    Yaw = AngleDifference(footAngle, yaw),
                    Air = air
                };
                support += (leg % 2 == 0 ? -1 : 1) * air;
            }
            pose.Support = support * .51f;
        }

        private void MakeFootprints(int lead)
        {
            float interval = IsTurn ? .169f : .173f;
            int beats = Math.Max(IsTurn ? 8 : 6, (int)Math.Round((Duration - .29f) / interval) + 1);
            float gap = (Duration - .28f) / (beats - 1);
            cycleTime = gap * 4;
            float swing = Math.Min(.246f, gap * 1.35f);
            var anchors = new Vector3[4];
            var angles = new float[4];
            for (int leg = 0; leg < 4; leg++)
            {
                steps[leg] = new List<Step>((beats + 3) / 4);
                anchors[leg] = Plant(leg, origin, startAngle);
                angles[leg] = startAngle;
            }

            for (int beat = 0; beat < beats; beat++)
            {
                int leg = Order[(beat + lead) % 4];
                float lift = .065f + gap * beat;
                float land = Math.Min(Duration - .009f, lift + swing);
                Vector3 next;
                float yaw;
                Locate(beat >= beats - 4 ? Duration : Math.Min(Duration, land + gap * 1.00f), out next, out yaw);
                Vector3 paw = Plant(leg, next, yaw);
                steps[leg].Add(new Step { Lift = lift, Land = land, From = anchors[leg], To = paw,
                    FromAngle = angles[leg], Angle = yaw });
                anchors[leg] = paw;
                angles[leg] = yaw;
            }
        }

        private Vector3 Plant(int leg, Vector3 point, float yaw)
        {
            Vector3 ankle = LeopardSkeleton.Joints[LeopardSkeleton.Paws[leg]].Bind;
            return point + Vector3.Transform(ankle * scale, Rotation(yaw));
        }

        private void Locate(float time, out Vector3 position, out float yaw)
        {
            float u = Progress(Math.Max(0, Math.Min(1, time / Duration)), ramp);
            if (IsTurn)
            {
                yaw = startAngle + angleChange * u;
                position = Arc(u);
                return;
            }

            float u2 = u * u, u3 = u2 * u;
            position = (2 * u3 - 3 * u2 + 1) * origin + (u3 - 2 * u2 + u) * tangentStart +
                (-2 * u3 + 3 * u2) * target + (u3 - u2) * tangentEnd;
            Vector3 slope = (6 * u2 - 6 * u) * (origin - target) +
                (3 * u2 - 4 * u + 1) * tangentStart + (3 * u2 - 2 * u) * tangentEnd;
            yaw = startAngle + AngleDifference((float)Math.Atan2(slope.X, slope.Z), startAngle);
        }

        private Vector3 Arc(float u)
        {
            float angle = startAngle + angleChange * u;
            float side = angleChange < 0 ? -radius : radius;
            return origin + new Vector3(side * (float)(Math.Cos(startAngle) - Math.Cos(angle)), 0,
                side * (float)(Math.Sin(angle) - Math.Sin(startAngle)));
        }

        private static float Progress(float u, float ramp)
        {
            if (u > .5f) return 1 - Progress(1 - u, ramp);
            if (u >= ramp) return (u - .5f * ramp) / (1 - ramp);
            float t = u / ramp;
            return ramp / (1 - ramp) * (t * t * t * (1 - .5f * t));
        }

        public static float Smooth(float value)
        {
            float t = Math.Max(0, Math.Min(1, value));
            return t * t * t * (10 - 15 * t + 6 * t * t);
        }

        public static float AngleDifference(float a, float b)
        {
            return (float)Math.Atan2(Math.Sin(a - b), Math.Cos(a - b));
        }

        private static Vector3 Forward(float angle)
        {
            return new Vector3((float)Math.Sin(angle), 0, (float)Math.Cos(angle));
        }

        private static Quaternion Rotation(float angle) { return Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle); }
    }

    internal sealed class LeopardLife
    {
        private enum Visit { None, Outside, Corner }
        private enum Nap { None, Approaching, Settling, Sleeping, Rising }
        private const float RestArrival = .09f, WakeDuration = 4.12f;
        private readonly Random random;
        private readonly LeopardNapSchedule naps;
        private readonly Func<DateTimeOffset> clock;
        private readonly LeopardPlayMotion play;
        private LeopardPath path;
        private Vector3 position, destination, corner, restSpot;
        private float heading, pathTime, wait, rubHeading, rubbing, nextRub, nextRest;
        private float sit, lie, fromSit, fromLie, toSit, toLie, settling, settleDuration;
        private bool plannedWalk, ready, first, turned, hadPlayTarget;
        private bool loungePending;
        private bool focusTapPending, focusVisit, tailVisit, nextTailVisit = true;
        private Visit visit;
        private Nap nap;
        private double nextNapCheck;
        private int lead;

        public LeopardLife(Random random) : this(random, new LeopardNapSchedule(), () => DateTimeOffset.Now) { }

        internal LeopardLife(Random random, LeopardNapSchedule schedule, Func<DateTimeOffset> wallClock)
        {
            this.random = random;
            naps = schedule;
            clock = wallClock;
            play = new LeopardPlayMotion(random);
        }

        public bool Turning { get { return path != null && path.IsTurn; } }
        public bool Hopping { get { return play.Jumping; } }
        public bool AtPlayPosition { get { return play.Active; } }
        public bool Asleep { get { return nap == Nap.Sleeping; } }
        public bool Rising { get { return nap == Nap.Rising; } }
        public bool RestingForNap { get { return nap == Nap.Settling || Asleep || Rising; } }
        public bool WokeUp { get; private set; }
        private bool HeadingToRest { get { return nap == Nap.Approaching || loungePending; } }

        public void WakeSoon()
        {
            if (Asleep) naps.EndEarly(clock());
        }

        public void RequestFocusTap()
        {
            focusTapPending = true;
            wait = Math.Min(wait, .35f);
        }

        private void RequeueFocusVisit()
        {
            focusTapPending |= focusVisit;
            focusVisit = false;
            tailVisit = false;
        }

        private void WakeForPlay()
        {
            if (Asleep) WakeSoon();
            if (nap == Nap.Approaching || nap == Nap.Settling) nap = Nap.None;
            loungePending = false;
            if (!RestingForNap && (sit + lie > .015f || toSit + toLie > .015f))
                Posture(0, 0, lie > .5f ? 1.38f : .88f);
            rubbing = 0;
            RequeueFocusVisit();
            visit = Visit.None;
            plannedWalk = false;
            wait = 0;
        }

        public void Reset()
        {
            path = null;
            play.Reset();
            RequeueFocusVisit();
            hadPlayTarget = false;
            ready = false;
            nap = Nap.None;
            WokeUp = false;
        }

        public Vector3 Update(float dt, float halfWidth, float scale, float pet, float grounded, bool pause,
            bool compact, float edge, float cornerDepth, Vector3 rightRest, Vector2? playTarget, ref LeopardPose pose)
        {
            WokeUp = false;
            double now = pose.Time;
            DateTimeOffset wall = clock();
            if (ready && (RestingForNap || lie > .015f || toLie > .015f))
                position += rightRest - restSpot;
            restSpot = rightRest;
            if (!ready)
            {
                position = new Vector3(-.12f, 0, -Math.Min(halfWidth * .72f, 1.84f));
                heading = .18f;
                wait = .83f;
                sit = lie = toSit = toLie = settleDuration = settling = rubbing = 0;
                plannedWalk = turned = loungePending = false;
                first = ready = true;
                nextRub = Range(10, 16);
                nextRest = Range(14, 22);
                nextNapCheck = now + Range(82, 125);
                nap = Nap.None;
                visit = Visit.None;
                lead = random.Next(4);
                if (naps.SleepingAt(wall))
                {
                    position = restSpot;
                    lie = 1;
                    nap = Nap.Sleeping;
                    first = false;
                }
            }

            if (playTarget.HasValue && !hadPlayTarget) WakeForPlay();
            hadPlayTarget = playTarget.HasValue;
            if (play.Active)
            {
                Vector2 home = new Vector2(Math.Max(-halfWidth * .36f, Math.Min(halfWidth * .36f, position.Z)), 0);
                position = play.Follow(dt, scale, playTarget ?? home, playTarget.HasValue, ref pose);
                heading = pose.Orientation;
                if (play.AtRest)
                {
                    play.Reset();
                    wait = 2.6f;
                    nextRest = Math.Max(10, nextRest);
                }
                return position;
            }
            if (pause) dt = 0;
            if (compact) nextRub -= dt;
            nextRest -= dt;
            if (!compact && (visit != Visit.None || rubbing > 0))
            {
                RequeueFocusVisit();
                visit = Visit.None;
                rubbing = 0;
                plannedWalk = false;
                wait = 1.2f;
                nextRub = Math.Max(nextRub, 5);
            }

            if (path != null)
            {
                float pace = path.IsTurn ? 1 : 1 - .53f * LeopardSkeleton.Ease((pet - .11f) / .73f);
                if (playTarget.HasValue) pace *= path.IsTurn ? 1.38f : 1.95f;
                pathTime = Math.Min(path.Duration, pathTime + dt * pace);
                if (pathTime < path.Duration)
                {
                    path.Sample(pathTime, scale, grounded, out position, ref pose);
                    return position;
                }
                position = path.End;
                heading = path.EndAngle;
                turned = path.IsTurn;
                path = null;
                if (turned)
                    wait = Range(.18f, .36f);
                else if (HeadingToRest)
                    SettleAtRest();
                else if (visit == Visit.Outside)
                {
                    visit = Visit.Corner;
                    destination = corner;
                    plannedWalk = true;
                    wait = Range(.25f, .55f);
                }
                else if (visit == Visit.Corner)
                {
                    visit = Visit.None;
                    rubbing = .001f;
                    nextRub = Range(42, 68);
                }
                else wait = Range(2.2f, 5.3f);
            }

            if (Asleep)
            {
                pose.Sleep = 1;
                if (!pause && !naps.SleepingAt(wall))
                {
                    nap = Nap.Rising;
                    Posture(0, 0, WakeDuration);
                }
            }
            else if (rubbing > 0)
            {
                rubbing += dt;
                float duration = focusVisit ? 1.78f : (tailVisit ? 2.36f : 3.12f);
                if (focusVisit)
                {
                    pose.PanelTap = LeopardSkeleton.Ease((rubbing - .13f) / .38f) *
                        LeopardSkeleton.Ease((1.60f - rubbing) / .67f) * (.84f + .16f * (float)Math.Cos(rubbing * 11));
                }
                else if (tailVisit)
                {
                    float rise = LeopardPath.Smooth((rubbing - .09f) / .87f);
                    float fall = LeopardPath.Smooth((duration - rubbing) / .91f);
                    pose.TailBrush = rise * fall;
                }
                else
                {
                    float peak = LeopardPath.Smooth((rubbing - .10f) / .63f) * LeopardPath.Smooth((duration - rubbing) / .77f);
                    pose.Rub = peak * (.82f + .18f * (float)Math.Sin(rubbing * 4.75f));
                }
                if (rubbing >= duration)
                {
                    float side = Math.Sign(position.Z);
                    Vector3 away = tailVisit
                        ? new Vector3(position.X + .48f, 0, position.Z + side * .39f)
                        : new Vector3(Range(-.14f, .19f), 0, position.Z - side * .74f);
                    rubbing = 0;
                    focusVisit = false;
                    tailVisit = false;
                    Schedule(away, Range(1.15f, 1.95f));
                }
            }
            else if (settleDuration > 0)
            {
                settling += dt;
                float progress = settling / settleDuration;
                float blend = Rising ? WakeBlend(settling, 1.52f, 2.29f) : LeopardPath.Smooth(progress);
                sit = fromSit + (toSit - fromSit) * blend;
                lie = fromLie + (toLie - fromLie) * blend;
                if (nap == Nap.Settling) pose.Sleep = LeopardPath.Smooth((progress - .36f) / .64f);
                if (Rising)
                {
                    SampleWake(settling, ref pose);
                }
                if (progress >= 1)
                {
                    settleDuration = 0;
                    if (nap == Nap.Settling)
                    {
                        if (naps.TryStart(wall)) nap = Nap.Sleeping;
                        else
                        {
                            nap = Nap.None;
                            pose.Sleep = 0;
                            wait = Range(9f, 14f);
                            nextNapCheck = now + 52;
                        }
                    }
                    else if (Rising)
                    {
                        nap = Nap.None;
                        WokeUp = true;
                        wait = Range(2.8f, 4.6f);
                        nextRest = Math.Max(nextRest, Range(23, 43));
                        nextRub = Math.Max(nextRub, Range(7, 13));
                    }
                }
            }
            else if (!pause)
            {
                if (playTarget.HasValue) play.Begin(position, heading, playTarget.Value);
                else if (pet > .24f) wait = Math.Max(wait, 1.1f);
                else
                {
                    wait -= dt;
                    if (wait <= 0)
                    {
                        if (plannedWalk) BeginWalk(scale);
                        else Decide(halfWidth, scale, compact, edge, cornerDepth, now, wall);
                    }
                }
            }

            pose.Orientation = heading;
            pose.Sit = sit;
            pose.Lie = lie;
            return position;
        }

        private void Decide(float halfWidth, float scale, bool compact, float edge, float cornerDepth, double now, DateTimeOffset wall)
        {
            if (position.X > 1.20f || Math.Abs(position.Z) > halfWidth + .28f)
            {
                float near = Math.Max(-halfWidth * .76f, Math.Min(halfWidth * .76f, position.Z));
                Schedule(new Vector3(.10f, 0, near), .12f);
                return;
            }
            double choice = random.NextDouble();
            if (focusTapPending && compact)
                ApproachIslandEdge(edge, cornerDepth, scale, true);
            else if (!first && now >= nextNapCheck && naps.CanStart(wall))
            {
                nap = Nap.Approaching;
                SettleAtRest();
            }
            else if (!first && compact && nextRub <= 0)
                ApproachIslandEdge(edge, cornerDepth, scale, false);
            else if (!first && sit + lie < .12f && nextRest <= 0 && choice < .35)
            {
                if (random.NextDouble() < .58)
                {
                    loungePending = true;
                    SettleAtRest();
                }
                else
                {
                    Posture(1, 0, Range(1.4f, 1.72f));
                    wait = Range(7.8f, 13.7f);
                    nextRest = wait + Range(21, 35);
                }
            }
            else if (!first && sit + lie < .12f && choice < .22 && position.X > -.75f)
                wait = Range(3.4f, 7.1f);
            else
            {
                float direction = position.Z < -.23f ? 1 : (position.Z > .23f ? -1 : (random.Next(2) == 0 ? -1 : 1));
                float length = Math.Min(halfWidth * 1.7f, Range(.83f, 2.86f));
                if (first) length = halfWidth * 1.33f;
                float goal = Math.Max(-halfWidth, Math.Min(halfWidth, position.Z + direction * length));
                Schedule(new Vector3(Range(-.22f, .28f), 0, goal), .04f);
            }
            first = false;
        }

        private void ApproachIslandEdge(float edge, float cornerDepth, float scale, bool forTimer)
        {
            int side = position.Z > .04f ? 1 : -1;
            bool tailFirst = !forTimer && nextTailVisit;
            if (!forTimer) nextTailVisit = !nextTailVisit;
            float reach = edge + .30f * scale - .048f;
            if (tailFirst && Math.Abs(position.Z) > reach - .14f) side = -side;
            tailVisit = tailFirst;
            rubHeading = tailFirst ? (side < 0 ? (float)Math.PI + .12f : -.12f) :
                (side < 0 ? .14f : (float)Math.PI - .14f);
            float depth = cornerDepth - (tailFirst ? .37f * scale : 0);
            corner = new Vector3(depth, 0, side * (edge + (tailFirst ? .30f : .438f) * scale - .048f));
            focusVisit = forTimer;
            if (forTimer) focusTapPending = false;
            visit = Visit.Outside;
            Vector3 approach = tailFirst
                ? new Vector3(corner.X + .44f, 0, corner.Z - side * (.67f + .14f * scale))
                : new Vector3(corner.X + .24f, 0, corner.Z + side * .54f);
            Schedule(approach, .21f);
        }

        private void SettleAtRest()
        {
            if (Vector3.DistanceSquared(position, restSpot) > RestArrival * RestArrival)
            {
                Schedule(restSpot, .24f);
                return;
            }
            plannedWalk = false;
            if (nap == Nap.Approaching)
            {
                nap = Nap.Settling;
                Posture(0, 1, 2.55f);
            }
            else
            {
                loungePending = false;
                Posture(0, 1, Range(2.05f, 2.38f));
                wait = Range(9.8f, 18.2f);
                nextRest = wait + Range(21, 35);
            }
        }

        private void Schedule(Vector3 at, float after)
        {
            destination = at;
            plannedWalk = true;
            turned = false;
            wait = after;
            if (sit + lie > .015f)
                Posture(0, 0, lie > .5f ? Range(1.89f, 2.23f) : Range(1.31f, 1.62f));
        }

        private void BeginWalk(float scale)
        {
            if (HeadingToRest) destination = restSpot;
            Vector3 delta = destination - position;
            float distance = delta.Length();
            float stoppingDistance = visit == Visit.None ? (HeadingToRest ? RestArrival : .33f) : .04f;
            if (distance <= stoppingDistance)
            {
                if (HeadingToRest)
                {
                    SettleAtRest();
                    return;
                }
                plannedWalk = false;
                wait = Range(2.3f, 3.9f);
                if (visit != Visit.None) { RequeueFocusVisit(); visit = Visit.None; nextRub = 9; }
                return;
            }
            float along = (float)Math.Atan2(delta.X, delta.Z);
            float final = visit == Visit.Corner ? rubHeading : along + (delta.Z >= 0 ? .18f : -.18f);
            float change = LeopardPath.AngleDifference(along, heading);
            if (!turned && Math.Abs(change) > .90f)
                path = LeopardPath.Turn(position, heading, along, scale, lead++);
            else
            {
                path = LeopardPath.Walk(position, destination, heading, final,
                    focusVisit ? .63f : (visit == Visit.Corner ? .285f : Range(.34f, .49f)), scale, lead++);
                plannedWalk = false;
                turned = false;
            }
            pathTime = 0;
        }

        private void Posture(float sitting, float lying, float seconds)
        {
            fromSit = sit;
            fromLie = lie;
            toSit = sitting;
            toLie = lying;
            settling = 0;
            settleDuration = seconds;
        }

        private static void SampleWake(float time, ref LeopardPose pose)
        {
            pose.Sleep = 1 - WakeBlend(time, 0, .88f);
            pose.Yawn = WakeBlend(time, .15f, .62f) * (1 - WakeBlend(time, .82f, 1.29f));
            // Keep the chest low until the front paws start stepping back in.
            pose.Stretch = WakeBlend(time, 1.60f, 2.39f) * (1 - WakeBlend(time, 2.88f, 3.69f));
            pose.LeftReach = WakeBlend(time, 1.19f, 1.80f) * (1 - WakeBlend(time, 3.08f, 3.71f));
            pose.RightReach = WakeBlend(time, 1.49f, 2.09f) * (1 - WakeBlend(time, 3.31f, 4.02f));
        }

        private static float WakeBlend(float time, float start, float end)
        {
            return LeopardPath.Smooth((time - start) / (end - start));
        }

        private float Range(float low, float high) { return low + (float)random.NextDouble() * (high - low); }
    }
}

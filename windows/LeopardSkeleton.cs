using System;
using System.Numerics;

namespace WinterIsland
{
    internal struct LeopardPose
    {
        public double Time, ShakeAge;
        public float Cycle, Walk, Sit, Eat, Sleep, ShakeStrength, Jump, JumpPitch, Crouch;
        public float Delight, Groom, Yawn, Stretch, LeftReach, RightReach, PanelTap, ToyTap, TailBrush;
        public LeopardFoot[] Feet;
        public float Orientation, Guide, Arc, Support, Lie, Pet, PetSide, PetLift, Rub;
    }

    internal sealed class LeopardSkeleton
    {
        internal struct Joint
        {
            public readonly string Name;
            public readonly int Parent;
            public readonly Vector3 Bind;

            public Joint(string name, int parent, float x, float y, float z)
            {
                Name = name;
                Parent = parent;
                Bind = new Vector3(x, y, z);
            }
        }

        internal struct Transform
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        public const int Pelvis = 1, Spine = 2, Chest = 3, Neck = 4, Head = 5, Tail = 6;
        private const float Pi = (float)Math.PI;
        private static readonly Vector3 Nose = new Vector3(-.013f, .354f, .477f);
        private static readonly int[] LegRoots = { 11, 14, 17, 21 };
        internal static readonly int[] Paws = { 13, 16, 20, 24 };

        public static readonly Joint[] Joints =
        {
            new Joint("Root", -1, 0, 0, 0),
            new Joint("Pelvis", 0, 0, .304f, -.166f),
            new Joint("Spine", 1, 0, .322f, -.018f),
            new Joint("Chest", 2, 0, .318f, .145f),
            new Joint("Neck", 3, -.003f, .357f, .247f),
            new Joint("Head", 4, -.009f, .398f, .326f),
            new Joint("Tail.01", 1, -.009f, .248f, -.241f),
            new Joint("Tail.02", 6, -.048f, .159f, -.323f),
            new Joint("Tail.03", 7, -.089f, .090f, -.401f),
            new Joint("Tail.04", 8, -.139f, .075f, -.492f),
            new Joint("Tail.05", 9, -.170f, .085f, -.574f),
            new Joint("Shoulder.L", 3, .069f, .277f, .183f),
            new Joint("Elbow.L", 11, .065f, .155f, .165f),
            new Joint("Forepaw.L", 12, .068f, .041f, .213f),
            new Joint("Shoulder.R", 3, -.072f, .277f, .183f),
            new Joint("Elbow.R", 14, -.068f, .155f, .163f),
            new Joint("Forepaw.R", 15, -.074f, .041f, .212f),
            new Joint("Hip.L", 1, .071f, .278f, -.187f),
            new Joint("Stifle.L", 17, .066f, .186f, -.108f),
            new Joint("Hock.L", 18, .062f, .085f, -.177f),
            new Joint("Hindpaw.L", 19, .064f, .042f, -.139f),
            new Joint("Hip.R", 1, -.073f, .279f, -.191f),
            new Joint("Stifle.R", 21, -.067f, .189f, -.110f),
            new Joint("Hock.R", 22, -.073f, .086f, -.183f),
            new Joint("Hindpaw.R", 23, -.072f, .042f, -.143f),
            new Joint("Ear.L", 5, .051f, .456f, .307f),
            new Joint("Ear.R", 5, -.058f, .457f, .311f)
        };

        public readonly Transform[] Bones = new Transform[Joints.Length];
        public Quaternion Yaw { get; private set; }
        public Vector3 Muzzle { get { return Map(Head, Nose); } }
        public Vector3 TailTip { get { return Map(Tail + 4, Joints[Tail + 4].Bind + new Vector3(-.018f, .004f, -.04f)); } }

        public void Sample(LeopardPose pose)
        {
            float sit = pose.Sit;
            float lie = pose.Lie;
            float sleep = pose.Sleep;
            float stretch = pose.Stretch;
            float curlSleep = Ease(sleep / .9f);
            float cycle = pose.Cycle * 2 * Pi;
            float angle = pose.Orientation;
            Yaw = RotationY(angle);

            float strength = pose.ShakeStrength > 0 ? pose.ShakeStrength : 1;
            float headShake = Ripple(pose.ShakeAge, .055, .63, 4.8) * strength;
            float chestShake = Ripple(pose.ShakeAge, .125, .63, 4.8) * strength;
            float hipShake = Ripple(pose.ShakeAge, .204, .57, 4.8) * strength;
            float recoil = Ease((float)(pose.ShakeAge / .063)) * Ease((float)((.24 - pose.ShakeAge) / .17));
            float listening = ((float)Math.Sin(pose.Time * .93) + .23f * (float)Math.Sin(pose.Time * 1.67)) *
                .087f * (1 - pose.Walk) * (1 - sleep) * (1 - .85f * stretch);
            float bounce = ((float)Math.Cos(2 * cycle + .6) * .0033f - .014f) * pose.Walk -
                Math.Abs(pose.Arc) * .027f;
            float sway = (float)Math.Sin(cycle) * .0049f * pose.Walk + .012f * pose.Support + pose.Arc * .018f;
            float awakeBreath = (float)Math.Sin(pose.Time * 2.05) * (.0036f + lie * .0018f);
            float breath = awakeBreath * (1 - sleep) + (float)Math.Sin(pose.Time * 1.42) * .0048f * sleep;
            float pet = pose.Pet * (1 - .83f * sleep) * (1 - .8f * stretch);
            float rubbing = pet * (.58f + .42f * (float)Math.Sin(pose.Time * 4.1));
            float rubSide = pose.PetSide * pet;
            float cheek = pose.Rub * (.75f + .25f * (float)Math.Sin(pose.Time * 3.9));
            float gait = (float)Math.Sin(cycle - .48f) * pose.Walk;

            Bones[0] = new Transform { Rotation = Quaternion.Identity };
            Set(Pelvis, Joints[Pelvis].Bind + new Vector3(sway + lie * .007f,
                    -.175f * sit - .164f * lie + bounce - .024f * recoil - .058f * pose.Crouch + .024f * stretch,
                    -.013f * sit + .020f * lie - .015f * pose.Crouch + .019f * stretch),
                Look(hipShake * .070f - .17f * pose.Guide - .08f * curlSleep,
                    -.60f * sit + .032f * lie + .055f * recoil - .26f * pose.JumpPitch + .23f * stretch,
                    sway * 2.7f + hipShake * .105f + pose.Arc * .125f + .034f * lie));
            Attach(Spine, Look(chestShake * .052f + pose.Guide * .19f + .15f * curlSleep,
                -.042f * sit + .049f * lie + breath + .011f * gait + .23f * stretch, -hipShake * .04f - .032f * lie));
            Attach(Chest, Look(chestShake * .096f + .25f * pose.Guide + .14f * curlSleep,
                .274f * sit + .085f * lie + .105f * pose.Eat + .07f * rubbing - .083f * cheek - .008f * gait + .10f * pose.Crouch +
                .11f * stretch + .12f * pose.Groom - .075f * pose.PanelTap,
                -chestShake * .10f + .071f * rubSide - .047f * pose.Support));
            Attach(Neck, Look(headShake * .14f + .63f * pose.Guide + .14f * rubSide + listening + .31f * curlSleep,
                .265f * sit + .088f * lie + .25f * pose.Eat + .14f * recoil
                    - .172f * rubbing - .043f * pose.PetLift * (1 - sleep) - .21f * pose.Rub + .114f * sleep -
                    .18f * pose.Yawn + .19f * pose.Groom - .10f * pose.PanelTap + .23f * stretch,
                chestShake * .07f + rubbing * .098f + .08f * rubSide + .12f * cheek));
            Bones[Neck].Position += new Vector3(.024f * rubSide, -.009f * rubbing + .028f * pose.Rub - .041f * sleep + .016f * stretch,
                .011f * rubbing + .017f * cheek - .056f * curlSleep);
            Attach(Head, Look(headShake * .37f + pose.Guide * .42f + .15f * rubSide - .56f * listening + .31f * curlSleep,
                .080f * sit - .124f * lie + .185f * pose.Eat - .12f * headShake +
                    breath * 2.4f - .085f * rubbing - .12f * cheek + .035f * sleep - .16f * pose.Yawn + .15f * pose.Groom + .025f * stretch,
                .24f * headShake - .49f * rubbing - .16f * rubSide - .23f * cheek - .20f * sleep));
            float dream = Ease(((float)Math.Sin(pose.Time * .46) - .91f) / .09f) * .055f * sleep;
            float flick = (float)Math.Sin(pose.Time * 7.2) * (pet * .20f + dream + pose.Delight * .20f);
            Attach(25, Look(.05f + flick, -.08f * rubbing, -.21f * rubbing));
            Attach(26, Look(-.05f + flick * .57f, -.11f * rubbing, .16f * rubbing));

            MoveTail(pose, cycle, angle);
            for (int leg = 0; leg < LegRoots.Length; leg++)
                MoveLeg(leg, pose);
        }

        private void MoveTail(LeopardPose pose, float cycle, float heading)
        {
            Vector3 start = Map(Pelvis, Joints[Tail].Bind);
            float rest = Math.Min(1, pose.Sit + pose.Lie);
            float walk = pose.Walk * (1 - rest);
            start.Y = Math.Max(start.Y + rest * .038f, .069f);
            float side = Math.Cos(heading) >= 0 ? 1 : -1;
            for (int i = Tail; i <= Tail + 4; i++)
            {
                int at = i - Tail;
                Vector3 bind = Joints[i].Bind;
                Vector3 segment = i < Tail + 4 ? Joints[i + 1].Bind - bind : new Vector3(-.018f, .004f, -.04f);
                float wave = (float)Math.Sin(pose.Time * 2.4 - at * .62f) * (1 - .85f * pose.Sleep);
                float follow = (float)Math.Sin(cycle - at * .67f - .85f);
                float lift = (float)Math.Sin(cycle - at * .56f + .66f);
                float shake = Ripple(pose.ShakeAge, .24 + at * .038, .53, 4.9) *
                    (pose.ShakeStrength > 0 ? pose.ShakeStrength : 1);
                Quaternion wag = Look((.118f + .17f * walk) * wave +
                    walk * ((.089f + .017f * at) * follow - .068f * pose.Support) +
                    .24f * shake - .29f * pose.Guide + .28f * pose.Delight * (float)Math.Sin(pose.Time * 9.5 - at * .68) +
                    .115f * pose.TailBrush * (1 - .15f * at),
                    .075f * shake + walk * (.20f + (.11f + .012f * at) * lift) + (.18f - .02f * at) * pose.Jump +
                    .52f * pose.TailBrush + .13f * pose.Delight + (.34f - .060f * at) * pose.Stretch,
                    0);
                Vector3 direction = Vector3.Transform(segment, wag);
                float curl = .17f + .23f * at + .16f * pose.Lie + (.24f + .42f * at) * pose.Sleep +
                    (.043f + .023f * at) * wave + .075f * pose.Pet;
                Vector3 drape = new Vector3(side * (float)Math.Sin(curl), 0, -(float)Math.Cos(curl));
                float length = segment.Length();
                Vector3 flat = Vector3.Normalize(new Vector3(direction.X, 0, direction.Z));
                flat = Vector3.Normalize(Vector3.Lerp(flat, drape, rest));
                // The centerline must clear the whole cross-section, especially the heavy tip.
                float clearance = .081f + at * .0051f;
                float homeY = clearance + .008f + .006f * wave;
                float desiredY = (1 - rest) * (start.Y + direction.Y) + rest * homeY;
                float dy = Math.Max(-length * .86f, Math.Min(length * .86f, Math.Max(clearance, desiredY) - start.Y));
                direction = flat * (float)Math.Sqrt(length * length - dy * dy) + Vector3.UnitY * dy;
                Set(i, start, Between(segment, direction));
                start += direction;
            }
        }

        private void MoveLeg(int leg, LeopardPose pose)
        {
            int upper = LegRoots[leg];
            bool rear = leg >= 2;
            int end = upper + (rear ? 3 : 2);
            Vector3 ankle = Joints[end].Bind;
            Vector3 target = ankle;
            float pitch = 0, yaw = 0;
            if (pose.Feet != null)
            {
                LeopardFoot foot = pose.Feet[leg];
                target = foot.Point;
                pitch = foot.Pitch;
                yaw = foot.Yaw;
            }
            else if (rear)
            {
                float tuck = Ease((pose.Sit - (leg % 2 == 0 ? .06f : .19f)) / .58f);
                target.Z += .026f * tuck;
                target.X += (leg % 2 == 0 ? 1 : -1) * .022f * tuck;
                target.Y += .016f * (float)Math.Sin(Math.PI * tuck);
            }
            float side = leg % 2 == 0 ? 1 : -1;
            float lounge = Ease((pose.Lie - (leg % 2 == 0 ? .053f : .15f)) / .68f);
            target.Z += lounge * (rear ? .018f : (leg == 0 ? .029f : .031f));
            target.X += lounge * side * (rear ? .052f : .011f);
            target.Y += (float)Math.Sin(Math.PI * lounge) * (rear ? .026f : .022f);
            target.Z += (rear ? .041f : -.058f) * pose.Sleep;
            target.X -= side * .015f * pose.Sleep;
            target.Y += (rear ? .089f : .108f) * pose.Jump;
            target.Z += (rear ? .076f : -.053f) * pose.Jump + (rear ? -.025f : .044f) * pose.JumpPitch;
            pitch += (rear ? .40f : -.38f) * pose.Jump;
            if (!rear)
            {
                float reach = leg == 0 ? pose.LeftReach : pose.RightReach;
                float forward = leg == 0 ? .225f : .199f;
                target = Vector3.Lerp(target, ankle + new Vector3(side * .025f, 0, forward), reach);
                target.Y += .029f * (float)Math.Sin(Math.PI * reach);
            }
            if (leg == 0)
            {
                target.Y += .178f * pose.Groom + .235f * pose.PanelTap + .112f * pose.ToyTap;
                target.Z += .108f * pose.Groom + .135f * pose.PanelTap + .204f * pose.ToyTap;
                pitch -= .47f * pose.Groom + .41f * pose.PanelTap + .23f * pose.ToyTap;
            }

            Vector3 hip = Map(Joints[upper].Parent, Joints[upper].Bind);
            if (!rear)
                hip += new Vector3(0, -.014f, .018f) * pose.Sit + new Vector3(side * .008f, .014f, .048f) * pose.Lie +
                    new Vector3(0, -.019f, .030f) * pose.Stretch;
            Vector3 bindThigh = Joints[upper + 1].Bind - Joints[upper].Bind;
            Vector3 bindShin = Joints[upper + 2].Bind - Joints[upper + 1].Bind;
            Vector3 distal = target;
            if (rear)
            {
                Quaternion hockAngle = Look(.78f * yaw, -.66f * pose.Sit - .72f * pose.Lie + .38f * pitch,
                    side * pose.Lie * .13f);
                distal += Vector3.Transform(Joints[upper + 2].Bind - ankle, hockAngle);
            }

            float maxReach = bindThigh.Length() + bindShin.Length() - .007f;
            Vector3 stretch = distal - hip;
            float extension = stretch.Length();
            if (extension > maxReach)
                hip += stretch * (Math.Min(extension - maxReach, rear ? .030f : .054f) / extension);
            Vector3 pole = new Vector3(side * (.095f + (rear ? .55f : .24f) * pose.Lie), -.19f * pose.Lie, rear ? 1 : -1);
            Vector3 bend;
            Solve(hip, distal, bindThigh.Length(), bindShin.Length(), pole, out bend, out distal);
            Set(upper, hip, Between(bindThigh, bend - hip));
            Set(upper + 1, bend, Between(bindShin, distal - bend));
            if (rear)
            {
                Vector3 metatarsal = ankle - Joints[upper + 2].Bind;
                Vector3 foot = distal + Vector3.Normalize(target - distal) * metatarsal.Length();
                Set(upper + 2, distal, Between(metatarsal, foot - distal));
                target = foot;
            }
            else target = distal;
            Set(end, target, Look(yaw, pitch, 0));
        }

        private static void Solve(Vector3 root, Vector3 goal, float a, float b, Vector3 pole,
            out Vector3 knee, out Vector3 reachable)
        {
            Vector3 ray = goal - root;
            float distance = Math.Max(.0001f, ray.Length());
            Vector3 direction = ray / distance;
            float length = Math.Max(Math.Abs(a - b) + .0001f, Math.Min(a + b - .0001f, distance));
            Vector3 bend = Vector3.Normalize(pole - Vector3.Dot(pole, direction) * direction);
            float along = (a * a - b * b + length * length) / (2 * length);
            knee = root + along * direction + (float)Math.Sqrt(Math.Max(0, a * a - along * along)) * bend;
            reachable = root + direction * length;
        }

        private void Attach(int i, Quaternion local)
        {
            int parent = Joints[i].Parent;
            Set(i, Map(parent, Joints[i].Bind), Quaternion.Normalize(Bones[parent].Rotation * local));
        }

        private void Set(int i, Vector3 position, Quaternion rotation)
        {
            Bones[i] = new Transform { Position = position, Rotation = rotation };
        }

        private Vector3 Map(int i, Vector3 bind)
        {
            return Bones[i].Position + Vector3.Transform(bind - Joints[i].Bind, Bones[i].Rotation);
        }

        private static Quaternion Between(Vector3 from, Vector3 to)
        {
            from = Vector3.Normalize(from);
            to = Vector3.Normalize(to);
            float match = Vector3.Dot(from, to);
            if (match < -.9999f)
            {
                Vector3 axis = Vector3.Cross(from, Math.Abs(from.X) > .8f ? Vector3.UnitY : Vector3.UnitX);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), Pi);
            }
            return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + match));
        }

        private static Quaternion Look(float yaw, float pitch, float roll)
        {
            return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }

        private static Quaternion RotationY(float angle) { return Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle); }

        public static float Ease(float value)
        {
            float t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        private static float Ripple(double age, double delay, double duration, double hz)
        {
            double t = age - delay;
            if (t <= 0 || t >= duration) return 0;
            return (float)Math.Sin(t * hz * 2 * Math.PI) * Ease((float)(t / .062)) *
                Ease((float)((duration - t) / (duration * .71)));
        }
    }
}

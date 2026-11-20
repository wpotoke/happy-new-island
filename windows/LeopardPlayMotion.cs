using System;
using System.Numerics;

namespace WinterIsland
{
    internal sealed class LeopardPlayMotion
    {
        private const float Windup = .16f, Flight = .46f, Landing = .24f;
        private static readonly float[] Phases = { .04f, .55f, .52f, .01f };
        private readonly Random random;
        private readonly LeopardFoot[] feet = new LeopardFoot[4];
        private readonly LeopardFoot[] takeoffFeet = new LeopardFoot[4];
        private Vector2 position, pointer, velocity, takeoffFrom, takeoff, landing;
        private float yaw, stride, gait, creep, stalkTime, cooldown, jumpTime = -1, launchCrouch, launchGait;
        private bool stalkNext;

        public LeopardPlayMotion(Random source) { random = source; }
        public bool Active { get; private set; }
        public bool Jumping { get { return jumpTime >= 0; } }
        public bool AtRest { get; private set; }

        public void Begin(Vector3 start, float heading, Vector2 target)
        {
            position = new Vector2(start.Z, start.X * LeopardModel.ViewSin);
            pointer = target;
            yaw = heading;
            velocity = Vector2.Zero;
            stride = gait = creep = stalkTime = 0;
            jumpTime = -1;
            cooldown = .85f;
            stalkNext = random.Next(2) == 0;
            Active = true;
            AtRest = false;
        }

        public void Reset()
        {
            Active = false;
            jumpTime = -1;
        }

        public Vector3 Follow(float seconds, float scale, Vector2 target, bool playing, ref LeopardPose pose)
        {
            float dt = Math.Min(.066f, seconds);
            float tracking = 1 - (float)Math.Exp(-10.5 * dt);
            pointer += (target - pointer) * tracking;
            if (Jumping) return Hop(dt, scale, ref pose);

            Vector2 delta = pointer - position;
            float distance = delta.Length();
            Vector2 direction = distance > .001f ? delta / distance : Vector2.Zero;
            cooldown -= dt;
            if (!playing) stalkTime = 0;
            if (stalkTime > 0)
            {
                stalkTime = Math.Max(0, stalkTime - dt);
                if (stalkTime == 0) BeginHop(direction, distance);
            }
            else if (playing && cooldown <= 0 && distance > .68f)
            {
                if (stalkNext) stalkTime = Random(.82f, 1.35f);
                else BeginHop(direction, distance);
            }

            if (Jumping) return Hop(dt, scale, ref pose);
            bool stalking = stalkTime > 0;
            creep += ((stalking ? 1f : 0f) - creep) * (1 - (float)Math.Exp(-8.5 * dt));
            float runSpeed = (playing ? 1.78f : 2.18f) + scale * .38f;
            float speed = Math.Min(stalking ? .57f : runSpeed,
                Math.Max(0, (distance - (playing ? .24f : .065f)) * 4.0f));
            Vector2 desired = direction * speed;
            velocity += (desired - velocity) * (1 - (float)Math.Exp(-(stalking ? 8.0 : 6.2) * dt));
            position += velocity * dt;
            float moving = velocity.Length();
            Vector2 facing = moving > .15f ? velocity : delta;
            if (facing.LengthSquared() > .013f)
                Face(facing, dt, stalking ? 3.7f : 5.3f, ref pose);
            pose.Orientation = yaw;
            pose.Crouch = creep * .83f;
            Step(dt, scale, moving, ref pose);
            AtRest = !playing && distance < .16f && moving < .072f && gait < .06f;
            return Root(0);
        }

        private void BeginHop(Vector2 direction, float distance)
        {
            takeoffFrom = position;
            float brake = Math.Max(0, Vector2.Dot(velocity, direction)) * .066f;
            float reach = Math.Min(1.42f, Math.Max(.08f, distance - .15f));
            takeoff = position + direction * Math.Min(reach * .32f, brake);
            landing = position + direction * reach;
            launchCrouch = creep * .83f;
            launchGait = gait;
            Array.Copy(feet, takeoffFeet, feet.Length);
            jumpTime = 0;
            velocity = Vector2.Zero;
            stalkNext = !stalkNext;
            cooldown = Random(1.8f, 3.35f);
        }

        private Vector3 Hop(float dt, float scale, ref LeopardPose pose)
        {
            jumpTime = Math.Min(Windup + Flight + Landing, jumpTime + dt);
            float flight = Math.Max(0, Math.Min(1, (jumpTime - Windup) / Flight));
            float arc = 4 * flight * (1 - flight);
            position = jumpTime < Windup
                ? Vector2.Lerp(takeoffFrom, takeoff, LeopardSkeleton.Ease(jumpTime / Windup))
                : Vector2.Lerp(takeoff, landing, LeopardSkeleton.Ease(flight));
            float crouch = LeopardSkeleton.Ease(jumpTime / .10f) * LeopardSkeleton.Ease((.245f - jumpTime) / .11f);
            pose.Crouch = Math.Max(launchCrouch * LeopardSkeleton.Ease((Windup - jumpTime) / .11f), .87f * crouch) +
                .66f * LeopardSkeleton.Ease((jumpTime - .57f) / .12f) * LeopardSkeleton.Ease((.86f - jumpTime) / .17f);
            pose.Jump = arc;
            pose.JumpPitch = arc * (1 - 2 * flight);
            if (jumpTime < Windup && launchGait > .06f)
            {
                float brace = LeopardSkeleton.Ease(jumpTime / Windup);
                pose.Feet = feet;
                pose.Cycle = stride;
                pose.Walk = launchGait * (1 - brace);
                for (int leg = 0; leg < feet.Length; leg++)
                {
                    Vector3 ankle = LeopardSkeleton.Joints[LeopardSkeleton.Paws[leg]].Bind;
                    feet[leg] = new LeopardFoot { Point = Vector3.Lerp(takeoffFeet[leg].Point, ankle, brace),
                        Pitch = takeoffFeet[leg].Pitch * (1 - brace) };
                }
            }
            Face(landing - takeoffFrom, dt, 4.15f, ref pose);
            pose.Orientation = yaw;
            gait = creep = 0;
            if (jumpTime >= Windup + Flight + Landing) jumpTime = -1;
            return Root(.286f * scale * arc);
        }

        private void Face(Vector2 vector, float dt, float speed, ref LeopardPose pose)
        {
            float toward = (float)Math.Atan2(vector.Y * 2.0f, vector.X);
            float difference = LeopardPath.AngleDifference(toward, yaw);
            yaw += Math.Max(-speed, Math.Min(speed, difference * 6.8f)) * dt;
            pose.Guide = Math.Max(-.55f, Math.Min(.55f, difference * .45f));
        }

        private void Step(float dt, float scale, float speed, ref LeopardPose pose)
        {
            float activity = LeopardSkeleton.Ease(speed / .88f);
            gait += (activity - gait) * (1 - (float)Math.Exp(-10 * dt));
            if (gait < .008f) return;
            stride += dt * speed / (.50f + .24f * scale);
            stride -= (float)Math.Floor(stride);
            pose.Cycle = stride;
            pose.Walk = gait;
            pose.Feet = feet;
            float support = 0;
            for (int leg = 0; leg < feet.Length; leg++)
            {
                float phase = (stride + Phases[leg]) % 1;
                float stance = .55f + .17f * creep;
                float swing = Math.Max(0, (phase - stance) / (1 - stance));
                float air = (float)Math.Sin(swing * Math.PI) * gait;
                float offset = phase < stance ? .5f - phase / stance : LeopardSkeleton.Ease(swing) - .5f;
                Vector3 ankle = LeopardSkeleton.Joints[LeopardSkeleton.Paws[leg]].Bind;
                ankle.Z += offset * gait * (.32f - .16f * creep);
                ankle.Y += air * (.056f - .031f * creep);
                feet[leg] = new LeopardFoot { Point = ankle, Air = air, Pitch = air * .32f * (1 - 2 * swing) };
                support += (leg % 2 == 0 ? -1 : 1) * air;
            }
            pose.Support = support * .46f;
        }

        private Vector3 Root(float elevation) { return new Vector3(position.Y / LeopardModel.ViewSin, elevation, position.X); }
        private float Random(float minimum, float maximum) { return minimum + (float)random.NextDouble() * (maximum - minimum); }
    }
}

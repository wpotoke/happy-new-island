using System;

namespace WinterIsland
{
    internal enum FocusStage { Idle, Running, Paused, Completed }

    internal sealed class FocusCountdown
    {
        private double duration, saved, started;
        public FocusStage Stage { get; private set; }

        public void Start(int seconds, double now)
        {
            duration = Math.Max(1, seconds);
            saved = 0;
            started = now;
            Stage = FocusStage.Running;
        }

        public void PauseOrContinue(double now)
        {
            if (Stage == FocusStage.Running)
            {
                saved = Elapsed(now);
                Stage = FocusStage.Paused;
            }
            else if (Stage == FocusStage.Paused)
            {
                started = now;
                Stage = FocusStage.Running;
            }
        }

        public void Reset() { Stage = FocusStage.Idle; duration = saved = 0; }

        public bool Tick(double now)
        {
            if (Stage != FocusStage.Running || Elapsed(now) < duration) return false;
            saved = duration;
            Stage = FocusStage.Completed;
            return true;
        }

        public float Progress(double now) { return duration > 0 ? (float)(Elapsed(now) / duration) : 0; }
        public int SecondsLeft(double now) { return (int)Math.Ceiling(duration - Elapsed(now)); }

        private double Elapsed(double now)
        {
            return Math.Min(duration, saved + (Stage == FocusStage.Running ? Math.Max(0, now - started) : 0));
        }
    }
}

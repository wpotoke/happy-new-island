using System.Numerics;
using Avalonia;
using Avalonia.Media;

namespace WinterIsland;

internal sealed class PortableLeopard : IDisposable
{
    public const int Width = 260, Height = 144;
    private const int Ground = 109;
    private readonly LeopardLife life = new(Random.Shared,
        new LeopardNapSchedule(Path.Combine(DesktopProfile.DataFolder, "leopard-naps.xml")), () => DateTimeOffset.Now);
    private LeopardModel? model;
    private readonly List<double> hearts = [];
    private readonly List<int> snow = [];
    private static readonly IBrush Heart = WinterArt.Brush("#F196AD");
    private static readonly IBrush Snow = WinterArt.Brush("#DAEDF7");
    private double time, mealAt = double.NegativeInfinity, shakeAt = double.NegativeInfinity, lastHeart, nextSnow;
    private bool mealQueued, asleep;
    private float body = .64f, pet;
    private Point lastPointer;
    private Rect sprite;
    public PixelRect ScreenArea { get; private set; }

    public void Feed() { mealQueued = true; life.WakeSoon(); }
    public void FocusEnded() => life.RequestFocusTap();
    public void Shake()
    {
        if (time - shakeAt < 1.4) return;
        shakeAt = time;
    }

    public PixelPoint Advance(double dt, PixelPoint centerFloor, double dpi, double islandWidth, PixelRect work,
        PixelPoint? pointer, bool playing, DesktopProfile options)
    {
        LoadModel();
        time += dt;
        var pose = new LeopardPose { Time = time, ShakeAge = time - shakeAt < .96 ? time - shakeAt : -1 };
        double meal = time - mealAt;
        if (mealQueued && !life.Asleep && !life.Rising && !life.Hopping)
        {
            mealAt = time;
            meal = 0;
            mealQueued = false;
        }
        pose.Eat = .92f * LeopardSkeleton.Ease((float)(meal / .36)) * LeopardSkeleton.Ease((float)((1.42 - meal) / .31));
        float goal = .64f + .68f * options.GrowthDays / 7f;
        body += (goal - body) * (float)Math.Min(1, dt * 3.3);
        double logicalUnit = LeopardModel.LogicalPixelsPerUnit;
        float edge = (float)(islandWidth / 2 / logicalUnit);
        float screenRoom = (float)((work.Right - centerFloor.X) / dpi / logicalUnit);
        var rest = new Vector3(-1.56f, 0, Math.Min(edge + .66f * body + .16f, screenRoom - .67f * body));
        Vector2? target = null;
        if (pointer is PixelPoint mouse)
        {
            var local = new Point((mouse.X - ScreenArea.X) / dpi, (mouse.Y - ScreenArea.Y) / dpi);
            bool touched = sprite.Contains(local) && model!.OpaqueAt((int)((local.X - sprite.X) / body), (int)((local.Y - sprite.Y) / body));
            if (touched && !playing && Math.Abs(local.X - lastPointer.X) + Math.Abs(local.Y - lastPointer.Y) > 1)
            {
                pet = Math.Min(1, pet + .45f);
                if (time >= lastHeart + .29)
                {
                    hearts.Add(time);
                    lastHeart = time;
                    if (snow.Count > 17) Shake();
                }
            }
            lastPointer = local;
            if (playing)
            {
                double x = Math.Clamp(mouse.X, work.X + 75 * dpi, Math.Max(work.X + 75 * dpi, work.Right - 75 * dpi));
                double y = Math.Clamp(mouse.Y + 12 * dpi, work.Y + 64 * dpi, Math.Max(work.Y + 64 * dpi, work.Bottom - 20 * dpi));
                target = new Vector2((float)((x - centerFloor.X) / dpi / logicalUnit), (float)((y - centerFloor.Y) / dpi / logicalUnit));
            }
        }
        pet = Math.Max(0, pet - (float)dt * .76f);
        pose.Pet = pet;
        Vector3 root = life.Update((float)dt, Math.Min(3.3f, edge + .72f), body, pet, 0, meal < 1.55,
            true, edge, -1.8f, rest, target, ref pose);
        asleep = pose.Sleep > .82f;
        if (life.WokeUp && snow.Count > 0) Shake();
        if (time - shakeAt is > .21 and < .96 && snow.Count > 0) snow.Clear();
        model!.Render(pose, 0, 0);
        sprite = new Rect((Width - model.LogicalSize.Width * body) / 2, Ground - model.Ground * body,
            model.LogicalSize.Width * body, model.LogicalSize.Height * body);
        if (options.Snow && time >= nextSnow && snow.Count < 30)
        {
            nextSnow = time + (asleep ? .38 : 1.1);
            double sx = model.LogicalSize.Width * (.25 + .5 * Random.Shared.NextDouble());
            double sy = model.Ground * (.45 + .24 * Random.Shared.NextDouble());
            int hit = model.FindSnowAnchor(new System.Drawing.PointF((float)sx, (float)sy), 15);
            if (hit >= 0 && !snow.Contains(hit)) snow.Add(hit);
        }
        if (!options.Snow) snow.Clear();
        hearts.RemoveAll(born => time - born > 1.2);
        double xAt = centerFloor.X + root.Z * logicalUnit * dpi - Width * dpi / 2;
        double yAt = centerFloor.Y + (root.X * LeopardModel.ViewSin - root.Y * LeopardModel.ViewLevel) * logicalUnit * dpi - Ground * dpi;
        var position = new PixelPoint((int)Math.Round(xAt), (int)Math.Round(yAt));
        ScreenArea = new PixelRect(position, new PixelSize((int)Math.Round(Width * dpi), (int)Math.Round(Height * dpi)));
        return position;
    }

    public void Paint(DrawingContext canvas, Size size)
    {
        if (model == null) return;
        using var scale = canvas.PushTransform(Matrix.CreateScale(size.Width / Width, size.Height / Height));
        canvas.DrawImage(model.Frame, new Rect(model.Frame.Size), sprite);
        foreach (int flake in snow)
            if (model.SnowPoint(flake, out System.Drawing.PointF at))
                canvas.DrawEllipse(Snow, null, new Point(sprite.X + at.X * body, sprite.Y + at.Y * body), 1.1, .7);
        foreach (double born in hearts)
        {
            double age = time - born;
            using var opacity = canvas.PushOpacity(Math.Min(1, (1.2 - age) * 2.5));
            WinterArt.Text(canvas, "♥", new Point(Width / 2 + 25 + Math.Sin(born) * 12, 30 - age * 23), 14, Heart);
        }
        if (asleep)
        {
            double wave = time % 2;
            using var opacity = canvas.PushOpacity(Math.Min(1, (2 - wave) * 1.4));
            WinterArt.Text(canvas, "z  Z", new Point(Width / 2 + 25 + wave * 8, 37 - wave * 14), 13, Snow);
        }
    }

    public void Preview()
    {
        model ??= LeopardModel.Load(2f) ?? throw new InvalidDataException("Missing snow leopard model.");
        model!.Render(new LeopardPose { Orientation = .17f, ShakeAge = -1, Sit = .85f, Time = 1.3 }, 0, 0);
        sprite = new Rect(24, 16, model.LogicalSize.Width * .58, model.LogicalSize.Height * .58);
    }

    private void LoadModel() => model ??= LeopardModel.Load(1f)
        ?? throw new InvalidDataException("Missing snow leopard model.");
    public void Dispose() => model?.Dispose();
}

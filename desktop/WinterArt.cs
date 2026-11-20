using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WinterIsland;

internal sealed class DrawSurface(Action<DrawingContext, Size> paint) : Control
{
    public override void Render(DrawingContext context) => paint(context, Bounds.Size);
}

internal static class WinterArt
{
    public static Bitmap Image(string name)
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://HappyNewIsland/Assets/" + name));
        return new Bitmap(stream);
    }

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static readonly Typeface Font = new("Segoe UI, Inter, sans-serif");

    public static void Text(DrawingContext canvas, string text, Point origin, double size, IBrush color)
    {
        canvas.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, Font, size, color), origin);
    }
}

internal sealed class GarlandView : Control, IDisposable
{
    internal const double ShellRadius = 23;
    internal static readonly Thickness ShellInset = new(7, 13, 7, 5);
    private static readonly Pen Branches = new(new SolidColorBrush(Color.FromUInt32((uint)GarlandLayout.BranchBase.ToArgb())),
        GarlandLayout.SeamWidth);
    private readonly Bitmap garland = WinterArt.Image("garland.png");
    private readonly Bitmap drifts = WinterArt.Image("snowdrift-atlas.png");
    private GarlandLayout? layout;
    public WinterMode Mode { get; set; }
    public bool Drifts { get; set; }
    public double Moment { get; set; }

    public GarlandView() { IsHitTestVisible = false; }

    public override void Render(DrawingContext canvas)
    {
        Rect shell = new Rect(Bounds.Size).Deflate(ShellInset);
        if (shell.Width < 60 || shell.Height < 16) return;
        var body = new System.Drawing.RectangleF((float)shell.X, (float)shell.Y, (float)shell.Width, (float)shell.Height);
        if (layout is null || layout.Capsule != body) layout = new GarlandLayout(body, 1);
        canvas.DrawRectangle(null, Branches, shell, ShellRadius, ShellRadius);
        foreach (GarlandLayout.Slice slice in layout.Branches)
        {
            var source = slice.Source;
            var target = slice.Target;
            canvas.DrawImage(garland, new Rect(source.X, source.Y, source.Width, source.Height),
                new Rect(target.X, target.Y, target.Width, target.Height));
        }
        if (Drifts)
        {
            Place(canvas, new Rect(13, 282, 614, 164), .161, .239, 17);
            Place(canvas, new Rect(647, 267, 890, 182), .478, .319, 16);
            Place(canvas, new Rect(1546, 283, 616, 164), .832, .247, 17);
        }
        for (int i = 0; i < layout.Bulbs.Length; i++)
        {
            double glow = (.5 + .5 * Math.Sin(Moment * (1.22 + i * .053) + i * 2.27));
            byte opacity = (byte)(24 + 109 * glow * glow * (Mode == WinterMode.Cozy ? .43 : 1));
            var tint = new SolidColorBrush(Color.FromArgb(opacity, 255, (byte)(i is 3 or 6 ? 118 : 215), 139));
            var bulb = layout.Bulbs[i];
            canvas.DrawEllipse(tint, null, new Point(bulb.X, bulb.Y), 1.65, 1.4);
        }
    }

    private void Place(DrawingContext canvas, Rect source, double at, double ratio, double bottom)
    {
        double width = Bounds.Width * ratio;
        double height = Math.Min(19, width * source.Height / source.Width);
        canvas.DrawImage(drifts, source, new Rect(Bounds.Width * at - width / 2, bottom - height, width, height));
    }

    public void Dispose() { garland.Dispose(); drifts.Dispose(); }
}

internal sealed class FloatingLayer : Window
{
    private readonly DrawSurface surface;
    private bool? supported;

    public FloatingLayer(Action<DrawingContext, Size> paint)
    {
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        surface = new DrawSurface(paint);
        Content = surface;
    }

    public bool Place(DesktopPlatform platform, double width, double height, PixelPoint where)
    {
        if (supported == false) return false;
        Width = width;
        Height = height;
        if (Position != where) Position = where;
        if (!IsVisible)
        {
            Show();
            supported = ActualTransparencyLevel == WindowTransparencyLevel.Transparent && platform.AllowInputThrough(this);
            if (supported == false) { Hide(); return false; }
        }
        surface.InvalidateVisual();
        return true;
    }
}

internal sealed class FallingSnow
{
    private readonly List<Particle> flakes = [];
    private double now, next;
    private static readonly Pen Crystal = new(new SolidColorBrush(Color.FromArgb(158, 237, 247, 255)), .65);
    private static readonly IBrush Dust = new SolidColorBrush(Color.FromArgb(126, 229, 245, 255));
    private readonly record struct Particle(double Born, double X, double Length, double Wave, double Drift, double Radius);

    public void Advance(double dt, double width, WinterMode mode)
    {
        now += dt;
        flakes.RemoveAll(flake => now - flake.Born >= flake.Length);
        if (now < next || flakes.Count >= 32) return;
        next = now + (mode == WinterMode.Cozy ? .58 : .23) + Random.Shared.NextDouble() * .13;
        flakes.Add(new Particle(now, 21 + Random.Shared.NextDouble() * (width - 42), 3.3 + Random.Shared.NextDouble() * 2.7,
            Random.Shared.NextDouble() * 6.28, Random.Shared.NextDouble() * 22 - 11, 1 + Random.Shared.NextDouble() * 2.1));
    }

    public void Paint(DrawingContext canvas, Size size)
    {
        foreach (Particle flake in flakes)
        {
            double age = now - flake.Born;
            double progress = age / flake.Length;
            double x = flake.X + Math.Sin(age * 1.7 + flake.Wave) * 10 + progress * flake.Drift;
            double y = 2 + progress * Math.Min(size.Height - 5, 130);
            using var opacity = canvas.PushOpacity(Math.Min(1, Math.Min(age * 3.2, (1 - progress) * 4.7)));
            if (flake.Radius < 1.65) canvas.DrawEllipse(Dust, null, new Point(x, y), 1.1, 1.1);
            else
                for (int blade = 0; blade < 3; blade++)
                {
                    double angle = blade * Math.PI / 3 + age * .52 + flake.Wave;
                    var ray = new Vector(Math.Cos(angle) * flake.Radius, Math.Sin(angle) * flake.Radius);
                    canvas.DrawLine(Crystal, new Point(x, y) - ray, new Point(x, y) + ray);
                }
        }
    }
}

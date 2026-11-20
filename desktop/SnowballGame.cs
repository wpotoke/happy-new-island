using Avalonia;
using Avalonia.Media;

namespace WinterIsland;

internal sealed class SnowballGame
{
    private const double Radius = 9, PaddleSize = 98;
    private static readonly IBrush Ice = WinterArt.Brush("#D4E6F1");
    private static readonly IBrush Furrow = WinterArt.Brush("#9AB3C7");
    private static readonly IBrush Paddle = WinterArt.Brush("#08090C");
    private static readonly Pen PaddleEdge = new(WinterArt.Brush("#717783"), 1);
    private Vector ball, velocity;
    private double pointer, width, height;
    private int score;

    public void Reset(double w, double h)
    {
        width = w;
        height = h;
        ball = new Vector(width * .5, height * .39);
        velocity = new Vector(125, 275);
        pointer = width * .5;
        score = 0;
    }

    public Point Advance(double dt, double x, double islandWidth, double islandBottom)
    {
        pointer = Math.Clamp(x, PaddleSize / 2, Math.Max(PaddleSize / 2, width - PaddleSize / 2));
        Vector previous = ball;
        ball += velocity * dt;
        if (ball.X < Radius || ball.X > width - Radius)
        {
            ball = new Vector(Math.Clamp(ball.X, Radius, Math.Max(Radius, width - Radius)), ball.Y);
            velocity = new Vector(-velocity.X, velocity.Y);
        }
        if (velocity.Y < 0 && ball.Y - Radius <= islandBottom && previous.Y - Radius > islandBottom &&
            Math.Abs(ball.X - width * .5) <= islandWidth * .5)
        {
            ball = new Vector(ball.X, islandBottom + Radius);
            velocity = new Vector(velocity.X, Math.Min(480, -velocity.Y * 1.045));
            score++;
        }
        else if (ball.Y < Radius && velocity.Y < 0)
        {
            ball = new Vector(ball.X, Radius);
            velocity = new Vector(velocity.X, -velocity.Y);
        }

        double shelf = height - 33;
        if (velocity.Y > 0 && ball.Y + Radius >= shelf && previous.Y + Radius < shelf &&
            Math.Abs(ball.X - pointer) <= PaddleSize / 2 + Radius)
        {
            ball = new Vector(ball.X, shelf - Radius);
            velocity = new Vector(Math.Clamp((ball.X - pointer) * 5.8, -335, 335), -Math.Min(450, velocity.Y * 1.018));
        }
        if (ball.Y - Radius > height) Reset(width, height);
        return new Point(ball.X, ball.Y);
    }

    public void Paint(DrawingContext canvas, Size size)
    {
        var center = new Point(ball.X, ball.Y);
        canvas.DrawEllipse(Ice, null, center, Radius, Radius);
        canvas.DrawEllipse(Brushes.White, null, center - new Vector(2.4, 2.8), 5.4, 4.5);
        canvas.DrawEllipse(Furrow, null, center + new Vector(4.5, 3.8), 2.4, 1.5);
        canvas.DrawRectangle(Paddle, PaddleEdge, new Rect(pointer - PaddleSize / 2, height - 33, PaddleSize, 10), 5, 5);
        WinterArt.Text(canvas, score.ToString(), new Point(size.Width * .5 + 24, height - 24), 11, Ice);
    }
}

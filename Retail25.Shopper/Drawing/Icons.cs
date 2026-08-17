namespace Retail25.Shopper.Drawing;

/// <summary>The show/hide-password eye, drawn beside the password field.</summary>
public sealed class EyeIcon : IDrawable
{
    /// <summary>When true the eye is struck through, meaning "the text is currently visible".</summary>
    public bool Struck { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        var cx = dirtyRect.Center.X;
        var cy = dirtyRect.Center.Y;

        canvas.StrokeColor = Color.FromArgb("#8C121430");
        canvas.StrokeSize = Math.Max(1.2f, w * 0.075f);
        canvas.StrokeLineCap = LineCap.Round;

        // The almond, as two arcs meeting at the corners.
        var half = w * 0.40f;
        var lid = h * 0.26f;

        var path = new PathF();
        path.MoveTo(cx - half, cy);
        path.QuadTo(cx, cy - (lid * 2f), cx + half, cy);
        path.QuadTo(cx, cy + (lid * 2f), cx - half, cy);
        canvas.DrawPath(path);

        canvas.DrawCircle(cx, cy, w * 0.135f);

        if (Struck)
        {
            canvas.DrawLine(cx - half, cy + (half * 0.75f), cx + half, cy - (half * 0.75f));
        }
    }
}

/// <summary>
/// A trolley with a tag being read off it â€” the pairing screen's illustration.
/// <para>
/// Everything is drawn with explicit paths. <c>DrawArc</c> takes (x, y, <em>width</em>,
/// <em>height</em>), which reads exactly like two corner points and is not; feeding it corners made
/// the signal arcs some sixty times their intended size, and they painted over the whole top of the
/// screen. Curves have no such ambiguity.
/// </para>
/// </summary>
public sealed class TrolleyMark : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var s = Math.Min(dirtyRect.Width, dirtyRect.Height);
        var x = dirtyRect.Center.X - (s / 2f);
        var y = dirtyRect.Center.Y - (s / 2f);

        // The illustration is authored on a 72-unit grid and scaled to whatever the view is.
        float U(float v) => v / 72f * s;

        canvas.StrokeColor = Color.FromArgb("#0B0D1C");
        canvas.StrokeSize = U(3.2f);
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        // Handle, then down and along the lower rail.
        var frame = new PathF();
        frame.MoveTo(x + U(7), y + U(17));
        frame.LineTo(x + U(15), y + U(17));
        frame.LineTo(x + U(23), y + U(44));
        frame.LineTo(x + U(51), y + U(44));
        canvas.DrawPath(frame);

        // The basket, tapering the way a trolley does.
        var basket = new PathF();
        basket.MoveTo(x + U(19), y + U(25));
        basket.LineTo(x + U(58), y + U(25));
        basket.LineTo(x + U(53), y + U(39));
        basket.LineTo(x + U(23), y + U(39));
        canvas.DrawPath(basket);

        canvas.DrawCircle(x + U(28), y + U(53), U(4.0f));
        canvas.DrawCircle(x + U(48), y + U(53), U(4.0f));

        // Two arcs off the basket's top-right corner: the tag being read.
        canvas.StrokeSize = U(2.4f);
        canvas.Alpha = 0.45f;

        DrawWave(canvas, x, y, U, 7f);
        DrawWave(canvas, x, y, U, 13f);

        canvas.Alpha = 1f;
    }

    /// <summary>
    /// One signal arc, bowing outward to the right of the basket. <paramref name="spread"/> is how far
    /// out from the corner it sits, in grid units.
    /// </summary>
    private static void DrawWave(ICanvas canvas, float x, float y, Func<float, float> u, float spread)
    {
        var originX = x + u(58);
        var originY = y + u(22);

        var path = new PathF();
        path.MoveTo(originX + u(spread * 0.25f), originY - u(spread * 0.85f));
        path.QuadTo(
            originX + u(spread * 1.15f),
            originY - u(spread * 0.1f),
            originX + u(spread * 0.35f),
            originY + u(spread * 0.75f));

        canvas.DrawPath(path);
    }
}


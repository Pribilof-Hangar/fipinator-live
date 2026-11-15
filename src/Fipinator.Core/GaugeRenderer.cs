// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using SkiaSharp;

namespace Fipinator.Core;

/// <summary>Draws a <see cref="GaugeDefinition"/> into a bitmap at its native size.</summary>
public sealed class GaugeRenderer : IDisposable
{
    private static readonly SKColor ShadowColor = new(0, 0, 0, 128);

    private readonly GaugeDefinition _gauge;
    private readonly SKTypeface _typeface;

    /// <param name="baseDirectory">Folder the gauge file lives in; the font path is relative to it.</param>
    public GaugeRenderer(GaugeDefinition gauge, string baseDirectory)
    {
        _gauge = gauge;
        _typeface = gauge.Font is { } font ? FindFont(font, baseDirectory, out var warning) : SKTypeface.Default;
        if (gauge.Font is { } f && _typeface == SKTypeface.Default)
            FontWarning = $"Font '{f}' wasn't found next to the gauge or the program; using the standard font.";
    }

    /// <summary>Set when the gauge's font couldn't be found and a standard font is used instead.</summary>
    public string? FontWarning { get; }

    /// <summary>
    /// The gauge's font: as written (relative to the gauge file), else a font with the same file
    /// name next to the program (FIPinator and the Gauge Maker ship their fonts), else the standard font.
    /// So gauges keep working when copied elsewhere or shared.
    /// </summary>
    private static SKTypeface FindFont(string font, string baseDirectory, out string? warning)
    {
        warning = null;
        foreach (var candidate in new[]
        {
            Path.GetFullPath(Path.Combine(baseDirectory, font)),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(font)),
        })
        {
            if (File.Exists(candidate) && SKTypeface.FromFile(candidate) is { } typeface) return typeface;
        }
        warning = font;
        return SKTypeface.Default;
    }

    public static GaugeRenderer FromFile(string gaugePath) =>
        new(GaugeDefinition.Load(gaugePath), Path.GetDirectoryName(Path.GetFullPath(gaugePath))!);

    public GaugeDefinition Gauge => _gauge;

    public SKBitmap Render(IReadOnlyDictionary<string, double>? values = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(_gauge.Canvas.Width, _gauge.Canvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Color(_gauge.Canvas.Background));
        foreach (var layer in _gauge.Layers)
        {
            canvas.Save();
            DrawLayer(canvas, layer, values);
            canvas.Restore();
        }
        return bitmap;
    }

    public byte[] RenderPng(IReadOnlyDictionary<string, double>? values = null)
    {
        using var bitmap = Render(values);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The value to draw: live value if given; else worked out from its formula; else the
    /// gauge's default; else 0.
    /// </summary>
    public double ValueOf(string name, IReadOnlyDictionary<string, double>? values) => ValueOf(name, values, 0);

    private double ValueOf(string name, IReadOnlyDictionary<string, double>? values, int depth)
    {
        if (values is not null && values.TryGetValue(name, out var live)) return live;
        if (!_gauge.Values.TryGetValue(name, out var spec)) return 0;
        if (spec.Expr is null || depth > 32) return spec.Default; // depth guard: Problems() reports loops
        if (!_formulas.TryGetValue(name, out var formula))
        {
            try { formula = ValueExpression.Parse(spec.Expr); }
            catch (FormatException) { formula = null; }
            _formulas[name] = formula;
        }
        if (formula is null) return spec.Default;
        var result = formula.Evaluate(n => ValueOf(n, values, depth + 1));
        return double.IsFinite(result) ? result : spec.Default;
    }

    private readonly Dictionary<string, ValueExpression?> _formulas = [];

    private void DrawLayer(SKCanvas canvas, Layer layer, IReadOnlyDictionary<string, double>? values)
    {
        var c = CenterOf(layer);
        switch (layer)
        {
            case RectLayer r:
                var rect = SKRect.Create(r.X, r.Y, r.Width, r.Height);
                if (r.Fill is not null) canvas.DrawRoundRect(rect, r.Radius, r.Radius, Fill(r.Fill));
                if (r.Stroke is not null) canvas.DrawRoundRect(rect, r.Radius, r.Radius, Stroke(r.Stroke, r.StrokeWidth));
                break;

            case CircleLayer ci:
                if (ci.FillTop is not null && ci.FillBottom is not null)
                {
                    using var shader = SKShader.CreateLinearGradient(
                        new SKPoint(c.X, c.Y - ci.R), new SKPoint(c.X, c.Y + ci.R),
                        [Color(ci.FillTop), Color(ci.FillBottom)], null, SKShaderTileMode.Clamp);
                    using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                    canvas.DrawCircle(c, ci.R, paint);
                }
                else if (ci.Fill is not null)
                {
                    canvas.DrawCircle(c, ci.R, Fill(ci.Fill));
                }
                if (ci.Stroke is not null) canvas.DrawCircle(c, ci.R, Stroke(ci.Stroke, ci.StrokeWidth));
                break;

            case ScrewLayer s:
                DrawScrew(canvas, s);
                break;

            case TicksLayer t:
            {
                var scale = _gauge.Scale(t.Scale);
                if (t.Shape == "triangle")
                {
                    foreach (var v in t.Steps(scale))
                    {
                        var a = scale.AngleFor(v);
                        var r = scale.Reach(v, t.Radius);
                        var half = t.Width / 2 / MathF.Max(1, MathF.Abs(r)) * 180 / MathF.PI; // half the base, as an angle
                        using var marker = Polygon(Polar(c, a - half, r), Polar(c, a + half, r), Polar(c, a, r - t.Length));
                        canvas.DrawPath(marker, Fill(t.Color));
                    }
                    break;
                }
                var paint = Stroke(t.Color, t.Width, round: true);
                foreach (var v in t.Steps(scale))
                {
                    var a = scale.AngleFor(v);
                    var r = scale.Reach(v, t.Radius);
                    canvas.DrawLine(Polar(c, a, r), Polar(c, a, r - t.Length), paint);
                }
                break;
            }

            case LabelsLayer l:
            {
                var scale = _gauge.Scale(l.Scale);
                using var font = TextFont(l.Size);
                var paint = Fill(l.Color);
                foreach (var v in l.Steps(scale))
                {
                    var angle = scale.AngleFor(v);
                    var p = Polar(c, angle, scale.Reach(v, l.Radius));
                    var key = v.ToString("0.##", CultureInfo.InvariantCulture);
                    var text = l.Texts is not null && l.Texts.TryGetValue(key, out var custom)
                        ? custom
                        : (v / l.DivideBy).ToString("0.##", CultureInfo.InvariantCulture);
                    if (l.Orient == "radial")
                    {
                        canvas.Save();
                        canvas.RotateDegrees(angle, p.X, p.Y);
                        canvas.DrawText(text, p.X, p.Y + l.Size * 0.36f, SKTextAlign.Center, font, paint);
                        canvas.Restore();
                    }
                    else
                    {
                        canvas.DrawText(text, p.X, p.Y + l.Size * 0.36f, SKTextAlign.Center, font, paint);
                    }
                }
                break;
            }

            case ArcLayer a:
            {
                var scale = _gauge.Scale(a.Scale);
                var to = a.ToValue is { } live ? ValueOf(live, values) : a.To;
                using var builder = new SKPathBuilder();
                if (scale.IsBent)
                {
                    // Follow the bent scale line in small straight steps.
                    const int steps = 40;
                    for (var i = 0; i <= steps; i++)
                    {
                        var v = a.From + (to - a.From) * i / steps;
                        var p = Polar(c, scale.AngleFor(v), scale.Reach(v, a.Radius));
                        if (i == 0) builder.MoveTo(p); else builder.LineTo(p);
                    }
                }
                else
                {
                    var start = scale.AngleFor(a.From);
                    var sweep = scale.AngleFor(to) - start;
                    // Skia measures from 3 o'clock; gauge files measure from 12 o'clock.
                    builder.AddArc(SKRect.Create(c.X - a.Radius, c.Y - a.Radius, a.Radius * 2, a.Radius * 2), start - 90, sweep);
                }
                using var path = builder.Detach();
                canvas.DrawPath(path, Stroke(a.Color, a.Width));
                break;
            }

            case TextLayer tx:
            {
                using var font = TextFont(tx.Size);
                if (tx.Italic) font.SkewX = -0.22f;
                var align = tx.Align switch
                {
                    "left" => SKTextAlign.Left,
                    "right" => SKTextAlign.Right,
                    _ => SKTextAlign.Center,
                };
                var shown = tx.Value is { } valueName
                    ? ValueOf(valueName, values).ToString(tx.Format, CultureInfo.InvariantCulture)
                    : tx.Text;
                canvas.DrawText(shown, tx.X, tx.Y, align, font, Fill(tx.Color));
                break;
            }

            case GroupLayer g:
            {
                // Placing a whole gauge (combined gauges): move, resize, keep it inside its box.
                if (g.Offset is { Length: 2 } o) canvas.Translate(o[0], o[1]);
                if (g.Zoom > 0 && g.Zoom != 1) canvas.Scale(g.Zoom);
                if (g.ClipBox is { Length: 4 } box) canvas.ClipRect(SKRect.Create(box[0], box[1], box[2], box[3]), SKClipOperation.Intersect, true);
                // The clip stays put (it's the dial window); the contents move behind it.
                if (g.Clip is { } r)
                {
                    using var builder = new SKPathBuilder();
                    builder.AddCircle(c.X, c.Y, r, SKPathDirection.Clockwise);
                    using var clip = builder.Detach();
                    canvas.ClipPath(clip, SKClipOperation.Intersect, true);
                }
                if (g.MoveX is { } mx) canvas.Translate(_gauge.Scale(mx.Scale).AngleFor(ValueOf(mx.Value, values)), 0);
                if (g.MoveY is { } my) canvas.Translate(0, _gauge.Scale(my.Scale).AngleFor(ValueOf(my.Value, values)));
                if (g.Rotate is { } rot) canvas.RotateDegrees(_gauge.Scale(rot.Scale).AngleFor(ValueOf(rot.Value, values)), c.X, c.Y);
                foreach (var child in g.Layers)
                {
                    canvas.Save();
                    DrawLayer(canvas, child, values);
                    canvas.Restore();
                }
                break;
            }

            case NeedleLayer n:
            {
                var scale = _gauge.Scale(n.Scale);
                var v = ValueOf(n.Value, values);
                var length = scale.Reach(v, n.Length);
                if (n.Style == "pointer") DrawPointer(canvas, n, c, scale.AngleFor(v), length);
                else DrawNeedle(canvas, n with { Length = length }, c, scale.AngleFor(v));
                break;
            }

            case HubLayer h:
                canvas.DrawCircle(c, h.R, Fill(h.Fill));
                if (h.Stroke is not null) canvas.DrawCircle(c, h.R, Stroke(h.Stroke, 1.2f));
                canvas.DrawCircle(c.X - h.R * 0.27f, c.Y - h.R * 0.27f, h.R * 0.36f, Fill("#3A3A3A"));
                break;

            case GlareLayer g:
                DrawGlare(canvas, g, c);
                break;
        }
    }

    private void DrawNeedle(SKCanvas canvas, NeedleLayer n, SKPoint c, float angle)
    {
        if (n.Style is "arrow" or "doubleArrow")
        {
            DrawRmiNeedle(canvas, n, c, angle);
            return;
        }

        var w = n.Width;
        using var blade = Polygon(
            new(c.X - w, c.Y),
            new(c.X - w * 0.7f, c.Y - n.Length + 14),
            new(c.X, c.Y - n.Length),
            new(c.X + w * 0.7f, c.Y - n.Length + 14),
            new(c.X + w, c.Y));
        using var tail = n.Tail > 0
            ? Polygon(
                new(c.X - w * 1.3f, c.Y),
                new(c.X - w * 1.5f, c.Y + n.Tail),
                new(c.X + w * 1.5f, c.Y + n.Tail),
                new(c.X + w * 1.3f, c.Y))
            : new SKPath();

        if (n.Shadow)
        {
            // Light comes from the upper left, so the shadow always falls down and right.
            canvas.Save();
            canvas.Translate(2.5f, 3.5f);
            canvas.RotateDegrees(angle, c.X, c.Y);
            using var shadow = new SKPaint { IsAntialias = true, Color = ShadowColor };
            canvas.DrawPath(blade, shadow);
            canvas.DrawPath(tail, shadow);
            canvas.Restore();
        }

        canvas.RotateDegrees(angle, c.X, c.Y);
        canvas.DrawPath(blade, Fill(n.Color));
        canvas.DrawPath(blade, Stroke("#000000", 0.6f));
        canvas.DrawPath(tail, Fill(n.TailColor));
        canvas.DrawPath(tail, Stroke("#4A4A4A", 0.8f));
    }

    /// <summary>
    /// Engine-dial pointer: an outlined blade from the pivot to the tip and a dark counterweight
    /// arm behind the pivot (with a weight on the end for the wider needles). Optional engine
    /// number in a small disc on the blade.
    /// </summary>
    private void DrawPointer(SKCanvas canvas, NeedleLayer n, SKPoint c, float angle, float reach)
    {
        var w = n.Width;
        var thin = w < 2.5f;
        var tip = thin ? 6 : 12;
        canvas.Save();
        canvas.RotateDegrees(angle, c.X, c.Y);
        if (n.Tail > 0)
        {
            var aw = thin ? 1.6f : 2.2f;
            using var arm = Polygon(new(c.X - aw, c.Y), new(c.X - aw, c.Y + n.Tail), new(c.X + aw, c.Y + n.Tail), new(c.X + aw, c.Y));
            canvas.DrawPath(arm, Fill(n.TailColor));
            canvas.DrawPath(arm, Stroke("#555555", 0.6f));
            if (!thin)
            {
                var weight = SKRect.Create(c.X - 6, c.Y + n.Tail - 4, 12, 10);
                canvas.DrawRect(weight, Fill(n.TailColor));
                canvas.DrawRect(weight, Stroke("#555555", 0.6f));
            }
        }
        using var blade = Polygon(
            new(c.X - w, c.Y + 2),
            new(c.X - w * 0.8f, c.Y - reach + tip),
            new(c.X, c.Y - reach),
            new(c.X + w * 0.8f, c.Y - reach + tip),
            new(c.X + w, c.Y + 2));
        canvas.DrawPath(blade, Fill(n.Color));
        canvas.DrawPath(blade, Stroke("#000000", 0.8f));
        canvas.Restore();

        if (n.Tag is { Length: > 0 } tag)
        {
            // Upright, wherever the needle points.
            var p = Polar(c, angle, reach - 30);
            canvas.DrawCircle(p, 7, Fill(n.Color));
            canvas.DrawCircle(p, 7, Stroke("#000000", 0.8f));
            using var font = TextFont(10);
            canvas.DrawText(tag, p.X, p.Y + 3.6f, SKTextAlign.Center, font, Fill("#111111"));
        }
    }

    /// <summary>RMI-style pointer: arrowhead at the tip, line(s) through the center, forked tail.</summary>
    private void DrawRmiNeedle(SKCanvas canvas, NeedleLayer n, SKPoint c, float angle)
    {
        var w = n.Width;                       // line thickness
        var gap = n.Style == "doubleArrow" ? w * 2.2f : 0;
        var head = MathF.Max(10, w * 5);
        var tip = c.Y - n.Length;
        var tail = c.Y + MathF.Max(n.Tail, 1);

        void Draw(SKPaint stroke, SKPaint fill)
        {
            if (gap > 0)
            {
                canvas.DrawLine(c.X - gap, tip + head * 0.8f, c.X - gap, tail, stroke);
                canvas.DrawLine(c.X + gap, tip + head * 0.8f, c.X + gap, tail, stroke);
            }
            else
            {
                canvas.DrawLine(c.X, tip + head * 0.8f, c.X, tail, stroke);
            }
            using var arrow = Polygon(new(c.X, tip), new(c.X - head * 0.45f - gap, tip + head), new(c.X + head * 0.45f + gap, tip + head));
            canvas.DrawPath(arrow, fill);
            // Forked tail: two short lines spreading out at the back end.
            canvas.DrawLine(c.X - gap, tail, c.X - gap - head * 0.35f, tail + head * 0.6f, stroke);
            canvas.DrawLine(c.X + gap, tail, c.X + gap + head * 0.35f, tail + head * 0.6f, stroke);
        }

        if (n.Shadow)
        {
            canvas.Save();
            canvas.Translate(2f, 3f);
            canvas.RotateDegrees(angle, c.X, c.Y);
            using var shadowStroke = new SKPaint { IsAntialias = true, Color = ShadowColor, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeCap = SKStrokeCap.Round };
            using var shadowFill = new SKPaint { IsAntialias = true, Color = ShadowColor };
            Draw(shadowStroke, shadowFill);
            canvas.Restore();
        }
        canvas.Save();
        canvas.RotateDegrees(angle, c.X, c.Y);
        Draw(Stroke(n.Color, w, round: true), Fill(n.Color));
        canvas.Restore();
    }

    private static void DrawGlare(SKCanvas canvas, GlareLayer g, SKPoint c)
    {
        using var builder = new SKPathBuilder();
        builder.AddCircle(c.X, c.Y, g.R, SKPathDirection.Clockwise);
        using var clip = builder.Detach();
        canvas.ClipPath(clip, SKClipOperation.Intersect, true);

        var ex = c.X - g.R * 0.3f;
        var ey = c.Y - g.R * 0.55f;
        var alpha = (byte)Math.Clamp(g.Opacity * 255, 0, 255);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(ex, ey - g.R * 0.5f), new SKPoint(ex, ey + g.R * 0.5f),
            [new SKColor(255, 255, 255, alpha), new SKColor(255, 255, 255, 0)], null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.Save();
        canvas.RotateDegrees(-24, ex, ey);
        canvas.DrawOval(ex, ey, g.R, g.R * 0.5f, paint);
        canvas.Restore();
    }

    private void DrawScrew(SKCanvas canvas, ScrewLayer s)
    {
        switch (s.Kind)
        {
            case "boss":
                var rect = SKRect.Create(s.X - 11, s.Y - 11, 22, 22);
                canvas.DrawRoundRect(rect, 7, 7, Fill("#2A2E30"));
                canvas.DrawRoundRect(rect, 7, 7, Stroke("#868C90", 0.8f));
                break;
            case "steel":
            {
                using var shader = SKShader.CreateRadialGradient(new SKPoint(s.X - 1.2f, s.Y - 1.8f), 8.4f,
                    [Color("#D4D6D5"), Color("#6D706F")], null, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                canvas.DrawCircle(s.X, s.Y, 6, paint);
                canvas.DrawCircle(s.X, s.Y, 6, Stroke("#4A4C4B", 1));
                canvas.DrawLine(s.X - 4, s.Y - 2, s.X + 4, s.Y + 2, Stroke("#3A3C3B", 1.6f));
                break;
            }
            case "calibration":
                canvas.DrawCircle(s.X, s.Y, 4, Fill("#171717"));
                canvas.DrawCircle(s.X, s.Y, 4, Stroke("#333333", 1));
                canvas.DrawLine(s.X - 2.5f, s.Y, s.X + 2.5f, s.Y, Stroke("#3A3A3A", 1));
                canvas.DrawLine(s.X, s.Y - 2.5f, s.X, s.Y + 2.5f, Stroke("#3A3A3A", 1));
                break;
            default:
                canvas.DrawCircle(s.X, s.Y, 6, Fill("#2C3133"));
                canvas.DrawCircle(s.X, s.Y, 6, Stroke("#8A9094", 0.8f));
                canvas.DrawLine(s.X - 4, s.Y - 1.5f, s.X + 4, s.Y + 1.5f, Stroke("#555B5E", 1.4f));
                break;
        }
    }

    private static SKPath Polygon(params SKPoint[] points)
    {
        using var builder = new SKPathBuilder();
        builder.AddPoly(points, true);
        return builder.Detach();
    }

    private SKPoint CenterOf(Layer layer)
    {
        var c = layer.Center ?? _gauge.Center;
        return new SKPoint(c[0], c[1]);
    }

    /// <summary>Point at an angle (clockwise from 12 o'clock) and distance from a center.</summary>
    public static SKPoint Polar(SKPoint center, float angleDegrees, float radius)
    {
        var t = angleDegrees * MathF.PI / 180f;
        return new SKPoint(center.X + radius * MathF.Sin(t), center.Y - radius * MathF.Cos(t));
    }

    private static SKColor Color(string hex) => SKColor.Parse(hex);

    // Paints are reused across frames; the renderer redraws many times a second.
    private readonly Dictionary<(string Hex, float Width, bool Round), SKPaint> _paints = [];

    private SKPaint Fill(string hex) => Paint(hex, 0, false);

    /// <summary>
    /// Font for labels and text. Sub-pixel positioning keeps small words evenly spaced (without
    /// it each letter snaps to a whole pixel and "AIRSPEED" reads "AIRS PEED").
    /// </summary>
    private SKFont TextFont(float size) => new(_typeface, size)
    {
        Subpixel = true,
        Edging = SKFontEdging.Antialias,
        Hinting = SKFontHinting.Slight,
    };

    private SKPaint Stroke(string hex, float width, bool round = false) => Paint(hex, width, round);

    private SKPaint Paint(string hex, float strokeWidth, bool round)
    {
        var key = (hex, strokeWidth, round);
        if (!_paints.TryGetValue(key, out var paint))
        {
            paint = new SKPaint
            {
                IsAntialias = true,
                Color = Color(hex),
                Style = strokeWidth > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
                StrokeWidth = strokeWidth,
                StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt,
            };
            _paints[key] = paint;
        }
        return paint;
    }

    public void Dispose()
    {
        foreach (var paint in _paints.Values) paint.Dispose();
        _paints.Clear();
        if (_typeface != SKTypeface.Default) _typeface.Dispose();
    }
}

// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fipinator.Core;
using SkiaSharp;

namespace Fipinator.Core.Tests;

/// <summary>
/// A stand-in for SPAD.neXt in tests: draws an exported gauge from its XML and pictures, working out
/// needles and digits with a small reader for the FSX-style RPN formulas the export writes. It only knows
/// what the export uses (numbers, sim values, + - * / abs flr min max &lt; &gt; and if{ } els{ }), so a formula the
/// export gets wrong fails here.
/// </summary>
internal static partial class SpadPlayer
{
    private static float F(XElement e, string a) => float.Parse(e.Attribute(a)!.Value, CultureInfo.InvariantCulture);

    public static SKBitmap Draw(string xmlFile, string art, IReadOnlyDictionary<string, double> sim)
    {
        var xml = XDocument.Load(xmlFile).Root!;
        var bitmap = new SKBitmap(320, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        var smooth = new SKSamplingOptions(SKFilterMode.Linear);
        void Put(string name, float x, float y)
        {
            using var picture = SKBitmap.Decode(Path.Combine(art, name));
            using var image = SKImage.FromBitmap(picture);
            canvas.DrawImage(image, x, y, smooth);
        }

        Put(xml.Element("Image")!.Attribute("Name")!.Value, 0, 0);
        foreach (var element in xml.Elements("Element"))
        {
            var pos = element.Element("Position")!;
            var image = element.Element("Image")!;
            if (element.Element("Visible") is { } visible && Evaluate(visible.Value, sim) == 0) continue;
            if (element.Element("Shift") is { } slideRule && element.Element("Rotate") is { } turnRule && turnRule.Element("Nonlinearity") is null)
            {
                // A horizon (FSX style, like SPAD's bundled attitude gauge): slid by value x Scale pixels in its own
                // level frame, then turned by the Rotate value in radians around the element's position. A positive
                // value turns clockwise on the FIP (seen on a real FIP, 2026-11-11).
                var value = slideRule.Element("Value")!;
                var v = Math.Clamp(Evaluate(value.Value, sim), F(value, "Minimum"), F(value, "Maximum"));
                var scaleRule = slideRule.Element("Scale")!;
                var dx = scaleRule.Attribute("X") is { } sx ? (float)v * float.Parse(sx.Value, CultureInfo.InvariantCulture) : 0;
                // SPAD.neXt slides a positive Y upward (seen on a real FIP, 2026-11-11).
                var dy = scaleRule.Attribute("Y") is { } sy ? -(float)v * float.Parse(sy.Value, CultureInfo.InvariantCulture) : 0;
                var radians = Evaluate(turnRule.Element("Value")!.Value, sim);
                var axis = image.Element("Axis")!;
                canvas.Save();
                canvas.Translate(F(pos, "X"), F(pos, "Y"));
                canvas.RotateDegrees((float)(radians * 180 / Math.PI));
                canvas.Translate(dx, dy);
                Put(image.Attribute("Name")!.Value, -F(axis, "X"), -F(axis, "Y"));
                canvas.Restore();
            }
            else if (element.Element("Shift") is { } shiftRule)
            {
                // FSX style: the picture moves by value x Scale pixels; a mask picture (white where it may show)
                // stays put, both lined up on the element's position by their axes.
                var value = shiftRule.Element("Value")!;
                var v = Math.Clamp(Evaluate(value.Value, sim), F(value, "Minimum"), F(value, "Maximum"));
                var scaleRule = shiftRule.Element("Scale")!;
                var dx = scaleRule.Attribute("X") is { } sx ? (float)v * float.Parse(sx.Value, CultureInfo.InvariantCulture) : 0;
                var dy = scaleRule.Attribute("Y") is { } sy ? (float)v * float.Parse(sy.Value, CultureInfo.InvariantCulture) : 0;
                var axis = image.Element("Axis")!;
                using var layer = new SKBitmap(320, 240);
                using (var layerCanvas = new SKCanvas(layer))
                {
                    layerCanvas.Clear(SKColors.Transparent);
                    using (var picture = SKBitmap.Decode(Path.Combine(art, image.Attribute("Name")!.Value)))
                    using (var pictureImage = SKImage.FromBitmap(picture))
                        layerCanvas.DrawImage(pictureImage, F(pos, "X") - F(axis, "X") + dx, F(pos, "Y") - F(axis, "Y") + dy, smooth);
                    if (element.Element("MaskImage") is { } maskRule)
                    {
                        var maskAxis = maskRule.Element("Axis")!;
                        using var mask = SKBitmap.Decode(Path.Combine(art, maskRule.Attribute("Name")!.Value));
                        using var maskImage = SKImage.FromBitmap(mask);
                        // Keep the part only where the mask is; nothing outside the mask's box.
                        using var keep = new SKBitmap(320, 240);
                        using (var keepCanvas = new SKCanvas(keep))
                        {
                            keepCanvas.Clear(SKColors.Transparent);
                            keepCanvas.DrawImage(maskImage, F(pos, "X") - F(maskAxis, "X"), F(pos, "Y") - F(maskAxis, "Y"), new SKSamplingOptions(SKFilterMode.Nearest));
                        }
                        using var keepImage = SKImage.FromBitmap(keep);
                        using var cutPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
                        layerCanvas.DrawImage(keepImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), cutPaint);
                    }
                }
                using var layerImage = SKImage.FromBitmap(layer);
                canvas.DrawImage(layerImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            else if (element.Element("Rotate") is { } rotate)
            {
                var value = rotate.Element("Value")!;
                var v = Math.Clamp(Evaluate(value.Value, sim), F(value, "Minimum"), F(value, "Maximum"));
                var items = rotate.Element("Nonlinearity")!.Elements("Item").Select(i => (V: F(i, "Value"), X: F(i, "X"), Y: F(i, "Y"))).ToList();
                var at = items.Zip(items.Skip(1)).First(p => v >= p.First.V && v <= p.Second.V);
                var t = (float)((v - at.First.V) / Math.Max(1e-9, at.Second.V - at.First.V));
                var (tx, ty) = (at.First.X + (at.Second.X - at.First.X) * t, at.First.Y + (at.Second.Y - at.First.Y) * t);
                var axis = image.Element("Axis")!;
                canvas.Save();
                canvas.Translate(F(pos, "X"), F(pos, "Y"));
                canvas.RotateDegrees((float)(Math.Atan2(ty - F(pos, "Y"), tx - F(pos, "X")) * 180 / Math.PI));
                Put(image.Attribute("Name")!.Value, -F(axis, "X"), -F(axis, "Y"));
                canvas.Restore();
            }
            else if (image.Attribute("Dynamic") is { } dynamic)
            {
                var value = image.Element("Value")!;
                var n = (int)Math.Clamp(Evaluate(value.Value, sim), F(value, "Minimum"), F(value, "Maximum"));
                Put(dynamic.Value.Replace("{VALUE}", n.ToString(CultureInfo.InvariantCulture)), F(pos, "X"), F(pos, "Y"));
            }
            else Put(image.Attribute("Name")!.Value, F(pos, "X"), F(pos, "Y"));
        }
        return bitmap;
    }

    /// <summary>The text the export's digit formulas show for this value in this format.</summary>
    public static string Digits(double value, string format)
    {
        var parent = Directory.CreateTempSubdirectory("fipinator-digits").FullName;
        try
        {
            var gauge = new JsonObject
            {
                ["name"] = "Digits",
                ["canvas"] = new JsonObject { ["width"] = 320, ["height"] = 240, ["background"] = "#000000" },
                ["values"] = new JsonObject { ["v"] = new JsonObject { ["sim"] = "TEST VALUE", ["unit"] = "number", ["default"] = value } },
                ["layers"] = new JsonArray(new JsonObject { ["type"] = "text", ["value"] = "v", ["format"] = format, ["x"] = 160, ["y"] = 120, ["size"] = 30 }),
            };
            var file = Path.Combine(parent, "digits.json");
            File.WriteAllText(file, gauge.ToJsonString());
            var result = SpadExport.Export(file, parent);
            using var zip = ZipFile.OpenRead(result.ZipFile);
            using var stream = zip.GetEntry(result.XmlName)!.Open();
            var xml = XDocument.Load(stream).Root!;
            var sim = new Dictionary<string, double> { ["(A:TEST VALUE, number)"] = value };
            var shown = "";
            foreach (var element in xml.Elements("Element").Where(e => e.Attribute("id")!.Value.StartsWith("number1_", StringComparison.Ordinal)))
            {
                var id = element.Attribute("id")!.Value;
                var style = SpadExport.NumberStyleOf(format)!;
                if (id.EndsWith("_point", StringComparison.Ordinal)) { shown += "."; continue; }
                if (id.EndsWith("_back", StringComparison.Ordinal)) { shown += style.Suffix; continue; }
                var n = (int)Evaluate(element.Element("Image")!.Element("Value")!.Value, sim);
                string[] signs = [style.Plus, style.Minus, style.Zero, ""];
                if (id.EndsWith("_front", StringComparison.Ordinal)) { shown += signs[n]; continue; }
                if (n >= 12) { shown += signs[n - 12]; continue; } // the sign sitting in a digit slot
                if (element.Attribute("id")!.Value.Contains("_comma", StringComparison.Ordinal)) { shown += n == 1 ? "," : ""; continue; }
                shown += n switch { 10 => "", 11 => "-", _ => n.ToString(CultureInfo.InvariantCulture) };
            }
            return shown;
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [GeneratedRegex(@"\([AL]:[^)]*\)|if\{|els\{|\}|\S+")]
    private static partial Regex Token();

    public static double Evaluate(string rpn, IReadOnlyDictionary<string, double> sim)
    {
        var tokens = Token().Matches(rpn).Select(m => m.Value).ToList();
        var stack = new Stack<double>();
        // The index of the "}" that closes the block opened just before "from".
        int Close(int from)
        {
            var depth = 0;
            for (var j = from; j < tokens.Count; j++)
            {
                if (tokens[j] is "if{" or "els{") depth++;
                else if (tokens[j] == "}") { if (depth == 0) return j; depth--; }
            }
            return tokens.Count;
        }
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t)
            {
                case "+": { var b = stack.Pop(); stack.Push(stack.Pop() + b); break; }
                case "-": { var b = stack.Pop(); stack.Push(stack.Pop() - b); break; }
                case "*": { var b = stack.Pop(); stack.Push(stack.Pop() * b); break; }
                case "/": { var b = stack.Pop(); stack.Push(stack.Pop() / b); break; }
                case "<": { var b = stack.Pop(); stack.Push(stack.Pop() < b ? 1 : 0); break; }
                case ">": { var b = stack.Pop(); stack.Push(stack.Pop() > b ? 1 : 0); break; }
                case "min": { var b = stack.Pop(); stack.Push(Math.Min(stack.Pop(), b)); break; }
                case "max": { var b = stack.Pop(); stack.Push(Math.Max(stack.Pop(), b)); break; }
                case "abs": stack.Push(Math.Abs(stack.Pop())); break;
                case "flr": stack.Push(Math.Floor(stack.Pop())); break;
                // SPAD.neXt ignores "near" (its log asked for "slide1_37.77.png", 2026-11-11): the export must not use it.
                case "near": throw new InvalidOperationException("SPAD.neXt doesn't round with 'near': use '0.5 + flr'");
                case "if{":
                    if (stack.Pop() == 0)
                    {
                        // False: jump past this block; an "els{" right after it runs instead.
                        i = Close(i + 1);
                        if (i + 1 < tokens.Count && tokens[i + 1] == "els{") i++;
                    }
                    break;
                // Reached after a true "if{" block: skip the "els{" block.
                case "els{": i = Close(i + 1); break;
                case "}": break;
                default:
                    if (t.StartsWith('(')) stack.Push(sim[t]);
                    else stack.Push(double.Parse(t, CultureInfo.InvariantCulture));
                    break;
            }
        }
        return stack.Pop();
    }
}

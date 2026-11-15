// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Fipinator.Core;
using SkiaSharp;
using Xunit;

namespace Fipinator.Core.Tests;

public class SpadExportTests
{
    private static string Gauges => Path.Combine(AppContext.BaseDirectory, "repo", "gauges");
    private static string Airspeed => Path.Combine(Gauges, "classic", "single", "airspeed.json");

    /// <summary>Exports the airspeed and unpacks the .zip, the way SPAD.neXt would read it.</summary>
    private static (SpadExport.Result Result, string Folder, string XmlFile) ExportAndUnpack(string parent)
    {
        var result = SpadExport.Export(Airspeed, parent);
        var folder = Path.Combine(parent, "unpacked");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        ZipFile.ExtractToDirectory(result.ZipFile, folder);
        return (result, folder, Path.Combine(folder, result.XmlName));
    }

    [Fact]
    public void Classic_airspeed_becomes_one_zip_with_xml_and_pictures()
    {
        var parent = Directory.CreateTempSubdirectory("fipinator-spad").FullName;
        try
        {
            var (result, folder, xmlFile) = ExportAndUnpack(parent);
            Assert.EndsWith(".zip", result.ZipFile);
            Assert.Equal(["FIPinator - Classic single Airspeed.zip"], Directory.GetFiles(parent).Select(Path.GetFileName));
            foreach (var file in new[] { "background.png", "needle1.png", "cover1.png", "preview.png" })
                Assert.True(File.Exists(Path.Combine(folder, SpadExport.ArtFolder, file)), file); // where SPAD looks
            Assert.True(File.Exists(Path.Combine(folder, SpadExport.Marker)));

            var xml = XDocument.Load(xmlFile).Root!;
            Assert.Equal("Gauge", xml.Name.LocalName);
            Assert.Equal("background.png", xml.Element("Image")!.Attribute("Name")!.Value);
            var needle = xml.Elements("Element").Single(e => e.Attribute("id")!.Value == "needle1");
            Assert.Equal("(A:AIRSPEED INDICATED, knots)", needle.Element("Rotate")!.Element("Value")!.Value);
            Assert.Equal("East", needle.Element("Image")!.Attribute("PointsTo")!.Value);
            Assert.True(needle.Element("Rotate")!.Element("Nonlinearity")!.Elements("Item").Count() > 10);
            Assert.False(File.ReadAllBytes(xmlFile).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })); // no BOM

            // Saving again replaces our own export.
            SpadExport.Export(Airspeed, parent);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    /// <summary>
    /// Plays SPAD.neXt's part: reads the exported gauge file, works out every needle and digit from the sim
    /// values with its own small RPN reader, draws the pictures, and compares with our own drawing.
    /// </summary>
    [Theory]
    [InlineData("classic/single/airspeed.json", 0.1)]
    [InlineData("classic/single/airspeed.json", 0.6)]
    [InlineData("classic/single/airspeed.json", 0.93)]
    [InlineData("classic/single/altimeter.json", 0.37)]   // hands that go round and round + baro window
    [InlineData("classic/single/altimeter.json", 0.81)]
    [InlineData("jet/737-max-round/n1.json", 0.55)]             // number displays
    [InlineData("jet/737-max-round/n1.json", 0.97)]
    [InlineData("jet/727/epr.json", 0.42)]
    [InlineData("dc6/engines/rpm-1-4.json", 0.6)]          // bent scales, four needles on one dial
    [InlineData("dc6/engines/oil-temperature-1-4.json", 0.3)] // two dials side by side
    [InlineData("dc6/mini/engines-mini.json", 0.5, 5.0)]  // worked-out numbers: 16 numbers with evenly spaced digits in SPAD
    [InlineData("jet/727/engines-mini.json", 0.6, 5.0)]   // growing arcs + worked-out numbers
    [InlineData("jet/737-max/altimeter.json", 0.45, 5.0)] // thousands comma ("12,000")
    [InlineData("classic/single/heading.json", 0.3)]       // turning compass card + heading bug on the card
    [InlineData("classic/single/heading.json", 0.77)]
    [InlineData("classic/nav/rmi-vor-adf.json", 0.4)]      // card + bug, needles on top
    [InlineData("dc6/rmi-vor12-adf12.json", 0.65)]         // four needles
    [InlineData("classic/twin/adf.json", 0.2)]             // ADF card turned by its knob
    [InlineData("classic/single/turn-coordinator.json", 0.8)] // turning airplane
    [InlineData("classic/nav/course-ils.json", 0.15)]      // sliding ILS bars cut to the dial window, flags
    [InlineData("classic/nav/course-ils.json", 0.62)]
    [InlineData("dc6/course-ils.json", 0.4)]
    [InlineData("jet/727/course-ils.json", 0.85)]
    [InlineData("templates/weather-small/wind.json", 0.4, 5.0)]     // number styles: "000°", "0 KT", "0 °C;-0 °C"
    [InlineData("templates/airbus-style/fcu.json", 0.6, 5.0)]       // "+0000;-0000;0000"
    [InlineData("templates/autopilot-radios/autopilot.json", 0.3, 6.0)] // "+#,0;-#,0;0"; the blue knob-mode box is Live only
    [InlineData("classic/single/radar-altimeter.json", 0.3)] // turning DH bug on an uneven scale
    [InlineData("classic/twin/radar-altimeter.json", 0.8)]
    [InlineData("classic/single/hsi.json", 0.3)]           // HSI: card, bug and course on the card, CDI bar slides and turns
    [InlineData("classic/twin/hsi.json", 0.75)]
    [InlineData("jet/737-max/eadi.json", 0.81)]             // glass horizon in a box window, steep pitch and bank
    [InlineData("turboprop/saab-340b/eadi.json", 0.3)]
    [InlineData("jet/727/adi.json", 0.62)]                  // ADI: flight director, localizer and glideslope pointers that hide
    [InlineData("jet/737-max/adi.json", 0.15)]
    [InlineData("jet/737-max/hsi.json", 0.55)]
    [InlineData("dc6/hsi.json", 0.15)]
    [InlineData("dc6/heading.json", 0.35)]                 // the DC-6's own directional gyro
    [InlineData("dc6/turn-and-slip.json", 0.6)]
    [InlineData("dc6/clock.json", 0.2)]
    [InlineData("dc6/attitude.json", 0.3)]                 // horizon: slides (pitch) and turns (bank), window covers
    [InlineData("dc6/attitude.json", 0.71)]
    [InlineData("classic/single/attitude.json", 0.45)]     // turning bank ring with the horizon inside it
    [InlineData("classic/single/attitude.json", 0.88)]
    public void The_pictures_put_back_together_match_the_gauge(string file, double fraction, double allowed = 1.5)
    {
        var path = Path.Combine(Gauges, file.Replace('/', Path.DirectorySeparatorChar));
        var parent = Directory.CreateTempSubdirectory("fipinator-spad").FullName;
        try
        {
            var gauge = GaugeDefinition.Load(path);
            // Every sim value somewhere inside its range (each a little different), for both drawings.
            var ours = new Dictionary<string, double>();
            var sim = new Dictionary<string, double>();
            var i = 0;
            foreach (var range in GaugeValueRanges.For(gauge))
            {
                if (SpadExport.SimValue(gauge, range.Name) is not { } simName) continue;
                var f = (fraction + 0.071 * i++) % 1;
                var v = Math.Round(range.Min + (range.Max - range.Min) * f, 3);
                ours[range.Name] = v;
                sim[simName] = v;
            }

            var result = SpadExport.Export(path, parent);
            var folder = Path.Combine(parent, "unpacked");
            ZipFile.ExtractToDirectory(result.ZipFile, folder);
            using var built = SpadPlayer.Draw(Path.Combine(folder, result.XmlName), Path.Combine(folder, SpadExport.ArtFolder), sim);
            using var renderer = GaugeRenderer.FromFile(path);
            using var expected = renderer.Render(ours);

            // Compared in 4 x 4 pixel blocks: a digit a pixel off doesn't count, a wrong digit or needle does.
            double diff = 0;
            for (var by = 0; by < 60; by++)
                for (var bx = 0; bx < 80; bx++)
                {
                    double r = 0, g = 0, b = 0;
                    for (var y = by * 4; y < by * 4 + 4; y++)
                        for (var x = bx * 4; x < bx * 4 + 4; x++)
                        {
                            var (p, q) = (built.GetPixel(x, y), expected.GetPixel(x, y));
                            r += p.Red - q.Red; g += p.Green - q.Green; b += p.Blue - q.Blue;
                        }
                    diff += (Math.Abs(r) + Math.Abs(g) + Math.Abs(b)) / 16;
                }
            var average = diff / (80 * 60 * 3);
            if (average >= allowed)
            {
                // Keep both pictures for a look: %TEMP%ipinator-spad-check\.
                var look = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "fipinator-spad-check")).FullName;
                var stem = $"{file.Replace('/', '-')}-{fraction:0.00}";
                foreach (var (bitmap, name) in new[] { (built, "spad"), (expected, "ours") })
                {
                    using var image = SKImage.FromBitmap(bitmap);
                    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(look, $"{stem}-{name}.png"), png.ToArray());
                }
            }
            // Number displays: SPAD shows one picture per digit, side by side, so digits are evenly spaced
            // (Live spaces them like normal text); a gauge full of numbers gets a little more room for that.
            Assert.True(average < allowed, $"{file}: average difference {average:0.00}");
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    /// <summary>Every gauge that goes to SPAD.neXt (not just the hand-picked ones above).</summary>
    public static IEnumerable<object[]> EverySpadGauge() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "repo", "gauges"), "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Replace(Path.DirectorySeparatorChar, '/').Contains("/parts/"))
            .Where(f => SpadExport.Check(GaugeDefinition.Load(f)).Count == 0)
            .Select(f => new object[] { Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "repo", "gauges"), f).Replace(Path.DirectorySeparatorChar, '/') })
            .OrderBy(x => (string)x[0]);

    [Theory]
    [MemberData(nameof(EverySpadGauge))]
    public void Every_spad_gauge_redraws_like_live(string file) =>
        // A looser limit than the hand-picked cases: some gauges are full of numbers (SPAD spaces digits evenly), and a
        // gauge with knob modes shows a box around the chosen mode only in FIPinator Live.
        The_pictures_put_back_together_match_the_gauge(file, 0.37,
            GaugeDefinition.Load(Path.Combine(Gauges, file.Replace('/', Path.DirectorySeparatorChar))).Modes.Count > 0 ? 10.0 : 6.0);

    [Theory]
    [InlineData(1234.5, "0", "1235")]
    [InlineData(7.25, "0.0", "7.3")]
    [InlineData(5, "000", "005")]
    [InlineData(-12.4, "0", "-12")]
    [InlineData(12345, "#,0", "12,345")]       // thousands comma
    [InlineData(950, "#,0", "950")]            // no comma while the number is small
    [InlineData(1234567.8, "#,##0.0", "1,234,567.8")]
    [InlineData(0.5, "0%", "50%")]                       // % shows the value times 100
    [InlineData(240, "000°", "240°")]
    [InlineData(18, "0 KT", "18 KT")]
    [InlineData(-12, "0 °C;-0 °C", "-12 °C")]
    [InlineData(250, "+0000;-0000;0000", "+0250")]
    [InlineData(-800, "+0000;-0000;0000", "-0800")]
    [InlineData(0, "+0000;-0000;0000", "0000")]
    [InlineData(12000, "+#,0;-#,0;0", "+12,000")]
    [InlineData(600, "+#,0;-#,0;0", "+600")]            // sign right next to the digits
    [InlineData(-1500, "+#,0;-#,0;0", "-1,500")]
    [InlineData(0, "+#,0;-#,0;0", "0")]
    [InlineData(0.78, ".00", ".78")]                     // Mach
    public void Number_formulas_give_the_right_digits(double value, string format, string shown)
    {
        // The digits the export's formulas pick, read back with the same RPN reader as above.
        var digits = SpadPlayer.Digits(value, format);
        Assert.Equal(shown, digits);
        Assert.Equal(value.ToString(format, System.Globalization.CultureInfo.InvariantCulture), digits); // what Live shows
    }

    [Fact]
    public void Every_gauge_but_the_737_standby_can_go_to_spad()
    {
        // Only the 737 MAX standby (rolling tapes) and the PMDG-data autobrake gauge (Live only) may be left out; a new gauge the export can't do shows up here.
        var cannot = Directory.GetFiles(Gauges, "*.json", SearchOption.AllDirectories)
            .Where(f => SpadExport.Check(GaugeDefinition.Load(f)).Count > 0)
            .Select(f => Path.GetRelativePath(Gauges, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();
        Assert.Equal(["jet/737-800/autobrake-flaps.json", "jet/737-max/standby.json"], cannot); // PMDG data: Live only
    }

    [Fact]
    public void Gauges_it_cannot_do_yet_say_why()
    {
        var gauge = GaugeDefinition.Load(Path.Combine(Gauges, "jet", "737-max", "standby.json"));
        Assert.NotEmpty(SpadExport.Check(gauge));
        Assert.True(SpadExport.Status(GaugeDefinition.Load(Path.Combine(Gauges, "classic", "single", "attitude.json"))).Ready); // horizon: fine
        Assert.False(SpadExport.Status(gauge).Ready);
        Assert.Contains("not yet", SpadExport.Status(gauge).Text);
        Assert.True(SpadExport.Status(GaugeDefinition.Load(Airspeed)).Ready);
        Assert.True(SpadExport.Status(GaugeDefinition.Load(Path.Combine(Gauges, "classic", "single", "heading.json"))).Ready); // turning card: fine
    }

    [Theory]
    [InlineData("classic/single/altimeter.json", "KOHLSMAN_INC", "KOHLSMAN_DEC")]
    [InlineData("dc6/altimeter.json", "KOHLSMAN_INC", "KOHLSMAN_DEC")]
    [InlineData("jet/737-max/altimeter.json", "KOHLSMAN_INC", "KOHLSMAN_DEC")]
    [InlineData("templates/boeing-style/flaps.json", "FLAPS_INCR", "FLAPS_DECR")]
    public void Knobs_come_built_into_the_spad_gauge(string file, string up, string down)
    {
        var parent = Directory.CreateTempSubdirectory("fipinator-spad").FullName;
        try
        {
            var result = SpadExport.Export(Path.Combine(Gauges, file.Replace('/', Path.DirectorySeparatorChar)), parent);
            using var zip = ZipFile.OpenRead(result.ZipFile);
            XElement xml;
            using (var stream = zip.GetEntry(result.XmlName)!.Open()) xml = XDocument.Load(stream).Root!;
            // One Mouse block per knob (right, then left), as in SPAD's gauge.xsd: DownArrow = anticlockwise.
            var mice = xml.Elements("Mouse").ToList();
            Assert.Equal(2, mice.Count);
            foreach (var mouse in mice)
            {
                var turns = mouse.Element("Area")!.Elements("Area")
                    .ToDictionary(a => a.Element("Cursor")!.Attribute("Type")!.Value, a => a.Element("Click")!.Attribute("Event")!.Value);
                Assert.Equal(down, turns["DownArrow"]);
                Assert.Equal(up, turns["UpArrow"]);
            }
            Assert.DoesNotContain(xml.Descendants("Button"), b => b.Attribute("BoundTo")!.Value.Contains("DIAL", StringComparison.Ordinal));
            // The blocks sit before the settings, like the Elements (SPAD reads the gauge top to bottom).
            Assert.True(xml.Elements().ToList().IndexOf(mice[0]) < xml.Elements().ToList().IndexOf(xml.Element("GaugeSettings")!));
            using var readme = new StreamReader(zip.GetEntry(SpadExport.Marker)!.Open());
            Assert.DoesNotContain("set these up in SPAD.neXt", readme.ReadToEnd());
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void A_zip_it_did_not_make_is_never_replaced()
    {
        var parent = Directory.CreateTempSubdirectory("fipinator-spad").FullName;
        try
        {
            var zip = Path.Combine(parent, "FIPinator - " + GaugeDefinition.Load(Airspeed).Name + ".zip");
            File.WriteAllText(zip, "someone else's file");
            Assert.Throws<IOException>(() => SpadExport.Export(Airspeed, parent));
            Assert.Equal("someone else's file", File.ReadAllText(zip));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void Ready_made_set_removes_our_copies_of_gauges_that_are_gone_and_nothing_else()
    {
        var root = Directory.CreateTempSubdirectory("fipinator-tidy").FullName;
        try
        {
            var gauges = Directory.CreateDirectory(Path.Combine(root, "gauges", "classic", "single")).FullName;
            File.Copy(Airspeed, Path.Combine(gauges, "airspeed.json"));
            var target = Path.Combine(root, "ready");
            // An old copy of ours (a gauge no longer in the set), and someone else's zip.
            var gone = Directory.CreateDirectory(Path.Combine(target, "Classic four-engine")).FullName;
            var ours = Path.Combine(gone, "FIPinator - Classic four-engine Airspeed.zip");
            using (var zip = ZipFile.Open(ours, ZipArchiveMode.Create)) zip.CreateEntry(SpadExport.Marker);
            var other = Directory.CreateDirectory(Path.Combine(target, "Someone else")).FullName;
            var theirs = Path.Combine(other, "Their gauge.zip");
            using (var zip = ZipFile.Open(theirs, ZipArchiveMode.Create)) zip.CreateEntry("their.xml");

            // The font is found next to the copied gauge's original folder: point it at the repo's font.
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(gauges, "airspeed.json")))!.AsObject();
            json["font"] = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Airspeed)!, json["font"]!.GetValue<string>()));
            File.WriteAllText(Path.Combine(gauges, "airspeed.json"), json.ToJsonString());

            Assert.Equal(1, SpadExport.MakeReadyMade(Path.Combine(root, "gauges"), target));
            Assert.False(File.Exists(ours));
            Assert.False(Directory.Exists(gone));
            Assert.True(File.Exists(theirs));
            Assert.Single(Directory.GetFiles(Path.Combine(target, "Classic single"), "*.zip"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Ready_made_set_has_every_gauge_that_can_go_to_spad_in_its_group_folder()
        => ReadyMade(Gauges);

    [Fact]
    public void Ready_made_set_from_3000s_templates_folder_names_the_dc6_groups_too()
    {
        // FIPinator 3000 installs the DC-6 gauges at the top of Templates, the rest in their folders.
        var templates = Directory.CreateTempSubdirectory("fipinator-templates").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(Gauges, "*.json", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Gauges, file);
                if (relative.StartsWith("dc6" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) relative = relative[4..];
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(templates, relative))!);
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                if (json["font"] is not null) json["font"] = Path.Combine(AppContext.BaseDirectory, "repo", "assets", "fonts", "BarlowCondensed-Medium.ttf");
                File.WriteAllText(Path.Combine(templates, relative), json.ToJsonString());
            }
            ReadyMade(templates);
        }
        finally { Directory.Delete(templates, recursive: true); }
    }

    private static void ReadyMade(string gaugesRoot)
    {
        var target = Directory.CreateTempSubdirectory("fipinator-ready").FullName;
        try
        {
            var saved = SpadExport.MakeReadyMade(gaugesRoot, target);
            var zips = Directory.GetFiles(target, "*.zip", SearchOption.AllDirectories);
            Assert.Equal(saved, zips.Length);
            Assert.True(saved >= 50, $"only {saved}");
            Assert.Contains(zips, z => z.EndsWith(Path.Combine("Classic single", "FIPinator - Classic single Airspeed.zip"), StringComparison.Ordinal));
            Assert.Contains(zips, z => z.Contains("Heading", StringComparison.Ordinal));            // turning cards go to SPAD too
            Assert.Contains(zips, z => z.Contains("Attitude", StringComparison.Ordinal));           // horizons go to SPAD too
            Assert.Contains(zips, z => z.Contains("HSI", StringComparison.Ordinal));                // HSIs go to SPAD too
            Assert.Contains(zips, z => z.Contains("DME", StringComparison.Ordinal));                // numbers that hide go too
            Assert.DoesNotContain(zips, z => z.Contains("Jet horizon (compact)", StringComparison.Ordinal)); // the 737 standby: rolling tapes, not yet
            Assert.Contains(zips, z => z.EndsWith(Path.Combine("DC-6", "FIPinator - DC-6 Airspeed.zip"), StringComparison.Ordinal));
            // SPAD.neXt's gauge list doesn't show subfolders: each gauge carries its group as a category.
            var dc6 = zips.Single(z => z.EndsWith("FIPinator - DC-6 Airspeed.zip", StringComparison.Ordinal));
            using var zip = ZipFile.OpenRead(dc6);
            using var xml = zip.Entries.Single(e => e.Name.EndsWith(".xml", StringComparison.Ordinal)).Open();
            Assert.Equal("FIPinator: DC-6", XDocument.Load(xml).Root!.Element("GaugeSettings")!.Element("Category")!.Value);
        }
        finally { Directory.Delete(target, recursive: true); }
    }

    [Fact]
    public void Quiet_save_never_throws_and_removes_our_copy_when_a_gauge_can_no_longer_go()
    {
        var folder = Directory.CreateTempSubdirectory("fipinator-quiet").FullName;
        try
        {
            var gauge = Path.Combine(folder, "mine.json");
            File.Copy(Airspeed, gauge);
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(gauge))!.AsObject();
            json["font"] = Path.Combine(AppContext.BaseDirectory, "repo", "assets", "fonts", "BarlowCondensed-Medium.ttf");
            File.WriteAllText(gauge, json.ToJsonString());
            var spad = Path.Combine(folder, "spad");
            Assert.True(SpadExport.SaveQuietly(gauge, spad));
            Assert.Single(Directory.GetFiles(spad, "*.zip"));

            // Now give it a part that slides two ways at once: it can't go to SPAD, so our old copy goes away.
            json["layers"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "group",
                ["moveX"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "ias", ["scale"] = "kt" },
                ["moveY"] = new System.Text.Json.Nodes.JsonObject { ["value"] = "ias", ["scale"] = "kt" },
                ["layers"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["type"] = "circle", ["r"] = 5, ["fill"] = "#FFFFFF" }),
            });
            File.WriteAllText(gauge, json.ToJsonString());
            Assert.False(SpadExport.SaveQuietly(gauge, spad));
            Assert.Empty(Directory.GetFiles(spad, "*.zip"));

            // A broken file: no exception, nothing saved.
            File.WriteAllText(gauge, "{ not a gauge");
            Assert.False(SpadExport.SaveQuietly(gauge, spad));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Saving_the_same_gauge_twice_at_once_works()
    {
        // FIPinator 3000 asks for the SPAD version on save and again when the gauge opens, at the same moment.
        var folder = Directory.CreateTempSubdirectory("fipinator-spad-race").FullName;
        try
        {
            var gauge = Path.Combine(folder, "airspeed.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Airspeed))!;
            json["font"] = Path.Combine(AppContext.BaseDirectory, "repo", "assets", "fonts", "BarlowCondensed-Medium.ttf");
            File.WriteAllText(gauge, json.ToJsonString());
            var spad = Path.Combine(folder, "spad");
            var results = Enumerable.Range(0, 4).AsParallel().WithDegreeOfParallelism(4)
                .Select(_ => SpadExport.SaveQuietly(gauge, spad)).ToArray();
            Assert.All(results, Assert.True);
            Assert.Single(Directory.GetFiles(spad, "*.zip"));
            Assert.Empty(Directory.GetFiles(spad, "*.tmp"));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("wrap360(a + 180 - b)")]
    [InlineData("wrap180(a - b)")]
    [InlineData("choose(a - 100, b * 2, -b)")]
    [InlineData("clamp(a / 3, 10, 50) + min(a, b) - max(a, 5)")]
    [InlineData("abs(b - a) % 7")]
    [InlineData("-(a + b) * 0.453592")]
    public void Formulas_translate_to_spad_and_give_the_same_answers(string formula)
    {
        var expression = ValueExpression.Parse(formula);
        var rpn = expression.ToRpn(n => $"(A:{n.ToUpperInvariant()}, number)")!;
        foreach (var (a, b) in new[] { (0.0, 0.0), (123.4, 56.7), (359.0, 2.0), (17.0, 250.0), (99.0, 101.0) })
        {
            var ours = expression.Evaluate(n => n == "a" ? a : b);
            var spad = SpadPlayer.Evaluate(rpn, new Dictionary<string, double> { ["(A:A, number)"] = a, ["(A:B, number)"] = b });
            Assert.True(Math.Abs(ours - spad) < 1e-6, $"{formula} with a={a}, b={b}: ours {ours}, SPAD {spad} ({rpn})");
        }
    }
}

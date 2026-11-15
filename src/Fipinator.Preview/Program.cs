// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;
using Fipinator.Core;
using SkiaSharp;

// Renders a gauge file to a PNG, so gauges can be checked without a FIP or the sim.
//   fipinator-preview <gauge.json> [--set name=value]... [--out file.png] [--zoom 2]

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Usage: fipinator-preview <gauge.json> [--set name=value]... [--out file.png] [--zoom N]
               fipinator-preview combine needles|side "<name>" <out.json> <gauge.json> <gauge.json>...
                   needles: all the gauges' needles on the first gauge's dial (e.g. RPM 1-2 + 3-4)
                   side:    two gauges side by side on one FIP
        """);
    return args.Length == 0 ? 1 : 0;
}

if (args[0] == "classic")
{
    // fipinator-preview classic <1|2|4> <out folder> <folder with attitude-standard/altimeter/vertical-speed.json>
    if (args.Length < 4 || !int.TryParse(args[1], out var engines))
    {
        Console.Error.WriteLine("Use: fipinator-preview classic 1|2|4 <out folder> <folder with the attitude, altimeter and vertical speed gauges>");
        return 1;
    }
    var outDir = Path.GetFullPath(args[2]);
    var srcDir = Path.GetFullPath(args[3]);
    var sources = new Dictionary<string, string>
    {
        ["attitude"] = File.ReadAllText(Path.Combine(srcDir, "attitude-standard.json")),
        ["altimeter"] = File.ReadAllText(Path.Combine(srcDir, "altimeter.json")),
        ["vertical-speed"] = File.ReadAllText(Path.Combine(srcDir, "vertical-speed.json")),
    };
    Directory.CreateDirectory(outDir);
    // The font, as a path from the output folder (the programs also carry it next to themselves).
    var repoFont = Path.Combine(srcDir, "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
    var font = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
    foreach (var (file, json) in ClassicPack.Create(ClassicPack.Typical(engines), GaugeLook.Classic, sources))
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject(); // standard knobs for any gauge without
        node["font"] = font;
        node["author"] = "FIPinator 3000";
        var path = Path.Combine(outDir, file + ".json");
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "dc6extras")
{
    // fipinator-preview dc6extras <gauges\dc6 folder>: the DC-6 cockpit's directional gyro, turn and slip and clock,
    // in the DC-6 look (the rest of the DC-6 set comes from the DC-6 Gauges designs).
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Use: fipinator-preview dc6extras <gauges\\dc6 folder>");
        return 1;
    }
    var outDir = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(outDir);
    var repoFont = Path.Combine(outDir, "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
    var font = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
    foreach (var (file, json) in new[]
    {
        ("heading", HeadingGenerator.Create("DC-6 Heading", GaugeLook.Dc6)),
        ("turn-and-slip", TurnAndSlipGenerator.Create("DC-6 Turn and slip", GaugeLook.Dc6)),
        ("clock", ClassicExtras.Clock("DC-6 Clock", GaugeLook.Dc6)),
        // What the old Classic four-engine set had beyond the DC-6 set (owner: all of it goes to the DC-6 group).
        ("turn-coordinator", TurnCoordinatorGenerator.Create("DC-6 Turn coordinator", GaugeLook.Dc6)),
        ("hsi", HsiGenerator.Create("DC-6 HSI", GaugeLook.Dc6)),
        ("adf", ClassicExtras.Adf("DC-6 ADF", GaugeLook.Dc6)),
        ("radar-altimeter", ClassicExtras.RadarAltimeter("DC-6 Radar altimeter", GaugeLook.Dc6)),
        ("egt", EngineDialGenerator.Egt(Dc6Plane, GaugeLook.Dc6)),
        ("fuel", FuelGenerator.Create(Dc6Plane.TankGallons, "DC-6 Fuel", GaugeLook.Dc6)),
    })
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject();
        node["font"] = font;
        node["author"] = "FIPinator 3000";
        var path = Path.Combine(outDir, file + ".json");
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "spadset")
{
    // fipinator-preview spadset <gauges folder> <out folder>: every gauge that can go to SPAD.neXt, as SPAD gauges
    // in group folders (the SPAD gauges download is made from this by tools\make-spad-repo.ps1).
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Use: fipinator-preview spadset <gauges folder> <out folder>");
        return 1;
    }
    var saved = SpadExport.MakeReadyMade(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    Console.WriteLine($"{saved} SPAD.neXt gauges in {Path.GetFullPath(args[2])}");
    return saved > 0 ? 0 : 1;
}

if (args[0] == "turboprop")
{
    // fipinator-preview turboprop saab|caravan <out folder>
    var plane = args.Length >= 3 ? args[1].ToLowerInvariant() switch
    {
        "saab" => TurbopropPack.Saab340B,
        "caravan" => TurbopropPack.Caravan208B,
        _ => null,
    } : null;
    if (plane is null)
    {
        Console.Error.WriteLine("Use: fipinator-preview turboprop saab|caravan <out folder>");
        return 1;
    }
    var outDir = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outDir);
    // The font, as a path from the output folder (gauges/turboprop/<plane> -> assets/fonts).
    var repoFont = Path.Combine(outDir, "..", "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
    var font = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
    var gauges = TurbopropPack.Create(plane).ToList();
    // The Saab's own flight instruments: wide-range airspeed and the glass attitude display.
    if (plane == TurbopropPack.Saab340B)
    {
        gauges.Add(("airspeed", TurbopropFlight.Airspeed("Saab 340B Airspeed")));
        gauges.Add(("eadi", TurbopropFlight.Eadi("Saab 340B Attitude (EADI)")));
    }
    foreach (var (file, json) in gauges)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject(); // standard knobs for any gauge without
        node["font"] = font;
        node["author"] = "FIPinator 3000";
        var path = Path.Combine(outDir, file + ".json");
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "templates")
{
    // fipinator-preview templates <out folder>   (gear, flaps, autopilot, radios... one folder per group)
    if (args.Length < 2) { Console.Error.WriteLine("Use: fipinator-preview templates <out folder>"); return 1; }
    var root = Path.GetFullPath(args[1]);
    foreach (var (group, file, json) in InstrumentTemplates.All())
    {
        var outDir = Path.Combine(root, group);
        Directory.CreateDirectory(outDir);
        var repoFont = Path.Combine(outDir, "..", "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject();
        node["font"] = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
        node["author"] = "FIPinator 3000";
        var path = Path.Combine(outDir, file + ".json");
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "dc6mini")
{
    // fipinator-preview dc6mini <out folder>   (the DC-6 "all engines on one screen" page)
    if (args.Length < 2) { Console.Error.WriteLine("Use: fipinator-preview dc6mini <out folder>"); return 1; }
    var outDir = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(outDir);
    var repoFont = Path.Combine(outDir, "..", "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
    var node = System.Text.Json.Nodes.JsonNode.Parse(MiniPanels.Dc6Engines())!.AsObject();
    node["font"] = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
    node["author"] = "FIPinator 3000";
    var path = Path.Combine(outDir, "engines-mini.json");
    node["license"] = GaugeDefinition.ShippedLicense;
    File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
    GaugeDefinition.Load(path);
    Console.WriteLine($"Wrote {path}");
    return 0;
}

if (args[0] == "titan")
{
    // fipinator-preview titan <out folder>: the Cessna 404 Titan's engine gauges (Cessna404Panel).
    if (args.Length < 2) { Console.Error.WriteLine("Use: fipinator-preview titan <out folder>"); return 1; }
    var titanDir = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(titanDir);
    var titanFont = Path.GetRelativePath(titanDir, Path.GetFullPath(Path.Combine(titanDir, "..", "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf"))).Replace('\\', '/');
    foreach (var (file, json) in Cessna404Panel.Create())
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node["font"] = titanFont;
        node["author"] = "FIPinator 3000";
        node["license"] = GaugeDefinition.ShippedLicense;
        var path = Path.Combine(titanDir, file + ".json");
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "jet")
{
    // fipinator-preview jet modern|classic|727|737-800 <out folder>
    //   737-800: the 737NG (PMDG 737-800) centre-panel gauges: standby instruments, RMI, flaps, autobrake (no engines)
    //   modern / classic: 737 MAX numbers (the modern set also gets the flight instruments)
    //   727: the Boeing 727 trijet set (EPR, N1, EGT, N2, fuel flow + flight instruments, 727 look)
    var is727 = args.Length >= 3 && args[1].Equals("727", StringComparison.Ordinal);
    var isNg = args.Length >= 3 && args[1].Equals("737-800", StringComparison.Ordinal);
    var jetLook = JetPack.Look.Classic;
    if (isNg) jetLook = JetPack.Look.Modern;
    if (args.Length < 3 || (!is727 && !isNg && !Enum.TryParse(args[1], ignoreCase: true, out jetLook)))
    {
        Console.Error.WriteLine("Use: fipinator-preview jet modern|classic|727|737-800 <out folder>");
        return 1;
    }
    var outDir = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(outDir);
    var repoFont = Path.Combine(outDir, "..", "..", "..", "assets", "fonts", "BarlowCondensed-Medium.ttf");
    var font = Path.GetRelativePath(outDir, Path.GetFullPath(repoFont)).Replace('\\', '/');
    // 727: its own engine dials in the 727 panel's look, three versions each (Boeing727Engines).
    var gauges = (is727 ? Boeing727Engines.Create()
        // 737-800 (PMDG): no engine pages (owner, 2026-11-13: the PMDG's engine numbers never matched; dropped).
        : isNg ? []
        : JetPack.Create(JetPack.Boeing737Max, jetLook)).ToList();
    if (isNg) gauges.Add(("standby-attitude", TurbopropFlight.Eadi("737-800 Standby attitude", standby: true)));
    if (isNg) gauges.Add(("standby-altimeter", Ng737Panel.StandbyAltimeter("737-800 Standby altimeter and airspeed")));
    if (isNg) gauges.Add(("rmi", Ng737Panel.Rmi("737-800 RMI")));
    if (isNg) gauges.Add(("autobrake-flaps", Ng737Panel.AutobrakeFlaps("737-800 Autobrake and flaps")));
    if (isNg) gauges.Add(("flaps", Ng737Panel.Flaps("737-800 Flaps")));
    if (is727)
    {
        const GaugeLook b727 = GaugeLook.Boeing727;
        gauges.Add(("airspeed", TurbopropFlight.MachAirspeed("727 Mach/airspeed", b727)));
        gauges.Add(("adi", JetInstruments.Adi("727 ADI", b727)));
        gauges.Add(("altimeter", JetInstruments.Altimeter("727 Altimeter", b727)));
        gauges.Add(("turn-and-slip", TurnAndSlipGenerator.Create("727 Turn and slip", b727)));
        gauges.Add(("hsi", JetInstruments.HsiWithWindows("727 HSI", b727)));
        var dc6 = Path.Combine(outDir, "..", "..", "dc6");
        gauges.Add(("course-ils", GaugeParts.Reskin(File.ReadAllText(Path.Combine(dc6, "course-ils.json")), b727, "727 Course / ILS")));
        gauges.Add(("engines-mini", MiniPanels.JetEngines(JetPack.Boeing727)));
        gauges.Add(("radio-altimeter", Boeing727Panel.RadioAltimeter()));
        gauges.Add(("ivsi", Boeing727Panel.Ivsi()));
    }
    else if (jetLook == JetPack.Look.Modern && !isNg)
    {
        gauges.Add(("airspeed", TurbopropFlight.MachAirspeed("Jet Mach/airspeed")));
        gauges.Add(("eadi", TurbopropFlight.Eadi("Jet Attitude (glass)")));
        gauges.Add(("standby", JetFlight.Standby("Jet horizon (compact)")));
        gauges.Add(("adi", JetInstruments.Adi("Jet ADI (airliner)")));
        gauges.Add(("altimeter", JetInstruments.Altimeter("Jet Altimeter")));
        gauges.Add(("hsi", JetInstruments.HsiWithWindows("Jet HSI")));
        gauges.Add(("vertical-speed", JetInstruments.VerticalSpeed("Jet Vertical speed")));
        gauges.Add(("fuel", JetPack.Fuel("Jet Fuel")));
        gauges.Add(("engines-mini", MiniPanels.JetEngines(JetPack.Boeing737Max)));
    }
    foreach (var (file, json) in gauges)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject(); // standard knobs for any gauge without
        node["font"] = font;
        node["author"] = "FIPinator 3000";
        var path = Path.Combine(outDir, file + ".json");
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(path);
        Console.WriteLine($"Wrote {path}");
    }
    return 0;
}

if (args[0] == "reskin")
{
    // fipinator-preview reskin classic|dc6|lighttwin <in.json> <out.json> ["<name>"]
    if (args.Length < 4 || !Enum.TryParse<GaugeLook>(args[1], ignoreCase: true, out var look))
    {
        Console.Error.WriteLine("Use: fipinator-preview reskin classic|dc6|lighttwin <in.json> <out.json> [\"<name>\"]");
        return 1;
    }
    var (input, target) = (Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));
    try
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(GaugeParts.Reskin(File.ReadAllText(input), look, args.Length > 4 ? args[4] : null)))!.AsObject();
        if (node["font"]?.GetValue<string>() is { } font && !Path.IsPathRooted(font))
            node["font"] = Path.GetRelativePath(Path.GetDirectoryName(target)!, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(input)!, font))).Replace('\\', '/');
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(target, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(target);
        Console.WriteLine($"Wrote {target}");
        return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (args[0] == "spad")
{
    // A gauge as a SPAD.neXt FIP gauge (.zip with XML + pictures): fipinator-preview spad <gauge.json> <parent folder>
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Use: fipinator-preview spad <gauge.json> <parent folder>");
        return 1;
    }
    try
    {
        var result = SpadExport.Export(args[1], args[2]);
        Console.WriteLine($"Wrote {result.ZipFile}");
        if (result.LeftOut.Count > 0) Console.WriteLine($"Left out: {string.Join(", ", result.LeftOut)}");
        return 0;
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (args[0] == "icon")
{
    // FIPinator 3000's icon: the Live gauge icon with a wrench badge, so the two apps look different.
    //   fipinator-preview icon <live.ico> <out.ico> [preview.png]
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Use: fipinator-preview icon <live.ico> <out.ico> [preview.png]");
        return 1;
    }
    var source = File.ReadAllBytes(args[1]);
    var count = BitConverter.ToUInt16(source, 4);
    var pictures = new List<(int Size, byte[] Png)>();
    for (var i = 0; i < count; i++)
    {
        var entry = 6 + 16 * i;
        var (length, offset) = (BitConverter.ToInt32(source, entry + 8), BitConverter.ToInt32(source, entry + 12));
        using var picture = SKBitmap.Decode(source.AsSpan(offset, length));
        var size = picture.Width;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        using (var original = SKImage.FromBitmap(picture)) c.DrawImage(original, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        DrawWrenchBadge(c, size);
        using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
        pictures.Add((size, png.ToArray()));
    }
    using (var ico = new BinaryWriter(File.Create(args[2])))
    {
        ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)pictures.Count);
        var at = 6 + 16 * pictures.Count;
        foreach (var (size, png) in pictures)
        {
            ico.Write((byte)(size >= 256 ? 0 : size)); ico.Write((byte)(size >= 256 ? 0 : size));
            ico.Write((byte)0); ico.Write((byte)0); ico.Write((ushort)1); ico.Write((ushort)32);
            ico.Write(png.Length); ico.Write(at);
            at += png.Length;
        }
        foreach (var (_, png) in pictures) ico.Write(png);
    }
    if (args.Length >= 4) File.WriteAllBytes(args[3], pictures.MaxBy(p => p.Size).Png);
    Console.WriteLine($"Wrote {args[2]} ({string.Join(", ", pictures.Select(p => $"{p.Size}px"))})");
    return 0;

    static void DrawWrenchBadge(SKCanvas c, int size)
    {
        // Small icons get a bigger badge so the wrench still reads at 16 px.
        var radius = size * (size <= 32 ? 0.30f : 0.25f);
        var center = new SKPoint(size - radius - size * 0.01f, size - radius - size * 0.01f);
        using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(0x2F, 0x6F, 0xC9) };
        using var edge = new SKPaint { IsAntialias = true, Color = new SKColor(0x10, 0x12, 0x14), Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, radius * 0.12f) };
        c.DrawCircle(center, radius, fill);
        c.DrawCircle(center, radius, edge);

        // The wrench, drawn lying flat, then turned so it runs bottom-left to top-right.
        var r = radius * 0.78f;
        c.Save();
        c.Translate(center);
        c.RotateDegrees(-45);
        c.SaveLayer();
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeCap = SKStrokeCap.Round, StrokeWidth = r * 0.30f, Style = SKPaintStyle.Stroke };
        c.DrawLine(-r * 0.80f, 0, r * 0.20f, 0, white);
        white.Style = SKPaintStyle.Fill;
        c.DrawCircle(r * 0.45f, 0, r * 0.40f, white);
        using var cut = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Clear };
        c.DrawRect(new SKRect(r * 0.45f, -r * 0.14f, r * 1.0f, r * 0.14f), cut);
        c.Restore();
        c.Restore();
    }
}

if (args[0] == "combine")
{
    if (args.Length < 6 || args[1] is not ("needles" or "side"))
    {
        Console.Error.WriteLine("Use: fipinator-preview combine needles|side \"<name>\" <out.json> <gauge.json> <gauge.json>...");
        return 1;
    }
    var (how, name, combined, inputs) = (args[1], args[2], Path.GetFullPath(args[3]), args[4..]);
    try
    {
        var files = inputs.Select(File.ReadAllText).ToList();
        var json = how == "side" ? GaugeCombiner.SideBySide(files[0], files[1], name) : GaugeCombiner.MoreNeedles(files, name);
        // Keep the font pointing at the same file from the output's folder.
        var node = System.Text.Json.Nodes.JsonNode.Parse(KnobDefaults.Fill(json))!.AsObject(); // standard knobs for any gauge without
        if (node["font"]?.GetValue<string>() is { } font && !Path.IsPathRooted(font))
        {
            var fontFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[0]))!, font));
            node["font"] = Path.GetRelativePath(Path.GetDirectoryName(combined)!, fontFile).Replace('\\', '/');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(combined)!);
        node["license"] = GaugeDefinition.ShippedLicense;
        File.WriteAllText(combined, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        GaugeDefinition.Load(combined); // check it
        Console.WriteLine($"Wrote {combined}");
        return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

var gaugePath = args[0];
var values = new Dictionary<string, double>();
string? outPath = null;
var zoom = 1;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--set" when i + 1 < args.Length:
            var parts = args[++i].Split('=', 2);
            if (parts.Length != 2 || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var v))
            {
                Console.Error.WriteLine($"Can't read '{args[i]}'. Use --set name=value, for example --set ias=190.");
                return 1;
            }
            values[parts[0]] = v;
            break;
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--zoom" when i + 1 < args.Length && int.TryParse(args[i + 1], out var z) && z is >= 1 and <= 8:
            zoom = z;
            i++;
            break;
        default:
            Console.Error.WriteLine($"Unknown option '{args[i]}'. Run with --help to see options.");
            return 1;
    }
}

using var renderer = GaugeRenderer.FromFile(gaugePath);
outPath ??= Path.ChangeExtension(Path.GetFileName(gaugePath), ".png");

using var bitmap = renderer.Render(values);
using var output = zoom == 1
    ? bitmap.Copy()
    : bitmap.Resize(new SKImageInfo(bitmap.Width * zoom, bitmap.Height * zoom), new SKSamplingOptions(SKFilterMode.Nearest));
using var image = SKImage.FromBitmap(output);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(outPath, data.ToArray());

Console.WriteLine($"Wrote {outPath} ({output.Width}x{output.Height}) for '{renderer.Gauge.Name}'.");
return 0;

/// <summary>The DC-6's numbers for the extra DC-6 gauges (speeds as the owner's sim reported them; RPM and MP marks
/// as on the DC-6's dials). These were the old "Classic four-engine" numbers.</summary>
partial class Program
{
    static readonly AircraftProfile Dc6Plane = new("DC-6", 4, true, new AircraftSpeeds(74, 94, 258, 301), 500, MaxRpm: 2800,
        RpmGreenFrom: 1900, RpmGreenTo: 2600, ManifoldMax: 60, ManifoldGreenFrom: 20, ManifoldGreenTo: 40, ManifoldRedLine: 53);
}

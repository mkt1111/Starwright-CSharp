using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starwright.Core;

public sealed class AttachSpec
{
    public bool stack { get; set; }
    public bool radial { get; set; }
    public bool surface { get; set; }
    public bool outer { get; set; }
}

public sealed class DragSpec
{
    public double top { get; set; } = 0.9;
    public double bottom { get; set; } = 0.9;
    public double side { get; set; } = 0.5;
}

public sealed class EngineSpec
{
    public string type { get; set; } = "liquid";
    public double thrust { get; set; }
    public double ispVac { get; set; }
    public double ispAtm { get; set; }
    public double gimbal { get; set; }
    public double elecUse { get; set; }
    public JetSpec? jet { get; set; }
}

public sealed class JetSpec
{
    public double maxAlt { get; set; } = 18000;
    public double maxMach { get; set; } = 2.5;
}

public sealed class DecouplerSpec { public double force { get; set; } }

public sealed class ChuteSpec
{
    public double area { get; set; }
    public double semiArea { get; set; }
    public double fullAlt { get; set; }
    public double maxSpeed { get; set; }
}

public sealed class SurfaceSpec
{
    public double area { get; set; }
    public double sweep { get; set; }
    public double slope { get; set; }
    public double stall { get; set; }
    public bool allMoving { get; set; }
    public JsonElement control { get; set; }

    public string? ControlKind => control.ValueKind switch
    {
        JsonValueKind.True => "auto",
        JsonValueKind.String => control.GetString(),
        _ => null,
    };
}

public sealed class SolarSpec
{
    public double rate { get; set; }
    public double span { get; set; }
    public bool @fixed { get; set; }
    public bool? tracking { get; set; }
}

public sealed class RcsSpec
{
    public double thrust { get; set; }
    public double isp { get; set; } = 240;
    public bool linear { get; set; }
}

public sealed class WheelSpec
{
    public double radius { get; set; }
    public double drive { get; set; }
    public double brake { get; set; }
    public bool steer { get; set; }
    public double maxSpeed { get; set; }
    public double elecUse { get; set; }
    public bool retract { get; set; }
}

public sealed class GeneratorSpec
{
    public double rate { get; set; }
    public double fuelUse { get; set; }
}

public sealed class AntennaSpec
{
    public double rate { get; set; }
    public double range { get; set; }
}

public sealed class AirbrakeSpec { public double area { get; set; } }
public sealed class LegsSpec { public double reach { get; set; } public double spread { get; set; } }
public sealed class LabSpec { public double rate { get; set; } = 1; public double mult { get; set; } = 1; public double elecUse { get; set; } }

public sealed class PartDef
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string cat { get; set; } = "";
    public string shape { get; set; } = "";
    public string? size { get; set; }
    public string? command { get; set; }
    public string? desc { get; set; }
    public double cost { get; set; }
    public double mass { get; set; }
    public double h { get; set; }
    public double r { get; set; }
    public double rTop { get; set; }
    public double rBottom { get; set; }
    public double rw { get; set; }
    public double t { get; set; }
    public double torque { get; set; }
    public double comY { get; set; }
    public double tolerance { get; set; } = 6;
    public double maxTemp { get; set; } = 1400;
    public double elecDrain { get; set; }
    public int crew { get; set; }
    public bool topOnly { get; set; }
    public bool heatshield { get; set; }
    public bool hidden { get; set; }
    public AttachSpec attach { get; set; } = new();
    public DragSpec drag { get; set; } = new();
    public Dictionary<string, double> res { get; set; } = new();
    public EngineSpec? engine { get; set; }
    public DecouplerSpec? decoupler { get; set; }
    public ChuteSpec? chute { get; set; }
    public SurfaceSpec? fin { get; set; }
    public SurfaceSpec? wing { get; set; }
    public SolarSpec? solar { get; set; }
    public RcsSpec? rcs { get; set; }
    public WheelSpec? wheel { get; set; }
    public GeneratorSpec? generator { get; set; }
    public AntennaSpec? antenna { get; set; }
    public AirbrakeSpec? airbrake { get; set; }
    public LegsSpec? legs { get; set; }
    public LabSpec? lab { get; set; }
    public JsonElement? light { get; set; }
}

public static class PartCatalog
{
    public const double G0 = Num.G0;
    public static IReadOnlyDictionary<string, PartDef> Parts { get; }
    public static IReadOnlyList<PartDef> List { get; }
    public static IReadOnlyDictionary<string, string> Renamed { get; }

    static PartCatalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("parts.json")
            ?? throw new InvalidOperationException("parts.json is not embedded");
        var doc = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOpts())
            ?? throw new InvalidOperationException("parts.json did not parse");
        Parts = doc.parts;
        List = doc.parts.Values.Where(p => !p.hidden).ToList();
        Renamed = doc.renamed;
    }

    public static JsonSerializerOptions JsonOpts() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string CurrentPartId(string id) =>
        !Parts.ContainsKey(id) && Renamed.TryGetValue(id, out var n) ? n : id;

    public static PartDef Def(string id)
    {
        id = CurrentPartId(id);
        if (!Parts.TryGetValue(id, out var d)) throw new InvalidOperationException("Unknown part " + id);
        return d;
    }

    public static bool WantsInterstage(PartDef? def) =>
        def != null && def.shape == "engine" && def.attach.stack && def.engine != null && def.engine.type != "jet";

    public static double InterstageMass(double h, double r0, double r1)
    {
        const double kgPerM2 = 12, minKg = 3;
        var wall = Math.PI * (r0 + r1) * Math.Sqrt(h * h + (r1 - r0) * (r1 - r0));
        return Math.Max(minKg, Math.Round(wall * kgPerM2 * 10) / 10);
    }

    public static double EngineIsp(PartDef def, double pAtm)
    {
        var e = def.engine!;
        var p = Math.Min(Math.Max(pAtm, 0), 4);
        return Math.Max(e.ispVac + (e.ispAtm - e.ispVac) * p, 1);
    }

    public static double EngineThrust(PartDef def, double pAtm)
    {
        var e = def.engine!;
        return e.thrust * 1000 * EngineIsp(def, pAtm) / e.ispVac;
    }

    public static string EngineProp(PartDef def)
    {
        var t = def.engine?.type;
        return t == "solid" ? "solid" : t == "ion" ? "xenon" : "fuel";
    }

    sealed class CatalogFile
    {
        public Dictionary<string, PartDef> parts { get; set; } = new();
        public Dictionary<string, string> renamed { get; set; } = new();
    }
}

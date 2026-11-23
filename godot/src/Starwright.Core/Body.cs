namespace Starwright.Core;

public sealed class Atmosphere
{
    public double height, scale, rho0;
    public double[] color = { 1, 1, 1 };
    public bool oxygen;
}

public sealed class RingSystem
{
    public double inner, outer;
}

public sealed class Biome
{
    public string id = "";
    public string name = "";
    public bool water;
}

public sealed class LaunchSite
{
    public string name = "";
    public double lat, lon;
    public DVec3 dir = new(1, 0, 0);
    public double heading = 90;
    public double length, width;
    public LaunchSite? start;
    public LaunchSite? center;
}

/// <summary>
/// A Helion-system body. Orbital elements, gravity and atmosphere match js/core/bodies.js.
/// Terrain height is 0 in this milestone (the procedural SURFACES height field is not ported yet),
/// so worlds are smooth spheres. The launch site sits on the datum, which the browser game also
/// measures as height 0.
/// </summary>
public sealed class Body
{
    public const double HomeRho0 = 1.22;
    public const double VerdaSea = -3;

    public string id = "";
    public string name = "";
    public string type = "";
    public Body? parent;
    public readonly List<Body> children = new();
    public double radius, gm, surfaceG, soi = double.PositiveInfinity;
    public double rotPeriod, rot0, seaLevel, minElevation, maxElevation, sciMult, spaceBorder;
    public bool ocean, gas, tidal, comet;
    public int color;
    public int seed;
    public string blurb = "";
    public Atmosphere? atmo;
    public RingSystem? ring;
    public Orbit? orbit;
    public Biome[] biomes = Array.Empty<Biome>();
    public LaunchSite? launchSite;
    public Dictionary<string, LaunchSite>? sites;

    public Body(BodyDef def, Body? parentBody)
    {
        id = def.id; name = def.name; type = def.type;
        parent = parentBody;
        radius = def.radius;
        gm = def.gm ?? def.surfaceG * def.radius * def.radius;
        surfaceG = gm / (radius * radius);
        if (def.atmo != null)
        {
            atmo = new Atmosphere
            {
                height = def.atmo[0], scale = def.atmo[1], rho0 = def.atmo[2],
                color = def.atmoColor ?? new[] { 0.7, 0.8, 1.0 },
                oxygen = def.oxygen,
            };
        }
        ocean = def.ocean;
        seaLevel = def.seaLevel;
        if (def.ring != null) ring = new RingSystem { inner = def.ring[0] * def.radius, outer = def.ring[1] * def.radius };
        if (def.relief != null) { minElevation = def.relief[0]; maxElevation = def.relief[1]; }
        blurb = def.blurb;
        gas = def.gas;
        color = def.color;
        seed = def.seed;
        sciMult = def.sciMult;
        spaceBorder = def.spaceBorder;
        comet = def.comet;
        if (parentBody != null && def.orbit != null)
        {
            parentBody.children.Add(this);
            var o = def.orbit;
            orbit = Orbit.FromElements(o[0], o[1], o[2], o[3], o[4], o[5], parentBody.gm, 0);
            soi = o[0] * Math.Pow(gm / parentBody.gm, 0.4);
        }
        tidal = def.rotPeriodTidal;
        rotPeriod = tidal ? orbit!.Period : (def.rotPeriod > 0 ? def.rotPeriod : 1);
        if (tidal)
        {
            var p = orbit!.PosAt(0);
            rot0 = Math.Atan2(p.z, -p.x);
        }
        else rot0 = def.rot0;
        biomes = def.biomes ?? new[] { new Biome { id = "surface", name = name, water = false } };
    }

    public double AngularRate => Num.Tau / rotPeriod;

    public DVec3 PosAt(double t, DVec3? o = null)
    {
        o ??= new DVec3();
        return orbit != null ? orbit.PosAt(t, o) : o.set(0, 0, 0);
    }

    public DVec3 VelAt(double t, DVec3? o = null)
    {
        o ??= new DVec3();
        return orbit != null ? orbit.VelAt(t, o) : o.set(0, 0, 0);
    }

    public DVec3 GlobalPosAt(double t, DVec3? o = null)
    {
        o ??= new DVec3();
        double x = 0, y = 0, z = 0;
        var scratch = new DVec3();
        for (var b = this; b?.orbit != null; b = b.parent)
        {
            b.orbit.PosAt(t, scratch);
            x += scratch.x; y += scratch.y; z += scratch.z;
        }
        return o.set(x, y, z);
    }

    public DVec3 GlobalVelAt(double t, DVec3? o = null)
    {
        o ??= new DVec3();
        double x = 0, y = 0, z = 0;
        var scratch = new DVec3();
        for (var b = this; b?.orbit != null; b = b.parent)
        {
            b.orbit.VelAt(t, scratch);
            x += scratch.x; y += scratch.y; z += scratch.z;
        }
        return o.set(x, y, z);
    }

    public double RotationAngle(double t)
    {
        var a = (rot0 + AngularRate * t) % Num.Tau;
        return a < 0 ? a + Num.Tau : a;
    }

    public DVec3 AngVel(DVec3? o = null) { o ??= new DVec3(); return o.set(0, AngularRate, 0); }

    public double Density(double alt)
    {
        var at = atmo;
        if (at == null || !(alt < at.height)) return 0;
        return at.rho0 * Math.Exp(-(alt > 0 ? alt : 0) / at.scale);
    }

    public double PressureAtm(double alt)
    {
        var at = atmo;
        if (at == null || !(alt < at.height)) return 0;
        return (at.rho0 / HomeRho0) * Math.Exp(-(alt > 0 ? alt : 0) / at.scale);
    }

    public DVec3 SurfaceDirToInertial(DVec3 d, double t, DVec3? o = null)
    {
        o ??= new DVec3();
        var a = RotationAngle(t); var c = Math.Cos(a); var s = Math.Sin(a);
        return o.set(d.x * c + d.z * s, d.y, -d.x * s + d.z * c);
    }

    public DVec3 InertialToSurfaceDir(DVec3 d, double t, DVec3? o = null)
    {
        o ??= new DVec3();
        var a = RotationAngle(t); var c = Math.Cos(a); var s = Math.Sin(a);
        return o.set(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
    }

    public (double lat, double lon) LatLon(DVec3 d)
    {
        var l = Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
        if (l == 0) l = 1;
        var yy = d.y / l;
        if (yy > 1) yy = 1; else if (yy < -1) yy = -1;
        return (Math.Asin(yy), Math.Atan2(-d.z, d.x));
    }

    public DVec3 DirFromLatLon(double lat, double lon, DVec3? o = null)
    {
        o ??= new DVec3();
        var c = Math.Cos(lat);
        return o.set(c * Math.Cos(lon), Math.Sin(lat), -c * Math.Sin(lon));
    }

    /// <summary>Metres above <see cref="radius"/>. 0 until the procedural height field is ported.</summary>
    public double TerrainHeight(DVec3 d, double lod = 0) => 0;

    public double SurfaceHeight(DVec3 d)
    {
        var h = TerrainHeight(d);
        return ocean && h < seaLevel ? seaLevel : h;
    }

    public bool IsWater(DVec3 d) => ocean && TerrainHeight(d) < seaLevel;

    public Biome BiomeAt(DVec3 d)
    {
        if (IsWater(d))
        {
            foreach (var b in biomes) if (b.water) return b;
        }
        foreach (var b in biomes) if (!b.water) return b;
        return biomes.Length > 0 ? biomes[0] : new Biome { id = "surface", name = name };
    }

    public double[] SurfaceColorAt(DVec3 d)
    {
        var hex = color;
        return new[] { ((hex >> 16) & 255) / 255.0, ((hex >> 8) & 255) / 255.0, (hex & 255) / 255.0 };
    }
}

public sealed class BodyDef
{
    public string id = "", name = "", type = "", blurb = "";
    public double radius, surfaceG, rotPeriod, rot0, seaLevel, sciMult, spaceBorder;
    public double? gm;
    public bool rotPeriodTidal, ocean, gas, oxygen, comet;
    public double[]? orbit;
    public double[]? atmo;
    public double[]? atmoColor;
    public double[]? relief;
    public double[]? ring;
    public int color, seed;
    public Biome[]? biomes;
    public string? rotSync;
}

public sealed class HelionSystem
{
    public Body[] bodies = Array.Empty<Body>();
    public Dictionary<string, Body> byId = new();
    public Body star = null!;
    public Body home = null!;
    public LaunchSite launchSite = null!;
    public Dictionary<string, LaunchSite> sites = new();

    public static readonly HelionSystem System = Build();

    static HelionSystem Build()
    {
        Biome Land(string id, string name) => new() { id = id, name = name, water = false };
        Biome Water(string id, string name) => new() { id = id, name = name, water = true };
        var defs = new List<BodyDef>
        {
            new() { id = "helion", name = "Helion", type = "star", radius = 2.2e8, gm = 1.2e18, rotPeriod = 432000, color = 0xffd76a, seed = 101, sciMult = 11, spaceBorder = 1e9, blurb = "The system's star. Everything else is in orbit around it.", biomes = new[] { Land("photosphere", "Photosphere") } },
            new() { id = "sear", name = "Sear", type = "planet", radius = 1.5e5, surfaceG = 2.0, rotPeriodTidal = true, orbit = new[] { 3.4e9, 0.06, 3.5, 20, 40, 0.7 }, color = 0xd9541a, seed = 111, sciMult = 8, spaceBorder = 4e4, relief = new[] { -6000d, 6500 }, blurb = "Tidally locked and half molten." },
            new() { id = "cindra", name = "Cindra", type = "planet", radius = 2.5e5, surfaceG = 2.8, rotPeriod = 1.2e6, orbit = new[] { 6.2e9, 0.18, 6, 70, 15, 2.1 }, color = 0x5a3a28, seed = 202, sciMult = 7, spaceBorder = 6e4, relief = new[] { -6000d, 8000 }, blurb = "A scorched, cratered cinder on an eccentric orbit." },
            new() { id = "murk", name = "Murk", type = "planet", radius = 5.4e5, surfaceG = 8.4, rotPeriod = 7.5e5, orbit = new[] { 9.9e9, 0.015, 2.2, 250, 0, 4.0 }, atmo = new[] { 110000d, 6500, 60 }, atmoColor = new[] { 0.95, 0.74, 0.42 }, color = 0xd8a55a, seed = 252, sciMult = 8, spaceBorder = 2.6e5, relief = new[] { -2500d, 12000 }, blurb = "Fifty atmospheres of ochre cloud." },
            new() { id = "verda", name = "Verda", type = "planet", radius = 6.4e5, surfaceG = 9.6, rotPeriod = 19800, rot0 = Math.PI, orbit = new[] { 1.36e10, 0d, 0, 0, 0, 0 }, atmo = new[] { 72000d, 5800, 1.22 }, atmoColor = new[] { 0.42, 0.66, 1.0 }, oxygen = true, ocean = true, seaLevel = Body.VerdaSea, color = 0x3f8fd0, seed = 303, sciMult = 1, spaceBorder = 2.5e5, relief = new[] { -6000d, 9000 }, blurb = "Home. Oceans, green lowlands and the Starwright Flight Center.", biomes = new[] { Land("greenbelt", "Greenbelt"), Land("highlands", "Highlands"), Water("ocean", "Ocean"), Land("ice", "Ice") } },
            new() { id = "pell", name = "Pell", type = "moon", radius = 2.1e5, surfaceG = 1.6, rotPeriodTidal = true, orbit = new[] { 1.25e7, 0d, 0, 0, 0, 1.7 }, color = 0x9a9a98, seed = 404, sciMult = 4, spaceBorder = 6e4, relief = new[] { -7000d, 7000 }, blurb = "Verda's grey, cratered near moon." },
            new() { id = "ost", name = "Ost", type = "moon", radius = 7.0e4, surfaceG = 0.5, rotPeriod = 40000, orbit = new[] { 4.4e7, 0.08, 5.5, 40, 30, 0.5 }, color = 0xcfe6f5, seed = 505, sciMult = 5, spaceBorder = 3e4, relief = new[] { -3500d, 3500 }, blurb = "A small, bright ice moon." },
            new() { id = "rusk", name = "Rusk", type = "planet", radius = 3.6e5, surfaceG = 3.2, rotPeriod = 24000, orbit = new[] { 2.05e10, 0.05, 1.5, 130, 60, 3.6 }, atmo = new[] { 45000d, 6500, 0.25 }, atmoColor = new[] { 0.86, 0.60, 0.42 }, color = 0xb5562f, seed = 606, sciMult = 6, spaceBorder = 1.4e5, relief = new[] { -8000d, 6500 }, blurb = "A rust-red desert world." },
            new() { id = "dray", name = "Dray", type = "moon", radius = 3.2e4, surfaceG = 0.22, rotPeriodTidal = true, orbit = new[] { 5.5e6, 0.02, 1.0, 10, 0, 1.1 }, color = 0x7a686a, seed = 616, sciMult = 6.5, spaceBorder = 1.2e4, relief = new[] { -5000d, 4500 }, blurb = "Rusk's lumpy moon." },
            new() { id = "thal", name = "Thal", type = "planet", radius = 5.2e6, surfaceG = 8.5, rotPeriod = 36000, orbit = new[] { 6.8e10, 0.04, 1.2, 200, 10, 5.1 }, atmo = new[] { 400000d, 40000, 3.0 }, atmoColor = new[] { 0.45, 0.75, 0.80 }, gas = true, color = 0x3fa3a0, seed = 707, sciMult = 9, spaceBorder = 2e6, blurb = "A teal and violet gas giant." },
            new() { id = "tiv", name = "Tiv", type = "moon", radius = 4.0e5, surfaceG = 2.2, rotPeriodTidal = true, orbit = new[] { 3.2e7, 0.01, 0.5, 0, 0, 2.4 }, color = 0xc9c84a, seed = 808, sciMult = 9, spaceBorder = 8e4, relief = new[] { -5000d, 7000 }, blurb = "Sulfur plains." },
            new() { id = "marn", name = "Marn", type = "moon", radius = 3.1e5, surfaceG = 2.9, rotPeriodTidal = true, orbit = new[] { 9.0e7, 0.02, 1.8, 60, 0, 0.3 }, atmo = new[] { 38000d, 5200, 0.85 }, atmoColor = new[] { 0.62, 0.70, 0.98 }, oxygen = true, color = 0x5d6a66, seed = 818, sciMult = 10, spaceBorder = 1.2e5, relief = new[] { -4500d, 8000 }, blurb = "A cold moon with real air." },
            new() { id = "slate", name = "Slate", type = "moon", radius = 2.6e5, surfaceG = 1.5, rotPeriodTidal = true, orbit = new[] { 2.3e8, 0.03, 3.0, 110, 0, 4.4 }, color = 0x3c424d, seed = 828, sciMult = 10, spaceBorder = 7e4, relief = new[] { -9000d, 8500 }, blurb = "Dark and ancient." },
            new() { id = "nock", name = "Nock", type = "moon", radius = 8.5e3, surfaceG = 0.05, rotPeriod = 21000, orbit = new[] { 6.5e8, 0.28, 31, 280, 70, 2.0 }, color = 0x3a3536, seed = 838, sciMult = 10.5, spaceBorder = 6e3, relief = new[] { -1500d, 1500 }, blurb = "A captured rock." },
            new() { id = "orrin", name = "Orrin", type = "planet", radius = 4.3e6, surfaceG = 7.2, rotPeriod = 41000, orbit = new[] { 1.3e11, 0.05, 2.4, 320, 80, 1.2 }, atmo = new[] { 350000d, 36000, 2.2 }, atmoColor = new[] { 0.95, 0.84, 0.60 }, gas = true, ring = new[] { 1.45, 2.35 }, color = 0xd9b06a, seed = 909, sciMult = 11, spaceBorder = 1.8e6, blurb = "An amber giant with bright rings." },
            new() { id = "brim", name = "Brim", type = "moon", radius = 2.4e5, surfaceG = 1.35, rotPeriodTidal = true, orbit = new[] { 2.6e7, 0.01, 0.3, 0, 0, 5.0 }, ocean = true, color = 0xd7e3ea, seed = 919, sciMult = 12, spaceBorder = 6e4, relief = new[] { -600d, 1500 }, blurb = "A cracked shell of ice." },
            new() { id = "quell", name = "Quell", type = "planet", radius = 1.4e5, surfaceG = 0.75, rotPeriod = 1, orbit = new[] { 2.3e11, 0.21, 12, 95, 110, 2.9 }, color = 0xd8b8a6, seed = 1010, sciMult = 14, spaceBorder = 5e4, relief = new[] { -5000d, 7000 }, blurb = "A far, tilted dwarf world.", rotSync = "nym" },
            new() { id = "nym", name = "Nym", type = "moon", radius = 5.5e4, surfaceG = 0.2, rotPeriodTidal = true, orbit = new[] { 3.2e6, 0d, 0, 0, 0, 0.9 }, color = 0x808086, seed = 1020, sciMult = 14, spaceBorder = 2e4, relief = new[] { -7500d, 5000 }, blurb = "Quell's companion." },
            new() { id = "wend", name = "Wend", type = "planet", radius = 6.5e3, surfaceG = 0.03, rotPeriod = 14000, comet = true, orbit = new[] { 4.4e10, 0.70, 24, 300, 40, 0.4 }, color = 0x2a2928, seed = 1111, sciMult = 13, spaceBorder = 5e3, relief = new[] { -1500d, 1500 }, blurb = "A two-lobed comet." },
        };

        var sys = new HelionSystem();
        var byId = new Dictionary<string, Body>();
        var list = new List<Body>();
        var parentOf = new Dictionary<string, string>
        {
            ["sear"] = "helion", ["cindra"] = "helion", ["murk"] = "helion", ["verda"] = "helion",
            ["pell"] = "verda", ["ost"] = "verda", ["rusk"] = "helion", ["dray"] = "rusk",
            ["thal"] = "helion", ["tiv"] = "thal", ["marn"] = "thal", ["slate"] = "thal", ["nock"] = "thal",
            ["orrin"] = "helion", ["brim"] = "orrin", ["quell"] = "helion", ["nym"] = "quell", ["wend"] = "helion",
        };
        foreach (var def in defs)
        {
            Body? parent = null;
            if (parentOf.TryGetValue(def.id, out var pid)) parent = byId[pid];
            var body = new Body(def, parent);
            list.Add(body);
            byId[body.id] = body;
        }
        // Quell turns once per Nym's orbit, facing it at t = 0.
        var quell = byId["quell"];
        var nym = byId["nym"];
        quell.rotPeriod = nym.orbit!.Period;
        var np = nym.orbit.PosAt(0);
        quell.rot0 = Math.Atan2(-np.z, np.x);

        var home = byId["verda"];
        var R = home.radius;
        LaunchSite Site(double east, double north, string? name = null, double heading = 90)
        {
            var lat = north / R; var lon = east / R;
            return new LaunchSite { lat = lat, lon = lon, dir = home.DirFromLatLon(lat, lon), name = name ?? "", heading = heading };
        }
        var pad = new LaunchSite { lat = 0, lon = 0, dir = new DVec3(1, 0, 0), name = "Starwright Flight Center", heading = 90 };
        var runway = Site(-950, -850, "Flight Center Runway", 90);
        runway.length = 2000; runway.width = 56;
        runway.start = Site(-850, -850, runway.name, 90);
        runway.center = Site(50, -850);
        home.sites = new Dictionary<string, LaunchSite> { ["pad"] = pad, ["runway"] = runway };
        home.launchSite = pad;
        sys.bodies = list.ToArray();
        sys.byId = byId;
        sys.star = byId["helion"];
        sys.home = home;
        sys.launchSite = pad;
        sys.sites = home.sites;
        return sys;
    }
}

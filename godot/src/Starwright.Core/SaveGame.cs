using System.Text.Json;

namespace Starwright.Core;

/// <summary>
/// Flight save. The browser game stores a whole career in localStorage; this milestone stores the
/// live flight (time, warp, every vessel) as JSON so a session can be resumed. Career funds,
/// science and contracts are not in this file yet.
/// </summary>
public static class SaveGame
{
    public const int Version = 1;

    public static string Write(World world)
    {
        var dto = new SaveDto
        {
            version = Version,
            ut = world.ut,
            warp = world.warpIndex,
            active = world.active?.id ?? 0,
            vessels = world.vessels.Where(v => !v.dead).Select(ToDto).ToList(),
        };
        return JsonSerializer.Serialize(dto, PartCatalog.JsonOpts());
    }

    public static World Read(string json)
    {
        var dto = JsonSerializer.Deserialize<SaveDto>(json, PartCatalog.JsonOpts())
            ?? throw new InvalidOperationException("empty save");
        var world = new World { ut = dto.ut, warpIndex = dto.warp };
        foreach (var v in dto.vessels) world.vessels.Add(FromDto(v));
        world.active = world.vessels.Find(v => v.id == dto.active) ?? world.vessels.FirstOrDefault();
        foreach (var v in world.vessels) v.UpdateTelemetry(world.ut);
        return world;
    }

    static VesselDto ToDto(Vessel v) => new()
    {
        id = v.id, name = v.name, type = v.type, body = v.body.id,
        pos = new[] { v.pos.x, v.pos.y, v.pos.z },
        vel = new[] { v.vel.x, v.vel.y, v.vel.z },
        q = new[] { v.q.x, v.q.y, v.q.z, v.q.w },
        w = new[] { v.w.x, v.w.y, v.w.z },
        landed = v.landed, launched = v.launched, situation = v.situation,
        stageIndex = v.stageIndex,
        stages = v.stages.Select(s => s.ToArray()).ToList(),
        control = v.controlId,
        throttle = v.ctrl.throttle, sas = v.ctrl.sas, sasMode = v.ctrl.sasMode, rcs = v.ctrl.rcs,
        parts = v.parts.Values.Select(p => new PartDto
        {
            id = p.id, type = p.def.id,
            pos = new[] { p.pos.x, p.pos.y, p.pos.z },
            rot = p.rot, flip = p.flip,
            links = p.links.ToArray(),
            res = new Dictionary<string, double>(p.res),
            ignited = p.ignited, flameout = p.flameout, deployed = p.deployed, temp = p.temp,
            crew = p.crew.ToArray(),
        }).ToList(),
    };

    static Vessel FromDto(VesselDto d)
    {
        var v = new Vessel
        {
            id = d.id, name = d.name, type = d.type,
            body = HelionSystem.System.byId[d.body],
            landed = d.landed, launched = d.launched, situation = d.situation,
            stageIndex = d.stageIndex, controlId = d.control,
            stages = d.stages.Select(s => s.ToArray()).ToList(),
        };
        v.pos.set(d.pos[0], d.pos[1], d.pos[2]);
        v.vel.set(d.vel[0], d.vel[1], d.vel[2]);
        v.q.set(d.q[0], d.q[1], d.q[2], d.q[3]);
        v.w.set(d.w[0], d.w[1], d.w[2]);
        v.ctrl.throttle = d.throttle; v.ctrl.sas = d.sas; v.ctrl.sasMode = d.sasMode; v.ctrl.rcs = d.rcs;
        foreach (var p in d.parts)
        {
            var part = new VPart
            {
                id = p.id, def = PartCatalog.Def(p.type),
                pos = new DVec3(p.pos[0], p.pos[1], p.pos[2]),
                rot = p.rot, flip = p.flip, ignited = p.ignited, flameout = p.flameout,
                deployed = p.deployed, temp = p.temp,
            };
            part.links.AddRange(p.links);
            foreach (var kv in p.res) part.res[kv.Key] = kv.Value;
            part.crew.AddRange(p.crew);
            v.parts[part.id] = part;
        }
        v.Rebuild();
        v.UpdateMass(false);
        if (v.landed)
        {
            var dir = v.pos.clone().normalize();
            v.body.InertialToSurfaceDir(dir, 0, dir);
            var qRel = new DQuat().setFromAxisAngle(new DVec3(0, 1, 0), -v.body.RotationAngle(0)).multiply(v.q);
            v.surface = new SurfaceAnchor { dir = dir, qRel = qRel, h = v.pos.length() - v.body.radius };
        }
        return v;
    }

    sealed class SaveDto
    {
        public int version { get; set; }
        public double ut { get; set; }
        public int warp { get; set; }
        public int active { get; set; }
        public List<VesselDto> vessels { get; set; } = new();
    }

    sealed class VesselDto
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string type { get; set; } = "ship";
        public string body { get; set; } = "verda";
        public double[] pos { get; set; } = new double[3];
        public double[] vel { get; set; } = new double[3];
        public double[] q { get; set; } = new double[4];
        public double[] w { get; set; } = new double[3];
        public bool landed { get; set; }
        public bool launched { get; set; }
        public string situation { get; set; } = "";
        public int stageIndex { get; set; }
        public int? control { get; set; }
        public double throttle { get; set; }
        public bool sas { get; set; }
        public string sasMode { get; set; } = "hold";
        public bool rcs { get; set; }
        public List<int[]> stages { get; set; } = new();
        public List<PartDto> parts { get; set; } = new();
    }

    sealed class PartDto
    {
        public int id { get; set; }
        public string type { get; set; } = "";
        public double[] pos { get; set; } = new double[3];
        public double rot { get; set; }
        public bool flip { get; set; }
        public int[] links { get; set; } = Array.Empty<int>();
        public Dictionary<string, double> res { get; set; } = new();
        public bool ignited { get; set; }
        public bool flameout { get; set; }
        public string deployed { get; set; } = "false";
        public double temp { get; set; }
        public string[] crew { get; set; } = Array.Empty<string>();
    }
}

using System.Text.Json;

namespace Starwright.Core;

public sealed class CraftPart
{
    public int id { get; set; }
    public string type { get; set; } = "";
    public VecDto pos { get; set; } = new();
    public double rot { get; set; }
    public double pitch { get; set; }
    public double roll { get; set; }
    public int? parent { get; set; }
    public string node { get; set; } = "";
    public int sym { get; set; }
    public bool flip { get; set; }
}

public sealed class VecDto
{
    public double x { get; set; }
    public double y { get; set; }
    public double z { get; set; }
}

public sealed class StageStat
{
    public int stage;
    public double dvVac, dvAtm, twr, twrVac, twrAtm, burnTime, startMass, endMass, thrust, thrustAtm, jetThrust;
}

public sealed class Interstage
{
    public int owner, engine, dir;
    public double h, r0, r1, mass;
}

/// <summary>
/// A built craft. This milestone loads the stock designs exported from js/core/craft.js
/// (exact part trees and staging) and computes the same per-stage delta-v. The interactive
/// assembly building is not ported yet.
/// </summary>
public sealed class Craft
{
    public string name = "Untitled Craft";
    public string? desc;
    public string? site;
    public List<CraftPart> parts = new();
    public List<int[]> stages = new();

    public CraftPart? Root => parts.Find(p => p.parent == null);
    public CraftPart? ControlPart => parts.Find(p => PartCatalog.Def(p.type).command != null) ?? Root;
    public CraftPart? Get(int id) => parts.Find(p => p.id == id);

    public static IReadOnlyList<Craft> Stock { get; } = LoadStock();

    public static Craft? ByName(string name) => Stock.FirstOrDefault(c => c.name == name);

    static List<Craft> LoadStock()
    {
        using var stream = typeof(Craft).Assembly.GetManifestResourceStream("stock-crafts.json")
            ?? throw new InvalidOperationException("stock-crafts.json is not embedded");
        var raw = JsonSerializer.Deserialize<List<CraftFile>>(stream, PartCatalog.JsonOpts()) ?? new();
        return raw.Select(f => f.ToCraft()).ToList();
    }

    public double RadiusAt(CraftPart p, double y)
    {
        var d = PartCatalog.Def(p.type);
        if (!d.attach.stack) return (d.rw) / 2;
        var f = d.h > 0 ? Math.Min(1, Math.Max(0, (y - (p.pos.y - d.h / 2)) / d.h)) : 0;
        if (p.flip) f = 1 - f;
        return d.rBottom + (d.rTop - d.rBottom) * f;
    }

    public List<Interstage> Interstages()
    {
        var outp = new List<Interstage>();
        foreach (var e in parts)
        {
            var d = PartCatalog.Def(e.type);
            if (!PartCatalog.WantsInterstage(d) || e.pitch != 0 || e.roll != 0) continue;
            var side = e.flip ? "top" : "bottom";
            CraftPart? n = parts.Find(c => c.parent == e.id && c.node == side);
            if (n == null && e.parent != null && e.node == (side == "bottom" ? "top" : "bottom")) n = Get(e.parent.Value);
            if (n == null) continue;
            var nd = PartCatalog.Def(n.type);
            if (!nd.attach.stack || n.pitch != 0 || n.roll != 0) continue;
            if (Math.Abs(n.pos.x - e.pos.x) > 1e-3 || Math.Abs(n.pos.z - e.pos.z) > 1e-3) continue;
            var dir = side == "bottom" ? 1 : -1;
            var y0 = n.pos.y + dir * nd.h / 2;
            var y1 = e.pos.y + dir * d.h / 2;
            var r0 = RadiusAt(n, y0);
            var r1 = RadiusAt(e, y1);
            if (!(r0 > 0.05) || !(r1 > 0.05)) continue;
            outp.Add(new Interstage { owner = n.id, engine = e.id, dir = dir, h = d.h, r0 = r0, r1 = r1, mass = PartCatalog.InterstageMass(d.h, r0, r1) });
        }
        return outp;
    }

    public double Mass()
    {
        var m = Interstages().Sum(t => t.mass);
        foreach (var p in parts)
        {
            var d = PartCatalog.Def(p.type);
            m += d.mass * 1000;
            foreach (var kv in d.res) if (kv.Key != "elec") m += kv.Value;
        }
        return m;
    }

    public double Cost() => parts.Sum(p => PartCatalog.Def(p.type).cost);

    Dictionary<int, List<int>> Adjacency(HashSet<int>? ids)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var p in parts)
            if (ids == null || ids.Contains(p.id)) adj[p.id] = new List<int>();
        foreach (var p in parts)
        {
            if (p.parent == null || !adj.ContainsKey(p.id) || !adj.ContainsKey(p.parent.Value)) continue;
            adj[p.id].Add(p.parent.Value);
            adj[p.parent.Value].Add(p.id);
        }
        return adj;
    }

    public Dictionary<int, int> Sections(HashSet<int>? alive = null)
    {
        var adj = Adjacency(alive);
        var sec = new Dictionary<int, int>();
        var next = 0;
        foreach (var p in parts)
        {
            if (!adj.ContainsKey(p.id) || sec.ContainsKey(p.id)) continue;
            if (PartCatalog.Def(p.type).decoupler != null) { sec[p.id] = -1; continue; }
            var id = next++;
            sec[p.id] = id;
            var queue = new Queue<int>();
            queue.Enqueue(p.id);
            while (queue.Count > 0)
            {
                foreach (var n in adj[queue.Dequeue()])
                {
                    if (sec.ContainsKey(n) || PartCatalog.Def(Get(n)!.type).decoupler != null) continue;
                    sec[n] = id;
                    queue.Enqueue(n);
                }
            }
        }
        return sec;
    }

    static double ResMass(PartDef d)
    {
        double m = 0;
        foreach (var kv in d.res) if (kv.Key != "elec") m += kv.Value;
        return m;
    }

    public List<StageStat> Stats(Body? body = null)
    {
        var g = body != null && body.surfaceG > 0 ? body.surfaceG : 9.6;
        var pSea = body != null ? body.PressureAtm(0) : 1;
        var control = ControlPart;
        var outp = new List<StageStat>();
        if (control == null) return outp;
        var defs = new Dictionary<int, PartDef>();
        var fuel = new Dictionary<int, double>();
        var solid = new Dictionary<int, double>();
        var along = new Dictionary<int, double>();
        var alive = new HashSet<int>();
        foreach (var p in parts)
        {
            var d = PartCatalog.Def(p.type);
            defs[p.id] = d;
            alive.Add(p.id);
            if (d.engine != null) along[p.id] = 1;
            if (d.res.TryGetValue("fuel", out var f)) fuel[p.id] = f;
            if (d.res.TryGetValue("xenon", out var x)) fuel[-p.id] = x;
            if (d.res.TryGetValue("solid", out var s)) solid[p.id] = s;
        }
        var truss = new Dictionary<int, double>();
        foreach (var t in Interstages()) truss[t.owner] = (truss.TryGetValue(t.owner, out var m0) ? m0 : 0) + t.mass;
        double MassOf()
        {
            double m = 0;
            foreach (var id in alive)
            {
                var d = defs[id];
                m += (truss.TryGetValue(id, out var tm) ? tm : 0) + d.mass * 1000 + ResMass(d)
                    - (d.res.TryGetValue("fuel", out var rf) ? rf : 0)
                    - (d.res.TryGetValue("solid", out var rs) ? rs : 0)
                    - (d.res.TryGetValue("xenon", out var rx) ? rx : 0)
                    + (fuel.TryGetValue(id, out var ff) ? ff : 0)
                    + (fuel.TryGetValue(-id, out var fx) ? fx : 0)
                    + (solid.TryGetValue(id, out var fs) ? fs : 0);
            }
            return m;
        }
        HashSet<int> AfterDecouple(HashSet<int> set, int[] stage)
        {
            var firing = stage.Where(id => set.Contains(id) && defs[id].decoupler != null).ToList();
            if (firing.Count == 0) return set;
            var rest = new HashSet<int>(set);
            foreach (var id in firing) rest.Remove(id);
            var adj = Adjacency(rest);
            var keep = new HashSet<int> { control.id };
            var queue = new Queue<int>();
            queue.Enqueue(control.id);
            while (queue.Count > 0)
            {
                if (!adj.TryGetValue(queue.Dequeue(), out var ns)) continue;
                foreach (var n in ns) if (keep.Add(n)) queue.Enqueue(n);
            }
            return keep;
        }
        var ignited = new HashSet<int>();
        var lifted = false;
        for (var si = 0; si < stages.Count; si++)
        {
            var stage = stages[si];
            alive = AfterDecouple(alive, stage);
            foreach (var id in stage) if (alive.Contains(id) && defs[id].engine != null) ignited.Add(id);
            var next = si + 1 < stages.Count ? stages[si + 1] : null;
            var survivors = next != null ? AfterDecouple(alive, next) : alive;
            var startMass = MassOf();
            var mass = startMass;
            double dvVac = 0, dvAtm = 0, burnTime = 0, thrust = 0, thrustAtm = 0, jetThrust = 0;
            var first = true;
            foreach (var id in ignited)
                if (alive.Contains(id) && defs[id].engine!.type == "jet")
                    jetThrust += defs[id].engine!.thrust * 1000 * Math.Max(0, along[id]);
            for (var guard = 0; guard < 10000; guard++)
            {
                var sec = Sections(alive);
                var tanksBySec = new Dictionary<int, List<int>>();
                var xenonBySec = new Dictionary<int, List<int>>();
                foreach (var id in alive)
                {
                    var s = sec[id];
                    if (fuel.TryGetValue(id, out var ff) && ff > 1e-9)
                    {
                        if (!tanksBySec.TryGetValue(s, out var list)) tanksBySec[s] = list = new();
                        list.Add(id);
                    }
                    if (fuel.TryGetValue(-id, out var fx) && fx > 1e-9)
                    {
                        if (!xenonBySec.TryGetValue(s, out var list)) xenonBySec[s] = list = new();
                        list.Add(-id);
                    }
                }
                var eng = new List<(int id, PartDef def, double mdot, List<int>? tanks)>();
                foreach (var id in ignited)
                {
                    if (!alive.Contains(id)) continue;
                    var d = defs[id];
                    var mdot = d.engine!.thrust * 1000 / (d.engine.ispVac * PartCatalog.G0);
                    if (d.engine.type == "jet") continue;
                    if (d.engine.type == "solid") { if (solid.TryGetValue(id, out var ss) && ss > 1e-9) eng.Add((id, d, mdot, null)); }
                    else
                    {
                        var map = d.engine.type == "ion" ? xenonBySec : tanksBySec;
                        if (map.TryGetValue(sec[id], out var tanks)) eng.Add((id, d, mdot, tanks));
                    }
                }
                if (first)
                {
                    foreach (var e in eng)
                    {
                        var k = along[e.id];
                        thrust += PartCatalog.EngineThrust(e.def, 0) * k;
                        thrustAtm += PartCatalog.EngineThrust(e.def, pSea) * k;
                    }
                    thrust = Math.Max(0, thrust); thrustAtm = Math.Max(0, thrustAtm);
                    first = false;
                }
                if (eng.Count == 0) break;
                var doomed = eng.Where(e => !survivors.Contains(e.id)).ToList();
                var everDoomed = ignited.Any(id => alive.Contains(id) && !survivors.Contains(id));
                if (everDoomed && doomed.Count == 0) break;
                var drain = new Dictionary<int, double>();
                var drainSolid = new Dictionary<int, double>();
                double fVac = 0, fAtm = 0, mdotSum = 0, dt = double.PositiveInfinity;
                foreach (var e in eng)
                {
                    var k = along[e.id];
                    fVac += PartCatalog.EngineThrust(e.def, 0) * k;
                    fAtm += PartCatalog.EngineThrust(e.def, pSea) * k;
                    mdotSum += e.mdot;
                    if (e.tanks == null)
                    {
                        drainSolid[e.id] = e.mdot;
                        dt = Math.Min(dt, solid[e.id] / e.mdot);
                    }
                    else foreach (var tank in e.tanks) drain[tank] = (drain.TryGetValue(tank, out var r) ? r : 0) + e.mdot / e.tanks.Count;
                }
                foreach (var kv in drain) dt = Math.Min(dt, fuel[kv.Key] / kv.Value);
                foreach (var kv in drain)
                {
                    var left = fuel[kv.Key] - kv.Value * dt;
                    fuel[kv.Key] = left < 1e-6 ? 0 : left;
                }
                foreach (var kv in drainSolid)
                {
                    var left = solid[kv.Key] - kv.Value * dt;
                    solid[kv.Key] = left < 1e-6 ? 0 : left;
                }
                var m1 = MassOf();
                var ln = Math.Log(mass / m1);
                dvVac += Math.Max(0, fVac) / mdotSum * ln;
                dvAtm += Math.Max(0, fAtm) / mdotSum * ln;
                burnTime += dt;
                mass = m1;
            }
            var twrVac = thrust / (startMass * g);
            var twrAtm = (thrustAtm + jetThrust) / (startMass * g);
            var liftoff = !lifted && (thrust > 0 || jetThrust > 0);
            if (liftoff) lifted = true;
            outp.Add(new StageStat
            {
                stage = si, dvVac = dvVac, dvAtm = dvAtm,
                twr = liftoff ? twrAtm : twrVac, twrVac = twrVac, twrAtm = twrAtm,
                burnTime = burnTime, startMass = startMass, endMass = mass,
                thrust = thrust, thrustAtm = thrustAtm, jetThrust = jetThrust,
            });
        }
        return outp;
    }

    public (double vac, double atm) DeltaV(Body? body = null)
    {
        double vac = 0, atm = 0;
        foreach (var s in Stats(body)) { vac += s.dvVac; atm += s.dvAtm; }
        return (vac, atm);
    }

    sealed class CraftFile
    {
        public string name { get; set; } = "";
        public string? desc { get; set; }
        public string? site { get; set; }
        public List<CraftPart> parts { get; set; } = new();
        public List<int[]> stages { get; set; } = new();
        public Craft ToCraft() => new() { name = name, desc = desc, site = site, parts = parts, stages = stages };
    }
}

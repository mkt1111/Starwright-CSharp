namespace Starwright.Core;

public static class Tuning
{
    public const double DtMax = 0.02;
    public const double DtCoast = 0.1;
    public const double DecoupleImpulse = 250;
    public const double HeatFluxK = 1.83e-4;
    public const double HeatK = 0.30;
    public const double HeatExp = 0.75;
    public const double HeatTau = 22;
    public const double CoolTau = 30;
    public const double HeatMinFlux = 1500;
    public const double SideExposure = 0.35;
    public const double RadialExposure = 0.6;
    public const double ShieldFactor = 0.3;
    public const double AblateRate = 6e-6;
    public const double AmbientT = 290;
    public const double BaseDrag = 0.25;
    public const double MachPeak = 2.0;
    public const double MachHigh = 1.5;
    public const double FaceCp = 0.25;
    public const double FaceDamp = 4;
    public const double ChuteCd = 1.6;
    public const double ChuteMinPressure = 0.02;
    public const double ChuteSemiTime = 3.0;
    public const double ChuteFullTime = 2.5;
    public const double ChuteRip = 1.5;
    public const double FinLift = 1.6;
    public const double WingSlope = 3.6;
    public const double WingStall = 0.28;
    public const double FinStall = 0.45;
    public const double WingCd0 = 0.012;
    public const double WingK = 0.07;
    public const double WingPlate = 0.9;
    public const double WingPlateCd = 1.8;
    public const double CtrlDeflect = 0.35;
    public const double CtrlTau = 0.55;
    public const double WheelEc = 0.02;
    public const double SoundSpeed = 320;
    public const double CrashAll = 2.2;
    public const double WaterTol = 1.5;
    public const double SasKp = 1.5;
    public const double SasWMax = 0.9;
    public const double SasT = 0.15;
    public const double MaxAngAcc = 2.0;
    public const double MaxSpin = 12;
    public const double GroundMu = 0.8;
}

public sealed class FlightCtrl
{
    public double pitch, yaw, roll, throttle, tx, ty, tz;
    public bool sas, rcs, brake;
    public string sasMode = "hold";
}

public sealed class SurfaceAnchor
{
    public DVec3 dir = new(1, 0, 0);
    public DQuat qRel = new();
    public double h;
}

public sealed class SimEvent
{
    public string type = "";
    public Vessel? vessel;
    public Vessel? debris;
    public VPart? part;
    public string? reason;
    public string? state;
    public Body? fromBody;
    public Body? toBody;
    public string? text;
}

public sealed class VPart
{
    public int id;
    public PartDef def = null!;
    public DVec3 pos = new();
    public double rot;
    public bool flip;
    public readonly List<int> links = new();
    public Dictionary<string, double> res = new();
    public bool ignited, flameout, destroyed;
    public string deployed = "false";
    public double deployT, temp = Tuning.AmbientT, mass, thrust, level;
    public readonly List<string> crew = new();
    public bool stack;
    public double rT, rB, aUp, aDown, aSide, aRad, sUp, sDown, eUp, eDown, ox, oz;
    public double wArea, wSlope, wStall, defl;
    public string? wCtl;
    public bool Has(string key) => res.TryGetValue(key, out var v) && v > 0;
    public bool DeployedBool => deployed == "true";
}

public sealed class EngineLink
{
    public VPart part = null!;
    public List<VPart> tanks = new();
    public string prop = "fuel";
}

/// <summary>
/// Live vessel. This milestone ports the rocket flight model from js/core/vessel.js:
/// thrust, fuel, staging, separation, stack drag, fin lift, heating, parachutes, SAS,
/// reaction wheels and a sphere-ground landing. Wheels, wing-borne flight, docking and EVA
/// are not in this build.
/// </summary>
public sealed class Vessel
{
    public int id;
    public string name = "Vessel";
    public string type = "ship";
    public readonly Dictionary<int, VPart> parts = new();
    public List<int[]> stages = new();
    public int stageIndex;
    public int? controlId;
    public Body body = HelionSystem.System.home;
    public readonly DVec3 pos = new();
    public readonly DVec3 vel = new();
    public readonly DQuat q = new();
    public readonly DVec3 w = new();
    public bool landed, splashed, launched, dead, onRails;
    public SurfaceAnchor? surface;
    public readonly FlightCtrl ctrl = new();
    public DQuat? sasTarget;
    public double mass, thrust, maxThrust, isp, altitude, radarAlt, airspeed, verticalSpeed, orbitalSpeed;
    public double dynamicPressure, mach, gForce, apoapsis, periapsis, period = double.PositiveInfinity;
    public double deltaV, stageDeltaV;
    public string situation = "prelaunch";
    public readonly DVec3 com = new();
    public readonly DVec3 inertia = new(1, 1, 1);
    public readonly DVec3 surfaceVel = new();
    public Orbit? orbit;

    public List<VPart> PartList => _list;
    public IEnumerable<VPart> Engines => _engines.Select(e => e.part);

    readonly List<VPart> _list = new();
    readonly List<EngineLink> _engines = new();
    readonly List<VPart> _chutes = new();
    readonly List<VPart> _wings = new();
    readonly List<VPart> _commands = new();
    readonly List<VPart> _solar = new();
    readonly List<(VPart p, double x, double y, double z, double r)> _cp = new();
    readonly Dictionary<string, List<VPart>> _res = new();
    double _wheel, _gim, _mdot, _thrTx, _thrTy, _thrTz, _tqx, _tqy, _tqz, _thrFx, _thrFz;
    double _minY, _baseH, _baseR, _bound = 1;
    bool _massDirty, _hot, _sasCoast;
    public readonly List<InterstageCarry> trusses = new();

    public sealed class InterstageCarry
    {
        public int owner;
        public double side, h, mass;
    }

    public static Vessel FromCraft(Craft craft, IReadOnlyList<string>? crew = null)
    {
        var v = new Vessel { name = craft.name };
        foreach (var p in craft.parts)
        {
            var def = PartCatalog.Def(p.type);
            var q = new VPart
            {
                id = p.id, def = def, pos = new DVec3(p.pos.x, p.pos.y, p.pos.z), rot = p.rot, flip = p.flip,
                deployed = def.chute != null ? "stowed" : def.wheel != null ? "true" : "false",
            };
            foreach (var kv in def.res) q.res[kv.Key] = kv.Value;
            v.parts[p.id] = q;
        }
        foreach (var t in craft.Interstages())
            v.trusses.Add(new InterstageCarry { owner = t.owner, side = t.dir, h = t.h, mass = t.mass });
        foreach (var p in craft.parts)
        {
            if (p.parent == null || !v.parts.ContainsKey(p.parent.Value)) continue;
            v.parts[p.id].links.Add(p.parent.Value);
            v.parts[p.parent.Value].links.Add(p.id);
        }
        v.stages = craft.stages.Select(s => s.ToArray()).ToList();
        var cp = craft.ControlPart;
        v.controlId = cp?.id ?? (craft.parts.Count > 0 ? craft.parts[0].id : null);
        var names = crew?.ToList() ?? new List<string>();
        foreach (var p in v.parts.Values.Where(p => p.def.command == "crew"))
            while (p.crew.Count < p.def.crew && names.Count > 0) { p.crew.Add(names[0]); names.RemoveAt(0); }
        v.Rebuild();
        v.UpdateMass(false);
        return v;
    }

    public VPart? Control => controlId is int id && parts.TryGetValue(id, out var p) ? p : null;

    public void Rebuild()
    {
        _list.Clear(); _list.AddRange(parts.Values);
        _engines.Clear(); _chutes.Clear(); _wings.Clear(); _commands.Clear(); _solar.Clear(); _res.Clear(); _cp.Clear();
        _wheel = 0; _minY = double.PositiveInfinity;
        foreach (var p in _list)
        {
            var d = p.def;
            p.stack = d.attach.stack;
            if (d.attach.stack) { p.rT = p.flip ? d.rBottom : d.rTop; p.rB = p.flip ? d.rTop : d.rBottom; }
            p.links.RemoveAll(id => !parts.ContainsKey(id));
            foreach (var k in p.res.Keys)
            {
                if (!_res.TryGetValue(k, out var list)) _res[k] = list = new();
                list.Add(p);
            }
            if (d.torque != 0) _wheel += d.torque * 1000;
            if (d.command != null) _commands.Add(p);
            if (d.chute != null) _chutes.Add(p);
            if (d.solar != null) _solar.Add(p);
            if (d.wing != null || d.fin != null)
            {
                var wg = d.wing ?? d.fin!;
                p.wArea = wg.area;
                p.wSlope = d.wing != null ? (wg.slope > 0 ? wg.slope : Tuning.WingSlope) : Tuning.FinLift;
                p.wStall = d.wing != null ? (wg.stall > 0 ? wg.stall : Tuning.WingStall) : Tuning.FinStall;
                p.wCtl = wg.ControlKind;
                _wings.Add(p);
            }
            p.ox = Math.Cos(p.rot); p.oz = -Math.Sin(p.rot);
            if (p.stack)
            {
                double coverTop = 0, coverBot = 0;
                foreach (var id in p.links)
                {
                    var n = parts[id];
                    if (!n.def.attach.stack) continue;
                    if (Math.Abs(n.pos.x - p.pos.x) > 1e-3 || Math.Abs(n.pos.z - p.pos.z) > 1e-3) continue;
                    if (n.pos.y > p.pos.y) coverTop = Math.Max(coverTop, n.flip ? n.def.rTop : n.def.rBottom);
                    else coverBot = Math.Max(coverBot, n.flip ? n.def.rBottom : n.def.rTop);
                }
                var rT = p.rT; var rB = p.rB;
                var dragT = p.flip ? d.drag.bottom : d.drag.top;
                var dragB = p.flip ? d.drag.top : d.drag.bottom;
                var aUp = Math.PI * Math.Max(0, rT * rT - coverTop * coverTop);
                var aDown = Math.PI * Math.Max(0, rB * rB - coverBot * coverBot);
                p.sUp = aUp * dragT; p.sDown = aDown * dragB;
                if (rB > rT) { var fl = Math.PI * (rB * rB - rT * rT); aUp += fl; p.sUp -= fl * dragT; }
                else if (rT > rB) { var fl = Math.PI * (rT * rT - rB * rB); aDown += fl; p.sDown -= fl * dragB; }
                p.aUp = aUp * dragT; p.aDown = aDown * dragB; p.aSide = (rT + rB) * d.h * d.drag.side;
                var rM = Math.Max(rT, rB);
                var refer = 0.5 * Math.PI * rM * rM;
                p.eUp = refer > 0 ? Math.Min(1, aUp / refer) : 0;
                p.eDown = refer > 0 ? Math.Min(1, aDown / refer) : 0;
                var yb = p.pos.y - d.h / 2;
                _cp.Add((p, p.pos.x, yb, p.pos.z, rB));
                _cp.Add((p, p.pos.x, p.pos.y + d.h / 2, p.pos.z, rT));
                if (yb < _minY) _minY = yb;
            }
            else
            {
                p.aRad = (d.wing != null || d.fin != null) ? 0 : d.h * Math.Max(d.t, 0.1) * 0.8;
                var r = Math.Max(0.05, Math.Max(d.rw, d.t) * 0.25);
                _cp.Add((p, p.pos.x, p.pos.y, p.pos.z, r));
                if (p.pos.y < _minY) _minY = p.pos.y;
            }
        }
        foreach (var p in _list)
        {
            if (p.def.engine == null) continue;
            var tanks = new List<VPart>();
            var prop = PartCatalog.EngineProp(p.def);
            if (p.def.engine.type != "solid")
            {
                var seen = new HashSet<int> { p.id };
                var queue = new Queue<VPart>();
                queue.Enqueue(p);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    if (cur.def.res.ContainsKey(prop)) tanks.Add(cur);
                    foreach (var id in cur.links)
                    {
                        if (!seen.Add(id)) continue;
                        var n = parts[id];
                        if (n.def.decoupler != null) continue;
                        queue.Enqueue(n);
                    }
                }
            }
            _engines.Add(new EngineLink { part = p, tanks = tanks, prop = prop });
        }
        if (controlId is not int cid || !parts.ContainsKey(cid))
            controlId = _commands.FirstOrDefault()?.id ?? _list.FirstOrDefault()?.id;
    }

    public void UpdateMass(bool shift)
    {
        double m = 0, cx = 0, cy = 0, cz = 0;
        foreach (var p in _list)
        {
            var pm = p.def.mass * 1000;
            foreach (var kv in p.res) if (kv.Key != "elec") pm += kv.Value;
            foreach (var t in trusses) if (t.owner == p.id)
            {
                pm += t.mass;
                cy += t.mass * t.side * (p.flip ? -1 : 1) * (p.def.h + t.h) / 2;
            }
            p.mass = pm;
            m += pm; cx += pm * p.pos.x; cy += pm * p.pos.y; cz += pm * p.pos.z;
            if (p.def.comY != 0)
            {
                var ay = p.flip ? -1 : 1;
                cy += pm * p.def.comY * ay;
            }
        }
        if (!(m > 0)) { mass = 0; return; }
        cx /= m; cy /= m; cz /= m;
        double ix = 0, iy = 0, iz = 0;
        foreach (var p in _list)
        {
            var d = p.def; var pm = p.mass;
            var dx = p.pos.x - cx; var dy = p.pos.y - cy; var dz = p.pos.z - cz;
            var r = p.stack ? Math.Max(d.rTop, d.rBottom) : Math.Max(d.rw, 0.2) / 2;
            var own = pm * (3 * r * r + d.h * d.h) / 12;
            ix += pm * (dy * dy + dz * dz) + own;
            iz += pm * (dy * dy + dx * dx) + own;
            iy += pm * (dx * dx + dz * dz) + pm * r * r / 2;
        }
        if (shift && !landed && (cx != com.x || cy != com.y || cz != com.z))
        {
            var a = new DVec3(cx - com.x, cy - com.y, cz - com.z);
            var b = new DVec3().crossVectors(w, a).applyQuaternion(q);
            vel.add(b);
            pos.add(a.applyQuaternion(q));
        }
        com.set(cx, cy, cz);
        mass = m;
        var floor = m * 0.02;
        inertia.set(Math.Max(ix, floor), Math.Max(iy, floor), Math.Max(iz, floor));
        _baseH = com.y - _minY;
        _bound = 1; _baseR = 0;
        foreach (var c in _cp)
        {
            _bound = Math.Max(_bound, Math.Sqrt((c.x - cx) * (c.x - cx) + (c.y - cy) * (c.y - cy) + (c.z - cz) * (c.z - cz)) + c.r);
            if (c.y <= _minY + 0.15) _baseR = Math.Max(_baseR, Math.Sqrt((c.x - cx) * (c.x - cx) + (c.z - cz) * (c.z - cz)) + c.r);
        }
        _bound += 0.5;
        _massDirty = false;
    }

    double Total(string k)
    {
        if (!_res.TryGetValue(k, out var list)) return 0;
        double t = 0;
        foreach (var p in list) if (p.res.TryGetValue(k, out var v)) t += v;
        return t;
    }

    double Drain(string k, double amount)
    {
        if (amount <= 0 || !_res.TryGetValue(k, out var list)) return 0;
        double have = 0;
        foreach (var p in list) if (p.res.TryGetValue(k, out var v)) have += v;
        var take = Math.Min(have, amount);
        var left = take;
        foreach (var p in list)
        {
            if (left <= 0) break;
            if (!p.res.TryGetValue(k, out var v) || v <= 0) continue;
            var d = Math.Min(v, left);
            p.res[k] = v - d;
            left -= d;
        }
        return take;
    }

    public bool WouldThrust()
    {
        foreach (var e in _engines)
        {
            var p = e.part;
            if (!p.ignited || p.flameout) continue;
            if (p.def.engine!.type == "solid") { if (p.res.GetValueOrDefault("solid") > 0) return true; continue; }
            if (ctrl.throttle > 0 && e.tanks.Any(t => t.res.GetValueOrDefault(e.prop) > 0)) return true;
        }
        return false;
    }

    public List<SimEvent> ActivateNextStage()
    {
        var ev = new List<SimEvent>();
        if (stageIndex >= stages.Count) return ev;
        var si = stageIndex++;
        var ids = stages[si].ToArray();
        ev.Add(new SimEvent { type = "stage", vessel = this });
        foreach (var id in ids)
            if (parts.TryGetValue(id, out var p) && p.def.decoupler != null) Decouple(p, ev);
        foreach (var id in ids)
        {
            if (!parts.TryGetValue(id, out var p)) continue;
            if (p.def.engine != null && !p.ignited) p.ignited = true;
            else if (p.def.chute != null && p.deployed == "stowed")
            {
                p.deployed = "armed";
                ev.Add(new SimEvent { type = "chute", vessel = this, part = p, state = "armed" });
            }
        }
        return ev;
    }

    void Decouple(VPart dec, List<SimEvent> ev)
    {
        if (controlId is not int cid) return;
        var keep = Component(cid, new HashSet<int> { dec.id });
        if (keep.Count == 0) return;
        foreach (var id in dec.links.ToArray())
        {
            if (!keep.Contains(id)) continue;
            var n = parts[id];
            n.links.Remove(dec.id);
            dec.links.Remove(id);
        }
        var ids = Component(dec.id, null);
        var keepCom = ComOf(keep);
        var dropCom = ComOf(ids);
        var debris = SplitOff(ids);
        var J = dec.def.decoupler!.force * Tuning.DecoupleImpulse;
        var a = dec.def.attach.outer ? new DVec3(dec.ox, 0, dec.oz) : new DVec3(0, dropCom.y >= keepCom.y ? 1 : -1, 0);
        a.applyQuaternion(q);
        if (debris.mass > 0) debris.vel.addScaledVector(a, J / debris.mass);
        if (mass > 0 && !landed) vel.addScaledVector(a, -J / mass);
        ev.Add(new SimEvent { type = "decouple", vessel = this, debris = debris, part = dec });
    }

    DVec3 ComOf(HashSet<int> ids)
    {
        double m = 0; var c = new DVec3();
        foreach (var id in ids)
        {
            var p = parts[id];
            m += p.mass;
            c.addScaledVector(p.pos, p.mass);
        }
        return m > 0 ? c.multiplyScalar(1 / m) : c;
    }

    HashSet<int> Component(int start, HashSet<int>? blocked)
    {
        var seen = new HashSet<int>();
        if (!parts.ContainsKey(start) || (blocked != null && blocked.Contains(start))) return seen;
        var queue = new Queue<int>();
        queue.Enqueue(start); seen.Add(start);
        while (queue.Count > 0)
        {
            foreach (var id in parts[queue.Dequeue()].links)
            {
                if (seen.Contains(id) || (blocked != null && blocked.Contains(id)) || !parts.ContainsKey(id)) continue;
                seen.Add(id); queue.Enqueue(id);
            }
        }
        return seen;
    }

    Vessel SplitOff(HashSet<int> ids)
    {
        var d = new Vessel { body = body, launched = true, name = name + " debris", type = "debris" };
        foreach (var id in ids.ToArray())
        {
            var p = parts[id];
            parts.Remove(id);
            d.parts[id] = p;
            if (p.def.command != null) d.type = "ship";
        }
        if (d.type == "ship") d.name = name + " probe";
        d.stages = stages.Select(s => s.Where(d.parts.ContainsKey).ToArray()).ToList();
        d.stageIndex = Math.Min(stageIndex, d.stages.Count);
        d.Rebuild();
        d.UpdateMass(false);
        var old = com.clone();
        d.q.copy(q); d.w.copy(w);
        var b = d.com.clone().sub(old);
        d.pos.copy(b).applyQuaternion(q).add(pos);
        d.vel.crossVectors(w, b).applyQuaternion(q).add(vel);
        d.situation = situation;
        Rebuild();
        UpdateMass(true);
        return d;
    }

    public void SetLegs(bool down) { foreach (var p in _list) if (p.def.legs != null) p.deployed = down ? "true" : "false"; }
    public void ToggleLegs() => SetLegs(!_list.Any(p => p.def.legs != null && p.DeployedBool));
    public void SetSolar(bool o) { foreach (var p in _solar) if (p.def.solar?.@fixed != true) p.deployed = o ? "true" : "false"; }
    public void ToggleSolar() => SetSolar(!_solar.Any(p => p.DeployedBool));
    public void ToggleSas() { ctrl.sas = !ctrl.sas; if (ctrl.sas && ctrl.sasMode == "hold") sasTarget = null; }

    public void Simulate(double dt, double ut, List<SimEvent> ev)
    {
        if (dead || _list.Count == 0) return;
        if (landed) StepLanded(dt, ut, ev);
        else StepFlight(dt, ut, ev);
        if (_massDirty && !dead) UpdateMass(true);
    }

    void StepLanded(double dt, double ut, List<SimEvent> ev)
    {
        var s = surface!;
        var R = body.radius + s.h;
        var thrust = Systems(dt, body.PressureAtm(s.h), ev);
        var qi = s.qRel.clone().invert();
        var ul = s.dir.clone().applyQuaternion(qi);
        var g = body.gm / (R * R);
        var lift = thrust * ul.y;
        if (thrust > 0 && lift > mass * g) { Release(ut, ev); return; }
        if (thrust > 0)
        {
            var side = thrust * Math.Sqrt(Math.Max(0, 1 - ul.y * ul.y));
            if (side > Tuning.GroundMu * Math.Max(0, mass * g - lift)) { Release(ut, ev); return; }
        }
        SyncLanded(ut);
    }

    void Release(double ut, List<SimEvent> ev)
    {
        SyncLanded(ut);
        landed = false; splashed = false; surface = null; sasTarget = null;
        if (!launched) { launched = true; ev.Add(new SimEvent { type = "liftoff", vessel = this }); }
    }

    public void SyncLanded(double ut)
    {
        var s = surface;
        if (s == null) return;
        var qi = s.qRel.clone().invert();
        var ul = s.dir.clone().applyQuaternion(qi);
        var low = Lowest(ul);
        var gh = body.SurfaceHeight(s.dir);
        s.h = gh - low.h;
        if (!(s.h > -1e6 && s.h < 1e6)) s.h = 0;
        body.SurfaceDirToInertial(s.dir, ut, pos).multiplyScalar(body.radius + s.h);
        var wy = body.AngularRate;
        vel.set(wy * pos.z, 0, -wy * pos.x);
        var spin = new DQuat().setFromAxisAngle(new DVec3(0, 1, 0), body.RotationAngle(ut));
        q.copy(spin).multiply(s.qRel).normalize();
        w.set(0, 0, 0);
    }

    (double h, VPart? part) Lowest(DVec3 upLocal)
    {
        var s = Math.Sqrt(Math.Max(0, 1 - upLocal.y * upLocal.y));
        double h = double.PositiveInfinity; VPart? part = null;
        foreach (var c in _cp)
        {
            var v = (c.x - com.x) * upLocal.x + (c.y - com.y) * upLocal.y + (c.z - com.z) * upLocal.z - c.r * s;
            if (v < h) { h = v; part = c.p; }
        }
        return (h, part);
    }

    void StepFlight(double dt, double ut, List<SimEvent> ev)
    {
        var R = pos.length();
        var alt = R - body.radius;
        var rho = body.Density(alt);
        var pAtm = body.PressureAtm(alt);
        var wy = body.AngularRate;
        var sx = vel.x - wy * pos.z; var sy = vel.y; var sz = vel.z + wy * pos.x;
        var speed2 = sx * sx + sy * sy + sz * sz;
        var speed = Math.Sqrt(speed2);
        var qdyn = 0.5 * rho * speed2;
        var qi = q.clone().invert();
        var ul = new DVec3(sx, sy, sz).applyQuaternion(qi);
        var ux = ul.x; var uy = ul.y; var uz = ul.z;

        var thrustF = Systems(dt, pAtm, ev);
        ControlStep(dt, qdyn);
        var m = mass;

        double Fx = _thrFx, Fy = thrustF, Fz = _thrFz;
        double Tx = _thrTx + _tqx, Ty = _thrTy + _tqy, Tz = _thrTz + _tqz;

        if (_chutes.Count > 0) UpdateChutes(dt, pAtm, speed, alt - _baseH, ev);

        double Ax = 0, Ay = 0, Az = 0, ATx = 0, ATy = 0, ATz = 0;
        var flux = rho > 0 ? Tuning.HeatFluxK * Math.Sqrt(rho) * speed2 * speed : 0;
        var heating = flux > Tuning.HeatMinFlux;
        List<int>? overheated = null;
        if (rho > 1e-10 && speed2 > 0.01)
        {
            var machN = speed / Tuning.SoundSpeed;
            var mf = machN <= 0.7 ? 1 : machN < 1.1 ? 1 + (machN - 0.7) * 2.5 * (Tuning.MachPeak - 1)
                : machN < 2.5 ? Tuning.MachPeak + (machN - 1.1) / 1.4 * (Tuning.MachHigh - Tuning.MachPeak) : Tuning.MachHigh;
            var uAx = speed > 0 ? uy / speed : 0;
            var lat0 = Math.Sqrt(Math.Max(0, 1 - uAx * uAx));
            foreach (var p in _list)
            {
                var d = p.def;
                var rx = p.pos.x - com.x; var ry = p.pos.y - com.y; var rz = p.pos.z - com.z;
                var vx = ux + (w.y * rz - w.z * ry);
                var vy = uy + (w.z * rx - w.x * rz);
                var vz = uz + (w.x * ry - w.y * rx);
                var v2 = vx * vx + vy * vy + vz * vz;
                if (v2 < 1e-6) continue;
                var sp = Math.Sqrt(v2);
                var qd = 0.5 * rho * v2;
                var qdm = qd * mf;
                double fx = 0, fy = 0, fz = 0;
                if (p.stack)
                {
                    var ca = vy / sp;
                    fy = ca > 0 ? -qdm * (p.aUp + Tuning.BaseDrag * p.aDown) * ca : -qdm * (p.aDown + Tuning.BaseDrag * p.aUp) * ca;
                    ATx -= rz * fy; ATz += rx * fy;
                    var rr = d.rTop > d.rBottom ? d.rTop : d.rBottom;
                    var kR = Tuning.FaceCp * qdm * (ca > 0 ? p.sUp : -p.sDown) * Math.Abs(ca) * rr / sp;
                    var kD = Tuning.FaceDamp * qdm * (p.aUp + p.aDown) * rr * rr / sp;
                    ATx += kR * vz - kD * w.x; ATz -= kR * vx + kD * w.z;
                    var k = qdm * p.aSide / sp;
                    fx = -k * vx; fz = -k * vz;
                    ATx += ry * fz; ATy += rz * fx - rx * fz; ATz -= ry * fx;
                }
                else if (p.wArea > 0)
                {
                    var nx = p.oz; var nz = -p.ox;
                    var vn = vx * nx + vz * nz;
                    var vpl2 = vy * vy + vn * vn;
                    if (vpl2 > 1e-8)
                    {
                        var vpl = Math.Sqrt(vpl2);
                        var alpha = Math.Atan2(-vn, vy) + (p.defl != 0 ? (p.def.wing != null ? Tuning.CtrlTau : 1) * Tuning.CtrlDeflect * p.defl : 0);
                        var cf = WingCoefficients(alpha, p.wSlope, p.wStall);
                        var qS = 0.5 * rho * vpl2 * p.wArea;
                        var L = qS * cf.cl; var D = qS * cf.cd * mf;
                        var fn = (L * vy - D * vn) / vpl;
                        fy = (-L * vn - D * vy) / vpl;
                        fx = fn * nx; fz = fn * nz;
                    }
                }
                else
                {
                    var k = qdm * p.aRad / sp;
                    fx = -k * vx; fy = -k * vy; fz = -k * vz;
                }
                Ax += fx; Ay += fy; Az += fz;
                ATx += ry * fz - rz * fy; ATy += rz * fx - rx * fz; ATz += rx * fy - ry * fx;
                if (d.chute != null && (p.deployed == "semi" || p.deployed == "full"))
                {
                    var area = p.deployed == "semi" ? d.chute.semiArea * p.deployT
                        : d.chute.semiArea + (d.chute.area - d.chute.semiArea) * p.deployT;
                    var k = qd * area * Tuning.ChuteCd / sp;
                    fx = -k * vx; fy = -k * vy; fz = -k * vz;
                    var py = ry + d.h / 2;
                    Ax += fx; Ay += fy; Az += fz;
                    ATx += py * fz - rz * fy; ATy += rz * fx - rx * fz; ATz += rx * fy - py * fx;
                }
                if (heating)
                {
                    double ex = p.stack ? Tuning.SideExposure * lat0 : Tuning.RadialExposure;
                    if (p.stack)
                    {
                        var f = uAx > 0 ? p.eUp * uAx : -p.eDown * uAx;
                        if (f > ex) ex = f;
                    }
                    var rise = Tuning.HeatK * Math.Pow(flux * ex, Tuning.HeatExp);
                    if (d.heatshield && p.res.GetValueOrDefault("ablator") > 0)
                    {
                        p.res["ablator"] = Math.Max(0, p.res["ablator"] - Tuning.AblateRate * flux * ex * dt);
                        rise *= Tuning.ShieldFactor;
                        _massDirty = true;
                    }
                    var target = Tuning.AmbientT + rise;
                    p.temp += (target - p.temp) * Math.Min(1, dt / (target > p.temp ? Tuning.HeatTau : Tuning.CoolTau));
                    if (p.temp > d.maxTemp) (overheated ??= new()).Add(p.id);
                }
            }
            var fm = Math.Sqrt(Ax * Ax + Ay * Ay + Az * Az);
            if (fm * dt > 0.5 * m * speed)
            {
                var s = 0.5 * m * speed / (fm * dt);
                Ax *= s; Ay *= s; Az *= s; ATx *= s; ATy *= s; ATz *= s;
            }
            Fx += Ax; Fy += Ay; Fz += Az; Tx += ATx; Ty += ATy; Tz += ATz;
        }
        if (!heating && _hot)
        {
            var k = Math.Min(1, dt / Tuning.CoolTau);
            var any = false;
            foreach (var p in _list)
            {
                p.temp += (Tuning.AmbientT - p.temp) * k;
                if (p.temp > Tuning.AmbientT + 2) any = true;
            }
            _hot = any;
        }
        else if (heating) _hot = true;

        var mMid = m - 0.5 * _mdot * dt;
        var im = 1 / (mMid > 0.5 * m ? mMid : m);
        var acc = new DVec3(Fx * im, Fy * im, Fz * im);
        gForce = acc.length() / PartCatalog.G0;
        acc.applyQuaternion(q);
        var gm = body.gm;
        var kgrav = -gm / (R * R * R) * 0.5 * dt;
        vel.x += pos.x * kgrav + acc.x * 0.5 * dt;
        vel.y += pos.y * kgrav + acc.y * 0.5 * dt;
        vel.z += pos.z * kgrav + acc.z * 0.5 * dt;
        pos.x += vel.x * dt; pos.y += vel.y * dt; pos.z += vel.z * dt;
        var R2 = pos.length();
        kgrav = -gm / (R2 * R2 * R2) * 0.5 * dt;
        vel.x += pos.x * kgrav + acc.x * 0.5 * dt;
        vel.y += pos.y * kgrav + acc.y * 0.5 * dt;
        vel.z += pos.z * kgrav + acc.z * 0.5 * dt;

        var Ix = inertia.x; var Iy = inertia.y; var Iz = inertia.z;
        var wx0 = w.x; var wy0 = w.y; var wz0 = w.z;
        w.x += (Tx - (Iz - Iy) * wy0 * wz0) / Ix * dt;
        w.y += (Ty - (Ix - Iz) * wz0 * wx0) / Iy * dt;
        w.z += (Tz - (Iy - Ix) * wx0 * wy0) / Iz * dt;
        var wl2 = w.lengthSq();
        if (wl2 > Tuning.MaxSpin * Tuning.MaxSpin) w.multiplyScalar(Tuning.MaxSpin / Math.Sqrt(wl2));
        if (wl2 > 1e-18)
        {
            var wl = w.length(); var ha = 0.5 * wl * dt; var s = Math.Sin(ha) / wl;
            var dq = new DQuat(w.x * s, w.y * s, w.z * s, Math.Cos(ha));
            q.multiply(dq).normalize();
        }
        if (overheated != null)
        {
            foreach (var id in overheated) parts.Remove(id);
            if (!parts.ContainsKey(controlId ?? -1) || parts.Count == 0)
            {
                dead = true;
                ev.Add(new SimEvent { type = "destroyed", vessel = this, reason = "overheat" });
                return;
            }
            Rebuild(); UpdateMass(true);
        }
        GroundCheck(ut, ev);
    }

    void GroundCheck(double ut, List<SimEvent> ev)
    {
        if (dead || landed) return;
        var R = pos.length();
        var up = pos.clone().multiplyScalar(1 / R);
        var ul = up.clone().applyQuaternion(q.clone().invert());
        var low = Lowest(ul);
        body.InertialToSurfaceDir(up, ut, up);
        var gh = body.SurfaceHeight(up);
        var hmin = R - body.radius + low.h - gh;
        if (hmin > 0) return;
        var wy = body.AngularRate;
        var air = new DVec3(vel.x - wy * pos.z, vel.y, vel.z + wy * pos.x);
        var vs = air.dot(pos.clone().multiplyScalar(1 / R));
        if (vs > 0.2) return;
        var speed = air.length();
        var part = low.part;
        var tol = part?.def.tolerance ?? 8;
        if (body.IsWater(up)) tol *= Tuning.WaterTol;
        if (speed > tol * Tuning.CrashAll || (part != null && speed > part.def.tolerance * (body.IsWater(up) ? Tuning.WaterTol : 1) && speed > tol))
        {
            dead = true;
            ev.Add(new SimEvent { type = "destroyed", vessel = this, reason = "crash" });
            return;
        }
        Land(ut, body.IsWater(up), speed, ev);
    }

    void Land(double ut, bool water, double speed, List<SimEvent> ev)
    {
        var R = pos.length();
        var dir = new DVec3();
        body.InertialToSurfaceDir(pos.clone().multiplyScalar(1 / R), ut, dir);
        var qRel = new DQuat().setFromAxisAngle(new DVec3(0, 1, 0), -body.RotationAngle(ut)).multiply(q).normalize();
        landed = true; splashed = water;
        surface = new SurfaceAnchor { dir = dir, qRel = qRel, h = R - body.radius };
        w.set(0, 0, 0);
        foreach (var p in _chutes) if (p.deployed is "semi" or "full") { p.deployed = "cut"; p.deployT = 0; }
        SyncLanded(ut);
        ev.Add(new SimEvent { type = water ? "splashed" : "landed", vessel = this });
    }

    void UpdateChutes(double dt, double pAtm, double speed, double radar, List<SimEvent> ev)
    {
        foreach (var p in _chutes)
        {
            var ch = p.def.chute!;
            if (p.deployed == "armed")
            {
                if (pAtm >= Tuning.ChuteMinPressure && speed < ch.maxSpeed)
                {
                    p.deployed = "semi"; p.deployT = 0;
                    ev.Add(new SimEvent { type = "chute", vessel = this, part = p, state = "semi" });
                }
            }
            else if (p.deployed is "semi" or "full")
            {
                if (speed > ch.maxSpeed * Tuning.ChuteRip && pAtm > 0)
                {
                    p.deployed = "cut"; p.deployT = 0;
                    ev.Add(new SimEvent { type = "chute", vessel = this, part = p, state = "cut" });
                    continue;
                }
                p.deployT = Math.Min(1, p.deployT + dt / (p.deployed == "semi" ? Tuning.ChuteSemiTime : Tuning.ChuteFullTime));
                if (p.deployed == "semi" && radar < ch.fullAlt && p.deployT >= 1)
                {
                    p.deployed = "full"; p.deployT = 0;
                    ev.Add(new SimEvent { type = "chute", vessel = this, part = p, state = "full" });
                }
            }
        }
    }

    static (double cl, double cd) WingCoefficients(double a, double slope, double stall)
    {
        var sa = Math.Sin(a); var ca = Math.Cos(a);
        var plateL = Tuning.WingPlate * 2 * sa * ca;
        var plateD = Tuning.WingCd0 + Tuning.WingPlateCd * sa * sa;
        var al = a; var eff = 1.0;
        if (ca < 0) { al = a > 0 ? a - Math.PI : a + Math.PI; eff = 0.5; }
        var x = Math.Abs(al) / stall;
        var wgt = x <= 0.85 ? 1 : x >= 1.35 ? 0 : 1 - (x - 0.85) / 0.5;
        wgt = wgt * wgt * (3 - 2 * wgt);
        var cl = slope * eff * al;
        var cd = Tuning.WingCd0 + Tuning.WingK * cl * cl;
        return (wgt * cl + (1 - wgt) * plateL, wgt * cd + (1 - wgt) * plateD);
    }

    double Systems(double dt, double pAtm, List<SimEvent> ev)
    {
        var ctl = false;
        double drain = 0; var probe = false;
        foreach (var p in _commands)
        {
            if (p.def.command == "crew") { if (p.crew.Count > 0) ctl = true; }
            else if (p.def.command == "probe") { probe = true; drain += p.def.elecDrain; }
        }
        if (probe && Drain("elec", drain * dt) >= drain * dt * 0.999 && Total("elec") > 0) ctl = true;
        if (!probe && _commands.Count == 0) ctl = true;
        // A crewed pod with a named crew, or a probe that still has charge.
        if (_commands.Any(p => p.def.command == "crew" && p.crew.Count > 0)) ctl = true;
        controllable = ctl;

        var throttle = Num.Clamp(ctrl.throttle, 0, 1);
        double F = 0, tx = 0, tz = 0, gim = 0, maxF = 0, mdotSum = 0, flx = 0, flz = 0;
        var burned = false;
        foreach (var e in _engines)
        {
            var p = e.part;
            p.thrust = 0; p.level = 0;
            if (!p.ignited) continue;
            var en = p.def.engine!;
            var full = PartCatalog.EngineThrust(p.def, pAtm);
            var mdot = en.thrust * 1000 / (en.ispVac * PartCatalog.G0);
            double level = 0;
            if (en.type == "solid")
            {
                if (p.flameout) continue;
                var need = mdot * dt; var have = p.res.GetValueOrDefault("solid");
                if (have <= need) { level = need > 0 ? have / need : 0; p.res["solid"] = 0; p.flameout = true; ev.Add(new SimEvent { type = "flameout", vessel = this, part = p }); }
                else { p.res["solid"] = have - need; level = 1; maxF += full; }
                burned = true;
            }
            else
            {
                double avail = 0;
                foreach (var t in e.tanks) avail += t.res.GetValueOrDefault(e.prop);
                if (avail <= 0)
                {
                    if (throttle > 0 && !p.flameout) { p.flameout = true; ev.Add(new SimEvent { type = "flameout", vessel = this, part = p }); }
                    continue;
                }
                p.flameout = false;
                maxF += full;
                if (throttle <= 0) continue;
                var need = mdot * throttle * dt;
                double got;
                if (avail <= need)
                {
                    got = avail;
                    foreach (var t in e.tanks) t.res[e.prop] = 0;
                    p.flameout = true;
                    ev.Add(new SimEvent { type = "flameout", vessel = this, part = p });
                }
                else
                {
                    got = need;
                    var rem = need; var n = e.tanks.Count(t => t.res.GetValueOrDefault(e.prop) > 0);
                    for (var pass = 0; pass < 4 && rem > 1e-12 && n > 0; pass++)
                    {
                        var share = rem / n; n = 0;
                        foreach (var t in e.tanks)
                        {
                            var f = t.res.GetValueOrDefault(e.prop);
                            if (f <= 0) continue;
                            if (f <= share) { rem -= f; t.res[e.prop] = 0; }
                            else { rem -= share; t.res[e.prop] = f - share; n++; }
                        }
                    }
                }
                level = need > 0 ? throttle * got / need : 0;
                burned = true;
            }
            var T = full * level;
            p.thrust = T; p.level = level;
            mdotSum += mdot * level;
            var Ts = p.flip ? -T : T;
            F += Ts;
            tx -= (p.pos.z - com.z) * Ts;
            tz += (p.pos.x - com.x) * Ts;
            if (en.gimbal != 0) gim += T * Math.Sin(en.gimbal * Math.PI / 180) * Math.Abs(p.pos.y - com.y);
        }
        if (burned) _massDirty = true;
        thrust = Math.Abs(F); maxThrust = maxF;
        isp = mdotSum > 0 ? Math.Abs(F) / (mdotSum * PartCatalog.G0) : 0;
        _mdot = mdotSum; _thrTx = tx; _thrTy = 0; _thrTz = tz; _gim = gim; _thrFx = flx; _thrFz = flz;
        EstimateDeltaV();
        return F;
    }

    public bool controllable = true;

    void ControlStep(double dt, double qdyn)
    {
        double ax = 0, ay = 0, az = 0;
        var elec = Total("elec");
        if (_wheel > 0 && elec > 0) ax = ay = az = _wheel;
        ax += _gim; az += _gim;
        var lim = Tuning.MaxAngAcc;
        var dx = ax; var dy = ay; var dz = az;
        if (dx > inertia.x * lim) dx = inertia.x * lim;
        if (dy > inertia.y * lim) dy = inertia.y * lim;
        if (dz > inertia.z * lim) dz = inertia.z * lim;
        double cx = 0, cy = 0, cz = 0;
        if (controllable)
        {
            var mx = Num.Clamp(ctrl.pitch, -1, 1);
            var my = Num.Clamp(ctrl.roll, -1, 1);
            var mz = -Num.Clamp(ctrl.yaw, -1, 1);
            var manual = Math.Abs(mx) > 0.01 || Math.Abs(my) > 0.01 || Math.Abs(mz) > 0.01;
            cx = mx; cy = my; cz = mz;
            if (ctrl.sas)
            {
                var T = Math.Max(Tuning.SasT, 2.5 * dt);
                var alx = ax / inertia.x; var aly = ay / inertia.y; var alz = az / inertia.z;
                double ex = 0, ey = 0, ez = 0;
                var track = true;
                var dir = SasDirection(ctrl.sasMode);
                if (dir != null)
                {
                    dir.applyQuaternion(q.clone().invert());
                    var s = Math.Sqrt(dir.x * dir.x + dir.z * dir.z);
                    var ang = Math.Atan2(s, dir.y);
                    if (s > 1e-9) { ex = dir.z / s * ang; ez = -dir.x / s * ang; }
                    else if (dir.y < 0) ex = Math.PI;
                    sasTarget = null;
                }
                else
                {
                    if (manual) { _sasCoast = true; sasTarget = null; }
                    if (_sasCoast && !manual && w.lengthSq() < 0.0009) _sasCoast = false;
                    if (_sasCoast) track = false;
                    else
                    {
                        sasTarget ??= q.clone();
                        var err = q.clone().invert().multiply(sasTarget);
                        if (err.w < 0) { err.x = -err.x; err.y = -err.y; err.z = -err.z; err.w = -err.w; }
                        var s = Math.Sqrt(err.x * err.x + err.y * err.y + err.z * err.z);
                        if (s > 1e-12) { var ang = 2 * Math.Atan2(s, err.w) / s; ex = err.x * ang; ey = err.y * ang; ez = err.z * ang; }
                    }
                }
                if (!track) { ex = ey = ez = 0; }
                if (Math.Abs(mx) <= 0.01) cx = SasAxis(ex, w.x, alx, T);
                if (Math.Abs(my) <= 0.01) cy = SasAxis(ey, w.y, aly, T);
                if (Math.Abs(mz) <= 0.01) cz = SasAxis(ez, w.z, alz, T);
            }
            else sasTarget = null;
        }
        var use = (Math.Abs(cx) + Math.Abs(cy) + Math.Abs(cz)) / 3;
        if (use > 0 && _wheel > 0 && elec > 0) Drain("elec", Tuning.WheelEc * _wheel / 1000 * use * dt);
        _tqx = cx * dx; _tqy = cy * dy; _tqz = cz * dz;
        foreach (var p in _wings)
        {
            if (p.wCtl == null) { p.defl = 0; continue; }
            p.defl = Num.Clamp(p.wCtl == "yaw" ? cz : p.wCtl == "roll" ? cy : cx, -1, 1);
        }
    }

    static double SasAxis(double e, double w, double alpha, double T)
    {
        if (!(alpha > 1e-7)) return 0;
        var a = Math.Abs(e);
        var wd = Math.Min(Tuning.SasKp * a, Math.Sqrt(alpha * a));
        if (wd > Tuning.SasWMax) wd = Tuning.SasWMax;
        if (e < 0) wd = -wd;
        return Num.Clamp((wd - w) / (alpha * T), -1, 1);
    }

    DVec3? SasDirection(string mode)
    {
        switch (mode)
        {
            case "prograde": return vel.lengthSq() > 1e-6 ? vel.clone().normalize() : null;
            case "retrograde": return vel.lengthSq() > 1e-6 ? vel.clone().normalize().negate() : null;
            case "normal":
                var n = new DVec3().crossVectors(pos, vel);
                return n.lengthSq() > 1e-6 ? n.normalize() : null;
            case "antinormal":
                var an = new DVec3().crossVectors(pos, vel);
                return an.lengthSq() > 1e-6 ? an.normalize().negate() : null;
            case "radialOut": return pos.lengthSq() > 0 ? pos.clone().normalize() : null;
            case "radialIn": return pos.lengthSq() > 0 ? pos.clone().normalize().negate() : null;
            case "up": return pos.lengthSq() > 0 ? pos.clone().normalize() : null;
            default: return null;
        }
    }

    void EstimateDeltaV()
    {
        double F = 0, mdot = 0, prop = 0;
        foreach (var e in _engines)
        {
            var p = e.part;
            if (!p.ignited || p.def.engine == null) continue;
            var en = p.def.engine;
            if (en.type == "solid")
            {
                var s = p.res.GetValueOrDefault("solid");
                if (s <= 0) continue;
                F += en.thrust * 1000; mdot += en.thrust * 1000 / (en.ispVac * PartCatalog.G0); prop += s;
            }
            else
            {
                double avail = 0;
                foreach (var t in e.tanks) avail += t.res.GetValueOrDefault(e.prop);
                if (avail <= 0) continue;
                F += en.thrust * 1000; mdot += en.thrust * 1000 / (en.ispVac * PartCatalog.G0);
                prop = Math.Max(prop, avail);
            }
        }
        stageDeltaV = mdot > 0 && mass > prop ? F / mdot * Math.Log(mass / Math.Max(1, mass - prop)) : 0;
        deltaV = stageDeltaV;
    }

    public string? UpdateTelemetry(double ut)
    {
        if (dead) return null;
        if (landed) SyncLanded(ut);
        var R = Math.Max(pos.length(), 1);
        var wy = body.AngularRate;
        var sv = surfaceVel.set(vel.x - wy * pos.z, vel.y, vel.z + wy * pos.x);
        altitude = R - body.radius;
        airspeed = sv.length();
        orbitalSpeed = vel.length();
        verticalSpeed = landed ? 0 : sv.dot(pos) / R;
        var rho = body.Density(altitude);
        dynamicPressure = 0.5 * rho * airspeed * airspeed;
        mach = airspeed / Tuning.SoundSpeed;
        radarAlt = landed ? 0 : Math.Max(0, altitude - _baseH);
        var energy = 0.5 * vel.lengthSq() - body.gm / R;
        var h2 = new DVec3().crossVectors(pos, vel).lengthSq();
        var ecc = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h2 / (body.gm * body.gm)));
        var rp = h2 / body.gm / (1 + ecc);
        double ra = double.PositiveInfinity, per = double.PositiveInfinity;
        if (energy < 0)
        {
            var aa = -body.gm / (2 * energy);
            ra = aa * (1 + ecc);
            per = Num.Tau * Math.Sqrt(aa * aa * aa / body.gm);
        }
        periapsis = rp - body.radius;
        apoapsis = ra - body.radius;
        period = per;
        string sit;
        if (splashed) sit = "splashed";
        else if (landed) sit = launched ? "landed" : "prelaunch";
        else if (body.atmo != null && altitude < body.atmo.height) sit = "flying";
        else
        {
            var floor = body.atmo?.height ?? 5000;
            if (energy >= 0 || ra > body.soi) sit = "escape";
            else if (periapsis > floor) sit = "orbit";
            else sit = "suborbital";
        }
        var from = situation;
        situation = sit;
        if (!landed) orbit = Orbit.FromState(pos, vel, body.gm, ut);
        return from != sit ? from : null;
    }

    public DVec3 PartWorld(VPart p, DVec3? o = null)
    {
        o ??= new DVec3();
        return o.set(p.pos.x - com.x, p.pos.y - com.y, p.pos.z - com.z).applyQuaternion(q).add(pos);
    }
}

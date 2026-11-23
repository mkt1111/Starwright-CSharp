namespace Starwright.Core;

public sealed class World
{
    public static readonly double[] WarpRates = { 1, 2, 3, 4, 5, 10, 50, 100, 1000, 10000, 100000 };
    public const int PhysicsWarpMax = 3;
    public const int MaxSubsteps = 400;

    public double ut;
    public readonly List<Vessel> vessels = new();
    public Vessel? active;
    public int warpIndex;
    public string limitReason = "limit";
    public int substeps;
    public readonly List<SimEvent> events = new();

    int _nextId = 1;

    public double Rate => WarpRates[warpIndex];

    public static DQuat PadFrame(DVec3 dir)
    {
        var up = dir.clone().normalize();
        var east = new DVec3().crossVectors(new DVec3(0, 1, 0), up);
        if (east.lengthSq() < 1e-12) east.set(0, 0, -1);
        east.normalize();
        var south = new DVec3().crossVectors(east, up);
        return new DQuat().setFromBasis(east, up, south);
    }

    public Vessel Launch(Craft craft, IReadOnlyList<string>? crew = null, string site = "pad")
    {
        var v = Vessel.FromCraft(craft, crew);
        v.id = _nextId++;
        v.body = HelionSystem.System.home;
        var sites = HelionSystem.System.sites;
        var st = site == "runway" && sites.TryGetValue("runway", out var rw) ? (rw.start ?? rw) : HelionSystem.System.launchSite;
        var dir = st.dir.clone().normalize();
        v.landed = true;
        v.launched = false;
        v.surface = new SurfaceAnchor { dir = dir, qRel = PadFrame(dir), h = 0 };
        v.situation = "prelaunch";
        v.ctrl.sas = true;
        v.ctrl.sasMode = "hold";
        v.SyncLanded(ut);
        v.UpdateTelemetry(ut);
        vessels.Add(v);
        active = v;
        warpIndex = 0;
        return v;
    }

    public Vessel Spawn(Craft craft, Body body, DVec3 pos, DVec3 vel, DQuat? q = null, IReadOnlyList<string>? crew = null)
    {
        var v = Vessel.FromCraft(craft, crew);
        v.id = _nextId++;
        v.body = body;
        v.pos.copy(pos);
        v.vel.copy(vel);
        if (q != null) v.q.copy(q);
        v.landed = false;
        v.launched = true;
        v.ctrl.sas = true;
        v.UpdateTelemetry(ut);
        vessels.Add(v);
        active = v;
        return v;
    }

    public int MaxWarpIndex()
    {
        var a = active;
        var top = WarpRates.Length - 1;
        if (a == null || a.dead) return top;
        if (a.WouldThrust()) { limitReason = "thrust"; return PhysicsWarpMax; }
        if (a.landed) return top;
        var alt = a.pos.length() - a.body.radius;
        if (a.body.atmo != null && alt < a.body.atmo.height) { limitReason = "atmosphere"; return PhysicsWarpMax; }
        var idx = PhysicsWarpMax;
        for (var i = PhysicsWarpMax + 1; i <= top; i++)
        {
            var minAlt = a.body.atmo != null ? a.body.atmo.height : Math.Max(a.body.radius * 0.015, (a.body.maxElevation) + 500);
            var rail = Math.Max(minAlt, a.body.radius * RailFrac(i));
            if (alt >= rail) idx = i;
            else break;
        }
        if (idx < top) limitReason = "altitude";
        return idx;
    }

    static double RailFrac(int i) => i switch
    {
        <= 4 => 0,
        5 => 0.05,
        6 => 0.1,
        7 => 0.2,
        8 => 0.5,
        9 => 1,
        _ => 2,
    };

    public int SetWarp(int index)
    {
        index = Math.Max(0, Math.Min(WarpRates.Length - 1, index));
        var max = MaxWarpIndex();
        var idx = Math.Min(index, max);
        if (idx != warpIndex)
        {
            warpIndex = idx;
            events.Add(new SimEvent { type = "warp", vessel = active, text = WarpRates[idx] + "×" });
        }
        if (index > max)
            events.Add(new SimEvent { type = "message", text = $"Time warp limited to {WarpRates[max]}× ({limitReason})" });
        return idx;
    }

    public List<SimEvent> Stage()
    {
        var a = active;
        if (a == null || a.dead) return new();
        var ev = a.ActivateNextStage();
        Absorb(ev);
        return ev;
    }

    public void Step(double realDt)
    {
        if (!(realDt > 0)) return;
        substeps = 0;
        if (warpIndex > MaxWarpIndex()) warpIndex = MaxWarpIndex();
        var simDt = realDt * WarpRates[warpIndex];
        if (warpIndex > PhysicsWarpMax) RailsStep(simDt);
        else PhysicsStep(simDt);
        foreach (var v in vessels)
        {
            if (v.dead) continue;
            var from = v.UpdateTelemetry(ut);
            if (from != null) events.Add(new SimEvent { type = "situation", vessel = v, reason = from, state = v.situation });
        }
    }

    void PhysicsStep(double simDt)
    {
        var remaining = simDt;
        var n = 0;
        var coast = CanCoast();
        while (remaining > 1e-10 && n < MaxSubsteps)
        {
            var dtMax = warpIndex > 0 && coast ? Tuning.DtCoast : Tuning.DtMax;
            var dt = remaining <= dtMax * 1.000001 ? remaining : dtMax;
            ut += dt;
            foreach (var v in vessels.ToArray())
            {
                if (v.dead) continue;
                if (active != null && v != active && v.pos.distanceToSquared(active.pos) > 2500.0 * 2500 && v.body == active.body)
                {
                    CoastOne(v, dt);
                    continue;
                }
                var before = events.Count;
                v.Simulate(dt, ut, events);
                for (var i = before; i < events.Count; i++)
                    if (events[i].type == "decouple" && events[i].debris != null)
                    {
                        var d = events[i].debris!;
                        d.id = _nextId++;
                        vessels.Add(d);
                    }
                if (!v.landed && !v.dead) SoiCheck(v);
            }
            remaining -= dt;
            n++;
            coast = CanCoast();
        }
        substeps = n;
    }

    bool CanCoast()
    {
        foreach (var v in vessels)
        {
            if (v.dead || v != active) continue;
            if (v.landed) return !v.WouldThrust();
            if (v.WouldThrust()) return false;
            var alt = v.pos.length() - v.body.radius;
            if (alt < (v.body.atmo?.height ?? 0) + 3000) return false;
        }
        return true;
    }

    void RailsStep(double simDt)
    {
        ut += simDt;
        foreach (var v in vessels)
        {
            if (v.dead || v.landed) { if (v.landed) v.SyncLanded(ut); continue; }
            if (v.WouldThrust()) { warpIndex = 0; PhysicsStep(0); return; }
            CoastOne(v, simDt);
        }
    }

    void CoastOne(Vessel v, double dt)
    {
        var t1 = ut;
        var t0 = ut - dt;
        v.orbit ??= Orbit.FromState(v.pos, v.vel, v.body.gm, t0);
        var patches = Trajectory.PredictPatches(v.orbit, v.body, t0, 2);
        var p = patches[0];
        if (p.endType is "exit" or "enter" && p.tEnd <= t1 && p.nextBody != null)
        {
            var s = v.orbit.StateAt(p.tEnd);
            var nx = p.endType == "exit"
                ? Trajectory.ToParentFrame(v.body, s.r, s.v, p.tEnd)
                : Trajectory.ToChildFrame(p.nextBody, s.r, s.v, p.tEnd);
            var from = v.body;
            v.body = nx.body;
            v.pos.copy(nx.pos);
            v.vel.copy(nx.vel);
            v.orbit = Orbit.FromState(nx.pos, nx.vel, nx.body.gm, p.tEnd);
            if (t1 > p.tEnd) v.orbit.State(t1, v.pos, v.vel);
            events.Add(new SimEvent { type = "soi", vessel = v, fromBody = from, toBody = nx.body });
            return;
        }
        if (p.endType is "atmo" or "impact" && p.tEnd <= t1)
        {
            v.orbit.State(p.tEnd, v.pos, v.vel);
            v.dead = true;
            events.Add(new SimEvent { type = "destroyed", vessel = v, reason = p.endType });
            return;
        }
        v.orbit.State(t1, v.pos, v.vel);
    }

    void SoiCheck(Vessel v)
    {
        var tr = Trajectory.CheckSoiTransition(v.body, v.pos, v.vel, ut);
        if (tr == null) return;
        var from = v.body;
        v.body = tr.Value.body;
        v.pos.copy(tr.Value.pos);
        v.vel.copy(tr.Value.vel);
        v.orbit = null;
        events.Add(new SimEvent { type = "soi", vessel = v, fromBody = from, toBody = v.body });
    }

    void Absorb(List<SimEvent> ev)
    {
        foreach (var e in ev)
        {
            events.Add(e);
            if (e.type == "decouple" && e.debris != null)
            {
                e.debris.id = _nextId++;
                vessels.Add(e.debris);
            }
        }
    }

    public void SwitchTo(Vessel v)
    {
        if (vessels.Contains(v) && !v.dead) active = v;
    }
}

namespace Starwright.Core;

public readonly struct ManeuverDv
{
    public readonly double prograde, normal, radial;
    public ManeuverDv(double prograde = 0, double normal = 0, double radial = 0)
    {
        this.prograde = prograde; this.normal = normal; this.radial = radial;
    }
}

public sealed class Patch
{
    public Orbit orbit = null!;
    public Body body = null!;
    public double tStart, tEnd = double.PositiveInfinity, searchedUntil = double.PositiveInfinity;
    public string? endType;
    public Body? nextBody;
}

public static class Trajectory
{
    const double EncStep = 0.1;
    const int MaxSamples = 8000;

    public static (Body body, DVec3 pos, DVec3 vel) ToParentFrame(Body body, DVec3 pos, DVec3 vel, double t)
    {
        var parent = body.parent;
        if (parent == null) return (body, pos.clone(), vel.clone());
        return (parent, body.PosAt(t).add(pos), body.VelAt(t).add(vel));
    }

    public static (Body body, DVec3 pos, DVec3 vel) ToChildFrame(Body child, DVec3 pos, DVec3 vel, double t)
    {
        return (child, pos.clone().sub(child.PosAt(t)), vel.clone().sub(child.VelAt(t)));
    }

    public static (Body body, DVec3 pos, DVec3 vel)? CheckSoiTransition(Body body, DVec3 pos, DVec3 vel, double t)
    {
        if (body.parent != null && pos.lengthSq() > body.soi * body.soi && pos.dot(vel) > 0)
            return ToParentFrame(body, pos, vel, t);
        foreach (var child in body.children)
        {
            var rel = child.PosAt(t).sub(pos);
            if (rel.lengthSq() < child.soi * child.soi)
            {
                var relVel = child.VelAt(t).sub(vel);
                if (rel.dot(relVel) < 0) return ToChildFrame(child, pos, vel, t);
            }
        }
        return null;
    }

    public static (DVec3 prograde, DVec3 normal, DVec3 radial) ManeuverBasis(DVec3 r, DVec3 v)
    {
        var prograde = v.clone().normalize();
        var normal = new DVec3().crossVectors(r, v).normalize();
        if (normal.lengthSq() == 0)
        {
            normal.set(0, 1, 0);
            if (Math.Abs(prograde.y) > 0.99) normal.set(1, 0, 0);
            normal.addScaledVector(prograde, -normal.dot(prograde)).normalize();
        }
        var radial = new DVec3().crossVectors(prograde, normal).normalize();
        return (prograde, normal, radial);
    }

    public static Orbit ApplyManeuver(Orbit orbit, double t, ManeuverDv dv)
    {
        var (r, v) = orbit.StateAt(t);
        var b = ManeuverBasis(r, v);
        v.addScaledVector(b.prograde, dv.prograde).addScaledVector(b.normal, dv.normal).addScaledVector(b.radial, dv.radial);
        return Orbit.FromState(r, v, orbit.gm, t);
    }

    static double CrossingTime(Orbit orbit, double R, bool inbound, double after)
    {
        if (orbit.Periapsis > R || orbit.Apoapsis < R) return double.PositiveInfinity;
        var t = orbit.TimeAtRadius(R, inbound, after);
        if (!(t >= after)) return t >= after - 1e-6 ? after : double.PositiveInfinity;
        return t;
    }

    static double SoiDistance(Orbit orbit, Body child, double t)
    {
        var p1 = orbit.PosAt(t);
        var p2 = child.PosAt(t);
        return p1.distanceTo(p2) - child.soi;
    }

    static double BisectEntry(Orbit orbit, Body child, double lo, double hi)
    {
        for (var k = 0; k < 60 && hi - lo > 1e-5; k++)
        {
            var mid = 0.5 * (lo + hi);
            if (SoiDistance(orbit, child, mid) > 0) lo = mid; else hi = mid;
        }
        return hi;
    }

    static double FindEncounter(Orbit orbit, Body child, double[] ts, double[] d, int n)
    {
        var arm = -1;
        for (var i = 0; i < n; i++)
        {
            if (arm < 0)
            {
                if (d[i] > 0) arm = i;
                continue;
            }
            if (d[i] <= 0) return BisectEntry(orbit, child, ts[i - 1], ts[i]);
            var j = i - 1;
            if (j - 1 >= arm && d[j] < d[j - 1] && d[j] <= d[i])
            {
                var a = ts[j - 1]; var b = ts[i];
                const double g = 0.6180339887498949;
                var c1 = b - g * (b - a); var c2 = a + g * (b - a);
                var f1 = SoiDistance(orbit, child, c1); var f2 = SoiDistance(orbit, child, c2);
                for (var k = 0; k < 40 && f1 > 0 && f2 > 0; k++)
                {
                    if (f1 < f2) { b = c2; c2 = c1; f2 = f1; c1 = b - g * (b - a); f1 = SoiDistance(orbit, child, c1); }
                    else { a = c1; c1 = c2; f1 = f2; c2 = a + g * (b - a); f2 = SoiDistance(orbit, child, c2); }
                }
                if (f1 <= 0) return BisectEntry(orbit, child, ts[j - 1], c1);
                if (f2 <= 0) return BisectEntry(orbit, child, ts[j - 1], c2);
            }
        }
        return double.PositiveInfinity;
    }

    static (int n, double searched) SampleEncounters(Orbit orbit, List<Body> cands, double t0, double tLimit, bool extend, double[] ts, double[][] ds)
    {
        double rlo = double.PositiveInfinity, rhi = 0;
        foreach (var c in cands)
        {
            rlo = Math.Min(rlo, c.orbit!.Periapsis - c.soi);
            rhi = Math.Max(rhi, c.orbit.Apoapsis + c.soi);
        }
        rlo *= 0.8; rhi *= 1.25;
        var gm = orbit.gm;
        var t = t0; var n = 0; var extra = extend ? 2 : 0; var searched = tLimit;
        var p1 = new DVec3(); var p2 = new DVec3();
        for (;;)
        {
            orbit.PosAt(t, p1);
            var r = p1.length();
            ts[n] = t;
            var dt = EncStep * Math.Sqrt(r * r * r / gm);
            if (orbit.hyper) dt = Math.Min(dt, EncStep * r / Math.Sqrt(gm * (2 / r - 1 / orbit.a)));
            for (var k = 0; k < cands.Count; k++)
            {
                var c = cands[k];
                c.PosAt(t, p2);
                ds[k][n] = p1.distanceTo(p2) - c.soi;
                if (r * 0.8 <= c.orbit!.Apoapsis + c.soi && r * 1.2 >= c.orbit.Periapsis - c.soi)
                {
                    var rc = p2.length();
                    dt = Math.Min(dt, EncStep * Math.Sqrt(rc * rc * rc / gm));
                }
            }
            n++;
            if (t >= tLimit && extra-- <= 0) break;
            if (n >= MaxSamples) { searched = t; break; }
            var tn = t + dt;
            if (r > rhi * (1 + 1e-9))
            {
                var tc = CrossingTime(orbit, rhi, true, t);
                if (double.IsPositiveInfinity(tc)) { searched = double.PositiveInfinity; break; }
                if (tc > tn) tn = tc;
            }
            else if (r < rlo * (1 - 1e-9))
            {
                var tc = CrossingTime(orbit, rlo, false, t);
                if (double.IsPositiveInfinity(tc)) { searched = double.PositiveInfinity; break; }
                if (tc > tn) tn = tc;
            }
            if (t < tLimit && tn > tLimit) tn = tLimit;
            t = tn;
        }
        return (n, searched);
    }

    sealed class PatchEnd
    {
        public double t = double.PositiveInfinity, searched = double.PositiveInfinity;
        public string? type;
        public Body? next;
    }

    static PatchEnd? FindPatchEnd(Orbit orbit, Body body, double t0)
    {
        double tEnd = double.PositiveInfinity;
        string? type = null;
        Body? next = null;
        var r0v = orbit.StateAt(t0);
        var r0 = r0v.r.length();
        var rsurf = body.radius;
        var ratmo = body.atmo != null ? rsurf + body.atmo.height : rsurf;
        if (body.atmo != null && r0 >= ratmo * (1 - 1e-9) && orbit.Periapsis < ratmo)
        {
            var t = CrossingTime(orbit, ratmo, true, t0);
            if (t < tEnd) { tEnd = t; type = "atmo"; }
        }
        else if (orbit.Periapsis < rsurf)
        {
            double t;
            if (r0 <= rsurf || orbit.Apoapsis < rsurf) t = t0;
            else t = CrossingTime(orbit, rsurf, true, t0);
            if (t < tEnd) { tEnd = t; type = "impact"; }
        }
        if (body.parent != null && double.IsFinite(body.soi) && orbit.Apoapsis > body.soi)
        {
            double t;
            if (r0 > body.soi && r0v.r.dot(r0v.v) > 0) t = t0;
            else t = CrossingTime(orbit, body.soi, false, t0);
            if (t < tEnd) { tEnd = t; type = "exit"; next = body.parent; }
        }
        var searched = double.PositiveInfinity;
        if (body.children.Count > 0)
        {
            var rp = orbit.Periapsis; var ra = orbit.Apoapsis;
            var candidates = body.children.Where(c => ra >= c.orbit!.Periapsis - c.soi && rp <= c.orbit.Apoapsis + c.soi).ToList();
            if (candidates.Count > 0)
            {
                var open = !double.IsFinite(tEnd);
                var tLimit = open ? (orbit.hyper ? double.PositiveInfinity : t0 + orbit.Period) : tEnd;
                if (tLimit > t0)
                {
                    var ts = new double[MaxSamples + 4];
                    var ds = new double[candidates.Count][];
                    for (var i = 0; i < candidates.Count; i++) ds[i] = new double[MaxSamples + 4];
                    var s = SampleEncounters(orbit, candidates, t0, tLimit, open && !orbit.hyper, ts, ds);
                    searched = s.searched;
                    for (var k = 0; k < candidates.Count; k++)
                    {
                        var t = FindEncounter(orbit, candidates[k], ts, ds[k], s.n);
                        if (t < tEnd) { tEnd = t; type = "enter"; next = candidates[k]; }
                    }
                }
            }
        }
        if (type != null && searched < tEnd) return new PatchEnd { t = double.PositiveInfinity, searched = searched };
        if (type != null) return new PatchEnd { t = tEnd, type = type, next = next, searched = searched };
        if (searched < double.PositiveInfinity) return new PatchEnd { searched = searched };
        return null;
    }

    public static List<Patch> PredictPatches(Orbit orbit, Body body, double tStart, int maxPatches = 4)
    {
        var patches = new List<Patch>();
        var o = orbit; var b = body; var t0 = tStart;
        for (var k = 0; k < maxPatches; k++)
        {
            var patch = new Patch { orbit = o, body = b, tStart = t0 };
            patches.Add(patch);
            var ev = FindPatchEnd(o, b, t0);
            if (ev == null) break;
            patch.searchedUntil = ev.searched;
            if (ev.type == null) break;
            patch.tEnd = ev.t;
            patch.endType = ev.type;
            patch.nextBody = ev.next;
            if (ev.type != "exit" && ev.type != "enter") break;
            var (r, v) = o.StateAt(ev.t);
            var nx = ev.type == "exit" ? ToParentFrame(b, r, v, ev.t) : ToChildFrame(ev.next!, r, v, ev.t);
            o = Orbit.FromState(nx.pos, nx.vel, nx.body.gm, ev.t);
            b = nx.body;
            t0 = ev.t;
        }
        return patches;
    }

    public static (double t, double distance, double relSpeed) ClosestApproach(Orbit a, Orbit b, double tStart, double tEnd, int samples = 0)
    {
        (double t, double distance, double relSpeed) At(double t)
        {
            var sa = a.StateAt(t); var sb = b.StateAt(t);
            return (t, sa.r.distanceTo(sb.r), sa.v.distanceTo(sb.v));
        }
        if (!(tEnd > tStart)) return At(tStart);
        var span = tEnd - tStart;
        var n = samples;
        if (n < 2)
        {
            var P = Math.Min(a.Period, b.Period);
            n = double.IsFinite(P) && P > 0 ? (int)Math.Ceiling(72 * span / P) : 200;
            n = Math.Max(64, Math.Min(4000, n));
        }
        var d = new double[n + 1];
        var p1 = new DVec3(); var p2 = new DVec3();
        double Sep2(double t)
        {
            a.PosAt(t, p1); b.PosAt(t, p2);
            return p1.distanceToSquared(p2);
        }
        for (var i = 0; i <= n; i++) d[i] = Sep2(tStart + span * i / n);
        const double g = 0.6180339887498949;
        var bestT = tStart; var bestD = d[0];
        if (d[n] < bestD) { bestD = d[n]; bestT = tEnd; }
        for (var i = 0; i <= n; i++)
        {
            var lo = i > 0 ? d[i - 1] : double.PositiveInfinity;
            var hi = i < n ? d[i + 1] : double.PositiveInfinity;
            if (d[i] > lo || d[i] > hi) continue;
            var aa = tStart + span * Math.Max(0, i - 1) / n;
            var bb = tStart + span * Math.Min(n, i + 1) / n;
            var c1 = bb - g * (bb - aa); var c2 = aa + g * (bb - aa);
            var f1 = Sep2(c1); var f2 = Sep2(c2);
            for (var k = 0; k < 60 && bb - aa > 1e-4; k++)
            {
                if (f1 < f2) { bb = c2; c2 = c1; f2 = f1; c1 = bb - g * (bb - aa); f1 = Sep2(c1); }
                else { aa = c1; c1 = c2; f1 = f2; c2 = aa + g * (bb - aa); f2 = Sep2(c2); }
            }
            var t = f1 < f2 ? c1 : c2; var f = Math.Min(f1, f2);
            if (f < bestD) { bestD = f; bestT = t; }
        }
        return At(bestT);
    }
}

namespace Starwright.Core;

/// <summary>
/// Two-body Kepler orbit. Port of js/core/orbit.js. Stored as perifocal basis P, Q, H plus shape,
/// so circular and equatorial orbits are never degenerate. Y-up; prograde is +X toward -Z.
/// </summary>
public sealed class Orbit
{
    const double CircularE = 1e-10;
    const double ParabolicEps = 1e-12;
    const double RadialEps = 1e-7;

    public double gm = 1, e, p = 1, a = 1, n = 1, M0, t0, alpha = 1, rp = 1, ome = 1, b = 1;
    public bool hyper;
    public readonly DVec3 P = new(1, 0, 0);
    public readonly DVec3 Q = new(0, 0, -1);
    public readonly DVec3 H = new(0, 1, 0);

    public Orbit() { Finish(1); }

    static double XMinusSin(double x)
    {
        if (Math.Abs(x) < 0.3)
        {
            var x2 = x * x;
            return (x * x2 / 6) * (1 - (x2 / 20) * (1 - (x2 / 42) * (1 - (x2 / 72) * (1 - (x2 / 110) * (1 - x2 / 156)))));
        }
        return x - Math.Sin(x);
    }

    static double SinhMinusX(double x)
    {
        if (Math.Abs(x) < 0.3)
        {
            var x2 = x * x;
            return (x * x2 / 6) * (1 + (x2 / 20) * (1 + (x2 / 42) * (1 + (x2 / 72) * (1 + (x2 / 110) * (1 + x2 / 156)))));
        }
        return Math.Sinh(x) - x;
    }

    public static double SolveKeplerElliptic(double M, double e, double? omeIn = null)
    {
        var ome = omeIn ?? 1 - e;
        if (double.IsNaN(M)) return 0;
        if (M > Math.PI || M < -Math.PI) M -= Num.Tau * Num.JsRound(M / Num.Tau);
        var m = Math.Abs(M);
        if (m == 0) return 0;
        double lo = 0, hi = Math.PI, E;
        if (e < 0.8) E = m + e * Math.Sin(m);
        else
        {
            E = Math.Cbrt(6 * m / e);
            if (ome > 0 && m / ome < E) E = m / ome;
        }
        if (!(E > lo && E < hi)) E = 0.5 * (lo + hi);
        for (var i = 0; i < 100; i++)
        {
            var f = ome * Math.Sin(E) + XMinusSin(E) - m;
            if (f == 0) break;
            if (f > 0) hi = E; else lo = E;
            var s = Math.Sin(0.5 * E);
            var fp = ome + 2 * e * s * s;
            var En = E - f / fp;
            if (!(En > lo && En < hi)) En = 0.5 * (lo + hi);
            var d = Math.Abs(En - E);
            E = En;
            if (d <= 2e-16 * Math.Max(E, 1e-300) || hi - lo <= 0) break;
        }
        return M < 0 ? -E : E;
    }

    public static double SolveKeplerHyperbolic(double M, double e, double? omeIn = null)
    {
        var ome = omeIn ?? 1 - e;
        if (double.IsNaN(M)) return 0;
        var m = Math.Abs(M);
        if (m == 0) return 0;
        var em1 = -ome;
        var lo = Math.Asinh(m / e);
        var fmax = Math.Cbrt(6 * m);
        if (em1 > 0 && m / em1 < fmax) fmax = m / em1;
        var hi = Math.Asinh((m + fmax) / e) * (1 + 1e-12) + 1e-300;
        lo *= 1 - 1e-12;
        var F = hi;
        for (var i = 0; i < 100; i++)
        {
            var f = em1 * Math.Sinh(F) + SinhMinusX(F) - m;
            if (f == 0) break;
            if (f > 0) hi = F; else lo = F;
            var s = Math.Sinh(0.5 * F);
            var fp = em1 + 2 * e * s * s;
            var Fn = F - f / fp;
            if (!(Fn > lo && Fn < hi)) Fn = 0.5 * (lo + hi);
            var d = Math.Abs(Fn - F);
            F = Fn;
            if (d <= 2e-16 * Math.Max(F, 1e-300) || hi - lo <= 0) break;
        }
        if (!double.IsFinite(F)) F = lo;
        return M < 0 ? -F : F;
    }

    static void AnyPerpendicular(DVec3 v, DVec3 o)
    {
        var ax = Math.Abs(v.x); var ay = Math.Abs(v.y); var az = Math.Abs(v.z);
        if (ax <= ay && ax <= az) o.set(1, 0, 0);
        else if (az <= ay) o.set(0, 0, 1);
        else o.set(0, 1, 0);
        var l2 = v.lengthSq();
        if (l2 == 0) l2 = 1;
        o.addScaledVector(v, -o.dot(v) / l2);
        o.normalize();
    }

    Orbit Finish(double alphaIn)
    {
        alpha = alphaIn;
        a = 1 / alphaIn;
        hyper = alphaIn < 0;
        n = Math.Sqrt(gm) * Math.Pow(Math.Abs(alphaIn), 1.5);
        rp = p / (1 + e);
        ome = rp * alphaIn;
        b = Math.Sqrt(p * Math.Abs(a));
        return this;
    }

    public static Orbit FromState(DVec3 r, DVec3 v, double gm, double t = 0)
    {
        var o = new Orbit();
        o.gm = gm; o.t0 = t;
        var R = r.clone();
        var V = v.clone();
        if (!double.IsFinite(R.x + R.y + R.z)) R.set(1, 0, 0);
        if (!double.IsFinite(V.x + V.y + V.z)) V.set(0, 0, 0);
        var r0 = R.length();
        if (!(r0 > 0)) { R.set(1e-3, 0, 0); r0 = 1e-3; }
        var rhat = R.clone().multiplyScalar(1 / r0);
        var h = new DVec3().crossVectors(R, V);
        var hmag = h.length();
        var hmin = RadialEps * Math.Sqrt(gm * r0);
        if (!(hmag >= hmin))
        {
            var vr = V.dot(rhat);
            var u = V.clone().addScaledVector(rhat, -vr);
            if (u.length() > 1e-12 * Math.Sqrt(gm / r0) && hmag > 0) u.normalize();
            else AnyPerpendicular(rhat, u);
            V.copy(rhat).multiplyScalar(vr).addScaledVector(u, hmin / r0);
            h.crossVectors(R, V);
            hmag = h.length();
        }
        var alpha = 2 / r0 - V.lengthSq() / gm;
        if (Math.Abs(alpha * r0) < ParabolicEps)
        {
            alpha = (alpha > 0 ? ParabolicEps : -ParabolicEps) / r0;
            var speed = Math.Sqrt(gm * (2 / r0 - alpha));
            V.multiplyScalar(speed / V.length());
            h.crossVectors(R, V);
            hmag = h.length();
        }
        o.p = hmag * hmag / gm;
        o.H.copy(h).multiplyScalar(1 / hmag);
        var evec = new DVec3().crossVectors(V, h).multiplyScalar(1 / gm).sub(rhat);
        var pa = o.p * alpha;
        var ee = Math.Abs(pa) < 1e-3 ? Math.Sqrt(Math.Max(0, 1 - pa)) : evec.length();
        if (alpha < 0 && ee < 1) ee = 1;
        if (alpha > 0 && ee > 1) ee = 1;
        var rv = R.dot(V);
        if (ee < CircularE)
        {
            o.e = 0;
            alpha = 1 / o.p;
            o.P.copy(rhat).addScaledVector(o.H, -rhat.dot(o.H)).normalize();
            o.Q.crossVectors(o.H, o.P);
            o.M0 = 0;
            return o.Finish(alpha);
        }
        o.e = ee;
        o.P.copy(evec).addScaledVector(o.H, -evec.dot(o.H));
        if (o.P.lengthSq() > 0) o.P.normalize();
        else o.P.copy(rhat).addScaledVector(o.H, -rhat.dot(o.H)).normalize();
        o.Q.crossVectors(o.H, o.P);
        o.Finish(alpha);
        if (!o.hyper)
        {
            var E = Math.Atan2(rv * Math.Sqrt(alpha / gm), 1 - r0 * alpha);
            o.M0 = o.ome * Math.Sin(E) + XMinusSin(E);
        }
        else
        {
            var F = Math.Asinh(rv * Math.Sqrt(-alpha / gm) / ee);
            o.M0 = -o.ome * Math.Sinh(F) + SinhMinusX(F);
        }
        return o;
    }

    public static Orbit FromElements(double a, double e, double iDeg, double lanDeg, double argpDeg, double M0, double gm, double t0 = 0)
    {
        var o = new Orbit();
        o.gm = gm; o.t0 = t0; o.M0 = M0;
        if (e > 1 && a > 0) a = -a;
        if (Math.Abs(e - 1) < 1e-12) e = e < 1 ? 1 - 1e-12 : 1 + 1e-12;
        o.e = e;
        o.p = a * (1 - e * e);
        var d = Math.PI / 180;
        var ci = Math.Cos(iDeg * d); var si = Math.Sin(iDeg * d);
        var cO = Math.Cos(lanDeg * d); var sO = Math.Sin(lanDeg * d);
        var cw = Math.Cos(argpDeg * d); var sw = Math.Sin(argpDeg * d);
        var px = cO * cw - sO * sw * ci; var py = sO * cw + cO * sw * ci; var pz = sw * si;
        var qx = -cO * sw - sO * cw * ci; var qy = -sO * sw + cO * cw * ci; var qz = cw * si;
        o.P.set(px, pz, -py).normalize();
        o.Q.set(qx, qz, -qy);
        o.H.crossVectors(o.P, o.Q).normalize();
        o.Q.crossVectors(o.H, o.P);
        return o.Finish(1 / a);
    }

    public double MeanAnomalyAt(double t)
    {
        var M = M0 + n * (t - t0);
        if (!hyper) M -= Num.Tau * Num.JsRound(M / Num.Tau);
        return M;
    }

    public double AnomalyAt(double t)
    {
        var M = MeanAnomalyAt(t);
        return hyper ? SolveKeplerHyperbolic(M, e, ome) : SolveKeplerElliptic(M, e, ome);
    }

    public void State(double t, DVec3? r, DVec3? v)
    {
        var A = AnomalyAt(t);
        double x, y, rr, vx = 0, vy = 0;
        if (!hyper)
        {
            var s = Math.Sin(0.5 * A); var s2 = 2 * s * s; var sE = Math.Sin(A);
            x = rp - a * s2;
            y = b * sE;
            rr = rp + a * e * s2;
            if (v != null)
            {
                var k = 1 / Math.Max(rr, 1e-300);
                vx = -Math.Sqrt(gm * a) * k * sE;
                vy = Math.Sqrt(gm * p) * k * Math.Cos(A);
            }
        }
        else
        {
            var s = Math.Sinh(0.5 * A); var s2 = 2 * s * s; var sF = Math.Sinh(A);
            x = rp + a * s2;
            y = b * sF;
            rr = rp - a * e * s2;
            if (v != null)
            {
                var k = 1 / Math.Max(rr, 1e-300);
                vx = -Math.Sqrt(-gm * a) * k * sF;
                vy = Math.Sqrt(gm * p) * k * Math.Cosh(A);
            }
        }
        if (r != null) r.set(P.x * x + Q.x * y, P.y * x + Q.y * y, P.z * x + Q.z * y);
        if (v != null) v.set(P.x * vx + Q.x * vy, P.y * vx + Q.y * vy, P.z * vx + Q.z * vy);
    }

    public (DVec3 r, DVec3 v) StateAt(double t)
    {
        var r = new DVec3(); var v = new DVec3();
        State(t, r, v);
        return (r, v);
    }

    public DVec3 PosAt(double t, DVec3? o = null) { o ??= new DVec3(); State(t, o, null); return o; }
    public DVec3 VelAt(double t, DVec3? o = null) { o ??= new DVec3(); State(t, null, o); return o; }

    public double TrueAnomalyAt(double t)
    {
        var A = AnomalyAt(t);
        if (!hyper)
            return 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(0.5 * A), Math.Sqrt(Math.Max(ome, 0)) * Math.Cos(0.5 * A));
        return 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Tanh(0.5 * A), Math.Sqrt(Math.Max(-ome, 0)));
    }

    public double RadiusAtTrueAnomaly(double nu)
    {
        var d = 1 + e * Math.Cos(nu);
        return d > 1e-12 ? p / d : double.PositiveInfinity;
    }

    public DVec3 PosAtTrueAnomaly(double nu, DVec3? o = null)
    {
        o ??= new DVec3();
        var r = RadiusAtTrueAnomaly(nu);
        var x = r * Math.Cos(nu); var y = r * Math.Sin(nu);
        return o.set(P.x * x + Q.x * y, P.y * x + Q.y * y, P.z * x + Q.z * y);
    }

    public DVec3 VelAtTrueAnomaly(double nu, DVec3? o = null)
    {
        o ??= new DVec3();
        var k = Math.Sqrt(gm / p);
        var x = -k * Math.Sin(nu); var y = k * (e + Math.Cos(nu));
        return o.set(P.x * x + Q.x * y, P.y * x + Q.y * y, P.z * x + Q.z * y);
    }

    public double TrueAnomalyAtRadius(double r)
    {
        if (e == 0) return 0;
        var c = (p / r - 1) / e;
        if (!(c < 1)) c = 1; else if (c < -1) c = -1;
        return Math.Acos(c);
    }

    public double MaxTrueAnomaly => hyper ? Math.Acos(-1 / e) : Math.PI;

    public double TimeAtTrueAnomaly(double nu, double? afterIn = null)
    {
        var after = afterIn ?? t0;
        if (!hyper)
        {
            var E = 2 * Math.Atan2(Math.Sqrt(Math.Max(ome, 0)) * Math.Sin(0.5 * nu), Math.Sqrt(1 + e) * Math.Cos(0.5 * nu));
            return NextTimeAtMean(ome * Math.Sin(E) + XMinusSin(E), after);
        }
        nu -= Num.Tau * Num.JsRound(nu / Num.Tau);
        var x = Math.Sqrt(-ome / (1 + e)) * Math.Tan(0.5 * nu);
        if (!(Math.Abs(x) < 1)) return nu > 0 ? double.PositiveInfinity : double.NegativeInfinity;
        var F = 2 * Math.Atanh(x);
        var M = -ome * Math.Sinh(F) + SinhMinusX(F);
        return t0 + (M - M0) / n;
    }

    double NextTimeAtMean(double M, double after)
    {
        var T = Num.Tau / n;
        var tt = t0 + (M - M0) / n;
        if (double.IsFinite(after))
        {
            tt += Math.Ceiling((after - tt) / T) * T;
            if (tt < after) tt = tt < after - 1e-9 * T ? tt + T : after;
            else if (tt - T >= after) tt -= T;
        }
        return tt;
    }

    public double TimeAtRadius(double r, bool inbound = false, double? afterIn = null)
    {
        var after = afterIn ?? t0;
        if (e == 0) return after;
        var s2 = (r - rp) / (2 * Math.Abs(a) * e);
        if (!(s2 > 0)) s2 = 0;
        if (!hyper)
        {
            var E = 2 * Math.Asin(Math.Sqrt(s2 > 1 ? 1 : s2));
            if (inbound) E = -E;
            return NextTimeAtMean(ome * Math.Sin(E) + XMinusSin(E), after);
        }
        var F = 2 * Math.Asinh(Math.Sqrt(s2));
        if (inbound) F = -F;
        return t0 + (-ome * Math.Sinh(F) + SinhMinusX(F) - M0) / n;
    }

    public double Periapsis => rp;
    public double Apoapsis => hyper ? double.PositiveInfinity : a * (1 + e);
    public double Period => hyper ? double.PositiveInfinity : Num.Tau / n;
    public double Energy => -0.5 * gm * alpha;
    public double Inclination
    {
        get
        {
            var yy = H.y;
            return Math.Acos(yy > 1 ? 1 : yy < -1 ? -1 : yy);
        }
    }

    public double TimeToPeriapsis(double t) => TimeAtTrueAnomaly(0, t) - t;
    public double TimeToApoapsis(double t) => hyper ? double.PositiveInfinity : TimeAtTrueAnomaly(Math.PI, t) - t;

    public Orbit Clone() => FromJson(ToJson());

    public OrbitJson ToJson() => new()
    {
        gm = gm, e = e, p = p, a = a, M0 = M0, t0 = t0,
        P = P.toArray(), Q = Q.toArray(), H = H.toArray(),
    };

    public static Orbit FromJson(OrbitJson j)
    {
        var o = new Orbit();
        o.gm = j.gm; o.e = j.e; o.p = j.p; o.M0 = j.M0; o.t0 = j.t0;
        o.P.fromArray(j.P); o.Q.fromArray(j.Q); o.H.fromArray(j.H);
        return o.Finish(1 / j.a);
    }
}

public sealed class OrbitJson
{
    public double gm, e, p, a, M0, t0;
    public double[] P { get; set; } = new double[3];
    public double[] Q { get; set; } = new double[3];
    public double[] H { get; set; } = new double[3];
}

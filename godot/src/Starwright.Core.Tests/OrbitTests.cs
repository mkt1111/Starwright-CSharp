using Starwright.Core;
using Xunit;

namespace Starwright.Core.Tests;

/// <summary>Port of tests/unit/orbit.test.js. The cases and tolerances are the browser game's.</summary>
public class OrbitTests
{
    const double Gm = 3.93216e12;

    static DVec3 V(double x, double y, double z) => new(x, y, z);
    static double Circ(double r) => Math.Sqrt(Gm / r);
    static double Energy(DVec3 r, DVec3 v, double gm) => 0.5 * v.lengthSq() - gm / r.length();

    static void Close(double actual, double expected, double rel = 1e-9, double abs = 0, string msg = "")
    {
        var tol = Math.Max(abs, rel * Math.Max(Math.Abs(expected), Math.Abs(actual)));
        Assert.True(Math.Abs(actual - expected) <= tol, $"{msg} expected {expected}, got {actual} (tol {tol})");
    }

    static void VecClose(DVec3 a, DVec3 b, double tol, string msg = "") =>
        Assert.True(a.distanceTo(b) <= tol, $"{msg} |d| = {a.distanceTo(b)} > {tol} a={a} b={b}");

    static void FiniteOrbit(Orbit o)
    {
        foreach (var (name, val) in new[] { ("gm", o.gm), ("e", o.e), ("p", o.p), ("a", o.a), ("n", o.n), ("M0", o.M0), ("t0", o.t0) })
            Assert.True(double.IsFinite(val), $"{name} = {val}");
        foreach (var (name, vec) in new[] { ("P", o.P), ("Q", o.Q), ("H", o.H) })
        {
            Assert.True(double.IsFinite(vec.x + vec.y + vec.z), name);
            Close(vec.length(), 1, 1e-9, 0, name);
        }
    }

    static Orbit RoundTrip(DVec3 r, DVec3 v, double gm = Gm, double t = 123.5, double rel = 1e-9)
    {
        var o = Orbit.FromState(r, v, gm, t);
        FiniteOrbit(o);
        var s = o.StateAt(t);
        VecClose(s.r, r, rel * r.length(), "r");
        VecClose(s.v, v, rel * Math.Max(v.length(), Math.Sqrt(gm / r.length())), "v");
        return o;
    }

    [Fact]
    public void Noise_is_deterministic_and_bounded()
    {
        var a = NoiseField.Make(42);
        var b = NoiseField.Make(42);
        var c = NoiseField.Make(43);
        var differs = false;
        double min = 1, max = -1;
        for (var i = 0; i < 4000; i++)
        {
            var x = i * 0.173 - 300; var y = i * 0.071 + 5; var z = Math.Sin(i) * 40;
            var n = a.Noise3(x, y, z);
            Assert.Equal(n, b.Noise3(x, y, z));
            if (n != c.Noise3(x, y, z)) differs = true;
            var f = a.Fbm(x, y, z);
            Assert.InRange(n, -1, 1);
            Assert.InRange(f, -1, 1);
            Assert.Equal(f, b.Fbm(x, y, z));
            min = Math.Min(min, n); max = Math.Max(max, n);
        }
        Assert.True(differs);
        Assert.True(min < -0.4 && max > 0.4, $"range {min}..{max}");
        Assert.True(Math.Abs(a.Noise3(1.2, 3.4, 5.6) - a.Noise3(1.2001, 3.4, 5.6)) < 1e-3);
    }

    [Fact]
    public void Circular_prograde_equatorial()
    {
        var r = V(7.5e5, 0, 0);
        var v = V(0, 0, -Circ(7.5e5));
        var o = RoundTrip(r, v, Gm, 50, 1e-8);
        Assert.True(o.e < 1e-9);
        Close(o.a, 7.5e5);
        Close(o.Inclination, 0, 0, 1e-12);
        Close(o.H.y, 1, 1e-12);
        var s = o.StateAt(50 + o.Period / 4);
        VecClose(s.r, V(0, 0, -7.5e5), 1e-3);
    }

    [Fact]
    public void Eccentric_round_trip()
    {
        var r = V(8e5, 0, 2e5);
        var v = V(300, 0, -2600);
        var o = RoundTrip(r, v);
        Assert.True(o.e > 0.05 && o.e < 1 && !o.hyper);
        foreach (var dt in new[] { 0d, 100, 1000, 5000, -777, 1e6 })
        {
            var s = o.StateAt(123.5 + dt);
            var o2 = Orbit.FromState(s.r, s.v, Gm, 123.5 + dt);
            Close(o2.e, o.e); Close(o2.a, o.a);
            VecClose(o2.P, o.P, 1e-8); VecClose(o2.H, o.H, 1e-10);
        }
    }

    [Fact]
    public void Inclined_polar_and_retrograde()
    {
        var sp = Circ(9e5);
        var o = RoundTrip(V(9e5, 0, 0), V(0, sp * Math.Sin(0.6), -sp * Math.Cos(0.6)), Gm, 0, 1e-8);
        Close(o.Inclination, 0.6);
        var polar = RoundTrip(V(0, 9e5, 0), V(sp * 1.1, 0, 0));
        Close(polar.Inclination, Math.PI / 2);
        var retro = RoundTrip(V(7.5e5, 0, 0), V(0, 0, Circ(7.5e5) * 1.15));
        Close(retro.Inclination, Math.PI);
        Close(retro.H.y, -1, 1e-12);
        Assert.True(retro.StateAt(123.5 + 60).r.z > 0);
    }

    [Fact]
    public void Hyperbolic_and_degenerate_states_stay_finite()
    {
        var o = RoundTrip(V(7.5e5, 1e5, 0), V(500, 200, -Circ(7.5e5) * 1.8));
        Assert.True(o.hyper && o.e > 1 && o.a < 0);
        Assert.Equal(double.PositiveInfinity, o.Apoapsis);
        Assert.Equal(double.PositiveInfinity, o.Period);
        var cases = new[]
        {
            (V(7e5, 0, 0), V(1500, 0, 0)),
            (V(7e5, 0, 0), V(0, 0, 0)),
            (V(7e5, 0, 0), V(0, 0, -Math.Sqrt(2 * Gm / 7e5))),
            (V(0, 0, 0), V(0, 0, 0)),
        };
        foreach (var (r, v) in cases)
        {
            var orb = Orbit.FromState(r, v, Gm, 10);
            FiniteOrbit(orb);
            var s = orb.StateAt(10);
            Assert.True(double.IsFinite(s.r.x + s.v.x));
        }
        var up = Orbit.FromState(V(7e5, 0, 0), V(1500, 0, 0), Gm, 0);
        var eps = 1500.0 * 1500 / 2 - Gm / 7e5;
        Close(up.Apoapsis, -Gm / eps);
    }

    [Fact]
    public void Period_and_apsides_match_closed_forms()
    {
        const double rp = 7.5e5, ra = 1.4e7;
        var a = (rp + ra) / 2;
        var vp = Math.Sqrt(Gm * (2 / rp - 1 / a));
        var o = Orbit.FromState(V(rp, 0, 0), V(0, 0, -vp), Gm, 0);
        Close(o.Periapsis, rp, 1e-10);
        Close(o.Apoapsis, ra);
        Close(o.e, (ra - rp) / (ra + rp), 1e-10);
        Close(o.Period, 2 * Math.PI * Math.Sqrt(a * a * a / Gm));
        Close(o.TimeToApoapsis(0), o.Period / 2);
        VecClose(o.StateAt(o.Period / 2).r, V(-ra, 0, 0), 1e-2);
    }

    [Fact]
    public void FromElements_uses_the_Y_up_frame()
    {
        var c = Orbit.FromElements(1e7, 0, 0, 0, 0, 0, Gm);
        VecClose(c.PosAt(0), V(1e7, 0, 0), 1e-3);
        Assert.True(c.VelAt(0).z < 0);
        VecClose(c.PosAt(c.Period / 4), V(0, 0, -1e7), 1e-2);
        VecClose(c.H, V(0, 1, 0), 1e-12);
        var o = Orbit.FromElements(4.4e7, 0.3, 25, 40, 30, 0.5, Gm, 77);
        Close(o.Inclination, 25 * Math.PI / 180, 1e-12);
        Close(o.Periapsis, 4.4e7 * 0.7, 1e-12);
        var hy = Orbit.FromElements(-2e6, 1.7, 10, 5, 80, -1, Gm);
        Assert.True(hy.hyper);
        Close(hy.Periapsis, 2e6 * 0.7, 1e-12);
    }

    [Fact]
    public void Kepler_solvers_converge()
    {
        foreach (var e in new[] { 0d, 0.1, 0.5, 0.9, 0.99, 0.999, 1 - 1e-12 })
        {
            for (var k = -20; k <= 20; k++)
            {
                var M = Math.PI * k / 20;
                var E = Orbit.SolveKeplerElliptic(M, e);
                Close(E - e * Math.Sin(E), M, 1e-12, 1e-13, $"e={e} M={M}");
            }
        }
        foreach (var e in new[] { 1 + 1e-12, 1.01, 1.5, 3, 10, 50 })
        {
            foreach (var M in new[] { 0d, 1e-6, 0.1, 1, 5, 30, 500, 1e5, -2, -777 })
            {
                var F = Orbit.SolveKeplerHyperbolic(M, e);
                Close(e * Math.Sinh(F) - F, M, 1e-11, 1e-13, $"e={e} M={M}");
            }
        }
    }

    [Fact]
    public void Json_round_trip()
    {
        var o = Orbit.FromElements(4.4e7, 0.3, 25, 40, 30, 0.5, Gm, 77);
        var o2 = Orbit.FromJson(o.ToJson());
        Assert.Equal(o.hyper, o2.hyper);
        VecClose(o.StateAt(999).r, o2.StateAt(999).r, 1e-6);
        VecClose(o.Clone().PosAt(321), o.PosAt(321), 1e-6);
    }
}

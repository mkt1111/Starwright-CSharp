using Starwright.Core;
using Xunit;

namespace Starwright.Core.Tests;

public class GameTests
{
    [Fact]
    public void System_has_nineteen_bodies_and_a_prograde_home_world()
    {
        var sys = HelionSystem.System;
        Assert.Equal(19, sys.bodies.Length);
        Assert.Equal("helion", sys.star.id);
        Assert.Equal("verda", sys.home.id);
        Assert.Equal("Starwright Flight Center", sys.launchSite.name);
        Assert.True(sys.launchSite.dir.distanceTo(new DVec3(1, 0, 0)) < 1e-12);
        Assert.Equal(0, sys.home.TerrainHeight(sys.launchSite.dir));
        Assert.False(sys.home.BiomeAt(sys.launchSite.dir).water);
        var verda = sys.home;
        Assert.Equal(9.6 * verda.radius * verda.radius, verda.gm, 1);
        Assert.True(verda.VelAt(0).z < 0);
        Assert.True(verda.soi > 7e7 && verda.soi < 1e8);
        var pell = sys.byId["pell"];
        Assert.True(pell.soi > 2e6 && pell.soi < 3e6);
        foreach (var b in sys.bodies)
        {
            if (b.parent == null) { Assert.Equal(double.PositiveInfinity, b.soi); continue; }
            var expect = b.orbit!.a * Math.Pow(b.gm / b.parent.gm, 0.4);
            Assert.Equal(expect, b.soi, expect * 1e-12);
            Assert.True(b.orbit.Periapsis > b.parent.radius * 2, b.id);
        }
        Assert.True(verda.atmo!.oxygen);
        Assert.Equal(72000, verda.atmo.height);
        Assert.True(verda.Density(0) > 1.2 && verda.Density(0) < 1.23);
        Assert.Equal(0, verda.Density(80000));
    }

    [Fact]
    public void Stock_rockets_meet_the_design_budgets()
    {
        Assert.Equal(186, PartCatalog.List.Count);
        var sky = Craft.ByName("Skylark II")!;
        var home = HelionSystem.System.home;
        var dv = sky.DeltaV(home);
        Assert.True(dv.vac >= 3600, $"Skylark dv {dv.vac}");
        var ss = sky.Stats(home);
        Assert.True(ss[0].twr >= 1.3 && ss[0].twr <= 1.9, $"TWR {ss[0].twr}");
        var pell = Craft.ByName("Pell Runner")!;
        Assert.True(pell.DeltaV(home).vac >= 5600, $"Pell Runner dv {pell.DeltaV(home).vac}");
        Assert.Equal(3, pell.Stats(home).Count(s => s.dvVac > 0));
        var hopper = Craft.ByName("Hopper I")!;
        Assert.True(hopper.Mass() > 1000);
        Assert.Contains(hopper.parts, p => p.type == "pod_acorn");
        Assert.Contains(hopper.parts, p => p.type == "srb_ember");
        Assert.Equal(16, Craft.Stock.Count);
    }

    [Fact]
    public void Hopper_lifts_off_stages_and_can_be_saved()
    {
        var world = new World();
        var v = world.Launch(Craft.ByName("Hopper I")!, new[] { "Pip" });
        Assert.Equal("prelaunch", v.situation);
        Assert.True(v.landed);
        Assert.Contains("Pip", v.Control!.crew);
        var alt0 = v.altitude;
        v.ctrl.throttle = 1;
        world.Stage();
        Assert.Contains(v.Engines, e => e.ignited);
        for (var i = 0; i < 200; i++) world.Step(0.05);
        Assert.False(v.dead);
        Assert.True(v.launched);
        Assert.True(v.altitude > alt0 + 50, $"altitude {v.altitude}");
        Assert.True(v.verticalSpeed > 0);
        var json = SaveGame.Write(world);
        var loaded = SaveGame.Read(json);
        Assert.Equal(world.ut, loaded.ut, 3);
        Assert.Equal(v.name, loaded.active!.name);
        Assert.True(Math.Abs(loaded.active.altitude - v.altitude) < 1);
        Assert.Equal(v.ctrl.throttle, loaded.active.ctrl.throttle);
    }

    [Fact]
    public void A_coasting_orbit_holds_its_altitude()
    {
        var home = HelionSystem.System.home;
        var r = home.radius + 200000;
        var speed = Math.Sqrt(home.gm / r);
        var world = new World();
        var v = world.Spawn(Craft.ByName("Skylark II")!, home, new DVec3(r, 0, 0), new DVec3(0, 0, -speed));
        v.ctrl.throttle = 0;
        var alt = v.altitude;
        world.SetWarp(0);
        for (var i = 0; i < 40; i++) world.Step(0.05);
        Assert.False(v.dead);
        Assert.InRange(v.altitude, alt - 50, alt + 50);
        Assert.Equal("orbit", v.situation);
    }

    [Fact]
    public void Soi_exit_hands_the_vessel_to_the_parent()
    {
        var pell = HelionSystem.System.byId["pell"];
        var pos = new DVec3(pell.soi * 1.01, 0, 0);
        var vel = new DVec3(20, 0, 0);
        var tr = Trajectory.CheckSoiTransition(pell, pos, vel, 0);
        Assert.NotNull(tr);
        Assert.Equal("verda", tr.Value.body.id);
        var basis = Trajectory.ManeuverBasis(new DVec3(1, 0, 0), new DVec3(0, 0, -1));
        Assert.True(basis.prograde.z < 0);
        Assert.True(basis.normal.y > 0.9);
    }

    [Fact]
    public void About_keeps_the_browser_sections_and_the_port_note()
    {
        var ids = AboutContent.Sections.Select(s => s.id).ToArray();
        Assert.Equal(new[] { "what", "ksp1", "ksp2", "made", "legal", "port" }, ids);
        var all = string.Join("\n", AboutContent.Sections.SelectMany(s => s.paras.Prepend(s.title)));
        foreach (var word in new[] { "Kerbal Space Program", "Kerbal Space Program 2", "2011", "2015", "2019", "2023", "2024", "Jerry Smith", "Claude", "Grok", "MIT License" })
            Assert.Contains(word, all);
        var legal = string.Join(" ", AboutContent.Sections.First(s => s.id == "legal").paras);
        Assert.Contains("not affiliated with or endorsed by the makers of Kerbal Space Program or by SpaceX", legal);
        Assert.Contains("not affiliated with or endorsed by Anthropic or xAI", legal);
        Assert.Contains(AboutContent.RepoUrl, legal);
        Assert.Contains("Godot 4.7", AboutContent.Sections.First(s => s.id == "port").paras[0]);
        foreach (var s in AboutContent.Sections)
            foreach (var p in s.paras)
            {
                var plain = System.Text.RegularExpressions.Regex.Replace(p, @"\[[^\]]+\]\(https://[^)\s]+\)", "");
                Assert.DoesNotMatch(@"[—–*_`#]", plain);
            }
    }

    [Fact]
    public void Wheel_normalisation_matches_the_browser_helper()
    {
        var w = InputMath.NormalizeWheel(0, 100, 0, false, false);
        Assert.InRange(w.zoom, 0.8, 0.81);
        var pinch = InputMath.NormalizeWheel(0, 10, 0, false, true);
        Assert.True(pinch.pinch);
        Assert.True(pinch.zoom < 1);
        var shifted = InputMath.NormalizeWheel(40, 0, 0, true, false);
        Assert.Equal(0, shifted.dx);
        Assert.Equal(40, shifted.dy);
    }

    [Fact]
    public void Warp_stays_in_physics_while_the_engine_burns()
    {
        var world = new World();
        var v = world.Launch(Craft.ByName("Mote Sounder")!);
        v.ctrl.throttle = 1;
        world.Stage();
        Assert.True(world.MaxWarpIndex() <= World.PhysicsWarpMax);
        world.SetWarp(10);
        Assert.True(world.warpIndex <= World.PhysicsWarpMax);
    }
}

using Starwright.Core;
using Xunit;

namespace Starwright.Core.Tests;

/// <summary>
/// The flight camera must stay a valid perspective frustum. Godot's light culler
/// (create_frustum_points) rejects the frame when near/far or the look-at basis is degenerate,
/// and Forward+ then presents a black window.
/// </summary>
public class CameraTests
{
    [Theory]
    [InlineData("Hopper I")]
    [InlineData("Skylark II")]
    public void Initial_flight_camera_is_a_valid_frustum(string craftName)
    {
        var world = new World();
        var vessel = world.Launch(Craft.ByName(craftName)!, new[] { "Pip" });
        var up = vessel.pos.clone();
        Assert.True(up.length() > 1000);
        up.normalize();

        var chase = RenderCamera.Craft(true, 28, 0.4, 0.35, vessel.q, up);
        AssertValid(chase, craftName + " chase");

        foreach (var yaw in new[] { -2.0, -0.4, 0, 0.8, 2.4 })
        foreach (var pitch in new[] { -1.2, -0.2, 0, 0.35, 1.2 })
        {
            var orbit = RenderCamera.Craft(false, 40, yaw, pitch, vessel.q, up);
            AssertValid(orbit, craftName + $" orbit y={yaw} p={pitch}");
        }

        var home = RenderCamera.PlaceBody(vessel.pos.clone().negate(), vessel.body.radius);
        Assert.True(home.Visible);
        Assert.True(home.Fits(RenderCamera.Far, out var why), why);

        var toStar = HelionSystem.System.star.GlobalPosAt(world.ut)
            .sub(vessel.body.GlobalPosAt(world.ut))
            .sub(vessel.pos);
        var star = RenderCamera.PlaceBody(toStar, HelionSystem.System.star.radius);
        Assert.True(star.Visible);
        Assert.True(star.Fits(RenderCamera.Far, out why), why);
    }

    [Fact]
    public void Look_up_falls_back_when_the_preferred_up_is_parallel()
    {
        var forward = new DVec3(0, 10, 0);
        var up = RenderCamera.SafeUp(forward, new DVec3(0, 1, 0));
        var f = forward.clone().normalize();
        Assert.True(Math.Abs(f.dot(up)) < 0.2);
        var frame = RenderCamera.Craft(true, 30, 0, 0, new DQuat(), new DVec3(0, 0, 1));
        AssertValid(frame, "identity craft");
    }

    static void AssertValid(RenderCamera.Frame frame, string label)
    {
        Assert.True(frame.IsValid(out var why), label + ": " + why);
        Assert.Equal(RenderCamera.Near, frame.Near);
        Assert.Equal(RenderCamera.Far, frame.Far);
        Assert.True(frame.Far <= RenderCamera.MaxFar);
    }
}

namespace Starwright.Core;

/// <summary>
/// Render-space camera and body placement. The simulation stays in metres. Everything this
/// returns is in a floating origin at the active craft, inside a camera whose far plane is
/// small enough that Godot's light culler can build a frustum in single precision.
/// </summary>
public static class RenderCamera
{
    public const double Near = 0.5;
    public const double Far = 100_000;
    public const double FovDegrees = 70;
    public const double MaxFar = 1_000_000;
    public const double MaxNearFarRatio = 1_000_000;

    public readonly struct Frame
    {
        public readonly DVec3 Position;
        public readonly DVec3 Target;
        public readonly DVec3 Up;
        public readonly double Near;
        public readonly double Far;
        public readonly double Fov;

        public Frame(DVec3 position, DVec3 target, DVec3 up, double near, double far, double fov)
        {
            Position = position; Target = target; Up = up; Near = near; Far = far; Fov = fov;
        }

        public bool IsValid(out string why)
        {
            why = "";
            if (!Finite(Position) || !Finite(Target) || !Finite(Up)) { why = "non-finite transform"; return false; }
            if (!(Near >= 0.05 && Near <= 10 && double.IsFinite(Near))) { why = "near " + Near; return false; }
            if (!(Far > Near && Far <= MaxFar && double.IsFinite(Far))) { why = "far " + Far; return false; }
            if (Far / Near > MaxNearFarRatio) { why = "near/far ratio"; return false; }
            if (!(Fov >= 20 && Fov <= 120 && double.IsFinite(Fov))) { why = "fov " + Fov; return false; }
            var forward = Target.clone().sub(Position);
            if (!(forward.length() > 1e-3)) { why = "zero look direction"; return false; }
            if (!(Up.length() > 1e-3)) { why = "zero up"; return false; }
            forward.normalize();
            var up = Up.clone().normalize();
            if (Math.Abs(forward.dot(up)) > 0.98) { why = "look parallel to up"; return false; }
            if (Position.length() > 50_000) { why = "camera too far from origin"; return false; }
            return true;
        }
    }

    public readonly struct BodyDraw
    {
        public readonly DVec3 Center;
        public readonly double Radius;
        public readonly bool Visible;

        public BodyDraw(DVec3 center, double radius, bool visible)
        {
            Center = center; Radius = radius; Visible = visible;
        }

        public bool Fits(double far, out string why)
        {
            why = "";
            if (!Visible) return true;
            if (!Finite(Center) || !double.IsFinite(Radius) || !(Radius > 0)) { why = "bad body"; return false; }
            if (Center.length() + Radius > far * 0.98) { why = "body outside far plane"; return false; }
            return true;
        }
    }

    public static Frame Craft(bool chase, double distance, double yaw, double pitch, DQuat craftRotation, DVec3 planetUp)
    {
        distance = Math.Clamp(double.IsFinite(distance) ? distance : 28, 4, 8_000);
        if (!double.IsFinite(yaw)) yaw = 0;
        if (!double.IsFinite(pitch)) pitch = 0.35;
        pitch = Math.Clamp(pitch, -1.2, 1.2);
        DVec3 local;
        if (chase)
            local = new DVec3(0, -distance * 0.15, distance);
        else
        {
            var cp = Math.Cos(pitch);
            local = new DVec3(Math.Sin(yaw) * cp * distance, Math.Sin(pitch) * distance, Math.Cos(yaw) * cp * distance);
            if (local.lengthSq() < 1) local = new DVec3(0, 2, distance);
        }
        var eye = local.applyQuaternion(craftRotation);
        if (!Finite(eye)) eye = new DVec3(0, 4, 20);
        var target = new DVec3(0, 0, 0);
        var forward = target.clone().sub(eye);
        var up = SafeUp(forward, planetUp);
        return new Frame(eye, target, up, Near, Far, FovDegrees);
    }

    /// <summary>An up vector that is not parallel to <paramref name="forward"/>.</summary>
    public static DVec3 SafeUp(DVec3 forward, DVec3 preferred)
    {
        var f = forward.clone();
        if (f.lengthSq() < 1e-12 || !Finite(f)) f = new DVec3(0, 0, -1);
        else f.normalize();
        var u = preferred.clone();
        if (u.lengthSq() < 1e-12 || !Finite(u)) u = new DVec3(0, 1, 0);
        else u.normalize();
        if (Math.Abs(f.dot(u)) > 0.95)
            u = Math.Abs(f.dot(new DVec3(1, 0, 0))) < 0.9 ? new DVec3(1, 0, 0) : new DVec3(0, 0, 1);
        u.addScaledVector(f, -u.dot(f));
        if (u.lengthSq() < 1e-12 || !Finite(u)) u = Math.Abs(f.y) < 0.9 ? new DVec3(0, 1, 0) : new DVec3(1, 0, 0);
        return u.normalize();
    }

    /// <summary>
    /// Where to draw a body so its centre and radius stay inside the camera far plane.
    /// Near the surface the sphere is shrunk (curvature only) and still passes under the craft.
    /// Far away, angular size is kept and the sphere sits on a shell inside the far plane.
    /// </summary>
    public static BodyDraw PlaceBody(DVec3 fromCraftToCenter, double trueRadius, double far = Far)
    {
        if (!(trueRadius > 1) || !(far > 10) || !Finite(fromCraftToCenter))
            return new BodyDraw(new DVec3(), 0, false);
        var dist = fromCraftToCenter.length();
        if (!(dist > 1e-3)) return new BodyDraw(new DVec3(), 0, false);
        var dir = fromCraftToCenter.clone().multiplyScalar(1 / dist);
        var altitude = dist - trueRadius;
        if (altitude < Math.Min(trueRadius * 0.25, far * 0.2))
        {
            var radius = Math.Min(trueRadius, far * 0.35);
            var center = dir.multiplyScalar(Math.Max(0, altitude) + radius);
            return new BodyDraw(center, radius, true);
        }
        var shell = far * 0.55;
        var sinA = Math.Clamp(trueRadius / dist, 0, 0.999);
        var radiusFar = Math.Max(1, shell * sinA);
        var centerFar = dir.multiplyScalar(shell);
        return new BodyDraw(centerFar, radiusFar, true);
    }

    static bool Finite(DVec3 v) => double.IsFinite(v.x) && double.IsFinite(v.y) && double.IsFinite(v.z);
}

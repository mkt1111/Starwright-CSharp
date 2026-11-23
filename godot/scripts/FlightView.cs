using Godot;
using Starwright.Core;

namespace Starwright;

/// <summary>
/// Renders the active vessel with a floating origin at the craft. Bodies are drawn from
/// <see cref="RenderCamera"/> so no mesh and no camera sits at planetary coordinates.
/// The camera is current only while a flight is live, so the title menu does not inherit it.
/// </summary>
public partial class FlightView : Node3D
{
    public World? Sim { get; set; }

    readonly Node3D _craft = new() { Name = "Craft" };
    readonly MeshInstance3D _planet = new() { Name = "Planet" };
    readonly MeshInstance3D _atmo = new() { Name = "Air" };
    readonly MeshInstance3D _star = new() { Name = "Star" };
    readonly Camera3D _cam = new() { Name = "Camera" };
    readonly DirectionalLight3D _sun = new() { Name = "Sun" };
    readonly StandardMaterial3D _planetMat = new();
    readonly StandardMaterial3D _atmoMat = new();
    readonly Dictionary<int, MeshInstance3D> _parts = new();
    readonly List<MeshInstance3D> _flames = new();

    public bool chase = true;
    public float yaw = 0.4f;
    public float pitch = 0.35f;
    public float distance = 28f;
    bool _live;
    string _built = "";

    public override void _Ready()
    {
        var env = new WorldEnvironment();
        var we = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("0b0f14"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.68f, 0.76f),
            AmbientLightEnergy = 0.7f,
        };
        env.Environment = we;
        AddChild(env);

        _sun.LightEnergy = 1.3f;
        _sun.ShadowEnabled = true;
        _sun.DirectionalShadowMaxDistance = 3000;
        _sun.DirectionalShadowBlendSplits = true;
        AddChild(_sun);

        var stars = new MeshInstance3D { Name = "Stars" };
        var pts = new ImmediateMesh();
        pts.SurfaceBegin(Mesh.PrimitiveType.Points);
        var rng = new Random(7);
        const double shell = RenderCamera.Far * 0.8;
        for (var i = 0; i < 500; i++)
        {
            var u = rng.NextDouble();
            var v = rng.NextDouble();
            var theta = 2 * Math.PI * u;
            var phi = Math.Acos(2 * v - 1);
            pts.SurfaceSetColor(new Color(1, 1, 1, (float)(0.35 + rng.NextDouble() * 0.65)));
            pts.SurfaceAddVertex(new Vector3(
                (float)(shell * Math.Sin(phi) * Math.Cos(theta)),
                (float)(shell * Math.Cos(phi)),
                (float)(shell * Math.Sin(phi) * Math.Sin(theta))));
        }
        pts.SurfaceEnd();
        stars.Mesh = pts;
        stars.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            PointSize = 2,
        };
        AddChild(stars);

        _planetMat.Roughness = 0.92f;
        _planet.Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 64, Rings = 32 };
        _planet.MaterialOverride = _planetMat;
        AddChild(_planet);

        _atmoMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _atmoMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _atmoMat.AlbedoColor = new Color(0.45f, 0.65f, 1f, 0.18f);
        _atmo.Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 };
        _atmo.MaterialOverride = _atmoMat;
        AddChild(_atmo);

        _star.Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 16 };
        _star.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color("ffd76a"),
            EmissionEnergyMultiplier = 2,
            AlbedoColor = new Color("ffd76a"),
        };
        AddChild(_star);

        AddChild(_craft);
        AddChild(_cam);
        ApplyProjection();
        _cam.Current = false;
        Aim(_cam, new Vector3(0, 6, 18), Vector3.Zero, Vector3.Up);
    }

    public void SetLive(bool live)
    {
        _live = live;
        Visible = live;
        _sun.Visible = live;
        if (!live)
        {
            _cam.Current = false;
            return;
        }
        ApplyProjection();
        Pose();
        _cam.Current = true;
    }

    public void ForgetCraft()
    {
        foreach (var n in _parts.Values) n.QueueFree();
        _parts.Clear();
        foreach (var f in _flames) f.QueueFree();
        _flames.Clear();
        _built = "";
    }

    public override void _Process(double delta)
    {
        if (!_live) return;
        Pose();
    }

    void ApplyProjection()
    {
        _cam.Projection = Camera3D.ProjectionType.Perspective;
        _cam.Fov = (float)RenderCamera.FovDegrees;
        _cam.Near = (float)RenderCamera.Near;
        _cam.Far = (float)RenderCamera.Far;
    }

    void Pose()
    {
        ApplyProjection();
        var v = Sim?.active;
        if (v == null || (v.dead && v.parts.Count == 0) || !FiniteVec(v.pos) || !FiniteQuat(v.q))
        {
            Aim(_cam, new Vector3(0, 6, 18), Vector3.Zero, Vector3.Up);
            _planet.Visible = false;
            _atmo.Visible = false;
            _star.Visible = false;
            return;
        }

        var key = v.id + ":" + v.parts.Count;
        if (key != _built) Rebuild(v);

        var up = v.pos.clone();
        if (up.lengthSq() < 1) up.set(0, 1, 0); else up.normalize();
        var frame = RenderCamera.Craft(chase, distance, yaw, pitch, v.q, up);
        if (!frame.IsValid(out _))
            frame = RenderCamera.Craft(true, 28, 0, 0.3, new DQuat(), new DVec3(0, 1, 0));
        Aim(_cam, ToGodot(frame.Position), ToGodot(frame.Target), ToGodot(frame.Up));

        var bodyDraw = RenderCamera.PlaceBody(v.pos.clone().negate(), v.body.radius);
        Place(_planet, bodyDraw);
        var col = v.body.color;
        _planetMat.AlbedoColor = new Color(((col >> 16) & 255) / 255f, ((col >> 8) & 255) / 255f, (col & 255) / 255f);

        if (v.body.atmo != null && bodyDraw.Visible)
        {
            var bulge = 1.0 + Math.Min(0.08, v.body.atmo.height / Math.Max(v.body.radius, 1));
            var atmo = new RenderCamera.BodyDraw(bodyDraw.Center, bodyDraw.Radius * bulge, true);
            if (atmo.Fits(RenderCamera.Far, out _))
            {
                Place(_atmo, atmo);
                var ac = v.body.atmo.color;
                _atmoMat.AlbedoColor = new Color((float)ac[0], (float)ac[1], (float)ac[2], 0.16f);
            }
            else _atmo.Visible = false;
        }
        else _atmo.Visible = false;

        var star = HelionSystem.System.star;
        var toStar = star.GlobalPosAt(Sim!.ut).sub(v.body.GlobalPosAt(Sim.ut)).sub(v.pos);
        var starDraw = RenderCamera.PlaceBody(toStar, star.radius);
        Place(_star, starDraw);
        if (starDraw.Visible && toStar.lengthSq() > 1)
        {
            var dir = toStar.clone().normalize();
            Aim(_sun, Vector3.Zero, ToGodot(dir.clone().negate()), ToGodot(frame.Up));
        }

        _craft.Quaternion = new Quaternion((float)v.q.x, (float)v.q.y, (float)v.q.z, (float)v.q.w);
        foreach (var p in v.PartList)
        {
            if (!_parts.TryGetValue(p.id, out var node)) continue;
            node.Position = new Vector3((float)(p.pos.x - v.com.x), (float)(p.pos.y - v.com.y), (float)(p.pos.z - v.com.z));
            node.Rotation = new Vector3(0, (float)p.rot, 0);
        }
        var fi = 0;
        foreach (var p in v.PartList)
        {
            if (p.def.engine == null || fi >= _flames.Count) continue;
            var flame = _flames[fi++];
            flame.Visible = p.thrust > 1;
            flame.Position = new Vector3((float)(p.pos.x - v.com.x), (float)(p.pos.y - v.com.y - p.def.h * 0.65), (float)(p.pos.z - v.com.z));
            var s = 0.4f + (float)Math.Min(3, p.thrust / 20000.0);
            flame.Scale = new Vector3(s, s * (0.6f + s), s);
        }
    }

    static void Place(MeshInstance3D mesh, RenderCamera.BodyDraw draw)
    {
        if (!draw.Visible || !draw.Fits(RenderCamera.Far, out _))
        {
            mesh.Visible = false;
            return;
        }
        mesh.Visible = true;
        mesh.Position = ToGodot(draw.Center);
        var r = (float)draw.Radius;
        mesh.Scale = new Vector3(r, r, r);
    }

    static void Aim(Node3D node, Vector3 eye, Vector3 target, Vector3 preferredUp)
    {
        if (!Finite(eye)) eye = new Vector3(0, 6, 18);
        if (!Finite(target)) target = Vector3.Zero;
        var dir = target - eye;
        if (dir.LengthSquared() < 1e-8f) dir = new Vector3(0, 0, -1);
        dir = dir.Normalized();
        var up = preferredUp;
        if (!Finite(up) || up.LengthSquared() < 1e-8f) up = Vector3.Up;
        up = up.Normalized();
        if (Mathf.Abs(dir.Dot(up)) > 0.95f)
            up = Mathf.Abs(dir.Dot(Vector3.Right)) < 0.9f ? Vector3.Right : Vector3.Back;
        up = (up - dir * up.Dot(dir)).Normalized();
        if (!Finite(up) || up.LengthSquared() < 1e-8f) up = Vector3.Up;
        node.Position = eye;
        node.LookAt(eye + dir, up);
    }

    static Vector3 ToGodot(DVec3 v) => new((float)v.x, (float)v.y, (float)v.z);

    static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    static bool FiniteVec(DVec3 v) =>
        double.IsFinite(v.x) && double.IsFinite(v.y) && double.IsFinite(v.z);

    static bool FiniteQuat(DQuat q) =>
        double.IsFinite(q.x) && double.IsFinite(q.y) && double.IsFinite(q.z) && double.IsFinite(q.w);

    void Rebuild(Vessel v)
    {
        ForgetCraft();
        _built = v.id + ":" + v.parts.Count;
        foreach (var p in v.PartList)
        {
            var mi = new MeshInstance3D();
            var d = p.def;
            if (d.attach.stack)
            {
                mi.Mesh = new CylinderMesh
                {
                    TopRadius = (float)Math.Max(0.02, p.rT > 0 ? p.rT : d.r),
                    BottomRadius = (float)Math.Max(0.02, p.rB > 0 ? p.rB : d.r),
                    Height = (float)Math.Max(0.05, d.h),
                    RadialSegments = 20,
                };
            }
            else
            {
                mi.Mesh = new BoxMesh
                {
                    Size = new Vector3(
                        (float)Math.Max(0.06, d.t > 0 ? d.t : 0.2),
                        (float)Math.Max(0.08, d.h),
                        (float)Math.Max(0.08, d.rw > 0 ? d.rw : 0.3)),
                };
            }
            mi.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = PartColor(d),
                Roughness = 0.55f,
                Metallic = d.engine != null ? 0.35f : 0.05f,
            };
            _craft.AddChild(mi);
            _parts[p.id] = mi;
            if (d.engine != null)
            {
                var flame = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = (float)Math.Max(0.15, d.r * 0.7), Height = (float)Math.Max(0.4, d.h * 0.8) },
                    Visible = false,
                };
                flame.MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.85f),
                    EmissionEnabled = true,
                    Emission = new Color("ffb347"),
                    EmissionEnergyMultiplier = 2,
                };
                _craft.AddChild(flame);
                _flames.Add(flame);
            }
        }
        var reach = v.PartList.Count > 0 ? v.PartList.Max(p => Math.Abs(p.pos.y)) : 8;
        distance = Math.Clamp((float)(reach * 2.2 + 10), 12f, 200f);
    }

    static Color PartColor(PartDef d)
    {
        if (d.command != null) return new Color("f3ead7");
        if (d.engine != null) return d.engine.type == "solid" ? new Color("c4552a") : new Color("2a313c");
        if (d.chute != null) return new Color("e07a3d");
        if (d.fin != null || d.wing != null) return new Color("39d0c0");
        if (d.heatshield) return new Color("6b5344");
        if (d.cat == "tanks") return new Color("f4f7fb");
        if (d.solar != null) return new Color("1d3d66");
        return new Color("d5dde6");
    }
}

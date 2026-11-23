using Godot;
using Starwright.Core;

namespace Starwright;

/// <summary>
/// Renders the active vessel and the sphere of its SOI body. The simulation stays in double
/// precision; this node is a floating origin, so the craft sits at the camera and the planet moves.
/// </summary>
public partial class FlightView : Node3D
{
    public World? Sim { get; set; }

    readonly Node3D _craft = new() { Name = "Craft" };
    readonly MeshInstance3D _planet = new() { Name = "Planet" };
    readonly MeshInstance3D _atmo = new() { Name = "Air" };
    readonly MeshInstance3D _star = new() { Name = "Star" };
    readonly Camera3D _cam = new() { Name = "Camera" };
    readonly StandardMaterial3D _planetMat = new();
    readonly StandardMaterial3D _atmoMat = new();
    readonly Dictionary<int, MeshInstance3D> _parts = new();
    readonly List<MeshInstance3D> _flames = new();

    public bool chase = true;
    public float yaw = 0.4f;
    public float pitch = 0.35f;
    public float distance = 28f;
    string _built = "";
    int _stamp;

    public override void _Ready()
    {
        var env = new WorldEnvironment();
        var we = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("0b0f14"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.62f, 0.72f),
            AmbientLightEnergy = 0.55f,
        };
        env.Environment = we;
        AddChild(env);

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-35, 40, 0),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
        };
        AddChild(sun);

        var stars = new MeshInstance3D();
        var pts = new ImmediateMesh();
        pts.SurfaceBegin(Mesh.PrimitiveType.Points);
        var rng = new Random(7);
        for (var i = 0; i < 600; i++)
        {
            var u = rng.NextDouble(); var v = rng.NextDouble();
            var theta = 2 * Math.PI * u;
            var phi = Math.Acos(2 * v - 1);
            var r = 8e6;
            pts.SurfaceSetColor(new Color(1, 1, 1, (float)(0.3 + rng.NextDouble() * 0.7)));
            pts.SurfaceAddVertex(new Vector3((float)(r * Math.Sin(phi) * Math.Cos(theta)), (float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi) * Math.Sin(theta))));
        }
        pts.SurfaceEnd();
        stars.Mesh = pts;
        var starMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            PointSize = 2,
        };
        stars.MaterialOverride = starMat;
        AddChild(stars);

        _planetMat.Roughness = 0.95f;
        _planet.Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 64, Rings = 32 };
        _planet.MaterialOverride = _planetMat;
        AddChild(_planet);

        _atmoMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _atmoMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        _atmoMat.AlbedoColor = new Color(0.45f, 0.65f, 1f, 0.18f);
        _atmo.Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 };
        _atmo.MaterialOverride = _atmoMat;
        AddChild(_atmo);

        var starMat2 = new StandardMaterial3D { EmissionEnabled = true, Emission = new Color("ffd76a"), EmissionEnergyMultiplier = 2 };
        _star.Mesh = new SphereMesh { Radius = 1, Height = 2 };
        _star.MaterialOverride = starMat2;
        AddChild(_star);

        AddChild(_craft);
        AddChild(_cam);
        _cam.Current = true;
        _cam.Far = 2e8f;
    }

    public void ForgetCraft()
    {
        foreach (var n in _parts.Values) n.QueueFree();
        _parts.Clear();
        foreach (var f in _flames) f.QueueFree();
        _flames.Clear();
        _built = "";
        _stamp = -1;
    }

    public override void _Process(double delta)
    {
        var v = Sim?.active;
        if (v == null || v.dead && v.parts.Count == 0)
        {
            return;
        }
        var key = v.id + ":" + v.parts.Count + ":" + v.situation;
        if (key != _built) Rebuild(v);

        var focus = v.pos;
        PlaceBody(_planet, v.body, focus, (float)v.body.radius);
        var col = v.body.color;
        _planetMat.AlbedoColor = Color.FromHtml(((col >> 16) & 255).ToString("x2") + ((col >> 8) & 255).ToString("x2") + (col & 255).ToString("x2"));
        if (v.body.atmo != null)
        {
            _atmo.Visible = true;
            PlaceBody(_atmo, v.body, focus, (float)(v.body.radius + v.body.atmo.height * 0.35));
            var ac = v.body.atmo.color;
            _atmoMat.AlbedoColor = new Color((float)ac[0], (float)ac[1], (float)ac[2], 0.16f);
        }
        else _atmo.Visible = false;

        var star = HelionSystem.System.star;
        var starPos = star.GlobalPosAt(Sim!.ut).sub(v.body.GlobalPosAt(Sim.ut)).sub(focus);
        _star.Position = F(starPos);
        var sr = (float)star.radius;
        _star.Scale = new Vector3(sr, sr, sr);

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
            flame.Scale = new Vector3(s, s, s);
        }

        var dist = distance;
        Vector3 camLocal;
        if (chase)
        {
            camLocal = new Vector3(0, -dist * 0.15f, dist);
        }
        else
        {
            var cp = Mathf.Cos(pitch);
            camLocal = new Vector3(Mathf.Sin(yaw) * cp * dist, Mathf.Sin(pitch) * dist, Mathf.Cos(yaw) * cp * dist);
        }
        var worldCam = _craft.Quaternion * camLocal;
        _cam.Position = worldCam;
        _cam.LookAt(Vector3.Zero, Vector3.Up);
    }

    static void PlaceBody(MeshInstance3D mesh, Body body, DVec3 focus, float radius)
    {
        var c = new DVec3().sub(focus);
        mesh.Position = F(c);
        mesh.Scale = new Vector3(radius, radius, radius);
    }

    static Vector3 F(DVec3 v) => new((float)v.x, (float)v.y, (float)v.z);

    void Rebuild(Vessel v)
    {
        ForgetCraft();
        _built = v.id + ":" + v.parts.Count + ":" + v.situation;
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
                    Size = new Vector3((float)Math.Max(0.06, d.t > 0 ? d.t : 0.2), (float)Math.Max(0.08, d.h), (float)Math.Max(0.08, d.rw > 0 ? d.rw : 0.3)),
                };
            }
            var mat = new StandardMaterial3D { AlbedoColor = PartColor(d), Roughness = 0.55f, Metallic = d.engine != null ? 0.35f : 0.05f };
            mi.MaterialOverride = mat;
            _craft.AddChild(mi);
            _parts[p.id] = mi;
            if (d.engine != null)
            {
                var flame = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = (float)Math.Max(0.15, d.r * 0.7), Height = (float)Math.Max(0.4, d.h * 0.8) },
                    Visible = false,
                };
                var fm = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.85f),
                    EmissionEnabled = true,
                    Emission = new Color("ffb347"),
                    EmissionEnergyMultiplier = 2,
                };
                flame.MaterialOverride = fm;
                _craft.AddChild(flame);
                _flames.Add(flame);
            }
        }
        distance = Math.Max(18f, (float)(v.PartList.Count > 0 ? v.PartList.Max(p => Math.Abs(p.pos.y)) * 2.4 + 8 : 28));
        _stamp++;
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

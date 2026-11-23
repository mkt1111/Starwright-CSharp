using Godot;
using Starwright.Core;

namespace Starwright;

/// <summary>
/// Headless check for the flight camera. Loads Hopper I, then Skylark II, and quits
/// with a failing status if the live Camera3D frustum is degenerate.
/// </summary>
public partial class FlightCheck : Node
{
    FlightView? _view;
    World? _world;
    int _frames;
    bool _skylark;

    public override void _Ready()
    {
        _view = new FlightView { Name = "Flight" };
        AddChild(_view);
        Launch("Hopper I");
    }

    void Launch(string name)
    {
        _world = new World();
        _world.Launch(Craft.ByName(name)!, new[] { "Pip" });
        _view!.Sim = _world;
        _view.ForgetCraft();
        _view.chase = true;
        _view.distance = 28;
        _view.SetLive(true);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == 4 && !Check("Hopper I")) return;
        if (_frames == 5 && !_skylark)
        {
            _skylark = true;
            Launch("Skylark II");
        }
        if (_frames == 9 && Check("Skylark II"))
        {
            GD.Print("CAMERA_OK");
            GetTree().Quit(0);
        }
    }

    bool Check(string label)
    {
        var cam = _view!.GetNode<Camera3D>("Camera");
        var planet = _view.GetNode<MeshInstance3D>("Planet");
        var star = _view.GetNode<MeshInstance3D>("Star");
        var why = Problem(cam, planet, label) ?? ProblemMesh(star, label + " star");
        if (why != null)
        {
            GD.PrintErr("CAMERA_BAD " + label + ": " + why);
            GetTree().Quit(1);
            return false;
        }
        GD.Print($"CAMERA_OK {label} near={cam.Near} far={cam.Far} fov={cam.Fov}");
        return true;
    }

    static string? Problem(Camera3D cam, MeshInstance3D planet, string label)
    {
        if (!(cam.Near >= 0.05f && cam.Far > cam.Near && cam.Far <= (float)RenderCamera.MaxFar))
            return "near/far " + cam.Near + " " + cam.Far;
        if (cam.Far / cam.Near > (float)RenderCamera.MaxNearFarRatio) return "ratio";
        if (!(cam.Fov >= 20 && cam.Fov <= 120)) return "fov " + cam.Fov;
        if (!cam.Current) return "camera is not current";
        var origin = cam.GlobalPosition;
        if (!Finite(origin)) return "camera position";
        if (origin.Length() > 50_000) return "camera far from origin " + origin.Length();
        var basis = cam.GlobalTransform.Basis;
        if (!Finite(basis.X) || !Finite(basis.Y) || !Finite(basis.Z)) return "basis";
        var forward = -basis.Z;
        var up = basis.Y;
        if (forward.LengthSquared() < 1e-8f || up.LengthSquared() < 1e-8f) return "zero basis axis";
        if (Mathf.Abs(forward.Normalized().Dot(up.Normalized())) > 0.98f) return "look parallel to up";
        return ProblemMesh(planet, label + " planet");
    }

    static string? ProblemMesh(MeshInstance3D mesh, string label)
    {
        if (!mesh.Visible) return label + " hidden";
        var p = mesh.GlobalPosition;
        var r = mesh.Scale.X;
        if (!Finite(p) || !float.IsFinite(r) || r <= 0) return label + " transform";
        if (p.Length() + r > (float)RenderCamera.Far * 0.98f) return label + " outside far plane";
        return null;
    }

    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

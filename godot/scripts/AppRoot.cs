using Godot;
using Starwright.Core;

namespace Starwright;

/// <summary>
/// Screen manager: title, About, settings, and the flight. The simulation is Starwright.Core.World.
/// </summary>
public partial class AppRoot : Node
{
    const string SavePath = "user://starwright-flight.json";
    const string SettingsPath = "user://starwright-settings.cfg";

    World? _world;
    FlightView? _flight;
    Synth? _synth;
    CanvasLayer? _ui;
    Control? _screen;
    bool _paused;
    bool _inFlight;
    double _music = 0.45;
    double _sfx = 0.8;
    string _status = "";

    public override void _Ready()
    {
        _synth = new Synth { Name = "Synth" };
        AddChild(_synth);
        LoadSettings();
        _synth.SetVolumes(_sfx, _music);

        var view = new Node3D { Name = "View" };
        AddChild(view);
        _flight = new FlightView { Name = "Flight" };
        view.AddChild(_flight);
        _flight.Visible = false;

        _ui = new CanvasLayer { Name = "UI" };
        AddChild(_ui);
        ShowTitle();
    }

    public override void _Process(double delta)
    {
        if (!_inFlight || _paused || _world == null || _flight == null) return;
        var dt = Math.Min(delta, 0.05);
        var before = _world.events.Count;
        _world.Step(dt);
        for (var i = before; i < _world.events.Count; i++) Cue(_world.events[i]);
        if (_world.events.Count > 80) _world.events.RemoveRange(0, _world.events.Count - 40);
        var v = _world.active;
        if (v != null)
        {
            var atm = v.body.atmo == null ? 0 : Math.Clamp(v.body.Density(v.altitude) / 1.22, 0, 1);
            var burning = v.thrust > 1 ? Math.Clamp(v.ctrl.throttle, 0.15, 1) : 0;
            _synth?.SetEngine(burning, 0.35 + 0.65 * atm);
        }
        RefreshHud();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_inFlight || _world?.active == null) return;
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _flight!.distance = Mathf.Max(6, _flight.distance / 1.12f);
            if (mb.ButtonIndex == MouseButton.WheelDown) _flight!.distance = Mathf.Min(4000, _flight.distance * 1.12f);
            if (mb.ButtonIndex == MouseButton.Right) _flight!.chase = false;
        }
        if (e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Right) != 0 && _flight != null)
        {
            _flight.chase = false;
            _flight.yaw -= mm.Relative.X * 0.005f;
            _flight.pitch = Mathf.Clamp(_flight.pitch - mm.Relative.Y * 0.005f, -1.2f, 1.2f);
        }
        if (e is InputEventKey key && key.Pressed && !key.Echo)
        {
            var c = _world.active.ctrl;
            switch (key.Keycode)
            {
                case Key.Escape: TogglePause(); break;
                case Key.Z: c.throttle = 1; break;
                case Key.X: c.throttle = 0; break;
                case Key.Space: Stage(); break;
                case Key.T: _world.active.ToggleSas(); _synth?.Play("switch"); break;
                case Key.R: c.rcs = !c.rcs; _synth?.Play("switch"); break;
                case Key.G: _world.active.ToggleLegs(); _synth?.Play("click"); break;
                case Key.U: _world.active.ToggleSolar(); _synth?.Play("click"); break;
                case Key.V: if (_flight != null) _flight.chase = !_flight.chase; break;
                case Key.C: _world.active.ctrl.sasMode = NextSas(_world.active.ctrl.sasMode); _synth?.Play("switch"); break;
                case Key.Period: _world.SetWarp(_world.warpIndex + 1); break;
                case Key.Comma: _world.SetWarp(_world.warpIndex - 1); break;
                case Key.Slash: _world.SetWarp(0); break;
                case Key.F5: SaveFlight(); _status = "Saved."; break;
                case Key.F9: LoadFlight(); break;
                case Key.Bracketright: CycleVessel(1); break;
                case Key.Bracketleft: CycleVessel(-1); break;
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!_inFlight || _paused || _world?.active == null) return;
        if (e is InputEventKey key)
        {
            var down = key.Pressed;
            var c = _world.active.ctrl;
            switch (key.Keycode)
            {
                case Key.W: c.pitch = down ? -1 : (c.pitch < 0 ? 0 : c.pitch); break;
                case Key.S: c.pitch = down ? 1 : (c.pitch > 0 ? 0 : c.pitch); break;
                case Key.A: c.yaw = down ? -1 : (c.yaw < 0 ? 0 : c.yaw); break;
                case Key.D: c.yaw = down ? 1 : (c.yaw > 0 ? 0 : c.yaw); break;
                case Key.Q: c.roll = down ? -1 : (c.roll < 0 ? 0 : c.roll); break;
                case Key.E: c.roll = down ? 1 : (c.roll > 0 ? 0 : c.roll); break;
                case Key.Shift: if (down) c.throttle = Math.Min(1, c.throttle + 0.05); break;
                case Key.Ctrl: if (down) c.throttle = Math.Max(0, c.throttle - 0.05); break;
            }
        }
    }

    void Stage()
    {
        if (_world == null) return;
        var ev = _world.Stage();
        foreach (var e in ev) Cue(e);
        _synth?.Play("stage");
    }

    void Cue(SimEvent e)
    {
        switch (e.type)
        {
            case "liftoff": _status = "Liftoff."; _synth?.Play("stage"); break;
            case "decouple": _synth?.Play("decouple"); break;
            case "landed": _status = "Touchdown."; _synth?.Play("land"); break;
            case "splashed": _status = "Splashed down."; _synth?.Play("land"); break;
            case "destroyed": _status = "Vessel lost (" + (e.reason ?? "destroyed") + ")."; _synth?.Play("boom"); break;
            case "chute": _synth?.Play("chute"); break;
            case "soi": _status = "Sphere of influence: " + (e.toBody?.name ?? ""); break;
            case "message": _status = e.text ?? ""; break;
            case "situation":
                if (e.state == "orbit") { _status = "Orbit."; _synth?.Play("switch"); }
                break;
        }
    }

    static string NextSas(string mode) => mode switch
    {
        "hold" => "prograde",
        "prograde" => "retrograde",
        "retrograde" => "radialOut",
        "radialOut" => "radialIn",
        "radialIn" => "normal",
        "normal" => "up",
        _ => "hold",
    };

    void CycleVessel(int dir)
    {
        if (_world == null || _world.vessels.Count == 0) return;
        var live = _world.vessels.Where(v => !v.dead).ToList();
        if (live.Count == 0) return;
        var i = live.FindIndex(v => v == _world.active);
        _world.SwitchTo(live[(i + dir + live.Count) % live.Count]);
        _flight?.ForgetCraft();
    }

    void ShowTitle()
    {
        _inFlight = false;
        _paused = false;
        if (_flight != null) _flight.Visible = false;
        _synth?.SetEngine(0, 0);
        var root = NewScreen();
        root.AddChild(Backdrop());
        var col = Column();
        col.SetAnchorsPreset(Control.LayoutPreset.Center);
        col.OffsetLeft = -220; col.OffsetRight = 220; col.OffsetTop = -260; col.OffsetBottom = 260;
        col.AddChild(Title("Starwright", 54));
        col.AddChild(Dim("Build it. Launch it. Bring the Pips home.", 16));
        col.AddChild(Gap(10));
        col.AddChild(Button("Fly", ShowCrafts));
        if (Godot.FileAccess.FileExists(SavePath)) col.AddChild(Button("Continue", LoadFlight));
        col.AddChild(Button("About", ShowAbout));
        col.AddChild(Button("Settings", ShowSettings));
        col.AddChild(Button("Quit", () => GetTree().Quit()));
        col.AddChild(Gap(8));
        col.AddChild(Dim("C sharp port for Godot 4.7. The browser game is still here.", 13));
        root.AddChild(col);
    }

    void ShowCrafts()
    {
        var root = NewScreen();
        root.AddChild(Backdrop());
        var col = Column();
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.OffsetLeft = 80; col.OffsetTop = 36; col.OffsetRight = -80; col.OffsetBottom = -24;
        col.AddChild(Title("Choose a rocket", 32));
        col.AddChild(Dim("Stock designs from the browser game. Z throttle, Space to stage, D to tip east.", 14));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 420) };
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 6);
        foreach (var c in Craft.Stock)
        {
            var runway = c.site == "runway";
            var label = c.name + (runway ? "  (runway, wheels not ported yet)" : "");
            var craft = c;
            var b = Button(label, runway ? null : () => Begin(craft, new[] { "Pip" }));
            if (runway) b.Disabled = true;
            list.AddChild(b);
            if (!string.IsNullOrEmpty(c.desc)) list.AddChild(Dim(c.desc!, 13));
        }
        scroll.AddChild(list);
        col.AddChild(scroll);
        col.AddChild(Button("Back", ShowTitle));
        root.AddChild(col);
    }

    void Begin(Craft craft, string[] crew)
    {
        _world = new World();
        _world.Launch(craft, crew);
        _flight!.Sim = _world;
        _flight.ForgetCraft();
        _flight.Visible = true;
        _flight.chase = true;
        _inFlight = true;
        _paused = false;
        _status = craft.name + " is on the pad. Z, then Space.";
        ShowHud();
        _synth?.Play("click");
    }

    void ShowHud()
    {
        var root = NewScreen();
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        var top = new Label();
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        top.OffsetTop = 12; top.OffsetLeft = 16; top.OffsetRight = -16;
        top.AddThemeFontSizeOverride("font_size", 18);
        top.AddThemeColorOverride("font_color", new Color("f4f7fb"));
        top.Name = "Readout";
        root.AddChild(top);
        var hint = new Label
        {
            Text = "W/S pitch   A/D yaw   Q/E roll   Shift/Ctrl throttle   Z full   X cut   Space stage\nT SAS   C SAS mode   G legs   U solar   V camera   right-drag orbit   wheel zoom\n, . time warp   / stop warp   F5 save   F9 load   Esc pause   [ ] vessel",
        };
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetTop = -64; hint.OffsetBottom = -8; hint.OffsetLeft = 16;
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.AddThemeColorOverride("font_color", new Color("9aa7b5"));
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(hint);
        RefreshHud();
    }

    void RefreshHud()
    {
        if (_screen == null) return;
        var readout = _screen.GetNodeOrNull<Label>("Readout");
        if (readout == null || _world?.active == null) return;
        var v = _world.active;
        var nose = new DVec3(0, 1, 0);
        nose.applyQuaternion(v.q);
        var up = v.pos.clone();
        if (up.lengthSq() > 0) up.normalize(); else up.set(1, 0, 0);
        var pitch = Math.Asin(Math.Clamp(nose.dot(up), -1, 1)) * 180 / Math.PI;
        var east = new DVec3().crossVectors(new DVec3(0, 1, 0), up);
        if (east.lengthSq() < 1e-8) east.set(0, 0, -1);
        east.normalize();
        var north = new DVec3().crossVectors(up, east);
        var hdg = Math.Atan2(nose.dot(east), nose.dot(north)) * 180 / Math.PI;
        if (hdg < 0) hdg += 360;
        string Ap(double m) => double.IsInfinity(m) ? "escape" : (m / 1000).ToString("0.0") + " km";
        readout.Text =
            $"{v.name}   {v.situation}   {v.body.name}\n" +
            $"Alt {v.altitude / 1000:0.00} km    radar {v.radarAlt:0} m    speed {v.airspeed:0.0} m/s    vert {v.verticalSpeed:0.0} m/s\n" +
            $"Apo {Ap(v.apoapsis)}    Peri {Ap(v.periapsis)}    throttle {v.ctrl.throttle:0.00}    thrust {v.thrust / 1000:0.0} kN\n" +
            $"Stage {v.stageIndex + 1}/{Math.Max(1, v.stages.Count)}    dV {v.stageDeltaV:0} m/s    pitch {pitch:0}°    hdg {hdg:0}°\n" +
            $"SAS {(v.ctrl.sas ? v.ctrl.sasMode : "off")}    warp {World.WarpRates[_world.warpIndex]:0}×    t {FormatTime(_world.ut)}" +
            (string.IsNullOrEmpty(_status) ? "" : "\n" + _status);
    }

    static string FormatTime(double ut)
    {
        var s = (int)Math.Floor(ut);
        return $"{s / 3600:00}:{(s / 60) % 60:00}:{s % 60:00}";
    }

    void TogglePause()
    {
        if (!_inFlight) return;
        _paused = !_paused;
        if (!_paused) { ShowHud(); return; }
        var root = NewScreen();
        var panel = Column();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -200; panel.OffsetRight = 200; panel.OffsetTop = -180; panel.OffsetBottom = 180;
        panel.AddChild(Title("Paused", 32));
        panel.AddChild(Button("Resume", () => { _paused = false; ShowHud(); }));
        panel.AddChild(Button("Save", () => { SaveFlight(); _status = "Saved."; }));
        panel.AddChild(Button("Main menu", () => { SaveFlight(); ShowTitle(); }));
        root.AddChild(panel);
    }

    void ShowAbout()
    {
        var root = NewScreen();
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetLeft = 48; scroll.OffsetTop = 28; scroll.OffsetRight = -48; scroll.OffsetBottom = -72;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(Title("About Starwright", 32));
        foreach (var s in AboutContent.Sections)
        {
            col.AddChild(Title(s.title, 20));
            foreach (var p in s.paras) col.AddChild(Paragraph(p));
            col.AddChild(Gap(6));
        }
        scroll.AddChild(col);
        root.AddChild(scroll);
        var back = Button("Close", ShowTitle);
        back.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        back.OffsetLeft = 48; back.OffsetRight = -48; back.OffsetTop = -60; back.OffsetBottom = -16;
        root.AddChild(back);
    }

    void ShowSettings()
    {
        var root = NewScreen();
        var col = Column();
        col.SetAnchorsPreset(Control.LayoutPreset.Center);
        col.OffsetLeft = -240; col.OffsetRight = 240; col.OffsetTop = -160; col.OffsetBottom = 160;
        col.AddChild(Title("Settings", 32));
        col.AddChild(Dim("Music", 14));
        var music = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _music, CustomMinimumSize = new Vector2(360, 24) };
        music.ValueChanged += v => { _music = v; _synth?.SetVolumes(_sfx, _music); SaveSettings(); };
        col.AddChild(music);
        col.AddChild(Dim("Effects", 14));
        var sfx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _sfx, CustomMinimumSize = new Vector2(360, 24) };
        sfx.ValueChanged += v => { _sfx = v; _synth?.SetVolumes(_sfx, _music); _synth?.Play("click"); SaveSettings(); };
        col.AddChild(sfx);
        col.AddChild(Button("Back", ShowTitle));
        root.AddChild(col);
    }

    void SaveFlight()
    {
        if (_world == null) return;
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(SaveGame.Write(_world));
        _synth?.Play("click");
    }

    void LoadFlight()
    {
        if (!Godot.FileAccess.FileExists(SavePath)) { ShowTitle(); return; }
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
        var json = f?.GetAsText() ?? "";
        try
        {
            _world = SaveGame.Read(json);
        }
        catch
        {
            _status = "That save could not be read.";
            ShowTitle();
            return;
        }
        _flight!.Sim = _world;
        _flight.ForgetCraft();
        _flight.Visible = true;
        _inFlight = true;
        _paused = false;
        _status = "Loaded.";
        ShowHud();
    }

    void LoadSettings()
    {
        var cf = new ConfigFile();
        if (cf.Load(SettingsPath) == Error.Ok)
        {
            _music = (double)cf.GetValue("audio", "music", _music);
            _sfx = (double)cf.GetValue("audio", "sfx", _sfx);
        }
    }

    void SaveSettings()
    {
        var cf = new ConfigFile();
        cf.SetValue("audio", "music", _music);
        cf.SetValue("audio", "sfx", _sfx);
        cf.Save(SettingsPath);
    }

    Control NewScreen()
    {
        _screen?.QueueFree();
        _screen = new Control { Name = "Screen" };
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.MouseFilter = Control.MouseFilterEnum.Stop;
        _ui!.AddChild(_screen);
        return _screen;
    }

    Control Backdrop()
    {
        var rect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        var media = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "media", "shot-centre.jpg"));
        var image = Image.LoadFromFile(media);
        if (image != null && !image.IsEmpty()) rect.Texture = ImageTexture.CreateFromImage(image);
        var shade = new ColorRect { Color = new Color(0.04f, 0.06f, 0.08f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var wrap = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        wrap.AddChild(rect);
        wrap.AddChild(shade);
        return wrap;
    }

    static VBoxContainer Column()
    {
        var c = new VBoxContainer();
        c.AddThemeConstantOverride("separation", 8);
        return c;
    }

    static Label Title(string text, int size)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", new Color("f4f7fb"));
        return l;
    }

    static Label Dim(string text, int size)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Word };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", new Color("c5d0da"));
        return l;
    }

    static RichTextLabel Paragraph(string text)
    {
        var l = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Word,
            Text = Linkify(text),
        };
        l.AddThemeFontSizeOverride("normal_font_size", 15);
        l.AddThemeColorOverride("default_color", new Color("c5d0da"));
        l.CustomMinimumSize = new Vector2(760, 0);
        return l;
    }

    static string Linkify(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([^\]]+)\]\((https://[^)\s]+)\)",
            "[url=$2]$1[/url]");
    }

    static Control Gap(int h) => new() { CustomMinimumSize = new Vector2(0, h) };

    static Button Button(string text, Action? press)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(360, 40) };
        b.AddThemeColorOverride("font_color", new Color("f4f7fb"));
        b.AddThemeColorOverride("font_hover_color", new Color("39d0c0"));
        if (press != null) b.Pressed += press;
        return b;
    }
}

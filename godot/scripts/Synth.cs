using Godot;

namespace Starwright;

/// <summary>
/// Procedural stand-in for js/ui/audio.js. There are no sound files in the repository;
/// the browser game synthesises everything, and so does this. Engine rumble follows
/// throttle, and the one-shots mark staging, touchdown and the menu.
/// </summary>
public partial class Synth : Node
{
    AudioStreamPlayer? _engine;
    AudioStreamGeneratorPlayback? _play;
    AudioStreamPlayer? _music;
    AudioStreamGeneratorPlayback? _musicPlay;
    readonly Dictionary<string, AudioStreamWav> _ones = new();
    double _level;
    double _atm = 1;
    double _phase;
    double _musicPhase;
    float _sfx = 0.8f;
    float _musicVol = 0.35f;
    uint _rng = 0x2f6e2b1;

    public override void _Ready()
    {
        var gen = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f };
        _engine = new AudioStreamPlayer { Stream = gen, VolumeDb = -6 };
        AddChild(_engine);
        _engine.Play();
        _play = (AudioStreamGeneratorPlayback)_engine.GetStreamPlayback();

        var music = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.2f };
        _music = new AudioStreamPlayer { Stream = music, VolumeDb = -18 };
        AddChild(_music);
        _music.Play();
        _musicPlay = (AudioStreamGeneratorPlayback)_music.GetStreamPlayback();

        _ones["click"] = Tone(880, 0.05f, 0.4f, false);
        _ones["stage"] = NoiseBurst(0.18f, 0.7f, 90);
        _ones["decouple"] = NoiseBurst(0.12f, 0.45f, 400);
        _ones["land"] = NoiseBurst(0.22f, 0.55f, 70);
        _ones["boom"] = NoiseBurst(0.45f, 0.9f, 50);
        _ones["chute"] = Tone(520, 0.12f, 0.25f, false);
        _ones["switch"] = Tone(660, 0.04f, 0.3f, false);
    }

    public void SetVolumes(double sfx, double music)
    {
        _sfx = (float)Mathf.Clamp((float)sfx, 0, 1);
        _musicVol = (float)Mathf.Clamp((float)music, 0, 1);
    }

    public void SetEngine(double level, double atm)
    {
        _level = Math.Clamp(level, 0, 1);
        _atm = Math.Clamp(atm, 0, 1);
    }

    public void Play(string name)
    {
        if (_sfx <= 0.001f || !_ones.TryGetValue(name, out var wav)) return;
        var p = new AudioStreamPlayer { Stream = wav, VolumeDb = Mathf.LinearToDb(_sfx) };
        AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    public override void _Process(double delta)
    {
        FillEngine();
        FillMusic();
    }

    void FillEngine()
    {
        if (_play == null) return;
        var n = _play.GetFramesAvailable();
        var rate = 22050.0;
        for (var i = 0; i < n; i++)
        {
            _rng = _rng * 1664525 + 1013904223;
            var white = ((_rng >> 8) & 65535) / 32768f - 1f;
            _phase += 70.0 / rate;
            var body = Math.Sin(_phase * Math.Tau) * 0.6 + white * 0.4;
            var amp = _level * (0.25 + 0.75 * _atm) * _sfx;
            var s = (float)(body * amp * 0.35);
            _play.PushFrame(new Vector2(s, s));
        }
    }

    void FillMusic()
    {
        if (_musicPlay == null) return;
        var n = _musicPlay.GetFramesAvailable();
        var rate = 22050.0;
        for (var i = 0; i < n; i++)
        {
            _musicPhase += 1.0 / rate;
            var a = Math.Sin(_musicPhase * 110 * Math.Tau) * 0.15;
            var b = Math.Sin(_musicPhase * 164.8 * Math.Tau) * 0.08;
            var env = 0.5 + 0.5 * Math.Sin(_musicPhase * 0.07 * Math.Tau);
            var s = (float)((a + b) * env * _musicVol * 0.25);
            _musicPlay.PushFrame(new Vector2(s, s));
        }
    }

    static AudioStreamWav Tone(float freq, float seconds, float volume, bool noise)
    {
        const int rate = 22050;
        var n = Math.Max(1, (int)(rate * seconds));
        var data = new byte[n * 2];
        for (var i = 0; i < n; i++)
        {
            var env = 1f - (float)i / n;
            var s = Mathf.Sin(i * freq * Mathf.Tau / rate) * env * volume;
            Write(data, i, s);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }

    static AudioStreamWav NoiseBurst(float seconds, float volume, float falloff)
    {
        const int rate = 22050;
        var n = Math.Max(1, (int)(rate * seconds));
        var data = new byte[n * 2];
        var rng = 12345;
        float acc = 0;
        for (var i = 0; i < n; i++)
        {
            rng = rng * 1103515245 + 12345;
            var white = ((rng >> 16) & 32767) / 32767f * 2f - 1f;
            acc = acc * (1f - falloff / rate) + white * (falloff / rate);
            var env = 1f - (float)i / n;
            Write(data, i, acc * env * volume);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }

    static void Write(byte[] data, int i, float s)
    {
        var v = (short)Mathf.Clamp(s * 32000f, -32767, 32767);
        data[i * 2] = (byte)(v & 255);
        data[i * 2 + 1] = (byte)((v >> 8) & 255);
    }
}

using System;
using System.Threading.Tasks;
using Godot;

namespace DeskCiv;

/// <summary>Generated sound: sea and wind beds, occasional birds and a short piece of music for each era.</summary>
public partial class Soundscape : Node
{
    private const int SampleRate = 22050, MusicSeconds = 36;
    private const double FadeInSeconds = 10, FadeOutSeconds = 2, MusicInterval = 240;

    private static readonly int[][] Scales =
    {
        new[] { 0, 3, 5, 7, 10 }, new[] { 0, 2, 5, 7, 9 }, new[] { 0, 1, 5, 7, 8 }, new[] { 0, 2, 3, 7, 8 },
        new[] { 0, 2, 3, 5, 7, 9, 10 }, new[] { 0, 2, 4, 5, 7, 9, 11 }, new[] { 0, 2, 4, 7, 9, 11 }, new[] { 0, 2, 4, 6, 7, 9, 11 },
    };
    private static readonly double[] Roots = { 110, 123.47, 116.54, 130.81, 146.83, 164.81, 174.61, 196 };
    private static readonly double[][] Timbres =
    {
        new[] { 1, 0.1 }, new[] { 1, 0.3, 0.08 }, new[] { 1, 0.45, 0.2 }, new[] { 1, 0.5, 0.15, 0.1 },
        new[] { 1, 0.6, 0.3, 0.15 }, new[] { 1, 0.35, 0.3, 0.12, 0.08 }, new[] { 1, 0.7, 0.4, 0.25, 0.1 }, new[] { 1, 0.25, 0.12 },
    };

    private readonly AudioSettings _settings;
    private readonly bool _forceSilent;
    private readonly RandomNumberGenerator _random = new();
    private readonly AudioStream[] _pieces = new AudioStream[Eras8];
    private const int Eras8 = 8;
    private int _master, _ambient, _music;
    private AudioStreamPlayer _musicPlayer, _birdPlayer, _rainPlayer, _cuePlayer;
    private AudioStream _bell, _whistle, _thunder;
    private AudioStreamPlayer _thunderPlayer;
    private AudioStream[] _birds = Array.Empty<AudioStream>();
    private bool _recordedBirds;
    private float _rainLevel;
    private double _gain, _nextMusic = 30, _nextBird = 20;
    private Task<short[]> _composing;
    private int _composingEra = -1;

    public bool Suppressed { get; set; }
    public bool Yielding { get; set; }
    public int Era { get; set; }
    /// <summary>0 = dry, 1 = heavy rain.</summary>
    public float Rain { get; set; }
    /// <summary>0 = night, 1 = full day. Birds sing in daylight only.</summary>
    public float Daylight { get; set; } = 1;
    public float MasterDb => AudioServer.GetBusVolumeDb(_master);

    public Soundscape() { }

    public Soundscape(AudioSettings settings, bool forceSilent)
    {
        _settings = settings;
        _forceSilent = forceSilent;
    }

    public override void _Ready()
    {
        _master = AudioServer.GetBusIndex("Master");
        _ambient = AddBus("Ambient");
        _music = AddBus("Music");
        Apply();
        if (_forceSilent) return;

        // Recorded sounds from assets/audio (see THIRD_PARTY_NOTICES.md); generated sound fills in for any that are missing.
        AddChild(new AudioStreamPlayer { Stream = Recorded("wind", true) ?? Wav(MakeWind(24), true), Bus = "Ambient", Autoplay = true, VolumeDb = -5 });
        AddChild(new AudioStreamPlayer { Stream = Recorded("surf", true) ?? Wav(MakeSurf(26), true), Bus = "Ambient", Autoplay = true });
        if (Recorded("rain", true) is AudioStream rain)
        {
            _rainPlayer = new AudioStreamPlayer { Stream = rain, Bus = "Ambient", VolumeDb = -60 };
            AddChild(_rainPlayer);
        }
        _birdPlayer = new AudioStreamPlayer { Bus = "Ambient", VolumeDb = -9 };
        AddChild(_birdPlayer);
        var birds = new System.Collections.Generic.List<AudioStream>();
        for (int i = 1; Recorded($"bird_{i}", false) is AudioStream bird; i++) birds.Add(bird);
        for (int i = 1; Recorded($"gull_{i}", false) is AudioStream gull; i++) birds.Add(gull);
        _birds = birds.Count > 0 ? birds.ToArray() : new AudioStream[] { Wav(MakeBird(2400, 3, 0.09), false), Wav(MakeBird(3200, 5, 0.06), false), Wav(MakeGull(), false) };
        _recordedBirds = birds.Count > 0;
        for (int era = 0; era < Eras8; era++) _pieces[era] = Recorded($"music_{era}", false);
        _musicPlayer = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicPlayer);
        // Event cues, synthesised: a church bell tolling and a steam whistle.
        _cuePlayer = new AudioStreamPlayer { Bus = "Ambient", VolumeDb = -14 };
        AddChild(_cuePlayer);
        _bell = Wav(MakeBell(3), false);
        _whistle = Wav(MakeWhistle(), false);
        _thunder = Wav(MakeThunder(), false);
        _thunderPlayer = new AudioStreamPlayer { Bus = "Ambient", VolumeDb = -8 };
        AddChild(_thunderPlayer);
    }

    /// <summary>Loads assets/audio/NAME.dcsnd (an Ogg Vorbis file under a name the Godot importer leaves alone).</summary>
    private static AudioStream Recorded(string name, bool loop)
    {
        string path = $"res://assets/audio/{name}.dcsnd";
        if (!FileAccess.FileExists(path)) return null;
        AudioStreamOggVorbis stream = AudioStreamOggVorbis.LoadFromBuffer(FileAccess.GetFileAsBytes(path));
        if (stream != null) stream.Loop = loop;
        return stream;
    }

    private static int AddBus(string name)
    {
        int existing = AudioServer.GetBusIndex(name);
        if (existing >= 0) return existing;
        AudioServer.AddBus();
        int index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
        return index;
    }

    public override void _Process(double delta)
    {
        double target = _settings.Muted || Suppressed || Yielding || _forceSilent ? 0 : 1;
        _gain = Mathf.MoveToward(_gain, target, delta / (target < _gain ? FadeOutSeconds : FadeInSeconds));
        Apply();
        if (_forceSilent || _gain <= 0) return;

        if (_rainPlayer != null)
        {
            _rainLevel = Mathf.MoveToward(_rainLevel, Rain, (float)delta * 0.1f);
            if (_rainLevel > 0.01f && !_rainPlayer.Playing) _rainPlayer.Play();
            else if (_rainLevel <= 0.01f && _rainPlayer.Playing) _rainPlayer.Stop();
            _rainPlayer.VolumeDb = Mathf.LinearToDb(Mathf.Max(_rainLevel, 0.001f)) - 2;
        }

        _nextBird -= delta;
        if (_nextBird <= 0)
        {
            _nextBird = _random.RandfRange(25, 70);
            if (Daylight > 0.3f && Rain < 0.4f && !_birdPlayer.Playing)
            {
                _birdPlayer.Stream = _birds[_random.RandiRange(0, _birds.Length - 1)];
                // Recordings keep their natural pitch; generated chirps vary a little.
                _birdPlayer.PitchScale = _recordedBirds ? 1f : _random.RandfRange(0.9f, 1.12f);
                _birdPlayer.Play();
            }
        }

        if (_composing is { IsCompleted: true })
        {
            _pieces[_composingEra] = Wav(_composing.Result, false);
            _composing = null;
            _musicPlayer.Stream = _pieces[_composingEra];
            _musicPlayer.Play();
        }
        if (_musicPlayer.Playing) return;
        _nextMusic -= delta;
        if (_nextMusic > 0 || _composing != null) return;
        // A quiet spell of three to six minutes follows every piece.
        _nextMusic = _random.RandfRange((float)MusicInterval * 0.75f, (float)MusicInterval * 1.5f);
        int era = Math.Clamp(Era, 0, Eras8 - 1);
        if (_pieces[era] != null)
        {
            _musicPlayer.Stream = _pieces[era];
            _musicPlayer.Play();
            return;
        }
        _composingEra = era;
        _composing = Task.Run(() => Compose(era));
    }

    /// <summary>The church bell tolls (the hour, a wedding or a funeral). Quiet by night and while another cue plays.</summary>
    public void Bell()
    {
        if (_cuePlayer == null || _gain <= 0 || _cuePlayer.Playing || Daylight < 0.3f) return;
        _cuePlayer.Stream = _bell;
        _cuePlayer.Play();
    }

    /// <summary>A steam whistle as the train comes out of the tunnel.</summary>
    public void Whistle()
    {
        if (_cuePlayer == null || _gain <= 0 || _cuePlayer.Playing) return;
        _cuePlayer.Stream = _whistle;
        _cuePlayer.Play();
    }

    /// <summary>A roll of thunder after a lightning flash.</summary>
    public void Thunder()
    {
        if (_thunderPlayer == null || _gain <= 0 || _thunderPlayer.Playing) return;
        _thunderPlayer.Stream = _thunder;
        _thunderPlayer.PitchScale = _random.RandfRange(0.85f, 1.1f);
        _thunderPlayer.Play();
    }

    /// <summary>Thunder: low-passed noise with a sharp crack, then a long rolling rumble that swells and fades (about 4 s).</summary>
    private static short[] MakeThunder()
    {
        int n = 4 * SampleRate;
        var data = new float[n];
        var noise = new Random(11);
        double low = 0, lower = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double white = noise.NextDouble() * 2 - 1;
            low += (white - low) * 0.06;
            lower += (low - lower) * 0.05;
            double crack = Math.Exp(-t * 18) * white * 0.6;
            double roll = lower * 9 * Math.Exp(-t * 0.9) * (0.6 + 0.4 * Math.Sin(t * 5.3) * Math.Sin(t * 2.1));
            data[i] = (float)((crack + roll) * Math.Min(1, t * 60) * Math.Min(1, (4 - t) * 2));
        }
        return Normalise(data, 0.7f);
    }

    /// <summary>A bronze bell: inharmonic partials with long decays, struck `strokes` times 1.8 s apart.</summary>
    private static short[] MakeBell(int strokes)
    {
        int stroke = (int)(1.8 * SampleRate), n = stroke * (strokes - 1) + 4 * SampleRate;
        var data = new float[n];
        double[] partials = { 0.5, 1.0, 1.19, 1.5, 2.0, 2.52, 3.0 };
        double[] levels = { 0.5, 1.0, 0.5, 0.35, 0.3, 0.18, 0.12 };
        const double f = 392;
        for (int s = 0; s < strokes; s++)
            for (int i = 0; i < 4 * SampleRate && s * stroke + i < n; i++)
            {
                double t = i / (double)SampleRate, v = 0;
                for (int p = 0; p < partials.Length; p++)
                    v += levels[p] * Math.Sin(2 * Math.PI * f * partials[p] * t) * Math.Exp(-t * (0.9 + p * 0.45));
                data[s * stroke + i] += (float)(v * Math.Min(1, t * 400));
            }
        return Normalise(data, 0.55f);
    }

    /// <summary>A two-note steam whistle with a breathy edge, about 1.6 s.</summary>
    private static short[] MakeWhistle()
    {
        int n = (int)(1.6 * SampleRate);
        var data = new float[n];
        var noise = new Random(7);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Min(1, t * 8) * Math.Min(1, (1.6 - t) * 5);
            double vib = 1 + 0.004 * Math.Sin(2 * Math.PI * 5.5 * t);
            double v = Math.Sin(2 * Math.PI * 523 * vib * t) + 0.8 * Math.Sin(2 * Math.PI * 659 * vib * t) + 0.25 * Math.Sin(2 * Math.PI * 1046 * vib * t);
            data[i] = (float)(env * (v + (noise.NextDouble() - 0.5) * 0.35));
        }
        return Normalise(data, 0.45f);
    }

    private static short[] Normalise(float[] data, float peak)
    {
        float max = 0.0001f;
        foreach (float v in data) max = Math.Max(max, Math.Abs(v));
        var samples = new short[data.Length];
        for (int i = 0; i < data.Length; i++) samples[i] = (short)(data[i] / max * peak * short.MaxValue);
        return samples;
    }

    private void Apply()
    {
        SetBus(_master, _settings.Master / 100f * (float)_gain);
        SetBus(_ambient, _settings.Ambient / 100f);
        SetBus(_music, _settings.Music / 100f);
    }

    private static void SetBus(int bus, float linear)
    {
        AudioServer.SetBusMute(bus, linear <= 0.0001f);
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
    }

    private static AudioStreamWav Wav(short[] samples, bool loop)
    {
        byte[] bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopEnd = loop ? samples.Length : 0,
        };
    }

    private static short[] Loop(float[] raw, int n, int fade, float gain)
    {
        var result = new short[n];
        for (int i = 0; i < n; i++)
        {
            float value = raw[i];
            if (i < fade)
            {
                float t = i / (float)fade;
                value = value * t + raw[n + i] * (1 - t);
            }
            result[i] = (short)(Mathf.Clamp(value * gain, -1f, 1f) * 12000);
        }
        return result;
    }

    private static short[] MakeWind(int seconds)
    {
        int n = seconds * SampleRate, fade = SampleRate * 2;
        var raw = new float[n + fade];
        var rng = new Random(7);
        float low = 0, slow = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            low += ((float)rng.NextDouble() * 2 - 1 - low) * 0.035f;
            slow += ((float)rng.NextDouble() * 2 - 1 - slow) * 0.00008f;
            raw[i] = low * (0.75f + Mathf.Clamp(slow * 8, -0.35f, 0.35f));
        }
        return Loop(raw, n, fade, 1.6f);
    }

    /// <summary>Filtered noise that swells and falls like waves on a beach.</summary>
    private static short[] MakeSurf(int seconds)
    {
        int n = seconds * SampleRate, fade = SampleRate * 3;
        var raw = new float[n + fade];
        var rng = new Random(23);
        float low = 0, band = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            float white = (float)rng.NextDouble() * 2 - 1;
            low += (white - low) * 0.12f;
            band += (low - band) * 0.01f;
            double t = i / (double)SampleRate;
            double swell = 0.5 + 0.5 * Math.Sin(Math.Tau * t / 6.5) * Math.Sin(Math.Tau * t / 6.5 + 0.8 * Math.Sin(Math.Tau * t / 13));
            raw[i] = (low - band) * (float)(0.12 + 0.88 * swell * swell);
        }
        return Loop(raw, n, fade, 1.5f);
    }

    private static short[] MakeBird(double pitch, int chirps, double length)
    {
        int n = (int)((length * 1.7 * chirps + 0.1) * SampleRate);
        var result = new short[n];
        for (int c = 0; c < chirps; c++)
        {
            int start = (int)(c * length * 1.7 * SampleRate), size = (int)(length * SampleRate);
            for (int i = 0; i < size && start + i < n; i++)
            {
                double t = i / (double)size, envelope = Math.Sin(Math.PI * t);
                double f = pitch * (1 + 0.22 * Math.Sin(Math.PI * t));
                result[start + i] += (short)(Math.Sin(Math.Tau * f * i / SampleRate) * envelope * 6000);
            }
        }
        return result;
    }

    private static short[] MakeGull()
    {
        int n = (int)(1.3 * SampleRate);
        var result = new short[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n, cry = (t * 3) % 1;
            phase += Math.Tau * (1250 - 420 * cry) / SampleRate;
            double envelope = Math.Sin(Math.PI * cry) * (1 - t * 0.4);
            result[i] = (short)((Math.Sin(phase) + 0.35 * Math.Sin(phase * 2.01)) * envelope * 4200);
        }
        return result;
    }

    /// <summary>A slow generated melody over a drone. Scale and tone change with the era.</summary>
    private static short[] Compose(int era)
    {
        int n = MusicSeconds * SampleRate;
        var mix = new float[n];
        var rng = new Random(era * 131 + 17);
        int[] scale = Scales[era];
        double root = Roots[era];
        double[] timbre = Timbres[era];

        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double swell = Math.Sin(Math.PI * t / MusicSeconds);
            mix[i] += (float)((Math.Sin(Math.Tau * root / 2 * t) + 0.5 * Math.Sin(Math.Tau * root * 0.75 * t)) * swell * swell * 0.13);
        }

        double beat = 1.5 - era * 0.06, time = 1.5;
        int degree = 0;
        while (time < MusicSeconds - 4)
        {
            degree = Math.Clamp(degree + rng.Next(-2, 3), 0, scale.Length * 2 - 1);
            int semitone = scale[degree % scale.Length] + 12 * (degree / scale.Length) + 12;
            double frequency = root * Math.Pow(2, semitone / 12.0), length = beat * (rng.Next(3) == 0 ? 2 : 1);
            AddNote(mix, time, length * 1.6, frequency, timbre);
            if (era >= 4 && rng.Next(3) == 0) AddNote(mix, time, length * 1.6, frequency * 0.75, timbre);
            time += length;
        }

        var result = new short[n];
        for (int i = 0; i < n; i++)
        {
            float edge = Math.Min(1f, Math.Min(i, n - 1 - i) / (SampleRate * 1.5f));
            result[i] = (short)(Math.Clamp(mix[i] * edge, -1f, 1f) * 11000);
        }
        return result;
    }

    private static void AddNote(float[] mix, double start, double length, double frequency, double[] timbre)
    {
        int first = (int)(start * SampleRate), count = (int)(length * SampleRate);
        for (int i = 0; i < count && first + i < mix.Length; i++)
        {
            double t = i / (double)SampleRate;
            double envelope = Math.Min(1, t / 0.04) * Math.Exp(-3.2 * t / length);
            double value = 0;
            for (int h = 0; h < timbre.Length; h++) value += timbre[h] * Math.Sin(Math.Tau * frequency * (h + 1) * t);
            mix[first + i] += (float)(value * envelope * 0.26);
        }
    }
}

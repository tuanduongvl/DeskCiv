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
    private readonly AudioStreamWav[] _pieces = new AudioStreamWav[Eras8];
    private const int Eras8 = 8;
    private int _master, _ambient, _music;
    private AudioStreamPlayer _musicPlayer, _birdPlayer;
    private AudioStreamWav[] _birds = Array.Empty<AudioStreamWav>();
    private double _gain, _nextMusic = 30, _nextBird = 20;
    private Task<short[]> _composing;
    private int _composingEra = -1;

    public bool Suppressed { get; set; }
    public bool Yielding { get; set; }
    public int Era { get; set; }
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

        AddChild(new AudioStreamPlayer { Stream = Wav(MakeWind(24), true), Bus = "Ambient", Autoplay = true, VolumeDb = -5 });
        AddChild(new AudioStreamPlayer { Stream = Wav(MakeSurf(26), true), Bus = "Ambient", Autoplay = true });
        _birdPlayer = new AudioStreamPlayer { Bus = "Ambient", VolumeDb = -9 };
        AddChild(_birdPlayer);
        _birds = new[] { Wav(MakeBird(2400, 3, 0.09), false), Wav(MakeBird(3200, 5, 0.06), false), Wav(MakeGull(), false) };
        _musicPlayer = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicPlayer);
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

        _nextBird -= delta;
        if (_nextBird <= 0)
        {
            _nextBird = _random.RandfRange(25, 70);
            _birdPlayer.Stream = _birds[_random.RandiRange(0, _birds.Length - 1)];
            _birdPlayer.PitchScale = _random.RandfRange(0.9f, 1.12f);
            _birdPlayer.Play();
        }

        if (_composing is { IsCompleted: true })
        {
            _pieces[_composingEra] = Wav(_composing.Result, false);
            _composing = null;
            _musicPlayer.Stream = _pieces[_composingEra];
            _musicPlayer.Play();
        }
        _nextMusic -= delta;
        if (_nextMusic > 0 || _composing != null) return;
        _nextMusic = MusicInterval;
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

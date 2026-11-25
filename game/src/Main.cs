using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DeskCiv.Sim;
using Godot;

namespace DeskCiv;

/// <summary>
/// Application host. User args: --windowed --silent --no-occlusion --fresh --no-save --duration=SEC
/// --metrics=PATH --seed=N --speed=N --start-year=Y --hour=H --shot=PNG --watch=CLIP --replay-seconds=N --restart-every=SEC --view=X,Z,DISTANCE.
/// </summary>
public partial class Main : Node3D
{
    private const int FullFps = 30, ReducedFps = 15, StoppedFps = 2;

    private SimHost _sim;
    private WorldView _view;
    private DesktopHost _host;
    private AudioSettings _settings;
    private TrayIcon _tray;
    private Soundscape _sound;
    private Camera3D _camera;
    private DirectionalLight3D _sun;
    private Godot.Environment _environment;
    private GpuParticles3D _rain;
    private StreamOutput _stream;
    private AudioEffectCapture _soundCapture;
    private static bool _weatherGlobals;
    private string _forcedWeather;
    private Label _overlay, _replayLabel;
    private bool _canSave, _replaying;
    private double _replayTime, _replaySeconds = 120, _restartEvery, _restartTimer;
    private AudioYield _audioYield;
    private int _quality, _slowSeconds, _steadySeconds;
    private const int MaxQuality = 3;
    private static readonly float[] RenderScales = { 1f, 0.85f, 0.72f, 0.6f };
    private RenderState _state = RenderState.Full;
    private bool _renderEnabled = true, _ignoreOcclusion, _closed;
    private float? _fixedHour;
    private string _shotPath;
    private double _shotAt;
    private int _baseSpeed = 1, _shotNumber, _shotFrames, _shotWait, _burst, _burstIndex;
    private float CloseDistance = 30f, _azimuth, _azimuthTarget, _driftAmount = 1, _lift, _liftTarget;
    private const float MinZoom = 10f, MaxZoom = 270f, ManualSeconds = 60f;
    private bool _manual, _wasPanning;
    private double _manualUntil;
    private Vector2 _panLast;
    private Clip? _forcedClip;
    private BType? _lookType;
    private bool _watchDig;
    private Vector2? _focusPoint;
    private Clip _lastShotClip = Clip.Idle;

    private double _elapsed, _hostTimer, _worldTimer, _sunTimer, _metricTimer, _duration, _focusTimer;
    private Vector3 _focus, _focusTarget;
    private float _distance, _distanceTarget;
    private long _lastEventTick = -1;
    private StreamWriter _metrics;
    private TimeSpan _lastCpu;
    private long _lastWall;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool windowed = HasFlag(args, "--windowed"), silent = HasFlag(args, "--silent");
        _ignoreOcclusion = HasFlag(args, "--no-occlusion");
        _duration = Number(args, "--duration", 0);
        if (Option(args, "--hour", null) != null) _fixedHour = (float)Number(args, "--hour", 12);
        string seedText = Option(args, "--seed", null);
        int? seed = seedText == null ? null : int.Parse(seedText, CultureInfo.InvariantCulture);
        string metricsPath = Option(args, "--metrics", null);
        _shotPath = Option(args, "--shot", null);
        _shotAt = Number(args, "--shot-at", 0);
        _burst = (int)Number(args, "--burst", 0);
        _forcedWeather = Option(args, "--weather", null);
        if (Enum.TryParse(Option(args, "--watch", ""), true, out Clip watched)) _forcedClip = watched;
        _watchDig = Array.IndexOf(args, "--watch-dig") >= 0; // test shots of the street being dug for water mains
        if (Enum.TryParse(Option(args, "--look", ""), true, out BType looked)) _lookType = looked; // test shots of one building type
        // --focus=x,z keeps the camera on one world point, for test shots of props that are not buildings.
        string[] focus = Option(args, "--focus", "").Split(',');
        if (focus.Length == 2 && float.TryParse(focus[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx)
            && float.TryParse(focus[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fz)) _focusPoint = new Vector2(fx, fz);
        CloseDistance = (float)Number(args, "--distance", CloseDistance);

        _baseSpeed = Math.Max(1, (int)Number(args, "--speed", 1));
        _canSave = !HasFlag(args, "--no-save");
        _replaySeconds = Math.Max(8, Number(args, "--replay-seconds", 120));
        _restartEvery = Number(args, "--restart-every", 0);
        _restartTimer = _restartEvery;
        _sim = new SimHost(seed, HasFlag(args, "--fresh"), _canSave, _baseSpeed, (float)Number(args, "--start-year", 0));
        BuildScene();
        // Test switches for measuring what the look costs.
        if (HasFlag(args, "--no-shadows")) _sun.ShadowEnabled = false;
        if (HasFlag(args, "--no-msaa")) GetViewport().Msaa3D = Viewport.Msaa.Disabled;
        if (Number(args, "--shadow-splits", 0) is 1 or 2)
            _sun.DirectionalShadowMode = Number(args, "--shadow-splits", 0) == 1 ? DirectionalLight3D.ShadowMode.Orthogonal : DirectionalLight3D.ShadowMode.Parallel2Splits;
        if (Number(args, "--shadow-size", 0) > 0) RenderingServer.DirectionalShadowAtlasSetSize((int)Number(args, "--shadow-size", 0), true);
        _view = new WorldView(_sim);
        AddChild(_view);

        _settings = AudioSettings.Load();
        _sound = new Soundscape(_settings, silent);
        AddChild(_sound);
        _tray = new TrayIcon(_settings)
        {
            BeforeSessionEnd = () => _sim.SaveNow(3000),
            StartupCommand = StartupLine(),
        };
        _tray.Start();
        if (!silent) _audioYield = new AudioYield();

        if (!windowed)
        {
            _host = new DesktopHost((IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), !HasFlag(args, "--no-layered"));
            bool attached = _host.Attach();
            GD.Print($"DeskCiv desktop attach: {attached} mode={_host.Mode}");
            if (!attached) _host = null;
        }

        BuildOverlay();
        WorldSnapshot first = _sim.Snapshot;
        _focusTarget = FocusAt(first.CenterX, first.CenterZ);
        _distanceTarget = CameraDistance(first);
        NextShot(first);
        _focus = _focusTarget;
        _distance = _distanceTarget;
        _azimuth = _azimuthTarget;
        if (Option(args, "--view", null) is string view)
        {
            string[] parts = view.Split(',');
            _focus = _focusTarget = FocusAt(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
            _distance = _distanceTarget = float.Parse(parts[2], CultureInfo.InvariantCulture);
            _azimuth = _azimuthTarget = 0;
            _manual = true;
            _manualUntil = double.MaxValue;
        }
        UpdateCamera(0);
        UpdateSun();
        _view.SyncWorld();
        UpdateOverlay(first);
        _sim.Start();
        if (metricsPath != null) OpenMetrics(metricsPath);
        // --stream-key-file=<file with the YouTube stream key> streams the game picture; --stream-test=<file.flv> records instead.
        string streamKey = Option(args, "--stream-key-file", null), streamTest = Option(args, "--stream-test", null);
        if (streamKey != null || streamTest != null)
        {
            Vector2I size = GetViewport().GetVisibleRect().Size is var v ? new Vector2I((int)v.X, (int)v.Y) : new Vector2I(1280, 720);
            _stream = StreamOutput.Start(streamKey, streamTest, Option(args, "--ffmpeg", "ffmpeg"), size.X & ~1, size.Y & ~1, 30, (int)AudioServer.GetMixRate());
            if (_stream != null)
            {
                // The stream carries the game's own sound, so it must not fall silent when other apps play.
                _audioYield?.Dispose();
                _audioYield = null;
                _soundCapture = new AudioEffectCapture { BufferLength = 1f };
                AudioServer.AddBusEffect(AudioServer.GetBusIndex("Master"), _soundCapture);
            }
        }
    }

    private void BuildScene()
    {
        // Weather values every world shader reads (cloud shadows, wet ground).
        if (!_weatherGlobals)
        {
            foreach (string name in new[] { "cloud_cover", "rain", "daylight" })
                RenderingServer.GlobalShaderParameterAdd(name, RenderingServer.GlobalShaderParameterType.Float, 0f);
            _weatherGlobals = true;
        }
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            FogEnabled = true,
            FogLightEnergy = 0.8f,
            FogDensity = 0.0012f,
            FogAerialPerspective = 0.35f,
        };
        AddChild(new WorldEnvironment { Environment = _environment });
        // Sun shadows give buildings, trees and villagers weight on the ground.
        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            DirectionalShadowMaxDistance = 160,
            ShadowBlur = 1.4f,
            ShadowBias = 0.06f,
            ShadowNormalBias = 1.0f,
        };
        AddChild(_sun);
        _camera = new Camera3D { Fov = 34, Near = 3, Far = 1000, Current = true };
        AddChild(_camera);
    }

    private void BuildOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        _overlay.AddThemeFontSizeOverride("font_size", 19);
        _overlay.AddThemeColorOverride("font_color", new Color(1f, 0.97f, 0.9f, 0.9f));
        _overlay.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.72f));
        _overlay.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_overlay);

        _replayLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visible = false };
        _replayLabel.AddThemeFontSizeOverride("font_size", 30);
        _replayLabel.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.86f));
        _replayLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        _replayLabel.AddThemeConstantOverride("outline_size", 8);
        layer.AddChild(_replayLabel);

        Rect2I area = _host?.PrimaryWorkArea() ?? new Rect2I(Vector2I.Zero, DisplayServer.WindowGetSize());
        var size = new Vector2(760, 110);
        _overlay.Size = size;
        _overlay.Position = area.Position + (Vector2)area.Size - size - new Vector2(26, 18);
        _replayLabel.Position = area.Position + new Vector2(0, area.Size.Y * 0.12f);
        _replayLabel.Size = new Vector2(area.Size.X, 230);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _host?.KeepResponsive();
        if (_tray.QuitRequested || (_duration > 0 && _elapsed >= _duration && _shotPath == null))
        {
            Shutdown();
            GetTree().Quit();
            return;
        }

        // A watched activity may not be happening yet, so wait for the camera to find it, up to about 40 seconds.
        // --shot-at=SEC delays the screenshot (it is normally taken after 75 frames), e.g. to catch a timed event.
        if (_shotPath != null && _elapsed >= _shotAt && (_forcedClip == null || _focusTimer > 1000 || ++_shotWait >= 1200) && ++_shotFrames >= (_forcedClip == null ? 75 : 300))
        {
            // --burst=N saves N extra frames a few frames apart (name_1.png ...) to check motion in tests.
            string path = _burstIndex == 0 ? _shotPath : _shotPath.Replace(".png", $"_{_burstIndex}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            if (_burstIndex++ < _burst) _shotFrames -= 4;
            else _shotPath = null;
        }

        _stream?.Push(GetViewport());
        if (_soundCapture != null && _soundCapture.GetFramesAvailable() > 0) _stream.PushSound(_soundCapture.GetBuffer(_soundCapture.GetFramesAvailable()));

        _hostTimer -= delta;
        if (_hostTimer <= 0)
        {
            _hostTimer = 1;
            if (_host != null && _host.UpdateBounds()) BuildOverlayPosition();
            ApplyState(_ignoreOcclusion ? RenderState.Full : _host?.Evaluate() ?? RenderState.Full);
            _sound.Yielding = _audioYield?.OthersPlaying ?? false;
            AdjustQuality();
        }

        _sim.Paused = _tray.Paused || _replaying;
        _sim.Speed = _tray.Speed > 1 ? _tray.Speed : _baseSpeed;
        _overlay.Visible = _settings.ShowOverlay;
        if (_renderEnabled)
        {
            _worldTimer -= delta;
            if (_worldTimer <= 0)
            {
                _worldTimer = 1;
                _view.SyncWorld();
                WorldSnapshot snapshot = _sim.Snapshot;
                if (snapshot.RunComplete && !_replaying) BeginReplay(snapshot);
                if (!_replaying) DirectCamera(snapshot);
                _sound.Era = snapshot.Era;
                UpdateOverlay(snapshot);
            }
            if (_replaying && !_tray.Paused) StepReplay(delta);
            if (_restartEvery > 0 && (_restartTimer -= delta) <= 0)
            {
                _restartTimer = _restartEvery;
                StartNewWorld();
            }
            HandleMouse();
            UpdateCamera((float)delta);
            if (!_tray.Paused) _view.SyncPeople();
            _sunTimer -= delta;
            if (_sunTimer <= 0)
            {
                _sunTimer = 3;
                UpdateSun();
            }
        }
        else if ((_sunTimer -= delta) <= 0)
        {
            // While the wallpaper is covered nothing renders, but villagers still keep day and night.
            _sunTimer = 3;
            _sim.Night = IsNight(CurrentHour());
        }

        _metricTimer -= delta;
        if (_metrics != null && _metricTimer <= 0)
        {
            _metricTimer = 5;
            WriteMetrics();
        }
    }

    /// <summary>Lowers render resolution while the frame rate is short, and restores it after a long steady spell.</summary>
    private void AdjustQuality()
    {
        if (_state != RenderState.Full) return;
        double fps = Engine.GetFramesPerSecond();
        _slowSeconds = fps < 25 ? _slowSeconds + 1 : 0;
        _steadySeconds = fps >= 29 ? _steadySeconds + 1 : 0;
        int next = _quality;
        if (_slowSeconds >= 10 && _quality < MaxQuality) next++;
        else if (_steadySeconds >= 300 && _quality > 0) next--;
        if (next == _quality) return;
        _quality = next;
        _slowSeconds = _steadySeconds = 0;
        GetViewport().Scaling3DScale = RenderScales[_quality];
        _sun.ShadowEnabled = _quality < 2;
        _view.SetDetail(_quality);
    }

    /// <summary>The portable launcher sets up its runtime, so Windows must start it rather than the engine.</summary>
    private static string StartupLine()
    {
        string engine = OS.GetExecutablePath().Replace('/', '\\');
        string launcher = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(engine)) ?? "", "DeskCiv.cmd");
        if (File.Exists(launcher)) return $"cmd /c \"\"{launcher}\"\"";
        return $"\"{engine}\" --path \"{ProjectSettings.GlobalizePath("res://").TrimEnd('/').Replace('/', '\\')}\"";
    }

    private void BeginReplay(WorldSnapshot s)
    {
        _replaying = true;
        _replayTime = 0;
        _manual = false;
        _focusTarget = FocusAt(s.CenterX, s.CenterZ);
        _distanceTarget = Mathf.Min(CameraDistance(s) * 1.15f, MaxZoom);
        _replayLabel.Visible = _settings.ShowOverlay;
    }

    /// <summary>Shows one card per era while the camera circles the finished town, then starts a new world.</summary>
    private void StepReplay(double delta)
    {
        _replayTime += delta;
        WorldSnapshot s = _sim.Snapshot;
        _azimuthTarget = (float)(_replayTime / _replaySeconds * MathF.Tau);
        int cards = s.History.Length + 1;
        int card = Math.Min(cards - 1, (int)(_replayTime / _replaySeconds * cards));
        var culture = CultureInfo.GetCultureInfo("en-US");
        if (card < s.History.Length)
        {
            EraRecord e = s.History[card];
            _replayLabel.Text = $"{Eras.Names[e.Era]}\n{Eras.FormatCalendar(Eras.StartOf(e.Era), culture)} – {Eras.FormatCalendar(Eras.EndOf(e.Era), culture)}\n{e.Population} people  ·  {e.Buildings} buildings  ·  {e.Births} births  ·  {e.Floods} storm surges";
        }
        else _replayLabel.Text = $"A civilisation has lived through 12,000 years\nFinal population {s.Population}  ·  {s.Buildings.Length} buildings\nA new land is waiting";
        if (_replayTime >= _replaySeconds) StartNewWorld();
    }

    private void StartNewWorld()
    {
        _replaying = false;
        _replayLabel.Visible = false;
        _sim.Stop();
        RemoveChild(_view);
        _view.QueueFree();
        _sim = new SimHost(null, true, _canSave, _baseSpeed, 0);
        _view = new WorldView(_sim);
        AddChild(_view);
        _view.SetDetail(_quality);
        GC.Collect();
        WorldSnapshot first = _sim.Snapshot;
        _lastEventTick = -1;
        _focusTarget = _focus = FocusAt(first.CenterX, first.CenterZ);
        _distanceTarget = _distance = CameraDistance(first);
        _azimuth = _azimuthTarget = 0;
        NextShot(first);
        _view.SyncWorld();
        UpdateSun();
        UpdateOverlay(first);
        _sim.Start();
        _sim.SaveNow(2000);
    }

    private void BuildOverlayPosition()
    {
        Rect2I area = _host.PrimaryWorkArea();
        _overlay.Position = area.Position + (Vector2)area.Size - _overlay.Size - new Vector2(26, 18);
        _replayLabel.Position = area.Position + new Vector2(0, area.Size.Y * 0.12f);
        _replayLabel.Size = new Vector2(area.Size.X, 230);
    }

    private void ApplyState(RenderState state)
    {
        if (state == _state) return;
        _state = state;
        _renderEnabled = state != RenderState.Stopped;
        RenderingServer.RenderLoopEnabled = _renderEnabled;
        Engine.MaxFps = state switch { RenderState.Full => FullFps, RenderState.Reduced => ReducedFps, _ => StoppedFps };
        _sound.Suppressed = state == RenderState.Stopped;
    }

    private static float CameraDistance(WorldSnapshot s)
    {
        float radius = 0;
        foreach (BuildingView b in s.Buildings)
            radius = Math.Max(radius, new Vector2(b.X - s.CenterX, b.Z - s.CenterZ).Length());
        return Mathf.Clamp(72f + radius * 1.1f, 72f, 250f);
    }

    private void DirectCamera(WorldSnapshot s)
    {
        if (_manual) return;
        _focusTimer -= 1;
        if (_forcedClip != null || _lookType != null || _focusPoint != null || _watchDig)
        {
            if (_focusTimer <= 0) NextShot(s);
            return;
        }
        if (s.LastEvent is WorldEvent e && e.Tick != _lastEventTick)
        {
            bool first = _lastEventTick < 0;
            _lastEventTick = e.Tick;
            if (!first && e.Importance >= 2)
            {
                _focusTarget = FocusAt(e.X, e.Z);
                _distanceTarget = CloseDistance * 1.35f;
                _azimuthTarget = ClearAzimuth(s, e.X, e.Z);
                // A funeral, feast or era celebration takes the crowd most of a minute to walk in; stay for it.
                _focusTimer = s.Gathering ? 75 : 30;
            }
        }
        if (_focusTimer <= 0) NextShot(s);
    }

    /// <summary>Picks the next working group to watch, with a periodic view of the whole settlement.</summary>
    private void NextShot(WorldSnapshot s)
    {
        _shotNumber++;
        _liftTarget = 0;
        if (_focusPoint is Vector2 point)
        {
            _focusTarget = FocusAt(point.X, point.Y);
            _distanceTarget = CloseDistance;
            _azimuthTarget = ClearAzimuth(s, point.X, point.Y);
            _focusTimer = 100000;
            return;
        }
        if (_lookType is BType look)
        {
            foreach (BuildingView b in s.Buildings)
            {
                if (b.Type != look || !b.Built) continue;
                _focusTarget = FocusAt(b.X, b.Z) + new Vector3(0, 3f, 0);
                _distanceTarget = CloseDistance;
                _azimuthTarget = ClearAzimuth(s, b.X, b.Z, 5f);
                _focusTimer = 100000;
                return;
            }
            _focusTimer = 1;
            return;
        }
        // Now and then the camera watches a street being dug up for water mains or sewers.
        if (_forcedClip == null && (_watchDig || _shotNumber % 7 == 6) && s.DigTile >= 0)
        {
            (float dx, float dz) = Simulation.TileCenter(s.DigTile);
            _focusTarget = FocusAt(dx, dz);
            _distanceTarget = CloseDistance * 1.2f;
            _azimuthTarget = ClearAzimuth(s, dx, dz);
            _focusTimer = _watchDig ? 15 : 24;
            return;
        }
        // Now and then the camera visits a town landmark: a well or fountain, the water tower, the boiler house.
        if (_forcedClip == null && _shotNumber % 7 == 3 && _view.Landmark(_shotNumber / 7) is Vector3 landmark)
        {
            _focusTarget = FocusAt(landmark.X, landmark.Z);
            _distanceTarget = CloseDistance * 1.6f;
            _azimuthTarget = ClearAzimuth(s, landmark.X, landmark.Z, 8f);
            _focusTimer = 20;
            return;
        }
        if (_forcedClip == null && _shotNumber % 5 == 0)
        {
            _focusTarget = FocusAt(s.CenterX, s.CenterZ);
            _distanceTarget = CameraDistance(s);
            _azimuthTarget = 0;
            _focusTimer = 24;
            return;
        }
        int best = -1, count = 0;
        for (int i = 0; i < s.Spots.Length; i++)
        {
            Clip clip = s.Spots[i].Clip;
            bool wanted = _forcedClip != null ? clip == _forcedClip : clip != _lastShotClip;
            if (!wanted) continue;
            count++;
            if ((_shotNumber * 7919 + i * 31) % count == 0) best = i;
        }
        if (best < 0 && _forcedClip == null && s.Spots.Length > 0) best = _shotNumber % s.Spots.Length;
        if (best < 0)
        {
            _focusTarget = FocusAt(s.CenterX, s.CenterZ);
            _distanceTarget = CameraDistance(s);
            _azimuthTarget = 0;
            _focusTimer = _forcedClip != null ? 1 : 18;
            return;
        }
        _lastShotClip = s.Spots[best].Clip;
        _focusTarget = FocusAt(s.Spots[best].X, s.Spots[best].Z);
        _distanceTarget = CloseDistance;
        _azimuthTarget = ClearAzimuth(s, s.Spots[best].X, s.Spots[best].Z);
        _focusTimer = _forcedClip != null ? 100000 : 34;
    }

    /// <summary>
    /// Finds the viewing direction where the fewest buildings and trees stand between the camera and the target.
    /// The camera looks down at a shallow angle, so a building blocks the view when it is taller than the sight
    /// line at that distance. If every direction is blocked, the camera also rises for this shot.
    /// </summary>
    private float ClearAzimuth(WorldSnapshot s, float x, float z, float skip = 3f)
    {
        float distance = Mathf.Max(_distanceTarget, 10f);
        float slope = SightHeight(distance, 0f) / 0.92f;
        (float[] treeX, float[] treeZ) = _sim.TreePositions;
        float bestCost = float.MaxValue, bestAngle = 0;
        for (int k = 0; k < 16; k++)
        {
            // 0, +22.5, -22.5, +45, ... so the usual front view wins ties.
            float degree = (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 22.5f;
            float angle = Mathf.DegToRad(degree), dx = MathF.Sin(angle), dz = MathF.Cos(angle);
            float cost = 0;
            for (float d = skip; d <= Mathf.Min(45f, distance * 0.92f) && cost < bestCost; d += 1.5f)
            {
                float sight = d * slope + 1f;
                if (sight > 18f) break;
                float px = x + dx * d, pz = z + dz * d;
                int tx = Math.Clamp((int)((px + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1);
                int tz = Math.Clamp((int)((pz + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1);
                if (s.Tiles[tx + tz * Rules.TileNx] == TileKind.Building) { cost += (18f - sight) / d; continue; }
                if (sight > 9f) continue;
                for (int i = 0; i < treeX.Length; i++)
                {
                    if (!s.TreeAlive[i]) continue;
                    float ax = treeX[i] - px, az = treeZ[i] - pz;
                    if (ax * ax + az * az < 9f) { cost += (9f - sight) / d * 0.5f; break; }
                }
            }
            if (cost < bestCost) { bestCost = cost; bestAngle = angle; }
            if (cost == 0) break;
        }
        _liftTarget = Mathf.Clamp(bestCost / 2f, 0f, 1f);
        return bestAngle;
    }

    /// <summary>Camera height as a fraction of its distance; raised for shots that would otherwise be blocked.</summary>
    private static float SightHeight(float distance, float lift) =>
        Mathf.Lerp(0.36f, 0.58f, Mathf.Clamp((distance - 40f) / 120f, 0f, 1f)) + lift * 0.5f;

    private Vector3 FocusAt(float x, float z) => new(x, Mathf.Max(0, _sim.Map.Height(x, z)) + 1f, z);

    /// <summary>Wheel zooms toward the pointer and middle-drag moves the map. The director resumes after idle time.</summary>
    private void HandleMouse()
    {
        if (_host == null) return;
        int wheel = _tray.TakeWheel();
        bool panning = _tray.Panning;
        if (wheel == 0 && !panning)
        {
            _wasPanning = false;
            if (_manual && _elapsed >= _manualUntil)
            {
                _manual = false;
                _focusTimer = 0;
            }
            return;
        }

        _manual = true;
        _manualUntil = _elapsed + ManualSeconds;
        Vector2 cursor = _host.CursorInWindow();
        if (wheel != 0)
        {
            float before = _distanceTarget;
            _distanceTarget = Mathf.Clamp(before * MathF.Pow(0.88f, wheel), MinZoom, MaxZoom);
            Vector3 origin = _camera.ProjectRayOrigin(cursor), ray = _camera.ProjectRayNormal(cursor);
            if (ray.Y < -0.01f)
            {
                Vector3 ground = origin + ray * ((_focus.Y - origin.Y) / ray.Y);
                float keep = 1f - _distanceTarget / before;
                _focusTarget += new Vector3(ground.X - _focusTarget.X, 0, ground.Z - _focusTarget.Z) * keep;
            }
        }
        if (panning)
        {
            if (_wasPanning)
            {
                Vector2 moved = cursor - _panLast;
                Vector3 offset = _camera.GlobalPosition - _focus;
                float length = offset.Length();
                float perPixel = 2f * length * MathF.Tan(Mathf.DegToRad(_camera.Fov / 2)) / Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
                float depth = length / Math.Max(1f, offset.Y);
                Vector3 right = new(MathF.Cos(_azimuth), 0, -MathF.Sin(_azimuth)), back = new(MathF.Sin(_azimuth), 0, MathF.Cos(_azimuth));
                _focusTarget -= right * moved.X * perPixel + back * moved.Y * perPixel * depth;
            }
            _panLast = cursor;
        }
        _wasPanning = panning;
        float x = Mathf.Clamp(_focusTarget.X, -WorldMap.SizeX / 2 + 12, WorldMap.SizeX / 2 - 12);
        float z = Mathf.Clamp(_focusTarget.Z, -WorldMap.SizeZ / 2 + 12, WorldMap.SizeZ / 2 - 12);
        _focusTarget = FocusAt(x, z);
    }

    private void UpdateCamera(float delta)
    {
        float ease = 1f - MathF.Exp(-delta / (_manual ? 0.09f : 7.5f));
        _driftAmount = Mathf.MoveToward(_driftAmount, _manual ? 0f : 1f, delta / 2f);
        _focus = _focus.Lerp(_focusTarget, ease);
        _distance = Mathf.Lerp(_distance, _distanceTarget, ease);
        _azimuth = Mathf.LerpAngle(_azimuth, _azimuthTarget, ease);
        float t = (float)_elapsed;
        var drift = new Vector3(MathF.Sin(t / 37f) * _distance * 0.055f, 0, MathF.Sin(t / 53f) * _distance * 0.03f) * _driftAmount;
        Vector3 target = _focus + drift * 0.45f;
        _lift = Mathf.Lerp(_lift, _liftTarget, ease);
        float height = SightHeight(_distance, _lift);
        var position = _focus + drift + new Vector3(MathF.Sin(_azimuth) * _distance * 0.92f, _distance * height, MathF.Cos(_azimuth) * _distance * 0.92f);
        position.X = Mathf.Clamp(position.X, -WorldMap.SizeX / 2 + 6, WorldMap.SizeX / 2 - 6);
        position.Z = Mathf.Clamp(position.Z, -WorldMap.SizeZ / 2 + 6, WorldMap.SizeZ / 2 - 6);
        float ground = _sim.Map.Height(position.X, position.Z) + 5f;
        if (position.Y < ground) position.Y = ground;
        _camera.LookAtFromPosition(position, target, Vector3.Up);
        if (_rain != null) _rain.Position = target + new Vector3(0, 32, 0);
        _sun.DirectionalShadowMaxDistance = Mathf.Clamp(_distance * 1.7f, 90f, 400f);
        _view.SetViewCenter(_focus);
        _view.UpdateFallingTrees(delta);
    }

    private float CurrentHour() => _fixedHour ?? DateTime.Now.Hour + DateTime.Now.Minute / 60f;

    private static bool IsNight(float hour) => hour < 6f || hour >= 20f;

    private void UpdateSun()
    {
        float hour = CurrentHour();
        _sim.Night = IsNight(hour);
        float arc = (hour - 6f) / 12f * Mathf.Pi;
        float height = MathF.Sin(arc), day = Mathf.SmoothStep(-0.12f, 0.2f, height);
        _sun.Rotation = new Vector3(-Mathf.Max(0.18f, MathF.Abs(height)) * 1.25f, MathF.Cos(arc) * 1.2f, 0);
        // The Compatibility renderer draws a shadowed sun in an extra additive pass that comes out several times brighter than the unshadowed path.
        (float cover, float rain) = Weather();
        // Low sun at dawn and dusk is warm orange; clouds and rain soften the light and the shadows.
        float golden = day * (1f - Mathf.SmoothStep(0.15f, 0.5f, height)) * (1f - cover * 0.7f);
        _sun.LightEnergy = (0.52f + day * 0.56f) * (1f - cover * 0.45f) * (_sun.ShadowEnabled ? 0.35f : 1f);
        _sun.ShadowOpacity = (0.35f + day * 0.5f) * (1f - cover * 0.6f);
        _sun.LightColor = new Color(0.72f, 0.78f, 0.98f).Lerp(new Color(1f, 0.95f, 0.84f), day).Lerp(new Color(1f, 0.68f, 0.42f), golden * 0.8f);
        Color sky = new Color(0.24f, 0.31f, 0.46f).Lerp(new Color(0.58f, 0.74f, 0.88f), day).Lerp(new Color(0.95f, 0.62f, 0.45f), golden * 0.5f);
        sky = sky.Lerp(new Color(0.55f, 0.58f, 0.6f) * (0.4f + 0.6f * day), cover * 0.75f);
        _environment.BackgroundColor = sky;
        _environment.FogLightColor = sky;
        _environment.AmbientLightColor = new Color(0.56f, 0.62f, 0.78f).Lerp(new Color(0.72f, 0.76f, 0.78f), day);
        _environment.AmbientLightEnergy = 0.8f - day * (_sun.ShadowEnabled ? 0.25f : 0.06f) * (1f - cover * 0.5f);
        // Morning mist over the estuary between 5 and 9 o'clock, and haze in rain.
        float mist = Mathf.SmoothStep(4.5f, 6f, hour) * (1f - Mathf.SmoothStep(7.5f, 9.5f, hour));
        _environment.FogDensity = 0.0012f + mist * 0.0045f + rain * 0.003f;
        RenderingServer.GlobalShaderParameterSet("cloud_cover", cover);
        RenderingServer.GlobalShaderParameterSet("rain", rain);
        RenderingServer.GlobalShaderParameterSet("daylight", day);
        SetRain(rain);
        if (_sound != null)
        {
            _sound.Rain = rain;
            _sound.Daylight = day;
        }
        _view?.SetNightGlow(1f - day);
    }

    /// <summary>
    /// Slowly changing weather from the wall clock: clear spells, cloudy spells and occasional rain, so the
    /// wallpaper looks different through the day. --weather=clear|cloudy|rain fixes it for tests.
    /// </summary>
    private (float Cover, float Rain) Weather()
    {
        if (_forcedWeather is string forced)
            return forced switch { "rain" => (0.95f, 1f), "cloudy" => (0.65f, 0f), _ => (0.1f, 0f) };
        double minutes = DateTime.Now.Ticks / (double)TimeSpan.TicksPerMinute;
        float cover = 0.42f + 0.3f * MathF.Sin((float)(minutes / 53.0)) + 0.18f * MathF.Sin((float)(minutes / 17.0) + 1.3f) + 0.1f * MathF.Sin((float)(minutes / 7.3) + 4.1f);
        // A drought year keeps the sky dry from late spring to autumn.
        WorldSnapshot s = _sim?.Snapshot;
        if (s != null && s.ActiveEvent == 1 && s.YearFraction is > 0.1f and < 0.8f) cover = Math.Min(cover, 0.3f);
        cover = Mathf.Clamp(cover, 0f, 1f);
        return (cover, Mathf.SmoothStep(0.78f, 0.92f, cover));
    }

    /// <summary>GPU rain streaks in a box over the area the camera watches.</summary>
    private void SetRain(float rain)
    {
        if (_rain == null)
        {
            var process = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(45, 1, 45),
                Direction = new Vector3(0.12f, -1, 0.06f),
                Spread = 2,
                InitialVelocityMin = 22,
                InitialVelocityMax = 27,
                Gravity = Vector3.Zero,
                ParticleFlagAlignY = true,
            };
            var drop = new QuadMesh { Size = new Vector2(0.03f, 0.9f) };
            drop.Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.8f, 0.85f, 0.92f, 0.35f),
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                BillboardKeepScale = true,
            };
            _rain = new GpuParticles3D
            {
                Amount = 3000, Lifetime = 1.5, ProcessMaterial = process, DrawPass1 = drop, Emitting = false,
                VisibilityAabb = new Aabb(new Vector3(-50, -45, -50), new Vector3(100, 50, 100)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_rain);
        }
        _rain.Emitting = rain > 0.05f;
        _rain.AmountRatio = Mathf.Clamp(rain, 0.05f, 1f);
    }

    private void UpdateOverlay(WorldSnapshot s)
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        string era = Eras.Names[s.Era];
        // News fades after about a simulated year (half an hour), so an old drought does not sit on screen for hours.
        string line = s.LastEvent is WorldEvent news && s.Year - news.Tick / (float)Rules.TicksPerYear < 1f ? news.Text : "";
        string speed = _sim.Speed > 1 ? $"  ·  Speed x{_sim.Speed}" : "";
        string project = CurrentProject(s);
        _overlay.Text = $"{Eras.FormatCalendar(s.Calendar, culture)}  ·  {era}  ·  {Rules.SeasonName(s.Season)}{speed}\nPopulation {s.Population}  ·  Food {s.Food}{project}\n{line}";
    }

    /// <summary>What the village is working on now: draining a polder, or the most advanced building site.</summary>
    private static string CurrentProject(WorldSnapshot s)
    {
        if (s.PolderDrain >= 0) return $"  ·  Draining the polder {(int)(s.PolderDrain * 100)}%";
        BuildingView? best = null;
        foreach (BuildingView b in s.Buildings)
            if (!b.Built && b.Type is not (BType.Field or BType.Vineyard) && (best == null || b.Progress > best.Value.Progress)) best = b;
        if (best is not BuildingView site) return "";
        string name = site.Type switch
        {
            BType.Hut => "a home",
            BType.TideGate => "a tide gate",
            BType.Dike => "a dike",
            _ => "a " + System.Text.RegularExpressions.Regex.Replace(site.Type.ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant(),
        };
        if (site.Renewed) return $"  ·  Rebuilding an old house {(int)(site.Progress * 100)}%";
        return site.Progress < 0.01f ? $"  ·  Marking out {name}" : $"  ·  Building {name} {(int)(site.Progress * 100)}%";
    }

    private void OpenMetrics(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _metrics = new StreamWriter(path, false) { AutoFlush = true };
        _metrics.WriteLine("seconds,fps,cpu_pct_all_cores,ram_mb,state,width,height,master_db,mode,population,year,camera_distance,focus_x,focus_z,manual,distance_target,manual_left,gpu_ms,cpu_render_ms");
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        using Process process = Process.GetCurrentProcess();
        _lastCpu = process.TotalProcessorTime;
        _lastWall = Stopwatch.GetTimestamp();
    }

    private void WriteMetrics()
    {
        using Process process = Process.GetCurrentProcess();
        long wall = Stopwatch.GetTimestamp();
        TimeSpan cpu = process.TotalProcessorTime;
        double seconds = (wall - _lastWall) / (double)Stopwatch.Frequency;
        double percent = seconds > 0 ? (cpu - _lastCpu).TotalSeconds / seconds / System.Environment.ProcessorCount * 100 : 0;
        _lastCpu = cpu;
        _lastWall = wall;
        Vector2I size = DisplayServer.WindowGetSize();
        WorldSnapshot s = _sim.Snapshot;
        _metrics.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{_elapsed:F0},{Engine.GetFramesPerSecond():F1},{percent:F2},{process.WorkingSet64 / 1048576.0:F0},{_state},{size.X},{size.Y},{_sound.MasterDb:F1},{_host?.Mode ?? "windowed"},{s.Population},{s.Year:F2},{_distance:F1},{_focus.X:F1},{_focus.Z:F1},{_manual},{_distanceTarget:F1},{_manualUntil - _elapsed:F1},{RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()):F2},{RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()):F2}"));
    }

    private void Shutdown()
    {
        if (_closed) return;
        _closed = true;
        _stream?.Dispose();
        _sim.Stop();
        _tray.Stop();
        _audioYield?.Dispose();
        _metrics?.Dispose();
        _metrics = null;
        _host?.Detach();
        _host = null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && _sim != null) Shutdown();
    }

    private static bool HasFlag(string[] args, string flag) => Array.IndexOf(args, flag) >= 0;

    private static double Number(string[] args, string name, double fallback)
    {
        string text = Option(args, name, null);
        return text == null ? fallback : double.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string Option(string[] args, string name, string fallback)
    {
        foreach (string arg in args)
            if (arg.StartsWith(name + "=", StringComparison.Ordinal)) return arg[(name.Length + 1)..];
        return fallback;
    }
}

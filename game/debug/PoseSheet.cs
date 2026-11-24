using System;
using Godot;

namespace DeskCiv;

/// <summary>Review scene: every work clip from the front and side. User args: --shot=PNG --phase=0..1.</summary>
public partial class PoseSheet : Node3D
{
    private string _shot;
    private double _elapsed;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        float phase = 0.5f;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--shot=", StringComparison.Ordinal)) _shot = arg[7..];
            if (arg.StartsWith("--phase=", StringComparison.Ordinal)) phase = float.Parse(arg[8..], System.Globalization.CultureInfo.InvariantCulture);
        }

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.62f, 0.72f, 0.66f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 25, 0), LightEnergy = 1.0f });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 15.5f, Current = true };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 7, 20), new Vector3(0, 0.05f, 0), Vector3.Up);

        var kit = new CharacterKit();
        int clips = kit.Clips.Length;
        for (int row = 0; row < 2; row++)
        {
            float yaw = row == 0 ? 0 : Mathf.Pi / 2;
            for (int c = 0; c < clips; c++)
            {
                Vector3 position = new((c - (clips - 1) / 2f) * 2.25f, 0, row == 0 ? -2.4f : 2.4f);
                AddInstance(kit, CharacterKit.LoadMesh(OutfitFor((Clip)c)), position, yaw, c, phase);
                int tool = CharacterKit.ToolFor((Clip)c);
                if (tool >= 0) AddInstance(kit, CharacterKit.LoadMesh("tool_" + CharacterKit.Tools[tool]), position, yaw, c, phase);
            }
        }
    }

    private static string OutfitFor(Clip clip) => clip switch
    {
        Clip.Chop => "woodcutter",
        Clip.Hoe or Clip.Sow or Clip.Gather or Clip.Reap => "farmer",
        Clip.Mine => "miner",
        Clip.Fish => "fisher",
        Clip.Build => "builder",
        Clip.Knead => "baker",
        Clip.Carry => "woman",
        _ => "laborer",
    };

    private void AddInstance(CharacterKit kit, Mesh mesh, Vector3 position, float yaw, int clip, float phase)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
        multi.SetInstanceTransform(0, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * 1.12f), position));
        multi.SetInstanceColor(0, Colors.White);
        (int start, int length) = kit.Clips[clip];
        multi.SetInstanceCustomData(0, new Color(start, length, phase, 0));
        AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = kit.Material, CustomAabb = new Aabb(position - Vector3.One * 5, Vector3.One * 10) });
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < 0.6) return;
        if (_shot != null) GetViewport().GetTexture().GetImage().SavePng(_shot);
        GetTree().Quit();
    }
}

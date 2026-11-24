using System;
using Godot;

namespace DeskCiv;

/// <summary>Shows the in-game woodcutter at game scale beside the game's tree. Usage: --out=DIR.</summary>
public partial class WoodcutterPreview : Node3D
{
    private const float GameScale = 1.12f, TreeDistance = 1.55f, Spacing = 5f;
    private static readonly int[] ChopFrames = { 26, 0, 2, 4, 5, 6, 7, 12 };
    private Camera3D _camera;
    private string _out = ".";
    private int _frames;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) _out = arg[6..].Trim('"');
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.36f, 0.53f, 0.25f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.72f, 0.76f, 0.78f), AmbientLightEnergy = 0.75f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-52, -20, 0), LightEnergy = 1.1f });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(120, 120) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.53f, 0.25f) },
        });

        var kit = new CharacterKit.Textured("hq_woodcutter", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Chop);
        var bodies = Skinned(kit.Body, kit.BodyMaterial, 11);
        var tools = Skinned(kit.Tool, kit.ToolMaterial, 8);
        for (int i = 0; i < 8; i++)
        {
            float x = (i - 3.5f) * Spacing;
            // Trunk only, matching MeshKit.Tree, so the canopy does not hide the contact point.
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { BottomRadius = 0.42f, TopRadius = 0.28f, Height = 1.8f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.34f, 0.24f, 0.16f) },
                Position = new Vector3(x, 0.9f, -2),
            });
            // A one-frame clip holds the pose still, so the swing can be inspected around contact (frame 6).
            (int start, _) = kit.ClipFor(Clip.Chop);
            var held = (start + ChopFrames[i], 1);
            Place(bodies, i, held, 0, new Vector3(x - TreeDistance, 0, -2), MathF.PI / 2 - CharacterKit.Textured.ChopAim);
            Place(tools, i, held, 0, new Vector3(x - TreeDistance, 0, -2), MathF.PI / 2 - CharacterKit.Textured.ChopAim);
        }
        Place(bodies, 8, kit.ClipFor(Clip.Idle), 0.2f, new Vector3(-Spacing, 0, 3), 0);
        Place(bodies, 9, kit.ClipFor(Clip.Walk), 0.3f, new Vector3(0, 0, 3), 0);
        Place(bodies, 10, kit.ClipFor(Clip.Carry), 0.6f, new Vector3(Spacing, 0, 3), 0);
        _camera = new Camera3D { Fov = 30 };
        AddChild(_camera);
        _camera.MakeCurrent();
        _camera.LookAtFromPosition(new Vector3(0, 21, 66), new Vector3(0, 1.3f, 0), Vector3.Up);
    }

    private MultiMesh Skinned(Mesh mesh, Material material, int count)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = count };
        AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = material, CustomAabb = new Aabb(new Vector3(-60, -5, -60), new Vector3(120, 30, 120)) });
        return multi;
    }

    private static void Place(MultiMesh multi, int index, (int Start, int Length) clip, float phase, Vector3 position, float yaw)
    {
        multi.SetInstanceTransform(index, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * GameScale), position));
        multi.SetInstanceColor(index, Colors.White);
        multi.SetInstanceCustomData(index, new Color(clip.Start, clip.Length, phase, 0));
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == 20) GetViewport().GetTexture().GetImage().SavePng(_out + "/game-distance.png");
        if (_frames == 21) _camera.LookAtFromPosition(new Vector3(0, 9, 42), new Vector3(0, 1.1f, -2), Vector3.Up);
        if (_frames == 42)
        {
            GetViewport().GetTexture().GetImage().SavePng(_out + "/close.png");
            GetTree().Quit();
        }
    }
}

using System;
using DeskCiv.Sim;
using Godot;

namespace DeskCiv;

/// <summary>Renders the animated buildings off-screen into two time samples. Usage: --out=DIR.</summary>
public partial class BuildingPreview : Node
{
    private const string Shader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform float clock = 0.0;
void vertex() {
    float t = clock * INSTANCE_CUSTOM.x + INSTANCE_CUSTOM.y;
    int mode = int(INSTANCE_CUSTOM.z + 0.5);
    if (mode == 1) {
        VERTEX.y += (sin(t) * 0.5 + 0.5) * INSTANCE_CUSTOM.w;
    } else {
        float a = mode == 0 ? t : sin(t) * INSTANCE_CUSTOM.w;
        float c = cos(a), s = sin(a);
        VERTEX.xy = vec2(c * VERTEX.x - s * VERTEX.y, s * VERTEX.x + c * VERTEX.y);
        NORMAL.xy = vec2(c * NORMAL.x - s * NORMAL.y, s * NORMAL.x + c * NORMAL.y);
    }
}
void fragment() { ALBEDO = COLOR.rgb; }";

    private static readonly BType[] Types = { BType.Windmill, BType.Watermill, BType.Mill, BType.Vineyard, BType.Winery, BType.Smithy, BType.Dike, BType.TideGate };
    private SubViewport _viewport;
    private ShaderMaterial _material;
    private string _out = ".";
    private int _frames;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--out=", StringComparison.Ordinal)) _out = arg[6..].Trim('"');
        _viewport = new SubViewport { Size = new Vector2I(1600, 900), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_viewport);
        var world = new Node3D();
        _viewport.AddChild(world);
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.2f, 0.42f, 0.56f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.72f, 0.76f, 0.78f), AmbientLightEnergy = 0.75f,
            },
        });
        world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-52, -30, 0), LightEnergy = 1.1f });
        var vertex = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };
        _material = new ShaderMaterial { Shader = new Shader { Code = Shader } };
        for (int i = 0; i < Types.Length; i++)
        {
            Vector3 at = new((i % 4 - 1.5f) * 25, 0, (i / 4 - 0.5f) * 26);
            world.AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(22, 12) },
                Position = at + new Vector3(0, -0.02f, 6),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.53f, 0.25f) },
            });
            world.AddChild(new MeshInstance3D { Mesh = MeshKit.Building(Types[i]), MaterialOverride = vertex, Position = at });
            if (MeshKit.Moving(Types[i]) is not MeshKit.MovingPart part) continue;
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = part.Mesh, InstanceCount = 1 };
            Basis basis = Types[i] == BType.Windmill ? new Basis(Vector3.Up, MathF.PI / 2) : part.Local;
            multi.SetInstanceTransform(0, new Transform3D(basis, at + part.Pivot));
            multi.SetInstanceColor(0, Colors.White);
            multi.SetInstanceCustomData(0, new Color(part.Speed, 0.3f, part.Mode, part.Amount));
            world.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = _material, CustomAabb = new Aabb(new Vector3(-80, -10, -80), new Vector3(160, 60, 160)) });
        }
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 58 };
        world.AddChild(camera);
        camera.MakeCurrent();
        camera.LookAtFromPosition(new Vector3(-22, 40, 58), new Vector3(0, 1, 3), Vector3.Up);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_frames == 12)
        {
            _viewport.GetTexture().GetImage().SavePng(_out + "/buildings-a.png");
            _material.SetShaderParameter("clock", 1.4f);
        }
        if (_frames == 24)
        {
            _viewport.GetTexture().GetImage().SavePng(_out + "/buildings-b.png");
            GetTree().Quit();
        }
    }
}

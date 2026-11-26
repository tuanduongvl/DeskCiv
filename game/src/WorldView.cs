using System;
using DeskCiv.Sim;
using Godot;

namespace DeskCiv;

/// <summary>Draws the published simulation state with instanced low-poly meshes.</summary>
public partial class WorldView : Node3D
{
    private const string TerrainShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D wear_tex : filter_linear, repeat_disable;
uniform sampler2D road_tex : filter_linear, repeat_disable;
uniform vec3 season_tint = vec3(1.0);
uniform float snow = 0.0;
uniform sampler2D polder_tex : filter_nearest, repeat_disable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float road_style = 0.0;
global uniform float cloud_cover;
global uniform float rain;
global uniform float daylight;
// CC0 photo textures from Poly Haven (see THIRD_PARTY_NOTICES.md), tiled in world metres.
uniform sampler2D grass_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D dirt_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D sand_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D rock_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D cobble_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D asphalt_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float street_style = 0.0;
varying vec3 world_pos;
// Two scales, the second rotated, blended by noise, so a tile does not repeat visibly.
vec3 photo(sampler2D t, vec2 p, float metres, float blend) {
    vec2 q = p / metres;
    vec3 a = texture(t, q).rgb;
    vec3 b = texture(t, mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * q * 0.43 + vec2(0.31, 0.17)).rgb;
    return mix(a, b, blend);
}
// Rounded cobble stones: bright centres, dark mortar lines.
float cobble(vec2 p) {
    vec2 cell = p + vec2(0.0, floor(p.x) * 0.5);
    vec2 f = fract(cell) - 0.5;
    return 1.0 - smoothstep(0.28, 0.5, max(abs(f.x), abs(f.y)));
}
varying float up;
varying float reclaim;
void vertex() {
    ivec2 size = textureSize(polder_tex, 0);
    ivec2 cell = clamp(ivec2(round((VERTEX.xz + vec2(480.0, 260.0)) / 2.5)), ivec2(0), size - 1);
    vec2 here = texelFetch(polder_tex, cell, 0).rg;
    reclaim = here.g;
    if (here.g > 0.0) {
        float l = texelFetch(polder_tex, clamp(cell - ivec2(1, 0), ivec2(0), size - 1), 0).r;
        float r = texelFetch(polder_tex, clamp(cell + ivec2(1, 0), ivec2(0), size - 1), 0).r;
        float d = texelFetch(polder_tex, clamp(cell - ivec2(0, 1), ivec2(0), size - 1), 0).r;
        float u = texelFetch(polder_tex, clamp(cell + ivec2(0, 1), ivec2(0), size - 1), 0).r;
        VERTEX.y = here.r;
        NORMAL = normalize(vec3(l - r, 5.0, d - u));
    }
    world_pos = VERTEX;
    up = NORMAL.y;
}
void fragment() {
    vec2 uv = (world_pos.xz + vec2(480.0, 260.0)) / vec2(960.0, 520.0);
    float wear = smoothstep(0.03, 0.42, texture(wear_tex, uv).r);
    vec3 soil = COLOR.rgb;
    if (reclaim > 0.0) soil = mix(soil, mix(vec3(0.36, 0.31, 0.23), vec3(0.34, 0.54, 0.25), smoothstep(0.5, 1.0, reclaim)), smoothstep(0.0, 0.12, reclaim));
    float green = clamp((soil.g - soil.r) * 7.0, 0.0, 1.0);
    vec3 c = mix(soil, soil * season_tint, green);
    float patches = sin(world_pos.x * 0.037 + sin(world_pos.z * 0.021) * 2.0) * sin(world_pos.z * 0.043 + sin(world_pos.x * 0.029) * 1.7);
    c *= 1.0 + green * patches * 0.05;
    // Large damp and dry patches plus small clumps break up flat grass.
    float broad = texture(noise_tex, world_pos.xz / 140.0).r;
    float clump = texture(noise_tex, world_pos.xz / 11.0 + vec2(0.37, 0.71)).r;
    float dry = smoothstep(0.52, 0.8, broad);
    c = mix(c, c * vec3(1.02, 0.94, 0.76), dry * green * 0.6);
    c = mix(c, c * vec3(0.8, 0.9, 0.82), smoothstep(0.45, 0.2, broad) * green * 0.6);
    c *= 1.0 + (clump - 0.5) * 0.16;
    // Photo detail: grass blades on green land, grains on sand, keeping the biome and season colour.
    float mixer = smoothstep(0.3, 0.7, clump);
    // The smallest mip level is the texture's average colour, in the same colour space as the samples.
    vec3 grassDetail = photo(grass_tex, world_pos.xz, 5.0, mixer) / textureLod(grass_tex, vec2(0.5), 12.0).rgb;
    vec3 sandDetail = photo(sand_tex, world_pos.xz, 6.0, mixer) / textureLod(sand_tex, vec2(0.5), 12.0).rgb;
    vec3 detail = mix(sandDetail, grassDetail, green);
    c *= mix(vec3(1.0), mix(vec3(dot(detail, vec3(0.333))), detail, 0.35), 0.85);
    vec3 dirt = photo(dirt_tex, world_pos.xz, 7.0, mixer) / textureLod(dirt_tex, vec2(0.5), 12.0).rgb;
    vec3 earthPhoto = vec3(0.47, 0.38, 0.27) * mix(vec3(dot(dirt, vec3(0.333))), dirt, 0.5);
    c = mix(c, earthPhoto, wear * 0.6);
    vec3 use = texture(road_tex, uv).rgb;
    // Quarry scars: pale broken stone and gravel with darker pits.
    float spoil = smoothstep(0.15, 0.7, use.b) * (0.6 + 0.4 * clump);
    vec3 rubble = photo(rock_tex, world_pos.xz, 5.0, mixer) * 1.05;
    c = mix(c, rubble, spoil * 0.85);
    // Worked ground around buildings: trampled earth, paved between the houses of a later town.
    float yard = smoothstep(0.25, 0.85, use.g) * (0.55 + 0.45 * clump);
    vec3 earth = earthPhoto * mix(1.0, 0.9, broad);
    vec3 paving = photo(cobble_tex, world_pos.xz, 3.2, 0.0) * 0.95;
    vec3 yardColor = road_style > 0.5 ? mix(earth, paving, smoothstep(0.55, 0.9, use.g)) : earth;
    c = mix(c, yardColor, yard * 0.75);
    float road = smoothstep(0.34, 0.58, use.r);
    float rut = 0.92 + 0.08 * sin(world_pos.x * 2.1 + sin(world_pos.z * 1.7) * 1.4) * sin(world_pos.z * 2.3);
    vec3 roadColor = vec3(0.56, 0.45, 0.32) / 0.47 * earthPhoto * rut;
    if (road_style > 1.5) roadColor = photo(asphalt_tex, world_pos.xz, 6.0, mixer) * 0.85;
    else if (road_style > 0.5) roadColor = photo(cobble_tex, world_pos.xz, 2.6, 0.0);
    c = mix(c, roadColor, road * 0.94);
    // Street dressing by era: stone kerbs from the Renaissance; from the Industrial era paved sidewalks,
    // a gutter at the carriageway edge and round drain covers along the middle of the street.
    if (street_style > 0.5) {
        float r = use.r;
        vec3 stone = photo(rock_tex, world_pos.xz, 3.0, 0.0) * vec3(0.95, 0.93, 0.88);
        if (street_style > 1.5) {
            vec2 slab = fract(world_pos.xz / 1.4);
            float joint = 1.0 - smoothstep(0.0, 0.05, min(min(slab.x, 1.0 - slab.x), min(slab.y, 1.0 - slab.y)));
            vec3 pave = vec3(0.66, 0.64, 0.6) * mix(vec3(1.0), dirt, 0.25) * (1.0 - joint * 0.25);
            float walk = smoothstep(0.42, 0.47, r) * (1.0 - smoothstep(0.6, 0.63, r));
            c = mix(c, pave, walk);
            float kerb = smoothstep(0.6, 0.62, r) * (1.0 - smoothstep(0.65, 0.67, r));
            c = mix(c, stone * 0.92, kerb);
            float gutter = smoothstep(0.66, 0.67, r) * (1.0 - smoothstep(0.69, 0.71, r));
            c = mix(c, c * 0.55, gutter * 0.8);
            vec2 cell = floor(world_pos.xz / 13.0);
            vec2 spot = (cell + 0.5) * 13.0;
            float d = length(world_pos.xz - spot);
            float centre = texture(road_tex, (spot + vec2(480.0, 260.0)) / vec2(960.0, 520.0)).r;
            float cover = (1.0 - smoothstep(0.32, 0.36, d)) * step(0.95, centre) * step(0.95, r);
            float rim = smoothstep(0.24, 0.27, d);
            c = mix(c, mix(vec3(0.17, 0.17, 0.18), vec3(0.3, 0.29, 0.28), rim) * (0.9 + 0.1 * sin(world_pos.x * 40.0)), cover);
        } else {
            float kerb = smoothstep(0.43, 0.46, r) * (1.0 - smoothstep(0.5, 0.53, r));
            c = mix(c, stone, kerb * 0.9);
        }
    }
    // Wet ground darkens in rain; drifting cloud shadows dim the sunlit land.
    c = mix(c, c * vec3(0.7, 0.72, 0.76), rain * 0.65);
    float cloud = texture(noise_tex, world_pos.xz / 300.0 + TIME * vec2(0.0035, 0.002)).r;
    c *= 1.0 - smoothstep(0.78 - cloud_cover * 0.55, 0.9 - cloud_cover * 0.55, cloud) * 0.36 * daylight;
    float cover = snow * smoothstep(0.72, 0.92, up) * smoothstep(0.3, 1.2, world_pos.y) * (1.0 - max(wear * 0.55, road * 0.72));
    ALBEDO = mix(c, vec3(0.9, 0.92, 0.95), cover);
}";

    private const string ScatterShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled;
uniform vec3 tint = vec3(1.0);
void vertex() {
    vec3 w = MODEL_MATRIX[3].xyz;
    float bend = VERTEX.y * VERTEX.y * 1.6;
    float gust = sin(TIME * 1.7 + w.x * 0.21 + w.z * 0.13) + 0.5 * sin(TIME * 2.9 + w.z * 0.4);
    VERTEX.x += gust * 0.09 * bend;
    VERTEX.z += cos(TIME * 1.3 + w.x * 0.17) * 0.05 * bend;
}
void fragment() { ALBEDO = COLOR.rgb * tint; }";

    private const string SmokeShader = @"
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
void vertex() {
    float t = fract(TIME * 0.16 + INSTANCE_CUSTOM.x);
    VERTEX *= 0.18 + t * 0.78;
    VERTEX += vec3(sin(t * 5.0 + INSTANCE_CUSTOM.x * 40.0) * t * 0.7, t * 5.2, cos(t * 4.0 + INSTANCE_CUSTOM.x * 30.0) * t * 0.5);
    COLOR.a = smoothstep(0.0, 0.14, t) * (1.0 - t) * (1.0 - t) * 0.3;
}
uniform vec3 tone = vec3(0.86, 0.85, 0.82);
void fragment() { ALBEDO = tone; ALPHA = COLOR.a; }";

    private const string MovingShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
void vertex() {
    float t = TIME * INSTANCE_CUSTOM.x + INSTANCE_CUSTOM.y;
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

    /// <summary>The moving-part shader for realistic textured parts; transparent texels are cut out, for example between sail laths.</summary>
    private static readonly string MovingTexturedShader = MovingShader
        .Replace("render_mode diffuse_lambert, specular_disabled;", "render_mode diffuse_lambert, specular_disabled, cull_disabled;\nuniform sampler2D albedo : source_color, filter_linear_mipmap;")
        .Replace("void fragment() { ALBEDO = COLOR.rgb; }", "void fragment() {\n    vec4 t = texture(albedo, UV);\n    if (t.a < 0.5) discard;\n    ALBEDO = t.rgb * COLOR.rgb;\n}");

    private const string TreeShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform vec3 leaf = vec3(0.2, 0.42, 0.2);
void vertex() {
    vec3 w = MODEL_MATRIX[3].xyz;
    float sway = smoothstep(2.4, 7.0, VERTEX.y);
    VERTEX.x += sin(TIME * 1.1 + w.x * 0.13 + w.z * 0.09) * 0.11 * sway;
    VERTEX.z += cos(TIME * 0.9 + w.x * 0.07) * 0.07 * sway;
}
void fragment() {
    float near = (14.5 - length(VERTEX)) / 3.0;
    if (near > 0.0 && fract((FRAGCOORD.x + FRAGCOORD.y) * 0.5) < near) discard;
    float crown = smoothstep(0.7, 0.84, COLOR.g);
    ALBEDO = mix(COLOR.rgb, COLOR.rgb * leaf, crown);
}";

    /// <summary>Realistic trees: crossed cut-out cards from a generated tree image. Leaves take the season's colour.</summary>
    private const string TexturedTreeShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled;
uniform sampler2D albedo : source_color, filter_linear_mipmap;
uniform vec3 leaf = vec3(0.2, 0.42, 0.2);
void vertex() {
    vec3 w = MODEL_MATRIX[3].xyz;
    float sway = smoothstep(2.4, 7.0, VERTEX.y);
    VERTEX.x += sin(TIME * 1.1 + w.x * 0.13 + w.z * 0.09) * 0.11 * sway;
    VERTEX.z += cos(TIME * 0.9 + w.x * 0.07) * 0.07 * sway;
}
void fragment() {
    float near = (14.5 - length(VERTEX)) / 3.0;
    if (near > 0.0 && fract((FRAGCOORD.x + FRAGCOORD.y) * 0.5) < near) discard;
    vec4 t = texture(albedo, UV);
    if (t.a < 0.5) discard;
    // The level crown card (right half of the atlas) is hidden when seen edge-on.
    if (UV.x > 0.5 && abs(dot(NORMAL, VIEW)) < 0.35) discard;
    float crown = smoothstep(0.0, 0.08, t.g - t.r);
    vec3 season = pow(leaf / vec3(0.2, 0.42, 0.2), vec3(0.7));
    vec3 tinted = clamp(t.rgb * season, 0.0, 1.0);
    // The winter leaf colour is a pale grey; multiplied into green needles it turned them purple. In winter
    // the crown keeps its dark needles and takes snow on its lighter parts instead.
    float frost = smoothstep(0.35, 0.7, leaf.b);
    float light = dot(t.rgb, vec3(0.3, 0.59, 0.11));
    vec3 snowy = mix(t.rgb * vec3(0.8, 0.92, 0.88), vec3(0.86, 0.9, 0.93), 0.15 + 0.6 * smoothstep(0.12, 0.45, light));
    ALBEDO = mix(t.rgb, mix(tinted, snowy, frost), crown);
}";

    /// <summary>Fields: the vertex-coloured furrows take detail from a CC0 photo of a dry ploughed field.</summary>
    private const string FieldShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D soil_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 green_tint = vec3(1.0);
uniform float snow = 0.0;
varying vec3 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec3 photo = texture(soil_tex, world.xz / 6.0).rgb / textureLod(soil_tex, vec2(0.5), 12.0).rgb;
    vec3 base = COLOR.rgb * mix(vec3(dot(photo, vec3(0.333))), photo, 0.6);
    // Green parts (vines, dike grass) follow the season like the land: autumn colours, then bare and snowed over.
    float green = smoothstep(0.04, 0.14, COLOR.g - COLOR.r);
    base = mix(base, base * green_tint, green);
    ALBEDO = mix(base, vec3(0.86, 0.9, 0.93), snow * 0.8 * green);
}";

    /// <summary>
    /// Realistic buildings share one model per era, so each house repaints its parts: roof (upward faces high up)
    /// and walls (plaster gets a stronger colour than stone, brick or timber), chosen from a hash of its position.
    /// </summary>
    private const string BuildingShader = @"
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;
uniform sampler2D albedo : source_color, filter_linear_mipmap;
uniform float model_height = 7.0;
uniform float variety = 1.0;
varying float roof;
varying vec3 pick;
void vertex() {
    vec2 w = MODEL_MATRIX[3].xz;
    pick = fract(sin(vec3(dot(w, vec2(12.9898, 78.233)), dot(w, vec2(39.35, 11.13)), dot(w, vec2(73.1, 5.7)))) * 43758.5453);
    roof = smoothstep(0.3, 0.55, NORMAL.y) * smoothstep(0.3, 0.5, VERTEX.y / model_height);
}
void fragment() {
    const vec3 roofs[5] = vec3[5](vec3(1.0), vec3(0.78, 0.75, 0.72), vec3(1.12, 0.93, 0.8), vec3(0.86, 0.9, 0.98), vec3(1.05, 1.0, 0.86));
    const vec3 walls[6] = vec3[6](vec3(1.0), vec3(1.1, 0.96, 0.74), vec3(1.08, 0.9, 0.86), vec3(0.9, 0.96, 1.04), vec3(0.96, 1.02, 0.86), vec3(1.04, 1.0, 0.95));
    vec3 t = texture(albedo, UV).rgb;
    float l = dot(t, vec3(0.3, 0.59, 0.11));
    float chroma = max(t.r, max(t.g, t.b)) - min(t.r, min(t.g, t.b));
    float plaster = smoothstep(0.35, 0.6, l) * (1.0 - smoothstep(0.1, 0.25, chroma));
    vec3 roofTint = roofs[int(pick.x * 4.99)];
    vec3 wallTint = walls[int(pick.y * 5.99)];
    vec3 c = mix(t, t * roofTint, roof * variety);
    c = mix(c, c * wallTint, (1.0 - roof) * mix(0.35, 1.0, plaster) * variety);
    c *= 0.93 + 0.14 * pick.z * variety;
    ALBEDO = clamp(c, 0.0, 1.0) * COLOR.rgb;
}";

    private const string WaterShader = @"
shader_type spatial;
render_mode diffuse_lambert;
uniform sampler2D ground_tex : filter_linear, repeat_disable;
uniform vec3 deep = vec3(0.14, 0.36, 0.5);
uniform vec3 shallow = vec3(0.34, 0.62, 0.62);
uniform float surge = 0.0;
// Winter ice along the shallow river banks (not the open sea): 0 none, 1 at the depth of a harsh winter.
uniform float ice = 0.0;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
global uniform float cloud_cover;
global uniform float rain;
global uniform float daylight;
varying vec3 world;
void vertex() {
    world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float sea = smoothstep(180.0, 340.0, world.x);
    VERTEX.y += (sin(world.x * 0.05 + TIME * 0.9) + sin(world.z * 0.07 + world.x * 0.02 + TIME * 1.25)) * (0.03 + 0.2 * sea);
    VERTEX.y += sin(TIME * 0.05) * 0.1 + surge * 1.05;
}
void fragment() {
    vec2 uv = (world.xz + vec2(480.0, 260.0)) / vec2(960.0, 520.0);
    float ground = texture(ground_tex, uv).r * 12.0 - 6.0;
    float depth = clamp(-ground / 4.2, 0.0, 1.0);
    float ripple = sin(world.x * 0.42 + TIME * 0.8 + sin(world.z * 0.31) * 1.6) * sin(world.z * 0.53 - TIME * 0.6 + sin(world.x * 0.23));
    float foam = smoothstep(0.2, 0.03, depth) * (0.45 + 0.55 * sin(depth * 34.0 - TIME * 1.3 + ripple * 1.4));
    vec3 c = mix(shallow, deep, smoothstep(0.05, 0.9, depth));
    c += ripple * 0.022;
    c = mix(c, vec3(0.92, 0.95, 0.92), clamp(foam, 0.0, 1.0) * 0.72);
    float rim = ice * smoothstep(0.9 * ice + 0.05, 0.35 * ice, depth) * (1.0 - smoothstep(140.0, 220.0, world.x));
    rim = clamp(rim * 1.8 - (texture(noise_tex, world.xz / 9.0).r - 0.5) * 0.6, 0.0, 1.0);
    float crack = texture(noise_tex, world.xz / 22.0 + vec2(0.37, 0.11)).r;
    c = mix(c, mix(vec3(0.7, 0.78, 0.83), vec3(0.9, 0.93, 0.95), crack), rim);
    float cloud = texture(noise_tex, world.xz / 300.0 + TIME * vec2(0.0035, 0.002)).r;
    c *= 1.0 - smoothstep(0.78 - cloud_cover * 0.55, 0.9 - cloud_cover * 0.55, cloud) * 0.3 * daylight;
    c = mix(c, c * vec3(0.78, 0.82, 0.86), rain * 0.5);
    ALBEDO = c;
    ROUGHNESS = mix(0.34 + rain * 0.3, 0.55, rim);
    SPECULAR = 0.36;
    NORMAL_MAP = normalize(vec3(0.5 + ripple * 0.07 * (1.0 - rim), 0.5 - ripple * 0.05 * (1.0 - rim), 1.0));
}";

    private const float CharacterScale = 1.12f, NearRadius = 700f;
    private const int HeroCapacity = 320, HeroFloats = 20, MaxFalling = 24, MaxPuffs = 260, LaborerVariant = 0, WomanVariant = 1, WoodcutterVariant = 2, FarmerVariant = 3, ElderVariant = 9;

    private readonly MultiMesh[] _heroes = new MultiMesh[CharacterKit.Characters.Length];
    private readonly MultiMesh[] _tools = new MultiMesh[CharacterKit.Tools.Length];
    private readonly float[][] _heroBuffers = new float[CharacterKit.Characters.Length][];
    private readonly float[][] _toolBuffers = new float[CharacterKit.Tools.Length][];
    private readonly int[] _heroCounts = new int[CharacterKit.Characters.Length];
    private readonly int[] _toolCounts = new int[CharacterKit.Tools.Length];
    private readonly System.Collections.Generic.List<(float X, float Z, float Age, float Scale, float Yaw)> _fallingTrees = new();
    private readonly MultiMesh[] _scatter = new MultiMesh[4];
    private readonly MultiMeshInstance3D[] _scatterNodes = new MultiMeshInstance3D[4];
    private readonly Transform3D[][] _scatterItems = new Transform3D[4][];
    private readonly MultiMesh[] _treeKinds = new MultiMesh[3];
    private Wildlife _wildlife;
    private ShaderMaterial _treeMaterial, _waterMaterial;
    private readonly System.Collections.Generic.List<ShaderMaterial> _realTreeMaterials = new();
    private int _treeNodeIndex;
    private readonly MultiMeshInstance3D[] _treeNodes = new MultiMeshInstance3D[3];
    private readonly MultiMeshInstance3D[] _movingNodes = new MultiMeshInstance3D[(int)BType.Count];
    private MultiMeshInstance3D _hedgeNode;
    private System.IO.FileSystemWatcher _modelWatcher;
    private volatile bool _modelsChanged;
    private double _modelsChangedAt;
    private ShaderMaterial _movingMaterial;
    private Shader _movingTexturedShader;
    private int _fallingNodeIndex;
    private static readonly string[] RealTrees = { "tree_pine", "tree_broad", "tree_birch" };

    /// <summary>Creates or refreshes a building's moving part (sails, wheel, sluice door...).</summary>
    private void ApplyMovingPart(int i)
    {
        _movingParts[i] = MeshKit.Moving((BType)i);
        if (_movingParts[i] is not MeshKit.MovingPart part) return;
        Material material = _movingMaterial ??= new ShaderMaterial { Shader = new Shader { Code = MovingShader } };
        if (part.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D textured && textured.AlbedoTexture != null)
        {
            var real = new ShaderMaterial { Shader = _movingTexturedShader ??= new Shader { Code = MovingTexturedShader } };
            real.SetShaderParameter("albedo", textured.AlbedoTexture);
            material = real;
        }
        if (_moving[i] == null)
        {
            _moving[i] = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = part.Mesh, InstanceCount = 32, VisibleInstanceCount = 0 };
            _movingNodes[i] = new MultiMeshInstance3D
            {
                Multimesh = _moving[i], CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
            };
            AddChild(_movingNodes[i]);
        }
        _moving[i].Mesh = part.Mesh;
        _movingNodes[i].MaterialOverride = material;
    }

    /// <summary>Uses the generated tree cards when they exist, else the kit tree.</summary>
    private void ApplyTree(int i)
    {
        Mesh mesh = i switch { 0 => MeshKit.Tree(), 1 => MeshKit.Broadleaf(), _ => MeshKit.Birch() };
        Material material = _treeMaterial;
        if (MeshKit.LoadModel(RealTrees[i]) is ArrayMesh real)
        {
            var textured = new ShaderMaterial { Shader = new Shader { Code = TexturedTreeShader } };
            textured.SetShaderParameter("albedo", MeshKit.ModelTexture(RealTrees[i], out _));
            _realTreeMaterials.Add(textured);
            mesh = real;
            material = textured;
        }
        _treeKinds[i].Mesh = mesh;
        _treeNodes[i].MaterialOverride = material;
        if (i == 1)
        {
            _hedges.Mesh = mesh;
            _hedgeNode.MaterialOverride = material;
        }
        if (i == 0 && _falling != null)
        {
            _falling.Mesh = mesh;
            ((MultiMeshInstance3D)GetChild(_fallingNodeIndex)).MaterialOverride = material;
        }
    }

    /// <summary>
    /// Watches assets/models so a model built while the wallpaper runs appears without a restart.
    /// Files are re-read 2 s after the last change, when the writer has finished.
    /// </summary>
    private void WatchModels()
    {
        string folder = ProjectSettings.GlobalizePath("res://assets/models");
        if (!System.IO.Directory.Exists(folder)) return;
        _modelWatcher = new System.IO.FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size,
        };
        _modelWatcher.Changed += OnModelFile;
        _modelWatcher.Created += OnModelFile;
        _modelWatcher.Renamed += OnModelFile;
        _modelWatcher.EnableRaisingEvents = true;
    }

    private void OnModelFile(object sender, System.IO.FileSystemEventArgs e)
    {
        _modelsChangedAt = Time.GetTicksMsec() / 1000.0;
        _modelsChanged = true;
    }

    /// <summary>Re-reads every generated model and swaps it in place. Returns true when a reload happened.</summary>
    public bool ReloadModelsIfChanged()
    {
        if (!_modelsChanged || Time.GetTicksMsec() / 1000.0 - _modelsChangedAt < 2.0) return false;
        _modelsChanged = false;
        MeshKit.ForgetModels();
        _buildingMaterials.Clear();
        _realTreeMaterials.Clear();
        for (int i = 0; i < _moving.Length; i++) ApplyMovingPart(i);
        for (int i = 0; i < _treeKinds.Length; i++) ApplyTree(i);
        _wildlife?.ReloadModels();
        if (_shown != null)
        {
            _eraTier = int.MinValue;
            UpdateEraLook(_shown.Era);
            UpdateSeason(_shown);
            UpdateBuildings(_shown);
        }
        GD.Print("DeskCiv: generated models reloaded");
        return true;
    }
    private int _eraTier = int.MinValue, _clothEra = -1, _detail, _polderVersion = -1, _roadEra = -1;
    private const int GroundWidth = 384, GroundDepth = 208;
    private byte[] _polderBytes, _groundBytes;
    private ImageTexture _polderTexture, _groundTexture;
    private float _surge;
    private ShaderMaterial _scatterMaterial;
    private int _scatterBuildings = -1;
    private readonly MultiMesh[] _loads = new MultiMesh[(int)Res.Count];
    private readonly float[][] _loadBuffers = new float[(int)Res.Count][];
    private readonly int[] _loadCounts = new int[(int)Res.Count];
    private ImageTexture _roadTexture;
    private byte[] _roadBytes;
    private int _roadVersion = -1;
    private CharacterKit _kit;
    private readonly MultiMesh[] _moving = new MultiMesh[(int)BType.Count];
    private readonly MeshKit.MovingPart?[] _movingParts = new MeshKit.MovingPart?[(int)BType.Count];
    private readonly TexturedRole[] _texturedRoles = new TexturedRole[CharacterKit.Characters.Length];

    /// <summary>GPU instances for one role drawn with a pipeline-made textured character.</summary>
    private sealed class TexturedRole
    {
        public CharacterKit.Textured Kit;
        public MultiMesh Body, Tool;
        public readonly float[] BodyBuffer = new float[HeroCapacity * HeroFloats], ToolBuffer = new float[HeroCapacity * HeroFloats];
        public int Bodies, Tools;
    }
    private MultiMesh _falling, _smoke;
    private bool[] _knownTrees;
    private Vector3 _viewCenter;

    private readonly SimHost _host;
    private readonly WorldMap _map;
    private readonly MultiMesh[] _buildings = new MultiMesh[(int)BType.Count];
    private readonly MultiMeshInstance3D[] _buildingNodes = new MultiMeshInstance3D[(int)BType.Count];
    private readonly MultiMesh[] _variants = new MultiMesh[(int)BType.Count];
    private readonly MultiMeshInstance3D[] _variantNodes = new MultiMeshInstance3D[(int)BType.Count];
    private readonly bool[] _variantShown = new bool[(int)BType.Count];
    // Buildings not yet rebuilt in the new era's style keep the previous era's model for a while.
    private readonly MultiMesh[] _older = new MultiMesh[(int)BType.Count];
    private readonly MultiMeshInstance3D[] _olderNodes = new MultiMeshInstance3D[(int)BType.Count];
    private float _renovationStart = float.NegativeInfinity;
    private const float RenovationYears = 12f;
    private StandardMaterial3D _vertexColors;
    private ShaderMaterial _fieldMaterial;
    private NoiseTexture2D _noiseTexture;
    private MultiMesh _crops, _people, _cargo, _graves, _sites, _glow, _stumps, _hedges;
    private readonly MultiMesh[] _piles = new MultiMesh[4];
    private MultiMesh _rubble, _churches, _quays, _trenches, _factories, _stations;
    private Vector3? _factoryAt;
    private Vector3 _factoryChimney;
    private float _factoryYaw;
    private Vector3 _churchShown;
    private MultiMesh _barrows, _lampPosts, _lanterns, _lampPools, _wells, _fountains, _towers, _boilers;
    private Vector3? _boilerAt;
    private Vector3 _chimneyTop = new(2.5f, 24.3f, -1.5f);
    private ShaderMaterial _puffMaterial;
    private StandardMaterial3D _lanternMaterial;
    private int _lampPoolNode;
    private readonly float[] _barrowBuffer = new float[HeroCapacity * 16];
    private ShaderMaterial _terrain;
    private StandardMaterial3D _glowMaterial;
    /// <summary>Flames over a burning house, flickered every frame.</summary>
    private MultiMesh _flames;
    /// <summary>Feasts and era celebrations: a bonfire at the gathering point and, for a new era, fireworks above it.</summary>
    private MultiMesh _bonfire, _sparks;
    private Vector3? _festiveAt;
    private bool _fireworks;
    private float _festClock;
    private const int Rockets = 5, SparksPerRocket = 28;
    /// <summary>Railway track along the sim's rail row, and the train that runs on it (view only, on its own clock).</summary>
    private MultiMesh _rails, _train, _portal, _stalls, _cargo2;
    private float _trainClock, _trainStopX, _trainZ, _tunnelX;
    private bool _hasRail;
    private Vector3? _fireAt;
    private float _fireClock;
    private Node3D _glowNode;
    private ImageTexture _wearTexture;
    private byte[] _wearBytes;
    private float[] _peopleBuffer = Array.Empty<float>(), _cargoBuffer = Array.Empty<float>();
    private int _treeVersion = -1, _graveCount = -1, _graveBuildings = -1;
    private (float X, float Z) _lastGrave;
    private WorldSnapshot _shown;

    private static readonly Color[] Clothes = { new(0.56f, 0.34f, 0.22f), new(0.72f, 0.62f, 0.42f), new(0.42f, 0.46f, 0.3f), new(0.6f, 0.28f, 0.26f) };
    private static readonly Color[] CargoColors =
    {
        new(0.58f, 0.42f, 0.25f), new(0.6f, 0.6f, 0.57f), new(0.62f, 0.18f, 0.32f), new(0.52f, 0.66f, 0.74f),
        new(0.87f, 0.73f, 0.32f), new(0.93f, 0.9f, 0.8f), new(0.74f, 0.5f, 0.25f),
        new(0.4f, 0.18f, 0.48f), new(0.66f, 0.38f, 0.24f), new(0.58f, 0.6f, 0.63f),
    };

    public WorldView() { }

    public WorldView(SimHost host)
    {
        _host = host;
        _map = host.Map;
    }

    public override void _Ready()
    {
        BuildTerrain();
        BuildWater();
        var vertexColors = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };
        _vertexColors = vertexColors;
        for (int i = 0; i < _buildings.Length; i++)
        {
            _buildings[i] = AddInstances(MeshKit.Building((BType)i), vertexColors, true, 32, true);
            _buildingNodes[i] = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
            _variants[i] = AddInstances(MeshKit.Building((BType)i), vertexColors, true, 32, true);
            _variantNodes[i] = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
            _older[i] = AddInstances(MeshKit.Building((BType)i), vertexColors, true, 32, true);
            _olderNodes[i] = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
            _older[i].VisibleInstanceCount = 0;
        }
        for (int i = 0; i < _moving.Length; i++) ApplyMovingPart(i);
        _crops = AddInstances(MeshKit.Crops(), vertexColors, true, 32);
        _graves = AddInstances(MeshKit.Grave(), vertexColors, false, 32);
        _sites = AddInstances(MeshKit.Site(), vertexColors, false, 8, true);
        _rubble = AddInstances(MeshKit.Rubble(), vertexColors, false, 4, true);
        _trenches = AddInstances(MeshKit.Trench(), vertexColors, false, 8, true);
        if (MeshKit.Factory() is ArrayMesh factory)
        {
            _factories = AddInstances(factory, factory.SurfaceGetMaterial(0) ?? vertexColors, false, 1, true);
            _factoryChimney = HighestPoint(factory);
        }
        if (MeshKit.Station() is ArrayMesh station) _stations = AddInstances(station, station.SurfaceGetMaterial(0) ?? vertexColors, false, 1, true);
        Mesh church = MeshKit.Church();
        _churches = AddInstances(church, church.SurfaceGetMaterial(0) ?? vertexColors, false, 1, true);
        Mesh quay = MeshKit.Quay();
        _quays = AddInstances(quay, quay.SurfaceGetMaterial(0) ?? vertexColors, false, 1, true);
        for (int k = 0; k < _piles.Length; k++) _piles[k] = AddInstances(MeshKit.GoodsPile(k), vertexColors, false, 16, true);
        _stumps = AddInstances(MeshKit.Stump(), vertexColors, false, Math.Max(1, _host.TreePositions.X.Length));
        _treeMaterial = new ShaderMaterial { Shader = new Shader { Code = TreeShader } };
        BuildScatter(vertexColors);
        for (int i = 0; i < _treeKinds.Length; i++)
        {
            _treeKinds[i] = AddInstances(MeshKit.Tree(), _treeMaterial, false, Math.Max(1, _host.TreePositions.X.Length), true);
            _treeNodes[i] = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
            if (i == 0) _treeNodeIndex = GetChildCount() - 1;
            if (i == 1)
            {
                _hedges = AddInstances(MeshKit.Broadleaf(), _treeMaterial, false, 512); // small oaks as hedgerows
                _hedgeNode = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
            }
            ApplyTree(i);
        }
        _people = AddInstances(MeshKit.Person(), vertexColors, true, 128);
        _cargo = AddInstances(MeshKit.Cargo(), vertexColors, true, 128);
        _glowMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, AlbedoColor = new Color(1f, 0.68f, 0.28f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _glow = AddInstances(MeshKit.Glow(), _glowMaterial, false, 32);
        _glowNode = (Node3D)GetChild(GetChildCount() - 1);
        var flameMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _flames = AddInstances(MeshKit.Flame(), flameMaterial, false, 5);
        _bonfire = AddInstances(MeshKit.Flame(), flameMaterial, false, 3);
        var sparkMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BlendMode = BaseMaterial3D.BlendModeEnum.Add, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _sparks = AddInstances(MeshKit.Spark(), sparkMaterial, true, Rockets * SparksPerRocket);
        _rails = AddInstances(MeshKit.RailTrack(), vertexColors, false, 64, true);
        _train = AddInstances(MeshKit.Train(), vertexColors, false, 1, true);
        _portal = AddInstances(MeshKit.TunnelPortal(), vertexColors, false, 1, true);
        _stalls = AddInstances(MeshKit.MarketStall(), vertexColors, false, 8, true);
        _cargo2 = AddInstances(MeshKit.GoodsPile(3), vertexColors, false, 4, true);
        // Felled trees use the pine model and material, realistic or kit.
        _falling = AddInstances(_treeKinds[0].Mesh, ((MultiMeshInstance3D)GetChild(_treeNodeIndex)).MaterialOverride, false, MaxFalling, true);
        _fallingNodeIndex = GetChildCount() - 1;
        WatchModels();

        _kit = new CharacterKit();
        for (int i = 0; i < _heroes.Length; i++)
        {
            _heroes[i] = AddSkinned(CharacterKit.LoadMesh(CharacterKit.Characters[i]));
            _heroBuffers[i] = new float[HeroCapacity * HeroFloats];
        }
        for (int i = 0; i < _tools.Length; i++)
        {
            _tools[i] = AddSkinned(CharacterKit.LoadMesh("tool_" + CharacterKit.Tools[i]));
            _toolBuffers[i] = new float[HeroCapacity * HeroFloats];
        }
        AddTexturedRole(WoodcutterVariant, TryTextured("hq_woodcutter", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Chop));
        AddTexturedRole(FarmerVariant, TryTextured("hq_farmer", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Sow, Clip.Reap));
        AddTexturedRole(LaborerVariant, TryTextured("hq_laborer", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Reap, Clip.Knead));
        AddTexturedRole(WomanVariant, TryTextured("hq_woman", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Sow, Clip.Reap, Clip.Knead));
        AddTexturedRole(4, TryTextured("hq_fisher", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Fish));
        AddTexturedRole(5, TryTextured("hq_miner", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Mine));
        AddTexturedRole(6, TryTextured("hq_builder", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Build));
        AddTexturedRole(7, TryTextured("hq_baker", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Knead));
        AddTexturedRole(ElderVariant, TryTextured("hq_elder", Clip.Idle, Clip.Walk, Clip.Carry, Clip.Fish));

        _wildlife = new Wildlife(_map, _host.Snapshot.CenterX, _host.Snapshot.CenterZ);
        AddChild(_wildlife);

        for (int i = 0; i < _loads.Length; i++)
        {
            _loads[i] = AddInstances(MeshKit.Load((Res)i), vertexColors, true, HeroCapacity);
            _loadBuffers[i] = new float[HeroCapacity * 16];
        }
        _barrows = AddInstances(MeshKit.Barrow(), vertexColors, true, HeroCapacity, true);
        _lampPosts = AddInstances(MeshKit.LampPost(), vertexColors, false, 64, true);
        _lanternMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.35f, 0.33f, 0.28f) };
        _lanterns = AddInstances(MeshKit.Lantern(), _lanternMaterial, false, 64);
        _lampPools = AddInstances(MeshKit.Glow(), _glowMaterial, false, 64);
        _lampPoolNode = GetChildCount() - 1;
        // Image-made prop models carry their own textured material; the procedural fallbacks use vertex colours.
        Mesh well = MeshKit.Well(), fountain = MeshKit.Fountain(), tower = MeshKit.WaterTower();
        _wells = AddInstances(well, well.SurfaceGetMaterial(0) ?? vertexColors, false, 16, true);
        _fountains = AddInstances(fountain, fountain.SurfaceGetMaterial(0) ?? vertexColors, false, 16, true);
        _towers = AddInstances(tower, tower.SurfaceGetMaterial(0) ?? vertexColors, false, 2, true);
        Mesh boiler = MeshKit.BoilerHouse();
        _boilers = AddInstances(boiler, boiler.SurfaceGetMaterial(0) ?? vertexColors, false, 2, true);
        _chimneyTop = HighestPoint(boiler);

        var puffMaterial = _puffMaterial = new ShaderMaterial { Shader = new Shader { Code = SmokeShader } };
        _smoke = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = MeshKit.Puff(), InstanceCount = MaxPuffs, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _smoke,
            MaterialOverride = puffMaterial,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 140, WorldMap.SizeZ)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>A pipeline-made character, or null when its files are absent (the public repository leaves them out).</summary>
    private static CharacterKit.Textured TryTextured(string name, params Clip[] order) =>
        FileAccess.FileExists($"res://assets/characters/{name}.rig") ? new CharacterKit.Textured(name, order) : null;

    private void AddTexturedRole(int variant, CharacterKit.Textured kit)
    {
        if (kit == null) return; // falls back to the baked low-poly villager
        _texturedRoles[variant] = new TexturedRole
        {
            Kit = kit,
            Body = AddSkinned(kit.Body, kit.BodyMaterial),
            Tool = kit.Tool == null ? null : AddSkinned(kit.Tool, kit.ToolMaterial),
        };
    }

    private MultiMesh AddSkinned(Mesh mesh, Material material = null)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = HeroCapacity, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = material ?? _kit.Material,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
        });
        return multi;
    }

    public void SetViewCenter(Vector3 center) => _viewCenter = center;

    public override void _ExitTree() => _modelWatcher?.Dispose();

    private void BuildScatter(Material unused)
    {
        _scatterMaterial = new ShaderMaterial { Shader = new Shader { Code = ScatterShader } };
        WorldSnapshot s = _host.Snapshot;
        Mesh[] meshes = { MeshKit.GrassTuft(), MeshKit.Flowers(), MeshKit.Pebbles(), MeshKit.Reeds() };
        int[] counts = { 15000, 2200, 900, 2400 };
        var rng = new Rng(unchecked((ulong)(uint)_map.Seed * 73 + 19));
        for (int kind = 0; kind < meshes.Length; kind++)
        {
            var items = new System.Collections.Generic.List<Transform3D>(counts[kind]);
            for (int i = 0; i < counts[kind] * 3 && items.Count < counts[kind]; i++)
            {
                float angle = rng.R(0, MathF.Tau), radius = MathF.Sqrt(rng.F()) * 235f;
                float x = s.CenterX + MathF.Cos(angle) * radius, z = s.CenterZ + MathF.Sin(angle) * radius * 0.8f;
                if (!_map.Walkable(x, z)) continue;
                float h = _map.Height(x, z);
                if (kind == 3 ? h > 1.25f : h < 1.25f || _map.Slope(x, z) > 0.42f) continue;
                float size = rng.R(0.75f, 1.45f);
                items.Add(new Transform3D(new Basis(Vector3.Up, rng.R(0, MathF.Tau)).Scaled(Vector3.One * size), new Vector3(x, h - 0.02f, z)));
            }
            _scatterItems[kind] = items.ToArray();
            _scatter[kind] = AddInstances(meshes[kind], _scatterMaterial, false, Math.Max(1, items.Count));
            _scatterNodes[kind] = (MultiMeshInstance3D)GetChild(GetChildCount() - 1);
        }
        UpdateScatter(s);
    }

    private void UpdateScatter(WorldSnapshot s)
    {
        _scatterBuildings = s.Buildings.Length;
        for (int kind = 0; kind < _scatter.Length; kind++)
        {
            int visible = 0;
            foreach (Transform3D item in _scatterItems[kind])
            {
                int tx = Math.Clamp((int)((item.Origin.X + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1);
                int tz = Math.Clamp((int)((item.Origin.Z + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1);
                bool covered = s.Tiles.Length > 0 && s.Tiles[tx + tz * Rules.TileNx] is TileKind.Building or TileKind.Field or TileKind.Road or TileKind.PlannedRoad;
                if (!covered) _scatter[kind].SetInstanceTransform(visible++, item);
            }
            _scatter[kind].VisibleInstanceCount = visible;
        }
    }

    /// <summary>On market day, up to eight stalls stand in rings round the church door, on open ground away from graves, facing inwards.</summary>
    private void UpdateMarket(WorldSnapshot s)
    {
        int count = 0;
        if (s.MarketOpen)
        {
            for (int k = 0; k < 24 && count < 8; k++)
            {
                // The inner ring first, then an outer one; never among the churchyard graves.
                float a = k * MathF.Tau / 12f + (k >= 12 ? 0.26f : 0f), ring = k < 12 ? 7.5f : 11f;
                float x = s.MarketX + MathF.Sin(a) * ring, z = s.MarketZ + MathF.Cos(a) * ring;
                bool grave = false;
                foreach ((float gx, float gz) in s.Graves)
                    if ((gx - x) * (gx - x) + (gz - z) * (gz - z) < 9f) { grave = true; break; }
                if (grave) continue;
                int tx = Math.Clamp((int)((x + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1);
                int tz = Math.Clamp((int)((z + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1);
                if (s.Tiles[tx + tz * Rules.TileNx] is TileKind.Building or TileKind.Rail || !_map.Walkable(x, z) || InsideChurch(x, z)) continue;
                _stalls.SetInstanceTransform(count++, new Transform3D(new Basis(Vector3.Up, a + MathF.PI), new Vector3(x, _map.Height(x, z), z)));
            }
        }
        _stalls.VisibleInstanceCount = count;
    }

    /// <summary>Lays track instances along the sim's rail row (built-over tiles are skipped) and finds where the train stops.</summary>
    private void UpdateRailway(WorldSnapshot s)
    {
        int count = 0;
        _hasRail = s.RailRow >= 0;
        if (_hasRail)
        {
            Ensure(_rails, s.RailX1 - s.RailX0 + 1);
            for (int x = s.RailX0; x <= s.RailX1; x++)
            {
                int tile = x + s.RailRow * Rules.TileNx;
                if (s.Tiles[tile] == TileKind.Building) continue;
                (float wx, float wz) = Simulation.TileCenter(tile);
                _rails.SetInstanceTransform(count++, new Transform3D(Basis.Identity, new Vector3(wx, _map.Height(wx, wz) + 0.03f, wz)));
            }
            (float sx, float sz) = Simulation.TileCenter(s.RailX1 - 4 + s.RailRow * Rules.TileNx);
            (_trainStopX, _trainZ) = (sx + 6f, sz);
            // Follow the sim's timetable; between snapshots the clock runs on by itself.
            float simClock = s.TrainTick * Rules.TickSeconds;
            if (Math.Abs(simClock - _trainClock) > 2f && Math.Abs(simClock - _trainClock) < 238f) _trainClock = simClock;
            (float tx, float tz) = Simulation.TileCenter(s.RailX0 + s.RailRow * Rules.TileNx);
            _tunnelX = tx - 2f;
            _portal.SetInstanceTransform(0, new Transform3D(Basis.Identity, new Vector3(_tunnelX, _map.Height(tx, tz) - 0.2f, tz)));
        }
        _rails.VisibleInstanceCount = count;
        _portal.VisibleInstanceCount = _hasRail ? 1 : 0;
    }

    /// <summary>
    /// The train follows the sim's four-minute timetable: it steams out of the tunnel in the western hills, stands at the station for a
    /// minute, backs out and is gone for two minutes. View only.
    /// </summary>
    private void AnimateTrain(float delta)
    {
        if (!_hasRail)
        {
            _train.VisibleInstanceCount = 0;
            return;
        }
        _trainClock = (_trainClock + delta) % 240f;
        // The train comes out of the tunnel; its front starts just inside the portal.
        float west = _tunnelX - 2f, x;
        if (_trainClock < 40f) x = Mathf.Lerp(west, _trainStopX, Mathf.SmoothStep(0f, 1f, _trainClock / 40f));
        else if (_trainClock < 100f) x = _trainStopX;
        else if (_trainClock < 140f) x = Mathf.Lerp(_trainStopX, west, Mathf.SmoothStep(0f, 1f, (_trainClock - 100f) / 40f));
        else
        {
            _train.VisibleInstanceCount = 0;
            return;
        }
        float ground = Math.Max(_map.Height(x, _trainZ), _map.Height(x - 20f, _trainZ)) + 0.25f;
        _train.SetInstanceTransform(0, new Transform3D(Basis.Identity, new Vector3(x, ground, _trainZ)));
        _train.VisibleInstanceCount = 1;
    }

    /// <summary>
    /// A bonfire burns at the gathering point of a feast or era celebration. For a new era (from the Middle Ages)
    /// rockets go up in turn and burst into coloured stars that fall and fade. View only, on its own clock.
    /// </summary>
    private void AnimateFestivities(float delta)
    {
        if (_festiveAt is not Vector3 at)
        {
            _bonfire.VisibleInstanceCount = 0;
            _sparks.VisibleInstanceCount = 0;
            return;
        }
        _festClock += delta;
        for (int k = 0; k < 3; k++)
        {
            float flicker = 0.7f + 0.3f * MathF.Sin(_festClock * (6f + k * 2.3f) + k);
            var basis = new Basis(Vector3.Up, k * 1.1f + _festClock * 0.5f).Scaled(new Vector3(0.55f, 0.6f * flicker, 0.55f));
            _bonfire.SetInstanceTransform(k, new Transform3D(basis, at + new Vector3((k - 1) * 0.6f, 0.1f, (k % 2) * 0.5f + 4f)));
        }
        _bonfire.VisibleInstanceCount = 3;
        if (!_fireworks)
        {
            _sparks.VisibleInstanceCount = 0;
            return;
        }
        Color[] palette = { new(1f, 0.35f, 0.25f), new(0.4f, 0.8f, 1f), new(1f, 0.85f, 0.3f), new(0.6f, 1f, 0.45f), new(1f, 0.45f, 0.9f) };
        int n = 0;
        for (int r = 0; r < Rockets; r++)
        {
            float cycle = 3.6f, t = _festClock + r * 0.72f, p = t % cycle;
            int round = (int)(t / cycle) * 7 + r * 13;
            // Each round bursts at a slightly different spot and height above the crowd.
            var burst = at + new Vector3(MathF.Sin(round * 1.7f) * 12f, 30f + MathF.Sin(round * 2.3f) * 6f, MathF.Cos(round * 1.3f) * 12f);
            Color color = palette[(round / 7 + r) % palette.Length];
            if (p < 0.9f)
            {
                // The rocket climbs: one bright spark with a short trail.
                for (int s = 0; s < 3; s++)
                {
                    float rise = Math.Max(0f, p - s * 0.05f) / 0.9f;
                    var pos = at.Lerp(burst, rise);
                    _sparks.SetInstanceTransform(n, new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.6f - s * 0.15f)), pos));
                    _sparks.SetInstanceColor(n++, new Color(1f, 0.85f, 0.6f));
                }
            }
            else if (p < 2.8f)
            {
                float age = p - 0.9f, fade = 1f - age / 1.9f;
                for (int s = 0; s < SparksPerRocket; s++)
                {
                    // Directions spread over a sphere (golden-angle spiral), slowing and dropping as they fall.
                    float y = 1f - 2f * (s + 0.5f) / SparksPerRocket, ring = MathF.Sqrt(1f - y * y), a = s * 2.39996f;
                    var dir = new Vector3(MathF.Cos(a) * ring, y, MathF.Sin(a) * ring);
                    float reach = 9f * (1f - MathF.Exp(-age * 2.2f));
                    var pos = burst + dir * reach + Vector3.Down * (2.2f * age * age);
                    _sparks.SetInstanceTransform(n, new Transform3D(Basis.Identity.Scaled(Vector3.One * (0.9f * fade + 0.15f)), pos));
                    _sparks.SetInstanceColor(n++, color * fade);
                }
            }
        }
        _sparks.VisibleInstanceCount = n;
    }

    /// <summary>Flickers the flames on a burning house: each tongue grows and shrinks on its own rhythm.</summary>
    private void AnimateFire(float delta)
    {
        if (_fireAt is not Vector3 fire)
        {
            _flames.VisibleInstanceCount = 0;
            return;
        }
        _fireClock += delta;
        Ensure(_flames, 5);
        for (int k = 0; k < 5; k++)
        {
            float flicker = 0.75f + 0.25f * MathF.Sin(_fireClock * (5.3f + k * 1.7f) + k * 2.1f) + 0.1f * MathF.Sin(_fireClock * 13f + k);
            var offset = new Vector3((k % 3 - 1) * 2.4f, (k % 2) * 1.4f, (k / 3 - 0.5f) * 2.6f);
            var basis = new Basis(Vector3.Up, k * 0.7f + _fireClock * 0.4f).Scaled(new Vector3(1.7f, 1.8f * flicker, 1.7f));
            _flames.SetInstanceTransform(k, new Transform3D(basis, fire + offset));
        }
        _flames.VisibleInstanceCount = 5;
    }

    /// <summary>Animates felled trees between simulation snapshots.</summary>
    public void UpdateFallingTrees(float delta)
    {
        _wildlife?.Step(delta, _shown?.Tiles);
        AnimateFire(delta);
        AnimateFestivities(delta);
        AnimateTrain(delta);
        if (_fallingTrees.Count == 0)
        {
            _falling.VisibleInstanceCount = 0;
            return;
        }
        int visible = 0;
        for (int i = _fallingTrees.Count - 1; i >= 0; i--)
        {
            var f = _fallingTrees[i];
            f.Age += delta;
            if (f.Age >= 3.2f)
            {
                _fallingTrees.RemoveAt(i);
                continue;
            }
            _fallingTrees[i] = f;
            float fall = Mathf.Clamp(f.Age / 1.25f, 0, 1);
            float angle = fall * fall * 1.5f;
            float sink = Mathf.Clamp((f.Age - 1.7f) / 1.5f, 0, 1);
            Vector3 axis = new Vector3(MathF.Cos(f.Yaw), 0, MathF.Sin(f.Yaw));
            Basis basis = new Basis(axis, angle) * Basis.Identity.Scaled(Vector3.One * f.Scale * (1 - sink * 0.75f));
            _falling.SetInstanceTransform(visible++, new Transform3D(basis, new Vector3(f.X, _map.Height(f.X, f.Z) - sink * 1.2f, f.Z)));
        }
        _falling.VisibleInstanceCount = visible;
    }

    private MultiMesh AddInstances(Mesh mesh, Material material, bool colors, int capacity, bool shadow = false)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = capacity, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = multi,
            MaterialOverride = material,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
            CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return multi;
    }

    private static void Ensure(MultiMesh multi, int count)
    {
        if (count <= multi.InstanceCount) return;
        multi.VisibleInstanceCount = 0;
        multi.InstanceCount = Math.Max(count, multi.InstanceCount * 2);
    }

    private void BuildTerrain()
    {
        int nx = _map.Nx, nz = _map.Nz;
        var vertices = new Vector3[nx * nz];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var indices = new int[(nx - 1) * (nz - 1) * 6];
        Color sand = new(0.72f, 0.66f, 0.46f), grass = new(0.36f, 0.53f, 0.25f), forest = new(0.25f, 0.41f, 0.21f), rock = new(0.47f, 0.45f, 0.41f);
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int i = x + z * nx;
            float h = _map.BaseHeightAtVertex(x, z), wx = x * WorldMap.Cell - WorldMap.SizeX / 2, wz = z * WorldMap.Cell - WorldMap.SizeZ / 2;
            vertices[i] = new Vector3(wx, h, wz);
            Vector3 n = new Vector3(_map.Height(wx - WorldMap.Cell, wz) - _map.Height(wx + WorldMap.Cell, wz), 2 * WorldMap.Cell,
                _map.Height(wx, wz - WorldMap.Cell) - _map.Height(wx, wz + WorldMap.Cell)).Normalized();
            normals[i] = n;
            Color c = sand.Lerp(grass.Lerp(forest, Mathf.SmoothStep(5f, 24f, h)), Mathf.SmoothStep(0.5f, 2.4f, h));
            // Dry salt grass and wider beaches toward the sea.
            float coastal = Mathf.SmoothStep(-75f, -8f, wx - _map.CoastX(wz));
            c = c.Lerp(new Color(0.6f, 0.59f, 0.36f), coastal * 0.5f);
            c = c.Lerp(sand, coastal * (1f - Mathf.SmoothStep(1.0f, 2.6f, h)));
            colors[i] = c.Lerp(rock, Mathf.SmoothStep(0.93f, 0.78f, n.Y));
        }
        int k = 0;
        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            int a = x + z * nx, b = a + 1, c = a + nx, d = c + 1;
            indices[k++] = a; indices[k++] = b; indices[k++] = c;
            indices[k++] = b; indices[k++] = d; indices[k++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        _wearBytes = new byte[Simulation.WearNx * Simulation.WearNz];
        _wearTexture = ImageTexture.CreateFromImage(Image.CreateFromData(Simulation.WearNx, Simulation.WearNz, false, Image.Format.L8, _wearBytes));
        _terrain = new ShaderMaterial { Shader = new Shader { Code = TerrainShader } };
        _terrain.SetShaderParameter("wear_tex", _wearTexture);
        _roadBytes = new byte[Rules.TileNx * Rules.TileNz * 3];
        _roadTexture = ImageTexture.CreateFromImage(Image.CreateFromData(Rules.TileNx, Rules.TileNz, false, Image.Format.Rgb8, _roadBytes));
        _terrain.SetShaderParameter("road_tex", _roadTexture);
        var soil = new Image();
        if (soil.LoadJpgFromBuffer(FileAccess.GetFileAsBytes("res://assets/ground/dry_mud_field_001.dctex")) == Error.Ok)
        {
            soil.GenerateMipmaps();
            _fieldMaterial = new ShaderMaterial { Shader = new Shader { Code = FieldShader } };
            _fieldMaterial.SetShaderParameter("soil_tex", ImageTexture.CreateFromImage(soil));
        }
        foreach ((string uniform, string file) in new[] { ("grass", "leafy_grass"), ("dirt", "dirt_aerial_02"), ("sand", "coast_sand_01"), ("rock", "rocks_ground_02"), ("cobble", "cobblestone_floor_01"), ("asphalt", "asphalt_02") })
        {
            var image = new Image();
            if (image.LoadJpgFromBuffer(FileAccess.GetFileAsBytes($"res://assets/ground/{file}.dctex")) != Error.Ok)
            {
                GD.PushWarning($"Ground texture {file} failed to load");
                continue;
            }
            image.GenerateMipmaps();
            _terrain.SetShaderParameter(uniform + "_tex", ImageTexture.CreateFromImage(image));
        }
        _polderBytes = new byte[nx * nz * 8];
        FillPolderBytes();
        _polderTexture = ImageTexture.CreateFromImage(Image.CreateFromData(nx, nz, false, Image.Format.Rgf, _polderBytes));
        _terrain.SetShaderParameter("polder_tex", _polderTexture);
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f, FractalOctaves = 4, Seed = _map.Seed };
        _noiseTexture = new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, GenerateMipmaps = true, Noise = noise };
        _terrain.SetShaderParameter("noise_tex", _noiseTexture);
        AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = _terrain,
            CustomAabb = new Aabb(new Vector3(-WorldMap.SizeX / 2, -10, -WorldMap.SizeZ / 2), new Vector3(WorldMap.SizeX, 120, WorldMap.SizeZ)),
        });
    }

    private void FillPolderBytes()
    {
        int nx = _map.Nx, nz = _map.Nz;
        var values = new float[nx * nz * 2];
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int i = (x + z * nx) * 2;
            values[i] = _map.HeightAtVertex(x, z);
            values[i + 1] = _map.ReclaimedAtVertex(x, z);
        }
        Buffer.BlockCopy(values, 0, _polderBytes, 0, _polderBytes.Length);
    }

    private void FillGroundBytes()
    {
        for (int z = 0; z < GroundDepth; z++)
        for (int x = 0; x < GroundWidth; x++)
        {
            float wx = (x + 0.5f) / GroundWidth * WorldMap.SizeX - WorldMap.SizeX / 2, wz = (z + 0.5f) / GroundDepth * WorldMap.SizeZ - WorldMap.SizeZ / 2;
            _groundBytes[x + z * GroundWidth] = (byte)Mathf.Clamp((_map.Height(wx, wz) + 6f) / 12f * 255f, 0, 255);
        }
    }

    private void UpdatePolderLand(WorldSnapshot s)
    {
        if (s.PolderVersion == _polderVersion) return;
        _polderVersion = s.PolderVersion;
        FillPolderBytes();
        _polderTexture.Update(Image.CreateFromData(_map.Nx, _map.Nz, false, Image.Format.Rgf, _polderBytes));
        FillGroundBytes();
        _groundTexture.Update(Image.CreateFromData(GroundWidth, GroundDepth, false, Image.Format.L8, _groundBytes));
    }

    private void BuildWater()
    {
        _groundBytes = new byte[GroundWidth * GroundDepth];
        FillGroundBytes();
        _groundTexture = ImageTexture.CreateFromImage(Image.CreateFromData(GroundWidth, GroundDepth, false, Image.Format.L8, _groundBytes));
        _waterMaterial = new ShaderMaterial { Shader = new Shader { Code = WaterShader } };
        _waterMaterial.SetShaderParameter("noise_tex", _noiseTexture);
        _waterMaterial.SetShaderParameter("ground_tex", _groundTexture);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(WorldMap.SizeX, WorldMap.SizeZ), SubdivideWidth = 120, SubdivideDepth = 64 },
            Position = new Vector3(0, WorldMap.WaterLevel, 0),
            MaterialOverride = _waterMaterial,
        });
    }

    /// <summary>Applies the latest world snapshot. Cheap when nothing changed.</summary>
    public void SyncWorld()
    {
        ReloadModelsIfChanged();
        WorldSnapshot s = _host.Snapshot;
        if (s == null || ReferenceEquals(s, _shown)) return;
        _shown = s;
        UpdateEraLook(s.Era);
        _wildlife.TraderVisiting = s.TraderVisiting;
        UpdatePolderLand(s);
        _surge = Mathf.MoveToward(_surge, s.Flooding ? 1f : 0f, 0.1f);
        _waterMaterial.SetShaderParameter("surge", _surge);
        UpdateSeason(s);
        // These follow sim state that changes without a road change (the dig, the train, market day, festivities).
        UpdateWaterMains(s);
        UpdateRailway(s);
        UpdateMarket(s);
        _festiveAt = s.Festive > 0 ? new Vector3(s.FestiveX, _map.Height(s.FestiveX, s.FestiveZ), s.FestiveZ) : null;
        _fireworks = s.Festive == 2 && s.Era >= 4;
        UpdateBuildings(s);
        int roadEra = s.Era;
        if (s.RoadVersion != _roadVersion || s.Buildings.Length != _scatterBuildings || roadEra != _roadEra)
        {
            _roadVersion = s.RoadVersion;
            _roadEra = roadEra;
            UpdateGroundUse(s);
            UpdateScatter(s);
        }
        if (s.TreeVersion != _treeVersion) UpdateTrees(s);
        // The list stays full once the oldest stones are cleared, so also watch its newest entry and new buildings.
        if (s.Graves.Length != _graveCount || s.Buildings.Length != _graveBuildings || (s.Graves.Length > 0 && s.Graves[^1] != _lastGrave)) UpdateGraves(s);
        Buffer.BlockCopy(s.Wear, 0, _wearBytes, 0, _wearBytes.Length);
        _wearTexture.Update(Image.CreateFromData(Simulation.WearNx, Simulation.WearNz, false, Image.Format.L8, _wearBytes));
    }

    /// <summary>
    /// Red: roads (none in the Stone Age camp, which only has footpaths from wear). Green: worked ground around
    /// buildings, from a trampled yard beside a hut to paved squares between the houses of a later town.
    /// </summary>
    private void UpdateGroundUse(WorldSnapshot s)
    {
        int nx = Rules.TileNx, nz = Rules.TileNz;
        var yard = new float[nx * nz];
        var spoil = new float[nx * nz];
        foreach (BuildingView b in s.Buildings)
        {
            if (b.Type is BType.Field or BType.Vineyard or BType.Dike) continue;
            int cx = (int)((b.X + WorldMap.SizeX / 2) / Rules.TileSize), cz = (int)((b.Z + WorldMap.SizeZ / 2) / Rules.TileSize);
            bool quarry = b.Type == BType.Quarry;
            // Quarries scar a wider patch with broken stone that grows as the town ages.
            int reach = quarry ? 5 + Math.Min(s.Era, 7) / 2 : b.Type == BType.Hut ? 3 : 4;
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(nz - 1, cz + reach); z++)
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(nx - 1, cx + reach); x++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / reach;
                if (quarry) spoil[x + z * nx] += Math.Max(0, 1 - d) * 1.4f;
                else yard[x + z * nx] += Math.Max(0, 1 - d) * 0.75f;
            }
        }
        for (int i = 0; i < nx * nz; i++)
        {
            byte tile = s.Tiles[i];
            _roadBytes[i * 3] = s.Era == 0 ? (byte)0 : tile == TileKind.Road ? (byte)255 : tile == TileKind.PlannedRoad ? (byte)150 : (byte)0;
            _roadBytes[i * 3 + 1] = tile == TileKind.Field ? (byte)0 : (byte)(Math.Min(1f, yard[i]) * 255);
            _roadBytes[i * 3 + 2] = (byte)(Math.Min(1f, spoil[i]) * 255);
        }
        _roadTexture.Update(Image.CreateFromData(nx, nz, false, Image.Format.Rgb8, _roadBytes));
        // Dirt tracks, then cobbles from the Middle Ages, then asphalt in the 20th century.
        _terrain.SetShaderParameter("road_style", s.Era >= 7 ? 2f : s.Era >= 4 ? 1f : 0f);
        _terrain.SetShaderParameter("street_style", s.Era >= 6 ? 2f : s.Era >= 5 ? 1f : 0f);
        UpdateStreetLamps(s);
        UpdateWaterSupply(s);
    }

    /// <summary>From the Industrial era a lamp stands at the edge of every few road tiles, beside the verge.</summary>
    private void UpdateStreetLamps(WorldSnapshot s)
    {
        int nx = Rules.TileNx, nz = Rules.TileNz, count = 0;
        var spots = new System.Collections.Generic.List<Transform3D>();
        if (s.Era >= 6)
            for (int z = 1; z < nz - 1; z++)
            for (int x = 1; x < nx - 1; x++)
            {
                int i = x + z * nx;
                if (s.Tiles[i] != TileKind.Road || (x * 7 + z * 3) % 5 != 0) continue;
                foreach ((int dx, int dz) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
                {
                    byte side = s.Tiles[i + dx + dz * nx];
                    if (side is TileKind.Road or TileKind.PlannedRoad or TileKind.Blocked) continue;
                    float cx = (x + 0.5f) * Rules.TileSize - WorldMap.SizeX / 2, cz = (z + 0.5f) * Rules.TileSize - WorldMap.SizeZ / 2;
                    float px = cx + dx * 1.75f, pz = cz + dz * 1.75f;
                    // The arm reaches back over the street.
                    var basis = new Basis(Vector3.Up, MathF.Atan2(-dx, -dz));
                    spots.Add(new Transform3D(basis, new Vector3(px, _map.Height(px, pz), pz)));
                    break;
                }
            }
        foreach (MultiMesh m in new[] { _lampPosts, _lanterns, _lampPools }) Ensure(m, spots.Count);
        foreach (Transform3D spot in spots)
        {
            _lampPosts.SetInstanceTransform(count, spot);
            _lanterns.SetInstanceTransform(count, spot);
            _lampPools.SetInstanceTransform(count, new Transform3D(Basis.Identity.Scaled(Vector3.One * 1.3f), spot.Origin + spot.Basis * new Vector3(0, 0.07f, 0.55f)));
            count++;
        }
        foreach (MultiMesh m in new[] { _lampPosts, _lanterns, _lampPools }) m.VisibleInstanceCount = count;
    }

    /// <summary>
    /// Water supply by era: a well beside each storehouse from the New Stone Age, a fountain there from the Middle Ages,
    /// and from the Industrial era a water tower on the highest free ground near the centre.
    /// </summary>
    /// <summary>The n-th visible water or heating landmark (cycling), or null when there is none.</summary>
    public Vector3? Landmark(int n)
    {
        var list = new System.Collections.Generic.List<Vector3>();
        foreach (MultiMesh m in new[] { _churches, _factories, _stations, _quays, _towers, _boilers, _fountains, _wells })
            if (m != null)
                for (int i = 0; i < Math.Min(2, m.VisibleInstanceCount); i++) list.Add(m.GetInstanceTransform(i).Origin);
        return list.Count == 0 ? null : list[n % list.Count];
    }

    /// <summary>The highest vertex of a mesh, for example the mouth of a chimney.</summary>
    private static Vector3 HighestPoint(Mesh mesh)
    {
        Vector3 best = Vector3.Zero;
        foreach (Vector3 v in (Vector3[])mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex]) if (v.Y > best.Y) best = v;
        return best;
    }

    private void UpdateWaterSupply(WorldSnapshot s)
    {
        int wells = 0, fountains = 0, towers = 0;
        foreach (BuildingView b in s.Buildings) if (b.Type == BType.Storehouse) { Ensure(_wells, wells + 1); Ensure(_fountains, fountains + 1); wells++; fountains++; }
        wells = fountains = 0;
        if (s.Era >= 1)
            foreach (BuildingView b in s.Buildings)
            {
                if (b.Type != BType.Storehouse || !b.Built) continue;
                // The first spot around the store that is open ground, so the well never sits inside a neighbour.
                Vector3? found = null;
                foreach (Vector3 local in new[] { new Vector3(7.6f, 0, 4.6f), new Vector3(-7.6f, 0, 4.6f), new Vector3(7.6f, 0, -4.6f), new Vector3(-7.6f, 0, -4.6f), new Vector3(0, 0, 9f), new Vector3(9f, 0, 0), new Vector3(-9f, 0, 0), new Vector3(0, 0, -9f) })
                {
                    Vector3 p = new Vector3(b.X, 0, b.Z) + local.Rotated(Vector3.Up, b.Rotation);
                    int tx = (int)((p.X + WorldMap.SizeX / 2) / Rules.TileSize), tz = (int)((p.Z + WorldMap.SizeZ / 2) / Rules.TileSize);
                    if (tx < 0 || tz < 0 || tx >= Rules.TileNx || tz >= Rules.TileNz || s.Tiles[tx + tz * Rules.TileNx] != TileKind.Free) continue;
                    found = p;
                    break;
                }
                if (found is not Vector3 at) continue;
                at.Y = _map.Height(at.X, at.Z);
                var spot = new Transform3D(new Basis(Vector3.Up, b.Rotation), at);
                if (s.Era >= 4) _fountains.SetInstanceTransform(fountains++, spot);
                else _wells.SetInstanceTransform(wells++, spot);
            }
        // Landmark lots are chosen and kept by the simulation, so they never move and nobody walks through them.
        Vector3 LotCentre(Simulation.Landmark l)
        {
            float wx = (l.TileX + 0.5f) * Rules.TileSize - WorldMap.SizeX / 2, wz = (l.TileZ + 0.5f) * Rules.TileSize - WorldMap.SizeZ / 2;
            return new Vector3(wx, _map.Height(wx, wz), wz);
        }
        foreach (Simulation.Landmark l in s.Landmarks)
            if (l.Kind == 1 && towers == 0) _towers.SetInstanceTransform(towers++, new Transform3D(Basis.Identity, LotCentre(l)));
        // Heating by era: coal smoke is darker in the Industrial era; in the 20th century a district heating boiler
        // house beside the water tower takes over from most hearths.
        _puffMaterial?.SetShaderParameter("tone", s.Era == 6 ? new Vector3(0.42f, 0.41f, 0.4f) : s.Era >= 7 ? new Vector3(0.8f, 0.8f, 0.8f) : new Vector3(0.86f, 0.85f, 0.82f));
        _boilerAt = null;
        foreach (Simulation.Landmark l in s.Landmarks) if (l.Kind == 2) _boilerAt = LotCentre(l);
        _factoryAt = null;
        foreach (Simulation.Landmark l in s.Landmarks)
            if (l.Kind == 3 && _factories != null) (_factoryAt, _factoryYaw) = (LotCentre(l), l.Yaw);
        if (_stations != null)
        {
            int stations = 0;
            foreach (Simulation.Landmark l in s.Landmarks)
                if (l.Kind == 4 && stations == 0) _stations.SetInstanceTransform(stations++, new Transform3D(new Basis(Vector3.Up, l.Yaw), LotCentre(l)));
            _stations.VisibleInstanceCount = stations;
        }
        if (_factories != null)
        {
            if (_factoryAt is Vector3 mill) _factories.SetInstanceTransform(0, new Transform3D(new Basis(Vector3.Up, _factoryYaw), mill));
            _factories.VisibleInstanceCount = _factoryAt != null ? 1 : 0;
        }
        if (_boilerAt is Vector3 boiler) _boilers.SetInstanceTransform(0, new Transform3D(Basis.Identity, boiler));
        _boilers.VisibleInstanceCount = _boilerAt != null ? 1 : 0;
        // From the Middle Ages a parish church stands on the open ground nearest the centre, facing it.
        int churches = 0;
        foreach (Simulation.Landmark l in s.Landmarks)
            if (l.Kind == 0 && churches == 0)
                // The model's long axis is Z with the tower at +Z; the lot's yaw turns the tower away from the centre.
                _churches.SetInstanceTransform(churches++, new Transform3D(new Basis(Vector3.Up, l.Yaw), LotCentre(l)));
        if (churches != _churches.VisibleInstanceCount || (churches > 0 && _churches.GetInstanceTransform(0).Origin != _churchShown)) _graveCount = -1;
        _churches.VisibleInstanceCount = churches;
        // From the Renaissance a quay with a treadwheel crane stands on the bank where the trading ship anchors.
        int quays = 0;
        // Shown only with an image-made model; the low-poly stand-in looked too crude beside the realistic town.
        if (s.Era >= 5 && MeshKit.LoadModel("landmark_quay") != null)
        {
            // The ship anchors and the crowd gathers at CenterX + 25 (Simulation); the quay stands just downstream of them so
            // the onlookers stand on the bank beside it, not inside its stonework.
            float qx = s.CenterX + 41;
            // The sim reserves the quay's strip of bank (landmark kind 5); older saves fall back to the fixed spot.
            foreach (Simulation.Landmark l in s.Landmarks)
                if (l.Kind == 5) qx = Simulation.TileCenter(l.TileX + l.TileZ * Rules.TileNx).X;
            float river = _map.RiverCenter(qx), side = MathF.Sign(s.CenterZ - river);
            for (float d = 0; d <= 120; d += 1)
                if (_map.Walkable(qx, river + side * d))
                {
                    float bank = river + side * d;
                    var at = new Vector3(qx, _map.Height(qx, bank + side * 3f), bank - side * 1.5f);
                    _quays.SetInstanceTransform(quays++, new Transform3D(new Basis(Vector3.Up, side > 0 ? MathF.PI : 0), at));
                    // While the trading ship is in, its cargo stands in piles on the quay.
                    int piles = 0;
                    if (s.TraderVisiting)
                        foreach (float dx in new[] { -5f, -2.5f, 3f })
                        {
                            var pile = new Vector3(qx + dx, _map.Height(qx, bank) + 0.05f, bank + side * 1.2f);
                            _cargo2.SetInstanceTransform(piles++, new Transform3D(new Basis(Vector3.Up, dx), pile));
                        }
                    _cargo2.VisibleInstanceCount = piles;
                    break;
                }
        }
        _quays.VisibleInstanceCount = quays;
        if (churches > 0) _churchShown = _churches.GetInstanceTransform(0).Origin;
        _wells.VisibleInstanceCount = wells;
        _fountains.VisibleInstanceCount = fountains;
        _towers.VisibleInstanceCount = towers;
    }

    /// <summary>
    /// The street tile the sim's diggers are working on gets a trench, pipe and spoil heap, laid along the street.
    /// The sim lays water mains outwards from the water tower in the Industrial era and sewers in the 20th century.
    /// </summary>
    private void UpdateWaterMains(WorldSnapshot s)
    {
        int count = 0;
        int tile = s.DigTile, nx = Rules.TileNx;
        if (tile >= 0 && tile < s.Tiles.Length && s.Tiles[tile] == TileKind.Road)
        {
            Ensure(_trenches, 4);
            (float x, float z) = Simulation.TileCenter(tile);
            // Lay the trench along the street: across X if the neighbours left/right are road.
            bool alongX = tile % nx > 0 && s.Tiles[tile - 1] == TileKind.Road || tile % nx < nx - 1 && s.Tiles[tile + 1] == TileKind.Road;
            var basis = new Basis(Vector3.Up, alongX ? 0 : MathF.PI / 2);
            _trenches.SetInstanceTransform(count++, new Transform3D(basis, new Vector3(x, _map.Height(x, z) + 0.02f, z)));
        }
        _trenches.VisibleInstanceCount = count;
    }
    private void UpdateEraLook(int era)
    {
        if (era != _clothEra)
        {
            _clothEra = era;
            _wildlife.SetEra(era);
            (Color a, Color b, float dye) = era switch
            {
                2 => (new Color(0.62f, 0.42f, 0.26f), new Color(0.68f, 0.58f, 0.4f), 0.45f),
                3 => (new Color(0.74f, 0.68f, 0.56f), new Color(0.56f, 0.24f, 0.2f), 0.6f),
                4 => (new Color(0.36f, 0.4f, 0.28f), new Color(0.5f, 0.24f, 0.2f), 0.65f),
                5 => (new Color(0.24f, 0.32f, 0.5f), new Color(0.56f, 0.2f, 0.24f), 0.72f),
                6 => (new Color(0.24f, 0.25f, 0.28f), new Color(0.42f, 0.34f, 0.26f), 0.8f),
                7 => (new Color(0.2f, 0.34f, 0.58f), new Color(0.5f, 0.52f, 0.54f), 0.85f),
                _ => (Colors.White, Colors.White, 0f),
            };
            foreach (TexturedRole role in _texturedRoles)
            {
                role?.Kit.SetCloth(a, b, dye);
                role?.Kit.SetPrimitive(era == 0);
            }
        }
        int tier = MeshKit.EraTier(era);
        if (tier == _eraTier) return;
        // A real era change (not a model reload or the first frame) starts a renovation wave: the old models stay
        // on the houses and workshops that have not been rebuilt yet.
        bool renovate = _eraTier != int.MinValue && _shown != null;
        if (renovate)
        {
            _renovationStart = _shown.Year;
            for (int i = 0; i < (int)BType.Count; i++)
            {
                _older[i].Mesh = _buildings[i].Mesh;
                _olderNodes[i].MaterialOverride = _buildingNodes[i].MaterialOverride;
            }
        }
        _eraTier = tier;
        for (int i = 0; i < (int)BType.Count; i++)
        {
            ArrayMesh mesh = MeshKit.EraBuilding((BType)i, tier);
            _buildings[i].Mesh = mesh;
            ArrayMesh second = MeshKit.RealisticVariant((BType)i, tier);
            _variantShown[i] = second != null;
            if (second != null)
            {
                _variants[i].Mesh = second;
                _variantNodes[i].MaterialOverride = BuildingMaterial((StandardMaterial3D)second.SurfaceGetMaterial(0), second, (BType)i);
            }
            else _variants[i].VisibleInstanceCount = 0;
            // Realistic models carry their own textured material; kit and procedural models use vertex colors.
            _buildingNodes[i].MaterialOverride = mesh.GetSurfaceCount() > 0 && mesh.SurfaceGetMaterial(0) is StandardMaterial3D real ? BuildingMaterial(real, mesh, (BType)i)
                : (BType)i is BType.Field or BType.Vineyard or BType.Dike && _fieldMaterial != null ? _fieldMaterial : _vertexColors;
        }
    }

    private readonly System.Collections.Generic.Dictionary<Texture2D, ShaderMaterial> _buildingMaterials = new();

    /// <summary>A recolouring material for a realistic model; mills, gates and dikes keep their own colours.</summary>
    private Material BuildingMaterial(StandardMaterial3D real, ArrayMesh mesh, BType type)
    {
        if (type is BType.Windmill or BType.Watermill or BType.TideGate or BType.Quarry || real.AlbedoTexture == null) return null;
        if (_buildingMaterials.TryGetValue(real.AlbedoTexture, out ShaderMaterial cached)) return cached;
        var material = new ShaderMaterial { Shader = _buildingShader ??= new Shader { Code = BuildingShader } };
        material.SetShaderParameter("albedo", real.AlbedoTexture);
        material.SetShaderParameter("model_height", Math.Max(1f, mesh.GetAabb().Size.Y));
        return _buildingMaterials[real.AlbedoTexture] = material;
    }

    private Shader _buildingShader;

    /// <summary>0 is full detail. Level 2 and above hides small ground scatter.</summary>
    public void SetDetail(int level)
    {
        _detail = level;
        if (_shown != null) UpdateSeason(_shown);
    }

    private void UpdateSeason(WorldSnapshot s)
    {
        _wildlife.Season = s.Season;
        float f = s.YearFraction * 4;
        int a = (int)f % 4, b = (a + 1) % 4;
        float t = Mathf.SmoothStep(0.72f, 1f, f - MathF.Floor(f));
        Vector3[] tints = { new(0.94f, 1.1f, 0.84f), new(1f, 1f, 0.96f), new(1.24f, 0.9f, 0.55f), new(0.82f, 0.82f, 0.78f) };
        float[] snow = { 0, 0, 0, 0.76f };
        Color[] leaves = { new(0.3f, 0.56f, 0.26f), new(0.2f, 0.42f, 0.2f), new(0.68f, 0.42f, 0.16f), new(0.74f, 0.78f, 0.78f) };
        Color[] water = { new(0.2f, 0.45f, 0.58f), new(0.18f, 0.44f, 0.56f), new(0.2f, 0.38f, 0.48f), new(0.56f, 0.68f, 0.76f) };
        // The year's event shows on the land: a drought parches grass and leaves from late spring, and a harsh
        // winter brings snow before the end of autumn and lays it deeper.
        float yf = s.YearFraction;
        float drought = s.ActiveEvent == 1 ? Mathf.SmoothStep(0.08f, 0.3f, yf) * (1 - Mathf.SmoothStep(0.8f, 0.95f, yf)) : 0;
        float harsh = s.ActiveEvent == 2 ? Mathf.SmoothStep(0.64f, 0.78f, yf) * (1 - Mathf.SmoothStep(0.94f, 1f, yf)) : 0;
        Vector3 tint = tints[a].Lerp(tints[b], t).Lerp(new Vector3(1.32f, 0.98f, 0.52f), drought * 0.7f);
        float snowLevel = Math.Max(Mathf.Lerp(snow[a], snow[b], t), harsh * 0.96f);
        _terrain.SetShaderParameter("season_tint", tint);
        _terrain.SetShaderParameter("snow", snowLevel);
        // Only a harsh winter freezes the river: ice spreads out from the shallow banks.
        _waterMaterial.SetShaderParameter("ice", harsh);
        // Skaters go out only once the ice has spread well out from the banks.
        _wildlife.Frozen = harsh > 0.8f;
        Color leaf = leaves[a].Lerp(leaves[b], t).Lerp(new Color(0.46f, 0.46f, 0.2f), drought * 0.35f).Lerp(leaves[3], harsh * 0.8f);
        Color sea = water[a].Lerp(water[b], t);
        _treeMaterial.SetShaderParameter("leaf", new Vector3(leaf.R, leaf.G, leaf.B));
        foreach (ShaderMaterial real in _realTreeMaterials) real.SetShaderParameter("leaf", new Vector3(leaf.R, leaf.G, leaf.B));
        _waterMaterial.SetShaderParameter("deep", new Vector3(sea.R * 0.78f, sea.G * 0.84f, sea.B * 0.92f));
        _scatterMaterial.SetShaderParameter("tint", tint);
        _fieldMaterial?.SetShaderParameter("green_tint", tint);
        _fieldMaterial?.SetShaderParameter("snow", snowLevel);
        bool snowy = snowLevel > 0.35f;
        _scatterNodes[0].Visible = !snowy && _detail < 2;
        _scatterNodes[1].Visible = s.Season is 0 or 1 && drought < 0.5f && _detail < 2;
        _scatterNodes[2].Visible = _detail < 2;
    }

    /// <summary>Homes, stores and workshops are rebuilt one by one after an era change; works and fields change at once.</summary>
    private static bool Renovates(BType type) => type is BType.Hut or BType.Storehouse or BType.Bakery or BType.Smithy or BType.Winery
        or BType.Woodcutter or BType.Fisher or BType.Gatherer or BType.Mill;

    private void UpdateBuildings(WorldSnapshot s)
    {
        var counts = new int[_buildings.Length];
        foreach (BuildingView b in s.Buildings) counts[(int)b.Type]++;
        _wildlife.SetVillage(counts[(int)BType.Field], counts[(int)BType.Fisher], counts[(int)BType.Hut]);
        for (int i = 0; i < counts.Length; i++)
        {
            Ensure(_buildings[i], counts[i]);
            Ensure(_variants[i], counts[i]);
            Ensure(_older[i], counts[i]);
            if (_moving[i] != null) Ensure(_moving[i], counts[i]);
        }
        var movingCounts = new int[_moving.Length];
        Ensure(_crops, counts[(int)BType.Field]);
        Ensure(_glow, counts[(int)BType.Hut]);
        Ensure(_sites, s.Buildings.Length);
        Array.Clear(counts);
        var variantCounts = new int[_buildings.Length];
        var olderCounts = new int[_buildings.Length];
        float renovated = (s.Year - _renovationStart) / RenovationYears;
        int crops = 0, glows = 0, sites = 0, rubble = 0;
        Ensure(_rubble, 4);
        var piles = new int[_piles.Length];
        foreach (MultiMesh pile in _piles) Ensure(pile, counts[(int)BType.Storehouse] * 4 + 4);
        foreach (BuildingView b in s.Buildings)
        {
            float y = _map.Height(b.X, b.Z);
            // From the New Stone Age a plot under construction gets stakes, scaffold and materials, and the house rises inside it.
            bool site = !b.Built && s.Era >= 1 && b.Type is not (BType.Field or BType.Vineyard or BType.Quarry or BType.Dike or BType.TideGate);
            float rise = b.Built ? 1 : site ? 0.03f + b.Progress * 0.97f : 0.18f + b.Progress * 0.82f;
            // A house being rebuilt is first knocked down (its walls sink as builders work from 20% to 30%),
            // then the new house rises from the ground over the rest of the work.
            if (b.Renewed && !b.Built)
                rise = b.Progress < 0.3f ? Math.Max(0.03f, 1f - (b.Progress - 0.2f) * 9.7f) : 0.03f + (b.Progress - 0.3f) / 0.7f * 0.97f;
            float yaw = b.Type == BType.Field ? MathF.Round(b.Rotation / (MathF.PI / 2)) * (MathF.PI / 2) : b.Rotation;
            // The windmill's cap and sails turn into the south-east sea wind, the same yaw as its moving part below.
            if (b.Type == BType.Windmill) yaw = MathF.PI * 0.3f;
            // A house rebuilt in the Industrial era stands a little wider and taller than its neighbours.
            float grow = b.Renewed ? 1.12f : 1f, lift = b.Renewed ? 1.22f : 1f;
            Basis basis = b.Type is BType.Field or BType.Vineyard ? GroundBasis(b.X, b.Z, yaw, rise) : new Basis(Vector3.Up, yaw).Scaled(new Vector3(grow, rise * lift, grow));
            if (b.Type == BType.Field) y += 0.22f;
            var transform = new Transform3D(basis, new Vector3(b.X, y, b.Z));
            // Every other building of a type with a second shape for this era uses that shape.
            bool alt = _variantShown[(int)b.Type] && ((((int)(b.X * 7.31f) ^ (int)(b.Z * 3.17f)) & 1) == 1) != b.Renewed;
            // Each building gets its turn at a fixed point of the renovation wave, from a hash of its position.
            float turn = ((uint)((int)(b.X * 13.7f) * 73856093 ^ (int)(b.Z * 11.3f) * 19349663) % 1000) / 1000f;
            bool renovating = renovated < 1f && b.Built && Renovates(b.Type);
            bool old = renovating && turn > renovated;
            // A house stands in scaffolding for a short while around its turn to be rebuilt.
            if (renovating && s.Era >= 1 && MathF.Abs(turn - renovated) < 0.03f) site = true;
            MultiMesh target = old ? _older[(int)b.Type] : alt ? _variants[(int)b.Type] : _buildings[(int)b.Type];
            int n = old ? olderCounts[(int)b.Type]++ : alt ? variantCounts[(int)b.Type]++ : counts[(int)b.Type]++;
            target.SetInstanceTransform(n, transform);
            target.SetInstanceColor(n, b.Built ? Colors.White : new Color(0.88f, 0.82f, 0.72f));
            if (b.Built && _movingParts[(int)b.Type] is MeshKit.MovingPart moving)
            {
                // Windmill caps turn into the south-east sea wind, so the sails also face the usual camera.
                bool wind = b.Type == BType.Windmill;
                const float windYaw = MathF.PI * 0.3f;
                Basis partBasis = wind ? new Basis(Vector3.Up, windYaw) : new Basis(Vector3.Up, yaw) * moving.Local;
                Vector3 pivot = wind ? new Vector3(MathF.Sin(windYaw) * moving.Pivot.X, moving.Pivot.Y, MathF.Cos(windYaw) * moving.Pivot.X) : moving.Pivot.Rotated(Vector3.Up, yaw);
                int slot = movingCounts[(int)b.Type]++;
                _moving[(int)b.Type].SetInstanceTransform(slot, new Transform3D(partBasis, new Vector3(b.X, y, b.Z) + pivot));
                _moving[(int)b.Type].SetInstanceColor(slot, Colors.White);
                _moving[(int)b.Type].SetInstanceCustomData(slot, new Color(moving.Speed, (b.X * 0.37f + b.Z * 0.61f) % MathF.Tau, moving.Mode, moving.Amount));
            }

            if (b.Type == BType.Field)
            {
                // Drought stunts and browns the crop; a bumper year stands tall and golden.
                float stand = s.ActiveEvent switch { 1 => 0.62f, 4 => 1.22f, _ => 1f };
                float growth = b.Sown ? Math.Max(0.06f, b.Growth) * stand : 0.001f;
                Basis cropBasis = GroundBasis(b.X, b.Z, yaw, growth);
                var crop = new Transform3D(cropBasis, new Vector3(b.X, y, b.Z) + cropBasis.Y.Normalized() * 0.2f);
                _crops.SetInstanceTransform(crops, crop);
                Color green = s.ActiveEvent == 1 ? new Color(0.5f, 0.52f, 0.24f) : new Color(0.3f, 0.58f, 0.22f);
                Color ripe = s.ActiveEvent switch { 1 => new Color(0.66f, 0.5f, 0.28f), 4 => new Color(0.98f, 0.78f, 0.22f), _ => new Color(0.9f, 0.74f, 0.28f) };
                _crops.SetInstanceColor(crops++, green.Lerp(ripe, Mathf.SmoothStep(0.55f, 1f, b.Growth)));
            }
            if (b.Type == BType.Storehouse && b.Built)
            {
                // Goods piles beside the store: logs and stone along the sides, sacks and barrels behind, one unit per level.
                int[] levels = { b.WoodPile, b.StonePile, b.SackPile, b.BarrelPile };
                for (int k = 0; k < levels.Length; k++)
                    for (int j = 0; j < levels[k]; j++)
                    {
                        Vector3 local = k switch
                        {
                            0 => new Vector3(-6.4f, 0, -2.4f + j * 1.15f),
                            1 => new Vector3(6.4f, 0, -2.4f + j * 1.15f),
                            2 => new Vector3(-3.4f + j * 1.0f, 0, -5.9f),
                            _ => new Vector3(0.9f + j * 1.0f, 0, -5.9f),
                        };
                        Vector3 at = new Vector3(b.X, 0, b.Z) + local.Rotated(Vector3.Up, yaw);
                        at.Y = _map.Height(at.X, at.Z);
                        float pileYaw = yaw + (k == 0 ? MathF.PI / 2 : 0) + ((j * 37 + k * 11) % 7 - 3) * 0.04f;
                        _piles[k].SetInstanceTransform(piles[k]++, new Transform3D(new Basis(Vector3.Up, pileYaw), at));
                    }
            }
            if (site)
            {
                float plot = Rules.Footprint(b.Type) / 2f;
                _sites.SetInstanceTransform(sites++, new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3(plot, 1, plot)), new Vector3(b.X, y, b.Z)));
                // A house being rebuilt has the rubble of the old one heaped beside it.
                if (b.Renewed && rubble < 4)
                {
                    Vector3 heap = new Vector3(b.X, 0, b.Z) + new Vector3(-4.2f * plot, 0, -2.5f).Rotated(Vector3.Up, yaw);
                    heap.Y = _map.Height(heap.X, heap.Z);
                    _rubble.SetInstanceTransform(rubble++, new Transform3D(new Basis(Vector3.Up, yaw + 0.7f), heap));
                }
            }
            if (b.Type == BType.Hut && b.Built && b.Occupants > 0)
                _glow.SetInstanceTransform(glows++, new Transform3D(Basis.Identity, new Vector3(b.X, y + 0.06f, b.Z) + new Vector3(0, 0, s.Era == 0 ? 3.5f : 2.72f).Rotated(Vector3.Up, yaw)));
        }
        for (int i = 0; i < _buildings.Length; i++)
        {
            _buildings[i].VisibleInstanceCount = counts[i];
            _variants[i].VisibleInstanceCount = variantCounts[i];
            _older[i].VisibleInstanceCount = olderCounts[i];
            if (_moving[i] != null) _moving[i].VisibleInstanceCount = movingCounts[i];
        }
        _crops.VisibleInstanceCount = crops;
        _glow.VisibleInstanceCount = glows;
        _sites.VisibleInstanceCount = sites;
        _rubble.VisibleInstanceCount = rubble;
        for (int k = 0; k < _piles.Length; k++) _piles[k].VisibleInstanceCount = piles[k];
        UpdateHedges(s);

        int puffs = 0;
        // Flames lick out over the roof: houses grow taller era by era.
        float roof = new[] { 3f, 3.5f, 4.5f, 5.5f, 7f, 7.5f, 9.5f, 10f }[Math.Clamp(s.Era, 0, 7)];
        _fireAt = s.Burning ? new Vector3(s.FireX, _map.Height(s.FireX, s.FireZ) + roof * 0.7f, s.FireZ) : null;
        if (_fireAt is Vector3 fire)
        {
            // A burning house sends up a thick column of smoke; it takes the budget first.
            for (int k = 0; k < 14; k++)
            {
                _smoke.SetInstanceTransform(puffs, new Transform3D(Basis.Identity.Scaled(Vector3.One * 3.2f), fire + new Vector3((k % 3 - 1) * 1.5f, 4f, (k / 3 % 3 - 1) * 1.5f)));
                _smoke.SetInstanceCustomData(puffs++, new Color(k / 14f, 0, 0));
            }
        }
        // In a harsh winter every hearth burns from late autumn.
        bool cold = s.Season == 3 || (s.ActiveEvent == 2 && s.YearFraction > 0.64f);
        // Bakeries and smithies take their smoke first, so a big winter town does not use the whole budget on huts.
        for (int pass = 0; pass < 2; pass++)
        foreach (BuildingView b in s.Buildings)
        {
            bool workshop = b.Type is BType.Bakery or BType.Smithy;
            if (workshop != (pass == 0)) continue;
            // A house on the district heating (20th century, street by street) seldom lights its own hearth.
            bool hutFire = b.Type == BType.Hut && b.Occupants > 0 && (cold || (int)(b.X * 3 + b.Z * 5) % 3 == 0) && (!b.DistrictHeated || (int)(b.X * 5 + b.Z * 3) % 4 == 0);
            if (!b.Built || (!workshop && !hutFire)) continue;
            int count = b.Type == BType.Hut ? 3 : 5;
            Vector3 source = b.Type == BType.Hut && s.Era == 0
                ? new Vector3(b.X, _map.Height(b.X, b.Z) + 0.3f, b.Z) + new Vector3(0, 0, 3.5f).Rotated(Vector3.Up, b.Rotation)
                : b.Type == BType.Bakery
                ? new Vector3(b.X, _map.Height(b.X, b.Z) + 2.2f, b.Z) + new Vector3(2.3f, 0, 0).Rotated(Vector3.Up, b.Rotation)
                : b.Type == BType.Smithy
                ? new Vector3(b.X, _map.Height(b.X, b.Z) + 5.2f, b.Z) + new Vector3(3.6f, 0, -2.0f).Rotated(Vector3.Up, b.Rotation)
                : new Vector3(b.X, _map.Height(b.X, b.Z) + 4.3f, b.Z);
            for (int k = 0; k < count && puffs < MaxPuffs; k++)
            {
                _smoke.SetInstanceTransform(puffs, new Transform3D(Basis.Identity, source));
                float phase = k / (float)count + b.X * 0.137f;
                _smoke.SetInstanceCustomData(puffs++, new Color(phase - MathF.Floor(phase), 0, 0));
            }
        }
        if (_boilerAt is Vector3 plant)
        {
            Vector3 top = plant + _chimneyTop + Vector3.Up * 0.3f;
            for (int k = 0; k < 8 && puffs < MaxPuffs; k++)
            {
                _smoke.SetInstanceTransform(puffs, new Transform3D(Basis.Identity.Scaled(Vector3.One * 2.2f), top));
                _smoke.SetInstanceCustomData(puffs++, new Color(k / 8f, 0, 0));
            }
        }
        if (_factoryAt is Vector3 works)
        {
            // The mill chimney smokes heavily, dark coal smoke in the Industrial era.
            Vector3 top = works + _factoryChimney.Rotated(Vector3.Up, _factoryYaw) + Vector3.Up * 0.3f;
            for (int k = 0; k < 10 && puffs < MaxPuffs; k++)
            {
                _smoke.SetInstanceTransform(puffs, new Transform3D(Basis.Identity.Scaled(Vector3.One * 2.6f), top));
                _smoke.SetInstanceCustomData(puffs++, new Color(k / 10f, 0, 0));
            }
        }
        _smoke.VisibleInstanceCount = puffs;
    }

    /// <summary>Lays a flat object along the terrain plane so it neither sinks nor overhangs.</summary>
    /// <summary>From the Middle Ages fields are enclosed: bushes grow along two sides of each field, where no road or building is.</summary>
    private void UpdateHedges(WorldSnapshot s)
    {
        int n = 0;
        if (s.Era >= 4 && s.Tiles.Length > 0)
            foreach (BuildingView b in s.Buildings)
            {
                if (b.Type != BType.Field || !b.Built) continue;
                float yaw = MathF.Round(b.Rotation / (MathF.PI / 2)) * (MathF.PI / 2);
                Vector3 right = new Vector3(1, 0, 0).Rotated(Vector3.Up, yaw), back = new Vector3(0, 0, -1).Rotated(Vector3.Up, yaw);
                for (int side = 0; side < 2; side++)
                for (int k = 0; k < 6 && n < _hedges.InstanceCount; k++)
                {
                    float along = -8.5f + k * 3.4f, seed = b.X * 0.37f + b.Z * 0.61f + k * 1.7f + side;
                    Vector3 offset = side == 0 ? back * 10.6f + right * along : right * 10.6f + back * -along;
                    float x = b.X + offset.X, z = b.Z + offset.Z;
                    int tx = (int)((x + WorldMap.SizeX / 2) / Rules.TileSize), tz = (int)((z + WorldMap.SizeZ / 2) / Rules.TileSize);
                    if (tx < 0 || tz < 0 || tx >= Rules.TileNx || tz >= Rules.TileNz || s.Tiles[tx + tz * Rules.TileNx] is TileKind.Road or TileKind.Building or TileKind.PlannedRoad) continue;
                    float size = 0.22f + 0.08f * MathF.Abs(MathF.Sin(seed * 3.1f));
                    _hedges.SetInstanceTransform(n++, new Transform3D(new Basis(Vector3.Up, seed).Scaled(new Vector3(size * 1.4f, size, size * 1.4f)), new Vector3(x, _map.Height(x, z) - 0.1f, z)));
                }
            }
        _hedges.VisibleInstanceCount = n;
    }

    private Basis GroundBasis(float x, float z, float yaw, float rise)
    {
        const float e = 8f;
        Vector3 up = new Vector3(_map.Height(x - e, z) - _map.Height(x + e, z), 2 * e, _map.Height(x, z - e) - _map.Height(x, z + e)).Normalized();
        Vector3 forward = new Basis(Vector3.Up, yaw).X;
        Vector3 xAxis = (forward - up * forward.Dot(up)).Normalized();
        return new Basis(xAxis, up * rise, xAxis.Cross(up));
    }

    private void UpdateTrees(WorldSnapshot s)
    {
        _treeVersion = s.TreeVersion;
        (float[] xs, float[] zs) = _host.TreePositions;
        var visibleKinds = new int[_treeKinds.Length];
        int stumps = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            float scale = 0.8f + (unchecked((uint)i * 2654435761u) >> 24) / 255f * 0.75f;
            if (!s.TreeAlive[i])
            {
                // Felled trees leave a stump on open ground until the tree grows back; cleared building land does not.
                int tile = Math.Clamp((int)((xs[i] + WorldMap.SizeX / 2) / Rules.TileSize), 0, Rules.TileNx - 1)
                    + Math.Clamp((int)((zs[i] + WorldMap.SizeZ / 2) / Rules.TileSize), 0, Rules.TileNz - 1) * Rules.TileNx;
                if (s.Tiles.Length == 0 || s.Tiles[tile] == TileKind.Free)
                    _stumps.SetInstanceTransform(stumps++, new Transform3D(new Basis(Vector3.Up, i * 1.3f).Scaled(Vector3.One * scale), new Vector3(xs[i], _map.Height(xs[i], zs[i]) - 0.05f, zs[i])));
                bool nearView = new Vector2(xs[i] - _viewCenter.X, zs[i] - _viewCenter.Z).LengthSquared() < NearRadius * NearRadius;
                if (_knownTrees != null && _knownTrees[i] && nearView && _fallingTrees.Count < MaxFalling)
                    _fallingTrees.Add((xs[i], zs[i], 0, scale, i * 1.7f));
                continue;
            }
            var basis = new Basis(Vector3.Up, i * 2.4f).Scaled(new Vector3(scale, scale, scale));
            // Pines on the hills, broadleaf and birch on the lowlands.
            float ground = _map.Height(xs[i], zs[i]);
            int roll = (int)(unchecked((uint)i * 2246822519u) >> 25);
            int kind = ground > 10f ? (roll < 90 ? 0 : 2) : roll < 58 ? 1 : roll < 96 ? 2 : 0;
            _treeKinds[kind].SetInstanceTransform(visibleKinds[kind]++, new Transform3D(basis, new Vector3(xs[i], ground - 0.1f, zs[i])));
        }
        for (int i = 0; i < _treeKinds.Length; i++) _treeKinds[i].VisibleInstanceCount = visibleKinds[i];
        _stumps.VisibleInstanceCount = stumps;
        _knownTrees = s.TreeAlive;
    }

    /// <summary>True for a point under the church's walls (the churchyard around it keeps its stones).</summary>
    private bool InsideChurch(float x, float z)
    {
        if (_churches == null || _churches.VisibleInstanceCount == 0) return false;
        Transform3D church = _churches.GetInstanceTransform(0);
        Vector3 local = church.AffineInverse() * new Vector3(x, church.Origin.Y, z);
        return MathF.Abs(local.X) < 6f && MathF.Abs(local.Z) < 12.8f;
    }

    private void UpdateGraves(WorldSnapshot s)
    {
        _graveCount = s.Graves.Length;
        _graveBuildings = s.Buildings.Length;
        if (_graveCount > 0) _lastGrave = s.Graves[^1];
        Ensure(_graves, _graveCount);
        for (int i = 0; i < _graveCount; i++)
        {
            (float x, float z) = s.Graves[i];
            // A stone the town has since built over is hidden.
            byte tile = s.Tiles[Simulation.TileAt(x, z)];
            float scale = tile is TileKind.Building or TileKind.Road or TileKind.Field || InsideChurch(x, z) ? 0f : 1f;
            _graves.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, i * 0.7f).Scaled(Vector3.One * scale), new Vector3(x, _map.Height(x, z), z)));
        }
        _graves.VisibleInstanceCount = _graveCount;
    }

    public void SetNightGlow(float amount)
    {
        // After dark the fishing boats come in to their moorings and the streets empty.
        if (_wildlife != null) _wildlife.Night = amount > 0.4f;
        _glowMaterial.AlbedoColor = new Color(1f, 0.68f, 0.28f) * Mathf.Clamp(amount, 0.05f, 1f) * 0.4f;
        // By day the unlit glow is near black and stood out as a dark block beside the realistic house models.
        if (_glowNode != null) _glowNode.Visible = amount > 0.15f;
        _lanternMaterial?.Set("albedo_color", new Color(0.35f, 0.33f, 0.28f).Lerp(new Color(1f, 0.86f, 0.55f), Mathf.Clamp(amount * 1.4f, 0, 1)));
        if (_lampPools != null) ((Node3D)GetChild(_lampPoolNode)).Visible = amount > 0.15f;
    }

    /// <summary>Uploads interpolated people and carried goods.</summary>
    public void SyncPeople() => _host.ReadPeople(WritePeople);

    private void WritePeople(PeopleFrame previous, PeopleFrame current, float t)
    {
        int slots = current.Slots;
        Ensure(_people, slots);
        Ensure(_cargo, slots);
        int peopleLength = _people.InstanceCount * 16, cargoLength = _cargo.InstanceCount * 16;
        if (_peopleBuffer.Length != peopleLength) _peopleBuffer = new float[peopleLength];
        if (_cargoBuffer.Length != cargoLength) _cargoBuffer = new float[cargoLength];
        int visible = 0, carried = 0, heroes = 0, barrows = 0;
        Array.Clear(_heroCounts);
        Array.Clear(_toolCounts);
        Array.Clear(_loadCounts);
        foreach (TexturedRole role in _texturedRoles)
            if (role != null) role.Bodies = role.Tools = 0;
        for (int i = 0; i < slots; i++)
        {
            byte kind = current.Kind[i];
            if (kind == 0) continue;
            // A slot freed and reused within one publish (a death then a birth) would streak from the old position.
            float jumpX = current.X[i] - previous.X[i], jumpZ = current.Z[i] - previous.Z[i];
            bool known = previous.Kind[i] != 0 && jumpX * jumpX + jumpZ * jumpZ < 25f;
            float x = known ? previous.X[i] + (current.X[i] - previous.X[i]) * t : current.X[i];
            float y = known ? previous.Y[i] + (current.Y[i] - previous.Y[i]) * t : current.Y[i];
            float z = known ? previous.Z[i] + (current.Z[i] - previous.Z[i]) * t : current.Z[i];
            float scale = kind == 3 ? 0.68f : kind == 4 ? 0.96f : 1f;

            float vx = x - _viewCenter.X, vz = z - _viewCenter.Z;
            if (heroes < HeroCapacity && vx * vx + vz * vz < NearRadius * NearRadius)
            {
                heroes++;
                var clip = (Clip)current.Anim[i];
                (int start, int length) = _kit.Clips[(int)clip];
                float phase = i * 0.61803f % 1f, yaw = current.Yaw[i], sin = MathF.Sin(yaw), cos = MathF.Cos(yaw);
                float size = CharacterScale * scale;
                float shade = 0.9f + (i * 37 % 11) / 100f;
                var tint = new Color(shade, shade * (0.97f + (i % 3) * 0.015f), shade * (0.94f + (i % 4) * 0.02f));
                int variant = current.Variant[i];
                // Women, children and elders keep their own model whatever job they hold.
                int drawn = kind switch { 2 => WomanVariant, 3 => i % 2 == 0 ? LaborerVariant : WomanVariant, 4 => ElderVariant, _ => variant };
                // Straw hats and aprons do not exist yet in the Old Stone Age.
                if (_clothEra == 0 && drawn is FarmerVariant or 6 or 7) drawn = LaborerVariant;
                TexturedRole textured = _texturedRoles[drawn];
                if (textured != null)
                {
                    (int hqStart, int hqLength) = textured.Kit.ClipFor(clip);
                    float aimed = clip == Clip.Chop ? yaw - CharacterKit.Textured.ChopAim : yaw;
                    float aimSin = MathF.Sin(aimed), aimCos = MathF.Cos(aimed);
                    WriteHero(textured.BodyBuffer, textured.Bodies++, size, aimSin, aimCos, x, y, z, tint, hqStart, hqLength, phase);
                    if (textured.Kit.ShowsTool(clip))
                        WriteHero(textured.ToolBuffer, textured.Tools++, size, aimSin, aimCos, x, y, z, Colors.White, hqStart, hqLength, phase);
                }
                else
                {
                    WriteHero(_heroBuffers[variant], _heroCounts[variant]++, size, sin, cos, x, y, z, tint, start, length, phase);
                    int tool = kind is 1 or 2 ? CharacterKit.ToolFor(clip) : -1;
                    if (tool >= 0) WriteHero(_toolBuffers[tool], _toolCounts[tool]++, size, sin, cos, x, y, z, Colors.White, start, length, phase);
                }
                if (current.Carry[i] > 0)
                {
                    // From the Middle Ages adults push their loads in a wheelbarrow; the goods ride in its tray.
                    bool barrow = _clothEra >= 4 && kind is 1 or 2;
                    float s = CharacterScale * scale, ahead = barrow ? 0.55f * s : 0, drop = barrow ? 0.4f * s : 0;
                    WriteYawed(_loadBuffers[current.Carry[i] - 1], _loadCounts[current.Carry[i] - 1]++, s, sin, cos, x + sin * ahead, y - drop, z + cos * ahead);
                    if (barrow) WriteYawed(_barrowBuffer, barrows++, s, sin, cos, x, y, z);
                }
                continue;
            }
            Color color = kind == 2 ? new Color(0.72f, 0.36f, 0.34f) : kind == 3 ? new Color(0.82f, 0.72f, 0.5f) : kind == 4 ? new Color(0.68f, 0.66f, 0.62f) : Clothes[i % Clothes.Length];
            Write(_peopleBuffer, visible++, scale, x, y, z, color);
            if (current.Carry[i] > 0)
                Write(_cargoBuffer, carried++, 1, x, y + 1.72f * scale, z, CargoColors[current.Carry[i] - 1]);
        }
        _people.VisibleInstanceCount = 0;
        _people.Buffer = _peopleBuffer;
        _people.VisibleInstanceCount = visible;
        _cargo.VisibleInstanceCount = 0;
        _cargo.Buffer = _cargoBuffer;
        _cargo.VisibleInstanceCount = carried;
        for (int i = 0; i < _heroes.Length; i++) Upload(_heroes[i], _heroBuffers[i], _heroCounts[i]);
        for (int i = 0; i < _tools.Length; i++) Upload(_tools[i], _toolBuffers[i], _toolCounts[i]);
        for (int i = 0; i < _loads.Length; i++) Upload(_loads[i], _loadBuffers[i], _loadCounts[i]);
        Upload(_barrows, _barrowBuffer, barrows);
        foreach (TexturedRole role in _texturedRoles)
        {
            if (role == null) continue;
            Upload(role.Body, role.BodyBuffer, role.Bodies);
            if (role.Tool != null) Upload(role.Tool, role.ToolBuffer, role.Tools);
        }
    }

    private static void WriteYawed(float[] b, int index, float s, float sin, float cos, float x, float y, float z)
    {
        int n = index * 16;
        b[n] = cos * s; b[n + 1] = 0; b[n + 2] = sin * s; b[n + 3] = x;
        b[n + 4] = 0; b[n + 5] = s; b[n + 6] = 0; b[n + 7] = y;
        b[n + 8] = -sin * s; b[n + 9] = 0; b[n + 10] = cos * s; b[n + 11] = z;
        b[n + 12] = 1; b[n + 13] = 1; b[n + 14] = 1; b[n + 15] = 1;
    }

    private static void Upload(MultiMesh multi, float[] buffer, int count)
    {
        if (count > 0) multi.Buffer = buffer;
        multi.VisibleInstanceCount = count;
    }

    private static void WriteHero(float[] b, int index, float s, float sin, float cos, float x, float y, float z, Color color, int start, int length, float phase)
    {
        int n = index * HeroFloats;
        b[n] = cos * s; b[n + 1] = 0; b[n + 2] = sin * s; b[n + 3] = x;
        b[n + 4] = 0; b[n + 5] = s; b[n + 6] = 0; b[n + 7] = y;
        b[n + 8] = -sin * s; b[n + 9] = 0; b[n + 10] = cos * s; b[n + 11] = z;
        b[n + 12] = color.R; b[n + 13] = color.G; b[n + 14] = color.B; b[n + 15] = 1;
        b[n + 16] = start; b[n + 17] = length; b[n + 18] = phase; b[n + 19] = 0;
    }

    private static void Write(float[] b, int index, float scale, float x, float y, float z, Color color)
    {
        int n = index * 16;
        b[n] = scale; b[n + 1] = 0; b[n + 2] = 0; b[n + 3] = x;
        b[n + 4] = 0; b[n + 5] = scale; b[n + 6] = 0; b[n + 7] = y;
        b[n + 8] = 0; b[n + 9] = 0; b[n + 10] = scale; b[n + 11] = z;
        b[n + 12] = color.R; b[n + 13] = color.G; b[n + 14] = color.B; b[n + 15] = 1;
    }
}

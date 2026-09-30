// Normal-mapped cat shader. Everything the shelf/floor cubes get from
// BasicEffect (world/view/projection transforms, key+fill+back directional
// lighting) is replicated here, plus tangent-space normal mapping fed by the
// baked normal map and the mesh's per-vertex tangents. Vertex colors are not
// used — the cat is textured.
//
// Lighting matches BasicEffect's EnableDefaultLighting so the cat sits
// naturally beside the floor/walls, which are still drawn with BasicEffect.
//
// Compiled for vs/ps_3_0: MonoGame's DesktopGL content profile translates
// shaders with MojoShader, which only accepts SM 3.0 or lower (this is why
// the MGCB project template uses vs_3_0/ps_3_0 for OPENGL). SM3 also means
// tex2D rather than Texture2D.Sample. Anisotropic sampling is set from
// Game1 after each pass is applied.

float4x4 World;
float4x4 View;
float4x4 Projection;

sampler2D DiffuseTexture : register(s0);
sampler2D NormalTexture : register(s1);

float3 AmbientColor = float3(0.5f, 0.5f, 0.5f);

// The same three directional lights BasicEffect sets up in
// EnableDefaultLighting. Directions are pre-negated to point toward the light
// (BasicEffect computes dot(N, -Direction)); colors are XNA's defaults. These
// are also pushed from C# — HLSL uniform initializers are not reliably applied
// by the effect framework, and a zeroed light set renders the cat black.
float3 KeyLightDirection = float3(0.5265408f, 0.5735765f, 0.6275069f);
float3 KeyLightColor = float3(1.0f, 0.9607844f, 0.8078432f);

float3 FillLightDirection = float3(-0.7198464f, -0.3420201f, -0.4545195f);
float3 FillLightColor = float3(0.9647059f, 0.9607844f, 0.945098f);

float3 BackLightDirection = float3(-0.4545195f, 0.3420201f, -0.7198464f);
float3 BackLightColor = float3(0.8078432f, 0.8039216f, 0.7647059f);

// Per-critter colour wash. The store has one cat model and one photographic
// diffuse texture, so the differences between species (a pale kitten, a fat
// ginger one) come from tinting it here rather than from separate textures.
// Defaults to white, i.e. no change.
float3 Tint = float3(1.0f, 1.0f, 1.0f);

float SpecularStrength = 0.2f;
float SpecularShininess = 16.0f;
float3 ViewDirection = float3(0.0f, 1.0f, 0.0f); // set per frame by Game1

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 Tangent : TANGENT0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
    float3 WorldNormal : TEXCOORD1;
    float3 WorldTangent : TEXCOORD2;
    float Handedness : TEXCOORD3;
};

VertexShaderOutput MainVS(VertexShaderInput input)
{
    VertexShaderOutput output;

    float4 worldPosition = mul(input.Position, World);
    output.Position = mul(worldPosition, View);
    output.Position = mul(output.Position, Projection);

    output.TexCoord = input.TexCoord;

    output.WorldNormal = mul(input.Normal, (float3x3)World);
    output.WorldTangent = mul(input.Tangent.xyz, (float3x3)World);
    output.Handedness = input.Tangent.w;

    return output;
}

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float3 N = normalize(input.WorldNormal);
    float3 T = normalize(input.WorldTangent);
    T = normalize(T - N * dot(N, T)); // re-orthogonalize after world transform
    float3 B = cross(N, T) * input.Handedness;

    // Baked normal from the height map (see ObjLoader).
    float3 tangentNormal = tex2D(NormalTexture, input.TexCoord).xyz;
    tangentNormal = tangentNormal * 2.0f - 1.0f;
    tangentNormal = normalize(tangentNormal);

    float3 normal = normalize(
        tangentNormal.x * T +
        tangentNormal.y * B +
        tangentNormal.z * N);

    float3 light = AmbientColor;
    light += KeyLightColor * saturate(dot(normal, normalize(KeyLightDirection)));
    light += FillLightColor * saturate(dot(normal, normalize(FillLightDirection)));
    light += BackLightColor * saturate(dot(normal, normalize(BackLightDirection)));

    // Small straight-on sheen, mirroring BasicEffect's SpecularColor look.
    float3 halfVec = normalize(normalize(KeyLightDirection) + normalize(ViewDirection));
    light += KeyLightColor * SpecularStrength *
        pow(saturate(dot(normal, halfVec)), SpecularShininess);

    float4 diffuse = tex2D(DiffuseTexture, input.TexCoord);
    return float4(diffuse.rgb * light * Tint, diffuse.a);
}

technique
{
    pass
    {
        VertexShader = compile vs_3_0 MainVS();
        PixelShader = compile ps_3_0 MainPS();
    }
}
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    // Vertex with a tangent/handedness channel in addition to position,
    // normal, and UV — the tangent basis the normal-mapping shader uses.
    // MonoGame ships no such vertex type (Position+Normal+Texture doesn't
    // carry tangents), so this one is declared here next to the loader that
    // fills it.
    /// <summary>
    /// Vertex with a tangent/handedness channel in addition to position,
    /// normal, and UV — the tangent basis the normal-mapping shader uses.
    /// </summary>
    public struct VertexPositionNormalTextureTangent : IVertexType
    {
        /// <summary>Vertex position in model space.</summary>
        public Vector3 Position;

        /// <summary>Vertex normal.</summary>
        public Vector3 Normal;

        /// <summary>Texture coordinate.</summary>
        public Vector2 TextureCoordinate;

        /// <summary>Tangent (xyz) and handedness (w = +1/-1).</summary>
        public Vector4 Tangent;

        public VertexPositionNormalTextureTangent(Vector3 position, Vector3 normal, Vector2 texCoord)
        {
            Position = position;
            Normal = normal;
            TextureCoordinate = texCoord;
            Tangent = new Vector4(1f, 0f, 0f, 1f);
        }

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(32, VertexElementFormat.Vector4, VertexElementUsage.Tangent, 0));

        VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }

    // A small, purpose-built OBJ/MTL loader — not a general-purpose one.
    // It assumes what this specific export produces: a single material,
    // quad faces, and vt/vn present for every vertex. If you swap in a
    // different OBJ later and it doesn't load, that's the first place to
    // look — this does not handle n-gons beyond quads, multiple materials,
    // or missing normals/UVs.
    //
    // Rendering uses VertexPositionNormalTextureTangent (position, normal,
    // UV, tangent) with a texture instead of the vertex-color scheme the
    // procedural cubes use, since the cat's detail comes from its diffuse
    // texture, not flat color. Tangents are derived at load so the
    // normal-mapped cat shader can build a per-pixel tangent space; the
    // grayscale bump map (map_Bump in the MTL) is baked into a tangent-space
    // normal map the same pass.
    //
    // Animation note: OBJ has no concept of a skeleton/bones — this is a
    // single static pose. StaticMesh below only stores geometry, deliberately
    // with no baked-in world transform, so a future animated format (glTF/
    // FBX with bones) can slot in beside this without reworking how Critter
    // or Game1 consume a mesh: both would expose vertices + a per-frame pose
    // transform, just computed differently under the hood.
    /// <summary>
    /// A parsed mesh: vertices, indices, optional textures, and bounds.
    /// Deliberately has no baked-in world transform, so a future animated
    /// format can slot in beside this without reworking how Critter or Game1
    /// consume a mesh.
    /// </summary>
    public class StaticMesh
    {
        /// <summary>The mesh's vertices.</summary>
        public VertexPositionNormalTextureTangent[] Vertices { get; }

        /// <summary>The mesh's indices (32-bit, no 65,535 vertex cap).</summary>
        public int[] Indices { get; }

        /// <summary>The diffuse texture, if one was loaded.</summary>
        public Texture2D? DiffuseTexture { get; }

        /// <summary>The normal map texture, if one was baked.</summary>
        public Texture2D? NormalMapTexture { get; }

        /// <summary>Bounding info in the mesh's own local space, useful for
        /// auto-scaling/centering when placing it in the world.</summary>
        public Vector3 BoundsMin { get; }

        /// <summary>Bounding info in the mesh's own local space.</summary>
        public Vector3 BoundsMax { get; }

        /// <summary>Creates a new StaticMesh from the given geometry and textures.</summary>
        public StaticMesh(
            VertexPositionNormalTextureTangent[] vertices,
            int[] indices,
            Texture2D? diffuseTexture,
            Texture2D? normalMapTexture)
        {
            Vertices = vertices;
            Indices = indices;
            DiffuseTexture = diffuseTexture;
            NormalMapTexture = normalMapTexture;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            BoundsMin = min;
            BoundsMax = max;
        }

        /// <summary>The mesh's size (BoundsMax - BoundsMin).</summary>
        public Vector3 Size => BoundsMax - BoundsMin;

        /// <summary>The mesh's center point.</summary>
        public Vector3 Center => (BoundsMax + BoundsMin) / 2f;

        /// <summary>
        /// Returns the same geometry with textures attached, sharing the vertex
        /// and index arrays. Lets the parser build a mesh without a
        /// GraphicsDevice (see ObjLoader.ParseGeometry) and the texture upload
        /// happen separately.
        /// </summary>
        public StaticMesh WithTextures(Texture2D? diffuse, Texture2D? normal)
            => new StaticMesh(Vertices, Indices, diffuse, normal);
    }

    /// <summary>
    /// A small, purpose-built OBJ/MTL loader — not a general-purpose one.
    /// It assumes what this specific export produces: a single material,
    /// quad faces, and vt/vn present for every vertex.
    /// </summary>
    public static class ObjLoader
    {
        // Amplitude of the bump effect when baking the grayscale height map
        // into a tangent-space normal map.
        private const float NormalMapStrength = 4f;

        /// <summary>
        /// Loads an OBJ + its referenced diffuse texture (from the MTL,
        /// map_Kd/map_Ka) and bump map (map_Bump) from disk. `objPath` and
        /// texture paths are resolved relative to each other as written in the
        /// files.
        /// </summary>
        public static StaticMesh Load(GraphicsDevice graphicsDevice, string objPath)
        {
            (StaticMesh mesh, string? diffuseFile, string? bumpFile) = ParseCore(objPath);
            string dir = Path.GetDirectoryName(objPath) ?? ".";

            Texture2D? texture = null;
            if (diffuseFile != null)
            {
                string texPath = Path.Combine(dir, diffuseFile);
                if (File.Exists(texPath))
                {
                    using var stream = File.OpenRead(texPath);
                    texture = Texture2D.FromStream(graphicsDevice, stream);
                    texture = TryGenerateMipmaps(graphicsDevice, texture);
                }
            }

            Texture2D? normalMap = null;
            if (bumpFile != null)
            {
                string bumpPath = Path.Combine(dir, bumpFile);
                if (File.Exists(bumpPath))
                {
                    using var stream = File.OpenRead(bumpPath);
                    var heightMap = Texture2D.FromStream(graphicsDevice, stream);
                    normalMap = HeightMapToNormalTexture(heightMap, graphicsDevice, NormalMapStrength);
                }
            }

            return mesh.WithTextures(texture, normalMap);
        }

        /// <summary>
        /// Parses the OBJ into geometry only, with no textures and no
        /// GraphicsDevice involved. The animation rig (CatAnimator) only ever
        /// needs vertices, so this is enough to drive it from a tool or a test
        /// outside a graphics context. Use Load for anything that renders.
        /// </summary>
        public static StaticMesh ParseGeometry(string objPath) => ParseCore(objPath).Mesh;

        private readonly record struct ParsedObj(StaticMesh Mesh, string? DiffuseFile, string? BumpFile);

        private static ParsedObj ParseCore(string objPath)
        {
            string dir = Path.GetDirectoryName(objPath) ?? ".";

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            var vertexList = new List<VertexPositionNormalTextureTangent>();
            var indexList = new List<int>();

            // OBJ face indices are 1-based and reference into the shared
            // positions/normals/uvs lists, but MonoGame needs one combined
            // vertex per unique (pos,uv,normal) triple. This dictionary
            // dedupes so shared corners reuse a vertex instead of
            // duplicating (keeps vertex count sane and lets indices work).
            var vertexCache = new Dictionary<(int, int, int), int>();

            string? diffuseTextureFile = null;
            string? bumpTextureFile = null;

            foreach (var rawLine in File.ReadLines(objPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("mtllib "))
                {
                    string mtlFile = line.Substring("mtllib ".Length).Trim();
                    (diffuseTextureFile, bumpTextureFile) = ReadTexturesFromMtl(Path.Combine(dir, mtlFile));
                    continue;
                }

                if (line.StartsWith("v "))
                {
                    var parts = SplitFloats(line, 1);
                    positions.Add(new Vector3(parts[0], parts[1], parts[2]));
                    continue;
                }

                if (line.StartsWith("vn "))
                {
                    var parts = SplitFloats(line, 1);
                    normals.Add(new Vector3(parts[0], parts[1], parts[2]));
                    continue;
                }

                if (line.StartsWith("vt "))
                {
                    var parts = SplitFloats(line, 1);
                    // OBJ v-coordinate is bottom-up; texture coordinates in
                    // MonoGame/DirectX convention are top-down, so flip.
                    uvs.Add(new Vector2(parts[0], 1f - parts[1]));
                    continue;
                }

                if (line.StartsWith("f "))
                {
                    var faceTokens = line.Substring(2).Trim()
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                    var faceVertexIndices = new List<int>();
                    foreach (var token in faceTokens)
                    {
                        var key = ParseFaceVertex(token);
                        if (!vertexCache.TryGetValue(key, out int vIndex))
                        {
                            var pos = positions[key.Item1];
                            var uv = key.Item2 >= 0 ? uvs[key.Item2] : Vector2.Zero;
                            var normal = key.Item3 >= 0 ? normals[key.Item3] : Vector3.Up;

                            vertexList.Add(new VertexPositionNormalTextureTangent(pos, normal, uv));
                            vIndex = vertexList.Count - 1;
                            vertexCache[key] = vIndex;
                        }
                        faceVertexIndices.Add(vIndex);
                    }

                    // Fan-triangulate the face (works for quads and convex
                    // n-gons in general, which covers this export).
                    for (int i = 1; i < faceVertexIndices.Count - 1; i++)
                    {
                        indexList.Add(faceVertexIndices[0]);
                        indexList.Add(faceVertexIndices[i]);
                        indexList.Add(faceVertexIndices[i + 1]);
                    }
                }
            }

            if (vertexList.Count > int.MaxValue)
                throw new InvalidOperationException(
                    $"Mesh has {vertexList.Count} unique vertices, which exceeds int index range. " +
                    "This would require a fundamentally different rendering approach (mesh splitting) " +
                    "rather than a bigger index type.");

            var vertices = vertexList.ToArray();
            var indices = indexList.ToArray();
            ComputeTangents(vertices, indices);

            return new ParsedObj(
                new StaticMesh(vertices, indices, null, null),
                diffuseTextureFile,
                bumpTextureFile);
        }

        // Parses one face-vertex token like "12/5/3", "12//3", or "12" into
        // (positionIndex, uvIndex, normalIndex), 0-based, or -1 if absent.
        private static (int, int, int) ParseFaceVertex(string token)
        {
            var segments = token.Split('/');
            int pos = int.Parse(segments[0], CultureInfo.InvariantCulture) - 1;
            int uv = -1, normal = -1;

            if (segments.Length > 1 && segments[1].Length > 0)
                uv = int.Parse(segments[1], CultureInfo.InvariantCulture) - 1;

            if (segments.Length > 2 && segments[2].Length > 0)
                normal = int.Parse(segments[2], CultureInfo.InvariantCulture) - 1;

            return (pos, uv, normal);
        }

        private static float[] SplitFloats(string line, int skipTokens)
        {
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var result = new float[tokens.Length - skipTokens];
            for (int i = skipTokens; i < tokens.Length; i++)
                result[i - skipTokens] = float.Parse(tokens[i], CultureInfo.InvariantCulture);
            return result;
        }

        // Reads what this loader needs from the MTL: the diffuse map filename
        // (map_Kd, falling back to map_Ka) and the bump map (map_Bump).
        // Everything else in the MTL (Ns, Ni, illum, etc.) is ignored.
        private static (string? diffuse, string? bump) ReadTexturesFromMtl(string mtlPath)
        {
            if (!File.Exists(mtlPath)) return (null, null);

            string? mapKd = null;
            string? mapKa = null;
            string? mapBump = null;

            foreach (var rawLine in File.ReadLines(mtlPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase))
                    mapKd = line.Substring("map_Kd ".Length).Trim();
                else if (line.StartsWith("map_Ka ", StringComparison.OrdinalIgnoreCase))
                    mapKa = line.Substring("map_Ka ".Length).Trim();
                else if (line.StartsWith("map_Bump ", StringComparison.OrdinalIgnoreCase))
                    mapBump = line.Substring("map_Bump ".Length).Trim();
            }

            return (mapKd ?? mapKa, mapBump);
        }

        // Accumulates a tangent+bitangent per triangle, averages them over the
        // shared corners, orthogonally projects the tangent against the normal,
        // and stores handedness in the w channel (so the shader can rebuild B
        // without it being a 4th vertex value).
        private static void ComputeTangents(VertexPositionNormalTextureTangent[] vertices, int[] indices)
        {
            int n = vertices.Length;
            var tangents = new Vector3[n];
            var bitangents = new Vector3[n];

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];

                Vector3 e1 = vertices[i1].Position - vertices[i0].Position;
                Vector3 e2 = vertices[i2].Position - vertices[i0].Position;
                Vector2 du1 = vertices[i1].TextureCoordinate - vertices[i0].TextureCoordinate;
                Vector2 du2 = vertices[i2].TextureCoordinate - vertices[i0].TextureCoordinate;

                float denom = du1.X * du2.Y - du2.X * du1.Y;
                if (MathF.Abs(denom) < 1e-9f) continue; // degenerate UV triangle
                float r = 1f / denom;

                Vector3 t = (e1 * du2.Y - e2 * du1.Y) * r;
                Vector3 b = (e2 * du1.X - e1 * du2.X) * r;

                tangents[i0] += t; bitangents[i0] += b;
                tangents[i1] += t; bitangents[i1] += b;
                tangents[i2] += t; bitangents[i2] += b;
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 normal = vertices[i].Normal;
                if (normal.LengthSquared() < 1e-9f) normal = Vector3.Up;
                normal.Normalize();

                Vector3 tangent = tangents[i] - normal * Vector3.Dot(normal, tangents[i]);
                if (tangent.LengthSquared() < 1e-9f)
                {
                    // No usable tangent (flat UVs); invent one orthogonal to N.
                    tangent = Vector3.Cross(Vector3.Up, normal);
                    if (tangent.LengthSquared() < 1e-9f)
                        tangent = Vector3.Cross(Vector3.Forward, normal);
                }
                tangent.Normalize();

                float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0f ? -1f : 1f;
                vertices[i].Tangent = new Vector4(tangent, handedness);
            }
        }

        // The shipped Cat_bump.jpg is a flat grayscale height map, not a
        // tangent-space normal map, so bake it into one here with central
        // differences (a blue 0,0,1 normal = no displacement). Wrapping
        // address mode at the borders matches the texture's wrap settings.
        private static Texture2D? HeightMapToNormalTexture(Texture2D height, GraphicsDevice device, float strength)
        {
            int w = height.Width, h = height.Height;
            var pixels = new Color[w * h];
            height.GetData(pixels);

            var normals = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float left = pixels[y * w + ((x - 1 + w) % w)].R / 255f;
                    float right = pixels[y * w + (x + 1) % w].R / 255f;
                    float up = pixels[((y - 1 + h) % h) * w + x].R / 255f;
                    float down = pixels[((y + 1) % h) * w + x].R / 255f;

                    float dx = (left - right) * strength;
                    float dy = (up - down) * strength;

                    Vector3 normal = new(-dx, -dy, 1f);
                    normal.Normalize();
                    normals[y * w + x] = new Color(normal * 0.5f + new Vector3(0.5f));
                }
            }

            return CreateMipmappedTexture(device, normals, w, h);
        }

        // Texture2D.FromStream loads a single level, which makes the cat's
        // diffuse alias when it's small on screen (see README). Rebake it into
        // a mipmapped texture so the anisotropic sampler in the cat effect gets
        // proper filtered minification.
        private static Texture2D? TryGenerateMipmaps(GraphicsDevice device, Texture2D? source)
        {
            if (source == null || source.Format != SurfaceFormat.Color)
                return source;

            try
            {
                var pixels = new Color[source.Width * source.Height];
                source.GetData(pixels);
                return CreateMipmappedTexture(device, pixels, source.Width, source.Height);
            }
            catch
            {
                return source;
            }
        }

        // Creates a mipmapped texture with *every* level filled in (box filter
        // down to 1x1). Filling only level 0 leaves the texture mipmap-
        // incomplete, and a mipmapping sampler (the default LinearWrap, or the
        // cat effect's AnisotropicClamp) then samples black for every pixel —
        // the cat renders as a black silhouette. So the whole chain matters.
        private static Texture2D CreateMipmappedTexture(GraphicsDevice device, Color[] level0, int width, int height)
        {
            var texture = new Texture2D(device, width, height, true, SurfaceFormat.Color);
            texture.SetData(0, null, level0, 0, level0.Length);

            var src = level0;
            int sw = width, sh = height;
            for (int level = 1; sw > 1 || sh > 1; level++)
            {
                int dw = Math.Max(1, sw / 2);
                int dh = Math.Max(1, sh / 2);
                var dst = new Color[dw * dh];
                for (int y = 0; y < dh; y++)
                {
                    int sy0 = Math.Min(sh - 1, y * 2);
                    int sy1 = Math.Min(sh - 1, y * 2 + 1);
                    for (int x = 0; x < dw; x++)
                    {
                        int sx0 = Math.Min(sw - 1, x * 2);
                        int sx1 = Math.Min(sw - 1, x * 2 + 1);
                        dst[y * dw + x] = Average(
                            src[sy0 * sw + sx0], src[sy0 * sw + sx1],
                            src[sy1 * sw + sx0], src[sy1 * sw + sx1]);
                    }
                }
                texture.SetData(level, null, dst, 0, dst.Length);
                src = dst; sw = dw; sh = dh;
            }

            return texture;
        }

        private static Color Average(Color a, Color b, Color c, Color d)
        {
            return new Color(
                (a.R + b.R + c.R + d.R) / 4,
                (a.G + b.G + c.G + d.G) / 4,
                (a.B + b.B + c.B + d.B) / 4,
                (a.A + b.A + c.A + d.A) / 4);
        }
    }
}
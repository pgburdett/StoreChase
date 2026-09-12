using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    // A small, purpose-built OBJ/MTL loader — not a general-purpose one.
    // It assumes what this specific export produces: a single material,
    // quad faces, and vt/vn present for every vertex. If you swap in a
    // different OBJ later and it doesn't load, that's the first place to
    // look — this does not handle n-gons beyond quads, multiple materials,
    // or missing normals/UVs.
    //
    // Rendering uses VertexPositionNormalTexture (position, normal, UV) with
    // a texture instead of the vertex-color scheme the procedural cubes use,
    // since the cat's detail comes from its diffuse texture, not flat color.
    //
    // Animation note: OBJ has no concept of a skeleton/bones — this is a
    // single static pose. StaticMesh below only stores geometry, deliberately
    // with no baked-in world transform, so a future animated format (glTF/
    // FBX with bones) can slot in beside this without reworking how Critter
    // or Game1 consume a mesh: both would expose vertices + a per-frame pose
    // transform, just computed differently under the hood.
    public class StaticMesh
    {
        public VertexPositionNormalTexture[] Vertices { get; }
        public int[] Indices { get; }
        public Texture2D? DiffuseTexture { get; }

        // Bounding info in the mesh's own local space, useful for
        // auto-scaling/centering when placing it in the world.
        public Vector3 BoundsMin { get; }
        public Vector3 BoundsMax { get; }

        public StaticMesh(VertexPositionNormalTexture[] vertices, int[] indices, Texture2D? diffuseTexture)
        {
            Vertices = vertices;
            Indices = indices;
            DiffuseTexture = diffuseTexture;

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

        public Vector3 Size => BoundsMax - BoundsMin;
        public Vector3 Center => (BoundsMax + BoundsMin) / 2f;
    }

    public static class ObjLoader
    {
        // Loads an OBJ + its referenced diffuse texture (from the MTL,
        // map_Kd/map_Ka) from disk. `objPath` and texture paths are resolved
        // relative to each other as written in the files.
        public static StaticMesh Load(GraphicsDevice graphicsDevice, string objPath)
        {
            string dir = Path.GetDirectoryName(objPath) ?? ".";

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            var vertexList = new List<VertexPositionNormalTexture>();
            var indexList = new List<int>();

            // OBJ face indices are 1-based and reference into the shared
            // positions/normals/uvs lists, but MonoGame needs one combined
            // vertex per unique (pos,uv,normal) triple. This dictionary
            // dedupes so shared corners reuse a vertex instead of
            // duplicating (keeps vertex count sane and lets indices work).
            var vertexCache = new Dictionary<(int, int, int), int>();

            string? diffuseTextureFile = null;

            foreach (var rawLine in File.ReadLines(objPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("mtllib "))
                {
                    string mtlFile = line.Substring("mtllib ".Length).Trim();
                    diffuseTextureFile = ReadDiffuseTextureFromMtl(Path.Combine(dir, mtlFile));
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

                            vertexList.Add(new VertexPositionNormalTexture(pos, normal, uv));
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

            Texture2D? texture = null;
            if (diffuseTextureFile != null)
            {
                string texPath = Path.Combine(dir, diffuseTextureFile);
                if (File.Exists(texPath))
                {
                    using var stream = File.OpenRead(texPath);
                    texture = Texture2D.FromStream(graphicsDevice, stream);
                }
            }

            if (vertexList.Count > int.MaxValue)
                throw new InvalidOperationException(
                    $"Mesh has {vertexList.Count} unique vertices, which exceeds int index range. " +
                    "This would require a fundamentally different rendering approach (mesh splitting) " +
                    "rather than a bigger index type.");

            return new StaticMesh(vertexList.ToArray(), indexList.ToArray(), texture);
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

        // Only reads what this loader needs from the MTL: the diffuse map
        // filename (map_Kd, falling back to map_Ka if that's what's set).
        // Everything else in the MTL (Ns, Ni, illum, etc.) is ignored.
        private static string? ReadDiffuseTextureFromMtl(string mtlPath)
        {
            if (!File.Exists(mtlPath)) return null;

            string? mapKd = null;
            string? mapKa = null;

            foreach (var rawLine in File.ReadLines(mtlPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("map_Kd ", StringComparison.OrdinalIgnoreCase))
                    mapKd = line.Substring("map_Kd ".Length).Trim();
                else if (line.StartsWith("map_Ka ", StringComparison.OrdinalIgnoreCase))
                    mapKa = line.Substring("map_Ka ".Length).Trim();
            }

            return mapKd ?? mapKa;
        }
    }
}

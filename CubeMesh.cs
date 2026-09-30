using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    // Builds a unit cube (1x1x1, centered on origin in X/Z, sitting on Y=0)
    // as a VertexPositionNormalColorTexture array. Scale it via the world
    // matrix.
    //
    // The u/v repeat arguments are how many times a texture tile is repeated
    // across a face. Because the mesh is a unit cube, the repeats are baked in
    // mesh space and then stretched by whatever world scale is applied, so they
    // describe texel density for one specific size of object. Every shelf in the
    // store is the same size, so one shared mesh works for the whole level
    // (see Game1.LoadContent).
    public static class CubeMesh
    {
        /// <summary>
        /// Builds a unit cube (1x1x1, centered on origin in X/Z, sitting on Y=0).
        /// Scale it via the world matrix.
        /// </summary>
        public static VertexPositionNormalColorTexture[] Build(Color color, float uRepeat = 1f, float vRepeat = 1f)
        {
            // 6 faces * 2 triangles * 3 vertices = 36 verts, not indexed
            // (simplest to read/maintain for a small project like this).
            var v = new VertexPositionNormalColorTexture[36];
            int i = 0;

            // Local X/Z span [-0.5, 0.5] and Y spans [0, 1], so these
            // normalize to 0..1 before the repeat is applied. Which axis feeds
            // u/v depends on the face, hence the separate accessors.
            float Ux(float x) => (x + 0.5f) * uRepeat;
            float Uz(float z) => (z + 0.5f) * uRepeat;
            float Vy(float y) => y * vRepeat;
            float Vz(float z) => (z + 0.5f) * uRepeat;

            Vector2 Uv(float u, float v) => new(u, v);

            void Face(Vector3 normal, Vector3 a, Vector2 ua, Vector3 b, Vector2 ub,
                      Vector3 c, Vector2 uc, Vector3 d, Vector2 ud)
            {
                v[i++] = new VertexPositionNormalColorTexture(a, normal, color, ua);
                v[i++] = new VertexPositionNormalColorTexture(b, normal, color, ub);
                v[i++] = new VertexPositionNormalColorTexture(c, normal, color, uc);
                v[i++] = new VertexPositionNormalColorTexture(a, normal, color, ua);
                v[i++] = new VertexPositionNormalColorTexture(c, normal, color, uc);
                v[i++] = new VertexPositionNormalColorTexture(d, normal, color, ud);
            }

            // Cube spans X:[-0.5,0.5], Z:[-0.5,0.5], Y:[0,1] (sits on the floor)
            var p000 = new Vector3(-0.5f, 0f, -0.5f);
            var p100 = new Vector3(0.5f, 0f, -0.5f);
            var p110 = new Vector3(0.5f, 1f, -0.5f);
            var p010 = new Vector3(-0.5f, 1f, -0.5f);
            var p001 = new Vector3(-0.5f, 0f, 0.5f);
            var p101 = new Vector3(0.5f, 0f, 0.5f);
            var p111 = new Vector3(0.5f, 1f, 0.5f);
            var p011 = new Vector3(-0.5f, 1f, 0.5f);

            // Front/back: u runs along X, v along Y.
            Face(Vector3.Backward, p000, Uv(Ux(p000.X), Vy(p000.Y)),
                 p010, Uv(Ux(p010.X), Vy(p010.Y)),
                 p110, Uv(Ux(p110.X), Vy(p110.Y)),
                 p100, Uv(Ux(p100.X), Vy(p100.Y)));
            Face(Vector3.Forward, p101, Uv(Ux(p101.X), Vy(p101.Y)),
                 p111, Uv(Ux(p111.X), Vy(p111.Y)),
                 p011, Uv(Ux(p011.X), Vy(p011.Y)),
                 p001, Uv(Ux(p001.X), Vy(p001.Y)));

            // Left/right: u runs along Z, v along Y.
            Face(Vector3.Left, p001, Uv(Uz(p001.Z), Vy(p001.Y)),
                 p011, Uv(Uz(p011.Z), Vy(p011.Y)),
                 p010, Uv(Uz(p010.Z), Vy(p010.Y)),
                 p000, Uv(Uz(p000.Z), Vy(p000.Y)));
            Face(Vector3.Right, p100, Uv(Uz(p100.Z), Vy(p100.Y)),
                 p110, Uv(Uz(p110.Z), Vy(p110.Y)),
                 p111, Uv(Uz(p111.Z), Vy(p111.Y)),
                 p101, Uv(Uz(p101.Z), Vy(p101.Y)));

            // Top/bottom: u runs along X, v along Z. The depth axis reuses
            // uRepeat because a shelf is as deep as it is wide.
            Face(Vector3.Up, p010, Uv(Ux(p010.X), Vz(p010.Z)),
                 p011, Uv(Ux(p011.X), Vz(p011.Z)),
                 p111, Uv(Ux(p111.X), Vz(p111.Z)),
                 p110, Uv(Ux(p110.X), Vz(p110.Z)));
            Face(Vector3.Down, p000, Uv(Ux(p000.X), Vz(p000.Z)),
                 p100, Uv(Ux(p100.X), Vz(p100.Z)),
                 p101, Uv(Ux(p101.X), Vz(p101.Z)),
                 p001, Uv(Ux(p001.X), Vz(p001.Z)));

            return v;
        }

        /// <summary>
        /// A flat quad on the XZ plane, for the floor. Sits at Y=0. The repeats
        /// are how many texture tiles span the whole quad, so a caller that wants
        /// one tile every N world units divides the extent by N.
        /// </summary>
        public static VertexPositionNormalColorTexture[] BuildFloorQuad(
            Color color, float width, float depth, float uRepeat, float vRepeat)
        {
            var v = new VertexPositionNormalColorTexture[6];
            var n = Vector3.Up;
            var p00 = new Vector2(0, 0);
            var p10 = new Vector2(width, 0);
            var p11 = new Vector2(width, depth);
            var p01 = new Vector2(0, depth);

            Vector2 Uv(Vector2 p) => new(p.X / width * uRepeat, p.Y / depth * vRepeat);

            v[0] = new VertexPositionNormalColorTexture(new Vector3(p00.X, 0, p00.Y), n, color, Uv(p00));
            v[1] = new VertexPositionNormalColorTexture(new Vector3(p01.X, 0, p01.Y), n, color, Uv(p01));
            v[2] = new VertexPositionNormalColorTexture(new Vector3(p11.X, 0, p11.Y), n, color, Uv(p11));
            v[3] = new VertexPositionNormalColorTexture(new Vector3(p00.X, 0, p00.Y), n, color, Uv(p00));
            v[4] = new VertexPositionNormalColorTexture(new Vector3(p11.X, 0, p11.Y), n, color, Uv(p11));
            v[5] = new VertexPositionNormalColorTexture(new Vector3(p10.X, 0, p10.Y), n, color, Uv(p10));
            return v;
        }
    }

    // MonoGame doesn't ship a Position+Normal+Color vertex, so we declare one
    // with a texture coordinate on top. Normal is needed for BasicEffect
    // lighting to shade the cube faces; TexCoord is what lets the store's cubes
    // be textured (see StoreTextures).
    //
    // This is the ONLY vertex type the shared BasicEffect draws. Every mesh
    // that goes through DrawMesh has to use it so they all agree on one vertex
    // declaration — including the ones that sample a flat white texture and
    // rely on vertex color alone (the player). A Position+Normal+Color vertex
    // used to live here; it was removed rather than kept around, because mixing
    // the two under one effect means sampling a texture with an uninitialised
    // TEXCOORD.
    /// <summary>
    /// The one vertex type the shared BasicEffect draws. Every mesh that goes
    /// through DrawMesh has to use it so they all agree on one vertex
    /// declaration — including the ones that sample a flat white texture and
    /// rely on vertex color alone (the player).
    /// </summary>
    public struct VertexPositionNormalColorTexture : IVertexType
    {
        /// <summary>Vertex position in model space.</summary>
        public Vector3 Position;

        /// <summary>Vertex normal (for BasicEffect lighting).</summary>
        public Vector3 Normal;

        /// <summary>Vertex color (multiplied with the texture).</summary>
        public Color Color;

        /// <summary>Texture coordinate.</summary>
        public Vector2 TexCoord;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
        );

        public VertexPositionNormalColorTexture(Vector3 position, Vector3 normal, Color color, Vector2 texCoord)
        {
            Position = position;
            Normal = normal;
            Color = color;
            TexCoord = texCoord;
        }

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}

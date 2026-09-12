using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace StoreChase
{
    // Builds a unit cube (1x1x1, centered on origin in X/Z, sitting on Y=0)
    // as a VertexPositionNormalColor array. Scale it via the world matrix.
    // No external model files needed — this is the whole "3D model".
    public static class CubeMesh
    {
        public static VertexPositionNormalColor[] Build(Color color)
        {
            // 6 faces * 2 triangles * 3 vertices = 36 verts, not indexed
            // (simplest to read/maintain for a small project like this).
            var v = new VertexPositionNormalColor[36];
            int i = 0;

            void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                v[i++] = new VertexPositionNormalColor(a, normal, color);
                v[i++] = new VertexPositionNormalColor(b, normal, color);
                v[i++] = new VertexPositionNormalColor(c, normal, color);
                v[i++] = new VertexPositionNormalColor(a, normal, color);
                v[i++] = new VertexPositionNormalColor(c, normal, color);
                v[i++] = new VertexPositionNormalColor(d, normal, color);
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

            Face(Vector3.Backward, p000, p010, p110, p100); // front (-Z)
            Face(Vector3.Forward, p101, p111, p011, p001);  // back (+Z)
            Face(Vector3.Left, p001, p011, p010, p000);     // left (-X)
            Face(Vector3.Right, p100, p110, p111, p101);    // right (+X)
            Face(Vector3.Up, p010, p011, p111, p110);       // top
            Face(Vector3.Down, p000, p100, p101, p001);     // bottom

            return v;
        }

        // A flat quad on the XZ plane, for the floor. Sits at Y=0.
        public static VertexPositionNormalColor[] BuildFloorQuad(Color color, float width, float depth)
        {
            var v = new VertexPositionNormalColor[6];
            var n = Vector3.Up;
            var p00 = new Vector3(0, 0, 0);
            var p10 = new Vector3(width, 0, 0);
            var p11 = new Vector3(width, 0, depth);
            var p01 = new Vector3(0, 0, depth);

            v[0] = new VertexPositionNormalColor(p00, n, color);
            v[1] = new VertexPositionNormalColor(p01, n, color);
            v[2] = new VertexPositionNormalColor(p11, n, color);
            v[3] = new VertexPositionNormalColor(p00, n, color);
            v[4] = new VertexPositionNormalColor(p11, n, color);
            v[5] = new VertexPositionNormalColor(p10, n, color);
            return v;
        }
    }

    // MonoGame doesn't ship a Position+Normal+Color vertex, so we declare one.
    // Normal is needed for BasicEffect lighting to shade the cube faces.
    public struct VertexPositionNormalColor : IVertexType
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Color Color;

        public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0)
        );

        public VertexPositionNormalColor(Vector3 position, Vector3 normal, Color color)
        {
            Position = position;
            Normal = normal;
            Color = color;
        }

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}
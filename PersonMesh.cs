using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace StoreChase
{
    // One piece of the procedural humanoid. Vertices are authored in the part's
    // own space with the limb joint (shoulder/hip) at the origin and the limb
    // hanging toward -Y; Pivot is where that joint sits in the body's space, and
    // SwingSign controls which limb swings forward when the walk phase is +.
    // Static parts (torso/head) have SwingSign 0 and Pivot zero.
    public readonly record struct PersonPart(VertexPositionNormalColorTexture[] Vertices, Vector3 Pivot, float SwingSign);
    // Builds a procedural humanoid (head, torso, arms, legs) out of
    // VertexPositionNormalColorTexture cuboids, replacing the old player cube. Part
    // geometry is baked once; per-frame limb swing is applied by rotating each
    // limb around its joint (see Game1.DrawPlayer) — all purely procedural.
    public static class PersonMesh
    {
        /// <summary>
        /// Builds the procedural humanoid (head, torso, arms, legs) as an array
        /// of <see cref="PersonPart"/>s. Part geometry is baked once; per-frame
        /// limb swing is applied by rotating each limb around its joint
        /// (see Game1.DrawPlayer).
        /// </summary>
        public static PersonPart[] Build()
        {
            Color skin = new(236, 196, 156);
            Color cloth = Color.DodgerBlue;   // keep the player's signature color
            Color pants = new(58, 62, 74);
            Color shoes = new(38, 38, 42);

            return new[]
            {
                // Torso.
                new PersonPart(BuildBox(new Vector3(0f, 1.5f, 0f), new Vector3(0.52f, 1f, 0.32f), cloth),
                    Vector3.Zero, 0f),

                // Head, biased slightly toward +Z so the "face" direction reads.
                new PersonPart(BuildBox(new Vector3(0f, 2.2f, 0.02f), new Vector3(0.36f, 0.34f, 0.34f), skin),
                    Vector3.Zero, 0f),

                // Arms hang from the shoulders at the top of the torso.
                new PersonPart(BuildBox(new Vector3(-0.42f, 1.45f, 0f) - new Vector3(-0.42f, 1.85f, 0f), new Vector3(0.14f, 0.8f, 0.14f), cloth),
                    new Vector3(-0.42f, 1.85f, 0f),  1f),
                new PersonPart(BuildBox(new Vector3(0.42f, 1.45f, 0f) - new Vector3(0.42f, 1.85f, 0f), new Vector3(0.14f, 0.8f, 0.14f), cloth),
                    new Vector3(0.42f, 1.85f, 0f), -1f),

                // Legs hang from the hips down to the floor, shoes baked on.
                new PersonPart(BuildLeg(),
                    new Vector3(-0.15f, 1f, 0f), -1f),
                new PersonPart(BuildLeg(),
                    new Vector3(0.15f, 1f, 0f),  1f),
            };
        }

        // A leg in hip-relative space: hip at origin, leg from 0 to -1 in Y,
        // with a shoe box at the bottom (forward offset in +Z).
        private static VertexPositionNormalColorTexture[] BuildLeg()
        {
            Color pants = new(58, 62, 74);
            Color shoes = new(38, 38, 42);

            var v = new List<VertexPositionNormalColorTexture>();
            AddBox(v, new Vector3(0f, -0.5f, 0f), new Vector3(0.18f, 1f, 0.18f), pants);
            AddBox(v, new Vector3(0f, -0.95f, 0.07f), new Vector3(0.2f, 0.12f, 0.3f), shoes);
            return v.ToArray();
        }

        // Builds a single cuboid as a standalone mesh (baked at the given center).
        private static VertexPositionNormalColorTexture[] BuildBox(Vector3 center, Vector3 size, Color color)
        {
            var v = new List<VertexPositionNormalColorTexture>();
            AddBox(v, center, size, color);
            return v.ToArray();
        }

        private static void AddBox(List<VertexPositionNormalColorTexture> v, Vector3 center, Vector3 size, Color color)
        {
            float hx = size.X * 0.5f;
            float hy = size.Y * 0.5f;
            float hz = size.Z * 0.5f;

            var p000 = new Vector3(center.X - hx, center.Y - hy, center.Z - hz);
            var p100 = new Vector3(center.X + hx, center.Y - hy, center.Z - hz);
            var p110 = new Vector3(center.X + hx, center.Y + hy, center.Z - hz);
            var p010 = new Vector3(center.X - hx, center.Y + hy, center.Z - hz);
            var p001 = new Vector3(center.X - hx, center.Y - hy, center.Z + hz);
            var p101 = new Vector3(center.X + hx, center.Y - hy, center.Z + hz);
            var p111 = new Vector3(center.X + hx, center.Y + hy, center.Z + hz);
            var p011 = new Vector3(center.X - hx, center.Y + hy, center.Z + hz);

            // The player is drawn with a BasicEffect, so its vertices carry UVs
            // like every other mesh that shares that effect — but the person
            // never samples the store's surface textures. Every face points at
            // the middle of the flat white 1x1 (see StoreTextures.Flat), so the
            // player is shaded entirely by its vertex color as before.
            void Face(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                // Wound at build time (see CubeMesh.Build's Face): the game
                // culls any triangle whose cross(v1-v0, v2-v0) · normal is not
                // negative, and hand-ordered corners had four of the person's
                // six faces backwards — the player rendered as a hollow shell
                // you could see straight through.
                var white = new Vector2(0.5f, 0.5f);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
                {
                    v.Add(new VertexPositionNormalColorTexture(a, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(b, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(c, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(a, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(c, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(d, normal, color, white));
                }
                else
                {
                    v.Add(new VertexPositionNormalColorTexture(a, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(c, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(b, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(a, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(d, normal, color, white));
                    v.Add(new VertexPositionNormalColorTexture(c, normal, color, white));
                }
            }

            Face(Vector3.Backward, p000, p010, p110, p100); // front (-Z)
            Face(Vector3.Forward, p101, p111, p011, p001);  // back (+Z)
            Face(Vector3.Left, p001, p011, p010, p000);     // left (-X)
            Face(Vector3.Right, p100, p110, p111, p101);    // right (+X)
            Face(Vector3.Up, p010, p011, p111, p110);       // top
            Face(Vector3.Down, p000, p100, p101, p001);     // bottom
        }
    }
}
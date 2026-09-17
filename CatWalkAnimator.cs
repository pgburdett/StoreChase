using System;
using Microsoft.Xna.Framework;

namespace StoreChase
{
    // Procedurally walks the cat's legs. The cat is a single rigid OBJ with no
    // skeleton, so there are no bones to drive. Instead this finds the four leg
    // vertex clusters from the mesh's own geometry — vertices in the low
    // vertical band, split left/right by X and nose/tail by Y — then each frame
    // rotates those vertices around their hip pivot with a diagonal ("trot")
    // gait. Weight blends to zero near the hip (and radially out from each leg
    // centroid) so the body doesn't tear or deform at the joint. No animation
    // assets or extra content.
    public sealed class CatWalkAnimator
    {
        // All thresholds are fractions of the model's raw height, so this
        // adapts to a re-exported/rescaled cat rather than hardcoding units
        // (the shipped model is ~37.6 raw units tall).
        private const float LegBandFraction = 0.20f;    // bottom 20% = paws/lower legs
        private const float HipHeightFraction = 0.45f;  // hip sits at 45% of height
        private const float BlendBandFraction = 0.12f;  // hip-to-body blend thickness
        private const float RadialInnerFactor = 0.55f;  // full weight within this * leg radius
        private const float RadialOuterFactor = 1.15f;  // weight reaches 0 by this * leg radius
        private const float SwingAmplitude = 0.38f;     // radians
        private const float WalkCycleSpeed = 10f;       // radians/sec while moving

        private struct Leg
        {
            public Vector3 Hip;
            public float Phase; // +1 / -1 so diagonal pairs move opposite
        }

        private readonly StaticMesh _mesh;
        private readonly Vector3[] _basePositions;
        private readonly Vector3[] _baseNormals;
        private readonly Vector3[] _baseTangents;
        private readonly Leg[] _legs;
        private readonly int[] _legFor;   // per-vertex leg index
        private readonly float[] _weight; // per-vertex 0..1
        private float _phase;

        public CatWalkAnimator(StaticMesh mesh)
        {
            _mesh = mesh;
            int n = mesh.Vertices.Length;
            _basePositions = new Vector3[n];
            _baseNormals = new Vector3[n];
            _baseTangents = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                _basePositions[i] = mesh.Vertices[i].Position;
                _baseNormals[i] = mesh.Vertices[i].Normal;
                Vector4 t = mesh.Vertices[i].Tangent;
                _baseTangents[i] = new Vector3(t.X, t.Y, t.Z);
            }

            (_legs, _legFor, _weight) = AnalyzeLegs(_basePositions);
        }

        public void Update(float dt, bool moving)
        {
            // Advance while walking, ease back to a neutral pose when idle so
            // the legs settle instead of freezing mid-step.
            _phase = moving
                ? _phase + dt * WalkCycleSpeed
                : MathHelper.Lerp(_phase, 0f, 1f - MathF.Exp(-6f * dt));

            float swing = MathF.Sin(_phase) * SwingAmplitude;

            for (int i = 0; i < _weight.Length; i++)
            {
                float w = _weight[i];
                if (w <= 0f) continue;

                var leg = _legs[_legFor[i]];
                float theta = swing * leg.Phase * w;
                float c = MathF.Cos(theta);
                float s = MathF.Sin(theta);

                Vector3 d = _basePositions[i] - leg.Hip;
                _mesh.Vertices[i].Position = leg.Hip + new Vector3(d.X, d.Y * c - d.Z * s, d.Y * s + d.Z * c);

                // Rotate the normal and tangent by the same swing so the
                // normal-mapped lighting follows the leg instead of staying
                // frozen in the base pose.
                float w4 = _mesh.Vertices[i].Tangent.W;
                Vector3 n = _baseNormals[i];
                Vector3 nr = new(n.X, n.Y * c - n.Z * s, n.Y * s + n.Z * c);
                if (nr.LengthSquared() > 1e-6f) nr.Normalize();
                _mesh.Vertices[i].Normal = nr;

                Vector3 tg = _baseTangents[i];
                Vector3 tr = new(tg.X, tg.Y * c - tg.Z * s, tg.Y * s + tg.Z * c);
                if (tr.LengthSquared() > 1e-6f) tr.Normalize();
                _mesh.Vertices[i].Tangent = new Vector4(tr, w4);
            }
        }

        // Derives the four legs and per-vertex skin weights from the mesh's raw
        // positions. Leg index order: 0=front-left, 1=front-right, 2=back-left,
        // 3=back-right (front = the nose/min-Y end, per the model's axes).
        private static (Leg[] legs, int[] legFor, float[] weight) AnalyzeLegs(Vector3[] positions)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            float floorZ = min.Z;
            float height = MathF.Max(0.0001f, max.Z - min.Z);
            float bandTop = floorZ + height * LegBandFraction;
            float hipZ = floorZ + height * HipHeightFraction;
            float blend = MathF.Max(0.0001f, height * BlendBandFraction);

            // Split front/back at the midpoint of the low band's Y span; the
            // four paw clusters straddle this with a clear gap.
            float lowYMin = float.MaxValue, lowYMax = float.MinValue;
            foreach (var p in positions)
            {
                if (p.Z > bandTop) continue;
                lowYMin = MathF.Min(lowYMin, p.Y);
                lowYMax = MathF.Max(lowYMax, p.Y);
            }
            float ySplit = (lowYMin + lowYMax) * 0.5f;

            // Mean X/Y per leg (from the low band) becomes its hip pivot.
            var sum = new Vector3[4];
            var count = new int[4];
            foreach (var p in positions)
            {
                if (p.Z > bandTop) continue;
                int idx = LegIndex(p.X, p.Y, ySplit);
                sum[idx] += p;
                count[idx]++;
            }

            var legRadius = new float[4];
            for (int i = 0; i < 4; i++)
            {
                float r = 0f;
                foreach (var p in positions)
                {
                    if (p.Z > bandTop || LegIndex(p.X, p.Y, ySplit) != i) continue;
                    Vector3 c = sum[i] / MathF.Max(1f, count[i]);
                    r = MathF.Max(r, new Vector2(p.X - c.X, p.Y - c.Y).Length());
                }
                legRadius[i] = MathF.Max(0.0001f, r);
            }

            var legs = new Leg[4];
            var phases = new float[] { 1f, -1f, -1f, 1f }; // FL+BR together, FR+BL opposite
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = sum[i] / MathF.Max(1f, count[i]);
                legs[i] = new Leg { Hip = new Vector3(c.X, c.Y, hipZ), Phase = phases[i] };
            }

            int n = positions.Length;
            var legFor = new int[n];
            var weight = new float[n];
            for (int i = 0; i < n; i++)
            {
                var p = positions[i];
                if (p.Z >= hipZ) { legFor[i] = -1; continue; }

                int li = LegIndex(p.X, p.Y, ySplit);
                legFor[i] = li;

                float zWeight = SmoothStep((hipZ - p.Z) / blend);

                Vector3 c = sum[li] / MathF.Max(1f, count[li]);
                float dist = new Vector2(p.X - c.X, p.Y - c.Y).Length();
                float rInner = legRadius[li] * RadialInnerFactor;
                float rOuter = legRadius[li] * RadialOuterFactor;
                float radial = rOuter > rInner ? 1f - SmoothStep((dist - rInner) / (rOuter - rInner)) : 1f;

                weight[i] = zWeight * radial;
            }

            return (legs, legFor, weight);
        }

        private static int LegIndex(float x, float y, float ySplit)
        {
            bool right = x >= 0f;
            bool back = y >= ySplit;
            if (!back) return right ? 1 : 0;
            return right ? 3 : 2;
        }

        private static float SmoothStep(float t)
        {
            t = MathHelper.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
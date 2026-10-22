using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StoreChase;

namespace StoreChase.Harness;

/// <summary>
/// A software measurement rig for the cat.
///
/// The rig rewrites the model's vertices in place every frame, and a broken
/// blend shears the mesh apart. Reading the source cannot tell you that — the
/// first pass of the pose table looked plausible in code and was unusable on
/// screen. So this drives the rig offscreen and measures the result.
///
/// The measurement is *edge stretch*: for every edge in the mesh, the ratio of
/// its length in the posed frame to its length in the rest pose. A rigid
/// rotation preserves length (ratio 1). A region being rotated by a fraction
/// of an angle stretches the edges across the blend band, and the ratio grows
/// with how much of the mesh is caught mid-rotation.
///
/// Edges that matter are the long ones — a short edge inside a blend band
/// stretching to 3x is invisible on screen. So the threshold only applies to
/// edges whose rest length is above a floor, and the report is broken down by
/// body region so a problem can be attributed to the head, the tail, a leg or
/// the torso.
///
/// No GraphicsDevice is involved: the OBJ is parsed with
/// <see cref="ObjLoader.ParseGeometry"/>, which is deliberately device-free.
/// </summary>
public static class RigCheck
{
    /// <summary>The result of measuring one pose.</summary>
    public sealed class Report
    {
        /// <summary>The pose that was measured.</summary>
        public string Pose { get; init; } = "";

        /// <summary>Worst stretch ratio seen on a long edge, per region.</summary>
        public Dictionary<string, float> WorstByRegion { get; } = new();

        /// <summary>How many long edges were measured in each region.</summary>
        public Dictionary<string, int> EdgeCount { get; } = new();

        /// <summary>The single worst ratio across the whole mesh.</summary>
        public float WorstOverall { get; set; }

        /// <summary>Which region the worst ratio came from.</summary>
        public string WorstRegion { get; set; } = "";

        /// <summary>Edges over the tear threshold.</summary>
        public int TearingCount { get; set; }
    }

    // An edge shorter than this is inside a blend band or a tessellation seam;
    // stretching one of those is invisible on screen, so they don't count.
    private const float MinRestEdgeLength = 0.5f;

    // Past this ratio, a visible edge is considered torn.
    private const float TearThreshold = 2.5f;

    /// <summary>Measures every pose in the rig against the rest pose.</summary>
    public static List<Report> MeasureAllPoses(string objPath)
    {
        var reports = new List<Report>();

        // Each pose gets a *fresh* mesh. The rig rewrites the vertices in
        // place, so sharing one mesh between poses would measure every pose
        // against the previous pose's deformed geometry — which compounds and
        // reports stretching that isn't there.
        foreach (CatPose pose in Enum.GetValues<CatPose>())
            reports.Add(Measure(ObjLoader.ParseGeometry(objPath), pose));

        return reports;
    }

    /// <summary>Measures one pose against the rest pose.</summary>
    public static Report Measure(StaticMesh mesh, CatPose pose)
    {
        var report = new Report { Pose = pose.ToString() };

        // The edge list is built from the mesh topology, so it is identical
        // before and after the rig runs. That means the rest lengths and the
        // posed lengths are indexed by the same edge, which is the whole
        // measurement.
        var edges = BuildEdges(mesh.Indices);

        var restLengths = new float[edges.Count];
        var regions = new string[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            var (a, b) = edges[i];
            restLengths[i] = Vector3.Distance(mesh.Vertices[a].Position, mesh.Vertices[b].Position);
            regions[i] = ClassifyRegion(mesh.Vertices, a, b);
        }

        // Drive the rig into the pose. The pose is applied by easing, so it
        // needs enough frames to settle; 200 frames of easing is well past
        // the point where the exponential blend has converged.
        var animator = new CatAnimator(mesh);
        for (int i = 0; i < 200; i++)
        {
            animator.Update(new CatAnimInput(
                Pose: pose,
                IsMoving: false,
                HasLookTarget: false,
                LookYaw: 0f,
                LookPitch: 0f,
                Dt: 1f / 60f));
        }

        for (int e = 0; e < edges.Count; e++)
        {
            float rest = restLengths[e];
            if (rest < MinRestEdgeLength) continue;

            var (a, b) = edges[e];
            float posed = Vector3.Distance(mesh.Vertices[a].Position, mesh.Vertices[b].Position);
            float ratio = posed / rest;

            string region = regions[e];
            report.EdgeCount.TryGetValue(region, out int n);
            report.EdgeCount[region] = n + 1;

            if (!report.WorstByRegion.TryGetValue(region, out float worst) || ratio > worst)
                report.WorstByRegion[region] = ratio;

            if (ratio > report.WorstOverall)
            {
                report.WorstOverall = ratio;
                report.WorstRegion = region;
            }

            if (ratio > TearThreshold) report.TearingCount++;
        }

        return report;
    }

    /// <summary>
    /// Every unique edge in the mesh, in a stable order.
    ///
    /// Edges are deduplicated by vertex-index pair, so a triangle pair that
    /// shares an edge counts once. Otherwise interior edges (which appear
    /// twice) would outweigh the boundary edges that actually show on screen.
    /// </summary>
    private static List<(int a, int b)> BuildEdges(int[] indices)
    {
        var seen = new HashSet<(int, int)>();
        var edges = new List<(int, int)>();

        void Add(int a, int b)
        {
            if (a == b) return;
            var key = a < b ? (a, b) : (b, a);
            if (seen.Add(key)) edges.Add((a, b));
        }

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Add(indices[i], indices[i + 1]);
            Add(indices[i + 1], indices[i + 2]);
            Add(indices[i + 2], indices[i]);
        }

        return edges;
    }

    /// <summary>
    /// Labels an edge with the body region it belongs to.
    ///
    /// The regions are the ones the rig itself thinks about, derived from the
    /// model's bounding box: the head is the front of the model (high Y on the
    /// nose-to-tail axis), the tail is the thin rear spike, the legs are the
    /// low band, and everything else is the torso.
    /// </summary>
    private static string ClassifyRegion(
        VertexPositionNormalTextureTangent[] verts, int a, int b)
    {
        var bounds = Bounds(verts);
        var mid = (verts[a].Position + verts[b].Position) * 0.5f;

        // Nose-to-tail position, 0 at the nose and 1 at the tail tip.
        float along = (mid.Y - bounds.MinY) / bounds.LengthY;

        // Height up the body, 0 at the floor and 1 at the ears.
        float up = (mid.Z - bounds.MinZ) / bounds.LengthZ;

        if (along < 0.22f) return "head";
        if (along > 0.72f) return "tail";
        if (up < 0.30f) return "legs";
        return "torso";
    }

    private readonly record struct MeshBounds(
        float MinY, float MaxY, float MinZ, float MaxZ, float LengthY, float LengthZ);

    private static MeshBounds Bounds(VertexPositionNormalTextureTangent[] verts)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var v in verts)
        {
            minY = Math.Min(minY, v.Position.Y); maxY = Math.Max(maxY, v.Position.Y);
            minZ = Math.Min(minZ, v.Position.Z); maxZ = Math.Max(maxZ, v.Position.Z);
        }
        return new MeshBounds(minY, maxY, minZ, maxZ,
            MathF.Max(0.0001f, maxY - minY),
            MathF.Max(0.0001f, maxZ - minZ));
    }
}

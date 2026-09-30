using System;
using Microsoft.Xna.Framework;

namespace StoreChase;

// What the cat is doing right now. The animator turns a pose into a full set
// of rig parameters (see CatRigPose) and blends between them, so new
// behaviour is a new enum value plus a block in CatAnimator.PoseTargets
// rather than new vertex math.
/// <summary>
/// What the cat is doing right now. The animator turns a pose into a full set
/// of rig parameters (see CatRigPose) and blends between them, so new
/// behaviour is a new enum value plus a block in CatAnimator.PoseTargets
/// rather than new vertex math.
/// </summary>
public enum CatPose
{
    /// <summary>Standing still, weight even.</summary>
    Stand,

    /// <summary>Walking (gait is driven by IsMoving, not the pose).</summary>
    Walk,

    /// <summary>Sitting: rear down, front up, tail curled round.</summary>
    Sit,

    /// <summary>Grooming: head down toward the front paw.</summary>
    Groom,

    /// <summary>Stretching: front legs forward, rear up, tail straight up.</summary>
    Stretch,

    /// <summary>Loafing: curled up asleep, body down, legs tucked.</summary>
    Loaf,

    /// <summary>Startled: up on the legs, tail bristled.</summary>
    Startle,
}
// One frame of "what should the cat be doing", assembled by Game1. LookYaw is
// relative to the cat's own facing (0 = straight ahead, + = to its right) and
// LookPitch is + for up; the animator smooths toward both, so callers can pass
// raw targets without any easing of their own.
/// <summary>
/// One frame of "what should the cat be doing", assembled by Game1. LookYaw is
/// relative to the cat's own facing (0 = straight ahead, + = to its right) and
/// LookPitch is + for up; the animator smooths toward both, so callers can pass
/// raw targets without any easing of their own.
/// </summary>
public readonly record struct CatAnimInput(
    /// <summary>The desired pose.</summary>
    CatPose Pose,

    /// <summary>Whether the cat is currently moving (drives the gait).</summary>
    bool IsMoving,

    /// <summary>Whether the head should track a target.</summary>
    bool HasLookTarget,

    /// <summary>Head yaw relative to the cat's facing (radians).</summary>
    float LookYaw,

    /// <summary>Head pitch (radians, + for up).</summary>
    float LookPitch,

    /// <summary>Delta time since the last frame (seconds).</summary>
    float Dt);
// Procedurally animates the cat. The OBJ is a single rigid pose with no
// skeleton, so there are no bones to drive; instead this derives a small
// rig from the mesh's own geometry and rotates regions of it every frame:
//
//   legs    four hip-pivot clusters found in the low band (diagonal trot)
//   knees   a second joint partway down each leg, so legs can bend
//   spine   the whole torso rotating about the rear hip
//   head    a neck pivot at the front of the torso
//   tail    a base pivot with an arc that grows toward the tip
//
// Every weight ramps to zero at its joint so regions blend instead of
// tearing, and all thresholds are fractions of the model's own bounding box
// so a re-exported or rescaled cat still works. No animation assets.
public sealed class CatAnimator
{
    // ---- Rig geometry, all as fractions of the model's own bbox --------

    // Vertical (Z, the model's up axis) landmarks. Legs hang from hip to
    // floor; the knee splits that span so a leg can bend rather than swing
    // as one rigid stick.
    private const float LegBandFraction = 0.20f;    // bottom 20% = paws
    private const float HipHeightFraction = 0.45f;  // hip sits at 45% up
    private const float KneeHeightFraction = 0.22f; // knee bends at 22% up
    private const float BlendBandFraction = 0.12f;  // hip-to-body blend
    // A wide knee blend is the single biggest lever against tearing. The knee
    // takes twice the hip's angle, so a narrow band forces a sharp shear where
    // the thigh stops and the shank starts; spreading that difference over more
    // of the leg is what lets a real fold happen on a rigid mesh. Measured on
    // this model, widening it from 10% to 26% of body height cut leg stretch on
    // a sitting pose from ~3.9x to ~1.8x.
    private const float KneeBlendFraction = 0.26f;  // knee-to-shank blend
    private const float RadialInnerFactor = 0.55f;  // full weight within this * leg radius
    private const float RadialOuterFactor = 1.15f;  // weight reaches 0 by this * leg radius

    // Nose-to-tail (Y) landmarks, as a fraction along the model's length
    // measured from the nose. Derived from vertex density: the head is a
    // dense cluster at the low-Y end, the tail is a thin isolated spike
    // past ~0.65, and the torso is sparse between them.
    // Where the skull ends and the neck begins, as a fraction along the body
    // from the nose. From the model's own vertex density, the head is the dense
    // cluster from Y=-22.3 back to about Y=-13 (the ears reach the top of the
    // model at Z=38.6 there) and the neck is the narrow stretch around Y=-11.
    //
    // The blend has to stay TIGHT. A head turn is a rotation about the neck, and
    // every vertex caught part-way through the blend is rotated by a fraction of
    // the angle, so a wide blend shears the mesh apart: measured on this model,
    // a blend spanning 6% of the body length already stretches edges ~2.4x, and
    // a blend reaching the mid-torso drags half the body around with it. The
    // skull should read as one rigid piece, so the ramp covers the neck only.
    private const float HeadFullFraction = 0.145f;  // fully head below this
    private const float HeadZeroFraction = 0.205f;  // fully torso above this
    private const float NeckFraction = 0.185f;      // the rotation axis, inside the blend

    // The head band is additionally gated by height: a vertex must be this far
    // above the leg tops to count as head. The front paws sit at the same Y as
    // the neck, so without this a head turn drags the paws along with it.
    private const float HeadHeightGateFraction = 0.18f;

    // The tail band is gated by width instead: the tail is a thin spike, so
    // anything much wider than this at tail-Y is a haunch and must not be swept
    // into the tail arc.
    private const float TailWidthFraction = 0.30f;   // of the body's half-width

    // The tail's arc grows toward the tip. Capping it matters: the tail is long
    // (a third of the body), so a large tip rotation swings the tip through a
    // big arc while the base barely moves, and the stretch across the blend
    // grows with it. A 0.55 rad tail-up is already a clearly "bristled" read.
    private const float TailCurlMax = 0.55f;
    private const float TailSwayMax = 0.45f;

    // How far a bristled tail inflates, as a fraction of each vertex's offset
    // from the tail's axis. Modest on purpose: the effect reads from the
    // silhouette, and a bigger multiplier tears the tip.
    private const float PuffMax = 0.35f;
    private const float TailZeroFraction = 0.60f;   // torso below this
    private const float TailFullFraction = 0.72f;   // fully tail above this

    // ---- Pose amplitudes (radians unless noted) -----------------------

    private const float WalkCycleSpeed = 10f;    // radians/sec while moving
    private const float SwingAmplitude = 0.38f;  // hip swing while walking
    private const float KneeSwingAmplitude = 0.18f;
    private const float TailSwaySpeed = 2.2f;    // idle tail swish
    // Capped tighter than a real cat's range of motion, because the neck is a
    // short blend on a rigid mesh: at 0.9 rad the head/neck transition stretched
    // torso edges ~2.7x. 0.6 rad (about 34 degrees) is still a clearly readable
    // "the cat turned to look at you" without shearing the neck.
    private const float HeadTurnMax = 0.6f;      // head yaw clamp
    private const float HeadPitchMin = -0.5f;
    private const float HeadPitchMax = 0.4f;
    // How far the body can drop, as a fraction of model height. This is the
    // main lever for sitting and loafing: the legs fold (see CrouchFold) to
    // keep the paws planted while the body comes down.
    // Capped low on purpose. The mesh is a single rigid pose, so bending a leg
    // shears it across the joint: at a 0.34 drop the knee needs a 0.66 rad hip
    // fold (1.3 rad at the knee) and the transition across the blend band
    // stretched visible edges ~4x. A shallower crouch still reads as a
    // crouch/loaf and keeps the surface intact.
    private const float BodyDropMax = 0.16f;

    // How far a pose can drive the rear legs forward (sitting) and the front
    // legs forward (stretching), in radians. Kept well under 90 deg: the leg is
    // a straight chain in the rest pose, so a large hip rotation swings the
    // paw a long way and reads as a broken leg.
    private const float RearFoldMax = 0.85f;
    private const float FrontExtendMax = 0.75f;
    private const float KneeTuckMax = 0.45f;    // cosmetic extra curl

    // A cat's leg shortens by folding at the knee, not by sinking through the
    // floor. Given the body has dropped by `drop`, this returns the hip
    // rotation that shortens the leg by the same amount. Treating the leg as two
    // equal links meeting at the knee, folding the hip by -t and the knee by +2t
    // leaves the paw where it was and lowers the hip by 2*L*cos(t) rather than
    // 2*L — so the knees travel outward and the cat crouches, as it should.
    private static float CrouchFold(float drop, float legLength)
        => MathF.Acos(MathHelper.Clamp(1f - drop / (2f * legLength), -1f, 1f));

    // How fast the rig chases its targets. Poses blend in rather than
    // snapping, which is what keeps a state change from looking like a
    // pop (and keeps the head from twitching with raw target noise).
    private const float PoseLerpRate = 7f;
    private const float HeadLerpRate = 9f;

    // ---- Per-vertex rig data ------------------------------------------

    private struct Leg
    {
        public Vector3 Hip;
        public Vector3 Knee;
        public float Phase; // +1 / -1 so diagonal pairs move opposite
    }

    private readonly StaticMesh _mesh;
    private readonly Vector3[] _basePositions;
    private readonly Vector3[] _baseNormals;
    private readonly Vector3[] _baseTangents;

    // Filled in by AnalyzeRig in the constructor. The `null!` initializers are
    // only there to tell the compiler these are always assigned.
    private readonly Leg[] _legs = null!;
    private readonly int[] _legFor = null!;    // per-vertex leg index, -1 if not a leg
    private readonly float[] _legWeight = null!;
    private readonly float[] _kneeWeight = null!;
    private readonly float[] _headWeight = null!;
    private readonly float[] _tailWeight = null!;
    private readonly float[] _spineWeight = null!;
    private readonly float[] _crouchWeight = null!;
    private readonly float[] _puffWeight = null!;
    private readonly float[] _tailAlong = null!;  // 0 at the tail base, 1 at the tip

    // Neck and tail pivots, plus the axis the spine rotates about.
    private readonly Vector3 _neckPivot;
    private readonly Vector3 _tailPivot;
    private readonly Vector3 _spinePivot;

    private float _height = 1f; // model height in raw units, for scaling drops
    private float _legLength = 1f; // hip height above the floor, in raw units

    // Smoothed rig state.
    private float _walkPhase;
    private float _spinePitch;
    private float _crouch;
    private float _tailCurl, _tailSway, _puff;
    private float _headYaw, _headPitch;
    private float _headIdleYaw, _headIdlePitch;
    private float _idleTimer;
    private float _tailPhase;

    /// <summary>
    /// Creates a new animator for the given mesh. The constructor analyses the
    /// mesh's geometry to derive the rig (leg pivots, head/tail bands, etc.) —
    /// this is done once and cached.
    /// </summary>
    public CatAnimator(StaticMesh mesh)
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

        _height = MathF.Max(0.0001f, mesh.Size.Z);

        CatRig rig = AnalyzeRig(_basePositions);
        _legs = rig.Legs;
        _legFor = rig.LegFor;
        _legWeight = rig.LegWeight;
        _kneeWeight = rig.KneeWeight;
        _headWeight = rig.HeadWeight;
        _tailWeight = rig.TailWeight;
        _tailAlong = rig.TailAlong;
        _spineWeight = rig.SpineWeight;
        _crouchWeight = rig.CrouchWeight;
        _puffWeight = rig.PuffWeight;
        _neckPivot = rig.NeckPivot;
        _tailPivot = rig.TailPivot;
        _spinePivot = _legs[2].Hip; // rear-left hip; the hips share a Y/Z
        _legLength = MathF.Max(0.0001f, _legs[0].Hip.Z); // hip height = leg length
    }

    /// <summary>
    /// Advances the animation one frame: eases toward the pose targets, updates
    /// the gait phase, and applies the rig to the mesh's vertices.
    /// </summary>
    public void Update(in CatAnimInput input)
    {
        float dt = MathF.Max(0f, input.Dt);
        UpdateIdleGaze(dt, input);

        // Pose targets, eased toward. Walking is driven by the caller's
        // IsMoving rather than by the pose, so a cat that keeps walking
        // while it changes pose doesn't pop out of its gait.
        var t = PoseTargets(input.Pose);
        float poseT = 1f - MathF.Exp(-PoseLerpRate * dt);
        _spinePitch += (t.SpinePitch - _spinePitch) * poseT;
        _crouch += (t.Crouch - _crouch) * poseT;
        _tailCurl += (t.TailCurl - _tailCurl) * poseT;
        _puff += (t.Puff - _puff) * poseT;
        _rearFold += (t.RearFold - _rearFold) * poseT;
        _frontExtend += (t.FrontExtend - _frontExtend) * poseT;
        _kneeTuck += (t.KneeTuck - _kneeTuck) * poseT;
        _headRestYaw += (t.HeadYaw - _headRestYaw) * poseT;
        _headRestPitch += (t.HeadPitch - _headRestPitch) * poseT;

        // Gait.
        _walkPhase = input.IsMoving
            ? _walkPhase + dt * WalkCycleSpeed
            : MathHelper.Lerp(_walkPhase, 0f, 1f - MathF.Exp(-6f * dt));

        // Tail sway is a continuous idle swish layered on top of whatever
        // curl the pose asked for, so a sitting cat still flicks its tail.
        _tailPhase += dt * TailSwaySpeed;
        float swayTarget = input.IsMoving ? 0.55f : 0.8f;
        _tailSway += (swayTarget - _tailSway) * (1f - MathF.Exp(-4f * dt));

        // Head aim. The pose supplies a resting angle (grooming looks down,
        // sleeping looks down and tucked); a look target overrides it.
        float yawTarget = input.HasLookTarget
            ? MathHelper.Clamp(input.LookYaw, -HeadTurnMax, HeadTurnMax)
            : _headRestYaw + _headIdleYaw;
        float pitchTarget = input.HasLookTarget
            ? MathHelper.Clamp(input.LookPitch, HeadPitchMin, HeadPitchMax)
            : _headRestPitch + _headIdlePitch;

        float headT = 1f - MathF.Exp(-HeadLerpRate * dt);
        _headYaw += (yawTarget - _headYaw) * headT;
        _headPitch += (pitchTarget - _headPitch) * headT;

        Apply();
    }

    // Secondary pose channels that PoseTargets fills in; kept as fields so
    // the smoothing above can treat every channel uniformly.
    private float _rearFold, _frontExtend, _kneeTuck, _headRestYaw, _headRestPitch;

    private struct RigTargets
    {
        public float SpinePitch;
        public float Crouch;
        public float TailCurl;
        public float Puff;
        public float RearFold;
        public float FrontExtend;
        public float KneeTuck;
        public float HeadYaw;
        public float HeadPitch;

        // HeadYaw is the resting head turn when nothing is looking at the
        // player. No pose sets one today; it's here so a "looking over its
        // shoulder" pose is a one-line addition rather than a new channel.
        public static RigTargets Defaults => new() { HeadPitch = 0.1f };
    }

    // The pose library. Each entry is a rest configuration; the walk cycle
    // and the tail swish are layered on top of these by Update. CatPose.Walk
    // is deliberately the same as Stand: the gait comes from IsMoving, not
    // from the pose, so a cat can stay mid-stride through a state change.
    private static RigTargets PoseTargets(CatPose pose) => pose switch
    {
        // Ears forward, weight even: nothing special.
        CatPose.Stand or CatPose.Walk => RigTargets.Defaults,

        // Every angle below was dialled back from a first pass that measured
        // ~5x edge stretch on the head and torso. The recurring reason is the
        // spine: the torso pivots about the rear hip, so the shoulders travel
        // roughly 14 units per radian, and the shoulders sit inside the head
        // blend. A crouch folds the legs instead, which is a local deformation
        // near the ground, so poses that want to get low lean on it and keep the
        // spine tilt shallow.

        // Rear down, front up, back legs folded under, tail curled round the
        // side. Two measurements shaped this: at -0.26 rad of spine the head
        // edges stretched 3.5x and the torso 3.1x (so the spine is now nearly
        // flat and the crouch does the work), and a large RearFold shears the
        // leg blend — 0.60 put leg edges at 3.0x where Loaf's *deeper* crouch
        // with a much smaller fold sits at 2.4x. So the fold is kept light and
        // the pose is mostly a crouch with the tail doing the visual work.
        CatPose.Sit => new RigTargets
        {
            SpinePitch = -0.10f,
            Crouch = 0.52f,
            TailCurl = 1.0f,
            RearFold = 0.30f,
            KneeTuck = 0.30f,
            HeadPitch = 0.0f,
        },

        // Head down toward the front paw, slight crouch, tail still. The neck
        // is a short blend, so the nod stays modest.
        CatPose.Groom => new RigTargets
        {
            Crouch = 0.10f,
            TailCurl = 0.55f,
            FrontExtend = 0.12f,
            KneeTuck = 0.20f,
            HeadPitch = -0.40f,
        },

        // Front legs forward, rear up, tail straight up in the air.
        CatPose.Stretch => new RigTargets
        {
            SpinePitch = 0.30f,
            FrontExtend = 0.65f,
            TailCurl = -0.9f,
            HeadPitch = 0.15f,
        },

        // Curled up asleep: body down onto the floor, legs tucked under, tail
        // wrapped around. This is mostly a deep crouch.
        CatPose.Loaf => new RigTargets
        {
            Crouch = 0.55f,
            TailCurl = 1.0f,
            RearFold = 0.30f,
            KneeTuck = 0.45f,
            HeadPitch = -0.25f,
            // Tucked round and slightly to one side, as a sleeping cat curls.
            HeadYaw = 0.12f,
        },

        // Spotted the player: up on the legs, tail bristled.
        CatPose.Startle => new RigTargets
        {
            SpinePitch = 0.16f,
            TailCurl = -1.0f,
            Puff = 1f,
            HeadPitch = 0.2f,
        },

        _ => RigTargets.Defaults,
    };

    // A slow wander of the head and eyes while idle, so a standing cat
    // doesn't look frozen. Reseeded on a timer; retargeted smoothly.
    private void UpdateIdleGaze(float dt, in CatAnimInput input)
    {
        _idleTimer -= dt;
        if (_idleTimer <= 0f)
        {
            _idleTimer = 0.8f + MathF.Abs(Hash01(_gazeSeed += 1)) * 2.2f;
            _headIdleYaw = (Hash01(_gazeSeed * 7) * 2f - 1f) * 0.45f;
            _headIdlePitch = (Hash01(_gazeSeed * 13) * 2f - 1f) * 0.2f;
        }

        // A walking cat looks where it's going; only a still cat glances
        // around.
        if (input.IsMoving)
        {
            float t = 1f - MathF.Exp(-6f * dt);
            _headIdleYaw += (0f - _headIdleYaw) * t;
            _headIdlePitch += (0f - _headIdlePitch) * t;
        }
    }

    private float _gazeSeed = 0.37f;
    private static float Hash01(float x) => MathF.Abs(MathF.Sin(x * 12.9898f) * 43758.5453f % 1f);

    // ---- Applying the rig ---------------------------------------------

    // Rebuilds every vertex from the base pose through the rig chain. Each
    // stage rotates position, normal and tangent together so the
    // normal-mapped lighting follows the deformation instead of staying
    // frozen at the rest pose.
    private void Apply()
    {
        float hipSwing = MathF.Sin(_walkPhase) * SwingAmplitude;
        float kneeSwing = MathF.Sin(_walkPhase + MathHelper.PiOver2) * KneeSwingAmplitude;
        float drop = _crouch * BodyDropMax * _height;
        float sway = _tailSway * TailSwayMax;
        float crouchFold = CrouchFold(drop, _legLength);

        var verts = _mesh.Vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = _basePositions[i];
            Vector3 n = _baseNormals[i];
            Vector3 t = _baseTangents[i];
            float w4 = verts[i].Tangent.W;

            // 1. Spine: the torso rotates about the rear hip. Runs before
            // the head and tail so those hang off the rotated body.
            float sw = _spineWeight[i];
            if (sw > 0f)
            {
                p = RotateX(p - _spinePivot, _spinePitch * sw) + _spinePivot;
                n = RotateX(n, _spinePitch * sw);
                t = RotateX(t, _spinePitch * sw);
            }

            // 2. Crouch: drop the body toward the floor. Weighted so paws
            // stay put while the body above them descends, and the knees
            // bend to make up the difference.
            float cw = _crouchWeight[i];
            if (cw > 0f) p.Y -= drop * cw;

            // 3. Head: neck yaw, then neck pitch. Both scale with the same head
            // weight, so the skull rotates as a unit and the neck stretches
            // smoothly back into the shoulders.
            float hw = _headWeight[i];
            if (hw > 0f)
            {
                float yaw = _headYaw * hw;
                float pitch = _headPitch * hw;
                p = RotateZ(p - _neckPivot, yaw) + _neckPivot;
                p = RotateX(p - _neckPivot, pitch) + _neckPivot;
                n = RotateZ(n, yaw);
                n = RotateX(n, pitch);
                t = RotateZ(t, yaw);
                t = RotateX(t, pitch);
            }

            // 4. Tail: an arc that grows toward the tip (curl about the
            // body's fore-aft axis) plus a side-to-side swish about the
            // vertical axis. Weight is 0 at the base, so the tail stays
            // attached to the hips no matter how hard it swings.
            float tw = _tailWeight[i];
            if (tw > 0f)
            {
                // Both angles grow toward the tip, so the tail bends as an arc
                // instead of swinging rigidly. _tailCurl is a 0..1 pose
                // fraction; TailCurlMax converts it to radians.
                float along = _tailAlong[i];
                float curl = _tailCurl * TailCurlMax * along;
                float swish = sway * along;

                // The base moves with the spine, so rotate about the
                // already-spine-transformed pivot.
                Vector3 pivot = RotateX(_tailPivot - _spinePivot, _spinePitch) + _spinePivot;

                p = RotateX(p - pivot, curl) + pivot;
                p = RotateZ(p - pivot, swish) + pivot;
                n = RotateX(n, curl);
                n = RotateZ(n, swish);
                t = RotateX(t, curl);
                t = RotateZ(t, swish);

                // Puff: inflate the tail away from its own centre line, so
                // an alarmed cat looks bristled rather than just upright.
                if (_puff > 0f)
                {
                    float pw = _puffWeight[i] * _puff * PuffMax;
                    // Offset from the tail's axis (x = 0, z = pivot height).
                    Vector3 off = new(p.X, 0f, p.Z - pivot.Z);
                    p += off * pw;
                }
            }

            // 5. Legs: crouch fold, hip swing, then the knee.
            int leg = _legFor[i];
            float lw = _legWeight[i];
            if (lw > 0f)
            {
                var l = _legs[leg];
                bool rear = leg >= 2;

                // The crouch shortens every leg by the same amount the body
                // dropped (see CrouchFold), so the hips stay attached and the
                // paws stay on the floor. The knee takes twice the hip's angle,
                // which is what makes it a fold (Z shape) rather than a lean.
                float fold = crouchFold;
                float hip = -fold * lw;
                float knee = 2f * fold * lw;

                // Poses fold the rear legs under the body and stretch the front
                // legs forward; the walk swing rides on top of both.
                if (rear)
                    hip += -_rearFold * RearFoldMax * lw;
                else
                    hip += _frontExtend * FrontExtendMax * lw;

                hip += hipSwing * l.Phase * lw;
                knee += kneeSwing * l.Phase * lw;

                // Knee tuck is a purely cosmetic extra curl (a loafing cat
                // pulls its paws right under itself).
                knee += _kneeTuck * KneeTuckMax * lw;

                p = RotateX(p - l.Hip, hip) + l.Hip;
                n = RotateX(n, hip);
                t = RotateX(t, hip);

                float kw = _kneeWeight[i];
                if (kw > 0f)
                {
                    float k = knee * kw;
                    p = RotateX(p - l.Knee, k) + l.Knee;
                    n = RotateX(n, k);
                    t = RotateX(t, k);
                }
            }

            verts[i].Position = p;
            verts[i].Normal = n;
            verts[i].Tangent = new Vector4(t, w4);
        }
    }

    // Rotations about the model's X (its up axis) and Z (its fore-aft
    // horizontal axis). The model's own axes are odd — see the orientation
    // notes in Game1.BuildCritterModelTransform — so the rig rotates in
    // model space, where up is +Z and the tail extends along +Y.
    // Positive angle about the model's X (left/right) axis. In the model's own
    // space up is +Z and the nose is at -Y, so this swings a limb's lower end
    // toward the tail (+Y) as the angle goes positive — the "knee bends
    // backward" direction, which is why the hip uses a negative angle to reach
    // forward.
    private static Vector3 RotateX(Vector3 v, float a)
    {
        if (a == 0f) return v;
        float s = MathF.Sin(a), c = MathF.Cos(a);
        return new Vector3(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
    }

    // Rotation about the model's up axis (+Z): a side-to-side swing. Positive
    // carries the nose (at -Y) toward +X, which after the model-to-world
    // transform is the cat's left.
    private static Vector3 RotateZ(Vector3 v, float a)
    {
        if (a == 0f) return v;
        float s = MathF.Sin(a), c = MathF.Cos(a);
        return new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }

    // ---- Analysing the mesh into a rig --------------------------------

    // The result of analysing the mesh: the pivots and the per-vertex weights
    // that decide how each vertex follows each joint. Built once at
    // construction and never changes.
    private sealed class CatRig
    {
        public Leg[] Legs { get; }
        public int[] LegFor { get; }      // per-vertex leg index, -1 if not a leg
        public float[] LegWeight { get; }
        public float[] KneeWeight { get; }
        public float[] HeadWeight { get; }
        public float[] TailWeight { get; }
        public float[] TailAlong { get; } // 0 at the tail base, 1 at the tip
        public float[] SpineWeight { get; }
        public float[] CrouchWeight { get; }
        public float[] PuffWeight { get; }
        public Vector3 NeckPivot { get; }
        public Vector3 TailPivot { get; }

        public CatRig(
            Leg[] legs, int[] legFor, float[] legWeight, float[] kneeWeight,
            float[] headWeight, float[] tailWeight, float[] tailAlong,
            float[] spineWeight, float[] crouchWeight, float[] puffWeight,
            Vector3 neckPivot, Vector3 tailPivot)
        {
            Legs = legs;
            LegFor = legFor;
            LegWeight = legWeight;
            KneeWeight = kneeWeight;
            HeadWeight = headWeight;
            TailWeight = tailWeight;
            TailAlong = tailAlong;
            SpineWeight = spineWeight;
            CrouchWeight = crouchWeight;
            PuffWeight = puffWeight;
            NeckPivot = neckPivot;
            TailPivot = tailPivot;
        }
    }

    // Derives every pivot and weight from the raw positions. Leg index
    // order: 0=front-left, 1=front-right, 2=back-left, 3=back-right
    // (front = the nose/min-Y end, and +X is the cat's left, per the
    // model transform in Game1).
    private CatRig AnalyzeRig(Vector3[] positions)
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
        float yLength = MathF.Max(0.0001f, max.Y - min.Y);

        float bandTop = floorZ + height * LegBandFraction;
        float hipZ = floorZ + height * HipHeightFraction;
        float kneeZ = floorZ + height * KneeHeightFraction;
        float blend = MathF.Max(0.0001f, height * BlendBandFraction);
        float kneeBlend = MathF.Max(0.0001f, height * KneeBlendFraction);

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

        // Mean X/Y of the low band per leg, used as the hip and knee
        // pivots' X/Y.
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
            Vector3 c = sum[i] / MathF.Max(1f, count[i]);
            foreach (var p in positions)
            {
                if (p.Z > bandTop || LegIndex(p.X, p.Y, ySplit) != i) continue;
                r = MathF.Max(r, new Vector2(p.X - c.X, p.Y - c.Y).Length());
            }
            legRadius[i] = MathF.Max(0.0001f, r);
        }

        var legs = new Leg[4];
        var phases = new float[] { 1f, -1f, -1f, 1f }; // FL+BR together, FR+BL opposite
        for (int i = 0; i < 4; i++)
        {
            Vector3 c = sum[i] / MathF.Max(1f, count[i]);
            legs[i] = new Leg
            {
                Hip = new Vector3(c.X, c.Y, hipZ),
                Knee = new Vector3(c.X, c.Y, kneeZ),
                Phase = phases[i],
            };
        }

        // Band weights along the nose-to-tail axis. The head is everything
        // forward of the neck, the tail everything behind the hips. See the
        // HeadFullFraction comment for why the head blend is narrow.
        float headFull = min.Y + yLength * HeadFullFraction;
        float headZero = min.Y + yLength * HeadZeroFraction;
        float tailZero = min.Y + yLength * TailZeroFraction;
        float tailFull = min.Y + yLength * TailFullFraction;

        // Height gate for the head: a vertex must be clear of the leg tops
        // before it can be part of the head. The front paws share the neck's Y
        // range, so this is what keeps a head turn from moving them.
        float legTop = floorZ + height * HipHeightFraction;
        float heightGate = MathF.Max(0.0001f, height * HeadHeightGateFraction);

        // Width gate for the tail, from the model's own proportions: the tail is
        // a thin spike a fraction of the body's half-width, and anything wider
        // than that at tail-Y is a haunch, not tail.
        float tailWidth = MathF.Max(0.0001f, (max.X - min.X) * 0.5f * TailWidthFraction);
        float tailWidthFade = MathF.Max(0.0001f, tailWidth * 0.5f);

        // Neck pivot: the midline, at the middle of the head blend, at roughly
        // shoulder height. The ear tips reach the top of the model, so the
        // height can't be derived from the bounding box max.
        float neckZ = floorZ + height * 0.62f;
        var neckPivot = new Vector3(0f, min.Y + yLength * NeckFraction, neckZ);

        // Tail pivot: the midline where the tail becomes isolated from the
        // haunches, at the tail's own height there.
        float tailBaseY = tailZero;
        float tailBaseZMin = float.MaxValue, tailBaseZMax = float.MinValue;
        foreach (var p in positions)
        {
            if (p.Y < tailBaseY || p.Y > tailFull) continue;
            tailBaseZMin = MathF.Min(tailBaseZMin, p.Z);
            tailBaseZMax = MathF.Max(tailBaseZMax, p.Z);
        }
        var tailPivot = new Vector3(0f, tailBaseY, (tailBaseZMin + tailBaseZMax) * 0.5f);

        int n = positions.Length;
        var legFor = new int[n];
        var legWeight = new float[n];
        var kneeWeight = new float[n];
        var head = new float[n];
        var tail = new float[n];
        var tailAlong = new float[n];
        var spine = new float[n];
        var crouch = new float[n];
        var puff = new float[n];

        float tailLength = MathF.Max(0.0001f, max.Y - tailBaseY);

        for (int i = 0; i < n; i++)
        {
            var p = positions[i];

            // Head band, ramping out over the neck, gated by height. The Y band
            // alone is not enough: the front paws sit at Y=-12, inside the same
            // range as the neck, so a head turn would drag the paws around with
            // it (measured: a downward look stretched leg edges ~7x). The head
            // and the paws are separated by height, not by Y, so require a
            // vertex to be above the leg tops before it counts as head.
            float headBand = 1f - SmoothStep((p.Y - headFull) / (headZero - headFull));
            head[i] = headBand * SmoothStep((p.Z - legTop) / heightGate);

            // Tail band, plus how far along the tail this vertex sits (drives
            // the growing arc and the puff). Also gated by width: the haunches
            // are as broad as the body (X = +-6.2) while the tail is a thin
            // spike (X = +-1.6), so a Y band alone would sweep the rear legs
            // into the tail arc.
            float tailT = (p.Y - tailBaseY) / tailLength;
            float tailBand = SmoothStep((p.Y - tailZero) / (tailFull - tailZero));
            float halfWidth = MathF.Abs(p.X);
            float widthGate = 1f - SmoothStep((halfWidth - tailWidth) / tailWidthFade);
            tail[i] = tailBand * widthGate;
            tailAlong[i] = MathHelper.Clamp(tailT, 0f, 1f);

            // The puff is deliberately gentler at the base than the curl is, and
            // fades out toward the very tip. Inflating the full length at once
            // (measured: 0.9x of the offset) pulled tip vertices ~29 units and
            // tripled edge stretch; a bristle that grows toward the tip and
            // leaves the last stretch of tail alone keeps the silhouette and the
            // surface intact.
            puff[i] = tailBand * widthGate * (1f - SmoothStep((tailT - 0.5f) / 0.35f));

            // Spine: everything above the hip line belongs to the torso,
            // which pivots about the rear hip. The ramp keeps the belly
            // from creasing where the legs meet the body.
            spine[i] = SmoothStep((p.Z - hipZ) / blend);
            // Crouch: the body comes down but the paws stay on the floor.
            crouch[i] = SmoothStep((p.Z - bandTop) / blend);

            if (p.Z < hipZ)
            {
                int li = LegIndex(p.X, p.Y, ySplit);
                legFor[i] = li;

                // Hip weight: strongest low, fading to 0 at the hip.
                float zWeight = SmoothStep((hipZ - p.Z) / blend);

                Vector3 c = sum[li] / MathF.Max(1f, count[li]);
                float dist = new Vector2(p.X - c.X, p.Y - c.Y).Length();
                float rInner = legRadius[li] * RadialInnerFactor;
                float rOuter = legRadius[li] * RadialOuterFactor;
                float radial = rOuter > rInner
                    ? 1f - SmoothStep((dist - rInner) / (rOuter - rInner))
                    : 1f;

                legWeight[i] = zWeight * radial;

                // The knee only carries the part of the leg below it, so the
                // shank swings while the thigh stays with the hip.
                kneeWeight[i] = SmoothStep((kneeZ - p.Z) / kneeBlend) * radial;
            }
            else
            {
                legFor[i] = -1;
            }
        }

        return new CatRig(
            legs, legFor, legWeight, kneeWeight,
            head, tail, tailAlong, spine, crouch, puff,
            neckPivot, tailPivot);
    }

    private static int LegIndex(float x, float y, float ySplit)
    {
        bool right = x >= 0f; // +X is the cat's left, so this is "left"
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

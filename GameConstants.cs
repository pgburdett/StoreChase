using Microsoft.Xna.Framework;

namespace StoreChase
{
    /// <summary>
    /// Centralized game constants to avoid magic numbers scattered across the codebase.
    /// </summary>
    public static class GameConstants
    {
        // Core world/grid
        public const float CellSize = 2f; // world units per grid cell
        public const float WalkCycleSpeed = 12f; // radians/sec of the player's limb swing (Game1)
        public const float MoveSpeed = 4.5f; // grid steps per second (Player)
        public const float FreeMoveSpeed = 6f; // world units per second (Player first-person)
        public const float FreeRadius = 0.35f; // collision radius for free movement
        
        // Camera
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 2.5f;
        public const float PanSpeed = 12f;
        public const float ZoomStep = 0.1f;
        public const float MaxTiltDegrees = 80f;
        public const float TiltSpeed = 45f;
        public const float DefaultTiltDegrees = 45f;
        public const float DefaultZoom = 0.6f;
        public const float ViewCellsWide = 16f;
        public const float LookSensitivity = 0.0025f;
        public const float MaxPitchDegrees = 85f;
        public const float FirstPersonEyeHeight = 2.2f;
        public const float FirstPersonFovDegrees = 60f;
        public const float FirstPersonNearPlane = 0.05f;
        public const float MaxShake = 1f;
        public const float ShakeDecay = 2.4f;
        public const float ShakeWorldScale = 0.3f;
        public const float CameraFOV = 50f; // default perspective FOV
        
        // Gameplay
        public const float CarryHeight = 1.5f;
        public const float CounterHeight = 0.35f;
        public const float SkatesSeconds = 8f;
        public const float SkatesSpeedBoost = 1.5f;
        public const float SprayPanicSeconds = 4f;
        public const int SprayPanicRadius = 5;
        public const float TreatSeconds = 6f;
        public const float CounterDropAlarmSpike = 0.25f;
        public const float FireAlarmSpike = 0.15f;
        public const int CleanRoundBonus = 25;
        public const float SlipSeconds = 1.1f;
        public const float SkatesNoiseLoudness = 1.6f;
        public const float SlipClatterLoudness = 2.4f;
        public const float PowerCutSeconds = 8f;
        public const float DarkVisionFactor = 0.4f;
        public const float DarkLightFactor = 0.45f;
        public const float RatSeconds = 6f;
        public const float AlarmAuditFraction = 0.2f;
        public const int DeliveryCount = 2;
        public const float NoiseHudScale = 2.9f;
        public const float HitStopSeconds = 0.07f;
        public const float HitStopScale = 0.12f;
        public const float SlipSpeedBoost = 1.35f;
    }
}

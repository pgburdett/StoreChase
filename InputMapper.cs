using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace StoreChase
{
    /// <summary>
    /// Centralized input mapping for keyboard and gamepad controls.
    /// </summary>
    public static class InputMapper
    {
        public enum DeviceType
        {
            KeyboardMouse,
            GamePad
        }

        // Movement keys
        public static Keys UpKey = Keys.W;
        public static Keys DownKey = Keys.S;
        public static Keys LeftKey = Keys.A;
        public static Keys RightKey = Keys.D;
        
        // Actions
        public static Keys UseKey = Keys.E;
        public static Keys ThrowKey = Keys.Space;
        public static Keys ToggleCameraKey = Keys.V;
        public static Keys CrouchKey = Keys.LeftShift;
        public static Keys StartKey = Keys.Enter;
        public static Keys RestartKey = Keys.R;
        public static Keys PauseKey = Keys.P;
        public static Keys MenuKey = Keys.Escape;
        
        // Camera controls
        public static Keys CameraUpKey = Keys.I;
        public static Keys CameraDownKey = Keys.K;
        public static Keys CameraLeftKey = Keys.J;
        public static Keys CameraRightKey = Keys.L;
        public static Keys ZoomInKey = Keys.OemPlus;
        public static Keys ZoomOutKey = Keys.OemMinus;
        public static Keys TiltUpKey = Keys.O;
        public static Keys TiltDownKey = Keys.U;
        
        // Shop buy slots
        public static Keys[] BuySlotKeys = new Keys[] { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6 };
        
        // Gamepad mappings
        public static Buttons GamepadUseButton = Buttons.B;
        public static Buttons GamepadThrowButton = Buttons.X;
        public static Buttons GamepadToggleCameraButton = Buttons.Y;
        public static Buttons GamepadCrouchButton = Buttons.LeftShoulder;
        public static Buttons GamepadStartButton = Buttons.Start;
        public static Buttons GamepadPauseButton = Buttons.Start;
    }
}

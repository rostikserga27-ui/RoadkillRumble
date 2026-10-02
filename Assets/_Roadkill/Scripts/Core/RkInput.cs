using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Roadkill
{
    /// <summary>
    /// Thin input wrapper so the prototype runs with either the Input System package
    /// or the legacy Input Manager, whichever the project's Player Settings enable.
    /// Keys follow the GDD controls line (section 3).
    /// </summary>
    public static class RkInput
    {
#if ENABLE_INPUT_SYSTEM
        static Keyboard Kb => Keyboard.current;
        static Mouse Ms => Mouse.current;

        public static Vector2 Move
        {
            get
            {
                if (Kb == null) return Vector2.zero;
                float x = (Kb.dKey.isPressed ? 1f : 0f) - (Kb.aKey.isPressed ? 1f : 0f);
                float y = (Kb.wKey.isPressed ? 1f : 0f) - (Kb.sKey.isPressed ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        // Pixel delta scaled to match the legacy "Mouse X/Y" axes (0.1 per pixel).
        public static Vector2 Look => Ms != null ? Ms.delta.ReadValue() * 0.1f : Vector2.zero;
        public static bool JumpPressed => Kb != null && Kb.spaceKey.wasPressedThisFrame;
        public static bool Sprint => Kb != null && Kb.leftShiftKey.isPressed;
        public static bool Crouch => Kb != null && Kb.leftCtrlKey.isPressed;
        public static bool Possum => Kb != null && Kb.cKey.isPressed;
        public static bool ThrowHeld => Kb != null && Kb.gKey.isPressed;
        public static bool ThrowReleased => Kb != null && Kb.gKey.wasReleasedThisFrame;
        public static bool LeftHandHeld => Ms != null && Ms.leftButton.isPressed;
        public static bool RightHandHeld => Ms != null && Ms.rightButton.isPressed;
        public static bool ClickPressed => Ms != null && Ms.leftButton.wasPressedThisFrame;
        public static bool ResetPressed => Kb != null && Kb.rKey.wasPressedThisFrame;
        public static bool HelpPressed => Kb != null && Kb.f1Key.wasPressedThisFrame;
        public static bool EscapePressed => Kb != null && Kb.escapeKey.wasPressedThisFrame;
        public static bool PushToTalk => Kb != null && Kb.vKey.isPressed;
        public static bool VoiceModePressed => Kb != null && Kb.f2Key.wasPressedThisFrame;
        public static bool LoopbackPressed => Kb != null && Kb.f3Key.wasPressedThisFrame;
        public static bool InvitePressed => Kb != null && Kb.f4Key.wasPressedThisFrame;
        public static bool InteractPressed => Kb != null && Kb.eKey.wasPressedThisFrame;
        public static bool DebugMoneyPressed => Kb != null && Kb.f5Key.wasPressedThisFrame;
        /// <summary>Mouse wheel this frame: positive = up.</summary>
        public static float Scroll => Ms != null ? Ms.scroll.ReadValue().y : 0f;

        /// <summary>Hotbar slot picked with 1-6 this frame (0-based), or -1.</summary>
        public static int SlotKeyPressed
        {
            get
            {
                if (Kb == null) return -1;
                if (Kb.digit1Key.wasPressedThisFrame) return 0;
                if (Kb.digit2Key.wasPressedThisFrame) return 1;
                if (Kb.digit3Key.wasPressedThisFrame) return 2;
                if (Kb.digit4Key.wasPressedThisFrame) return 3;
                if (Kb.digit5Key.wasPressedThisFrame) return 4;
                if (Kb.digit6Key.wasPressedThisFrame) return 5;
                return -1;
            }
        }
#else
        public static Vector2 Move
        {
            get
            {
                float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        public static Vector2 Look => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
        public static bool Sprint => Input.GetKey(KeyCode.LeftShift);
        public static bool Crouch => Input.GetKey(KeyCode.LeftControl);
        public static bool Possum => Input.GetKey(KeyCode.C);
        public static bool ThrowHeld => Input.GetKey(KeyCode.G);
        public static bool ThrowReleased => Input.GetKeyUp(KeyCode.G);
        public static bool LeftHandHeld => Input.GetMouseButton(0);
        public static bool RightHandHeld => Input.GetMouseButton(1);
        public static bool ClickPressed => Input.GetMouseButtonDown(0);
        public static bool ResetPressed => Input.GetKeyDown(KeyCode.R);
        public static bool HelpPressed => Input.GetKeyDown(KeyCode.F1);
        public static bool EscapePressed => Input.GetKeyDown(KeyCode.Escape);
        public static bool PushToTalk => Input.GetKey(KeyCode.V);
        public static bool VoiceModePressed => Input.GetKeyDown(KeyCode.F2);
        public static bool LoopbackPressed => Input.GetKeyDown(KeyCode.F3);
        public static bool InvitePressed => Input.GetKeyDown(KeyCode.F4);
        public static bool InteractPressed => Input.GetKeyDown(KeyCode.E);
        public static bool DebugMoneyPressed => Input.GetKeyDown(KeyCode.F5);
        public static float Scroll => Input.mouseScrollDelta.y;

        public static int SlotKeyPressed
        {
            get
            {
                for (int i = 0; i < 6; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) return i;
                return -1;
            }
        }
#endif
    }
}

using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Debug window for the ragdoll test scene: Ragdoll Toggle, Launch, Reset Position and Fire Box
    /// (also F5-F8), live sliders for the ActiveRagdollController tuning values and its current state.
    /// Press Esc to free the mouse; clicks on the panel don't recapture it.
    /// </summary>
    public class RagdollDebugPanel : MonoBehaviour
    {
        public RagdollTestBuilder builder;
        public RagdollAutotest autotest;

        Rect window = new Rect(12f, 12f, 300f, 520f);
        bool limp;

        ActiveRagdollController Body => builder != null ? builder.Body : null;
        PlayerMotor Motor => builder != null ? builder.Motor : null;

        void Update()
        {
            if (Body == null) return;
            if (RkInput.RagdollTogglePressed) ToggleRagdoll();
            if (RkInput.LaunchPressed) Launch();
            if (RkInput.ResetPosePressed) ResetPosition();
            if (RkInput.FireBoxPressed) builder.FireBox();
        }

        void ToggleRagdoll()
        {
            limp = !limp;
            Body.SetLimp(limp);
        }

        void Launch()
        {
            Vector3 forward = Motor != null ? Motor.transform.forward : Vector3.forward;
            Body.Launch(forward * 7f + Vector3.up * 8f);
        }

        void ResetPosition()
        {
            limp = false;
            Body.SetLimp(false);
            if (Motor != null) Motor.Respawn();
            Body.ResetPose();
        }

        void OnDisable() => PlayerMotor.UiHasCursor = false;

        void OnGUI()
        {
            if (Body == null) return;
            PlayerMotor.UiHasCursor = Cursor.lockState != CursorLockMode.Locked && window.Contains(Event.current.mousePosition);
            window = GUI.Window(0x52414744, window, Draw, "Active Ragdoll");
        }

        void Draw(int id)
        {
            var b = Body;
            GUILayout.Label($"{(b.IsKnockedOut ? "KNOCKED OUT" : b.IsLimp ? "LIMP" : "ACTIVE")}  active {b.ActiveWeight:0.00}  " +
                            $"speed {b.Speed:0.0} m/s  {(b.IsGrounded ? "grounded" : "airborne")}");
            if (b.LastImpact.Length > 0) GUILayout.Label("Last hit: " + b.LastImpact);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(limp ? "Ragdoll: ON (F5)" : "Ragdoll: off (F5)")) ToggleRagdoll();
            if (GUILayout.Button("Launch (F6)")) Launch();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Position (F7)")) ResetPosition();
            if (GUILayout.Button("Fire Box (F8)")) builder.FireBox();
            GUILayout.EndHorizontal();
            if (autotest != null)
            {
                if (GUILayout.Button(autotest.Running ? "Autotest running..." : "Run Autotest")) autotest.Run();
                if (autotest.Summary.Length > 0) GUILayout.Label("Autotest: " + autotest.Summary);
            }

            b.balanceStrength = Slider("Balance Strength", b.balanceStrength, 0f, 1f);
            b.jointSpring = Slider("Joint Spring", b.jointSpring, 0f, 2000f);
            b.jointDamper = Slider("Joint Damper", b.jointDamper, 0f, 100f);
            b.maxForce = Slider("Max Force", b.maxForce, 0f, 5000f);
            b.wobbleAmount = Slider("Wobble Amount", b.wobbleAmount, 0f, 1f);
            b.headFloppiness = Slider("Head Floppiness", b.headFloppiness, 0f, 1f);
            b.gravityMultiplier = Slider("Gravity Multiplier", b.gravityMultiplier, 0.5f, 3f);
            b.knockoutVelocity = Slider("Knockout Velocity", b.knockoutVelocity, 3f, 20f);
            GUILayout.Label("WASD move · Space jump · C possum · R respawn · V 1st/3rd person · Esc mouse");
            GUI.DragWindow();
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:0.##}");
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}

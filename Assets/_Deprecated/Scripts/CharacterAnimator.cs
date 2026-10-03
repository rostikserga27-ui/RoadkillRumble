using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Procedural animation for the character model, no Animator needed: legs and arms swing with
    /// ground speed, the hips bob, the chest breathes when idle. Speed comes from how far the player
    /// moved this frame, so it works the same for the owner's body and for networked copies.
    /// Swing axes are taken from the player's "right" direction at rest, so it does not care how the
    /// bones are oriented inside the model. Steps aside while the RagdollRig is active and blends back
    /// smoothly afterwards.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public Transform root;            // the player (its right / up axes define the swing plane)
        public RagdollRig ragdoll;
        public float strideLength = 1.6f;   // metres per full walk cycle
        public float fullStrideSpeed = 3.5f;   // m/s at which the swing reaches full size
        public float legSwing = 40f;
        public float kneeBend = 50f;
        public float armSwing = 35f;
        public float elbowBend = 15f;
        public float hipBob = 0.035f;
        public float blendSpeed = 14f;

        class Bone
        {
            public Transform T;
            public Quaternion RestRotation;
            public Vector3 RestPosition;
            public Vector3 SwingAxis;   // the player's right, in the bone's parent space
            public Vector3 UpAxis;      // the player's up, in the bone's parent space
        }

        Bone hips, chest, upperLegL, lowerLegL, upperLegR, lowerLegR, upperArmL, lowerArmL, upperArmR, lowerArmR;
        Bone[] all;
        // Every other bone, so whatever the ragdoll displaced settles back to the rest pose.
        // The head keeps its rotation (PlayerNet aims it); only its position is restored.
        readonly System.Collections.Generic.List<Bone> others = new System.Collections.Generic.List<Bone>();
        Vector3 lastPosition;
        float speed;
        float phase;

        void Awake()
        {
            hips = Find("Hips");
            chest = Find("Chest");
            upperLegL = Find("UpperLeg_L"); lowerLegL = Find("LowerLeg_L");
            upperLegR = Find("UpperLeg_R"); lowerLegR = Find("LowerLeg_R");
            upperArmL = Find("UpperArm_L"); lowerArmL = Find("LowerArm_L");
            upperArmR = Find("UpperArm_R"); lowerArmR = Find("LowerArm_R");
            all = new[] { hips, chest, upperLegL, lowerLegL, upperLegR, lowerLegR, upperArmL, lowerArmL, upperArmR, lowerArmR };
            var posed = new System.Collections.Generic.HashSet<Transform>();
            foreach (var b in all) if (b != null) posed.Add(b.T);
            if (hips != null)
            {
                foreach (var t in hips.T.GetComponentsInChildren<Transform>())
                {
                    if (posed.Contains(t)) continue;
                    others.Add(new Bone { T = t, RestRotation = t.localRotation, RestPosition = t.localPosition });
                }
            }
            lastPosition = root.position;
        }

        Bone Find(string boneName)
        {
            var t = FindDeep(transform, boneName);
            if (t == null) return null;
            return new Bone
            {
                T = t,
                RestRotation = t.localRotation,
                RestPosition = t.localPosition,
                SwingAxis = t.parent.InverseTransformDirection(root.right).normalized,
                UpAxis = t.parent.InverseTransformDirection(root.up).normalized
            };
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 moved = Vector3.ProjectOnPlane(root.position - lastPosition, Vector3.up);
            lastPosition = root.position;
            if (ragdoll != null && ragdoll.IsActive) return;

            speed = Mathf.Lerp(speed, moved.magnitude / dt, 1f - Mathf.Exp(-8f * dt));
            float walk = Mathf.Clamp01(speed / fullStrideSpeed);
            phase += speed / strideLength * Mathf.PI * 2f * dt;
            float s = Mathf.Sin(phase);
            float c = Mathf.Cos(phase);

            // Positive angles swing a limb backward about the player's right axis.
            Pose(upperLegL, s * legSwing * walk);
            Pose(upperLegR, -s * legSwing * walk);
            Pose(lowerLegL, Mathf.Max(0f, c) * kneeBend * walk);
            Pose(lowerLegR, Mathf.Max(0f, -c) * kneeBend * walk);
            Pose(upperArmL, -s * armSwing * walk);
            Pose(upperArmR, s * armSwing * walk);
            Pose(lowerArmL, -elbowBend - Mathf.Max(0f, -s) * elbowBend * walk);
            Pose(lowerArmR, -elbowBend - Mathf.Max(0f, s) * elbowBend * walk);

            float breathe = Mathf.Sin(Time.time * 2.2f) * 1.5f * (1f - walk);
            Pose(chest, breathe - 4f * walk);
            Pose(hips, 0f, Mathf.Abs(s) * hipBob * walk);

            float k = 1f - Mathf.Exp(-blendSpeed * dt);
            foreach (var b in others)
            {
                b.T.localPosition = Vector3.Lerp(b.T.localPosition, b.RestPosition, k);
                if (b.T.name != "Head") b.T.localRotation = Quaternion.Slerp(b.T.localRotation, b.RestRotation, k);
            }
        }

        void Pose(Bone bone, float angle, float lift = 0f)
        {
            if (bone == null) return;
            float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            var targetRotation = Quaternion.AngleAxis(angle, bone.SwingAxis) * bone.RestRotation;
            bone.T.localRotation = Quaternion.Slerp(bone.T.localRotation, targetRotation, k);
            bone.T.localPosition = Vector3.Lerp(bone.T.localPosition, bone.RestPosition + bone.UpAxis * lift, k);
        }
    }
}

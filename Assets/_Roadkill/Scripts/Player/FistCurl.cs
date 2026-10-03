using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Curls the fisherman's fingers (visual only, no physics): a fist in his guard and when he punches,
    /// half closed round what he holds, loose when he goes limp. The finger bones (one for the four fingers,
    /// one for their tips, one for the thumb, per hand) are added to the model by the editor's FistRig, which
    /// also fills in the axes they bend about.
    /// </summary>
    public class FistCurl : MonoBehaviour
    {
        [System.Serializable]
        public class Hand
        {
            public Transform fingers, tips, thumb;
            [Tooltip("Bend axes in each bone's own space: a positive angle closes the hand.")]
            public Vector3 fingersAxis, tipsAxis, thumbAxis;
        }

        public Hand[] hands = new Hand[2];
        [Tooltip("Degrees at the knuckles, at the middle joints and at the thumb for a full fist.")]
        public float knuckleAngle = 90f;
        public float middleAngle = 95f;
        public float thumbAngle = 55f;
        [Tooltip("How closed the hand is while holding something / while limp (1 = fist).")]
        [Range(0f, 1f)] public float holding = 0.55f;
        [Range(0f, 1f)] public float limp = 0.25f;
        public float closeSpeed = 8f;

        ActiveRagdollController body;
        readonly float[] curl = { 1f, 1f };

        void Awake() => body = GetComponent<ActiveRagdollController>();

        void LateUpdate()
        {
            for (int i = 0; i < hands.Length && i < 2; i++)
            {
                var h = hands[i];
                if (h == null || h.fingers == null) continue;
                float target = body == null ? 1f : body.IsLimp ? limp : body.IsReaching(i) ? holding : 1f;
                curl[i] = Mathf.MoveTowards(curl[i], target, closeSpeed * Time.deltaTime);
                h.fingers.localRotation = Quaternion.AngleAxis(knuckleAngle * curl[i], h.fingersAxis);
                if (h.tips != null) h.tips.localRotation = Quaternion.AngleAxis(middleAngle * curl[i], h.tipsAxis);
                if (h.thumb != null) h.thumb.localRotation = Quaternion.AngleAxis(thumbAngle * curl[i], h.thumbAxis);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// A fan blowing straight up through its box: players float, limp fishermen tumble in it and props
    /// dance. The push fades with height so things hover near the top instead of leaving. Checked with an
    /// overlap query every step, since trigger callbacks stop for bodies that fall asleep on the fan.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Updraft : MonoBehaviour
    {
        [Tooltip("Upward acceleration at the fan (m/s^2); gravity is about 10.")]
        public float acceleration = 17f;
        [Tooltip("Air drag on vertical speed (1/s), so floaters settle into a hover instead of bobbing.")]
        public float damping = 1.6f;
        public Transform blades;
        public float bladeSpeed = 720f;

        readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();
        readonly Collider[] hits = new Collider[128];
        BoxCollider box;

        void Start()
        {
            box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        void FixedUpdate()
        {
            Bounds bounds = box.bounds;
            int n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            // Bodies with several colliders show up several times; the set pushes each once.
            for (int i = 0; i < n; i++)
                if (PlaygroundSurface.Pushable(hits[i].attachedRigidbody)) inside.Add(hits[i].attachedRigidbody);
            foreach (var body in inside)
            {
                float fade = 1f - Mathf.Clamp01((body.position.y - bounds.min.y) / bounds.size.y);
                body.WakeUp();
                body.AddForce(Vector3.up * (acceleration * fade - damping * body.linearVelocity.y), ForceMode.Acceleration);
            }
            inside.Clear();
        }

        void Update()
        {
            if (blades != null) blades.Rotate(Vector3.up, bladeSpeed * Time.deltaTime, Space.World);
        }
    }
}

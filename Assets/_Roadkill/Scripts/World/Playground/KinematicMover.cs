using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Turns a kinematic rigidbody about a world axis as a function of PlaygroundClock: a steady (or
    /// speeding-up-and-slowing-down) spin, or a pendulum swing. Every peer computes the same pose from the
    /// shared clock, so spinners and hammers need no networking; whatever they hit is pushed by physics
    /// where that thing is simulated (players on their owner, props on the server).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KinematicMover : MonoBehaviour
    {
        public enum Mode { Spin, Swing }

        public Mode mode = Mode.Spin;
        [Tooltip("World axis to turn about, through the pivot.")]
        public Vector3 axis = Vector3.up;
        public Vector3 pivot;
        [Tooltip("Spin: degrees per second (at the peak of the cycle, if there is one).")]
        public float speed = 90f;
        [Tooltip("Spin: when above 0, the speed rises from 0 to full and back over this many seconds, over and over.")]
        public float speedCycleSeconds;
        [Tooltip("Swing: degrees either side of the start pose.")]
        public float amplitude = 60f;
        [Tooltip("Swing: seconds per full swing there and back.")]
        public float period = 2.5f;
        [Tooltip("Offset along the cycle (0..1), so neighbours do not move in step.")]
        [Range(0f, 1f)] public float phase;

        Rigidbody body;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            startPosition = transform.position;
            startRotation = transform.rotation;
            axis = axis.normalized;
        }

        void FixedUpdate()
        {
            Pose(PlaygroundClock.Now, out Vector3 position, out Quaternion rotation);
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        float Angle(double t)
        {
            if (mode == Mode.Swing)
                return amplitude * Mathf.Sin((float)Frac(t / period + phase) * Mathf.PI * 2f);
            if (speedCycleSeconds <= 0f)
                return (float)((speed * t + phase * 360.0) % 360.0);
            // Speed follows speed * sin^2(pi t / T); the angle is its integral.
            double cycle = speedCycleSeconds;
            double shifted = t + phase * cycle;
            double angle = speed * (shifted * 0.5 - cycle / (4.0 * System.Math.PI) * System.Math.Sin(2.0 * System.Math.PI * shifted / cycle));
            return (float)(angle % 360.0);
        }

        static double Frac(double x) => x - System.Math.Floor(x);

        void Pose(double t, out Vector3 position, out Quaternion rotation)
        {
            Quaternion turn = Quaternion.AngleAxis(Angle(t), axis);
            rotation = turn * startRotation;
            position = pivot + turn * (startPosition - pivot);
        }

        /// <summary>World velocity of the point of this body that is at `point` now.</summary>
        public Vector3 VelocityAt(Vector3 point)
        {
            const double step = 0.02;
            double t = PlaygroundClock.Now;
            Pose(t, out Vector3 p0, out Quaternion r0);
            Pose(t + step, out Vector3 p1, out Quaternion r1);
            Vector3 local = Quaternion.Inverse(r0) * (point - p0);
            return (p1 + r1 * local - point) / (float)step;
        }
    }
}

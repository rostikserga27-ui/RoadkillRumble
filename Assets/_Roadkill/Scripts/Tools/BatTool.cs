using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Roadkill
{
    /// <summary>
    /// Baseball bat, on the held-bat prefab. LMB swings a procedural arc (wind-up, strike, follow
    /// through, return) with a cooldown, so it cannot be spammed. Only during the strike does the bat head
    /// sweep for hits; each object is hit at most once per swing, and PlayerTools sends the hit to the
    /// server, which owns prop physics. On other players' screens the same swing plays without hits.
    /// The prefab's root is the grip and the bat runs along its local +Y.
    /// </summary>
    public class BatTool : MonoBehaviour
    {
        [Header("Swing")]
        [SerializeField] float swingDuration = 0.45f;
        [SerializeField] float cooldown = 0.2f;
        [Tooltip("Normalised swing time where each phase ends: wind-up, contact, follow-through. The rest returns to idle.")]
        [SerializeField] float windupEnd = 0.2f, contactTime = 0.38f, followThroughEnd = 0.55f;
        [Tooltip("Grip rotations (Euler, camera space) for each pose.")]
        [SerializeField] Vector3 restEuler = new Vector3(25f, 0f, 10f);
        [SerializeField] Vector3 windupEuler = new Vector3(-15f, 0f, -55f);
        [SerializeField] Vector3 contactEuler = new Vector3(90f, 25f, 0f);
        [SerializeField] Vector3 followThroughEuler = new Vector3(90f, -75f, 0f);

        [Header("Hits")]
        [Tooltip("Hits only count between these normalised swing times.")]
        [SerializeField, Range(0f, 1f)] float activeStart = 0.22f;
        [SerializeField, Range(0f, 1f)] float activeEnd = 0.55f;
        [SerializeField] float batLength = 0.85f;
        [SerializeField] float hitRadius = 0.25f;
        [Tooltip("At the moment of contact, also hit what the crosshair is on up to this far from the eyes, so low or " +
                 "far targets the bat head would just miss still connect.")]
        [SerializeField] float aimReach = 2f;
        [SerializeField] LayerMask hitMask = ~0;
        [Tooltip("Impulse in N·s. The server caps the launch speed, so light props do not leave orbit.")]
        [SerializeField] float impulse = 40f;
        [Tooltip("How much the swing's own direction bends the hit sideways (0 = straight along the view).")]
        [SerializeField] float sideInfluence = 0.5f;
        [SerializeField] float upwardFactor = 0.3f;
        [Tooltip("Tumble added to light props, rad/s.")]
        [SerializeField] float spinSpeed = 8f;
        [SerializeField] bool logHits = true;

        PlayerTools owner;
        bool isLocal;
        bool swinging;
        float nextSwingTime;
        Vector3 lastHead;
        readonly HashSet<Object> hitThisSwing = new HashSet<Object>();

        Vector3 HeadPosition => transform.TransformPoint(Vector3.up * batLength);

        public void Init(PlayerTools tools, bool local)
        {
            owner = tools;
            isLocal = local;
            transform.localRotation = Quaternion.Euler(restEuler);
        }

        void OnDisable()
        {
            // Hidden mid-swing (ragdoll, slot change): start the next one from idle.
            StopAllCoroutines();
            swinging = false;
            transform.localRotation = Quaternion.Euler(restEuler);
        }

        void Update()
        {
            if (!isLocal || owner == null || swinging || Time.time < nextSwingTime) return;
            if (RkInput.ClickPressed && owner.CanUseTool)
            {
                PlaySwing();
                owner.BroadcastSwing();
            }
        }

        public void PlaySwing()
        {
            if (!swinging && isActiveAndEnabled) StartCoroutine(Swing());
        }

        IEnumerator Swing()
        {
            swinging = true;
            hitThisSwing.Clear();
            lastHead = HeadPosition;
            bool aimChecked = false;
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / swingDuration);
                transform.localRotation = Pose(t);
                if (isLocal && t >= activeStart && t <= activeEnd) SweepForHits();
                if (isLocal && !aimChecked && t >= contactTime)
                {
                    aimChecked = true;
                    SweepAlongView();
                }
                lastHead = HeadPosition;
                yield return null;
            }
            transform.localRotation = Quaternion.Euler(restEuler);
            swinging = false;
            nextSwingTime = Time.time + cooldown;
        }

        Quaternion Pose(float t)
        {
            Quaternion rest = Quaternion.Euler(restEuler), windup = Quaternion.Euler(windupEuler);
            Quaternion contact = Quaternion.Euler(contactEuler), follow = Quaternion.Euler(followThroughEuler);
            if (t < windupEnd) return Quaternion.Slerp(rest, windup, Mathf.SmoothStep(0f, 1f, t / windupEnd));
            if (t < contactTime) return Quaternion.Slerp(windup, contact, EaseIn((t - windupEnd) / (contactTime - windupEnd)));
            if (t < followThroughEnd) return Quaternion.Slerp(contact, follow, EaseOut((t - contactTime) / (followThroughEnd - contactTime)));
            return Quaternion.Slerp(follow, rest, Mathf.SmoothStep(0f, 1f, (t - followThroughEnd) / (1f - followThroughEnd)));
        }

        static float EaseIn(float x) => x * x;
        static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

        /// <summary>Sweep the bat head from last frame's position to this one, so fast swings cannot skip a target.</summary>
        void SweepForHits()
        {
            Vector3 head = HeadPosition;
            Vector3 delta = head - lastHead;
            float distance = delta.magnitude;
            Vector3 swingDir = distance > 0.0001f ? delta / distance : owner.ViewForward;

            if (distance > 0.0001f)
            {
                foreach (var hit in Physics.SphereCastAll(lastHead, hitRadius, swingDir, distance, hitMask, QueryTriggerInteraction.Ignore))
                    Hit(hit.collider, hit.distance > 0f ? hit.point : head, swingDir);
            }
            foreach (var collider in Physics.OverlapSphere(head, hitRadius, hitMask, QueryTriggerInteraction.Ignore))
                Hit(collider, head, swingDir);
        }

        void SweepAlongView()
        {
            Vector3 eye = owner.ViewOrigin, forward = owner.ViewForward;
            foreach (var hit in Physics.SphereCastAll(eye, hitRadius, forward, aimReach, hitMask, QueryTriggerInteraction.Ignore))
                Hit(hit.collider, hit.distance > 0f ? hit.point : eye + forward * 0.5f, forward);
        }

        void Hit(Collider collider, Vector3 point, Vector3 swingDir)
        {
            // Mostly along the view, bent by the swing, with some lift so things fly instead of skid.
            Vector3 forward = owner.ViewForward;
            Vector3 flat = Vector3.ProjectOnPlane(forward + swingDir * sideInfluence, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            Vector3 direction = (flat.normalized + Vector3.up * upwardFactor).normalized;
            Vector3 spin = Vector3.Cross(Vector3.up, direction) * spinSpeed + Random.insideUnitSphere * (spinSpeed * 0.3f);
            owner.ReportHit(collider, point, direction * impulse, spin, hitThisSwing, logHits);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(HeadPosition, hitRadius);
        }
    }
}

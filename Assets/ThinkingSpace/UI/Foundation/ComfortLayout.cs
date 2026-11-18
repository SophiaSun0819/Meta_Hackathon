using UnityEngine;

namespace ThinkingSpace.UI
{
    /// <summary>A spot in the user's view: horizontal angle, vertical angle and distance from the head.</summary>
    public readonly struct ComfortZone
    {
        public readonly float Yaw, Pitch, Distance;
        public ComfortZone(float yaw, float pitch, float distance) { Yaw = yaw; Pitch = pitch; Distance = distance; }
    }

    /// <summary>
    /// Shared spatial layout. The center of view belongs to idea bubbles; Orby sits to the left;
    /// the microphone pill sits low in the center; dialogs sit just below eye level.
    /// Nothing is head-locked: content stays put until the user turns away, then glides back.
    /// Negative yaw is to the user's left, negative pitch is below eye level.
    /// </summary>
    public static class ComfortLayout
    {
        public static readonly ComfortZone Orby = new ComfortZone(-35f, 0f, 1.0f);
        public static readonly ComfortZone MicPill = new ComfortZone(0f, -22f, 0.8f);
        public static readonly ComfortZone Dialog = new ComfortZone(0f, -10f, 0.9f);

        public const float BubbleArcDistance = 1.2f;
        public const float BubbleArcPitch = -8f;
        public const float BubbleArcFirstYaw = -10f;
        public const float BubbleArcStepYaw = 15f;
        public const int BubblesPerRow = 5;
        public const float BubbleRowDrop = 0.22f;

        /// <summary>Content glides back once it is this far (degrees) from where it belongs.</summary>
        public const float RecenterAngle = 35f;
        public const float RecenterDistanceTolerance = 0.35f;
        public const float FollowSharpness = 5f;
        public const float SettleDistance = 0.01f;

        public static Vector3 FlatForward(Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            return flat.sqrMagnitude < 0.0001f ? Vector3.forward : flat.normalized;
        }

        public static Vector3 PointInView(Vector3 headPosition, Vector3 headForward, ComfortZone zone)
        {
            Vector3 flat = Quaternion.AngleAxis(zone.Yaw, Vector3.up) * FlatForward(headForward);
            float pitch = zone.Pitch * Mathf.Deg2Rad;
            Vector3 direction = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
            return headPosition + direction * Mathf.Max(0f, zone.Distance);
        }

        /// <summary>Where the index-th new bubble goes: an arc in front of the user, filling rows left to right.</summary>
        public static Vector3 BubbleArcSlot(Vector3 arcOrigin, Vector3 arcForward, int index)
        {
            int safeIndex = Mathf.Max(0, index);
            int column = safeIndex % BubblesPerRow;
            int row = safeIndex / BubblesPerRow;
            ComfortZone zone = new ComfortZone(BubbleArcFirstYaw + column * BubbleArcStepYaw,
                BubbleArcPitch, BubbleArcDistance);
            return PointInView(arcOrigin, arcForward, zone) + Vector3.down * (row * BubbleRowDrop);
        }

        /// <summary>True when content has drifted out of its zone because the user turned or walked.</summary>
        public static bool NeedsRecenter(Vector3 headPosition, Vector3 headForward, Vector3 contentPosition,
            ComfortZone zone)
        {
            Vector3 toContent = Vector3.ProjectOnPlane(contentPosition - headPosition, Vector3.up);
            if (toContent.sqrMagnitude < 0.0001f) return true;
            Vector3 expected = Quaternion.AngleAxis(zone.Yaw, Vector3.up) * FlatForward(headForward);
            if (Vector3.Angle(expected, toContent) > RecenterAngle) return true;
            float expectedFlatDistance = zone.Distance * Mathf.Cos(zone.Pitch * Mathf.Deg2Rad);
            return Mathf.Abs(toContent.magnitude - expectedFlatDistance) > RecenterDistanceTolerance;
        }

        /// <summary>Upright rotation that makes a world-space canvas (readable from local -Z) face the viewer.</summary>
        public static Quaternion FaceViewer(Vector3 contentPosition, Vector3 viewerPosition)
        {
            Vector3 away = Vector3.ProjectOnPlane(contentPosition - viewerPosition, Vector3.up);
            if (away.sqrMagnitude < 0.000001f) away = Vector3.forward;
            return Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        /// <summary>
        /// Lazy follow for one frame. With snap, content jumps to its zone (use when it first appears).
        /// Otherwise it stays still until it leaves the zone, then glides back and settles.
        /// </summary>
        public static void StepLazyFollow(Transform content, Transform head, ComfortZone zone,
            ref bool recentering, bool snap, float deltaTime)
        {
            if (content == null || head == null) return;
            Vector3 target = PointInView(head.position, head.forward, zone);
            if (snap)
            {
                content.position = target;
                recentering = false;
            }
            else
            {
                if (!recentering && NeedsRecenter(head.position, head.forward, content.position, zone))
                    recentering = true;
                if (recentering)
                {
                    float blend = 1f - Mathf.Exp(-FollowSharpness * Mathf.Max(0f, deltaTime));
                    content.position = Vector3.Lerp(content.position, target, blend);
                    if (Vector3.Distance(content.position, target) <= SettleDistance) recentering = false;
                }
            }
            content.rotation = FaceViewer(content.position, head.position);
        }
    }
}

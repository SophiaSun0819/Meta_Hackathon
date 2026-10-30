using System;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    /// <summary>
    /// Arbitrates left and right Meta hands and activates a recording visual after a
    /// deliberate, head-proximate index pinch. This milestone does not record audio.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PinchRecordingGestureController : MonoBehaviour
    {
        [Serializable]
        private sealed class HandBinding
        {
            public string displayName;
            public OVRHand hand;
            public OVRSkeleton skeleton;

            [NonSerialized] public Transform thumbTip;
            [NonSerialized] public Transform indexTip;
        }

        [Header("Existing Scene References")]
        [SerializeField] private Transform centerEyeAnchor;
        [SerializeField] private HandBinding leftHand = new HandBinding { displayName = "Left" };
        [SerializeField] private HandBinding rightHand = new HandBinding { displayName = "Right" };
        [SerializeField] private PinchRecordingIndicator indicator;

        [Header("Gesture")]
        [SerializeField, Min(0f)] private float holdDuration = 0.4f;
        [SerializeField, Range(0f, 1f)] private float releasePinchStrength = 0.35f;
        [SerializeField, Min(0f)] private float cooldownDuration = 0.35f;
        [SerializeField, Min(0f)] private float trackingLossGracePeriod = 0.15f;

        [Header("Head-relative Activation")]
        [Tooltip("Maximum distance from CenterEyeAnchor to the fingertip midpoint.")]
        [SerializeField, Min(0.05f)] private float faceActivationRadius = 0.45f;
        [Tooltip("Rejects pinches substantially behind the headset.")]
        [SerializeField] private float minimumHeadLocalZ = -0.12f;
        [Tooltip("Rejects distant pinches directly in front of the user.")]
        [SerializeField] private float maximumHeadLocalZ = 0.42f;

        private HandBinding pendingHand;
        private HandBinding activeHand;
        private float holdTimer;
        private float cooldownTimer;
        private float trackingLossTimer;

        private void Awake()
        {
            if (indicator == null)
            {
                indicator = GetComponentInChildren<PinchRecordingIndicator>(true);
            }

            if (indicator == null)
            {
                indicator = gameObject.AddComponent<PinchRecordingIndicator>();
            }

            indicator.Hide();
        }

        private void Update()
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.unscaledDeltaTime);

            if (activeHand != null)
            {
                UpdateActiveGesture();
                return;
            }

            if (cooldownTimer > 0f)
            {
                ResetPendingGesture();
                return;
            }

            // Once a hand begins the hold, keep it as the pending owner. This prevents
            // two simultaneous pinches from swapping ownership as their distances vary.
            HandBinding candidate = pendingHand != null && IsActivationCandidate(pendingHand, out _)
                ? pendingHand
                : ChooseCandidate();
            if (candidate == null)
            {
                ResetPendingGesture();
                return;
            }

            if (pendingHand != candidate)
            {
                pendingHand = candidate;
                holdTimer = 0f;
            }

            holdTimer += Time.unscaledDeltaTime;
            if (holdTimer < holdDuration)
            {
                return;
            }

            if (!TryGetPinchMidpoint(candidate, out Vector3 midpoint))
            {
                ResetPendingGesture();
                return;
            }

            activeHand = candidate;
            pendingHand = null;
            holdTimer = 0f;
            trackingLossTimer = 0f;
            indicator.Show(midpoint, centerEyeAnchor);
        }

        private HandBinding ChooseCandidate()
        {
            bool leftValid = IsActivationCandidate(leftHand, out Vector3 leftMidpoint);
            bool rightValid = IsActivationCandidate(rightHand, out Vector3 rightMidpoint);

            if (leftValid && rightValid)
            {
                float leftDistance = (leftMidpoint - centerEyeAnchor.position).sqrMagnitude;
                float rightDistance = (rightMidpoint - centerEyeAnchor.position).sqrMagnitude;
                return leftDistance <= rightDistance ? leftHand : rightHand;
            }

            if (leftValid)
            {
                return leftHand;
            }

            return rightValid ? rightHand : null;
        }

        private bool IsActivationCandidate(HandBinding binding, out Vector3 midpoint)
        {
            midpoint = default;
            if (!HasHighConfidence(binding) || centerEyeAnchor == null)
            {
                return false;
            }

            if (!binding.hand.GetFingerIsPinching(OVRHand.HandFinger.Index))
            {
                return false;
            }

            if (!TryGetPinchMidpoint(binding, out midpoint))
            {
                return false;
            }

            Vector3 headLocalPoint = centerEyeAnchor.InverseTransformPoint(midpoint);
            return headLocalPoint.sqrMagnitude <= faceActivationRadius * faceActivationRadius
                && headLocalPoint.z >= minimumHeadLocalZ
                && headLocalPoint.z <= maximumHeadLocalZ;
        }

        private void UpdateActiveGesture()
        {
            bool tracked = HasHighConfidence(activeHand);
            if (!tracked || !TryGetPinchMidpoint(activeHand, out Vector3 midpoint))
            {
                trackingLossTimer += Time.unscaledDeltaTime;
                if (trackingLossTimer >= trackingLossGracePeriod)
                {
                    EndActiveGesture();
                }
                return;
            }

            trackingLossTimer = 0f;
            float pinchStrength = activeHand.hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            if (pinchStrength <= releasePinchStrength)
            {
                EndActiveGesture();
                return;
            }

            indicator.SetTarget(midpoint, centerEyeAnchor);
        }

        private static bool HasHighConfidence(HandBinding binding)
        {
            return binding != null
                && binding.hand != null
                && binding.skeleton != null
                && binding.hand.IsTracked
                && binding.hand.HandConfidence == OVRHand.TrackingConfidence.High
                && binding.hand.GetFingerConfidence(OVRHand.HandFinger.Index) == OVRHand.TrackingConfidence.High;
        }

        private static bool TryGetPinchMidpoint(HandBinding binding, out Vector3 midpoint)
        {
            midpoint = default;
            if (binding == null || binding.skeleton == null)
            {
                return false;
            }

            if (binding.thumbTip == null || binding.indexTip == null)
            {
                ResolveFingertips(binding);
            }

            if (binding.thumbTip == null || binding.indexTip == null)
            {
                return false;
            }

            midpoint = (binding.thumbTip.position + binding.indexTip.position) * 0.5f;
            return true;
        }

        private static void ResolveFingertips(HandBinding binding)
        {
            if (binding.skeleton.Bones == null)
            {
                return;
            }

            foreach (OVRBone bone in binding.skeleton.Bones)
            {
                if (bone.Id == OVRSkeleton.BoneId.Hand_ThumbTip
                    || bone.Id == OVRSkeleton.BoneId.XRHand_ThumbTip)
                {
                    binding.thumbTip = bone.Transform;
                }
                else if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip
                    || bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip)
                {
                    binding.indexTip = bone.Transform;
                }
            }
        }

        private void EndActiveGesture()
        {
            activeHand = null;
            trackingLossTimer = 0f;
            cooldownTimer = cooldownDuration;
            indicator.Hide();
        }

        private void ResetPendingGesture()
        {
            pendingHand = null;
            holdTimer = 0f;
        }

        private void OnDisable()
        {
            activeHand = null;
            ResetPendingGesture();
            if (indicator != null)
            {
                indicator.Hide();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maximumHeadLocalZ = Mathf.Max(minimumHeadLocalZ, maximumHeadLocalZ);
            releasePinchStrength = Mathf.Clamp01(releasePinchStrength);
        }
#endif
    }
}

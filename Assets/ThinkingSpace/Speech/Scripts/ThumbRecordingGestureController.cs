using System;
using System.Text;
using UnityEngine;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class ThumbRecordingGestureController : MonoBehaviour
    {
        public enum RecordingHand { Left, Right }

        private enum RecordingGestureState { Idle, StartGesturePending, Recording, StopGesturePending }
        private enum ThumbGesture { None, Up, Down }

        [Serializable]
        private sealed class FingerJoints
        {
            [NonSerialized] public Transform proximal;
            [NonSerialized] public Transform intermediate;
            [NonSerialized] public Transform distal;
            [NonSerialized] public Transform tip;
            public bool SimpleCurlJointsResolved => proximal != null && intermediate != null
                && distal != null && tip != null;
        }

        [Serializable]
        private sealed class HandBinding
        {
            public string displayName;
            public OVRHand hand;
            public OVRSkeleton skeleton;

            [NonSerialized] public Transform wrist;
            [NonSerialized] public Transform thumbBase;
            [NonSerialized] public Transform thumbTip;
            [NonSerialized] public FingerJoints index = new FingerJoints();
            [NonSerialized] public FingerJoints middle = new FingerJoints();
            [NonSerialized] public FingerJoints ring = new FingerJoints();
            [NonSerialized] public FingerJoints pinky = new FingerJoints();
            [NonSerialized] public Vector3 previousWristPosition;
            [NonSerialized] public bool hasPreviousWristPosition;
            [NonSerialized] public float smoothedWristSpeed;
            [NonSerialized] public float thumbUpAngle = 180f;
            [NonSerialized] public float thumbVerticalDot;
            [NonSerialized] public float thumbExtension;
            [NonSerialized] public float indexCurlRatio;
            [NonSerialized] public float middleCurlRatio;
            [NonSerialized] public float ringCurlRatio;
            [NonSerialized] public float pinkyCurlRatio;
            [NonSerialized] public int curledFingerCount;
            [NonSerialized] public bool indexFingerCurled;
            [NonSerialized] public string thumbDirection = "Unknown";
            [NonSerialized] public ThumbGesture detectedGesture;
            [NonSerialized] public string rejectionReason = "Not evaluated";

            public bool SimpleJointsResolved => wrist != null && thumbBase != null && thumbTip != null
                && index.SimpleCurlJointsResolved && middle.SimpleCurlJointsResolved
                && ring.SimpleCurlJointsResolved && pinky.SimpleCurlJointsResolved;
        }

        [Header("Existing Scene References")]
        [SerializeField] private Transform centerEyeAnchor;
        [SerializeField] private HandBinding leftHand = new HandBinding { displayName = "Left" };
        [SerializeField] private HandBinding rightHand = new HandBinding { displayName = "Right" };
        [SerializeField] private PinchRecordingIndicator indicator;
        [SerializeField] private ThumbGestureDiagnosticsPanel diagnosticsPanel;
        [SerializeField] private ThumbJointDebugVisualizer jointDebugVisualizer;
        [SerializeField] private QuestMicrophoneRecorder microphoneRecorder;
        [SerializeField] private SpeechTranscriptionCoordinator transcriptionCoordinator;

        [Header("Recording Hand")]
        [SerializeField] private RecordingHand selectedRecordingHand = RecordingHand.Right;

        [Header("Gesture Timing")]
        [SerializeField, Min(0f)] private float holdDuration = 0.5f;
        [SerializeField, Min(0f)] private float cooldownDuration = 0.35f;
        [SerializeField, Min(0f)] private float neutralRearmDuration = 0.2f;
        [SerializeField, Min(0f)] private float gestureBreakGracePeriod = 0.12f;

        [Header("Thumb Orientation")]
        [SerializeField, Range(5f, 89f)] private float thumbDirectionTolerance = 75f;
        [Header("Finger Curl")]
        [Tooltip("A finger is curled when its base-to-tip chord is sufficiently shorter than its joint path.")]
        [SerializeField, Range(0.4f, 0.98f)] private float maximumCurledChordRatio = 0.82f;
        [SerializeField, Range(1, 4)] private int requiredCurledFingerCount = 3;

        [Header("Diagnostics")]
        [Tooltip("Shows the recording indicator without requiring a gesture. Visual test only.")]
        [SerializeField] private bool debugShowIndicator;

        private RecordingGestureState state = RecordingGestureState.Idle;
        private HandBinding pendingHand;
        private float pendingTimer;
        private float cooldownTimer;
        private float neutralTimer;
        private float pendingBreakTimer;
        private bool gestureArmed;
        private bool debugIndicatorWasActive;
        private RecordingHand appliedRecordingHand;
        private bool sessionActive;

        public string DiagnosticText { get; private set; }
        public RecordingHand SelectedRecordingHand => selectedRecordingHand;
        public bool SessionActive => sessionActive;
        public bool IsMicrophoneRecording => microphoneRecorder != null && microphoneRecorder.IsRecording;
        public QuestMicrophoneRecorder MicrophoneRecorder => microphoneRecorder;
        public Transform CenterEyeAnchor => centerEyeAnchor;
        public OVRHand LeftOVRHand => leftHand.hand;
        public OVRSkeleton LeftOVRSkeleton => leftHand.skeleton;
        public OVRHand RightOVRHand => rightHand.hand;
        public OVRSkeleton RightOVRSkeleton => rightHand.skeleton;

        private void Awake()
        {
            if (indicator == null) indicator = GetComponentInChildren<PinchRecordingIndicator>(true);
            if (indicator == null) indicator = gameObject.AddComponent<PinchRecordingIndicator>();
            if (diagnosticsPanel == null) diagnosticsPanel = GetComponentInChildren<ThumbGestureDiagnosticsPanel>(true);
            if (jointDebugVisualizer == null) jointDebugVisualizer = GetComponentInChildren<ThumbJointDebugVisualizer>(true);
            if (microphoneRecorder == null) microphoneRecorder = GetComponent<QuestMicrophoneRecorder>();
            if (microphoneRecorder == null) microphoneRecorder = gameObject.AddComponent<QuestMicrophoneRecorder>();
            if (transcriptionCoordinator == null) transcriptionCoordinator = GetComponent<SpeechTranscriptionCoordinator>();
            appliedRecordingHand = selectedRecordingHand;
            sessionActive = false;
            gestureArmed = true;
            indicator.Hide();
        }

        private void OnEnable()
        {
            if (microphoneRecorder != null)
            {
                microphoneRecorder.RecordingStarted += HandleMicrophoneStarted;
                microphoneRecorder.RecordingCompleted += HandleMicrophoneCompleted;
                microphoneRecorder.RecordingFailed += HandleMicrophoneFailed;
            }
            // Initial activation is eligible immediately. Neutral rearm is required only
            // after a completed start/stop transition.
            if (sessionActive && state == RecordingGestureState.Idle) gestureArmed = true;
        }

        private void Update()
        {
            if (appliedRecordingHand != selectedRecordingHand)
                ApplyRecordingHandChange();

            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.unscaledDeltaTime);
            ThumbGesture leftGesture = EvaluateHand(leftHand);
            ThumbGesture rightGesture = EvaluateHand(rightHand);
            ThumbGesture selectedGesture = selectedRecordingHand == RecordingHand.Left
                ? leftGesture : rightGesture;
            UpdateJointDebugVisuals();

            if (!sessionActive)
            {
                UpdateDebugIndicator();
                DiagnosticText = BuildDiagnosticText();
                if (diagnosticsPanel != null)
                    diagnosticsPanel.SetData(DiagnosticText, centerEyeAnchor);
                return;
            }

            if (microphoneRecorder != null && (microphoneRecorder.State == MicrophoneRecordingState.RequestingPermission
                || microphoneRecorder.State == MicrophoneRecordingState.Starting
                || microphoneRecorder.State == MicrophoneRecordingState.Stopping))
            {
                UpdateDebugIndicator();
                DiagnosticText = BuildDiagnosticText();
                if (diagnosticsPanel != null) diagnosticsPanel.SetData(DiagnosticText, centerEyeAnchor);
                return;
            }

            switch (state)
            {
                case RecordingGestureState.Idle:
                    UpdateIdle(selectedGesture);
                    break;
                case RecordingGestureState.StartGesturePending:
                    UpdatePendingGesture(
                        ThumbGesture.Up,
                        RecordingGestureState.Idle,
                        selectedGesture);
                    break;
                case RecordingGestureState.Recording:
                    UpdateRecording(selectedGesture);
                    break;
                case RecordingGestureState.StopGesturePending:
                    UpdatePendingGesture(
                        ThumbGesture.Down,
                        RecordingGestureState.Recording,
                        selectedGesture);
                    break;
            }

            UpdateDebugIndicator();
            DiagnosticText = BuildDiagnosticText();
            if (diagnosticsPanel != null)
                diagnosticsPanel.SetData(DiagnosticText, centerEyeAnchor);
        }

        private void UpdateDebugIndicator()
        {
            if (debugShowIndicator && sessionActive)
            {
                if (!debugIndicatorWasActive && state != RecordingGestureState.Recording)
                    indicator.Show(centerEyeAnchor);
                debugIndicatorWasActive = true;
            }
            else if (debugIndicatorWasActive)
            {
                if (state != RecordingGestureState.Recording) indicator.Hide();
                debugIndicatorWasActive = false;
            }
        }

        private void UpdateJointDebugVisuals()
        {
            if (jointDebugVisualizer == null) return;
            jointDebugVisualizer.SetHeadset(centerEyeAnchor);
            jointDebugVisualizer.SetHand(
                true,
                leftHand.hand != null && leftHand.hand.IsTracked,
                leftHand.thumbBase,
                leftHand.thumbTip,
                leftHand.index.proximal,
                leftHand.index.tip,
                GetJointMarkerUnavailableReason(leftHand));
            jointDebugVisualizer.SetHand(
                false,
                rightHand.hand != null && rightHand.hand.IsTracked,
                rightHand.thumbBase,
                rightHand.thumbTip,
                rightHand.index.proximal,
                rightHand.index.tip,
                GetJointMarkerUnavailableReason(rightHand));
        }

        private static string GetJointMarkerUnavailableReason(HandBinding hand)
        {
            if (hand.hand == null) return "OVRHand reference missing";
            if (hand.skeleton == null) return "skeleton reference missing";
            if (!hand.hand.IsTracked) return "hand not tracked";
            if (hand.skeleton.Bones == null || hand.skeleton.Bones.Count == 0)
                return "skeleton not initialized";
            if (hand.thumbBase == null || hand.thumbTip == null) return "thumb joints unresolved";
            if (hand.index.proximal == null || hand.index.tip == null) return "index joints unresolved";
            return "joint positions unavailable";
        }

        public void SetSelectedRecordingHand(RecordingHand hand)
        {
            if (selectedRecordingHand == hand && appliedRecordingHand == hand) return;
            selectedRecordingHand = hand;
            ApplyRecordingHandChange();
        }

        public void SetSessionActive(bool active)
        {
            if (sessionActive == active) return;
            sessionActive = active;
            if (transcriptionCoordinator != null) transcriptionCoordinator.SetSessionActive(active);
            state = RecordingGestureState.Idle;
            pendingHand = null;
            pendingTimer = 0f;
            pendingBreakTimer = 0f;
            neutralTimer = 0f;
            cooldownTimer = 0f;
            gestureArmed = false;
            if (!active && microphoneRecorder != null
                && microphoneRecorder.State != MicrophoneRecordingState.Idle
                && microphoneRecorder.State != MicrophoneRecordingState.Error)
                microphoneRecorder.CancelRecording("Session ended.");
            if (indicator != null) indicator.Hide();
        }

        public void SetSessionIdentifier(string identifier)
        {
            if (microphoneRecorder != null) microphoneRecorder.SetSessionIdentifier(identifier);
        }

        private void ApplyRecordingHandChange()
        {
            appliedRecordingHand = selectedRecordingHand;
            if (state == RecordingGestureState.StartGesturePending)
                state = RecordingGestureState.Idle;
            else if (state == RecordingGestureState.StopGesturePending)
                state = RecordingGestureState.Recording;
            pendingHand = null;
            pendingTimer = 0f;
            pendingBreakTimer = 0f;
            neutralTimer = 0f;
            gestureArmed = false;
        }

        private HandBinding SelectedHandBinding => selectedRecordingHand == RecordingHand.Left
            ? leftHand : rightHand;

        private void UpdateIdle(ThumbGesture selectedGesture)
        {
            if (transcriptionCoordinator != null && !transcriptionCoordinator.CanBeginRecording)
            {
                gestureArmed = false;
                neutralTimer = 0f;
                return;
            }
            UpdateNeutralRearm(selectedGesture);
            if (!gestureArmed || cooldownTimer > 0f) return;
            if (selectedGesture == ThumbGesture.Up)
                BeginPending(SelectedHandBinding, RecordingGestureState.StartGesturePending);
        }

        private void UpdateRecording(ThumbGesture selectedGesture)
        {
            // Tracking loss cannot end an active recording.
            indicator.SetHeadset(centerEyeAnchor);
            if (microphoneRecorder != null)
                indicator.SetRecordingStatus(
                    microphoneRecorder.ElapsedSeconds,
                    microphoneRecorder.LiveRms,
                    microphoneRecorder.LivePeak,
                    microphoneRecorder.State.ToString(),
                    microphoneRecorder.LastStatus);
            UpdateNeutralRearm(selectedGesture);
            if (!gestureArmed || cooldownTimer > 0f) return;
            if (selectedGesture == ThumbGesture.Down)
                BeginPending(SelectedHandBinding, RecordingGestureState.StopGesturePending);
        }

        private void UpdatePendingGesture(
            ThumbGesture required,
            RecordingGestureState fallback,
            ThumbGesture selectedGesture)
        {
            if (pendingHand != SelectedHandBinding || selectedGesture != required)
            {
                pendingBreakTimer += Time.unscaledDeltaTime;
                if (pendingBreakTimer < gestureBreakGracePeriod) return;
                pendingHand = null;
                pendingTimer = 0f;
                pendingBreakTimer = 0f;
                state = fallback;
                return;
            }

            pendingBreakTimer = 0f;
            pendingTimer += Time.unscaledDeltaTime;
            if (pendingTimer < holdDuration) return;
            if (required == ThumbGesture.Up) EnterRecording();
            else ExitRecording();
        }

        private void BeginPending(HandBinding hand, RecordingGestureState pendingState)
        {
            pendingHand = hand;
            pendingTimer = 0f;
            pendingBreakTimer = 0f;
            state = pendingState;
        }

        private void EnterRecording()
        {
            if (transcriptionCoordinator != null && !transcriptionCoordinator.CanBeginRecording)
            {
                ResetTransitionState(RecordingGestureState.Idle);
                return;
            }
            pendingHand = null;
            pendingTimer = pendingBreakTimer = neutralTimer = 0f;
            gestureArmed = false;
            cooldownTimer = cooldownDuration;
            state = RecordingGestureState.StartGesturePending;
            if (microphoneRecorder == null || !microphoneRecorder.BeginRecording())
                HandleMicrophoneFailed(microphoneRecorder != null ? microphoneRecorder.LastError : "Microphone recorder missing.");
        }

        private void ExitRecording()
        {
            ResetTransitionState(RecordingGestureState.Idle);
            indicator.Hide();
            if (microphoneRecorder != null) microphoneRecorder.StopRecording();
        }

        private void HandleMicrophoneStarted()
        {
            ResetTransitionState(RecordingGestureState.Recording);
            indicator.Show(centerEyeAnchor);
            indicator.SetRecordingStatus(0f, 0f, 0f, microphoneRecorder.State.ToString(),
                microphoneRecorder.LastStatus);
        }

        private void HandleMicrophoneCompleted(CompletedMicrophoneRecording recording)
        {
            ResetTransitionState(RecordingGestureState.Idle);
            indicator.Hide();
        }

        private void HandleMicrophoneFailed(string message)
        {
            ResetTransitionState(RecordingGestureState.Idle);
            indicator.Hide();
        }

        private void ResetTransitionState(RecordingGestureState nextState)
        {
            pendingHand = null;
            pendingTimer = 0f;
            pendingBreakTimer = 0f;
            neutralTimer = 0f;
            gestureArmed = false;
            cooldownTimer = cooldownDuration;
            state = nextState;
        }

        public void RequireFreshRecordingGesture()
        {
            state = RecordingGestureState.Idle;
            pendingHand = null;
            pendingTimer = pendingBreakTimer = neutralTimer = 0f;
            cooldownTimer = cooldownDuration;
            gestureArmed = false;
        }

        private void UpdateNeutralRearm(ThumbGesture selectedGesture)
        {
            if (selectedGesture == ThumbGesture.None)
            {
                neutralTimer += Time.unscaledDeltaTime;
                if (neutralTimer >= neutralRearmDuration) gestureArmed = true;
            }
            else
            {
                neutralTimer = 0f;
            }
        }

        private ThumbGesture EvaluateHand(HandBinding binding)
        {
            if (binding == null || binding.hand == null || binding.skeleton == null)
            {
                if (binding != null) binding.rejectionReason = "Missing hand or skeleton reference";
                return ThumbGesture.None;
            }

            binding.detectedGesture = ThumbGesture.None;
            binding.thumbDirection = "Unknown";
            binding.thumbVerticalDot = 0f;
            binding.thumbUpAngle = 180f;
            binding.curledFingerCount = 0;
            if (!binding.hand.IsTracked)
            {
                binding.hasPreviousWristPosition = false;
                binding.rejectionReason = "Hand is not tracked";
                return ThumbGesture.None;
            }
            if (!ResolveJoints(binding))
            {
                binding.rejectionReason = "Required skeleton joints unresolved";
                return ThumbGesture.None;
            }

            float wristSpeed = 0f;
            if (binding.hasPreviousWristPosition && Time.unscaledDeltaTime > 0.0001f)
            {
                wristSpeed = Vector3.Distance(binding.wrist.position, binding.previousWristPosition)
                    / Time.unscaledDeltaTime;
            }
            binding.previousWristPosition = binding.wrist.position;
            binding.hasPreviousWristPosition = true;

            float speedBlend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.12f);
            binding.smoothedWristSpeed = Mathf.Lerp(binding.smoothedWristSpeed, wristSpeed, speedBlend);
            bool enoughFingersCurled = AreOtherFingersCurled(binding);

            Vector3 thumbVector = binding.thumbTip.position - binding.thumbBase.position;
            binding.thumbExtension = thumbVector.magnitude;
            if (binding.thumbExtension < 0.0001f)
            {
                binding.rejectionReason = "Thumb base-to-tip vector is invalid";
                return ThumbGesture.None;
            }

            float verticalDot = Vector3.Dot(thumbVector.normalized, Vector3.up);
            binding.thumbVerticalDot = verticalDot;
            binding.thumbUpAngle = Mathf.Acos(Mathf.Clamp(verticalDot, -1f, 1f)) * Mathf.Rad2Deg;
            float requiredDot = Mathf.Cos(thumbDirectionTolerance * Mathf.Deg2Rad);
            if (verticalDot >= requiredDot)
            {
                binding.thumbDirection = "Up";
                binding.detectedGesture = ThumbGesture.Up;
                binding.rejectionReason = "Accepted: thumbs up";
            }
            else if (verticalDot <= -requiredDot)
            {
                binding.thumbDirection = "Down";
                binding.detectedGesture = ThumbGesture.Down;
                binding.rejectionReason = "Accepted: thumbs down";
            }
            else
            {
                binding.thumbDirection = "Sideways";
                binding.rejectionReason = "Thumb direction is sideways/neutral";
            }

            if (!enoughFingersCurled)
            {
                binding.detectedGesture = ThumbGesture.None;
                binding.rejectionReason = !binding.indexFingerCurled
                    ? "Index finger is extended; pointing poses are ignored"
                    : "Only " + binding.curledFingerCount + "/4 non-thumb fingers are curled";
            }
            return binding.detectedGesture;
        }

        private bool AreOtherFingersCurled(HandBinding hand)
        {
            hand.curledFingerCount = 0;
            hand.indexFingerCurled = IsFingerCurled(hand.index, out hand.indexCurlRatio);
            if (hand.indexFingerCurled) hand.curledFingerCount++;
            if (IsFingerCurled(hand.middle, out hand.middleCurlRatio)) hand.curledFingerCount++;
            if (IsFingerCurled(hand.ring, out hand.ringCurlRatio)) hand.curledFingerCount++;
            if (IsFingerCurled(hand.pinky, out hand.pinkyCurlRatio)) hand.curledFingerCount++;
            return hand.indexFingerCurled && hand.curledFingerCount >= requiredCurledFingerCount;
        }

        private bool IsFingerCurled(FingerJoints finger, out float chordRatio)
        {
            float pathLength = Vector3.Distance(finger.proximal.position, finger.intermediate.position)
                + Vector3.Distance(finger.intermediate.position, finger.distal.position)
                + Vector3.Distance(finger.distal.position, finger.tip.position);
            if (pathLength < 0.001f)
            {
                chordRatio = 1f;
                return false;
            }

            float chordLength = Vector3.Distance(finger.proximal.position, finger.tip.position);
            chordRatio = chordLength / pathLength;
            return chordRatio <= maximumCurledChordRatio;
        }

        private static bool ResolveJoints(HandBinding binding)
        {
            if (binding.SimpleJointsResolved) return true;
            if (binding.skeleton.Bones == null) return false;
            OVRSkeleton.SkeletonType skeletonType = binding.skeleton.GetSkeletonType();
            bool isOpenXr = skeletonType == OVRSkeleton.SkeletonType.XRHandLeft
                || skeletonType == OVRSkeleton.SkeletonType.XRHandRight;
            bool isLegacy = skeletonType == OVRSkeleton.SkeletonType.HandLeft
                || skeletonType == OVRSkeleton.SkeletonType.HandRight;
            if (!isOpenXr && !isLegacy) return false;

            foreach (OVRBone bone in binding.skeleton.Bones)
            {
                if (isOpenXr) AssignOpenXrJoint(binding, bone.Id, bone.Transform);
                else AssignLegacyJoint(binding, bone.Id, bone.Transform);
            }
            return binding.SimpleJointsResolved;
        }

        private static void AssignOpenXrJoint(HandBinding h, OVRSkeleton.BoneId id, Transform t)
        {
            if (id == OVRSkeleton.BoneId.XRHand_Wrist) h.wrist = t;
            else if (id == OVRSkeleton.BoneId.XRHand_ThumbProximal) h.thumbBase = t;
            else if (id == OVRSkeleton.BoneId.XRHand_ThumbTip) h.thumbTip = t;
            else if (id == OVRSkeleton.BoneId.XRHand_IndexProximal) h.index.proximal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_IndexIntermediate) h.index.intermediate = t;
            else if (id == OVRSkeleton.BoneId.XRHand_IndexDistal) h.index.distal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_IndexTip) h.index.tip = t;
            else if (id == OVRSkeleton.BoneId.XRHand_MiddleProximal) h.middle.proximal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_MiddleIntermediate) h.middle.intermediate = t;
            else if (id == OVRSkeleton.BoneId.XRHand_MiddleDistal) h.middle.distal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_MiddleTip) h.middle.tip = t;
            else if (id == OVRSkeleton.BoneId.XRHand_RingProximal) h.ring.proximal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_RingIntermediate) h.ring.intermediate = t;
            else if (id == OVRSkeleton.BoneId.XRHand_RingDistal) h.ring.distal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_RingTip) h.ring.tip = t;
            else if (id == OVRSkeleton.BoneId.XRHand_LittleProximal) h.pinky.proximal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_LittleIntermediate) h.pinky.intermediate = t;
            else if (id == OVRSkeleton.BoneId.XRHand_LittleDistal) h.pinky.distal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_LittleTip) h.pinky.tip = t;
        }

        private static void AssignLegacyJoint(HandBinding h, OVRSkeleton.BoneId id, Transform t)
        {
            if (id == OVRSkeleton.BoneId.Hand_WristRoot) h.wrist = t;
            else if (id == OVRSkeleton.BoneId.Hand_Thumb1) h.thumbBase = t;
            else if (id == OVRSkeleton.BoneId.Hand_ThumbTip) h.thumbTip = t;
            else if (id == OVRSkeleton.BoneId.Hand_Index1) h.index.proximal = t;
            else if (id == OVRSkeleton.BoneId.Hand_Index2) h.index.intermediate = t;
            else if (id == OVRSkeleton.BoneId.Hand_Index3) h.index.distal = t;
            else if (id == OVRSkeleton.BoneId.Hand_IndexTip) h.index.tip = t;
            else if (id == OVRSkeleton.BoneId.Hand_Middle1) h.middle.proximal = t;
            else if (id == OVRSkeleton.BoneId.Hand_Middle2) h.middle.intermediate = t;
            else if (id == OVRSkeleton.BoneId.Hand_Middle3) h.middle.distal = t;
            else if (id == OVRSkeleton.BoneId.Hand_MiddleTip) h.middle.tip = t;
            else if (id == OVRSkeleton.BoneId.Hand_Ring1) h.ring.proximal = t;
            else if (id == OVRSkeleton.BoneId.Hand_Ring2) h.ring.intermediate = t;
            else if (id == OVRSkeleton.BoneId.Hand_Ring3) h.ring.distal = t;
            else if (id == OVRSkeleton.BoneId.Hand_RingTip) h.ring.tip = t;
            else if (id == OVRSkeleton.BoneId.Hand_Pinky1) h.pinky.proximal = t;
            else if (id == OVRSkeleton.BoneId.Hand_Pinky2) h.pinky.intermediate = t;
            else if (id == OVRSkeleton.BoneId.Hand_Pinky3) h.pinky.distal = t;
            else if (id == OVRSkeleton.BoneId.Hand_PinkyTip) h.pinky.tip = t;
        }

        private string BuildDiagnosticText()
        {
            StringBuilder text = new StringBuilder(1200);
            text.AppendLine("THUMB RECORDING DIAGNOSTICS");
            text.Append("Session recording: ").AppendLine(sessionActive ? "ENABLED" : "DISABLED");
            HandBinding selected = SelectedHandBinding;
            text.Append("Selected recording hand: ").AppendLine(selectedRecordingHand.ToString());
            text.Append("Selected-hand gesture: ").AppendLine(selected.detectedGesture.ToString());
            text.Append("Unselected hand: ")
                .Append(selectedRecordingHand == RecordingHand.Left ? "Right" : "Left")
                .AppendLine(" (ignored by recording state machine)");
            text.Append("State: ").Append(state)
                .Append("   Hold: ").Append(pendingTimer.ToString("0.00"))
                .Append('/').Append(holdDuration.ToString("0.00")).AppendLine(" s");
            text.Append("Armed: ").Append(gestureArmed ? "YES" : "NO")
                .Append("   Neutral: ").Append(neutralTimer.ToString("0.00"))
                .Append('/').Append(neutralRearmDuration.ToString("0.00"))
                .Append("   Cooldown: ").Append(cooldownTimer.ToString("0.00")).AppendLine(" s");
            text.Append("Selected-hand state gate: ").AppendLine(GetStateGateDiagnostic());
            text.Append("Indicator test: ").AppendLine(debugShowIndicator ? "FORCED ON" : "off");
            if (microphoneRecorder != null) text.AppendLine(microphoneRecorder.GetDiagnosticSummary());
            else text.AppendLine("Mic: recorder component missing");
            if (transcriptionCoordinator != null) text.AppendLine(transcriptionCoordinator.DiagnosticText);
            if (jointDebugVisualizer == null)
                text.AppendLine("Joint markers: visualizer reference missing");
            else if (!jointDebugVisualizer.enabled)
                text.AppendLine("Joint markers: visualizer component disabled");
            else
                text.Append("Joint markers: ").AppendLine(jointDebugVisualizer.Status);
            AppendHandDiagnostic(text, leftHand);
            AppendHandDiagnostic(text, rightHand);
            return text.ToString();
        }

        private string GetStateGateDiagnostic()
        {
            if (!sessionActive) return "Blocked: no active brainstorming session";
            if (microphoneRecorder != null && microphoneRecorder.State == MicrophoneRecordingState.RequestingPermission)
                return "Waiting for microphone permission";
            if (microphoneRecorder != null && microphoneRecorder.State == MicrophoneRecordingState.Starting)
                return "Waiting for first microphone samples";
            if (microphoneRecorder != null && microphoneRecorder.State == MicrophoneRecordingState.Stopping)
                return "Finalizing microphone clip";
            if (cooldownTimer > 0f) return "Blocked by cooldown";
            if (!gestureArmed) return "Blocked until selected hand is neutral";
            if (state == RecordingGestureState.Idle) return "Eligible; waiting for thumbs up";
            if (state == RecordingGestureState.StartGesturePending) return "Confirming thumbs up";
            if (state == RecordingGestureState.Recording) return "Eligible; waiting for thumbs down";
            return "Confirming thumbs down";
        }

        private void AppendHandDiagnostic(StringBuilder text, HandBinding hand)
        {
            text.AppendLine();
            text.Append("[ ").Append(hand.displayName).AppendLine(" ]");
            if (hand.hand == null)
            {
                text.AppendLine("Missing OVRHand reference");
                return;
            }

            text.Append("Tracked: ").Append(hand.hand.IsTracked)
                .Append("   Hand confidence: ").Append(hand.hand.HandConfidence).AppendLine();
            text.Append("Finger confidence T/I/M/R/P: ")
                .Append(Confidence(hand, OVRHand.HandFinger.Thumb)).Append('/')
                .Append(Confidence(hand, OVRHand.HandFinger.Index)).Append('/')
                .Append(Confidence(hand, OVRHand.HandFinger.Middle)).Append('/')
                .Append(Confidence(hand, OVRHand.HandFinger.Ring)).Append('/')
                .Append(Confidence(hand, OVRHand.HandFinger.Pinky)).AppendLine();
            text.Append("Joints: ").Append(hand.SimpleJointsResolved ? "resolved" : "UNRESOLVED")
                .Append("   Thumb up angle: ").Append(hand.thumbUpAngle.ToString("0.0")).Append(" deg")
                .Append("   Dot: ").Append(hand.thumbVerticalDot.ToString("0.00"))
                .Append("   Direction: ").AppendLine(hand.thumbDirection);
            text.Append("Curled fingers: ").Append(hand.curledFingerCount).Append("/4")
                .Append("   Index curled: ").Append(hand.indexFingerCurled ? "YES" : "NO")
                .Append("   Chord/path I/M/R/P: ")
                .Append(hand.indexCurlRatio.ToString("0.00")).Append('/')
                .Append(hand.middleCurlRatio.ToString("0.00")).Append('/')
                .Append(hand.ringCurlRatio.ToString("0.00")).Append('/')
                .Append(hand.pinkyCurlRatio.ToString("0.00")).Append("   Limit <= ")
                .Append(maximumCurledChordRatio.ToString("0.00")).AppendLine();
            text.Append("Gesture: ").Append(hand.detectedGesture).AppendLine();
            text.Append("Reason: ").AppendLine(hand.rejectionReason);
        }

        private static string Confidence(HandBinding hand, OVRHand.HandFinger finger)
        {
            return hand.hand.IsTracked ? hand.hand.GetFingerConfidence(finger).ToString() : "N/A";
        }

        private void OnDisable()
        {
            if (microphoneRecorder != null)
            {
                microphoneRecorder.RecordingStarted -= HandleMicrophoneStarted;
                microphoneRecorder.RecordingCompleted -= HandleMicrophoneCompleted;
                microphoneRecorder.RecordingFailed -= HandleMicrophoneFailed;
                microphoneRecorder.CancelRecording();
            }
            state = RecordingGestureState.Idle;
            pendingHand = null;
            pendingTimer = pendingBreakTimer = cooldownTimer = neutralTimer = 0f;
            gestureArmed = false;
            leftHand.hasPreviousWristPosition = rightHand.hasPreviousWristPosition = false;
            if (indicator != null) indicator.Hide();
        }
    }
}

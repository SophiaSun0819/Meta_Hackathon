using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.Speech;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class BrainstormMenuController : MonoBehaviour
    {
        private enum MenuScreen { Main, Session, VoiceHelp, GestureHelp }
        private enum OpeningHand { None, Left, Right }
        private enum MenuGestureState { WaitingForFist, FistHold, WaitingForOpen, OpenHold, WaitingForRelease }

        [Header("Existing References")]
        [SerializeField] private ThumbRecordingGestureController recordingGestureController;
        [SerializeField] private BrainstormMenuView authoredMenuPrefab;

        [Header("Authored UI Test")]
        [SerializeField] private bool debugShowAuthoredMenuAtHeadset;
        [SerializeField] private Vector3 authoredTestHeadsetOffset = new Vector3(0f, -0.03f, 0.55f);

        [Header("Open Palm")]
        [SerializeField, Min(0.05f)] private float fistArmHoldDuration = 0.3f;
        [SerializeField, Min(0.1f)] private float fistToOpenTimeout = 2f;
        [SerializeField, Min(0.05f)] private float openPalmHoldDuration = 0.2f;
        [SerializeField, Min(0.05f)] private float closeFistHoldDuration = 0.35f;
        [SerializeField, Min(0.05f)] private float sequenceTrackingLossGrace = 0.35f;
        [SerializeField, Min(0f)] private float fistReleaseDebounce = 0.08f;
        [SerializeField, Range(10f, 85f)] private float palmUpTolerance = 45f;
        [SerializeField, Range(0.7f, 0.99f)] private float extendedFingerChordRatio = 0.82f;
        [SerializeField, Range(0.4f, 0.95f)] private float curledFingerChordRatio = 0.78f;
        [SerializeField, Range(2, 4)] private int requiredExtendedFingers = 3;
        [Tooltip("Thumb tip must lie close to the palm center for a true closed fist.")]
        [SerializeField, Range(0.5f, 1.5f)] private float thumbFoldedPalmWidthRatio = 1.05f;
        [SerializeField] private Vector3 menuOffsetFromHand = new Vector3(0f, 0.1f, 0.03f);
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.08f;
        [SerializeField, Min(0.1f)] private float facingSmoothSpeed = 10f;

        [Header("Fist Progress")]
        [SerializeField, Min(0.1f)] private float readyMessageDuration = 0.6f;
        [SerializeField] private Vector3 progressOffsetFromHand = new Vector3(0f, 0.065f, 0.02f);

        [Header("World Space UI")]
        [SerializeField, Min(0.0001f)] private float canvasScale = 0.00032f;
        [SerializeField] private Vector2 panelSize = new Vector2(620f, 620f);
        [SerializeField, Min(0.05f)] private float duplicateClickGuard = 0.2f;
        [SerializeField] private TMP_FontAsset menuFont;

        [Header("Open Palm Diagnostics")]
        [SerializeField] private bool showOpenPalmDiagnostics;
        [SerializeField] private bool showInteractionDiagnostics;
        [SerializeField] private bool showCanvasFrontDiagnostics;
        [SerializeField] private bool showFrontTestLabel;
        [SerializeField] private Vector3 diagnosticsHeadsetOffset = new Vector3(0f, -0.18f, 0.55f);

        private sealed class FingerChain
        {
            public Transform proximal, intermediate, distal, tip;
            public bool Resolved => proximal != null && intermediate != null && distal != null && tip != null;
        }

        private sealed class OpenPalmHand
        {
            public OVRHand hand;
            public OVRSkeleton skeleton;
            public Transform wrist, palm, thumbProximal, thumbDistal, thumbTip;
            public readonly FingerChain index = new FingerChain();
            public readonly FingerChain middle = new FingerChain();
            public readonly FingerChain ring = new FingerChain();
            public readonly FingerChain pinky = new FingerChain();
            public float indexRatio = 1f, middleRatio = 1f, ringRatio = 1f, pinkyRatio = 1f;
            public int curledCount, extendedCount;
            public float palmFacingAngle = 180f, palmUpAngle = 180f;
            public float palmUpDot;
            public bool fistRecognized, openRecognized;
            public string classification = "NEUTRAL";
            public float thumbPalmRatio = 10f;
            public bool Resolved => wrist != null && thumbProximal != null && thumbDistal != null
                && thumbTip != null && index.Resolved && middle.Resolved && ring.Resolved && pinky.Resolved;
        }

        private readonly BrainstormSessionStore sessionStore = new BrainstormSessionStore();
        private readonly OpenPalmHand left = new OpenPalmHand();
        private readonly OpenPalmHand right = new OpenPalmHand();
        private Canvas menuCanvas;
        private BrainstormMenuView authoredView;
        private RectTransform contentRoot;
        private RectTransform canvasRect;
        private BoundsClipper canvasBoundsClipper;
        private GameObject diagnosticsRoot;
        private TextMeshPro diagnosticsText;
        private GameObject fistProgressRoot;
        private Transform fistProgressFill;
        private TextMeshPro fistProgressText;
        private Material progressBackgroundMaterial;
        private Material progressFillMaterial;
        private GameObject frontTestRoot;
        private bool authoredTestWasVisible;
        private MenuScreen currentScreen = MenuScreen.Main;
        private MenuScreen helpReturnScreen = MenuScreen.Main;
        private OpeningHand sequenceOwner;
        private OpeningHand menuOwner;
        private MenuGestureState gestureState = MenuGestureState.WaitingForFist;
        private float gestureHoldTimer;
        private float sequenceTimer;
        private float trackingLossTimer;
        private float fistReleaseTimer;
        private float closeFistTimer;
        private float lastButtonActivation = -10f;
        private string leftPalmDiagnostic = "Not evaluated";
        private string rightPalmDiagnostic = "Not evaluated";
        private string leftFistDiagnostic = "Not evaluated";
        private string rightFistDiagnostic = "Not evaluated";
        private string interactionDiagnostic = "No UI interaction received";
        private Vector3 followVelocity;
        private bool microphoneMenuLock;

        private static readonly Color PanelColor = new Color(0.035f, 0.055f, 0.09f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.11f, 0.2f, 0.32f, 1f);
        private static readonly Color AccentColor = new Color(0.1f, 0.65f, 1f, 1f);

        private void Awake()
        {
            if (recordingGestureController == null)
                recordingGestureController = GetComponent<ThumbRecordingGestureController>();
            if (recordingGestureController == null)
            {
                enabled = false;
                Debug.LogError("BrainstormMenuController requires the existing ThumbRecordingGestureController.", this);
                return;
            }
            if (menuFont == null) menuFont = TMP_Settings.defaultFontAsset;
            if (menuFont == null)
                Debug.LogError("Brainstorm menu has no TMP font asset; assign a runtime font in the prefab.", this);

            left.hand = recordingGestureController.LeftOVRHand;
            left.skeleton = recordingGestureController.LeftOVRSkeleton;
            right.hand = recordingGestureController.RightOVRHand;
            right.skeleton = recordingGestureController.RightOVRSkeleton;
            recordingGestureController.SetSessionActive(false);
            EnsureMetaCanvasEventSystem();
            if (authoredMenuPrefab != null) BuildAuthoredMenu();
            else BuildCanvas();
            BuildDiagnostics();
            BuildFistProgress();
            if (authoredView == null) BuildFrontTestLabel();
        }

        private void OnEnable()
        {
            PointableCanvasModule.WhenSelectableHovered += HandleCanvasHover;
            PointableCanvasModule.WhenSelectableUnhovered += HandleCanvasUnhover;
            PointableCanvasModule.WhenSelected += HandleCanvasPress;
            PointableCanvasModule.WhenUnselected += HandleCanvasRelease;
        }

        private void OnDisable()
        {
            PointableCanvasModule.WhenSelectableHovered -= HandleCanvasHover;
            PointableCanvasModule.WhenSelectableUnhovered -= HandleCanvasUnhover;
            PointableCanvasModule.WhenSelected -= HandleCanvasPress;
            PointableCanvasModule.WhenUnselected -= HandleCanvasRelease;
        }

        private void Update()
        {
            if (UpdateAuthoredMenuTestMode()) return;
            bool microphoneRecording = recordingGestureController != null
                && recordingGestureController.IsMicrophoneRecording;
            if (microphoneRecording)
            {
                if (!microphoneMenuLock)
                {
                    microphoneMenuLock = true;
                    CloseMenuInternal(OpeningHand.None);
                    ResetOpeningSequence();
                }
                if (fistProgressRoot != null) fistProgressRoot.SetActive(false);
                return;
            }
            if (microphoneMenuLock)
            {
                microphoneMenuLock = false;
                ResetOpeningSequence();
            }
            ResetFrameMetrics(left);
            ResetFrameMetrics(right);
            bool leftOpen = IsOpenPalm(left, true, out Vector3 leftAnchor);
            bool rightOpen = IsOpenPalm(right, false, out Vector3 rightAnchor);
            bool leftFist = IsFist(left, true);
            bool rightFist = IsFist(right, false);

            if (menuCanvas != null && menuCanvas.gameObject.activeSelf)
            {
                UpdateMenuFollowing();
                bool ownerTracked = IsOwnerTracked(menuOwner);
                bool ownerFist = menuOwner == OpeningHand.Left ? leftFist : rightFist;
                if (ownerTracked && ownerFist)
                {
                    closeFistTimer += Time.unscaledDeltaTime;
                    if (closeFistTimer >= closeFistHoldDuration) CloseMenuInternal(menuOwner);
                }
                else if (ownerTracked)
                {
                    closeFistTimer = 0f;
                }
                return;
            }

            switch (gestureState)
            {
                case MenuGestureState.WaitingForFist:
                    OpeningHand candidate = leftFist ? OpeningHand.Left
                        : rightFist ? OpeningHand.Right : OpeningHand.None;
                    if (candidate != OpeningHand.None)
                    {
                        sequenceOwner = candidate;
                        gestureHoldTimer = 0f;
                        trackingLossTimer = 0f;
                        gestureState = MenuGestureState.FistHold;
                    }
                    break;

                case MenuGestureState.FistHold:
                    if (!IsOwnerTracked(sequenceOwner))
                    {
                        if (AccumulateTrackingLoss()) ResetOpeningSequence();
                        break;
                    }
                    trackingLossTimer = 0f;
                    if (!GetOwnerPose(sequenceOwner, leftFist, rightFist))
                    {
                        ResetOpeningSequence();
                        break;
                    }
                    gestureHoldTimer += Time.unscaledDeltaTime;
                    if (gestureHoldTimer >= fistArmHoldDuration)
                    {
                        gestureHoldTimer = 0f;
                        sequenceTimer = 0f;
                        fistReleaseTimer = 0f;
                        gestureState = MenuGestureState.WaitingForOpen;
                    }
                    break;

                case MenuGestureState.WaitingForOpen:
                case MenuGestureState.OpenHold:
                    sequenceTimer += Time.unscaledDeltaTime;
                    if (sequenceTimer > fistToOpenTimeout)
                    {
                        ResetOpeningSequence();
                        break;
                    }
                    if (!IsOwnerTracked(sequenceOwner))
                    {
                        if (AccumulateTrackingLoss()) ResetOpeningSequence();
                        break;
                    }
                    trackingLossTimer = 0f;
                    bool ownerStillFist = GetOwnerPose(sequenceOwner, leftFist, rightFist);
                    if (ownerStillFist)
                    {
                        fistReleaseTimer = 0f;
                        gestureHoldTimer = 0f;
                        gestureState = MenuGestureState.WaitingForOpen;
                        break;
                    }
                    fistReleaseTimer += Time.unscaledDeltaTime;
                    if (fistReleaseTimer < fistReleaseDebounce) break;
                    bool ownerOpen = GetOwnerPose(sequenceOwner, leftOpen, rightOpen);
                    if (!ownerOpen)
                    {
                        gestureHoldTimer = 0f;
                        gestureState = MenuGestureState.WaitingForOpen;
                        break;
                    }
                    if (gestureState != MenuGestureState.OpenHold)
                    {
                        gestureState = MenuGestureState.OpenHold;
                        gestureHoldTimer = 0f;
                    }
                    gestureHoldTimer += Time.unscaledDeltaTime;
                    if (gestureHoldTimer >= openPalmHoldDuration)
                    {
                        Vector3 anchor = sequenceOwner == OpeningHand.Left ? leftAnchor : rightAnchor;
                        OpenMenu(sequenceOwner, anchor);
                    }
                    break;

                case MenuGestureState.WaitingForRelease:
                    if (!IsOwnerTracked(sequenceOwner)) break;
                    bool stillFist = GetOwnerPose(sequenceOwner, leftFist, rightFist);
                    if (!stillFist) ResetOpeningSequence();
                    break;
            }
        }

        private static void ResetFrameMetrics(OpenPalmHand hand)
        {
            hand.fistRecognized = false;
            hand.openRecognized = false;
            hand.curledCount = 0;
            hand.extendedCount = 0;
            hand.classification = "NEUTRAL";
        }

        private bool AccumulateTrackingLoss()
        {
            trackingLossTimer += Time.unscaledDeltaTime;
            return trackingLossTimer >= sequenceTrackingLossGrace;
        }

        private bool IsOwnerTracked(OpeningHand owner)
        {
            OpenPalmHand hand = owner == OpeningHand.Left ? left : owner == OpeningHand.Right ? right : null;
            return hand != null && hand.hand != null && hand.hand.IsTracked;
        }

        private static bool GetOwnerPose(OpeningHand owner, bool leftPose, bool rightPose)
        {
            return owner == OpeningHand.Left ? leftPose : owner == OpeningHand.Right && rightPose;
        }

        private void ResetOpeningSequence()
        {
            sequenceOwner = OpeningHand.None;
            gestureState = MenuGestureState.WaitingForFist;
            gestureHoldTimer = sequenceTimer = trackingLossTimer = fistReleaseTimer = 0f;
        }

        private bool IsOpenPalm(OpenPalmHand hand, bool isLeft, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            hand.openRecognized = false;
            if (hand.hand == null || hand.skeleton == null)
            {
                SetPalmDiagnostic(isLeft, "Missing OVRHand or OVRSkeleton reference");
                return false;
            }
            if (!hand.hand.IsTracked)
            {
                SetPalmDiagnostic(isLeft, "Hand not tracked");
                return false;
            }
            if (!ResolveOpenPalmJoints(hand))
            {
                SetPalmDiagnostic(isLeft, "Required OpenXR joints unresolved");
                return false;
            }
            UpdateFingerRatios(hand);

            anchor = hand.palm != null ? hand.palm.position
                : (hand.index.proximal.position + hand.pinky.proximal.position + hand.wrist.position) / 3f;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            if (headset == null)
            {
                SetPalmDiagnostic(isLeft, "CenterEyeAnchor missing");
                return false;
            }

            Vector3 acrossPalm = hand.index.proximal.position - hand.pinky.proximal.position;
            Vector3 alongPalm = hand.middle.proximal.position - hand.wrist.position;
            Vector3 palmNormal = Vector3.Cross(acrossPalm, alongPalm).normalized;
            // OpenXR hand geometry is mirrored. Quest runtime measurements show
            // this cross product already points out of the left palm, while the
            // right-hand result must be inverted to preserve the anatomical sign.
            if (!isLeft) palmNormal = -palmNormal;
            Vector3 towardHeadset = (headset.position - anchor).normalized;
            float requiredUpDot = Mathf.Cos(palmUpTolerance * Mathf.Deg2Rad);
            float facingDot = Vector3.Dot(palmNormal, towardHeadset);
            float upDot = Vector3.Dot(palmNormal, Vector3.up);
            float facingAngle = Mathf.Acos(Mathf.Clamp(facingDot, -1f, 1f)) * Mathf.Rad2Deg;
            float upAngle = Mathf.Acos(Mathf.Clamp(upDot, -1f, 1f)) * Mathf.Rad2Deg;
            hand.palmFacingAngle = facingAngle;
            hand.palmUpAngle = upAngle;
            hand.palmUpDot = upDot;

            int extended = 0;
            if (hand.indexRatio >= extendedFingerChordRatio) extended++;
            if (hand.middleRatio >= extendedFingerChordRatio) extended++;
            if (hand.ringRatio >= extendedFingerChordRatio) extended++;
            if (hand.pinkyRatio >= extendedFingerChordRatio) extended++;
            hand.extendedCount = extended;
            bool facesUp = upDot >= requiredUpDot;
            if (!facesUp)
            {
                SetPalmDiagnostic(isLeft, "Rejected: palm is not up; up dot " + upDot.ToString("0.00")
                    + " / angle " + upAngle.ToString("0") + "°; extended " + extended + "/4");
                return false;
            }
            if (extended < requiredExtendedFingers)
            {
                SetPalmDiagnostic(isLeft, "Only " + extended + "/4 fingers extended");
                return false;
            }
            SetPalmDiagnostic(isLeft, "OPEN PALM UP; dot " + upDot.ToString("0.00")
                + "; head " + facingAngle.ToString("0") + "° / up " + upAngle.ToString("0")
                + "°; extended " + extended + "/4");
            hand.openRecognized = true;
            hand.classification = "OPEN";
            return true;
        }

        private bool IsFist(OpenPalmHand hand, bool isLeft)
        {
            hand.fistRecognized = false;
            if (hand.hand == null || hand.skeleton == null)
            {
                SetFistDiagnostic(isLeft, "Missing hand or skeleton reference");
                return false;
            }
            if (!hand.hand.IsTracked)
            {
                SetFistDiagnostic(isLeft, "Hand not tracked");
                return false;
            }
            if (!ResolveOpenPalmJoints(hand))
            {
                SetFistDiagnostic(isLeft, "Required joints unresolved");
                return false;
            }
            UpdateFingerRatios(hand);

            bool indexCurled = hand.indexRatio <= curledFingerChordRatio;
            int curled = indexCurled ? 1 : 0;
            if (hand.middleRatio <= curledFingerChordRatio) curled++;
            if (hand.ringRatio <= curledFingerChordRatio) curled++;
            if (hand.pinkyRatio <= curledFingerChordRatio) curled++;
            hand.curledCount = curled;
            Vector3 palmCenter = (hand.index.proximal.position + hand.middle.proximal.position
                + hand.ring.proximal.position + hand.pinky.proximal.position) * 0.25f;
            float palmWidth = Vector3.Distance(hand.index.proximal.position, hand.pinky.proximal.position);
            hand.thumbPalmRatio = palmWidth > 0.001f
                ? Vector3.Distance(hand.thumbTip.position, palmCenter) / palmWidth : 10f;
            bool thumbFolded = hand.thumbPalmRatio <= thumbFoldedPalmWidthRatio;
            bool accepted = indexCurled && curled == 4 && thumbFolded;
            SetFistDiagnostic(isLeft, accepted
                ? "FIST accepted; all fingers curled and thumb folded"
                : !indexCurled ? "Rejected: index finger is not curled"
                : curled < 4 ? "Rejected: only " + curled + "/4 fingers curled"
                : "Rejected: thumb extended (tip-to-palm ratio " + hand.thumbPalmRatio.ToString("0.00")
                    + " > " + thumbFoldedPalmWidthRatio.ToString("0.00") + ")");
            hand.fistRecognized = accepted;
            hand.classification = accepted ? "FIST" : hand.openRecognized ? "OPEN" : "NEUTRAL";
            return accepted;
        }

        private static void UpdateFingerRatios(OpenPalmHand hand)
        {
            hand.indexRatio = GetFingerChordRatio(hand.index);
            hand.middleRatio = GetFingerChordRatio(hand.middle);
            hand.ringRatio = GetFingerChordRatio(hand.ring);
            hand.pinkyRatio = GetFingerChordRatio(hand.pinky);
        }

        private static float GetFingerChordRatio(FingerChain finger)
        {
            float path = Vector3.Distance(finger.proximal.position, finger.intermediate.position)
                + Vector3.Distance(finger.intermediate.position, finger.distal.position)
                + Vector3.Distance(finger.distal.position, finger.tip.position);
            if (path < 0.001f) return 1f;
            return Vector3.Distance(finger.proximal.position, finger.tip.position) / path;
        }

        private static bool ResolveOpenPalmJoints(OpenPalmHand hand)
        {
            if (hand.Resolved) return true;
            if (hand.skeleton.Bones == null) return false;
            bool openXr = hand.skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft
                || hand.skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight;
            foreach (OVRBone bone in hand.skeleton.Bones)
            {
                if (openXr) AssignOpenXr(hand, bone.Id, bone.Transform);
                else AssignLegacy(hand, bone.Id, bone.Transform);
            }
            return hand.Resolved;
        }

        private static void AssignOpenXr(OpenPalmHand h, OVRSkeleton.BoneId id, Transform t)
        {
            if (id == OVRSkeleton.BoneId.XRHand_Wrist) h.wrist = t;
            else if (id == OVRSkeleton.BoneId.XRHand_Palm) h.palm = t;
            else if (id == OVRSkeleton.BoneId.XRHand_ThumbProximal) h.thumbProximal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_ThumbDistal) h.thumbDistal = t;
            else if (id == OVRSkeleton.BoneId.XRHand_ThumbTip) h.thumbTip = t;
            else AssignFinger(h, id, t, true);
        }

        private static void AssignLegacy(OpenPalmHand h, OVRSkeleton.BoneId id, Transform t)
        {
            if (id == OVRSkeleton.BoneId.Hand_WristRoot) h.wrist = t;
            else if (id == OVRSkeleton.BoneId.Hand_Thumb1) h.thumbProximal = t;
            else if (id == OVRSkeleton.BoneId.Hand_Thumb3) h.thumbDistal = t;
            else if (id == OVRSkeleton.BoneId.Hand_ThumbTip) h.thumbTip = t;
            else AssignFinger(h, id, t, false);
        }

        private static void AssignFinger(OpenPalmHand h, OVRSkeleton.BoneId id, Transform t, bool xr)
        {
            if (xr)
            {
                SetChain(h.index, id, t, OVRSkeleton.BoneId.XRHand_IndexProximal, OVRSkeleton.BoneId.XRHand_IndexIntermediate, OVRSkeleton.BoneId.XRHand_IndexDistal, OVRSkeleton.BoneId.XRHand_IndexTip);
                SetChain(h.middle, id, t, OVRSkeleton.BoneId.XRHand_MiddleProximal, OVRSkeleton.BoneId.XRHand_MiddleIntermediate, OVRSkeleton.BoneId.XRHand_MiddleDistal, OVRSkeleton.BoneId.XRHand_MiddleTip);
                SetChain(h.ring, id, t, OVRSkeleton.BoneId.XRHand_RingProximal, OVRSkeleton.BoneId.XRHand_RingIntermediate, OVRSkeleton.BoneId.XRHand_RingDistal, OVRSkeleton.BoneId.XRHand_RingTip);
                SetChain(h.pinky, id, t, OVRSkeleton.BoneId.XRHand_LittleProximal, OVRSkeleton.BoneId.XRHand_LittleIntermediate, OVRSkeleton.BoneId.XRHand_LittleDistal, OVRSkeleton.BoneId.XRHand_LittleTip);
            }
            else
            {
                SetChain(h.index, id, t, OVRSkeleton.BoneId.Hand_Index1, OVRSkeleton.BoneId.Hand_Index2, OVRSkeleton.BoneId.Hand_Index3, OVRSkeleton.BoneId.Hand_IndexTip);
                SetChain(h.middle, id, t, OVRSkeleton.BoneId.Hand_Middle1, OVRSkeleton.BoneId.Hand_Middle2, OVRSkeleton.BoneId.Hand_Middle3, OVRSkeleton.BoneId.Hand_MiddleTip);
                SetChain(h.ring, id, t, OVRSkeleton.BoneId.Hand_Ring1, OVRSkeleton.BoneId.Hand_Ring2, OVRSkeleton.BoneId.Hand_Ring3, OVRSkeleton.BoneId.Hand_RingTip);
                SetChain(h.pinky, id, t, OVRSkeleton.BoneId.Hand_Pinky1, OVRSkeleton.BoneId.Hand_Pinky2, OVRSkeleton.BoneId.Hand_Pinky3, OVRSkeleton.BoneId.Hand_PinkyTip);
            }
        }

        private static void SetChain(FingerChain chain, OVRSkeleton.BoneId id, Transform t,
            OVRSkeleton.BoneId proximal, OVRSkeleton.BoneId intermediate,
            OVRSkeleton.BoneId distal, OVRSkeleton.BoneId tip)
        {
            if (id == proximal) chain.proximal = t;
            else if (id == intermediate) chain.intermediate = t;
            else if (id == distal) chain.distal = t;
            else if (id == tip) chain.tip = t;
        }

        private void EnsureMetaCanvasEventSystem()
        {
            EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject("Thinking Space UI Event System");
                eventSystem = eventObject.AddComponent<EventSystem>();
            }
            if (eventSystem.GetComponent<PointableCanvasModule>() == null)
            {
                PointableCanvasModule module = eventSystem.gameObject.AddComponent<PointableCanvasModule>();
                module.ExclusiveMode = false;
            }
        }

        private void BuildAuthoredMenu()
        {
            authoredView = Instantiate(authoredMenuPrefab, transform);
            authoredView.name = "Brainstorm Menu (Authored)";
            menuCanvas = authoredView.GetComponent<Canvas>();
            canvasRect = menuCanvas.GetComponent<RectTransform>();
            canvasBoundsClipper = authoredView.GetComponent<BoundsClipper>();
            authoredView.Bind(
                () => InvokeGuarded(StartNewSession),
                () => InvokeGuarded(() => OpenHelp(MenuScreen.VoiceHelp)),
                () => InvokeGuarded(() => OpenHelp(MenuScreen.GestureHelp)),
                () => InvokeGuarded(() => SetRecordingHand(ThumbRecordingGestureController.RecordingHand.Left)),
                () => InvokeGuarded(() => SetRecordingHand(ThumbRecordingGestureController.RecordingHand.Right)),
                () => InvokeGuarded(CloseMenu),
                () => InvokeGuarded(() => ShowPlaceholder("Challenge My Idea — Coming Soon")),
                () => InvokeGuarded(() => ShowPlaceholder("Expand My Idea — Coming Soon")),
                () => InvokeGuarded(() => ShowPlaceholder("Connect My Ideas — Coming Soon")),
                () => InvokeGuarded(() => ShowPlaceholder("Whiteboard — Coming Soon")),
                () => InvokeGuarded(EndSession),
                () => InvokeGuarded(() => ShowScreen(helpReturnScreen)));
            authoredView.SetFrontMarkerVisible(false);
            authoredView.gameObject.SetActive(false);
        }

        private bool UpdateAuthoredMenuTestMode()
        {
            if (authoredView == null) return false;
            if (!debugShowAuthoredMenuAtHeadset)
            {
                if (authoredTestWasVisible)
                {
                    authoredView.gameObject.SetActive(false);
                    authoredTestWasVisible = false;
                    ResetOpeningSequence();
                }
                return false;
            }

            Transform headset = recordingGestureController.CenterEyeAnchor;
            if (headset == null) return true;
            if (!authoredTestWasVisible)
            {
                authoredView.gameObject.SetActive(true);
                authoredView.ShowMain(sessionStore.Sessions, recordingGestureController.SelectedRecordingHand, ReopenSession);
                authoredTestWasVisible = true;
            }
            menuOwner = OpeningHand.None;
            authoredView.transform.position = headset.TransformPoint(authoredTestHeadsetOffset);
            authoredView.transform.rotation = headset.rotation;
            authoredView.SetFrontMarkerVisible(showFrontTestLabel);
            return true;
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Brainstorm Hand Menu");
            canvasObject.transform.SetParent(transform, false);
            menuCanvas = canvasObject.AddComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.WorldSpace;
            menuCanvas.overrideSorting = true;
            menuCanvas.sortingOrder = 20;
            menuCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            canvasObject.AddComponent<GraphicRaycaster>();
            PointableCanvas pointable = canvasObject.AddComponent<PointableCanvas>();
            pointable.InjectAllPointableCanvas(menuCanvas);
            canvasRect = menuCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = panelSize;
            canvasRect.localScale = Vector3.one * canvasScale;

            Image panel = canvasObject.AddComponent<Image>();
            panel.color = PanelColor;
            panel.raycastTarget = false;

            PlaneSurface plane = canvasObject.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, false);
            canvasBoundsClipper = canvasObject.AddComponent<BoundsClipper>();
            canvasBoundsClipper.Size = new Vector3(panelSize.x, panelSize.y, 0.01f);
            ClippedPlaneSurface clippedSurface = canvasObject.AddComponent<ClippedPlaneSurface>();
            clippedSurface.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { canvasBoundsClipper });
            RayInteractable rayInteractable = canvasObject.AddComponent<RayInteractable>();
            rayInteractable.InjectAllRayInteractable(clippedSurface);
            rayInteractable.InjectOptionalPointableElement(pointable);
            PokeInteractable pokeInteractable = canvasObject.AddComponent<PokeInteractable>();
            pokeInteractable.InjectAllPokeInteractable(clippedSurface);
            pokeInteractable.InjectOptionalPointableElement(pointable);
            RectTransform viewport = CreateRect("Scrollable Viewport", canvasRect);
            viewport.anchorMin = new Vector2(0.04f, 0.04f);
            viewport.anchorMax = new Vector2(0.96f, 0.96f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            Image viewportGraphic = viewport.gameObject.AddComponent<Image>();
            viewportGraphic.color = new Color(0f, 0f, 0f, 0.001f);
            viewportGraphic.raycastTarget = true;
            Mask mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            contentRoot = CreateRect("Content", viewport);
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;
            contentRoot.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = canvasObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = contentRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 25f;
            menuCanvas.gameObject.SetActive(false);
        }

        private void BuildDiagnostics()
        {
            diagnosticsRoot = new GameObject("Open Palm Diagnostics", typeof(RectTransform));
            diagnosticsRoot.transform.SetParent(transform, false);
            RectTransform rect = diagnosticsRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(82f, 48f);
            rect.localScale = Vector3.one * 0.01f;
            diagnosticsText = diagnosticsRoot.AddComponent<TextMeshPro>();
            diagnosticsText.font = menuFont;
            diagnosticsText.fontSize = 2.6f;
            diagnosticsText.color = Color.white;
            diagnosticsText.alignment = TextAlignmentOptions.TopLeft;
            diagnosticsText.textWrappingMode = TextWrappingModes.Normal;
            diagnosticsRoot.SetActive(false);
        }

        private void BuildFistProgress()
        {
            fistProgressRoot = new GameObject("Fist Hold Progress");
            fistProgressRoot.transform.SetParent(transform, false);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            progressBackgroundMaterial = CreateProgressMaterial(shader, new Color(0.04f, 0.07f, 0.11f, 0.92f), "Fist Progress Background");
            progressFillMaterial = CreateProgressMaterial(shader, new Color(0.1f, 0.8f, 1f, 1f), "Fist Progress Fill");

            Transform background = CreateProgressBlock("Background", fistProgressRoot.transform, progressBackgroundMaterial);
            background.localScale = new Vector3(0.085f, 0.012f, 0.003f);
            fistProgressFill = CreateProgressBlock("Fill", fistProgressRoot.transform, progressFillMaterial);
            fistProgressFill.localScale = new Vector3(0.001f, 0.007f, 0.004f);

            GameObject textObject = new GameObject("Progress Label", typeof(RectTransform));
            textObject.transform.SetParent(fistProgressRoot.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(130f, 25f);
            textRect.localScale = Vector3.one * 0.001f;
            textRect.localPosition = new Vector3(0f, 0.024f, -0.003f);
            fistProgressText = textObject.AddComponent<TextMeshPro>();
            fistProgressText.font = menuFont;
            fistProgressText.fontSize = 18f;
            fistProgressText.alignment = TextAlignmentOptions.Center;
            fistProgressText.color = Color.white;
            fistProgressText.text = "Hold Fist";
            fistProgressRoot.SetActive(false);
        }

        private void BuildFrontTestLabel()
        {
            RectTransform rect = CreateRect("FRONT Side Diagnostic", canvasRect);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -8f);
            rect.sizeDelta = new Vector2(180f, 44f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.15f, 1f, 0.25f, 0.95f);
            image.raycastTarget = false;
            TextMeshProUGUI text = AddText("FRONT", rect, 27f, TextAlignmentOptions.Center);
            text.color = Color.black;
            frontTestRoot = rect.gameObject;
            frontTestRoot.SetActive(false);
        }

        private static Material CreateProgressMaterial(Shader shader, Color color, string materialName)
        {
            if (shader == null) return null;
            Material material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }

        private static Transform CreateProgressBlock(string name, Transform parent, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            Collider collider = block.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block.transform;
        }

        private void LateUpdate()
        {
            UpdateFistProgressVisual();
            if (frontTestRoot != null)
            {
                frontTestRoot.SetActive(showFrontTestLabel && menuCanvas != null && menuCanvas.gameObject.activeSelf);
                if (frontTestRoot.activeSelf) frontTestRoot.transform.SetAsLastSibling();
            }
            if (authoredView != null && !debugShowAuthoredMenuAtHeadset)
                authoredView.SetFrontMarkerVisible(showFrontTestLabel && menuCanvas != null && menuCanvas.gameObject.activeSelf);
            if (diagnosticsRoot == null) return;
            bool showDiagnostics = showOpenPalmDiagnostics || showInteractionDiagnostics || showCanvasFrontDiagnostics;
            diagnosticsRoot.SetActive(showDiagnostics);
            if (!showDiagnostics) return;
            Transform headset = recordingGestureController != null
                ? recordingGestureController.CenterEyeAnchor : null;
            if (headset == null) return;
            diagnosticsRoot.transform.position = headset.TransformPoint(diagnosticsHeadsetOffset);
            Vector3 diagnosticsForward = diagnosticsRoot.transform.position - headset.position;
            if (diagnosticsForward.sqrMagnitude > 0.001f)
                diagnosticsRoot.transform.rotation = Quaternion.LookRotation(diagnosticsForward.normalized, Vector3.up);
            string palmStatus = showOpenPalmDiagnostics
                ? "FIST-TO-OPEN MENU   LEFT: " + left.classification + "   RIGHT: " + right.classification
                    + FormatHandMetrics("Left", left, leftFistDiagnostic, leftPalmDiagnostic)
                    + FormatHandMetrics("Right", right, rightFistDiagnostic, rightPalmDiagnostic)
                    + "\nState: " + gestureState + "   Sequence hand: " + sequenceOwner
                    + "   Hold: " + gestureHoldTimer.ToString("0.00")
                    + "   Window: " + sequenceTimer.ToString("0.00") + "/" + fistToOpenTimeout.ToString("0.00")
                    + "\nOwner: " + menuOwner + "   Close fist: " + closeFistTimer.ToString("0.00")
                : string.Empty;
            string interactionStatus = showInteractionDiagnostics
                ? (palmStatus.Length > 0 ? "\n" : string.Empty) + "UI: " + interactionDiagnostic
                : string.Empty;
            string canvasStatus = showCanvasFrontDiagnostics ? GetCanvasFrontDiagnostic(headset) : string.Empty;
            diagnosticsText.text = palmStatus + interactionStatus + canvasStatus
                + "\nMenu: " + (menuCanvas != null && menuCanvas.gameObject.activeSelf ? "OPEN" : "closed");
        }

        private static string FormatHandMetrics(string name, OpenPalmHand hand, string fistReason, string openReason)
        {
            return "\n[" + name + "] Fist: " + (hand.fistRecognized ? "YES" : "no")
                + "   Open: " + (hand.openRecognized ? "YES" : "no")
                + "   Curled: " + hand.curledCount + "/4   Extended: " + hand.extendedCount + "/4"
                + "\nRatios I/M/R/P: " + hand.indexRatio.ToString("0.00") + "/"
                + hand.middleRatio.ToString("0.00") + "/" + hand.ringRatio.ToString("0.00")
                + "/" + hand.pinkyRatio.ToString("0.00")
                + "   Head: " + hand.palmFacingAngle.ToString("0") + "°   Up: "
                + hand.palmUpAngle.ToString("0") + "°   Up dot: " + hand.palmUpDot.ToString("0.00")
                + "   Thumb/palm: " + hand.thumbPalmRatio.ToString("0.00")
                + "\nFist reason: " + fistReason + "\nOpen reason: " + openReason;
        }

        private void UpdateFistProgressVisual()
        {
            if (fistProgressRoot == null) return;
            bool holdingFist = gestureState == MenuGestureState.FistHold;
            bool showingReady = (gestureState == MenuGestureState.WaitingForOpen
                || gestureState == MenuGestureState.OpenHold) && sequenceTimer <= readyMessageDuration;
            bool visible = menuCanvas != null && !menuCanvas.gameObject.activeSelf
                && !microphoneMenuLock
                && sequenceOwner != OpeningHand.None && (holdingFist || showingReady)
                && IsOwnerTracked(sequenceOwner);
            fistProgressRoot.SetActive(visible);
            if (!visible) return;

            OpenPalmHand owner = sequenceOwner == OpeningHand.Left ? left : right;
            if (!ResolveOpenPalmJoints(owner))
            {
                fistProgressRoot.SetActive(false);
                return;
            }
            Vector3 anchor = owner.palm != null ? owner.palm.position
                : (owner.index.proximal.position + owner.pinky.proximal.position + owner.wrist.position) / 3f;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            Vector3 towardHeadset = headset != null ? (headset.position - anchor).normalized : Vector3.zero;
            fistProgressRoot.transform.position = anchor + Vector3.up * progressOffsetFromHand.y
                + (headset != null ? headset.right * progressOffsetFromHand.x : Vector3.zero)
                + towardHeadset * progressOffsetFromHand.z;
            if (headset != null)
            {
                Vector3 forward = fistProgressRoot.transform.position - headset.position;
                if (forward.sqrMagnitude > 0.001f)
                    fistProgressRoot.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }

            float progress = showingReady ? 1f : Mathf.Clamp01(gestureHoldTimer / fistArmHoldDuration);
            float fillWidth = 0.079f * Mathf.Max(0.001f, progress);
            fistProgressFill.localScale = new Vector3(fillWidth, 0.007f, 0.004f);
            fistProgressFill.localPosition = new Vector3(-0.0395f + fillWidth * 0.5f, 0f, -0.003f);
            fistProgressText.text = showingReady
                ? "Ready to Open"
                : "Hold Fist  " + Mathf.RoundToInt(progress * 100f) + "%";
        }

        private string GetCanvasFrontDiagnostic(Transform headset)
        {
            if (menuCanvas == null) return "\nCanvas: missing";
            Vector3 towardHeadset = (headset.position - menuCanvas.transform.position).normalized;
            Vector3 visibleFront = -menuCanvas.transform.forward;
            float frontDot = Vector3.Dot(visibleFront, towardHeadset);
            TMP_Text sampleText = menuCanvas.GetComponentInChildren<TMP_Text>(true);
            bool fontValid = menuFont != null && menuFont.atlasTexture != null;
            bool materialValid = menuFont != null && menuFont.material != null && menuFont.material.shader != null;
            int characters = 0;
            int vertices = 0;
            if (sampleText != null && sampleText.gameObject.activeInHierarchy)
            {
                sampleText.ForceMeshUpdate(false, false);
                characters = sampleText.textInfo.characterCount;
                if (sampleText.textInfo.meshInfo != null && sampleText.textInfo.meshInfo.Length > 0)
                    vertices = sampleText.textInfo.meshInfo[0].vertexCount;
            }
            return "\nCANVAS FRONT"
                + "\nVisible front: " + visibleFront.ToString("F2")
                + "   Toward headset: " + towardHeadset.ToString("F2")
                + "   Dot: " + frontDot.ToString("0.00")
                + "\nScale: " + menuCanvas.transform.lossyScale.ToString("F5")
                + "   Font/Material: " + (fontValid ? "OK" : "INVALID") + "/" + (materialValid ? "OK" : "INVALID")
                + "   Text chars/vertices: " + characters + "/" + vertices;
        }

        private void HandleCanvasHover(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
                interactionDiagnostic = "Hover: " + GetInteractionObjectName(args.Hovered);
        }

        private void HandleCanvasUnhover(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
                interactionDiagnostic = "Hover ended: " + GetInteractionObjectName(args.Hovered);
        }

        private void HandleCanvasPress(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
                interactionDiagnostic = "Press: " + GetInteractionObjectName(args.Hovered);
        }

        private void HandleCanvasRelease(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
                interactionDiagnostic = "Release: " + GetInteractionObjectName(args.Hovered);
        }

        private static string GetInteractionObjectName(GameObject target)
        {
            return target != null ? target.name : "<none>";
        }

        private void SetPalmDiagnostic(bool isLeft, string message)
        {
            if (isLeft) leftPalmDiagnostic = message;
            else rightPalmDiagnostic = message;
        }

        private void SetFistDiagnostic(bool isLeft, string message)
        {
            if (isLeft) leftFistDiagnostic = message;
            else rightFistDiagnostic = message;
        }

        private void OpenMenu(OpeningHand owner, Vector3 handAnchor)
        {
            menuOwner = owner;
            sequenceOwner = OpeningHand.None;
            gestureHoldTimer = sequenceTimer = trackingLossTimer = fistReleaseTimer = closeFistTimer = 0f;
            followVelocity = Vector3.zero;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            Vector3 position = GetMenuTargetPosition(handAnchor, headset);
            menuCanvas.transform.position = position;
            if (headset != null)
            {
                Vector3 forward = position - headset.position;
                if (forward.sqrMagnitude > 0.001f)
                    menuCanvas.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
            menuCanvas.gameObject.SetActive(true);
            ShowScreen(sessionStore.ActiveSession == null ? MenuScreen.Main : MenuScreen.Session);
        }

        private void UpdateMenuFollowing()
        {
            if (menuCanvas == null || !menuCanvas.gameObject.activeSelf || menuOwner == OpeningHand.None) return;
            OpenPalmHand owner = menuOwner == OpeningHand.Left ? left : right;
            if (owner.hand == null || owner.skeleton == null || !owner.hand.IsTracked
                || !ResolveOpenPalmJoints(owner)) return;

            Vector3 anchor = owner.palm != null ? owner.palm.position
                : (owner.index.proximal.position + owner.pinky.proximal.position + owner.wrist.position) / 3f;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            Vector3 targetPosition = GetMenuTargetPosition(anchor, headset);
            menuCanvas.transform.position = Vector3.SmoothDamp(
                menuCanvas.transform.position, targetPosition, ref followVelocity,
                followSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            if (headset == null) return;
            Vector3 forward = menuCanvas.transform.position - headset.position;
            if (forward.sqrMagnitude < 0.001f) return;
            Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            float rotationBlend = 1f - Mathf.Exp(-facingSmoothSpeed * Time.unscaledDeltaTime);
            menuCanvas.transform.rotation = Quaternion.Slerp(menuCanvas.transform.rotation, targetRotation, rotationBlend);
        }

        private Vector3 GetMenuTargetPosition(Vector3 handAnchor, Transform headset)
        {
            Vector3 towardHeadset = headset != null
                ? (headset.position - handAnchor).normalized : Vector3.zero;
            Vector3 horizontal = headset != null ? headset.right * menuOffsetFromHand.x : Vector3.zero;
            return handAnchor + horizontal + Vector3.up * menuOffsetFromHand.y
                + towardHeadset * menuOffsetFromHand.z;
        }

        public void CloseMenu()
        {
            CloseMenuInternal(menuOwner);
        }

        private void CloseMenuInternal(OpeningHand releasingHand)
        {
            if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
            sequenceOwner = releasingHand;
            menuOwner = OpeningHand.None;
            gestureHoldTimer = sequenceTimer = trackingLossTimer = fistReleaseTimer = closeFistTimer = 0f;
            gestureState = releasingHand == OpeningHand.None
                ? MenuGestureState.WaitingForFist : MenuGestureState.WaitingForRelease;
        }

        private void ShowScreen(MenuScreen screen)
        {
            currentScreen = screen;
            if (authoredView != null)
            {
                if (screen == MenuScreen.Main)
                    authoredView.ShowMain(sessionStore.Sessions, recordingGestureController.SelectedRecordingHand, ReopenSession);
                else if (screen == MenuScreen.Session)
                    authoredView.ShowSession(
                        sessionStore.ActiveSession != null ? sessionStore.ActiveSession.Number : 0,
                        recordingGestureController.SelectedRecordingHand);
                else if (screen == MenuScreen.VoiceHelp) authoredView.ShowVoiceHelp();
                else authoredView.ShowGestureHelp();
                return;
            }
            ClearContent();
            if (screen == MenuScreen.Main) BuildMainScreen();
            else if (screen == MenuScreen.Session) BuildSessionScreen();
            else BuildHelpScreen(screen);
        }

        private void BuildMainScreen()
        {
            ResizePanel(650f + sessionStore.Sessions.Count * 68f);
            AddHeading("Brainstorm", 46f);
            AddLabel("Brainstorming Sessions", 29f);
            if (sessionStore.Sessions.Count == 0) AddLabel("No sessions yet", 24f, new Color(0.7f, 0.75f, 0.82f));
            else
            {
                foreach (BrainstormSession session in sessionStore.Sessions)
                {
                    int number = session.Number;
                    AddButton("Session " + number, () => ReopenSession(number));
                }
            }
            AddButton("Start New Session", StartNewSession, true);
            AddButton("Voice Commands", () => OpenHelp(MenuScreen.VoiceHelp));
            AddButton("Hand Gestures", () => OpenHelp(MenuScreen.GestureHelp));
            AddRecordingHandSelector();
            AddButton("Close Menu", CloseMenu);
        }

        private void BuildSessionScreen()
        {
            ResizePanel(840f);
            int number = sessionStore.ActiveSession != null ? sessionStore.ActiveSession.Number : 0;
            AddHeading("Session " + number, 44f);
            AddButton("Challenge My Idea", () => ShowPlaceholder("Challenge My Idea — Coming Soon"));
            AddButton("Expand My Idea", () => ShowPlaceholder("Expand My Idea — Coming Soon"));
            AddButton("Connect My Ideas", () => ShowPlaceholder("Connect My Ideas — Coming Soon"));
            AddButton("Spawn Whiteboard", () => ShowPlaceholder("Whiteboard — Coming Soon"));
            AddButton("Voice Commands", () => OpenHelp(MenuScreen.VoiceHelp));
            AddButton("Hand Gestures", () => OpenHelp(MenuScreen.GestureHelp));
            AddRecordingHandSelector();
            AddButton("End Session", EndSession);
            AddButton("Close Menu", CloseMenu);
        }

        private void BuildHelpScreen(MenuScreen screen)
        {
            ResizePanel(270f);
            bool voice = screen == MenuScreen.VoiceHelp;
            AddHeading(voice ? "Voice Commands" : "Hand Gestures", 42f);
            AddLabel(voice
                ? "Voice command reference will be added with speech recognition."
                : "Gesture reference will be expanded as interactions are finalized.", 26f);
            AddButton("Back", () => ShowScreen(helpReturnScreen));
        }

        private void AddRecordingHandSelector()
        {
            AddLabel("Recording Hand", 27f);
            AddButton("Left" + (recordingGestureController.SelectedRecordingHand == ThumbRecordingGestureController.RecordingHand.Left ? "  ✓" : string.Empty),
                () => SetRecordingHand(ThumbRecordingGestureController.RecordingHand.Left));
            AddButton("Right" + (recordingGestureController.SelectedRecordingHand == ThumbRecordingGestureController.RecordingHand.Right ? "  ✓" : string.Empty),
                () => SetRecordingHand(ThumbRecordingGestureController.RecordingHand.Right));
        }

        private void SetRecordingHand(ThumbRecordingGestureController.RecordingHand hand)
        {
            recordingGestureController.SetSelectedRecordingHand(hand);
            ShowScreen(currentScreen);
        }

        private void StartNewSession()
        {
            BrainstormSession session = sessionStore.StartNewSession();
            recordingGestureController.SetSessionIdentifier("Session " + session.Number);
            recordingGestureController.SetSessionActive(true);
            ShowScreen(MenuScreen.Session);
        }

        private void ReopenSession(int number)
        {
            if (!sessionStore.ReopenSession(number)) return;
            recordingGestureController.SetSessionActive(true);
            recordingGestureController.SetSessionIdentifier("Session " + number);
            ShowScreen(MenuScreen.Session);
        }

        private void EndSession()
        {
            recordingGestureController.SetSessionActive(false);
            sessionStore.EndActiveSession();
            ShowScreen(MenuScreen.Main);
        }

        private void ResizePanel(float pixelHeight)
        {
            if (canvasRect == null) return;
            float height = Mathf.Min(pixelHeight, panelSize.y);
            canvasRect.sizeDelta = new Vector2(panelSize.x, height);
            if (canvasBoundsClipper != null)
                canvasBoundsClipper.Size = new Vector3(panelSize.x, height, 0.01f);
        }

        private void OpenHelp(MenuScreen helpScreen)
        {
            helpReturnScreen = currentScreen;
            ShowScreen(helpScreen);
        }

        private void ShowPlaceholder(string message)
        {
            if (authoredView != null)
            {
                authoredView.SetSessionStatus(message);
                return;
            }
            AddLabel(message, 22f, new Color(1f, 0.78f, 0.25f));
        }

        private Button AddButton(string label, Action action, bool accent = false)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(contentRoot, false);
            LayoutElement element = buttonObject.GetComponent<LayoutElement>();
            element.preferredHeight = 58f;
            Image image = buttonObject.GetComponent<Image>();
            image.color = accent ? AccentColor : ButtonColor;
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.65f, 0.75f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                interactionDiagnostic = "Click: " + label;
                InvokeGuarded(action);
            });
            AddText(label, buttonObject.GetComponent<RectTransform>(), 25f, TextAlignmentOptions.Center);
            return button;
        }

        private void InvokeGuarded(Action action)
        {
            if (Time.unscaledTime - lastButtonActivation < duplicateClickGuard) return;
            lastButtonActivation = Time.unscaledTime;
            action?.Invoke();
        }

        private void AddHeading(string text, float size) => AddLabel(text, size, Color.white, 76f, FontStyles.Bold);

        private void AddLabel(string text, float size, Color? color = null, float height = 48f, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = CreateRect(text, contentRoot);
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            TextMeshProUGUI tmp = AddText(text, rect, size, TextAlignmentOptions.Center);
            tmp.color = color ?? Color.white;
            tmp.fontStyle = style;
        }

        private TextMeshProUGUI AddText(string value, RectTransform parent, float size, TextAlignmentOptions alignment)
        {
            RectTransform rect = CreateRect("Text", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = menuFont;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private void ClearContent()
        {
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
                Destroy(contentRoot.GetChild(i).gameObject);
        }

        private void OnDestroy()
        {
            if (progressBackgroundMaterial != null) Destroy(progressBackgroundMaterial);
            if (progressFillMaterial != null) Destroy(progressFillMaterial);
        }
    }
}

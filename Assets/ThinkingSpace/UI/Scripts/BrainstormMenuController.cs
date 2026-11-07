using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.Speech;
using ThinkingSpace.Companion;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class BrainstormMenuController : MonoBehaviour
    {
        public event Action PalmMenuOpened;
        public event Action<BrainstormSession> SessionActivated;
        private enum MenuScreen { Main, Session, VoiceHelp, GestureHelp }
        private enum OpeningHand { None, Left, Right }
        private enum MenuButtonState { WaitingForPalm, Stabilizing, Visible, WaitingForRelease }
        private enum MenuGestureState { WaitingForFist, FistHold, WaitingForOpen, OpenHold, WaitingForRelease }

        [Header("Existing References")]
        [SerializeField] private ThumbRecordingGestureController recordingGestureController;
        [SerializeField] private BrainstormMenuView authoredMenuPrefab;
        [SerializeField] private PalmMenuButtonView palmMenuButtonPrefab;

        [Header("Authored UI Test")]
        [SerializeField] private bool debugShowAuthoredMenuAtHeadset;
        [SerializeField] private Vector3 authoredTestHeadsetOffset = new Vector3(0f, -0.03f, 0.55f);
        [SerializeField] private bool debugShowPalmMenuButtonAtHeadset;
        [SerializeField] private Vector3 palmButtonTestHeadsetOffset = new Vector3(0f, -0.12f, 0.5f);

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

        [Header("Palm Menu Button")]
        [SerializeField, Min(0.05f)] private float palmButtonHoldDuration = 0.2f;
        [SerializeField] private Vector3 palmButtonOffset = new Vector3(0f, 0.055f, 0.015f);
        [SerializeField, Min(0.01f)] private float palmButtonFollowSmoothTime = 0.06f;
        [SerializeField, Min(0.05f)] private float ownerTrackingLossGrace = 1f;
        [SerializeField, Range(46f, 85f)] private float palmUpExitTolerance = 75f;
        [SerializeField, Min(0.05f)] private float palmPoseLossGrace = 0.5f;
        [FormerlySerializedAs("interactionEnableDelay")]
        [SerializeField, HideInInspector] private float legacyInteractionEnableDelay;

        [Header("Palm Normal Development Calibration")]
        [SerializeField] private bool developmentPalmNormalCalibration;
        [SerializeField] private bool captureLeftPalmUpPose;
        [SerializeField] private bool captureRightPalmUpPose;
        [SerializeField] private bool showPalmNormalVisualization;
        // Quest runtime evidence showed the prior (-1/+1) convention accepted palm-down.
        // Keep these serialized and independently calibratable rather than hard-coding a cross-product flip.
        [SerializeField, Range(-1f, 1f)] private float leftPalmNormalSign = 1f;
        [SerializeField, Range(-1f, 1f)] private float rightPalmNormalSign = -1f;

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
        [SerializeField, Range(1, 4)] private int diagnosticPage = 1;
        [SerializeField] private Vector3 diagnosticsHeadsetOffset = new Vector3(0f, 0f, 0.72f);

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
            public Vector3 rawPalmNormalWorld, palmNormalWorld;
            public float calibrationSign = 1f;
            public bool calibrated;
            public bool tracked, jointsResolved;
            public bool fistRecognized, openRecognized;
            public string classification = "NEUTRAL";
            public float thumbPalmRatio = 10f;
            public bool Resolved => wrist != null && thumbProximal != null && thumbDistal != null
                && thumbTip != null && index.Resolved && middle.Resolved && ring.Resolved && pinky.Resolved;
        }

        private BrainstormSessionStore sessionStore;
        private readonly OpenPalmHand left = new OpenPalmHand();
        private readonly OpenPalmHand right = new OpenPalmHand();
        private Canvas menuCanvas;
        private Canvas palmButtonCanvas;
        private PalmMenuButtonView palmMenuButtonView;
        private BrainstormMenuView authoredView;
        private RectTransform contentRoot;
        private RectTransform canvasRect;
        private BoundsClipper canvasBoundsClipper;
        private GameObject diagnosticsRoot;
        private TextMeshPro diagnosticsText;
        private GameObject diagnosticsBackground;
        private Material diagnosticsBackgroundMaterial;
        private GameObject fistProgressRoot;
        private Transform fistProgressFill;
        private TextMeshPro fistProgressText;
        private Material progressBackgroundMaterial;
        private Material progressFillMaterial;
        private GameObject frontTestRoot;
        private bool authoredTestWasVisible;
        private bool palmButtonTestWasVisible;
        private bool palmButtonDebugOpenedMenu;
        private MenuScreen currentScreen = MenuScreen.Main;
        private MenuScreen helpReturnScreen = MenuScreen.Main;
        private OpeningHand buttonOwner;
        private OpeningHand rearmOwner;
        // Legacy diagnostic fields retained for serialized compatibility; fist activation is no longer evaluated.
        private OpeningHand sequenceOwner;
        private OpeningHand menuOwner;
        private MenuButtonState buttonState = MenuButtonState.WaitingForPalm;
        private MenuGestureState gestureState = MenuGestureState.WaitingForFist;
        private float gestureHoldTimer;
        private float sequenceTimer;
        private float trackingLossTimer;
        private float fistReleaseTimer;
        private float closeFistTimer;
        private float palmPoseLossTimer;
        private float lastButtonActivation = -10f;
        private string leftPalmDiagnostic = "Not evaluated";
        private string rightPalmDiagnostic = "Not evaluated";
        private string leftFistDiagnostic = "Not evaluated";
        private string rightFistDiagnostic = "Not evaluated";
        private string interactionDiagnostic = "No UI interaction received";
        private string lastPointerHover = "None", lastPointerPress = "None", lastPointerClick = "None";
        private Vector3 followVelocity;
        private Vector3 palmButtonFollowVelocity;
        private Vector3 lastStableButtonAnchor;
        private bool microphoneMenuLock;
        private bool rearmRequired;
        private float canvasFacingDot;
        private string trackingLossStatus = "None";
        private string buttonConstructionStatus = "Not built";
        private string buttonDismissalReason = "None";
        private TextMeshProUGUI palmButtonLabel;
        private Button palmMenuButton;
        private Image palmMenuButtonImage;
        private GraphicRaycaster palmMenuGraphicRaycaster;
        private float debugClickConfirmationUntil;
        private bool palmButtonVisualVisible;
        private bool palmButtonInteractionActive;
        private bool palmButtonPoseLocked;
        private bool menuPoseLocked;
        private bool palmButtonHovered;
        private bool palmButtonSelected;
        private bool menuHovered;
        private bool menuSelected;
        private bool debugButtonPosePlaced;
        private Vector3 debugButtonFrozenPosition;
        private Quaternion debugButtonFrozenRotation;
        private int interactionEventSequence;
        private readonly Queue<string> interactionEventHistory = new Queue<string>(12);
        private string menuVisibilityDiagnostic = "Menu not opened";
        private int buttonTransitionCount;
        private string lastButtonTransition = "Initialized";
        private readonly PalmMenuActivationGate toggleGate = new PalmMenuActivationGate();
        private string lastToggleGateResult = "No toggle attempted";
        private HandNormalVisualization leftNormalVisual, rightNormalVisual;

        private sealed class HandNormalVisualization
        {
            public GameObject root;
            public Transform marker;
            public LineRenderer normalLine, upLine;
        }

        public BrainstormSessionStore SessionStore => sessionStore;
        public ThinkingSpacePreferencesStore PreferencesStore => preferencesStore;
        public bool IsMenuVisible => menuCanvas != null && menuCanvas.gameObject.activeSelf;
        public string DevelopmentMenuStatus => "Menu visible=" + IsMenuVisible
            + " · owner=" + menuOwner + " · button=" + buttonState + "/" + buttonOwner
            + " · gate=" + toggleGate.ConsumedGeneration + "/" + toggleGate.InteractionGeneration
            + " held=" + toggleGate.PointerHeld + " · " + lastToggleGateResult
            + " · " + menuVisibilityDiagnostic;
        public void SetDevelopmentDiagnosticsVisible(bool visible)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            showOpenPalmDiagnostics = visible;
            showInteractionDiagnostics = visible;
            showCanvasFrontDiagnostics = visible;
            showPalmNormalVisualization = visible;
#else
            showOpenPalmDiagnostics = showInteractionDiagnostics = showCanvasFrontDiagnostics = false;
            showPalmNormalVisualization = false;
#endif
        }
        private ThinkingSpacePreferencesStore preferencesStore;

        private static readonly Color PanelColor = new Color(0.035f, 0.055f, 0.09f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.11f, 0.2f, 0.32f, 1f);
        private static readonly Color AccentColor = new Color(0.1f, 0.65f, 1f, 1f);

        private void Awake()
        {
            // MonoBehaviour field initializers run during Unity object construction, where
            // Application.persistentDataPath is forbidden. Resolve it on the main thread here.
            if (sessionStore == null) sessionStore = BrainstormSessionStore.CreateForApplication();
            if (preferencesStore == null) preferencesStore = ThinkingSpacePreferencesStore.CreateForApplication();
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
            bool restoredSession = sessionStore.ActiveSession != null;
            if (restoredSession)
                recordingGestureController.SetSessionIdentifier("Session " + sessionStore.ActiveSession.Number);
            // Restoring session data and completing onboarding are independent. Never
            // enable recording during the Awake/Start gap when onboarding is incomplete.
            bool onboardingRequired=OrbyOnboardingController.ShouldBeginTutorial(preferencesStore.Data);
            recordingGestureController.SetSessionActive(restoredSession&&!onboardingRequired);
            ApplyPreferences();
            EnsureMetaCanvasEventSystem();
            if (authoredMenuPrefab != null) BuildAuthoredMenu();
            else BuildCanvas();
            BuildPalmMenuButton();
            BuildDiagnostics();
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
            if (UpdatePalmButtonTestMode()) return;
            bool microphoneRecording = recordingGestureController != null
                && recordingGestureController.ShouldLockMenuForRecording;
            if (microphoneRecording)
            {
                if (!microphoneMenuLock)
                {
                    microphoneMenuLock = true;
                    CloseMenuInternal(OpeningHand.None);
                    ResetPalmButton(true);
                }
                SetPalmButtonVisible(false);
                return;
            }
            if (microphoneMenuLock)
            {
                microphoneMenuLock = false;
                ResetPalmButton(true);
            }
            ResetFrameMetrics(left);
            ResetFrameMetrics(right);
            bool leftPalmUp = IsPalmUp(left, true, out Vector3 leftAnchor);
            bool rightPalmUp = IsPalmUp(right, false, out Vector3 rightAnchor);
            HandlePalmCalibration(left, true);
            HandlePalmCalibration(right, false);

            if (menuCanvas != null && menuCanvas.gameObject.activeSelf)
            {
                // The authored full menu is now the sole menu surface while open.
                // Ownership is retained so CloseMenuInternal can enforce rearm.
                SetPalmButtonVisible(false);
                return;
            }

            if (rearmRequired)
            {
                SetPalmButtonVisible(false);
                bool releaseObserved = rearmOwner == OpeningHand.Left ? !leftPalmUp
                    : rearmOwner == OpeningHand.Right ? !rightPalmUp : !leftPalmUp && !rightPalmUp;
                if (releaseObserved)
                {
                    rearmRequired = false;
                    SetButtonState(MenuButtonState.WaitingForPalm, "Rearm owner left palm-up pose");
                    buttonOwner = OpeningHand.None;
                    rearmOwner = OpeningHand.None;
                }
                return;
            }

            if (buttonState == MenuButtonState.Visible)
            {
                if (!IsOwnerTracked(buttonOwner))
                {
                    trackingLossTimer += Time.unscaledDeltaTime;
                    trackingLossStatus = "Button owner tracking lost " + trackingLossTimer.ToString("0.00") + "s";
                    if (trackingLossTimer >= ownerTrackingLossGrace)
                    {
                        buttonDismissalReason = "Owner tracking lost beyond grace period";
                        lastButtonTransition = buttonDismissalReason;
                        ResetPalmButton(false);
                    }
                }
                else
                {
                    trackingLossTimer = 0f; trackingLossStatus = "None";
                    OpenPalmHand owner = buttonOwner == OpeningHand.Left ? left : right;
                    float exitDot = Mathf.Cos(palmUpExitTolerance * Mathf.Deg2Rad);
                    if (!owner.jointsResolved || owner.palmUpDot < exitDot)
                    {
                        palmPoseLossTimer += Time.unscaledDeltaTime;
                        if (palmPoseLossTimer >= palmPoseLossGrace)
                        {
                            buttonDismissalReason = owner.jointsResolved
                                ? "Owner palm left the " + palmUpExitTolerance.ToString("0") + "° exit cone"
                                : "Owner palm joints unresolved beyond grace period";
                            lastButtonTransition = buttonDismissalReason;
                            ResetPalmButton(false);
                        }
                    }
                    else palmPoseLossTimer = 0f;
                }
                return;
            }

            if (buttonState == MenuButtonState.Stabilizing && buttonOwner != OpeningHand.None)
            {
                bool ownerStillUp = buttonOwner == OpeningHand.Left ? leftPalmUp : rightPalmUp;
                if (!ownerStillUp)
                {
                    buttonOwner = OpeningHand.None; gestureHoldTimer = 0f;
                    SetButtonState(MenuButtonState.WaitingForPalm, "Qualifying palm lost during stability hold"); return;
                }
                gestureHoldTimer += Time.unscaledDeltaTime;
                if (gestureHoldTimer >= palmButtonHoldDuration)
                {
                    SetButtonState(MenuButtonState.Visible, "Palm stable for entry hold"); trackingLossTimer = 0f;
                    palmPoseLossTimer = 0f;
                    SetPalmButtonVisible(true);
                    PlacePalmButtonImmediately(buttonOwner == OpeningHand.Left ? leftAnchor : rightAnchor);
                }
                return;
            }

            OpeningHand candidate = leftPalmUp ? OpeningHand.Left : rightPalmUp ? OpeningHand.Right : OpeningHand.None;
            if (candidate == OpeningHand.None)
            {
                buttonOwner = OpeningHand.None; gestureHoldTimer = 0f;
                SetButtonState(MenuButtonState.WaitingForPalm, "No qualifying palm"); return;
            }
            buttonOwner = candidate; SetButtonState(MenuButtonState.Stabilizing, candidate + " palm entered 45° cone");
            gestureHoldTimer = 0f;
        }

        private static void ResetFrameMetrics(OpenPalmHand hand)
        {
            hand.fistRecognized = false;
            hand.openRecognized = false;
            hand.curledCount = 0;
            hand.extendedCount = 0;
            hand.classification = "NEUTRAL";
            hand.tracked = false;
            hand.jointsResolved = false;
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

        private bool IsPalmUp(OpenPalmHand hand, bool isLeft, out Vector3 anchor)
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
            hand.tracked = true;
            if (!ResolveOpenPalmJoints(hand))
            {
                SetPalmDiagnostic(isLeft, "Required OpenXR joints unresolved");
                return false;
            }
            hand.jointsResolved = true;
            UpdateFingerRatios(hand);

            anchor = GetStablePalmAnchor(hand);
            Transform headset = recordingGestureController.CenterEyeAnchor;
            if (headset == null)
            {
                SetPalmDiagnostic(isLeft, "CenterEyeAnchor missing");
                return false;
            }

            Vector3 acrossPalm = hand.index.proximal.position - hand.pinky.proximal.position;
            Vector3 alongPalm = hand.middle.proximal.position - hand.wrist.position;
            Vector3 rawPalmNormal = Vector3.Cross(acrossPalm, alongPalm).normalized;
            float sign = isLeft ? leftPalmNormalSign : rightPalmNormalSign;
            Vector3 palmNormal = rawPalmNormal * (sign < 0f ? -1f : 1f);
            hand.rawPalmNormalWorld = rawPalmNormal;
            hand.palmNormalWorld = palmNormal;
            hand.calibrationSign = sign < 0f ? -1f : 1f;
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
            SetPalmDiagnostic(isLeft, "PALM UP; dot " + upDot.ToString("0.00")
                + "; head " + facingAngle.ToString("0") + "° / up " + upAngle.ToString("0")
                + "°; extended " + extended + "/4");
            hand.openRecognized = true;
            hand.classification = "PALM UP";
            return true;
        }

        private void HandlePalmCalibration(OpenPalmHand hand, bool isLeft)
        {
            if (!developmentPalmNormalCalibration || !hand.tracked || !hand.jointsResolved) return;
            bool capture = isLeft ? captureLeftPalmUpPose : captureRightPalmUpPose;
            if (!capture) return;
            float rawUpDot = Vector3.Dot(hand.rawPalmNormalWorld, Vector3.up);
            float required = Mathf.Cos(palmUpTolerance * Mathf.Deg2Rad);
            if (Mathf.Abs(rawUpDot) < required || hand.extendedCount < 3)
            {
                SetPalmDiagnostic(isLeft, "CALIBRATION waiting: deliberately hold anatomical palm up within "
                    + palmUpTolerance.ToString("0") + "° with 3+ fingers extended; raw dot "
                    + rawUpDot.ToString("0.00") + ", extended " + hand.extendedCount + "/4");
                return;
            }
            float capturedSign = rawUpDot >= 0f ? 1f : -1f;
            if (isLeft) { leftPalmNormalSign = capturedSign; captureLeftPalmUpPose = false; }
            else { rightPalmNormalSign = capturedSign; captureRightPalmUpPose = false; }
            hand.calibrationSign = capturedSign;
            hand.calibrated = true;
            SetPalmDiagnostic(isLeft, "CALIBRATED from deliberate palm-up pose; raw dot "
                + rawUpDot.ToString("0.00") + ", sign " + capturedSign.ToString("+0;-0"));
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
            // Keep the world-space UI independent of any motion or scale on the speech prototype.
            authoredView = Instantiate(authoredMenuPrefab);
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
                () => InvokeGuarded(() => ShowScreen(helpReturnScreen)),
                () => InvokeGuarded(() => ChangeSilenceDuration(-ThinkingSpacePreferencesStore.StepSeconds)),
                () => InvokeGuarded(() => ChangeSilenceDuration(ThinkingSpacePreferencesStore.StepSeconds)),
                () => InvokeGuarded(() => ChangeRecordingMode(ThumbRecordingGestureController.RecordingMode.Conversational)),
                () => InvokeGuarded(() => ChangeRecordingMode(ThumbRecordingGestureController.RecordingMode.Manual)),
                () => InvokeGuarded(() => ChangeSubtitles(true)),
                () => InvokeGuarded(() => ChangeSubtitles(false)),
                () => InvokeGuarded(EditPreferredName),
                () => InvokeGuarded(ReplayTutorial));
            authoredView.SetAudioPreferences(preferencesStore.Data.SilenceDurationSeconds,
                preferencesStore.Data.RecordingMode,preferencesStore.Data.SubtitlesEnabled);
            authoredView.SetFrontMarkerVisible(false);
            authoredView.gameObject.SetActive(false);
        }

        private void ApplyPreferences()
        {
            if (preferencesStore == null || recordingGestureController == null) return;
            recordingGestureController.SetRecordingMode(preferencesStore.Data.RecordingMode);
            recordingGestureController.ConversationalListening?.SetSilenceDuration(
                preferencesStore.Data.SilenceDurationSeconds);
        }

        private void ChangeSilenceDuration(float delta)
        {
            if (preferencesStore == null) return;
            if (!preferencesStore.SetSilenceDuration(preferencesStore.Data.SilenceDurationSeconds + delta))
            { authoredView?.SetSessionStatus(preferencesStore.LastError); return; }
            recordingGestureController.ConversationalListening?.SetSilenceDuration(
                preferencesStore.Data.SilenceDurationSeconds);
            authoredView?.SetAudioPreferences(preferencesStore.Data.SilenceDurationSeconds,
                preferencesStore.Data.RecordingMode,preferencesStore.Data.SubtitlesEnabled);
        }

        private void ChangeRecordingMode(ThumbRecordingGestureController.RecordingMode mode)
        {
            if (preferencesStore == null || !preferencesStore.SetRecordingMode(mode)) return;
            recordingGestureController.SetRecordingMode(mode);
            authoredView?.SetAudioPreferences(preferencesStore.Data.SilenceDurationSeconds, mode,preferencesStore.Data.SubtitlesEnabled);
        }

        private void ChangeSubtitles(bool value)
        {if(preferencesStore==null||!preferencesStore.SetSubtitlesEnabled(value))return;OrbyVoiceDirector.Instance?.SetSubtitlesEnabled(value);authoredView?.SetAudioPreferences(preferencesStore.Data.SilenceDurationSeconds,preferencesStore.Data.RecordingMode,value);}
        private void EditPreferredName()=>FindFirstObjectByType<OrbyOnboardingController>()?.EditPreferredName();
        private void ReplayTutorial()=>FindFirstObjectByType<OrbyOnboardingController>()?.ReplayTutorial();

        private void BuildPalmMenuButton()
        {
            if (palmMenuButtonPrefab == null)
            {
                buttonConstructionStatus = "Authored PalmMenuButton prefab reference missing";
                Debug.LogError(buttonConstructionStatus, this);
                return;
            }
            palmMenuButtonView = Instantiate(palmMenuButtonPrefab);
            palmMenuButtonView.name = "Palm Menu Button (Authored)";
            palmButtonCanvas = palmMenuButtonView.WorldCanvas;
            palmMenuGraphicRaycaster = palmMenuButtonView.GraphicRaycaster;
            palmMenuButton = palmMenuButtonView.MenuButton;
            palmMenuButtonImage = palmMenuButtonView.ButtonImage;
            palmButtonLabel = palmMenuButtonView.Label;
            palmMenuButton.onClick.RemoveAllListeners();
            // The palm toggle owns its physical press/release gate. The previous
            // nested InvokeGuarded call stamped the click before this callback and
            // caused the callback's second guard to reject every valid click.
            palmMenuButton.onClick.AddListener(PressPalmMenuButton);
            palmMenuButtonView.UnityPointerDown += HandleUnityPointerDown;
            palmMenuButtonView.UnityPointerUp += HandleUnityPointerUp;
            palmMenuButtonView.UnityPointerClick += HandleUnityPointerClick;
            buttonConstructionStatus = palmMenuButtonView.InteractionComponentsValid
                ? "Authored prefab ready" : "Authored prefab has missing component references";
            if (!palmMenuButtonView.InteractionComponentsValid)
                Debug.LogError(buttonConstructionStatus, this);
            palmButtonVisualVisible = true;
            SetPalmButtonVisible(false);
        }

        private bool UpdatePalmButtonTestMode()
        {
            if (!debugShowPalmMenuButtonAtHeadset)
            {
                if (palmButtonTestWasVisible)
                {
                    palmButtonTestWasVisible = false;
                    palmButtonDebugOpenedMenu = false;
                    debugButtonPosePlaced = false;
                    SetPalmButtonVisible(false);
                    ResetPalmButton(false);
                }
                return false;
            }
            Transform headset = recordingGestureController != null
                ? recordingGestureController.CenterEyeAnchor : null;
            if (headset == null)
            {
                buttonConstructionStatus = "Debug test rejected: CenterEyeAnchor missing";
                return true;
            }
            palmButtonTestWasVisible = true;
            if (palmButtonDebugOpenedMenu)
            {
                SetPalmButtonVisible(false);
                return true;
            }
            SetPalmButtonVisible(true);
            if (!debugButtonPosePlaced)
            {
                debugButtonPosePlaced = true;
                debugButtonFrozenPosition = headset.TransformPoint(palmButtonTestHeadsetOffset);
                debugButtonFrozenRotation = headset.rotation;
                palmButtonCanvas.transform.SetPositionAndRotation(
                    debugButtonFrozenPosition, debugButtonFrozenRotation);
                RecordInteractionEvent("DEBUG BASELINE PLACED", null,
                    palmButtonCanvas.gameObject);
            }
            else
            {
                palmButtonCanvas.transform.SetPositionAndRotation(
                    debugButtonFrozenPosition, debugButtonFrozenRotation);
            }
            if (palmButtonLabel != null && Time.unscaledTime >= debugClickConfirmationUntil)
                palmButtonLabel.text = "Menu";
            return true;
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
                    ResetPalmButton(true);
                }
                return false;
            }

            Transform headset = recordingGestureController.CenterEyeAnchor;
            if (headset == null) return true;
            SetPalmButtonVisible(false);
            if (!authoredTestWasVisible)
            {
                authoredView.gameObject.SetActive(true);
                authoredView.ShowMain(sessionStore.Sessions, recordingGestureController.SelectedRecordingHand, ReopenSession, RequestDeleteSession);
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
            RectTransform rect = diagnosticsRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0.72f, 0.52f);
            rect.localScale = Vector3.one;
            diagnosticsText = diagnosticsRoot.AddComponent<TextMeshPro>();
            diagnosticsText.font = menuFont;
            diagnosticsText.fontSharedMaterial = menuFont != null ? menuFont.material : null;
            diagnosticsText.enableAutoSizing = false;
            // TMP's 3D text scale makes the previous 0.055 value roughly 4 mm high.
            // 0.20 produces approximately 14 mm capitals with the configured Rubik SDF.
            diagnosticsText.fontSize = 0.20f;
            diagnosticsText.color = Color.white;
            diagnosticsText.alignment = TextAlignmentOptions.TopLeft;
            diagnosticsText.textWrappingMode = TextWrappingModes.Normal;
            diagnosticsText.overflowMode = TextOverflowModes.Truncate;
            diagnosticsText.margin = new Vector4(0.03f, 0.025f, 0.03f, 0.025f);

            diagnosticsBackground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            diagnosticsBackground.name = "Diagnostics Background (Non-interactive)";
            diagnosticsBackground.transform.SetParent(diagnosticsRoot.transform, false);
            diagnosticsBackground.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            diagnosticsBackground.transform.localScale = new Vector3(0.76f, 0.56f, 0.008f);
            Collider backgroundCollider = diagnosticsBackground.GetComponent<Collider>();
            if (backgroundCollider != null) Destroy(backgroundCollider);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            diagnosticsBackgroundMaterial = CreateProgressMaterial(shader,
                new Color(0.005f, 0.01f, 0.02f, 0.96f), "Palm Diagnostics Background");
            diagnosticsBackground.GetComponent<Renderer>().sharedMaterial = diagnosticsBackgroundMaterial;

            leftNormalVisual = BuildHandNormalVisualization("Left Palm Normal", Color.red);
            rightNormalVisual = BuildHandNormalVisualization("Right Palm Normal", Color.cyan);
            diagnosticsRoot.SetActive(false);
        }

        private HandNormalVisualization BuildHandNormalVisualization(string name, Color normalColor)
        {
            var visual = new HandNormalVisualization { root = new GameObject(name) };
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Palm Center"; marker.transform.SetParent(visual.root.transform, false);
            marker.transform.localScale = Vector3.one * 0.018f;
            Collider collider = marker.GetComponent<Collider>(); if (collider != null) Destroy(collider);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            marker.GetComponent<Renderer>().sharedMaterial = CreateProgressMaterial(shader, normalColor, name + " Marker");
            visual.marker = marker.transform;
            visual.normalLine = CreateDiagnosticLine("Anatomical Palm Normal", visual.root.transform, normalColor, shader);
            visual.upLine = CreateDiagnosticLine("World Up Reference", visual.root.transform, Color.green, shader);
            visual.root.SetActive(false);
            return visual;
        }

        private static LineRenderer CreateDiagnosticLine(string name, Transform parent, Color color, Shader shader)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = 0.006f; line.endWidth = 0.002f;
            line.numCapVertices = 4;
            line.sharedMaterial = CreateProgressMaterial(shader, color, name + " Material");
            line.startColor = line.endColor = color;
            return line;
        }

        private void UpdateHandNormalVisualization(HandNormalVisualization visual, OpenPalmHand hand)
        {
            bool visible = showPalmNormalVisualization && hand.tracked && hand.jointsResolved;
            if (visual == null) return;
            visual.root.SetActive(visible);
            if (!visible) return;
            Vector3 center = GetStablePalmAnchor(hand);
            visual.marker.position = center;
            visual.normalLine.SetPosition(0, center);
            visual.normalLine.SetPosition(1, center + hand.palmNormalWorld * 0.09f);
            visual.upLine.SetPosition(0, center);
            visual.upLine.SetPosition(1, center + Vector3.up * 0.07f);
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
            UpdatePalmButtonFollowing();
            UpdateMenuFollowing();
            UpdateHandNormalVisualization(leftNormalVisual, left);
            UpdateHandNormalVisualization(rightNormalVisual, right);
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
            if (diagnosticPage == 1)
                diagnosticsText.text = "PALM  1 / 4"
                    + FormatHandMetrics("LEFT", left, leftPalmDiagnostic)
                    + FormatHandMetrics("RIGHT", right, rightPalmDiagnostic);
            else if (diagnosticPage == 2)
                diagnosticsText.text = "BUTTON  2 / 4"
                    + "\nState " + buttonState + " | owner " + buttonOwner
                    + "\nVisible " + palmButtonVisualVisible + " | eligible " + palmButtonInteractionActive
                    + "\nLocked " + palmButtonPoseLocked + " | hold "
                    + gestureHoldTimer.ToString("0.00") + "/" + palmButtonHoldDuration.ToString("0.00")
                    + "\nGrace pose/tracking " + palmPoseLossTimer.ToString("0.00") + "/" + trackingLossTimer.ToString("0.00")
                    + "\nDismiss " + buttonDismissalReason
                    + "\nTransitions " + buttonTransitionCount + " | " + lastButtonTransition;
            else if (diagnosticPage == 3)
                diagnosticsText.text = "INTERACTION  3 / 4"
                    + "\nBuilt " + buttonConstructionStatus
                    + "\nCanvas/Raycaster " + (palmButtonCanvas != null && palmButtonCanvas.enabled)
                    + "/" + (palmMenuGraphicRaycaster != null && palmMenuGraphicRaycaster.enabled)
                    + "\nButton/Meta " + (palmMenuButton != null && palmMenuButton.interactable)
                    + "/" + (palmMenuButtonView != null && palmMenuButtonView.IsEligible)
                    + "\nHover/Select " + palmButtonHovered + "/" + palmButtonSelected
                    + "\nGate consumed/generation " + toggleGate.ConsumedGeneration + "/" + toggleGate.InteractionGeneration
                    + " held=" + toggleGate.PointerHeld + " · " + lastToggleGateResult
                    + "\nLast " + interactionDiagnostic
                    + "\nMenu " + menuVisibilityDiagnostic;
            else
                diagnosticsText.text = "EVENT CHAIN  4 / 4\n" + string.Join("\n", interactionEventHistory);
        }

        private static string FormatHandMetrics(string name, OpenPalmHand hand, string openReason)
        {
            return "\n[" + name + "] Tracked: " + (hand.tracked ? "YES" : "no")
                + "   Joints: " + (hand.jointsResolved ? "RESOLVED" : "unresolved")
                + "   Pose: " + (hand.openRecognized ? "UP" : "rejected")
                + "\nAngle: " + hand.palmUpAngle.ToString("0") + "°   Dot: "
                + hand.palmUpDot.ToString("0.00") + "   Sign: " + hand.calibrationSign.ToString("+0;-0")
                + "\nReason: " + openReason;
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
            {
                menuHovered = true;
                menuPoseLocked = true;
                interactionDiagnostic = "Hover: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META HOVER MENU", args.PointerId, args.Hovered);
            }
            else if (args.Canvas == palmButtonCanvas)
            {
                palmButtonHovered = true;
                palmButtonPoseLocked = true;
                interactionDiagnostic = "Palm Menu hover: " + GetInteractionObjectName(args.Hovered);
                lastPointerHover = GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META HOVER", args.PointerId, args.Hovered);
            }
        }

        private void HandleCanvasUnhover(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
            {
                menuHovered = false;
                menuPoseLocked = menuSelected;
                interactionDiagnostic = "Hover ended: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META UNHOVER MENU", args.PointerId, args.Hovered);
            }
            else if (args.Canvas == palmButtonCanvas)
            {
                palmButtonHovered = false;
                palmButtonPoseLocked = palmButtonSelected;
                interactionDiagnostic = "Palm Menu hover ended: " + GetInteractionObjectName(args.Hovered);
                lastPointerHover = "Released hover: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META UNHOVER", args.PointerId, args.Hovered);
            }
        }

        private void HandleCanvasPress(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
            {
                menuSelected = true;
                menuPoseLocked = true;
                interactionDiagnostic = "Press: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META SELECT MENU", args.PointerId, args.Hovered);
            }
            else if (args.Canvas == palmButtonCanvas)
            {
                toggleGate.BeginInteraction(Time.unscaledTime);
                palmButtonSelected = true;
                palmButtonPoseLocked = true;
                interactionDiagnostic = "Palm Menu press: " + GetInteractionObjectName(args.Hovered);
                lastPointerPress = GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META SELECT", args.PointerId, args.Hovered);
            }
        }

        private void HandleCanvasRelease(PointableCanvasEventArgs args)
        {
            if (args.Canvas == menuCanvas)
            {
                menuSelected = false;
                menuPoseLocked = menuHovered;
                interactionDiagnostic = "Release: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META UNSELECT MENU", args.PointerId, args.Hovered);
            }
            else if (args.Canvas == palmButtonCanvas)
            {
                toggleGate.EndInteraction();
                palmButtonSelected = false;
                palmButtonPoseLocked = palmButtonHovered;
                interactionDiagnostic = "Palm Menu release: " + GetInteractionObjectName(args.Hovered);
                lastPointerPress = "Released: " + GetInteractionObjectName(args.Hovered);
                RecordInteractionEvent("META UNSELECT", args.PointerId, args.Hovered);
            }
        }

        private void HandleUnityPointerDown(PointerEventData args)
        {
            toggleGate.BeginInteraction(Time.unscaledTime);
            palmButtonPoseLocked = true;
            RecordInteractionEvent("UNITY POINTER DOWN", args.pointerId, args.pointerPressRaycast.gameObject);
        }

        private void HandleUnityPointerUp(PointerEventData args)
        {
            toggleGate.EndInteraction();
            RecordInteractionEvent("UNITY POINTER UP", args.pointerId, args.pointerPressRaycast.gameObject);
            palmButtonPoseLocked = palmButtonHovered || palmButtonSelected;
        }

        private void HandleUnityPointerClick(PointerEventData args)
        {
            RecordInteractionEvent("UNITY POINTER CLICK", args.pointerId, args.pointerPressRaycast.gameObject);
        }

        private void RecordInteractionEvent(string eventName, int? pointerId, GameObject target)
        {
            interactionEventSequence++;
            string entry = "#" + interactionEventSequence + " F" + Time.frameCount
                + " T" + Time.unscaledTime.ToString("0.000")
                + " P" + (pointerId.HasValue ? pointerId.Value.ToString() : "-")
                + " " + eventName + " > " + GetInteractionObjectName(target);
            while (interactionEventHistory.Count >= 6) interactionEventHistory.Dequeue();
            interactionEventHistory.Enqueue(entry);
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

        private void PressPalmMenuButton()
        {
            RecordInteractionEvent("UNITY BUTTON ONCLICK", null,
                palmMenuButton != null ? palmMenuButton.gameObject : null);
            if (!toggleGate.TryConsume(Time.unscaledTime, duplicateClickGuard))
            {
                lastToggleGateResult = "Rejected duplicate callback without a new press/release cycle";
                RecordInteractionEvent("TOGGLE GATE REJECTED", null,
                    palmMenuButton != null ? palmMenuButton.gameObject : null);
                return;
            }
            lastToggleGateResult = "Accepted generation " + toggleGate.ConsumedGeneration;
            if(menuCanvas!=null&&menuCanvas.gameObject.activeSelf)
            {lastPointerClick="Menu closed by palm button at "+Time.unscaledTime.ToString("0.00")+"s";CloseMenuInternal(menuOwner);RecordInteractionEvent("CONTROLLER TOGGLE CLOSE",null,menuCanvas.gameObject);return;}
            if (debugShowPalmMenuButtonAtHeadset)
            {
                interactionDiagnostic = "Debug palm Menu button clicked successfully";
                lastPointerClick = "Debug Menu button at " + Time.unscaledTime.ToString("0.00") + "s";
                debugClickConfirmationUntil = Time.unscaledTime + 1.5f;
                if (palmButtonLabel != null) palmButtonLabel.text = "Clicked ✓";
                Transform headset = recordingGestureController != null
                    ? recordingGestureController.CenterEyeAnchor : null;
                if (headset == null || menuCanvas == null)
                {
                    interactionDiagnostic = "Debug click received, but headset or authored menu is missing";
                    return;
                }
                palmButtonDebugOpenedMenu = true;
                SetPalmButtonVisible(false);
                OpenMenuAtHeadset(headset);
                RecordInteractionEvent("CONTROLLER OPEN CALLBACK", null, menuCanvas.gameObject);
                return;
            }
            if (buttonState != MenuButtonState.Visible || buttonOwner == OpeningHand.None) return;
            OpeningHand activatingHand = buttonOwner;
            lastPointerClick = "Menu opened at " + Time.unscaledTime.ToString("0.00") + "s";
            // The same authored button remains the close toggle. Do not disable and
            // re-enable Meta interactables inside their own click dispatch.
            // Use the last stable tracked anchor. A single joint-resolution drop on
            // the poke/release frame must not consume a valid Unity Button click.
            OpenMenu(activatingHand, lastStableButtonAnchor);
            RecordInteractionEvent("CONTROLLER OPEN CALLBACK", null, menuCanvas.gameObject);
        }

        private static Vector3 GetStablePalmAnchor(OpenPalmHand hand)
        {
            return (hand.wrist.position + hand.index.proximal.position + hand.middle.proximal.position
                + hand.ring.proximal.position + hand.pinky.proximal.position) * 0.2f;
        }

        private void SetPalmButtonVisible(bool visible)
        {
            if (palmButtonCanvas == null) return;
            bool changed = palmButtonVisualVisible != visible
                || palmButtonInteractionActive != visible;
            palmButtonVisualVisible = visible;
            palmButtonInteractionActive = visible;
            if (changed)
            {
                buttonTransitionCount++;
                lastButtonTransition = (visible ? "Shown + eligible" : "Hidden + ineligible") + " at "
                    + Time.unscaledTime.ToString("0.00") + "s";
            }
            if (palmMenuButtonView != null)
                palmMenuButtonView.SetEligibility(visible);
            else
            {
                palmButtonCanvas.enabled = visible;
                if (palmMenuGraphicRaycaster != null) palmMenuGraphicRaycaster.enabled = visible;
                if (palmMenuButtonImage != null) palmMenuButtonImage.raycastTarget = visible;
                if (palmMenuButton != null) palmMenuButton.interactable = visible;
            }
            if (!visible)
            {
                palmButtonHovered = palmButtonSelected = palmButtonPoseLocked = false;
                palmButtonCanvas.transform.position = new Vector3(0f, -1000f, 0f);
            }
        }

        private void ResetPalmButton(bool requireRelease)
        {
            SetPalmButtonVisible(false);
            palmButtonHovered = palmButtonSelected = palmButtonPoseLocked = false;
            buttonOwner = OpeningHand.None;
            SetButtonState(requireRelease ? MenuButtonState.WaitingForRelease : MenuButtonState.WaitingForPalm,
                requireRelease ? "Waiting for owner palm release" : "Button reset");
            rearmRequired = requireRelease;
            if (!requireRelease) rearmOwner = OpeningHand.None;
            gestureHoldTimer = trackingLossTimer = palmPoseLossTimer = 0f;
            palmButtonFollowVelocity = Vector3.zero;
        }

        private void SetButtonState(MenuButtonState next, string reason)
        {
            if (buttonState == next) return;
            buttonState = next;
            buttonTransitionCount++;
            lastButtonTransition = next + ": " + reason + " at " + Time.unscaledTime.ToString("0.00") + "s";
        }

        private void PlacePalmButtonImmediately(Vector3 anchor)
        {
            Transform headset = recordingGestureController.CenterEyeAnchor;
            lastStableButtonAnchor = anchor;
            palmButtonCanvas.transform.position = GetPalmButtonTargetPosition(anchor, headset);
            FaceCanvasToHeadset(palmButtonCanvas.transform, headset, true);
        }

        private void UpdatePalmButtonFollowing()
        {
            if (palmButtonCanvas == null || !palmButtonVisualVisible
                || buttonOwner == OpeningHand.None || palmButtonPoseLocked
                || debugShowPalmMenuButtonAtHeadset) return;
            OpenPalmHand owner = buttonOwner == OpeningHand.Left ? left : right;
            if (!IsOwnerTracked(buttonOwner) || !ResolveOpenPalmJoints(owner)) return;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            lastStableButtonAnchor = GetStablePalmAnchor(owner);
            Vector3 target = GetPalmButtonTargetPosition(lastStableButtonAnchor, headset);
            palmButtonCanvas.transform.position = Vector3.SmoothDamp(palmButtonCanvas.transform.position,
                target, ref palmButtonFollowVelocity, palmButtonFollowSmoothTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
            FaceCanvasToHeadset(palmButtonCanvas.transform, headset, false);
        }

        private Vector3 GetPalmButtonTargetPosition(Vector3 anchor, Transform headset)
        {
            Vector3 towardHeadset = headset != null ? (headset.position - anchor).normalized : Vector3.zero;
            Vector3 horizontal = headset != null ? headset.right * palmButtonOffset.x : Vector3.zero;
            return anchor + horizontal + Vector3.up * palmButtonOffset.y + towardHeadset * palmButtonOffset.z;
        }

        private void FaceCanvasToHeadset(Transform target, Transform headset, bool immediate)
        {
            if (target == null || headset == null) return;
            Vector3 awayFromHeadset = target.position - headset.position;
            if (awayFromHeadset.sqrMagnitude < 0.001f) return;
            Quaternion desired = Quaternion.LookRotation(awayFromHeadset.normalized, Vector3.up);
            if (immediate) target.rotation = desired;
            else
            {
                float blend = 1f - Mathf.Exp(-facingSmoothSpeed * Time.unscaledDeltaTime);
                target.rotation = Quaternion.Slerp(target.rotation, desired, blend);
            }
        }

        private void OpenMenu(OpeningHand owner, Vector3 handAnchor)
        {
            menuOwner = owner;
            buttonOwner = owner;
            SetButtonState(MenuButtonState.WaitingForRelease, "Menu button clicked; full menu opened");
            buttonDismissalReason = "Menu button clicked; full menu opened";
            SetPalmButtonVisible(false);
            PalmMenuOpened?.Invoke();
            gestureHoldTimer = sequenceTimer = trackingLossTimer = fistReleaseTimer = closeFistTimer = 0f;
            followVelocity = Vector3.zero;
            Transform headset = recordingGestureController.CenterEyeAnchor;
            Vector3 position = ConstrainMenuPosition(GetMenuTargetPosition(handAnchor, headset), headset);
            menuCanvas.transform.position = position;
            FaceCanvasToHeadset(menuCanvas.transform, headset, true);
            menuCanvas.enabled = true;
            menuCanvas.gameObject.SetActive(true);
            ShowScreen(sessionStore.ActiveSession == null ? MenuScreen.Main : MenuScreen.Session);
            ValidateMenuVisibility(headset, "palm open callback");
        }

        private void OpenMenuAtHeadset(Transform headset)
        {
            menuOwner = OpeningHand.None;
            buttonOwner = OpeningHand.None;
            SetButtonState(MenuButtonState.WaitingForRelease, "Debug button clicked; full menu opened");
            buttonDismissalReason = "Debug Menu button clicked; full menu opened";
            menuCanvas.transform.position = ConstrainMenuPosition(
                headset.TransformPoint(authoredTestHeadsetOffset), headset);
            FaceCanvasToHeadset(menuCanvas.transform, headset, true);
            menuCanvas.enabled = true;
            menuCanvas.gameObject.SetActive(true);
            ShowScreen(sessionStore.ActiveSession == null ? MenuScreen.Main : MenuScreen.Session);
            ValidateMenuVisibility(headset, "debug open callback");
            interactionDiagnostic = "Click callback completed; " + menuVisibilityDiagnostic;
        }

        private void UpdateMenuFollowing()
        {
            if (menuCanvas == null || !menuCanvas.gameObject.activeSelf || menuOwner == OpeningHand.None
                || menuPoseLocked) return;
            OpenPalmHand owner = menuOwner == OpeningHand.Left ? left : right;
            if (owner.hand == null || owner.skeleton == null || !owner.hand.IsTracked
                || !ResolveOpenPalmJoints(owner))
            {
                trackingLossStatus = "Menu owner tracking unavailable; panel pose frozen";
                return;
            }
            trackingLossStatus = "None";

            Vector3 anchor = GetStablePalmAnchor(owner);
            Transform headset = recordingGestureController.CenterEyeAnchor;
            Vector3 targetPosition = ConstrainMenuPosition(GetMenuTargetPosition(anchor, headset), headset);
            menuCanvas.transform.position = Vector3.SmoothDamp(
                menuCanvas.transform.position, targetPosition, ref followVelocity,
                followSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            FaceCanvasToHeadset(menuCanvas.transform, headset, false);
            if (headset != null)
            {
                Vector3 towardHeadset = (headset.position - menuCanvas.transform.position).normalized;
                canvasFacingDot = Vector3.Dot(-menuCanvas.transform.forward, towardHeadset);
            }
        }

        private Vector3 GetMenuTargetPosition(Vector3 handAnchor, Transform headset)
        {
            Vector3 towardHeadset = headset != null
                ? (headset.position - handAnchor).normalized : Vector3.zero;
            Vector3 horizontal = headset != null ? headset.right * menuOffsetFromHand.x : Vector3.zero;
            return handAnchor + horizontal + Vector3.up * menuOffsetFromHand.y
                + towardHeadset * menuOffsetFromHand.z;
        }

        private static Vector3 ConstrainMenuPosition(Vector3 worldPosition, Transform headset)
        {
            if (headset == null) return worldPosition;
            Vector3 local = headset.InverseTransformPoint(worldPosition);
            local.z = Mathf.Clamp(local.z, 0.35f, 0.85f);
            local.x = Mathf.Clamp(local.x, -0.35f, 0.35f);
            local.y = Mathf.Clamp(local.y, -0.28f, 0.28f);
            return headset.TransformPoint(local);
        }

        private void ValidateMenuVisibility(Transform headset, string source)
        {
            if (menuCanvas == null || headset == null)
            {
                menuVisibilityDiagnostic = source + ": missing Canvas or headset";
                return;
            }
            Vector3 cameraSpace = headset.InverseTransformPoint(menuCanvas.transform.position);
            Vector3 towardHeadset = (headset.position - menuCanvas.transform.position).normalized;
            canvasFacingDot = Vector3.Dot(-menuCanvas.transform.forward, towardHeadset);
            bool screenContentActive = false;
            Graphic[] graphics = menuCanvas.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i].gameObject.activeInHierarchy)
                {
                    screenContentActive = true;
                    break;
                }
            }
            menuVisibilityDiagnostic = source + ": canvas=" + menuCanvas.enabled
                + " content=" + screenContentActive
                + " cam=" + cameraSpace.ToString("F2")
                + " facing=" + canvasFacingDot.ToString("0.00")
                + " scale=" + menuCanvas.transform.lossyScale.ToString("F5");
            RecordInteractionEvent("MAIN CANVAS VISIBLE="
                + (menuCanvas.enabled && screenContentActive && cameraSpace.z > 0f), null,
                menuCanvas.gameObject);
        }

        public void CloseMenu()
        {
            CloseMenuInternal(menuOwner);
        }

        private void CloseMenuInternal(OpeningHand releasingHand)
        {
            OpeningHand ownerToRelease = menuOwner != OpeningHand.None ? menuOwner : releasingHand;
            if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
            menuHovered = menuSelected = menuPoseLocked = false;
            menuVisibilityDiagnostic = "Menu closed";
            menuOwner = OpeningHand.None;
            ResetPalmButton(true);
            rearmOwner = ownerToRelease;
        }

        private void ShowScreen(MenuScreen screen)
        {
            currentScreen = screen;
            if (authoredView != null)
            {
                if (screen == MenuScreen.Main)
                    authoredView.ShowMain(sessionStore.Sessions, recordingGestureController.SelectedRecordingHand, ReopenSession, RequestDeleteSession);
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
            OrbyVoiceDirector.Instance?.SetActiveSession(session.SessionId);
            SessionActivated?.Invoke(session);
            OrbyVoiceDirector director=OrbyVoiceDirector.Instance;
            if(!OrbyOnboardingController.ShouldBeginTutorial(preferencesStore.Data)&&director!=null&&!director.TutorialRecordingBlocked)
                director.PlayPrerecorded("session_started",session.SessionId);
            else director?.ReportEvent("session_started",false,false,OrbyVoicePriority.Acknowledgement,session.SessionId);
            ShowScreen(MenuScreen.Session);
        }

        private void ReopenSession(int number)
        {
            if (!sessionStore.ReopenSession(number)) return;
            recordingGestureController.SetSessionActive(true);
            recordingGestureController.SetSessionIdentifier("Session " + number);
            OrbyVoiceDirector.Instance?.SetActiveSession(sessionStore.ActiveSession.SessionId);
            SessionActivated?.Invoke(sessionStore.ActiveSession);
            OrbyVoiceDirector director=OrbyVoiceDirector.Instance;
            if(!OrbyOnboardingController.ShouldBeginTutorial(preferencesStore.Data)&&director!=null&&!director.TutorialRecordingBlocked)
                director.PlayPrerecorded("greeting_return",sessionStore.ActiveSession.SessionId);
            else director?.ReportEvent("session_resumed",false,false,OrbyVoicePriority.Acknowledgement,sessionStore.ActiveSession.SessionId);
            ShowScreen(MenuScreen.Session);
        }

        private void RequestDeleteSession(int number)
        {
            BrainstormSession target = null;
            for (int i = 0; i < sessionStore.Sessions.Count; i++)
                if (sessionStore.Sessions[i].Number == number) { target = sessionStore.Sessions[i]; break; }
            if (target == null) return;
            bool wasActive = sessionStore.ActiveSession != null
                && sessionStore.ActiveSession.SessionId == target.SessionId;
            if (!sessionStore.TryDeleteSession(number))
            {
                authoredView?.SetSessionStatus(string.IsNullOrEmpty(sessionStore.LastPersistenceError)
                    ? "Session deletion failed." : sessionStore.LastPersistenceError);
                return;
            }
            OrbyVoiceDirector.Instance?.RemoveSessionCache(target.SessionId);
            if (wasActive) recordingGestureController.SetSessionActive(false);
            ShowScreen(MenuScreen.Main);
        }

        private void EndSession()
        {
            recordingGestureController.SetSessionActive(false);
            OrbyVoiceDirector director=OrbyVoiceDirector.Instance;
            director?.SetActiveSession(string.Empty);
            director?.PlayPrerecorded("session_ended",string.Empty,null,true);
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ContextMenu("Development/Palm Menu/Show Full Menu At Headset")]
        public void DevelopmentShowMenuAtHeadset()
        {
            Transform headset = recordingGestureController != null
                ? recordingGestureController.CenterEyeAnchor : Camera.main != null ? Camera.main.transform : null;
            if (menuCanvas == null || headset == null)
            {
                lastToggleGateResult = "Development show rejected: menu Canvas or headset unavailable";
                return;
            }
            OpenMenuAtHeadset(headset);
            lastToggleGateResult = "Development show completed; no session changed";
        }

        [ContextMenu("Development/Palm Menu/Hide Full Menu")]
        public void DevelopmentHideMenu()
        {
            if (menuCanvas != null) menuCanvas.gameObject.SetActive(false);
            menuOwner = OpeningHand.None;
            menuHovered = menuSelected = menuPoseLocked = false;
            menuVisibilityDiagnostic = "Hidden by development command";
            lastToggleGateResult = "Development hide completed; no session changed";
        }

        [ContextMenu("Development/Palm Menu/Simulate One Button Callback")]
        public void DevelopmentSimulateMenuButtonCallback()
        {
            toggleGate.BeginDevelopmentInteraction(Time.unscaledTime);
            if (IsMenuVisible) PressPalmMenuButton();
            else
            {
                if (!toggleGate.TryConsume(Time.unscaledTime, 0f)) return;
                DevelopmentShowMenuAtHeadset();
                RecordInteractionEvent("DEVELOPMENT SIMULATED OPEN CALLBACK", null,
                    menuCanvas != null ? menuCanvas.gameObject : null);
            }
        }

        [ContextMenu("Development/Palm Menu/Test Separate Toggle Click")]
        public void DevelopmentTestToggle() => DevelopmentSimulateMenuButtonCallback();
#endif

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
            if (palmMenuButtonView != null)
            {
                palmMenuButtonView.UnityPointerDown -= HandleUnityPointerDown;
                palmMenuButtonView.UnityPointerUp -= HandleUnityPointerUp;
                palmMenuButtonView.UnityPointerClick -= HandleUnityPointerClick;
            }
            if (progressBackgroundMaterial != null) Destroy(progressBackgroundMaterial);
            if (progressFillMaterial != null) Destroy(progressFillMaterial);
            if (palmButtonCanvas != null) Destroy(palmButtonCanvas.gameObject);
            if (menuCanvas != null && menuCanvas.transform.parent == null) Destroy(menuCanvas.gameObject);
            if (diagnosticsBackgroundMaterial != null) Destroy(diagnosticsBackgroundMaterial);
            if (diagnosticsRoot != null) Destroy(diagnosticsRoot);
            if (leftNormalVisual != null && leftNormalVisual.root != null) Destroy(leftNormalVisual.root);
            if (rightNormalVisual != null && rightNormalVisual.root != null) Destroy(rightNormalVisual.root);
        }
    }
}

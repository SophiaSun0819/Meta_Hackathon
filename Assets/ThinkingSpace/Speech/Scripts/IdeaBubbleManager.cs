using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ThinkingSpace.Companion;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    [DisallowMultipleComponent]
    public sealed class IdeaBubbleManager : MonoBehaviour
    {
        [SerializeField, Range(0.12f, 0.15f)] private float bubbleDiameter = 0.135f;
        [SerializeField, Range(0f, 1f)] private float bubbleCategorySaturation = 0.78f;
        [SerializeField] private bool showDevelopmentDiagnostics;
        [Header("Beckon and return")]
        [SerializeField, Range(0.55f, 0.65f)] private float inspectionDistance = 0.60f;
        [SerializeField, Range(0.16f, 0.30f)] private float inspectionSlotSpacing = 0.22f;
        [SerializeField, Range(0f, 0.10f)] private float inspectionArcDepth = 0.04f;
        [SerializeField, Range(0.02f, 0.15f)] private float inspectionClearanceMargin = 0.05f;
        [SerializeField, Range(0.75f, 3f)] private float inspectionZoneMembershipDistance = 1.5f;
        [SerializeField, Range(0.1f, 0.6f)] private float inspectionSlotReleaseRadius = 0.28f;
        [SerializeField, Range(3, 24)] private int maximumInspectionZones = 12;
        [SerializeField, Range(0.15f, 0.5f)] private float resummonDistance = 0.30f;
        [SerializeField, Range(0.1f, 0.5f)] private float neutralHoldDuration = 0.2f;
        [SerializeField, Range(0.15f, 0.5f)] private float fistHoldDuration = 0.25f;
        [SerializeField, Range(0.08f, 0.35f)] private float returnOpenHoldDuration = 0.15f;
        [SerializeField, Range(0.2f, 1.2f)] private float gestureCooldown = 0.65f;
        [SerializeField, Range(0.15f, 1f)] private float movementDuration = 0.45f;
        [SerializeField, Range(0.12f, 0.4f)] private float nearGrabExclusionRadius = 0.22f;
        [Header("Direct manipulation")]
        [SerializeField, Range(0.02f, 0.12f)] private float directGrabSurfaceRange = 0.07f;
        [SerializeField, Range(0.05f, 0.3f)] private float grabGlideDuration = 0.14f;
        [SerializeField, Range(0.05f, 0.3f)] private float grabReleaseHold = 0.12f;
        [SerializeField, Range(0.15f, 0.45f)] private float resizeHandsProximity = 0.30f;
        [SerializeField, Range(0.02f, 0.12f)] private float resizeHandleRange = 0.07f;
        [SerializeField, Range(0.25f, 1f)] private float minimumBubbleScale = 0.60f;
        [SerializeField, Range(1f, 3f)] private float maximumBubbleScale = 2.00f;
        [SerializeField, Range(4f, 20f)] private float resizeSmoothing = 8f;
        [SerializeField, Range(0.002f, 0.05f)] private float resizeScaleDeadband = 0.015f;
        [SerializeField, Range(0.01f, 0.08f)] private float fingertipHoverRange = 0.045f;
        [SerializeField, Range(0.002f, 0.04f)] private float fingertipContactRange = 0.015f;
        [SerializeField, Range(0.03f, 0.12f)] private float fingertipExitRange = 0.065f;
        [SerializeField, Range(0.03f, 0.2f)] private float fingertipSelectionHold = 0.08f;
        [SerializeField, Range(0.1f, 1f)] private float nearSelectionCooldown = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float microphoneGestureReleaseCooldown = 0.35f;
        [SerializeField, Range(0.05f, 0.3f)] private float twoHandResizeReservation = 0.16f;
        [SerializeField, Range(0.05f, 0.18f)] private float resizeHandleHoverRange = 0.11f;
        [SerializeField, Range(0.45f, 0.8f)] private float curledChordRatio = 0.64f;
        [SerializeField, Range(0.55f, 0.85f)] private float nearCurledChordRatio = 0.74f;
        [SerializeField, Range(0.65f, 0.9f)] private float fistReleaseChordRatio = 0.82f;
        [SerializeField, Range(0.7f, 0.98f)] private float extendedChordRatio = 0.82f;
        [Header("Details panel manipulation")]
        [SerializeField, Range(0.025f, 0.10f)] private float panelBorderGrabRange = 0.06f;
        [SerializeField, Range(0.08f, 0.35f)] private float panelGrabConfirmation = 0.16f;
        [SerializeField, Range(0.01f, 0.15f)] private float panelDragSmoothTime = 0.045f;
        private readonly Dictionary<string, IdeaBubbleView> visible = new Dictionary<string, IdeaBubbleView>();
        private readonly Dictionary<string, IdeaDetailsPanel> openPanels = new Dictionary<string, IdeaDetailsPanel>();
        private readonly Dictionary<string, BubbleInteractionHand> selectionOwners =
            new Dictionary<string, BubbleInteractionHand>();
        private readonly List<string> panelFocusOrder = new List<string>();
        private Func<BrainstormSessionStore> storeProvider;
        private Transform headset, bubbleRoot, panelRoot, diagnosticRoot;
        private Action<string> ideaDeleted;
        private Func<BrainstormIdeaRecord, Action<bool>, string> playIdeaSummary;
        private Action<string> stopIdeaSummary;
        private TMP_Text diagnosticText;
        private string activeSessionId = string.Empty;
        private string selectedIdeaId = string.Empty;
        private BrainstormMenuController menuController;
        private ThinkingSpaceAIWorkflowController workflowController;
        private SpeechTranscriptionCoordinator transcriptionCoordinator;
        private ThumbRecordingGestureController recordingGestureController;
        private bool interactionAvailable = true;
        private readonly BeckonHand leftBeckon = new BeckonHand();
        private readonly BeckonHand rightBeckon = new BeckonHand();
        private const int MaximumInspectedBubbles = 3;
        private readonly Dictionary<string, BubbleInspection> inspections =
            new Dictionary<string, BubbleInspection>();
        private readonly Dictionary<string, InspectionZone> inspectionZones =
            new Dictionary<string, InspectionZone>();
        private readonly Dictionary<string, BubbleMovement> movements =
            new Dictionary<string, BubbleMovement>();
        private float gestureCooldownRemaining;
        private BubbleInteractionHand activeSelectionHand = BubbleInteractionHand.Unknown;
        private int lastSelectionPointerId = int.MinValue;
        private string lastSelectionIdeaId = string.Empty;
        private float lastSelectionTime = float.NegativeInfinity;
        private int operationEpoch;
        private bool workspaceInitialized;
        private Vector3 workspaceAnchor;
        private Vector3 workspaceForward = Vector3.forward;
        private Vector3 lastHeadWorldPosition;
        private int stableHeadPoseFrames;
        private string lastPanelMovementEvent = "None";
        private string lastGestureDiagnostic = "None";
        private DirectManipulationMode directMode;
        private string directIdeaId = string.Empty;
        private BubbleInteractionHand directOwner = BubbleInteractionHand.Unknown;
        private Vector3 directStartPosition, directFixedCenter, directStartScale;
        private float directElapsed, directReleaseTimer, directStartHandDistance, directScaleTarget;
        private int directPanelInstanceId, directFirstCorner = -1, directSecondCorner = -1;
        private readonly NearSelectionState leftNearSelection = new NearSelectionState();
        private readonly NearSelectionState rightNearSelection = new NearSelectionState();
        private string resizeReservationIdeaId = string.Empty;
        private float resizeReservationRemaining;
        private int nextInspectionZoneNumber = 1;
        private IdeaDetailsPanel draggedPanel;
        private BubbleInteractionHand panelDragOwner = BubbleInteractionHand.Unknown;
        private string panelGrabCandidateId = string.Empty;
        private BubbleInteractionHand panelGrabCandidateHand = BubbleInteractionHand.Unknown;
        private float panelGrabTimer;

        public string LastCreatedIdeaId { get; private set; } = "None";
        public string LastRestoredIdeaId { get; private set; } = "None";
        public string LastSaveResult { get; private set; } = "Not attempted";
        public string LastLoadResult { get; private set; } = "Not attempted";
        public int VisibleBubbleCount => visible.Count;
        public string SelectedIdeaId => selectedIdeaId;
        public int InspectedBubbleCount => inspections.Count;
        public int InspectionZoneCount => inspectionZones.Count;
        public int ActivePlatformCount
        {
            get
            {
                int count = 0;
                foreach (BubbleInspection inspection in inspections.Values)
                    if (inspection.platform != null) count++;
                return count;
            }
        }
        public DirectManipulationMode DirectMode => directMode;
        public bool IsPanelDragging => draggedPanel != null;

        public enum DirectManipulationMode { None, GlideToGrip, Grabbed, Resizing }

        private sealed class FingerChain
        {
            public Transform proximal, intermediate, distal, tip;
            public bool Resolved => proximal != null && intermediate != null && distal != null && tip != null;
        }

        private sealed class NearSelectionState
        {
            public string ideaId = string.Empty;
            public float contactTimer, cooldown;
            public bool selectedThisEntry;
            public void ResetEntry() { ideaId = string.Empty; contactTimer = 0f; selectedThisEntry = false; }
        }

        private sealed class BeckonHand
        {
            public OVRHand hand;
            public OVRSkeleton skeleton;
            public Transform wrist, palm;
            public readonly FingerChain index = new FingerChain();
            public readonly FingerChain middle = new FingerChain();
            public readonly FingerChain ring = new FingerChain();
            public readonly FingerChain pinky = new FingerChain();
            public GesturePhase phase;
            public float poseTimer;
            public bool fistLatched;
            public bool Resolved => wrist != null && palm != null && index.Resolved && middle.Resolved && ring.Resolved && pinky.Resolved;
            public void Reset(bool inspected = false)
            {
                phase = inspected ? GesturePhase.AwaitReturnNeutral : GesturePhase.AwaitBeckonNeutral;
                poseTimer = 0f;
                fistLatched = false;
            }
        }

        public enum GesturePhase
        {
            AwaitBeckonNeutral,
            AwaitBeckonFist,
            AwaitReturnNeutral,
            AwaitReturnFist,
            AwaitReturnOpen
        }

        private sealed class BubbleInspection
        {
            public string ideaId, sessionId, zoneId;
            public int epoch;
            public Vector3 position, scale;
            public Quaternion rotation;
            public int slot;
            public Vector3 slotPosition;
            public GameObject platform;
            public Material platformMaterial;
        }

        private sealed class InspectionZone
        {
            public string zoneId;
            public Vector2 horizontalAnchor;
            public float inspectionHeight;
            public readonly Dictionary<int, string> slots = new Dictionary<int, string>();
            public readonly List<string> oldestFirst = new List<string>();
        }

        private sealed class BubbleMovement
        {
            public string ideaId, sessionId;
            public int epoch;
            public float elapsed;
            public Vector3 fromPosition, toPosition, fromScale, toScale;
            public Quaternion fromRotation, toRotation;
            public bool returning;
            public int hiddenPanelInstanceId;
        }

        public void Configure(Func<BrainstormSessionStore> provider, Transform headsetTransform,
            Action<string> deletedCallback = null,
            Func<BrainstormIdeaRecord, Action<bool>, string> playSummaryCallback = null,
            Action<string> stopSummaryCallback = null)
        {
            storeProvider = provider;
            headset = headsetTransform;
            ideaDeleted = deletedCallback;
            playIdeaSummary = playSummaryCallback;
            stopIdeaSummary = stopSummaryCallback;
            if (bubbleRoot == null)
            {
                GameObject root = new GameObject("Session Idea Bubbles");
                root.transform.SetParent(null, true);
                bubbleRoot = root.transform;
            }
            if (panelRoot == null)
            {
                GameObject root = new GameObject("Idea Detail Panels");
                root.transform.SetParent(null, true);
                panelRoot = root.transform;
            }
            BuildDiagnostics();
            ResolveInteractionOwners();
        }

        public void SetSessionActive(bool active, string sessionId)
        {
            if (!active || string.IsNullOrEmpty(sessionId))
            {
                ClearInspectionWorkspace();
                activeSessionId = string.Empty;
                ClearVisibleBubbles();
                ClearPanels();
                LastLoadResult = "No active session";
                RefreshDiagnostics();
                return;
            }
            if (activeSessionId == sessionId && visible.Count > 0)
            {
                RefreshDiagnostics();
                return;
            }
            ClearInspectionWorkspace();
            activeSessionId = sessionId;
            InitializeWorkspacePose();
            RestoreActiveSession();
        }

        public bool ShowSavedIdea(string ideaId)
        {
            BrainstormSessionStore store = Store;
            BrainstormSession session = store?.ActiveSession;
            if (session == null || session.SessionId != activeSessionId || string.IsNullOrEmpty(ideaId))
            {
                LastSaveResult = "Spawn rejected: active session mismatch";
                RefreshDiagnostics();
                return false;
            }
            if (visible.ContainsKey(ideaId)) return true;
            BrainstormIdeaRecord idea = session.Ideas.Find(item => item.IdeaId == ideaId);
            if (idea == null)
            {
                LastSaveResult = "Spawn rejected: saved idea not found";
                RefreshDiagnostics();
                return false;
            }
            Vector3 position = IsValidSavedTransform(idea.Bubble)
                ? idea.Bubble.Position : FallbackPosition(visible.Count);
            Quaternion rotation = IsValidSavedTransform(idea.Bubble)
                ? idea.Bubble.Rotation : Quaternion.identity;
            Vector3 scale = IsValidSavedTransform(idea.Bubble)
                ? idea.Bubble.Scale : Vector3.one;
            IdeaBubbleView bubble = CreateBubble(idea, position, rotation, scale);
            if (bubble == null) return false;
            LastCreatedIdeaId = ideaId;
            if (!IsValidSavedTransform(idea.Bubble))
            {
                bool saved = store.TryUpdateBubbleTransform(activeSessionId, ideaId,
                    bubble.transform.position, bubble.transform.rotation, bubble.transform.localScale);
                LastSaveResult = saved ? "Transform saved" : "Transform save failed: " + store.LastPersistenceError;
            }
            RefreshDiagnostics();
            return true;
        }

        private void RestoreActiveSession()
        {
            ClearVisibleBubbles();
            BrainstormSessionStore store = Store;
            BrainstormSession session = store?.ActiveSession;
            if (session == null || session.SessionId != activeSessionId)
            {
                LastLoadResult = "Active session unavailable";
                RefreshDiagnostics();
                return;
            }
            int restored = 0;
            for (int i = 0; i < session.Ideas.Count; i++)
            {
                BrainstormIdeaRecord idea = session.Ideas[i];
                if (idea == null || string.IsNullOrEmpty(idea.IdeaId) || visible.ContainsKey(idea.IdeaId)) continue;
                bool valid = IsValidSavedTransform(idea.Bubble);
                Vector3 position = valid ? idea.Bubble.Position : FallbackPosition(i);
                Quaternion rotation = valid ? idea.Bubble.Rotation : Quaternion.identity;
                Vector3 scale = valid ? idea.Bubble.Scale : Vector3.one;
                IdeaBubbleView bubble = CreateBubble(idea, position, rotation, scale);
                if (bubble == null) continue;
                restored++;
                LastRestoredIdeaId = idea.IdeaId;
                if (!valid)
                {
                    bool saved = store.TryUpdateBubbleTransform(activeSessionId, idea.IdeaId,
                        position, rotation, scale);
                    LastSaveResult = saved ? "Fallback transform saved" : "Fallback transform save failed";
                }
            }
            LastLoadResult = "Restored " + restored + " of " + session.Ideas.Count + " saved ideas";
            RefreshDiagnostics();
        }

        private IdeaBubbleView CreateBubble(BrainstormIdeaRecord idea, Vector3 position,
            Quaternion rotation, Vector3 scale)
        {
            if (visible.ContainsKey(idea.IdeaId)) return visible[idea.IdeaId];
            GameObject root = new GameObject("Idea Bubble · " + idea.IdeaId);
            root.transform.SetParent(bubbleRoot, true);
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = IsFinite(scale) && scale.x > 0.01f && scale.y > 0.01f && scale.z > 0.01f
                ? scale : Vector3.one;
            IdeaBubbleView bubble = root.AddComponent<IdeaBubbleView>();
            bubble.Initialize(idea, bubbleDiameter, SelectAndShowDetails, bubbleCategorySaturation);
            bubble.SetInteractionAvailable(interactionAvailable);
            visible.Add(idea.IdeaId, bubble);
            return bubble;
        }

        private void SelectAndShowDetails(BrainstormIdeaRecord idea, BubbleInteractionHand hand, int pointerId)
        {
            if (!interactionAvailable || idea == null || string.IsNullOrEmpty(idea.IdeaId)
                || !visible.TryGetValue(idea.IdeaId, out IdeaBubbleView next) || next == null) return;
            float now = Time.unscaledTime;
            if (!ShouldAcceptSelection(lastSelectionIdeaId, lastSelectionPointerId,
                lastSelectionTime, idea.IdeaId, pointerId, now)) return;
            lastSelectionIdeaId = idea.IdeaId;
            lastSelectionPointerId = pointerId;
            lastSelectionTime = now;
            if (ShouldToggleSelectionOff(selectedIdeaId, openPanels.ContainsKey(idea.IdeaId), idea.IdeaId))
            {
                ClosePanel(openPanels[idea.IdeaId]);
                return;
            }
            if (!ShowDetails(idea)) return;
            selectionOwners[idea.IdeaId] = hand;
            SetActivePanel(idea.IdeaId);
            RefreshDiagnostics();
        }

        public static bool ShouldAcceptSelection(string previousIdeaId, int previousPointerId,
            float previousTime, string ideaId, int pointerId, float now, float guard = 0.2f)
            => previousIdeaId != ideaId || now - previousTime >= guard;

        public static bool ShouldToggleSelectionOff(string activeIdeaId, bool panelOpen, string selectedIdeaIdValue)
            => panelOpen && !string.IsNullOrEmpty(activeIdeaId) && activeIdeaId == selectedIdeaIdValue;

        private bool ShowDetails(BrainstormIdeaRecord idea)
        {
            if (idea == null || string.IsNullOrEmpty(idea.IdeaId)) return false;
            if (openPanels.TryGetValue(idea.IdeaId, out IdeaDetailsPanel existing) && existing != null)
            { existing.FocusPanel(); return true; }
            if (openPanels.Count >= 4)
            { LastLoadResult = "Four idea panels are already open"; RefreshDiagnostics(); return false; }
            int slot = FirstAvailablePanelSlot();
            GameObject root = new GameObject("Idea Details · " + idea.IdeaId, typeof(RectTransform));
            root.transform.SetParent(panelRoot, true);
            IdeaDetailsPanel panel = root.AddComponent<IdeaDetailsPanel>();
            openPanels.Add(idea.IdeaId, panel);
            panel.Configure(idea, visible.TryGetValue(idea.IdeaId, out IdeaBubbleView bubble) ? bubble : null,
                headset, slot, ClosePanel, HandlePanelFocused, DeleteFromPanel,
                playIdeaSummary, stopIdeaSummary, RequestReturn, HandlePanelDragStateChanged,
                SetColorFromPanel);
            panel.SetReturnAvailable(inspections.ContainsKey(idea.IdeaId));
            if (movements.TryGetValue(idea.IdeaId, out BubbleMovement activeMovement))
            {
                panel.SetMovementHidden(true);
                activeMovement.hiddenPanelInstanceId = panel.GetInstanceID();
            }
            if (bubble != null) bubble.SetOpen(true);
            panel.NudgeToAvoid(CollectVisiblePanelPositions(idea.IdeaId));
            return true;
        }

        private string SetColorFromPanel(IdeaDetailsPanel panel,string visualColor)
        {
            if(panel==null||Store?.ActiveSession==null||Store.ActiveSession.SessionId!=activeSessionId)
                return "The active session changed; color was not saved.";
            if(!IdeaVisualColorPalette.IsSupported(visualColor))return "That color is not supported.";
            if(!Store.TryUpdateIdeaVisualColor(activeSessionId,panel.IdeaId,visualColor))
                return string.IsNullOrEmpty(Store.LastPersistenceError)?"The color could not be saved.":Store.LastPersistenceError;
            if(visible.TryGetValue(panel.IdeaId,out IdeaBubbleView bubble)&&bubble!=null)
                bubble.SetVisualColor(visualColor);
            LastSaveResult="Bubble color saved";RefreshDiagnostics();return string.Empty;
        }

        private int FirstAvailablePanelSlot()
        {
            bool[] used = new bool[4];
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null)
                    if (panel.SlotIndex >= 0 && panel.SlotIndex < 4) used[panel.SlotIndex] = true;
            for (int i = 0; i < 4; i++) if (!used[i]) return i;
            return openPanels.Count % 4;
        }

        private void ClosePanel(IdeaDetailsPanel panel)
        {
            if (panel == null) return;
            if (panel == draggedPanel) EndPanelDrag();
            string ideaId = panel.IdeaId;
            openPanels.Remove(ideaId);
            selectionOwners.Remove(ideaId);
            panelFocusOrder.Remove(ideaId);
            if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
                bubble.SetOpen(false);
            if (selectedIdeaId == ideaId) ActivateMostRecentlyFocusedPanel();
            Destroy(panel.gameObject);
            RefreshDiagnostics();
        }

        private string DeleteFromPanel(IdeaDetailsPanel panel)
        {
            if (panel == null || Store?.ActiveSession == null || Store.ActiveSession.SessionId != activeSessionId)
                return "The active session changed; the idea was not deleted.";
            if (panel == draggedPanel) EndPanelDrag();
            string ideaId = panel.IdeaId;
            if (!Store.TryDeleteIdea(activeSessionId, ideaId))
                return string.IsNullOrEmpty(Store.LastPersistenceError)
                    ? "The idea could not be deleted." : Store.LastPersistenceError;
            RemoveInspectionForDeletedIdea(ideaId);
            if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
            {
                bubble.SetOpen(false);
                Destroy(bubble.gameObject);
            }
            visible.Remove(ideaId);
            openPanels.Remove(ideaId);
            selectionOwners.Remove(ideaId);
            panelFocusOrder.Remove(ideaId);
            if (selectedIdeaId == ideaId) ActivateMostRecentlyFocusedPanel();
            Destroy(panel.gameObject);
            ideaDeleted?.Invoke(ideaId);
            LastSaveResult = "Deleted idea " + ideaId;
            RefreshDiagnostics();
            return string.Empty;
        }

        private Vector3 FallbackPosition(int index)
        {
            Transform reference = headset != null ? headset : Camera.main != null ? Camera.main.transform : null;
            OrbyPlaceholderView orby = FindFirstObjectByType<OrbyPlaceholderView>();
            Vector3 anchor = orby != null ? orby.transform.position
                : reference != null ? reference.position + reference.forward * 0.8f : new Vector3(0f, 1.4f, 0.8f);
            Vector3 right = reference != null ? Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized : Vector3.right;
            int column = index % 3;
            int row = index / 3;
            return anchor + right * ((column - 1) * 0.22f) + Vector3.up * (0.10f - row * 0.20f);
        }

        private static bool IsValidSavedTransform(BubbleTransformRecord value)
        {
            if (value == null || !IsFinite(value.Position) || !IsFinite(value.Scale)
                || !IsFinite(value.Rotation)) return false;
            if (value.Scale.x <= 0.01f || value.Scale.y <= 0.01f || value.Scale.z <= 0.01f) return false;
            return value.Position.sqrMagnitude > 0.0001f;
        }

        private static bool IsFinite(Vector3 value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool IsFinite(Quaternion value) => float.IsFinite(value.x)
            && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w)
            && new Vector4(value.x, value.y, value.z, value.w).sqrMagnitude > 0.0001f;

        private void ClearVisibleBubbles()
        {
            ClearSelection();
            ClearPanels();
            foreach (IdeaBubbleView bubble in visible.Values)
                if (bubble != null) Destroy(bubble.gameObject);
            visible.Clear();
        }

        private void ClearPanels()
        {
            EndPanelDrag();
            foreach (string ideaId in openPanels.Keys)
                if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
                    bubble.SetOpen(false);
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null) Destroy(panel.gameObject);
            openPanels.Clear();
            selectionOwners.Clear();
            panelFocusOrder.Clear();
            selectedIdeaId = string.Empty;
            activeSelectionHand = BubbleInteractionHand.Unknown;
        }

        private BrainstormSessionStore Store => storeProvider != null ? storeProvider() : null;

        private void Update()
        {
            UpdateHeadPoseReadiness();
            if (menuController == null || workflowController == null || transcriptionCoordinator == null)
                ResolveInteractionOwners();
            bool available = !string.IsNullOrEmpty(activeSessionId)
                && (menuController == null || (!menuController.IsMenuVisible
                    && !menuController.IsPalmMenuButtonVisible))
                && (workflowController == null || !workflowController.IsBlockingLocalBubbleInteraction)
                && (transcriptionCoordinator == null
                    || !transcriptionCoordinator.IsBlockingLocalBubbleInteraction);
            if (available != interactionAvailable)
            {
                interactionAvailable = available;
                foreach (IdeaBubbleView bubble in visible.Values)
                    if (bubble != null) bubble.SetInteractionAvailable(available);
                if (!available)
                {
                    EndPanelDrag();
                    FinishDirectManipulation(true);
                    HideAllBoundingBoxes();
                    ResetGestureRecognition();
                }
                RefreshDiagnostics();
            }
            UpdateMovements();
            gestureCooldownRemaining = Mathf.Max(0f, gestureCooldownRemaining - Time.unscaledDeltaTime);
            if (interactionAvailable && movements.Count == 0)
            {
                bool panelReserved = UpdatePanelManipulation();
                if (!panelReserved) UpdateDirectManipulation();
                else if (directMode != DirectManipulationMode.None) FinishDirectManipulation(true);
                if (!panelReserved && directMode == DirectManipulationMode.None) UpdateFingertipNearSelection();
                else ResetNearSelectionEntries();
                if (!panelReserved && directMode == DirectManipulationMode.None && gestureCooldownRemaining <= 0f)
                    UpdateBeckonGestures();
            }
        }

        private bool UpdatePanelManipulation()
        {
            bool leftValid=TryGetManipulationHand(leftBeckon,out Vector3 leftPalm,
                out bool leftFist,out bool leftOpen);
            bool rightValid=TryGetManipulationHand(rightBeckon,out Vector3 rightPalm,
                out bool rightFist,out bool rightOpen);

            if(draggedPanel!=null)
            {
                bool valid=panelDragOwner==BubbleInteractionHand.Left?leftValid:rightValid;
                bool release=panelDragOwner==BubbleInteractionHand.Left?leftOpen:rightOpen;
                Vector3 palm=panelDragOwner==BubbleInteractionHand.Left?leftPalm:rightPalm;
                if(!valid||release){EndPanelDrag();return true;}
                draggedPanel.UpdateBorderDrag(palm,panelDragSmoothTime);
                return true;
            }

            IdeaDetailsPanel nearest=null; BubbleInteractionHand owner=BubbleInteractionHand.Unknown;
            Vector3 ownerPalm=Vector3.zero; float best=float.PositiveInfinity;
            foreach(IdeaDetailsPanel panel in openPanels.Values)
            {
                if(panel==null||panel.IsMovementHidden||panel.IsPressed)continue;
                float left=leftValid?panel.DistanceToBorder(leftPalm):float.PositiveInfinity;
                float right=rightValid?panel.DistanceToBorder(rightPalm):float.PositiveInfinity;
                float distance=Mathf.Min(left,right);
                panel.SetBorderProximity(distance<=panelBorderGrabRange);
                if(distance>=best||distance>panelBorderGrabRange)continue;
                best=distance;nearest=panel;
                if(left<=right){owner=BubbleInteractionHand.Left;ownerPalm=leftPalm;}
                else{owner=BubbleInteractionHand.Right;ownerPalm=rightPalm;}
            }
            if(nearest==null){ResetPanelGrabCandidate();return false;}

            bool fist=owner==BubbleInteractionHand.Left?leftFist:rightFist;
            if(!fist){ResetPanelGrabCandidate();return true;}
            if(panelGrabCandidateId!=nearest.IdeaId||panelGrabCandidateHand!=owner)
            {
                panelGrabCandidateId=nearest.IdeaId;panelGrabCandidateHand=owner;panelGrabTimer=0f;
            }
            panelGrabTimer+=Time.unscaledDeltaTime;
            if(panelGrabTimer<panelGrabConfirmation)return true;
            if(directMode!=DirectManipulationMode.None)FinishDirectManipulation(true);
            draggedPanel=nearest;panelDragOwner=owner;nearest.BeginBorderDrag(ownerPalm);
            recordingGestureController?.SetBubbleManipulationSuppressed(true,
                microphoneGestureReleaseCooldown);
            HideAllBoundingBoxes();ResetPanelGrabCandidate();
            return true;
        }

        private void HandlePanelDragStateChanged(IdeaDetailsPanel panel,bool active)
        {
            if(active&&panel!=null)SetActivePanel(panel.IdeaId);
            lastPanelMovementEvent=(active?"Panel drag started: ":"Panel drag ended: ")
                +(panel!=null?panel.IdeaId:"None");
            RefreshDiagnostics();
        }

        private void EndPanelDrag()
        {
            if(draggedPanel!=null)draggedPanel.EndBorderDrag();
            draggedPanel=null;panelDragOwner=BubbleInteractionHand.Unknown;
            ResetPanelGrabCandidate();
            foreach(IdeaDetailsPanel panel in openPanels.Values)
                if(panel!=null)panel.SetBorderProximity(false);
            recordingGestureController?.SetBubbleManipulationSuppressed(false,
                microphoneGestureReleaseCooldown);
            gestureCooldownRemaining=Mathf.Max(gestureCooldownRemaining,microphoneGestureReleaseCooldown);
        }

        private void ResetPanelGrabCandidate()
        {
            panelGrabCandidateId=string.Empty;panelGrabCandidateHand=BubbleInteractionHand.Unknown;
            panelGrabTimer=0f;
        }

        private void ResolveInteractionOwners()
        {
            if (menuController == null) menuController = FindFirstObjectByType<BrainstormMenuController>();
            if (workflowController == null) workflowController = GetComponent<ThinkingSpaceAIWorkflowController>();
            if (transcriptionCoordinator == null) transcriptionCoordinator = GetComponent<SpeechTranscriptionCoordinator>();
            if (recordingGestureController == null)
                recordingGestureController = FindFirstObjectByType<ThumbRecordingGestureController>();
            if (recordingGestureController != null)
            {
                leftBeckon.hand = recordingGestureController.LeftOVRHand;
                leftBeckon.skeleton = recordingGestureController.LeftOVRSkeleton;
                rightBeckon.hand = recordingGestureController.RightOVRHand;
                rightBeckon.skeleton = recordingGestureController.RightOVRSkeleton;
            }
        }

        private void UpdateDirectManipulation()
        {
            if (directMode != DirectManipulationMode.None)
            {
                UpdateActiveDirectManipulation();
                return;
            }
            HideAllBoundingBoxes();
            resizeReservationRemaining = Mathf.Max(0f,
                resizeReservationRemaining - Time.unscaledDeltaTime);
            if (resizeReservationRemaining <= 0f) resizeReservationIdeaId = string.Empty;
            foreach (IdeaBubbleView value in visible.Values)
                if (value != null) value.SetDirectProximity(false);
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null && panel.IsInteracting) return;

            bool leftValid = TryGetManipulationHand(leftBeckon, out Vector3 leftPalm,
                out bool leftFist, out bool leftOpen);
            bool rightValid = TryGetManipulationHand(rightBeckon, out Vector3 rightPalm,
                out bool rightFist, out bool rightOpen);
            IdeaBubbleView bubble = FindTwoHandManipulationCandidate(leftValid, rightValid,
                leftPalm, rightPalm);
            if (bubble != null && bubble.IdeaId != selectedIdeaId)
                SetActiveBubbleForManipulation(bubble);
            if (bubble == null && (!string.IsNullOrEmpty(selectedIdeaId)
                && visible.TryGetValue(selectedIdeaId, out IdeaBubbleView selected))) bubble = selected;
            if (bubble == null || movements.ContainsKey(bubble.IdeaId)) return;
            float leftSurface = leftValid ? MinimumHandSurfaceDistance(leftBeckon, leftPalm, bubble)
                : float.PositiveInfinity;
            float rightSurface = rightValid ? MinimumHandSurfaceDistance(rightBeckon, rightPalm, bubble)
                : float.PositiveInfinity;
            float grabTolerance = ScaleAwareSurfaceTolerance(directGrabSurfaceRange,
                bubble.WorldRadius, .05f, .11f);
            bool leftNear = leftSurface <= grabTolerance;
            bool rightNear = rightSurface <= grabTolerance;
            bubble.SetDirectProximity(leftNear || rightNear);

            bool showBounds = leftValid && rightValid
                && Vector3.Distance(leftPalm, bubble.transform.position) - bubble.WorldRadius <= resizeHandsProximity
                && Vector3.Distance(rightPalm, bubble.transform.position) - bubble.WorldRadius <= resizeHandsProximity;
            bubble.SetBoundingBoxVisible(showBounds);
            if (showBounds)
            {
                int leftCorner = FindNearestCorner(bubble, leftPalm, out float leftCornerDistance);
                int rightCorner = FindNearestCorner(bubble, rightPalm, out float rightCornerDistance);
                bool leftAtHandle = leftCornerDistance <= resizeHandleHoverRange;
                bool rightAtHandle = rightCornerDistance <= resizeHandleHoverRange;
                int acquired = leftFist && leftAtHandle ? leftCorner
                    : rightFist && rightAtHandle ? rightCorner : -1;
                bubble.SetBoundsHandleState(leftAtHandle ? leftCorner
                        : rightAtHandle ? rightCorner : -1,
                    acquired, -1, acquired >= 0);
                if (ShouldReserveResize(leftAtHandle, rightAtHandle))
                {
                    resizeReservationIdeaId = bubble.IdeaId;
                    resizeReservationRemaining = twoHandResizeReservation;
                }
                float acquisitionRange = Mathf.Max(resizeHandleRange, resizeHandleHoverRange * .8f);
                if (leftFist && rightFist && leftCornerDistance <= acquisitionRange
                    && rightCornerDistance <= acquisitionRange
                    && IdeaBubbleView.IsOppositeSide(leftCorner, rightCorner))
                {
                    BeginResize(bubble, leftCorner, rightCorner, leftPalm, rightPalm);
                    return;
                }
                if (resizeReservationIdeaId == bubble.IdeaId && resizeReservationRemaining > 0f)
                    return;
            }
            if (leftNear && leftFist) BeginGrab(bubble, BubbleInteractionHand.Left, leftPalm);
            else if (rightNear && rightFist) BeginGrab(bubble, BubbleInteractionHand.Right, rightPalm);
        }

        private IdeaBubbleView FindTwoHandManipulationCandidate(bool leftValid, bool rightValid,
            Vector3 leftPalm, Vector3 rightPalm)
        {
            if (!leftValid || !rightValid) return null;
            IdeaBubbleView nearest = null;
            float best = float.PositiveInfinity;
            foreach (IdeaBubbleView bubble in visible.Values)
            {
                if (bubble == null || movements.ContainsKey(bubble.IdeaId)) continue;
                float left = DistanceToSphereSurface(leftPalm, bubble.transform.position, bubble.WorldRadius);
                float right = DistanceToSphereSurface(rightPalm, bubble.transform.position, bubble.WorldRadius);
                float score = Mathf.Max(left, right);
                if (ShouldAutoSelectForManipulation(leftValid, rightValid,
                        score, resizeHandsProximity, movements.ContainsKey(bubble.IdeaId))
                    && score < best) { best = score; nearest = bubble; }
            }
            return nearest;
        }

        public static bool ShouldReserveResize(bool leftAtHandle, bool rightAtHandle)
            => leftAtHandle && rightAtHandle;

        public static bool ShouldAutoSelectForManipulation(bool leftTracked, bool rightTracked,
            float maximumSurfaceDistance, float proximityRange, bool moving)
            => leftTracked && rightTracked && !moving && maximumSurfaceDistance <= proximityRange;

        private void SetActiveBubbleForManipulation(IdeaBubbleView bubble)
        {
            if (bubble == null) return;
            if (!string.IsNullOrEmpty(selectedIdeaId) && selectedIdeaId != bubble.IdeaId
                && visible.TryGetValue(selectedIdeaId, out IdeaBubbleView previous) && previous != null)
                previous.SetSelected(false);
            selectedIdeaId = bubble.IdeaId;
            activeSelectionHand = BubbleInteractionHand.Unknown;
            bubble.SetSelected(true);
            ResetGestureRecognition();
            RefreshDiagnostics();
        }

        private void UpdateFingertipNearSelection()
        {
            leftNearSelection.cooldown = Mathf.Max(0f,
                leftNearSelection.cooldown - Time.unscaledDeltaTime);
            rightNearSelection.cooldown = Mathf.Max(0f,
                rightNearSelection.cooldown - Time.unscaledDeltaTime);
            UpdateFingertipNearSelection(leftBeckon, leftNearSelection,
                BubbleInteractionHand.Left, -1001);
            UpdateFingertipNearSelection(rightBeckon, rightNearSelection,
                BubbleInteractionHand.Right, -1002);
        }

        private void UpdateFingertipNearSelection(BeckonHand hand, NearSelectionState state,
            BubbleInteractionHand interactionHand, int pointerId)
        {
            if (hand.hand == null || hand.skeleton == null || !hand.hand.IsTracked
                || hand.hand.HandConfidence != OVRHand.TrackingConfidence.High
                || !ResolveBeckonJoints(hand))
            { state.ResetEntry(); return; }
            IdeaBubbleView nearest = null;
            float nearestSurface = float.PositiveInfinity;
            foreach (IdeaBubbleView bubble in visible.Values)
            {
                if (bubble == null || movements.ContainsKey(bubble.IdeaId)) continue;
                float surface = MinimumFingertipSurfaceDistance(hand, bubble);
                if (surface < nearestSurface) { nearestSurface = surface; nearest = bubble; }
            }
            if (nearest == null) { state.ResetEntry(); return; }
            float hover = ScaleAwareSurfaceTolerance(fingertipHoverRange,
                nearest.WorldRadius, .035f, .075f);
            float exit = ScaleAwareSurfaceTolerance(fingertipExitRange,
                nearest.WorldRadius, .05f, .095f);
            if (nearestSurface <= hover) nearest.SetDirectProximity(true);
            if (state.ideaId != nearest.IdeaId)
            {
                if (!string.IsNullOrEmpty(state.ideaId)
                    && visible.TryGetValue(state.ideaId, out IdeaBubbleView previous) && previous != null)
                {
                    float previousExit = ScaleAwareSurfaceTolerance(fingertipExitRange,
                        previous.WorldRadius, .05f, .095f);
                    if (MinimumFingertipSurfaceDistance(hand, previous) <= previousExit) return;
                }
                state.ResetEntry(); state.ideaId = nearest.IdeaId;
            }
            if (nearestSurface > exit) { state.ResetEntry(); return; }
            float contact = ScaleAwareSurfaceTolerance(fingertipContactRange,
                nearest.WorldRadius, .01f, .032f);
            if (!IsNearSelectionEligible(nearestSurface, contact,
                    state.selectedThisEntry, state.cooldown))
            { if (nearestSurface > contact) state.contactTimer = 0f; return; }
            state.contactTimer += Time.unscaledDeltaTime;
            if (state.contactTimer < fingertipSelectionHold) return;
            state.selectedThisEntry = true; state.cooldown = nearSelectionCooldown;
            if (selectedIdeaId != nearest.IdeaId)
                SelectAndShowDetails(nearest.Idea, interactionHand, pointerId);
        }

        private void ResetNearSelectionEntries()
        {
            leftNearSelection.ResetEntry(); rightNearSelection.ResetEntry();
        }

        private static float MinimumFingertipSurfaceDistance(BeckonHand hand, IdeaBubbleView bubble)
        {
            float result = DistanceToSphereSurface(hand.index.tip.position, bubble.transform.position,
                bubble.WorldRadius);
            result = Mathf.Min(result, DistanceToSphereSurface(hand.middle.tip.position,
                bubble.transform.position, bubble.WorldRadius));
            return Mathf.Min(result, DistanceToSphereSurface(hand.ring.tip.position,
                bubble.transform.position, bubble.WorldRadius));
        }

        private static float MinimumHandSurfaceDistance(BeckonHand hand, Vector3 palm,
            IdeaBubbleView bubble)
            => Mathf.Min(DistanceToSphereSurface(palm, bubble.transform.position, bubble.WorldRadius),
                MinimumFingertipSurfaceDistance(hand, bubble));

        public static float ScaleAwareSurfaceTolerance(float baseTolerance, float worldRadius,
            float minimum, float maximum)
            => Mathf.Clamp(baseTolerance + Mathf.Max(0f, worldRadius) * .08f, minimum, maximum);

        public static bool IsNearSelectionEligible(float surfaceDistance, float contactTolerance,
            bool selectedThisEntry, float cooldownRemaining)
            => surfaceDistance <= contactTolerance && !selectedThisEntry && cooldownRemaining <= 0f;

        private void UpdateActiveDirectManipulation()
        {
            if (!visible.TryGetValue(directIdeaId, out IdeaBubbleView bubble) || bubble == null)
            { CancelDirectState(); return; }
            bool leftValid = TryGetManipulationHand(leftBeckon, out Vector3 leftPalm,
                out bool leftFist, out bool leftOpen);
            bool rightValid = TryGetManipulationHand(rightBeckon, out Vector3 rightPalm,
                out bool rightFist, out bool rightOpen);
            if (directMode != DirectManipulationMode.Resizing
                && inspections.TryGetValue(directIdeaId, out BubbleInspection grabbedInspection)
                && grabbedInspection.platform != null)
                grabbedInspection.platform.SetActive(Vector3.Distance(bubble.transform.position,
                    grabbedInspection.slotPosition) <= inspectionSlotReleaseRadius);
            if (directMode == DirectManipulationMode.Resizing)
            {
                if (!leftValid || !rightValid)
                { FinishDirectManipulation(true); return; }
                if (leftOpen || rightOpen)
                {
                    directReleaseTimer += Time.unscaledDeltaTime;
                    if (directReleaseTimer >= grabReleaseHold) FinishDirectManipulation(true);
                    return;
                }
                directReleaseTimer = 0f;
                float distance = Vector3.Distance(leftPalm, rightPalm);
                float rawTarget = CalculateUniformResize(directStartScale.x, directStartHandDistance,
                    distance, minimumBubbleScale, maximumBubbleScale);
                directScaleTarget = ApplyDeadband(directScaleTarget, rawTarget, resizeScaleDeadband);
                float smoothed = Mathf.Lerp(bubble.transform.localScale.x, directScaleTarget,
                    1f - Mathf.Exp(-resizeSmoothing * Time.unscaledDeltaTime));
                bubble.transform.position = directFixedCenter;
                bubble.transform.localScale = Vector3.one * smoothed;
                return;
            }

            BeckonHand owner = directOwner == BubbleInteractionHand.Left ? leftBeckon : rightBeckon;
            bool valid = directOwner == BubbleInteractionHand.Left ? leftValid : rightValid;
            Vector3 palm = directOwner == BubbleInteractionHand.Left ? leftPalm : rightPalm;
            bool open = directOwner == BubbleInteractionHand.Left ? leftOpen : rightOpen;
            if (!valid) { FinishDirectManipulation(true); return; }
            if (open)
            {
                directReleaseTimer += Time.unscaledDeltaTime;
                if (directReleaseTimer >= grabReleaseHold) FinishDirectManipulation(true);
                return;
            }
            directReleaseTimer = 0f;
            if (directMode == DirectManipulationMode.GlideToGrip)
            {
                directElapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(directElapsed / grabGlideDuration));
                bubble.transform.position = Vector3.Lerp(directStartPosition, palm, t);
                if (t >= 1f) directMode = DirectManipulationMode.Grabbed;
            }
            else bubble.transform.position = palm;
        }

        private bool TryGetManipulationHand(BeckonHand hand, out Vector3 palm,
            out bool fist, out bool open)
        {
            palm = Vector3.zero; fist = open = false;
            if (hand.hand == null || hand.skeleton == null || !hand.hand.IsTracked
                || hand.hand.HandConfidence != OVRHand.TrackingConfidence.High
                || !ResolveBeckonJoints(hand)) return false;
            palm = hand.palm.position;
            bool pinching = hand.hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            float[] ratios = GetCurlRatios(hand);
            fist = IsNaturalFist(ratios, pinching, hand.fistLatched,
                curledChordRatio, nearCurledChordRatio, fistReleaseChordRatio);
            hand.fistLatched = fist;
            open = CountExtended(hand) >= 3;
            return true;
        }

        private void BeginGrab(IdeaBubbleView bubble, BubbleInteractionHand hand, Vector3 palm)
        {
            directMode = DirectManipulationMode.GlideToGrip;
            directIdeaId = bubble.IdeaId; directOwner = hand;
            directStartPosition = bubble.transform.position;
            directElapsed = directReleaseTimer = 0f;
            HidePanelForDirectMovement(bubble.IdeaId);
            bubble.SetMoving(true); bubble.SetBoundingBoxVisible(false);
            recordingGestureController?.SetBubbleManipulationSuppressed(true,
                microphoneGestureReleaseCooldown);
            ResetGestureRecognition();
        }

        private void BeginResize(IdeaBubbleView bubble, int firstCorner, int secondCorner,
            Vector3 leftPalm, Vector3 rightPalm)
        {
            directMode = DirectManipulationMode.Resizing;
            directIdeaId = bubble.IdeaId; directOwner = BubbleInteractionHand.Unknown;
            directFirstCorner = firstCorner; directSecondCorner = secondCorner;
            directFixedCenter = bubble.transform.position;
            directStartScale = bubble.transform.localScale;
            directScaleTarget = directStartScale.x;
            directStartHandDistance = Mathf.Max(.001f, Vector3.Distance(leftPalm, rightPalm));
            directReleaseTimer = 0f;
            HidePanelForDirectMovement(bubble.IdeaId);
            bubble.SetMoving(true);
            bubble.SetBoundsHandleState(-1, firstCorner, secondCorner);
            recordingGestureController?.SetBubbleManipulationSuppressed(true,
                microphoneGestureReleaseCooldown);
            ResetGestureRecognition();
        }

        private void HidePanelForDirectMovement(string ideaId)
        {
            directPanelInstanceId = 0;
            if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
            {
                if (panel == draggedPanel) EndPanelDrag();
                directPanelInstanceId = panel.GetInstanceID();
                panel.SetMovementHidden(true);
                lastPanelMovementEvent = "Hidden for direct manipulation: " + ideaId;
            }
        }

        private void FinishDirectManipulation(bool persist)
        {
            if (directMode == DirectManipulationMode.None) return;
            if (visible.TryGetValue(directIdeaId, out IdeaBubbleView bubble) && bubble != null)
            {
                bubble.SetMoving(false); bubble.SetBoundingBoxVisible(false); bubble.SetDirectProximity(false);
                if (persist) CommitManualTransform(bubble);
                if (openPanels.TryGetValue(directIdeaId, out IdeaDetailsPanel panel) && panel != null
                    && ShouldRestoreMovedPanel(directPanelInstanceId, panel.GetInstanceID()))
                {
                    panel.RelocateAfterBubbleMovement(CollectVisiblePanelPositions(directIdeaId));
                    lastPanelMovementEvent = "Restored after direct manipulation: " + directIdeaId;
                }
            }
            CancelDirectState();
        }

        private void CommitManualTransform(IdeaBubbleView bubble)
        {
            Vector3 persistedPosition = bubble.transform.position;
            Quaternion persistedRotation = bubble.transform.rotation;
            Vector3 persistedScale = bubble.transform.localScale;
            if (inspections.TryGetValue(bubble.IdeaId, out BubbleInspection inspection))
            {
                if (directMode == DirectManipulationMode.Resizing)
                {
                    inspection.scale = bubble.transform.localScale;
                    persistedPosition = inspection.position;
                    persistedRotation = inspection.rotation;
                    persistedScale = inspection.scale;
                    UpdateInspectionPlatform(inspection, bubble);
                }
                else if (!ShouldReleaseInspectionSlot(bubble.transform.position,
                    inspection.slotPosition, inspectionSlotReleaseRadius, false))
                {
                    persistedPosition = inspection.position;
                    persistedRotation = inspection.rotation;
                    persistedScale = inspection.scale;
                    if (inspection.platform != null) inspection.platform.SetActive(true);
                }
                else
                {
                    ReleaseInspectionOwnership(inspection, true);
                }
            }
            bool saved = Store != null && Store.TryUpdateBubbleTransform(activeSessionId, bubble.IdeaId,
                persistedPosition, persistedRotation, persistedScale);
            LastSaveResult = saved ? "Manual transform saved" : "Manual transform save failed";
        }

        private void CancelDirectState()
        {
            recordingGestureController?.SetBubbleManipulationSuppressed(false,
                microphoneGestureReleaseCooldown);
            directMode = DirectManipulationMode.None; directIdeaId = string.Empty;
            directOwner = BubbleInteractionHand.Unknown; directPanelInstanceId = 0;
            directFirstCorner = directSecondCorner = -1; directReleaseTimer = directElapsed = 0f;
            resizeReservationIdeaId = string.Empty; resizeReservationRemaining = 0f;
            gestureCooldownRemaining = gestureCooldown;
            ResetGestureRecognition();
        }

        private void HideAllBoundingBoxes()
        {
            foreach (IdeaBubbleView bubble in visible.Values)
                if (bubble != null) bubble.SetBoundingBoxVisible(false);
        }

        public static float DistanceToSphereSurface(Vector3 point, Vector3 center, float radius)
            => Mathf.Max(0f, Vector3.Distance(point, center) - Mathf.Max(0f, radius));

        private static int FindNearestCorner(IdeaBubbleView bubble, Vector3 position, out float distance)
        {
            int nearest = -1; distance = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                float candidate = Vector3.Distance(position, bubble.GetBoundsCornerWorldPosition(i));
                if (candidate < distance) { distance = candidate; nearest = i; }
            }
            return nearest;
        }

        public static float CalculateUniformResize(float initialScale, float initialHandDistance,
            float currentHandDistance, float minimumScale, float maximumScale)
            => Mathf.Clamp(initialScale * (initialHandDistance < .001f
                ? 1f : currentHandDistance / initialHandDistance), minimumScale, maximumScale);

        public static float ApplyDeadband(float previousTarget, float candidate, float deadband)
            => Mathf.Abs(candidate - previousTarget) < Mathf.Max(0f, deadband)
                ? previousTarget : candidate;

        private void UpdateBeckonGestures()
        {
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null && panel.IsInteracting) { ResetGestureRecognition(); return; }
            if (string.IsNullOrEmpty(selectedIdeaId) || !visible.ContainsKey(selectedIdeaId))
            { ResetGestureRecognition(); return; }
            BeckonHand owner = IsGestureOwner(activeSelectionHand, BubbleInteractionHand.Left) ? leftBeckon
                : IsGestureOwner(activeSelectionHand, BubbleInteractionHand.Right) ? rightBeckon : null;
            if (owner == null) { ResetGestureRecognition(); return; }
            bool selectedInspected = inspections.ContainsKey(selectedIdeaId);
            GestureAction action = EvaluateHandGesture(owner, selectedInspected);
            if (action == GestureAction.Beckon) BeginInspection(selectedIdeaId);
            else if (action == GestureAction.Return) RequestReturn(selectedIdeaId);
            else if (action == GestureAction.Resummon) ResummonInspection(selectedIdeaId);
            else return;
            gestureCooldownRemaining = gestureCooldown;
        }

        public enum GestureAction { None, Beckon, Return, Resummon }

        private GestureAction EvaluateHandGesture(BeckonHand hand, bool inspected)
        {
            if (hand.hand == null || hand.skeleton == null || !hand.hand.IsTracked
                || hand.hand.HandConfidence != OVRHand.TrackingConfidence.High
                || !ResolveBeckonJoints(hand))
            { hand.Reset(inspected); return GestureAction.None; }

            int extended = CountExtended(hand);
            bool pinching = hand.hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            float[] curlRatios = GetCurlRatios(hand);
            bool fist = IsNaturalFist(curlRatios, pinching, hand.fistLatched,
                curledChordRatio, nearCurledChordRatio, fistReleaseChordRatio);
            hand.fistLatched = fist;
            lastGestureDiagnostic = pinching ? "Rejected: index pinch"
                : fist ? "Natural fist accepted"
                : "Rejected: finger curl " + FormatCurlRatios(curlRatios);
            bool open = extended >= 3;
            if (!visible.TryGetValue(selectedIdeaId, out IdeaBubbleView bubble) || bubble == null)
                return GestureAction.None;
            bool nearBubble = Vector3.Distance(hand.wrist.position, bubble.transform.position)
                < nearGrabExclusionRadius;
            bool allowResummon = inspected && ShouldResummon(bubble.transform.position,
                GetInspectionSlotPosition(inspections[selectedIdeaId].slot,
                    headset != null ? headset.position : workspaceAnchor), resummonDistance);

            return AdvanceGestureState(ref hand.phase, ref hand.poseTimer, true, inspected,
                open, fist, nearBubble, allowResummon, Time.unscaledDeltaTime, neutralHoldDuration,
                fistHoldDuration, returnOpenHoldDuration);
        }

        public static bool IsCompleteFist(int curledFingerCount, bool indexPinching)
            => curledFingerCount >= 4 && !indexPinching;

        public static bool IsNaturalFist(IReadOnlyList<float> chordRatios, bool indexPinching,
            bool wasFist, float curledThreshold = .64f, float nearCurledThreshold = .74f,
            float releaseThreshold = .82f)
        {
            if (indexPinching || chordRatios == null || chordRatios.Count < 4) return false;
            int stronglyCurled = 0;
            for (int i = 0; i < 4; i++)
            {
                float ratio = chordRatios[i];
                if (!float.IsFinite(ratio)) return false;
                if (ratio <= (wasFist ? nearCurledThreshold : curledThreshold)) stronglyCurled++;
                float allowed = wasFist ? releaseThreshold : nearCurledThreshold;
                if (ratio > allowed) return false;
            }
            return stronglyCurled >= (wasFist ? 2 : 3);
        }

        public static bool IsGestureOwner(BubbleInteractionHand selectedOwner,
            BubbleInteractionHand candidate)
            => selectedOwner != BubbleInteractionHand.Unknown && selectedOwner == candidate;

        public static bool ShouldResummon(Vector3 currentPosition, Vector3 currentUserSlot,
            float minimumDistance)
            => Vector3.Distance(currentPosition, currentUserSlot) >= minimumDistance;

        public static bool AccumulatePose(ref float timer, bool valid, float duration, float delta = -1f)
        {
            timer = valid ? timer + (delta >= 0f ? delta : Time.unscaledDeltaTime) : 0f;
            return timer >= duration;
        }

        public static GestureAction AdvanceGestureState(ref GesturePhase phase, ref float timer,
            bool tracked, bool inspected, bool open, bool fist, bool nearBubble,
            bool allowResummon, float delta,
            float neutralDuration = 0.2f, float fistDuration = 0.35f,
            float openDuration = 0.15f)
        {
            if (!tracked)
            {
                phase = inspected ? GesturePhase.AwaitReturnNeutral : GesturePhase.AwaitBeckonNeutral;
                timer = 0f;
                return GestureAction.None;
            }
            switch (phase)
            {
                case GesturePhase.AwaitBeckonNeutral:
                    if (AccumulatePose(ref timer, open, neutralDuration, delta))
                    { phase = GesturePhase.AwaitBeckonFist; timer = 0f; }
                    break;
                case GesturePhase.AwaitBeckonFist:
                    if (nearBubble) timer = 0f;
                    else if (AccumulatePose(ref timer, fist, fistDuration, delta))
                    { timer = 0f; return GestureAction.Beckon; }
                    break;
                case GesturePhase.AwaitReturnNeutral:
                    if (AccumulatePose(ref timer, open, neutralDuration, delta))
                    { phase = GesturePhase.AwaitReturnFist; timer = 0f; }
                    break;
                case GesturePhase.AwaitReturnFist:
                    if (nearBubble) timer = 0f;
                    else if (AccumulatePose(ref timer, fist, fistDuration, delta))
                    {
                        timer = 0f;
                        if (allowResummon) return GestureAction.Resummon;
                        phase = GesturePhase.AwaitReturnOpen;
                    }
                    break;
                case GesturePhase.AwaitReturnOpen:
                    if (nearBubble) timer = 0f;
                    else if (AccumulatePose(ref timer, open, openDuration, delta))
                    { timer = 0f; return GestureAction.Return; }
                    break;
            }
            return GestureAction.None;
        }

        private void BeginInspection(string ideaId)
        {
            if (string.IsNullOrEmpty(ideaId)
                || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null
                || !openPanels.ContainsKey(ideaId) || headset == null) return;
            if (inspections.ContainsKey(ideaId))
            {
                ResummonInspection(ideaId);
                return;
            }
            if (!TryGetReadyHeadPosition(out Vector3 currentAnchor))
            {
                lastGestureDiagnostic = "Summon deferred: tracked head pose not initialized";
                return;
            }
            EnsureWorkspacePose();
            InspectionZone zone = FindOrCreateInspectionZone(currentAnchor);
            if (zone == null) return;
            int slot = FindAvailableInspectionSlot(zone);
            if (slot < 0) slot = EvictOldestInspection(zone);
            if (slot < 0) return;
            BubbleInspection inspection = new BubbleInspection
            {
                ideaId=ideaId, sessionId=activeSessionId, epoch=operationEpoch,
                zoneId=zone.zoneId, slot=slot,
                position=bubble.transform.position, rotation=bubble.transform.rotation,
                scale=bubble.transform.localScale
            };
            inspections.Add(ideaId, inspection);
            zone.slots[slot] = ideaId;
            zone.oldestFirst.Add(ideaId);
            inspection.slotPosition = ResolveInspectionDestination(zone, slot, ideaId);
            StartMovement(bubble, inspection.slotPosition,
                bubble.transform.rotation, bubble.transform.localScale, false);
        }

        private void RequestReturn(string ideaId)
        {
            if (!inspections.TryGetValue(ideaId, out BubbleInspection inspection)
                || movements.ContainsKey(ideaId) || inspection.sessionId != activeSessionId
                || inspection.epoch != operationEpoch
                || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null) return;
            ReleaseInspectionOwnership(inspection, true);
            if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
                panel.SetReturnAvailable(false);
            StartMovement(bubble, inspection.position, inspection.rotation, inspection.scale, true);
            ResetGestureRecognition();
        }

        private void ResummonInspection(string ideaId)
        {
            if (headset == null || !TryGetReadyHeadPosition(out Vector3 currentHeadPosition)
                || !inspections.TryGetValue(ideaId, out BubbleInspection inspection)
                || movements.ContainsKey(ideaId) || inspection.sessionId != activeSessionId
                || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null) return;
            InspectionZone targetZone = FindOrCreateInspectionZone(currentHeadPosition);
            if (targetZone == null) return;
            if (inspection.zoneId != targetZone.zoneId)
            {
                int targetSlot = FindAvailableInspectionSlot(targetZone);
                if (targetSlot < 0) targetSlot = EvictOldestInspection(targetZone);
                if (targetSlot < 0) return;
                ReleaseInspectionOwnership(inspection, false);
                inspection.zoneId = targetZone.zoneId;
                inspection.slot = targetSlot;
                targetZone.slots[targetSlot] = ideaId;
                targetZone.oldestFirst.Add(ideaId);
            }
            else DestroyInspectionPlatform(inspection);
            if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
                panel.SetReturnAvailable(false);
            inspection.slotPosition = ResolveInspectionDestination(targetZone,
                inspection.slot, ideaId);
            StartMovement(bubble, inspection.slotPosition,
                bubble.transform.rotation, bubble.transform.localScale, false);
            ResetGestureRecognition();
        }

        private void StartMovement(IdeaBubbleView bubble, Vector3 position, Quaternion rotation,
            Vector3 scale, bool returning)
        {
            int hiddenPanelInstanceId = 0;
            if (openPanels.TryGetValue(bubble.IdeaId, out IdeaDetailsPanel panel) && panel != null)
            {
                if (panel == draggedPanel) EndPanelDrag();
                hiddenPanelInstanceId = panel.GetInstanceID();
                panel.SetMovementHidden(true);
                lastPanelMovementEvent = "Hidden for movement: " + bubble.IdeaId;
            }
            movements[bubble.IdeaId] = new BubbleMovement
            {
                ideaId=bubble.IdeaId, sessionId=activeSessionId, epoch=operationEpoch,
                fromPosition=bubble.transform.position, toPosition=position,
                fromRotation=bubble.transform.rotation, toRotation=rotation,
                fromScale=bubble.transform.localScale, toScale=scale, returning=returning,
                hiddenPanelInstanceId=hiddenPanelInstanceId
            };
            bubble.SetMoving(true);
        }

        private void UpdateMovements()
        {
            if (movements.Count == 0) return;
            var completed = new List<string>();
            foreach (KeyValuePair<string, BubbleMovement> pair in movements)
            {
                BubbleMovement movement = pair.Value;
                if (movement.sessionId != activeSessionId || movement.epoch != operationEpoch
                    || !visible.TryGetValue(movement.ideaId, out IdeaBubbleView bubble) || bubble == null)
                { completed.Add(pair.Key); continue; }
                movement.elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(movement.elapsed / movementDuration));
                bubble.transform.position = Vector3.LerpUnclamped(movement.fromPosition, movement.toPosition, t);
                bubble.transform.rotation = Quaternion.SlerpUnclamped(movement.fromRotation, movement.toRotation, t);
                bubble.transform.localScale = Vector3.LerpUnclamped(movement.fromScale, movement.toScale, t);
                if (t < 1f) continue;
                bubble.SetMoving(false);
                bool slotStillOwned = inspections.TryGetValue(movement.ideaId,
                    out BubbleInspection arrived);
                if (ShouldShowInspectionPlatform(true, slotStillOwned, movement.returning))
                    CreateInspectionPlatform(arrived, bubble);
                if (openPanels.TryGetValue(movement.ideaId, out IdeaDetailsPanel panel) && panel != null
                    && ShouldRestoreMovedPanel(movement.hiddenPanelInstanceId, panel.GetInstanceID()))
                {
                    panel.RelocateAfterBubbleMovement(CollectVisiblePanelPositions(movement.ideaId));
                    lastPanelMovementEvent = "Restored after movement: " + movement.ideaId;
                    if (!movement.returning && inspections.ContainsKey(movement.ideaId))
                        panel.SetReturnAvailable(true);
                }
                completed.Add(pair.Key);
            }
            for (int i = 0; i < completed.Count; i++) movements.Remove(completed[i]);
            if (completed.Count > 0) ResetGestureRecognition();
        }

        private int EvictOldestInspection(InspectionZone zone)
        {
            while (zone != null && zone.oldestFirst.Count > 0)
            {
                string oldestId = OldestInspectionId(zone.oldestFirst);
                zone.oldestFirst.RemoveAt(0);
                if (!inspections.TryGetValue(oldestId, out BubbleInspection oldest)) continue;
                zone.slots.Remove(oldest.slot);
                inspections.Remove(oldestId);
                DestroyInspectionPlatform(oldest);
                if (openPanels.TryGetValue(oldestId, out IdeaDetailsPanel panel) && panel != null)
                    panel.SetReturnAvailable(false);
                if (visible.TryGetValue(oldestId, out IdeaBubbleView bubble) && bubble != null)
                    StartMovement(bubble, oldest.position, oldest.rotation, oldest.scale, true);
                return oldest.slot;
            }
            return -1;
        }

        private void RemoveInspectionForDeletedIdea(string ideaId)
        {
            if (directIdeaId == ideaId) CancelDirectState();
            if (inspections.TryGetValue(ideaId, out BubbleInspection inspection))
                ReleaseInspectionOwnership(inspection, true);
            movements.Remove(ideaId);
            if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
                bubble.SetMoving(false);
        }

        private void ReleaseInspectionOwnership(BubbleInspection inspection, bool removeRegistry)
        {
            if (inspection == null) return;
            if (inspectionZones.TryGetValue(inspection.zoneId, out InspectionZone zone))
            {
                if (zone.slots.TryGetValue(inspection.slot, out string owner)
                    && owner == inspection.ideaId) zone.slots.Remove(inspection.slot);
                zone.oldestFirst.Remove(inspection.ideaId);
            }
            DestroyInspectionPlatform(inspection);
            if (removeRegistry) inspections.Remove(inspection.ideaId);
        }

        private void CreateInspectionPlatform(BubbleInspection inspection, IdeaBubbleView bubble)
        {
            if (inspection == null || bubble == null || inspection.platform != null) return;
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            platform.name = "Inspection Platform · " + inspection.zoneId + " · " + inspection.slot;
            platform.transform.SetParent(bubbleRoot, true);
            float diameter = Mathf.Max(.16f, bubble.WorldRadius * 2f + inspectionClearanceMargin);
            platform.transform.position = inspection.slotPosition - Vector3.up * (bubble.WorldRadius + .012f);
            platform.transform.localScale = new Vector3(diameter, .008f, diameter);
            Collider collider = platform.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new Material(shader) { name = "Inspection Platform Runtime Material" };
            Color color = new Color(.12f, .72f, .9f, .30f);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", 5f);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", 10f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            platform.GetComponent<Renderer>().sharedMaterial = material;
            inspection.platform = platform;
            inspection.platformMaterial = material;
        }

        private void UpdateInspectionPlatform(BubbleInspection inspection, IdeaBubbleView bubble)
        {
            if (inspection?.platform == null || bubble == null) return;
            float diameter = Mathf.Max(.16f, bubble.WorldRadius * 2f + inspectionClearanceMargin);
            inspection.platform.transform.position = inspection.slotPosition
                - Vector3.up * (bubble.WorldRadius + .012f);
            inspection.platform.transform.localScale = new Vector3(diameter, .008f, diameter);
            inspection.platform.SetActive(true);
        }

        private static void DestroyInspectionPlatform(BubbleInspection inspection)
        {
            if (inspection == null) return;
            if (inspection.platform != null) Destroy(inspection.platform);
            if (inspection.platformMaterial != null) Destroy(inspection.platformMaterial);
            inspection.platform = null;
            inspection.platformMaterial = null;
        }

        private List<Vector3> CollectVisiblePanelPositions(string excludingIdeaId)
        {
            var occupied = new List<Vector3>();
            foreach (KeyValuePair<string, IdeaDetailsPanel> pair in openPanels)
                if (pair.Key != excludingIdeaId && pair.Value != null && !pair.Value.IsMovementHidden)
                    occupied.Add(pair.Value.transform.position);
            foreach (KeyValuePair<string, IdeaBubbleView> pair in visible)
                if (pair.Key != excludingIdeaId && pair.Value != null)
                    occupied.Add(pair.Value.transform.position);
            return occupied;
        }

        public static bool ShouldRestoreMovedPanel(int hiddenPanelInstanceId, int currentPanelInstanceId)
            => hiddenPanelInstanceId != 0 && hiddenPanelInstanceId == currentPanelInstanceId;

        private void UpdateHeadPoseReadiness()
        {
            if (headset == null || !IsHeadPoseInitialized(headset.position, headset.localPosition,
                    headset.gameObject.activeInHierarchy))
            {
                stableHeadPoseFrames = 0;
                return;
            }
            if (stableHeadPoseFrames == 0 || Vector3.Distance(lastHeadWorldPosition, headset.position) < 0.5f)
                stableHeadPoseFrames++;
            else stableHeadPoseFrames = 1;
            lastHeadWorldPosition = headset.position;
        }

        private bool TryGetReadyHeadPosition(out Vector3 position)
        {
            position = lastHeadWorldPosition;
            return stableHeadPoseFrames >= 2;
        }

        public static bool IsHeadPoseInitialized(Vector3 worldPosition, Vector3 localPosition,
            bool activeInHierarchy)
            => activeInHierarchy && IsFinite(worldPosition) && IsFinite(localPosition)
                && localPosition.sqrMagnitude > 0.0025f;

        private void ClearInspectionWorkspace()
        {
            FinishDirectManipulation(true);
            ResetNearSelectionEntries();
            resizeReservationIdeaId = string.Empty;
            resizeReservationRemaining = 0f;
            HideAllBoundingBoxes();
            recordingGestureController?.ClearBubbleManipulationSuppression();
            operationEpoch++;
            foreach (KeyValuePair<string, BubbleMovement> pair in movements)
                if (visible.TryGetValue(pair.Key, out IdeaBubbleView bubble) && bubble != null)
                    bubble.SetMoving(false);
            foreach (string ideaId in inspections.Keys)
                if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
                    panel.SetReturnAvailable(false);
            foreach (BubbleInspection inspection in inspections.Values)
                DestroyInspectionPlatform(inspection);
            movements.Clear();
            inspections.Clear();
            inspectionZones.Clear();
            nextInspectionZoneNumber = 1;
            workspaceInitialized = false;
            ResetGestureRecognition();
        }

        private void InitializeWorkspacePose()
        {
            if (headset == null || !IsHeadPoseInitialized(headset.position, headset.localPosition,
                    headset.gameObject.activeInHierarchy))
            {
                workspaceInitialized = false;
                return;
            }
            workspaceAnchor = headset.position;
            workspaceForward = Vector3.forward;
            workspaceInitialized = true;
        }

        private void EnsureWorkspacePose()
        {
            if (!workspaceInitialized) InitializeWorkspacePose();
        }

        [ContextMenu("Recenter Inspection Workspace")]
        public void RecenterInspectionWorkspace()
        {
            if (headset == null || movements.Count > 0) return;
            if (!TryGetReadyHeadPosition(out Vector3 currentHead)) return;
            InspectionZone zone = FindNearestInspectionZone(currentHead, float.PositiveInfinity);
            if (zone == null) return;
            zone.horizontalAnchor = new Vector2(currentHead.x, currentHead.z);
            zone.inspectionHeight = currentHead.y;
            foreach (string ideaId in new List<string>(zone.oldestFirst))
            {
                if (!inspections.TryGetValue(ideaId, out BubbleInspection inspection)
                    || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null) continue;
                DestroyInspectionPlatform(inspection);
                if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
                    panel.SetReturnAvailable(false);
                inspection.slotPosition = ResolveInspectionDestination(zone, inspection.slot, ideaId);
                StartMovement(bubble, inspection.slotPosition,
                    bubble.transform.rotation, bubble.transform.localScale, false);
            }
            ResetGestureRecognition();
        }

        private int FindAvailableInspectionSlot(InspectionZone zone)
        {
            var occupied = new List<int>();
            if (zone != null) foreach (int slot in zone.slots.Keys) occupied.Add(slot);
            return FindFirstFreeSlot(occupied, MaximumInspectedBubbles);
        }

        private InspectionZone FindOrCreateInspectionZone(Vector3 currentHeadPosition)
        {
            InspectionZone existing = FindNearestInspectionZone(currentHeadPosition,
                inspectionZoneMembershipDistance);
            if (existing != null) return existing;
            if (inspectionZones.Count >= maximumInspectionZones)
            {
                LastLoadResult = "Inspection zone limit reached";
                RefreshDiagnostics();
                return null;
            }
            string zoneId = "zone-" + nextInspectionZoneNumber++.ToString("D4");
            var zone = new InspectionZone
            {
                zoneId = zoneId,
                horizontalAnchor = new Vector2(currentHeadPosition.x, currentHeadPosition.z),
                inspectionHeight = currentHeadPosition.y
            };
            inspectionZones.Add(zoneId, zone);
            return zone;
        }

        private InspectionZone FindNearestInspectionZone(Vector3 position, float maximumDistance)
        {
            InspectionZone best = null;
            float bestDistance = maximumDistance;
            Vector2 horizontal = new Vector2(position.x, position.z);
            foreach (InspectionZone zone in inspectionZones.Values)
            {
                float distance = Vector2.Distance(horizontal, zone.horizontalAnchor);
                if ((best == null && distance <= bestDistance)
                    || distance < bestDistance - .0001f
                    || (Mathf.Abs(distance - bestDistance) <= .0001f && best != null
                        && string.CompareOrdinal(zone.zoneId, best.zoneId) < 0))
                { best = zone; bestDistance = distance; }
            }
            return best;
        }

        public static float HorizontalDistance(Vector3 first, Vector3 second)
            => Vector2.Distance(new Vector2(first.x, first.z), new Vector2(second.x, second.z));

        public static bool IsWithinInspectionZone(Vector3 userPosition, Vector2 zoneAnchor,
            float membershipDistance)
            => Vector2.Distance(new Vector2(userPosition.x, userPosition.z), zoneAnchor)
                <= Mathf.Max(0f, membershipDistance);

        public static bool ShouldReleaseInspectionSlot(Vector3 bubblePosition, Vector3 slotPosition,
            float releaseRadius, bool resizing)
            => !resizing && Vector3.Distance(bubblePosition, slotPosition) > Mathf.Max(0f, releaseRadius);

        public static bool ShouldShowInspectionPlatform(bool movementCompleted,
            bool slotStillOwned, bool returning)
            => movementCompleted && slotStillOwned && !returning;

        public static int FindFirstFreeSlot(IReadOnlyList<int> occupiedSlots, int capacity = 3)
        {
            for (int slot = 0; slot < capacity; slot++)
            {
                bool occupied = false;
                if (occupiedSlots != null)
                    for (int i = 0; i < occupiedSlots.Count; i++)
                        if (occupiedSlots[i] == slot) { occupied = true; break; }
                if (!occupied) return slot;
            }
            return -1;
        }

        public static string OldestInspectionId(IReadOnlyList<string> oldestFirst)
            => oldestFirst != null && oldestFirst.Count > 0 ? oldestFirst[0] : string.Empty;

        private Vector3 GetInspectionSlotPosition(int slot, Vector3 anchor)
            => CalculateInspectionSlotPosition(anchor, workspaceForward, slot,
                inspectionDistance, inspectionSlotSpacing, inspectionArcDepth);

        private Vector3 ResolveInspectionDestination(InspectionZone zone, int slot,
            string movingIdeaId)
        {
            Vector3 anchor = new Vector3(zone.horizontalAnchor.x, zone.inspectionHeight,
                zone.horizontalAnchor.y);
            Vector3 candidate = GetInspectionSlotPosition(slot, anchor);
            var occupied = new List<Vector3>();
            var radii = new List<float>();
            foreach (KeyValuePair<string, BubbleInspection> pair in inspections)
                if (pair.Key != movingIdeaId && visible.TryGetValue(pair.Key, out IdeaBubbleView bubble)
                    && bubble != null)
                {
                    occupied.Add(bubble.transform.position);
                    radii.Add(bubble.WorldRadius);
                }
            float candidateRadius = visible.TryGetValue(movingIdeaId, out IdeaBubbleView moving)
                && moving != null ? moving.WorldRadius : bubbleDiameter * .5f;
            return FindScaleAwareDestination(candidate, workspaceForward, candidateRadius,
                occupied, radii, inspectionClearanceMargin);
        }

        public static Vector3 FindScaleAwareDestination(Vector3 candidate, Vector3 forward,
            float candidateRadius, IReadOnlyList<Vector3> occupied,
            IReadOnlyList<float> occupiedRadii, float margin, int maximumAttempts = 8)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < .001f) flatForward = Vector3.forward;
            Vector3 result = candidate;
            for (int attempt = 0; attempt <= maximumAttempts; attempt++)
            {
                float requiredAdvance = 0f;
                if (occupied != null)
                    for (int i = 0; i < occupied.Count; i++)
                    {
                        float otherRadius = occupiedRadii != null && i < occupiedRadii.Count
                            ? Mathf.Max(0f, occupiedRadii[i]) : 0f;
                        float required = Mathf.Max(0f, candidateRadius) + otherRadius + Mathf.Max(0f, margin);
                        float actual = Vector3.Distance(result, occupied[i]);
                        if (actual < required) requiredAdvance = Mathf.Max(requiredAdvance,
                            required - actual + Mathf.Max(.01f, margin));
                    }
                if (requiredAdvance <= 0f) return result;
                result += flatForward * requiredAdvance;
            }
            return result;
        }

        public static Vector3 FindNonOverlappingDestination(Vector3 candidate, Vector3 forward,
            IReadOnlyList<Vector3> occupied, float minimumSeparation, int maximumAttempts = 4)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            Vector3 result = candidate;
            for (int attempt = 0; attempt <= maximumAttempts; attempt++)
            {
                bool overlaps = false;
                if (occupied != null)
                    for (int i = 0; i < occupied.Count; i++)
                        if (Vector3.Distance(result, occupied[i]) < minimumSeparation)
                        { overlaps = true; break; }
                if (!overlaps) return result;
                result += flatForward * minimumSeparation;
            }
            return result;
        }

        public static Vector3 CalculateInspectionSlotPosition(Vector3 anchor, Vector3 forward,
            int slot, float distance, float spacing, float arcDepth)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, flatForward).normalized;
            float horizontal = slot == 1 ? -spacing : slot == 2 ? spacing : 0f;
            float depth = slot == 0 ? 0f : arcDepth;
            return anchor + flatForward * (distance + depth) + right * horizontal - Vector3.up * 0.08f;
        }

        private void ResetGestureRecognition()
        {
            bool inspected = !string.IsNullOrEmpty(selectedIdeaId) && inspections.ContainsKey(selectedIdeaId);
            leftBeckon.Reset(inspected);
            rightBeckon.Reset(inspected);
        }

        private int CountCurled(BeckonHand hand)
        {
            int count=0;
            if (ChordRatio(hand.index) <= curledChordRatio) count++;
            if (ChordRatio(hand.middle) <= curledChordRatio) count++;
            if (ChordRatio(hand.ring) <= curledChordRatio) count++;
            if (ChordRatio(hand.pinky) <= curledChordRatio) count++;
            return count;
        }

        private int CountExtended(BeckonHand hand)
        {
            int count=0;
            if (ChordRatio(hand.index) >= extendedChordRatio) count++;
            if (ChordRatio(hand.middle) >= extendedChordRatio) count++;
            if (ChordRatio(hand.ring) >= extendedChordRatio) count++;
            if (ChordRatio(hand.pinky) >= extendedChordRatio) count++;
            return count;
        }

        private static float[] GetCurlRatios(BeckonHand hand)
            => new[] { ChordRatio(hand.index), ChordRatio(hand.middle),
                ChordRatio(hand.ring), ChordRatio(hand.pinky) };

        private static string FormatCurlRatios(IReadOnlyList<float> values)
            => values == null || values.Count < 4 ? "unavailable"
                : values[0].ToString("0.00") + "," + values[1].ToString("0.00") + ","
                    + values[2].ToString("0.00") + "," + values[3].ToString("0.00");

        private static float ChordRatio(FingerChain finger)
        {
            float path=Vector3.Distance(finger.proximal.position,finger.intermediate.position)
                +Vector3.Distance(finger.intermediate.position,finger.distal.position)
                +Vector3.Distance(finger.distal.position,finger.tip.position);
            return path < .001f ? 1f : Vector3.Distance(finger.proximal.position,finger.tip.position)/path;
        }

        private static bool ResolveBeckonJoints(BeckonHand hand)
        {
            if (hand.Resolved) return true;
            if (hand.skeleton.Bones == null) return false;
            bool xr=hand.skeleton.GetSkeletonType()==OVRSkeleton.SkeletonType.XRHandLeft
                || hand.skeleton.GetSkeletonType()==OVRSkeleton.SkeletonType.XRHandRight;
            foreach(OVRBone bone in hand.skeleton.Bones)
            {
                OVRSkeleton.BoneId id=bone.Id; Transform t=bone.Transform;
                if(xr)
                {
                    if(id==OVRSkeleton.BoneId.XRHand_Wrist)hand.wrist=t;
                    else if(id==OVRSkeleton.BoneId.XRHand_Palm)hand.palm=t;
                    else AssignFinger(hand,id,t,true);
                }
                else
                {
                    if(id==OVRSkeleton.BoneId.Hand_WristRoot){hand.wrist=t;hand.palm=t;}
                    else AssignFinger(hand,id,t,false);
                }
            }
            return hand.Resolved;
        }

        private static void AssignFinger(BeckonHand hand, OVRSkeleton.BoneId id, Transform t, bool xr)
        {
            if(xr)
            {
                SetFinger(hand.index,id,t,OVRSkeleton.BoneId.XRHand_IndexProximal,OVRSkeleton.BoneId.XRHand_IndexIntermediate,OVRSkeleton.BoneId.XRHand_IndexDistal,OVRSkeleton.BoneId.XRHand_IndexTip);
                SetFinger(hand.middle,id,t,OVRSkeleton.BoneId.XRHand_MiddleProximal,OVRSkeleton.BoneId.XRHand_MiddleIntermediate,OVRSkeleton.BoneId.XRHand_MiddleDistal,OVRSkeleton.BoneId.XRHand_MiddleTip);
                SetFinger(hand.ring,id,t,OVRSkeleton.BoneId.XRHand_RingProximal,OVRSkeleton.BoneId.XRHand_RingIntermediate,OVRSkeleton.BoneId.XRHand_RingDistal,OVRSkeleton.BoneId.XRHand_RingTip);
                SetFinger(hand.pinky,id,t,OVRSkeleton.BoneId.XRHand_LittleProximal,OVRSkeleton.BoneId.XRHand_LittleIntermediate,OVRSkeleton.BoneId.XRHand_LittleDistal,OVRSkeleton.BoneId.XRHand_LittleTip);
            }
            else
            {
                SetFinger(hand.index,id,t,OVRSkeleton.BoneId.Hand_Index1,OVRSkeleton.BoneId.Hand_Index2,OVRSkeleton.BoneId.Hand_Index3,OVRSkeleton.BoneId.Hand_IndexTip);
                SetFinger(hand.middle,id,t,OVRSkeleton.BoneId.Hand_Middle1,OVRSkeleton.BoneId.Hand_Middle2,OVRSkeleton.BoneId.Hand_Middle3,OVRSkeleton.BoneId.Hand_MiddleTip);
                SetFinger(hand.ring,id,t,OVRSkeleton.BoneId.Hand_Ring1,OVRSkeleton.BoneId.Hand_Ring2,OVRSkeleton.BoneId.Hand_Ring3,OVRSkeleton.BoneId.Hand_RingTip);
                SetFinger(hand.pinky,id,t,OVRSkeleton.BoneId.Hand_Pinky1,OVRSkeleton.BoneId.Hand_Pinky2,OVRSkeleton.BoneId.Hand_Pinky3,OVRSkeleton.BoneId.Hand_PinkyTip);
            }
        }

        private static void SetFinger(FingerChain finger, OVRSkeleton.BoneId id, Transform t,
            OVRSkeleton.BoneId proximal, OVRSkeleton.BoneId intermediate,
            OVRSkeleton.BoneId distal, OVRSkeleton.BoneId tip)
        {
            if(id==proximal)finger.proximal=t; else if(id==intermediate)finger.intermediate=t;
            else if(id==distal)finger.distal=t; else if(id==tip)finger.tip=t;
        }

        private void ClearSelection()
        {
            if (!string.IsNullOrEmpty(selectedIdeaId)
                && visible.TryGetValue(selectedIdeaId, out IdeaBubbleView selectedBubble) && selectedBubble != null)
                selectedBubble.SetSelected(false);
            selectedIdeaId = string.Empty;
            activeSelectionHand = BubbleInteractionHand.Unknown;
        }

        private void HandlePanelFocused(IdeaDetailsPanel panel)
        {
            if (panel != null) SetActivePanel(panel.IdeaId);
        }

        private void SetActivePanel(string ideaId)
        {
            if (string.IsNullOrEmpty(ideaId) || !openPanels.ContainsKey(ideaId)
                || !visible.TryGetValue(ideaId, out IdeaBubbleView next) || next == null) return;
            if (!string.IsNullOrEmpty(selectedIdeaId) && selectedIdeaId != ideaId
                && visible.TryGetValue(selectedIdeaId, out IdeaBubbleView previous) && previous != null)
                previous.SetSelected(false);
            selectedIdeaId = ideaId;
            activeSelectionHand = selectionOwners.TryGetValue(ideaId, out BubbleInteractionHand owner)
                ? owner : BubbleInteractionHand.Unknown;
            next.SetOpen(true);
            next.SetSelected(true);
            panelFocusOrder.Remove(ideaId);
            panelFocusOrder.Add(ideaId);
            ResetGestureRecognition();
            RefreshDiagnostics();
        }

        private void ActivateMostRecentlyFocusedPanel()
        {
            selectedIdeaId = string.Empty;
            activeSelectionHand = BubbleInteractionHand.Unknown;
            while (panelFocusOrder.Count > 0)
            {
                string candidate = panelFocusOrder[panelFocusOrder.Count - 1];
                if (openPanels.ContainsKey(candidate) && visible.ContainsKey(candidate))
                {
                    SetActivePanel(candidate);
                    return;
                }
                panelFocusOrder.RemoveAt(panelFocusOrder.Count - 1);
            }
        }

        private void BuildDiagnostics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticRoot != null) return;
            GameObject root = new GameObject("Idea Bubble Diagnostics");
            root.transform.SetParent(transform, false);
            diagnosticRoot = root.transform;
            diagnosticText = root.AddComponent<TextMeshPro>();
            diagnosticText.fontSize = 1.4f;
            diagnosticText.alignment = TextAlignmentOptions.TopLeft;
            diagnosticText.color = Color.white;
            diagnosticText.rectTransform.sizeDelta = new Vector2(0.55f, 0.28f);
            root.SetActive(showDevelopmentDiagnostics);
#endif
        }

        public void SetDevelopmentDiagnosticsVisible(bool visibleValue)
        {
            showDevelopmentDiagnostics = visibleValue;
            if (diagnosticRoot != null) diagnosticRoot.gameObject.SetActive(visibleValue);
            RefreshDiagnostics();
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticRoot == null || !diagnosticRoot.gameObject.activeSelf) return;
            if (headset == null && Camera.main != null) headset = Camera.main.transform;
            if (headset == null) return;
            diagnosticRoot.position = headset.TransformPoint(new Vector3(-0.38f, 0.12f, 0.72f));
            diagnosticRoot.rotation = Quaternion.LookRotation(diagnosticRoot.position - headset.position, Vector3.up);
            RefreshDiagnostics();
#endif
        }

        private void RefreshDiagnostics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticText == null) return;
            BrainstormSession session = Store?.ActiveSession;
            diagnosticText.text = "IDEA BUBBLES"
                + "\nSession: " + (string.IsNullOrEmpty(activeSessionId) ? "None" : activeSessionId)
                + "\nSaved: " + (session != null ? session.Ideas.Count : 0)
                + "   Visible: " + visible.Count
                + "\nSelected: " + (string.IsNullOrEmpty(selectedIdeaId) ? "None" : selectedIdeaId)
                + "   Open panels: " + openPanels.Count
                + "\nInteraction: " + (interactionAvailable ? "available" : "blocked")
                + "   Inspected: " + inspections.Count + " in " + inspectionZones.Count + " zones"
                + "   Platforms: " + ActivePlatformCount
                + "\nMovements: " + movements.Count
                + "   Direct: " + directMode + (string.IsNullOrEmpty(directIdeaId) ? string.Empty : " · " + directIdeaId)
                + "\nHead pose ready: " + (stableHeadPoseFrames >= 2 ? "yes" : "no")
                + "\nGesture: " + lastGestureDiagnostic
                + "\nPanel: " + lastPanelMovementEvent
                + "\nLast created: " + LastCreatedIdeaId
                + "\nLast restored: " + LastRestoredIdeaId
                + "\nSave: " + LastSaveResult
                + "\nLoad: " + LastLoadResult;
#endif
        }

        private void OnDestroy()
        {
            ClearInspectionWorkspace();
            ClearVisibleBubbles();
            if (bubbleRoot != null) Destroy(bubbleRoot.gameObject);
            if (panelRoot != null) Destroy(panelRoot.gameObject);
        }
    }
}

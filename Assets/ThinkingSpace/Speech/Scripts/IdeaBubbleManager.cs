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
        [SerializeField] private bool showDevelopmentDiagnostics;
        [Header("Beckon and return")]
        [SerializeField, Range(0.7f, 1f)] private float inspectionDistance = 0.82f;
        [SerializeField, Range(0.04f, 0.18f)] private float deliberateHandTravel = 0.08f;
        [SerializeField, Range(0.1f, 0.5f)] private float neutralHoldDuration = 0.2f;
        [SerializeField, Range(0.5f, 2f)] private float gestureWindow = 1.25f;
        [SerializeField, Range(0.2f, 1.2f)] private float gestureCooldown = 0.65f;
        [SerializeField, Range(0.15f, 1f)] private float movementDuration = 0.45f;
        [SerializeField, Range(0.45f, 0.8f)] private float curledChordRatio = 0.64f;
        [SerializeField, Range(0.7f, 0.98f)] private float extendedChordRatio = 0.82f;
        private readonly Dictionary<string, IdeaBubbleView> visible = new Dictionary<string, IdeaBubbleView>();
        private readonly Dictionary<string, IdeaDetailsPanel> openPanels = new Dictionary<string, IdeaDetailsPanel>();
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
        private BubbleInspection inspection;
        private BubbleMovement movement;
        private float gestureCooldownRemaining;
        private int operationEpoch;

        public string LastCreatedIdeaId { get; private set; } = "None";
        public string LastRestoredIdeaId { get; private set; } = "None";
        public string LastSaveResult { get; private set; } = "Not attempted";
        public string LastLoadResult { get; private set; } = "Not attempted";
        public int VisibleBubbleCount => visible.Count;
        public string SelectedIdeaId => selectedIdeaId;

        private sealed class FingerChain
        {
            public Transform proximal, intermediate, distal, tip;
            public bool Resolved => proximal != null && intermediate != null && distal != null && tip != null;
        }

        private sealed class BeckonHand
        {
            public OVRHand hand;
            public OVRSkeleton skeleton;
            public Transform wrist;
            public readonly FingerChain index = new FingerChain();
            public readonly FingerChain middle = new FingerChain();
            public readonly FingerChain ring = new FingerChain();
            public readonly FingerChain pinky = new FingerChain();
            public bool armed;
            public float neutralTimer, gestureTimer;
            public Vector3 startWrist;
            public bool Resolved => wrist != null && index.Resolved && middle.Resolved && ring.Resolved && pinky.Resolved;
            public void Reset() { armed=false; neutralTimer=gestureTimer=0f; }
        }

        private sealed class BubbleInspection
        {
            public string ideaId, sessionId;
            public int epoch;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        private sealed class BubbleMovement
        {
            public string ideaId, sessionId;
            public int epoch;
            public float elapsed;
            public Vector3 fromPosition, toPosition, fromScale, toScale;
            public Quaternion fromRotation, toRotation;
            public bool returning;
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
                CancelInspectionMovement();
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
            CancelInspectionMovement();
            activeSessionId = sessionId;
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
            bubble.Initialize(idea, bubbleDiameter, SelectAndShowDetails);
            bubble.SetInteractionAvailable(interactionAvailable);
            visible.Add(idea.IdeaId, bubble);
            return bubble;
        }

        private void SelectAndShowDetails(BrainstormIdeaRecord idea)
        {
            if (!interactionAvailable || idea == null || string.IsNullOrEmpty(idea.IdeaId)
                || !visible.TryGetValue(idea.IdeaId, out IdeaBubbleView next) || next == null) return;
            if (!ShowDetails(idea)) return;
            SetActivePanel(idea.IdeaId);
            RefreshDiagnostics();
        }

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
            panel.Configure(idea, visible.TryGetValue(idea.IdeaId, out IdeaBubbleView bubble) ? bubble.transform : null,
                headset, slot, ClosePanel, HandlePanelFocused, DeleteFromPanel,
                playIdeaSummary, stopIdeaSummary, RequestReturn);
            if (bubble != null) bubble.SetOpen(true);
            var occupied = new List<Vector3>();
            foreach (IdeaDetailsPanel other in openPanels.Values)
                if (other != null && other != panel) occupied.Add(other.transform.position);
            panel.NudgeToAvoid(occupied);
            return true;
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
            string ideaId = panel.IdeaId;
            if (inspection != null && inspection.ideaId == ideaId) RestoreInspectionImmediately();
            openPanels.Remove(ideaId);
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
            string ideaId = panel.IdeaId;
            if (!Store.TryDeleteIdea(activeSessionId, ideaId))
                return string.IsNullOrEmpty(Store.LastPersistenceError)
                    ? "The idea could not be deleted." : Store.LastPersistenceError;
            if (inspection != null && inspection.ideaId == ideaId) CancelInspectionMovement();
            if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
            {
                bubble.SetOpen(false);
                Destroy(bubble.gameObject);
            }
            visible.Remove(ideaId);
            openPanels.Remove(ideaId);
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
            foreach (string ideaId in openPanels.Keys)
                if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null)
                    bubble.SetOpen(false);
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null) Destroy(panel.gameObject);
            openPanels.Clear();
            panelFocusOrder.Clear();
            selectedIdeaId = string.Empty;
        }

        private BrainstormSessionStore Store => storeProvider != null ? storeProvider() : null;

        private void Update()
        {
            if (menuController == null || workflowController == null || transcriptionCoordinator == null)
                ResolveInteractionOwners();
            bool available = !string.IsNullOrEmpty(activeSessionId)
                && (menuController == null || (!menuController.IsMenuVisible
                    && !menuController.IsPalmMenuButtonVisible))
                && (workflowController == null || !workflowController.IsBlockingRecording)
                && (transcriptionCoordinator == null || transcriptionCoordinator.State == RecordingWorkflowState.Ready);
            if (available != interactionAvailable)
            {
                interactionAvailable = available;
                foreach (IdeaBubbleView bubble in visible.Values)
                    if (bubble != null) bubble.SetInteractionAvailable(available);
                if (!available) ResetGestureRecognition();
                RefreshDiagnostics();
            }
            UpdateMovement();
            gestureCooldownRemaining = Mathf.Max(0f, gestureCooldownRemaining - Time.unscaledDeltaTime);
            if (interactionAvailable && movement == null && gestureCooldownRemaining <= 0f)
                UpdateBeckonGestures();
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

        private void UpdateBeckonGestures()
        {
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null && panel.IsInteracting) { ResetGestureRecognition(); return; }
            if (string.IsNullOrEmpty(selectedIdeaId) || !visible.ContainsKey(selectedIdeaId))
            { ResetGestureRecognition(); return; }
            bool returning = inspection != null;
            if (EvaluateHandGesture(leftBeckon, returning) || EvaluateHandGesture(rightBeckon, returning))
            {
                if (returning) RequestReturn(selectedIdeaId);
                else BeginInspection(selectedIdeaId);
                gestureCooldownRemaining = gestureCooldown;
                ResetGestureRecognition();
            }
        }

        private bool EvaluateHandGesture(BeckonHand hand, bool returnGesture)
        {
            if (hand.hand == null || hand.skeleton == null || !hand.hand.IsTracked
                || hand.hand.HandConfidence != OVRHand.TrackingConfidence.High
                || hand.hand.GetFingerIsPinching(OVRHand.HandFinger.Index)
                || !ResolveBeckonJoints(hand))
            { hand.Reset(); return false; }

            int curled = CountCurled(hand);
            int extended = CountExtended(hand);
            bool neutral = extended >= 3;
            if (!hand.armed)
            {
                hand.neutralTimer = neutral ? hand.neutralTimer + Time.unscaledDeltaTime : 0f;
                if (hand.neutralTimer >= neutralHoldDuration)
                {
                    hand.armed = true;
                    hand.gestureTimer = 0f;
                    hand.startWrist = hand.wrist.position;
                }
                return false;
            }

            hand.gestureTimer += Time.unscaledDeltaTime;
            if (hand.gestureTimer > gestureWindow) { hand.Reset(); return false; }
            if (headset == null) return false;
            Vector3 towardHead = (headset.position - hand.startWrist).normalized;
            float signedTravel = Vector3.Dot(hand.wrist.position - hand.startWrist, towardHead);
            int poseCount = returnGesture ? extended : curled;
            return IsDeliberateGesture(poseCount, signedTravel, deliberateHandTravel, returnGesture);
        }

        public static bool IsDeliberateGesture(int qualifyingFingerCount, float signedTravel,
            float minimumTravel, bool returnGesture)
            => qualifyingFingerCount >= 3 && (returnGesture
                ? signedTravel <= -minimumTravel : signedTravel >= minimumTravel);

        private void BeginInspection(string ideaId)
        {
            if (inspection != null || movement != null || string.IsNullOrEmpty(ideaId)
                || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null
                || !openPanels.ContainsKey(ideaId) || headset == null) return;
            inspection = new BubbleInspection
            {
                ideaId=ideaId, sessionId=activeSessionId, epoch=++operationEpoch,
                position=bubble.transform.position, rotation=bubble.transform.rotation,
                scale=bubble.transform.localScale
            };
            Vector3 horizontalForward = Vector3.ProjectOnPlane(headset.forward, Vector3.up).normalized;
            if (horizontalForward.sqrMagnitude < .001f) horizontalForward = headset.forward.normalized;
            Vector3 destination = headset.position + horizontalForward * inspectionDistance - Vector3.up * .08f;
            StartMovement(bubble, destination, bubble.transform.rotation, bubble.transform.localScale, false);
        }

        private void RequestReturn(string ideaId)
        {
            if (inspection == null || movement != null || ideaId != inspection.ideaId
                || inspection.sessionId != activeSessionId || inspection.epoch != operationEpoch
                || !visible.TryGetValue(ideaId, out IdeaBubbleView bubble) || bubble == null) return;
            if (openPanels.TryGetValue(ideaId, out IdeaDetailsPanel panel) && panel != null)
                panel.SetReturnAvailable(false);
            StartMovement(bubble, inspection.position, inspection.rotation, inspection.scale, true);
        }

        private void StartMovement(IdeaBubbleView bubble, Vector3 position, Quaternion rotation,
            Vector3 scale, bool returning)
        {
            movement = new BubbleMovement
            {
                ideaId=bubble.IdeaId, sessionId=activeSessionId, epoch=operationEpoch,
                fromPosition=bubble.transform.position, toPosition=position,
                fromRotation=bubble.transform.rotation, toRotation=rotation,
                fromScale=bubble.transform.localScale, toScale=scale, returning=returning
            };
            bubble.SetMoving(true);
        }

        private void UpdateMovement()
        {
            if (movement == null) return;
            if (movement.sessionId != activeSessionId || movement.epoch != operationEpoch
                || !visible.TryGetValue(movement.ideaId, out IdeaBubbleView bubble) || bubble == null)
            { CancelInspectionMovement(); return; }
            movement.elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(movement.elapsed / movementDuration));
            bubble.transform.position = Vector3.LerpUnclamped(movement.fromPosition, movement.toPosition, t);
            bubble.transform.rotation = Quaternion.SlerpUnclamped(movement.fromRotation, movement.toRotation, t);
            bubble.transform.localScale = Vector3.LerpUnclamped(movement.fromScale, movement.toScale, t);
            if (t < 1f) return;
            bool returned = movement.returning;
            bubble.SetMoving(false);
            movement = null;
            if (returned)
            {
                if (openPanels.TryGetValue(inspection.ideaId, out IdeaDetailsPanel panel) && panel != null)
                    panel.SetReturnAvailable(false);
                inspection = null;
                operationEpoch++;
            }
            else if (inspection != null && openPanels.TryGetValue(inspection.ideaId,
                out IdeaDetailsPanel inspectionPanel) && inspectionPanel != null)
                inspectionPanel.SetReturnAvailable(true);
        }

        private void CancelInspectionMovement()
        {
            operationEpoch++;
            if (movement != null && visible.TryGetValue(movement.ideaId, out IdeaBubbleView moving) && moving != null)
                moving.SetMoving(false);
            if (inspection != null && openPanels.TryGetValue(inspection.ideaId, out IdeaDetailsPanel panel) && panel != null)
                panel.SetReturnAvailable(false);
            movement = null;
            inspection = null;
            ResetGestureRecognition();
        }

        private void RestoreInspectionImmediately()
        {
            if (inspection != null && visible.TryGetValue(inspection.ideaId, out IdeaBubbleView bubble)
                && bubble != null)
            {
                bubble.transform.SetPositionAndRotation(inspection.position, inspection.rotation);
                bubble.transform.localScale=inspection.scale;
                bubble.SetMoving(false);
            }
            CancelInspectionMovement();
        }

        private void ResetGestureRecognition() { leftBeckon.Reset(); rightBeckon.Reset(); }

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
                    else AssignFinger(hand,id,t,true);
                }
                else
                {
                    if(id==OVRSkeleton.BoneId.Hand_WristRoot)hand.wrist=t;
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
        }

        private void HandlePanelFocused(IdeaDetailsPanel panel)
        {
            if (panel != null) SetActivePanel(panel.IdeaId);
        }

        private void SetActivePanel(string ideaId)
        {
            if (string.IsNullOrEmpty(ideaId) || !openPanels.ContainsKey(ideaId)
                || !visible.TryGetValue(ideaId, out IdeaBubbleView next) || next == null) return;
            if (inspection != null && inspection.ideaId != ideaId) RestoreInspectionImmediately();
            if (!string.IsNullOrEmpty(selectedIdeaId) && selectedIdeaId != ideaId
                && visible.TryGetValue(selectedIdeaId, out IdeaBubbleView previous) && previous != null)
                previous.SetSelected(false);
            selectedIdeaId = ideaId;
            next.SetOpen(true);
            next.SetSelected(true);
            panelFocusOrder.Remove(ideaId);
            panelFocusOrder.Add(ideaId);
            RefreshDiagnostics();
        }

        private void ActivateMostRecentlyFocusedPanel()
        {
            selectedIdeaId = string.Empty;
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
                + "   Inspection: " + (inspection != null ? inspection.ideaId : "None")
                + "\nMovement: " + (movement == null ? "idle" : movement.returning ? "returning" : "pulling")
                + "\nLast created: " + LastCreatedIdeaId
                + "\nLast restored: " + LastRestoredIdeaId
                + "\nSave: " + LastSaveResult
                + "\nLoad: " + LastLoadResult;
#endif
        }

        private void OnDestroy()
        {
            ClearVisibleBubbles();
            if (bubbleRoot != null) Destroy(bubbleRoot.gameObject);
            if (panelRoot != null) Destroy(panelRoot.gameObject);
        }
    }
}

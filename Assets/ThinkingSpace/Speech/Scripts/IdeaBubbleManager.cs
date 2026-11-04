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
        private readonly Dictionary<string, IdeaBubbleView> visible = new Dictionary<string, IdeaBubbleView>();
        private readonly Dictionary<string, IdeaDetailsPanel> openPanels = new Dictionary<string, IdeaDetailsPanel>();
        private Func<BrainstormSessionStore> storeProvider;
        private Transform headset, bubbleRoot, panelRoot, diagnosticRoot;
        private Action<string> ideaDeleted;
        private TMP_Text diagnosticText;
        private string activeSessionId = string.Empty;

        public string LastCreatedIdeaId { get; private set; } = "None";
        public string LastRestoredIdeaId { get; private set; } = "None";
        public string LastSaveResult { get; private set; } = "Not attempted";
        public string LastLoadResult { get; private set; } = "Not attempted";
        public int VisibleBubbleCount => visible.Count;

        public void Configure(Func<BrainstormSessionStore> provider, Transform headsetTransform,
            Action<string> deletedCallback = null)
        {
            storeProvider = provider;
            headset = headsetTransform;
            ideaDeleted = deletedCallback;
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
        }

        public void SetSessionActive(bool active, string sessionId)
        {
            if (!active || string.IsNullOrEmpty(sessionId))
            {
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
            bubble.Initialize(idea, bubbleDiameter, ShowDetails);
            visible.Add(idea.IdeaId, bubble);
            return bubble;
        }

        private void ShowDetails(BrainstormIdeaRecord idea)
        {
            if (idea == null || string.IsNullOrEmpty(idea.IdeaId)) return;
            if (openPanels.TryGetValue(idea.IdeaId, out IdeaDetailsPanel existing) && existing != null)
            { existing.FocusPanel(); return; }
            if (openPanels.Count >= 4)
            { LastLoadResult = "Four idea panels are already open"; RefreshDiagnostics(); return; }
            int slot = FirstAvailablePanelSlot();
            GameObject root = new GameObject("Idea Details · " + idea.IdeaId, typeof(RectTransform));
            root.transform.SetParent(panelRoot, true);
            IdeaDetailsPanel panel = root.AddComponent<IdeaDetailsPanel>();
            openPanels.Add(idea.IdeaId, panel);
            panel.Configure(idea, visible.TryGetValue(idea.IdeaId, out IdeaBubbleView bubble) ? bubble.transform : null,
                headset, slot, ClosePanel, DeleteFromPanel);
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
            openPanels.Remove(panel.IdeaId);
            Destroy(panel.gameObject);
        }

        private string DeleteFromPanel(IdeaDetailsPanel panel)
        {
            if (panel == null || Store?.ActiveSession == null || Store.ActiveSession.SessionId != activeSessionId)
                return "The active session changed; the idea was not deleted.";
            string ideaId = panel.IdeaId;
            if (!Store.TryDeleteIdea(activeSessionId, ideaId))
                return string.IsNullOrEmpty(Store.LastPersistenceError)
                    ? "The idea could not be deleted." : Store.LastPersistenceError;
            if (visible.TryGetValue(ideaId, out IdeaBubbleView bubble) && bubble != null) Destroy(bubble.gameObject);
            visible.Remove(ideaId);
            openPanels.Remove(ideaId);
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
            ClearPanels();
            foreach (IdeaBubbleView bubble in visible.Values)
                if (bubble != null) Destroy(bubble.gameObject);
            visible.Clear();
        }

        private void ClearPanels()
        {
            foreach (IdeaDetailsPanel panel in openPanels.Values)
                if (panel != null) Destroy(panel.gameObject);
            openPanels.Clear();
        }

        private BrainstormSessionStore Store => storeProvider != null ? storeProvider() : null;

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

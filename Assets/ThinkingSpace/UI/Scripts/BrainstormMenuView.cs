using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.Speech;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class BrainstormMenuView : MonoBehaviour
    {
        [Header("Screens")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject sessionPanel;
        [SerializeField] private GameObject voiceHelpPanel;
        [SerializeField] private GameObject gestureHelpPanel;

        [Header("Main")]
        [SerializeField] private GameObject noSessionsLabel;
        [SerializeField] private RectTransform sessionList;
        [SerializeField] private Button sessionButtonTemplate;
        [SerializeField] private Button startSessionButton;
        [SerializeField] private Button mainVoiceButton;
        [SerializeField] private Button mainGesturesButton;
        [SerializeField] private Button mainLeftHandButton;
        [SerializeField] private Button mainRightHandButton;
        [SerializeField] private Button mainCloseButton;

        [Header("Session")]
        [SerializeField] private TMP_Text sessionTitle;
        [SerializeField] private TMP_Text sessionStatus;
        [SerializeField] private Button challengeButton;
        [SerializeField] private Button expandButton;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button whiteboardButton;
        [SerializeField] private Button sessionVoiceButton;
        [SerializeField] private Button sessionGesturesButton;
        [SerializeField] private Button sessionLeftHandButton;
        [SerializeField] private Button sessionRightHandButton;
        [SerializeField] private Button endSessionButton;
        [SerializeField] private Button sessionCloseButton;

        [Header("Help")]
        [SerializeField] private Button voiceBackButton;
        [SerializeField] private Button gesturesBackButton;

        [Header("Diagnostics")]
        [SerializeField] private GameObject frontMarker;

        private readonly List<Button> runtimeSessionButtons = new List<Button>();
        private Action<int> reopenSession;
        private Button mainPreviousButton, mainNextButton, sessionPreviousButton, sessionNextButton;
        private TMP_Text mainPageIndicator, sessionPageIndicator;
        private int mainPage, mainPageCount, sessionPage;
        private const int SessionsPerPage = 3;
        private const int SessionPageCount = 3;

        public Canvas Canvas { get; private set; }
        public RectTransform CanvasRect => Canvas != null ? Canvas.GetComponent<RectTransform>() : null;
        public GameObject FrontMarker => frontMarker;

        private void Awake()
        {
            Canvas = GetComponent<Canvas>();
            DisableScrolling(mainPanel);
            DisableScrolling(sessionPanel);
            CreatePageNavigation(mainPanel, mainCloseButton, out mainPreviousButton,
                out mainNextButton, out mainPageIndicator, ChangeMainPage);
            CreatePageNavigation(sessionPanel, sessionCloseButton, out sessionPreviousButton,
                out sessionNextButton, out sessionPageIndicator, ChangeSessionPage);
        }

        public void Bind(
            Action startSession, Action voiceHelp, Action gestureHelp,
            Action selectLeft, Action selectRight, Action close,
            Action challenge, Action expand, Action connect, Action whiteboard,
            Action endSession, Action backFromHelp)
        {
            BindButton(startSessionButton, startSession);
            BindButton(mainVoiceButton, voiceHelp);
            BindButton(mainGesturesButton, gestureHelp);
            BindButton(mainLeftHandButton, selectLeft);
            BindButton(mainRightHandButton, selectRight);
            BindButton(mainCloseButton, close);
            BindButton(challengeButton, challenge);
            BindButton(expandButton, expand);
            BindButton(connectButton, connect);
            BindButton(whiteboardButton, whiteboard);
            BindButton(sessionVoiceButton, voiceHelp);
            BindButton(sessionGesturesButton, gestureHelp);
            BindButton(sessionLeftHandButton, selectLeft);
            BindButton(sessionRightHandButton, selectRight);
            BindButton(endSessionButton, endSession);
            BindButton(sessionCloseButton, close);
            BindButton(voiceBackButton, backFromHelp);
            BindButton(gesturesBackButton, backFromHelp);
        }

        public void ShowMain(
            IReadOnlyList<BrainstormSession> sessions,
            ThumbRecordingGestureController.RecordingHand recordingHand,
            Action<int> reopen)
        {
            ShowOnly(mainPanel);
            reopenSession = reopen;
            ClearSessionButtons();
            noSessionsLabel.SetActive(sessions.Count == 0);
            for (int i = 0; i < sessions.Count; i++)
            {
                BrainstormSession session = sessions[i];
                Button button = Instantiate(sessionButtonTemplate, sessionList);
                button.gameObject.SetActive(true);
                button.name = "Session " + session.Number;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Session " + session.Number;
                int number = session.Number;
                BindButton(button, () => reopenSession?.Invoke(number));
                runtimeSessionButtons.Add(button);
            }
            SetHandSelection(mainLeftHandButton, mainRightHandButton, recordingHand);
            mainPage = 0;
            mainPageCount = Mathf.Max(1, Mathf.CeilToInt(sessions.Count / (float)SessionsPerPage)) + 1;
            RefreshMainPage(sessions.Count);
        }

        public void ShowSession(int sessionNumber, ThumbRecordingGestureController.RecordingHand recordingHand)
        {
            ShowOnly(sessionPanel);
            sessionTitle.text = "Session " + sessionNumber;
            sessionStatus.text = string.Empty;
            SetHandSelection(sessionLeftHandButton, sessionRightHandButton, recordingHand);
            sessionPage = 0;
            RefreshSessionPage();
        }

        public void ShowVoiceHelp() => ShowOnly(voiceHelpPanel);
        public void ShowGestureHelp() => ShowOnly(gestureHelpPanel);
        public void SetSessionStatus(string message) => sessionStatus.text = message;

        public void SetFrontMarkerVisible(bool visible)
        {
            if (frontMarker != null) frontMarker.SetActive(visible);
        }

        private void ShowOnly(GameObject panel)
        {
            mainPanel.SetActive(panel == mainPanel);
            sessionPanel.SetActive(panel == sessionPanel);
            voiceHelpPanel.SetActive(panel == voiceHelpPanel);
            gestureHelpPanel.SetActive(panel == gestureHelpPanel);
        }

        private void ClearSessionButtons()
        {
            for (int i = 0; i < runtimeSessionButtons.Count; i++)
                if (runtimeSessionButtons[i] != null) Destroy(runtimeSessionButtons[i].gameObject);
            runtimeSessionButtons.Clear();
        }

        private void ChangeMainPage(int delta)
        {
            mainPage = Mathf.Clamp(mainPage + delta, 0, mainPageCount - 1);
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        private void RefreshMainPage(int sessionCount)
        {
            int sessionPageCount = Mathf.Max(1, Mathf.CeilToInt(sessionCount / (float)SessionsPerPage));
            bool settingsPage = mainPage >= sessionPageCount;
            if (sessionList != null) sessionList.gameObject.SetActive(!settingsPage);
            if (startSessionButton != null) startSessionButton.gameObject.SetActive(!settingsPage);
            if (noSessionsLabel != null) noSessionsLabel.SetActive(!settingsPage && sessionCount == 0 && mainPage == 0);
            for (int i = 0; i < runtimeSessionButtons.Count; i++)
                runtimeSessionButtons[i].gameObject.SetActive(!settingsPage
                    && i >= mainPage * SessionsPerPage && i < (mainPage + 1) * SessionsPerPage);
            SetActive(mainVoiceButton, settingsPage);
            SetActive(mainGesturesButton, settingsPage);
            SetActive(mainLeftHandButton, settingsPage);
            SetActive(mainRightHandButton, settingsPage);
            SetActive(mainCloseButton, settingsPage);
            SetLabelVisible(mainPanel, "Brainstorming Sessions", !settingsPage);
            SetLabelVisible(mainPanel, "Recording Hand", settingsPage);
            UpdateNavigation(mainPreviousButton, mainNextButton, mainPageIndicator, mainPage, mainPageCount);
        }

        private void ChangeSessionPage(int delta)
        {
            sessionPage = Mathf.Clamp(sessionPage + delta, 0, SessionPageCount - 1);
            RefreshSessionPage();
        }

        private void RefreshSessionPage()
        {
            bool ideas = sessionPage == 0;
            bool helpAndHand = sessionPage == 1;
            bool finish = sessionPage == 2;
            SetActive(challengeButton, ideas);
            SetActive(expandButton, ideas);
            SetActive(connectButton, ideas);
            SetActive(whiteboardButton, ideas);
            SetActive(sessionVoiceButton, helpAndHand);
            SetActive(sessionGesturesButton, helpAndHand);
            SetActive(sessionLeftHandButton, helpAndHand);
            SetActive(sessionRightHandButton, helpAndHand);
            SetLabelVisible(sessionPanel, "Recording Hand", helpAndHand);
            SetActive(endSessionButton, finish);
            SetActive(sessionCloseButton, finish);
            UpdateNavigation(sessionPreviousButton, sessionNextButton, sessionPageIndicator,
                sessionPage, SessionPageCount);
        }

        private static void SetActive(Button button, bool active)
        {
            if (button != null) button.gameObject.SetActive(active);
        }

        private static void SetLabelVisible(GameObject panel, string exactText, bool visible)
        {
            if (panel == null) return;
            TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null && labels[i].text.Trim() == exactText)
                    labels[i].gameObject.SetActive(visible);
        }

        private static void DisableScrolling(GameObject panel)
        {
            if (panel == null) return;
            ScrollRect[] scrollRects = panel.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrollRects.Length; i++)
            {
                scrollRects[i].StopMovement();
                scrollRects[i].verticalNormalizedPosition = 1f;
                scrollRects[i].enabled = false;
            }
        }

        private static void CreatePageNavigation(GameObject panel, Button styleSource,
            out Button previous, out Button next, out TMP_Text indicator, Action<int> changed)
        {
            previous = next = null;
            indicator = null;
            if (panel == null || styleSource == null) return;
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            if (panelRect == null) return;
            previous = Instantiate(styleSource, panelRect);
            next = Instantiate(styleSource, panelRect);
            previous.name = "Previous Page Button";
            next.name = "Next Page Button";
            ConfigureNavigationButton(previous, "Previous", new Vector2(0.05f, 0.02f), new Vector2(0.3f, 0.11f));
            ConfigureNavigationButton(next, "Next", new Vector2(0.7f, 0.02f), new Vector2(0.95f, 0.11f));
            BindButton(previous, () => changed?.Invoke(-1));
            BindButton(next, () => changed?.Invoke(1));

            TMP_Text sourceLabel = styleSource.GetComponentInChildren<TMP_Text>(true);
            if (sourceLabel != null)
            {
                indicator = Instantiate(sourceLabel, panelRect);
                indicator.name = "Page Indicator";
                indicator.text = "Page 1 / 1";
                indicator.alignment = TextAlignmentOptions.Center;
                indicator.raycastTarget = false;
                RectTransform rect = indicator.rectTransform;
                rect.anchorMin = new Vector2(0.32f, 0.02f);
                rect.anchorMax = new Vector2(0.68f, 0.11f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                LayoutElement layout = indicator.GetComponent<LayoutElement>();
                if (layout != null) layout.ignoreLayout = true;
            }
        }

        private static void ConfigureNavigationButton(Button button, string label, Vector2 min, Vector2 max)
        {
            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label;
            button.gameObject.SetActive(true);
        }

        private static void UpdateNavigation(Button previous, Button next, TMP_Text indicator,
            int page, int pageCount)
        {
            if (previous != null) previous.interactable = page > 0;
            if (next != null) next.interactable = page < pageCount - 1;
            if (indicator != null) indicator.text = "Page " + (page + 1) + " / " + pageCount;
        }

        private static void BindButton(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private static void SetHandSelection(
            Button left, Button right, ThumbRecordingGestureController.RecordingHand selected)
        {
            SetButtonSelected(left, selected == ThumbRecordingGestureController.RecordingHand.Left);
            SetButtonSelected(right, selected == ThumbRecordingGestureController.RecordingHand.Right);
        }

        private static void SetButtonSelected(Button button, bool selected)
        {
            if (button == null) return;
            Image image = button.targetGraphic as Image;
            if (image != null)
                image.color = selected ? new Color(0.08f, 0.65f, 0.85f, 1f) : new Color(0.12f, 0.2f, 0.3f, 1f);
        }
    }
}

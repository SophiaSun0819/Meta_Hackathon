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
        private readonly List<Button> runtimeSessionDeleteButtons = new List<Button>();
        private readonly List<GameObject> runtimeSessionRows = new List<GameObject>();
        private Action<int> reopenSession;
        private Action<int> deleteSession;
        private Button mainPreviousButton, mainNextButton, sessionPreviousButton, sessionNextButton;
        private Button silenceMinusButton, silencePlusButton, conversationalButton, manualButton;
        private TMP_Text silenceValueLabel, settingsTitle;
        private Button sessionSilenceMinusButton, sessionSilencePlusButton, sessionConversationalButton, sessionManualButton;
        private Button subtitlesOnButton,subtitlesOffButton,editNameButton,replayTutorialButton;
        private Button sessionSubtitlesOnButton,sessionSubtitlesOffButton,sessionEditNameButton,sessionReplayTutorialButton;
        private TMP_Text sessionSilenceValueLabel, sessionSettingsTitle;
        private GameObject sessionDeleteConfirmation;
        private TMP_Text sessionDeleteQuestion;
        private Button sessionDeleteCancel, sessionDeleteConfirm;
        private TMP_Text mainPageIndicator, sessionPageIndicator;
        private int mainPage, mainPageCount, sessionPage;
        private const int SessionsPerPage = 3;
        private const int SessionPageCount = 4;

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
            CreateAudioSettingsPage();
            CreateSessionAudioSettingsPage();
            CreateSessionDeleteConfirmation();
        }

        public void Bind(
            Action startSession, Action voiceHelp, Action gestureHelp,
            Action selectLeft, Action selectRight, Action close,
            Action challenge, Action expand, Action connect, Action whiteboard,
            Action endSession, Action backFromHelp,
            Action decreaseSilence, Action increaseSilence, Action conversationalMode, Action manualMode,
            Action subtitlesOn,Action subtitlesOff,Action editName,Action replayTutorial)
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
            BindButton(silenceMinusButton, decreaseSilence);
            BindButton(silencePlusButton, increaseSilence);
            BindButton(conversationalButton, conversationalMode);
            BindButton(manualButton, manualMode);
            BindButton(sessionSilenceMinusButton, decreaseSilence);
            BindButton(sessionSilencePlusButton, increaseSilence);
            BindButton(sessionConversationalButton, conversationalMode);
            BindButton(sessionManualButton, manualMode);
            BindButton(subtitlesOnButton,subtitlesOn);BindButton(subtitlesOffButton,subtitlesOff);BindButton(editNameButton,editName);BindButton(replayTutorialButton,replayTutorial);
            BindButton(sessionSubtitlesOnButton,subtitlesOn);BindButton(sessionSubtitlesOffButton,subtitlesOff);BindButton(sessionEditNameButton,editName);BindButton(sessionReplayTutorialButton,replayTutorial);
        }

        public void SetAudioPreferences(float silenceSeconds,
            ThumbRecordingGestureController.RecordingMode mode,bool subtitlesEnabled=true)
        {
            if (silenceValueLabel != null) silenceValueLabel.text = silenceSeconds.ToString("0.0") + " seconds";
            if (sessionSilenceValueLabel != null) sessionSilenceValueLabel.text = silenceSeconds.ToString("0.0") + " seconds";
            SetButtonSelected(conversationalButton, mode == ThumbRecordingGestureController.RecordingMode.Conversational);
            SetButtonSelected(manualButton, mode == ThumbRecordingGestureController.RecordingMode.Manual);
            SetButtonSelected(sessionConversationalButton, mode == ThumbRecordingGestureController.RecordingMode.Conversational);
            SetButtonSelected(sessionManualButton, mode == ThumbRecordingGestureController.RecordingMode.Manual);
            SetButtonSelected(subtitlesOnButton,subtitlesEnabled);SetButtonSelected(subtitlesOffButton,!subtitlesEnabled);SetButtonSelected(sessionSubtitlesOnButton,subtitlesEnabled);SetButtonSelected(sessionSubtitlesOffButton,!subtitlesEnabled);
        }

        public void ShowMain(
            IReadOnlyList<BrainstormSession> sessions,
            ThumbRecordingGestureController.RecordingHand recordingHand,
            Action<int> reopen, Action<int> delete)
        {
            ShowOnly(mainPanel);
            reopenSession = reopen;
            deleteSession = delete;
            ClearSessionButtons();
            noSessionsLabel.SetActive(sessions.Count == 0);
            for (int i = 0; i < sessions.Count; i++)
            {
                BrainstormSession session = sessions[i];
                GameObject row = new GameObject("Session " + session.Number + " Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(sessionList, false);
                HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 8f; layout.childControlWidth = true; layout.childForceExpandWidth = true;
                LayoutElement rowLayout = row.GetComponent<LayoutElement>(); rowLayout.preferredHeight = 56f;
                Button button = Instantiate(sessionButtonTemplate, row.transform);
                button.gameObject.SetActive(true);
                button.name = "Session " + session.Number;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Session " + session.Number;
                int number = session.Number;
                BindButton(button, () => reopenSession?.Invoke(number));
                Button deleteButton = Instantiate(sessionButtonTemplate, row.transform);
                deleteButton.gameObject.SetActive(true); deleteButton.name = "Delete Session " + number;
                TMP_Text deleteLabel = deleteButton.GetComponentInChildren<TMP_Text>(true);
                if (deleteLabel != null) deleteLabel.text = "Delete";
                LayoutElement deleteLayout = deleteButton.GetComponent<LayoutElement>();
                if (deleteLayout != null) { deleteLayout.flexibleWidth = .45f; deleteLayout.preferredWidth = 130f; }
                BindButton(deleteButton, () => ShowDeleteConfirmation(number));
                runtimeSessionButtons.Add(button);
                runtimeSessionDeleteButtons.Add(deleteButton);
                runtimeSessionRows.Add(row);
            }
            SetHandSelection(mainLeftHandButton, mainRightHandButton, recordingHand);
            mainPage = 0;
            mainPageCount = Mathf.Max(1, Mathf.CeilToInt(sessions.Count / (float)SessionsPerPage)) + 2;
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
                if (runtimeSessionRows[i] != null) Destroy(runtimeSessionRows[i]);
            runtimeSessionButtons.Clear();
            runtimeSessionDeleteButtons.Clear();
            runtimeSessionRows.Clear();
        }

        private void ChangeMainPage(int delta)
        {
            mainPage = Mathf.Clamp(mainPage + delta, 0, mainPageCount - 1);
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        private void RefreshMainPage(int sessionCount)
        {
            int sessionPageCount = Mathf.Max(1, Mathf.CeilToInt(sessionCount / (float)SessionsPerPage));
            bool settingsPage = mainPage == sessionPageCount;
            bool audioPage = mainPage > sessionPageCount;
            bool sessionPage = !settingsPage && !audioPage;
            if (sessionList != null) sessionList.gameObject.SetActive(sessionPage);
            if (startSessionButton != null) startSessionButton.gameObject.SetActive(sessionPage);
            if (noSessionsLabel != null) noSessionsLabel.SetActive(sessionPage && sessionCount == 0 && mainPage == 0);
            for (int i = 0; i < runtimeSessionRows.Count; i++)
                runtimeSessionRows[i].SetActive(sessionPage
                    && i >= mainPage * SessionsPerPage && i < (mainPage + 1) * SessionsPerPage);
            SetActive(mainVoiceButton, settingsPage);
            SetActive(mainGesturesButton, settingsPage);
            SetActive(mainLeftHandButton, settingsPage);
            SetActive(mainRightHandButton, settingsPage);
            SetActive(mainCloseButton, settingsPage);
            SetActive(silenceMinusButton, audioPage); SetActive(silencePlusButton, audioPage);
            SetActive(conversationalButton, audioPage); SetActive(manualButton, audioPage);
            SetActive(subtitlesOnButton,audioPage);SetActive(subtitlesOffButton,audioPage);SetActive(editNameButton,audioPage);SetActive(replayTutorialButton,audioPage);
            if (silenceValueLabel != null) silenceValueLabel.gameObject.SetActive(audioPage);
            if (settingsTitle != null) settingsTitle.gameObject.SetActive(audioPage);
            SetLabelVisible(mainPanel, "Brainstorming Sessions", sessionPage);
            SetLabelVisible(mainPanel, "Recording Hand", settingsPage);
            UpdateNavigation(mainPreviousButton, mainNextButton, mainPageIndicator, mainPage, mainPageCount);
        }

        private void CreateAudioSettingsPage()
        {
            if (mainPanel == null || mainCloseButton == null) return;
            RectTransform parent = mainPanel.GetComponent<RectTransform>();
            silenceMinusButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(silenceMinusButton, "− 0.5s", new Vector2(.08f,.50f), new Vector2(.34f,.62f));
            silencePlusButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(silencePlusButton, "+ 0.5s", new Vector2(.66f,.50f), new Vector2(.92f,.62f));
            conversationalButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(conversationalButton, "Conversational", new Vector2(.08f,.25f), new Vector2(.47f,.38f));
            manualButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(manualButton, "Manual", new Vector2(.53f,.25f), new Vector2(.92f,.38f));
            subtitlesOnButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(subtitlesOnButton,"Subtitles On",new Vector2(.08f,.39f),new Vector2(.47f,.48f));
            subtitlesOffButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(subtitlesOffButton,"Subtitles Off",new Vector2(.53f,.39f),new Vector2(.92f,.48f));
            editNameButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(editNameButton,"Edit Name",new Vector2(.08f,.12f),new Vector2(.47f,.22f));
            replayTutorialButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(replayTutorialButton,"Replay Tutorial",new Vector2(.53f,.12f),new Vector2(.92f,.22f));
            TMP_Text source = mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if (source != null)
            {
                settingsTitle = Instantiate(source, parent); settingsTitle.text = "Recording Settings";
                settingsTitle.alignment = TextAlignmentOptions.Center; settingsTitle.raycastTarget = false;
                SetRect(settingsTitle.rectTransform, new Vector2(.08f,.76f), new Vector2(.92f,.91f));
                silenceValueLabel = Instantiate(source, parent); silenceValueLabel.text = "2.0 seconds";
                silenceValueLabel.alignment = TextAlignmentOptions.Center; silenceValueLabel.raycastTarget = false;
                SetRect(silenceValueLabel.rectTransform, new Vector2(.35f,.50f), new Vector2(.65f,.62f));
            }
        }

        private void CreateSessionAudioSettingsPage()
        {
            if (sessionPanel == null || sessionCloseButton == null) return;
            RectTransform parent=sessionPanel.GetComponent<RectTransform>();
            sessionSilenceMinusButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSilenceMinusButton,"− 0.5s",new Vector2(.08f,.50f),new Vector2(.34f,.62f));
            sessionSilencePlusButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSilencePlusButton,"+ 0.5s",new Vector2(.66f,.50f),new Vector2(.92f,.62f));
            sessionConversationalButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionConversationalButton,"Conversational",new Vector2(.08f,.25f),new Vector2(.47f,.38f));
            sessionManualButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionManualButton,"Manual",new Vector2(.53f,.25f),new Vector2(.92f,.38f));
            sessionSubtitlesOnButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSubtitlesOnButton,"Subtitles On",new Vector2(.08f,.39f),new Vector2(.47f,.48f));
            sessionSubtitlesOffButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSubtitlesOffButton,"Subtitles Off",new Vector2(.53f,.39f),new Vector2(.92f,.48f));
            sessionEditNameButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionEditNameButton,"Edit Name",new Vector2(.08f,.12f),new Vector2(.47f,.22f));
            sessionReplayTutorialButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionReplayTutorialButton,"Replay Tutorial",new Vector2(.53f,.12f),new Vector2(.92f,.22f));
            TMP_Text source=sessionCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(source!=null){sessionSettingsTitle=Instantiate(source,parent);sessionSettingsTitle.text="Recording Settings";sessionSettingsTitle.alignment=TextAlignmentOptions.Center;sessionSettingsTitle.raycastTarget=false;SetRect(sessionSettingsTitle.rectTransform,new Vector2(.08f,.76f),new Vector2(.92f,.91f));sessionSilenceValueLabel=Instantiate(source,parent);sessionSilenceValueLabel.text="2.0 seconds";sessionSilenceValueLabel.alignment=TextAlignmentOptions.Center;sessionSilenceValueLabel.raycastTarget=false;SetRect(sessionSilenceValueLabel.rectTransform,new Vector2(.35f,.50f),new Vector2(.65f,.62f));}
        }

        private void CreateSessionDeleteConfirmation()
        {
            if(mainPanel==null||mainCloseButton==null)return;
            RectTransform parent=mainPanel.GetComponent<RectTransform>();
            sessionDeleteConfirmation=new GameObject("Delete Session Confirmation",typeof(RectTransform),typeof(Image));
            sessionDeleteConfirmation.transform.SetParent(parent,false);SetRect(sessionDeleteConfirmation.GetComponent<RectTransform>(),new Vector2(.04f,.14f),new Vector2(.96f,.92f));
            sessionDeleteConfirmation.GetComponent<Image>().color=new Color(.035f,.055f,.09f,.99f);
            TMP_Text source=mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(source!=null){sessionDeleteQuestion=Instantiate(source,sessionDeleteConfirmation.transform);sessionDeleteQuestion.alignment=TextAlignmentOptions.Center;sessionDeleteQuestion.raycastTarget=false;SetRect(sessionDeleteQuestion.rectTransform,new Vector2(.08f,.48f),new Vector2(.92f,.82f));}
            sessionDeleteCancel=Instantiate(mainCloseButton,sessionDeleteConfirmation.transform);ConfigureNavigationButton(sessionDeleteCancel,"Cancel",new Vector2(.08f,.16f),new Vector2(.44f,.32f));
            sessionDeleteConfirm=Instantiate(mainCloseButton,sessionDeleteConfirmation.transform);ConfigureNavigationButton(sessionDeleteConfirm,"Delete Session",new Vector2(.56f,.16f),new Vector2(.92f,.32f));
            sessionDeleteConfirmation.SetActive(false);
        }

        private void ShowDeleteConfirmation(int number)
        {
            if(sessionDeleteConfirmation==null)return;
            sessionDeleteQuestion.text="Delete Session "+number+"?\nIts ideas and conversation history will be removed.";
            BindButton(sessionDeleteCancel,()=>sessionDeleteConfirmation.SetActive(false));
            BindButton(sessionDeleteConfirm,()=>{sessionDeleteConfirmation.SetActive(false);deleteSession?.Invoke(number);});
            sessionDeleteConfirmation.SetActive(true);
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; LayoutElement layout=rect.GetComponent<LayoutElement>(); if(layout!=null)layout.ignoreLayout=true; }

        private void ChangeSessionPage(int delta)
        {
            sessionPage = Mathf.Clamp(sessionPage + delta, 0, SessionPageCount - 1);
            RefreshSessionPage();
        }

        private void RefreshSessionPage()
        {
            bool ideas = sessionPage == 0;
            bool helpAndHand = sessionPage == 1;
            bool audioSettings = sessionPage == 2;
            bool finish = sessionPage == 3;
            SetActive(challengeButton, ideas);
            SetActive(expandButton, ideas);
            SetActive(connectButton, ideas);
            SetActive(whiteboardButton, ideas);
            SetActive(sessionVoiceButton, helpAndHand);
            SetActive(sessionGesturesButton, helpAndHand);
            SetActive(sessionLeftHandButton, helpAndHand);
            SetActive(sessionRightHandButton, helpAndHand);
            SetActive(sessionSilenceMinusButton,audioSettings);SetActive(sessionSilencePlusButton,audioSettings);
            SetActive(sessionConversationalButton,audioSettings);SetActive(sessionManualButton,audioSettings);
            SetActive(sessionSubtitlesOnButton,audioSettings);SetActive(sessionSubtitlesOffButton,audioSettings);SetActive(sessionEditNameButton,audioSettings);SetActive(sessionReplayTutorialButton,audioSettings);
            if(sessionSilenceValueLabel!=null)sessionSilenceValueLabel.gameObject.SetActive(audioSettings);
            if(sessionSettingsTitle!=null)sessionSettingsTitle.gameObject.SetActive(audioSettings);
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

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
        private Button mainWristLeftButton,mainWristRightButton,sessionWristLeftButton,sessionWristRightButton;
        private TMP_Text mainWristLabel,sessionWristLabel;
        private Button mainEnglishButton,mainSpanishButton,mainFrenchButton;
        private Button sessionEnglishButton,sessionSpanishButton,sessionFrenchButton;
        private TMP_Text mainLanguageLabel,sessionLanguageLabel;
        private readonly List<Button> mainConversationLanguageButtons=new List<Button>();
        private readonly List<Button> sessionConversationLanguageButtons=new List<Button>();
        private Button mainConversationAutoButton,sessionConversationAutoButton;
        private TMP_Text mainConversationLabel,sessionConversationLabel;
        private Button exportSessionButton,importSessionButton;
        private GameObject mainSessionActionRow;
        private TMP_Text mainTransferStatus;
        private TMP_Text sessionSilenceValueLabel, sessionSettingsTitle;
        private GameObject sessionDeleteConfirmation;
        private TMP_Text sessionDeleteQuestion;
        private Button sessionDeleteCancel, sessionDeleteConfirm;
        private GameObject languageConfirmation;
        private TMP_Text languageConfirmationQuestion;
        private Button languageConfirmationYes,languageConfirmationNo;
        private TMP_Text mainPageIndicator, sessionPageIndicator;
        private int mainPage, mainPageCount, sessionPage;
        private int currentSessionNumber;
        private float currentSilenceSeconds=2f;
        private const int SessionsPerPage = 3;
        private const int SessionPageCount = 5;
        private const int MainUtilityPageCount = 3;

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
            CreateWristSettings();
            CreateLanguageSettings();
            CreateConversationLanguageSettings(mainPanel,mainCloseButton,mainConversationLanguageButtons,
                out mainConversationAutoButton,out mainConversationLabel);
            CreateConversationLanguageSettings(sessionPanel,sessionCloseButton,sessionConversationLanguageButtons,
                out sessionConversationAutoButton,out sessionConversationLabel);
            CreateSessionTransferActions();
            CreateSessionDeleteConfirmation();
            CreateLanguageConfirmation();
            RefreshLocalization();
        }

        private void OnEnable()
        {
            ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=HandleLanguageChanged;
            ThinkingSpaceLocalizationController.InterfaceLanguageConfirmationRequested+=ShowLanguageConfirmation;
            RefreshLocalization();
        }

        private void OnDisable(){ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=HandleLanguageChanged;
            ThinkingSpaceLocalizationController.InterfaceLanguageConfirmationRequested-=ShowLanguageConfirmation;}
        private void Update(){if(languageConfirmation!=null&&languageConfirmation.activeSelf
            &&!(ThinkingSpaceLocalizationController.Instance?.HasPendingInterfaceLanguageChange??false))languageConfirmation.SetActive(false);}
        private void HandleLanguageChanged(string _)=>RefreshLocalization();

        public void RefreshLocalization()
        {
            ThinkingSpaceLocalizationController.LocalizeKnownText(gameObject);
            SetButtonLabel(mainPreviousButton,L("common.previous"));SetButtonLabel(mainNextButton,L("common.next"));
            SetButtonLabel(sessionPreviousButton,L("common.previous"));SetButtonLabel(sessionNextButton,L("common.next"));
            SetButtonLabel(conversationalButton,L("settings.conversational"));SetButtonLabel(manualButton,L("settings.manual"));
            SetButtonLabel(sessionConversationalButton,L("settings.conversational"));SetButtonLabel(sessionManualButton,L("settings.manual"));
            SetButtonLabel(silenceMinusButton,L("settings.decrease_silence"));SetButtonLabel(silencePlusButton,L("settings.increase_silence"));
            SetButtonLabel(sessionSilenceMinusButton,L("settings.decrease_silence"));SetButtonLabel(sessionSilencePlusButton,L("settings.increase_silence"));
            SetButtonLabel(subtitlesOnButton,L("settings.subtitles_on"));SetButtonLabel(subtitlesOffButton,L("settings.subtitles_off"));
            SetButtonLabel(sessionSubtitlesOnButton,L("settings.subtitles_on"));SetButtonLabel(sessionSubtitlesOffButton,L("settings.subtitles_off"));
            SetButtonLabel(editNameButton,L("settings.edit_name"));SetButtonLabel(replayTutorialButton,L("settings.replay_tutorial"));
            SetButtonLabel(sessionEditNameButton,L("settings.edit_name"));SetButtonLabel(sessionReplayTutorialButton,L("settings.replay_tutorial"));
            SetButtonLabel(mainWristLeftButton,L("settings.wrist.left"));SetButtonLabel(mainWristRightButton,L("settings.wrist.right"));
            SetButtonLabel(sessionWristLeftButton,L("settings.wrist.left"));SetButtonLabel(sessionWristRightButton,L("settings.wrist.right"));
            SetButtonLabel(importSessionButton,L("menu.session.import"));SetButtonLabel(exportSessionButton,L("menu.session.export"));
            SetButtonLabel(mainEnglishButton,L("language.english"));SetButtonLabel(mainSpanishButton,L("language.spanish"));SetButtonLabel(mainFrenchButton,L("language.french"));
            SetButtonLabel(sessionEnglishButton,L("language.english"));SetButtonLabel(sessionSpanishButton,L("language.spanish"));SetButtonLabel(sessionFrenchButton,L("language.french"));
            SetButtonLabel(sessionDeleteCancel,L("common.cancel"));SetButtonLabel(sessionDeleteConfirm,L("session.delete.confirm"));
            SetButtonLabel(languageConfirmationYes,L("common.yes"));SetButtonLabel(languageConfirmationNo,L("common.no"));
            if(settingsTitle!=null)settingsTitle.text=L("settings.recording.title");
            if(sessionSettingsTitle!=null)sessionSettingsTitle.text=L("settings.recording.title");
            if(mainWristLabel!=null)mainWristLabel.text=L("settings.wrist.title");
            if(sessionWristLabel!=null)sessionWristLabel.text=L("settings.wrist.title");
            if(mainLanguageLabel!=null)mainLanguageLabel.text=L("settings.interface_language");
            if(sessionLanguageLabel!=null)sessionLanguageLabel.text=L("settings.interface_language");
            if(mainConversationLabel!=null)mainConversationLabel.text=L("settings.conversation_mode");
            if(sessionConversationLabel!=null)sessionConversationLabel.text=L("settings.conversation_mode");
            SetButtonLabel(mainConversationAutoButton,L("settings.language.auto"));
            SetButtonLabel(sessionConversationAutoButton,L("settings.language.auto"));
            RefreshConversationButtonLabels(mainConversationLanguageButtons);
            RefreshConversationButtonLabels(sessionConversationLanguageButtons);
            string language=ThinkingSpaceLocalizationController.Instance?.InterfaceLanguageCode??"en";
            SetButtonSelected(mainEnglishButton,language=="en");SetButtonSelected(mainSpanishButton,language=="es");SetButtonSelected(mainFrenchButton,language=="fr");
            SetButtonSelected(sessionEnglishButton,language=="en");SetButtonSelected(sessionSpanishButton,language=="es");SetButtonSelected(sessionFrenchButton,language=="fr");
            if(currentSessionNumber>0&&sessionTitle!=null)sessionTitle.text=L("menu.session.format",currentSessionNumber);
            if(silenceValueLabel!=null)silenceValueLabel.text=L("settings.seconds_format",currentSilenceSeconds.ToString("0.0"));
            if(sessionSilenceValueLabel!=null)sessionSilenceValueLabel.text=L("settings.seconds_format",currentSilenceSeconds.ToString("0.0"));
            for(int i=0;i<runtimeSessionButtons.Count;i++)SetButtonLabel(runtimeSessionButtons[i],L("menu.session.format",ExtractSessionNumber(runtimeSessionButtons[i].name)));
            for(int i=0;i<runtimeSessionDeleteButtons.Count;i++)SetButtonLabel(runtimeSessionDeleteButtons[i],L("common.delete"));
            UpdateNavigation(mainPreviousButton,mainNextButton,mainPageIndicator,mainPage,Mathf.Max(1,mainPageCount));
            UpdateNavigation(sessionPreviousButton,sessionNextButton,sessionPageIndicator,sessionPage,SessionPageCount);
        }

        private static int ExtractSessionNumber(string value)
        {int result=0;if(!string.IsNullOrEmpty(value)){string[] parts=value.Split(' ');int.TryParse(parts[parts.Length-1],out result);}return result;}
        private static string L(string key,params object[] arguments)=>ThinkingSpaceLocalizationController.Get(key,arguments);

        public void Bind(
            Action startSession, Action voiceHelp, Action gestureHelp,
            Action selectLeft, Action selectRight, Action close,
            Action challenge, Action expand, Action connect, Action whiteboard,
            Action endSession, Action backFromHelp,
            Action decreaseSilence, Action increaseSilence, Action conversationalMode, Action manualMode,
            Action subtitlesOn,Action subtitlesOff,Action editName,Action replayTutorial,
            Action wristLeft,Action wristRight,Action exportSession,Action importSession)
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
            BindButton(mainWristLeftButton,wristLeft);BindButton(mainWristRightButton,wristRight);
            BindButton(sessionWristLeftButton,wristLeft);BindButton(sessionWristRightButton,wristRight);
            BindButton(exportSessionButton,exportSession);BindButton(importSessionButton,importSession);
        }

        public void SetWristMenuHand(WristMenuHand hand)
        {
            SetButtonSelected(mainWristLeftButton,hand==WristMenuHand.Left);
            SetButtonSelected(mainWristRightButton,hand==WristMenuHand.Right);
            SetButtonSelected(sessionWristLeftButton,hand==WristMenuHand.Left);
            SetButtonSelected(sessionWristRightButton,hand==WristMenuHand.Right);
        }

        public void SetAudioPreferences(float silenceSeconds,
            ThumbRecordingGestureController.RecordingMode mode,bool subtitlesEnabled=true)
        {
            currentSilenceSeconds=silenceSeconds;
            if (silenceValueLabel != null) silenceValueLabel.text = L("settings.seconds_format",silenceSeconds.ToString("0.0"));
            if (sessionSilenceValueLabel != null) sessionSilenceValueLabel.text = L("settings.seconds_format",silenceSeconds.ToString("0.0"));
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
                if (label != null) label.text = L("menu.session.format",session.Number);
                int number = session.Number;
                BindButton(button, () => reopenSession?.Invoke(number));
                Button deleteButton = Instantiate(sessionButtonTemplate, row.transform);
                deleteButton.gameObject.SetActive(true); deleteButton.name = "Delete Session " + number;
                TMP_Text deleteLabel = deleteButton.GetComponentInChildren<TMP_Text>(true);
                if (deleteLabel != null) deleteLabel.text = L("common.delete");
                LayoutElement deleteLayout = deleteButton.GetComponent<LayoutElement>();
                if (deleteLayout != null) { deleteLayout.flexibleWidth = .45f; deleteLayout.preferredWidth = 130f; }
                BindButton(deleteButton, () => ShowDeleteConfirmation(number));
                runtimeSessionButtons.Add(button);
                runtimeSessionDeleteButtons.Add(deleteButton);
                runtimeSessionRows.Add(row);
            }
            SetHandSelection(mainLeftHandButton, mainRightHandButton, recordingHand);
            mainPage = 0;
            mainPageCount = CalculateMainPageCount(sessions.Count);
            RefreshMainPage(sessions.Count);
        }

        public void ShowSession(int sessionNumber, ThumbRecordingGestureController.RecordingHand recordingHand)
        {
            ShowOnly(sessionPanel);
            currentSessionNumber=sessionNumber;
            sessionTitle.text = L("menu.session.format",sessionNumber);
            sessionStatus.text = string.Empty;
            SetHandSelection(sessionLeftHandButton, sessionRightHandButton, recordingHand);
            sessionPage = 0;
            RefreshSessionPage();
        }

        public void ShowVoiceHelp() => ShowOnly(voiceHelpPanel);
        public void ShowGestureHelp() => ShowOnly(gestureHelpPanel);
        public void SetSessionStatus(string message) => sessionStatus.text = message;
        public void SetTransferStatus(string message)
        {
            string safeMessage=message??string.Empty;
            if(mainTransferStatus!=null)mainTransferStatus.text=safeMessage;
            if(sessionStatus!=null)sessionStatus.text=safeMessage;
        }

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
            bool audioPage = mainPage == sessionPageCount+1;
            bool languagePage = mainPage == sessionPageCount+2;
            bool sessionPage = !settingsPage && !audioPage && !languagePage;
            bool firstSessionPage=sessionPage&&IsImportOnMainPage(mainPage);
            if (sessionList != null) sessionList.gameObject.SetActive(sessionPage);
            if(mainSessionActionRow!=null)mainSessionActionRow.SetActive(firstSessionPage);
            if(mainTransferStatus!=null)mainTransferStatus.gameObject.SetActive(firstSessionPage);
            if (noSessionsLabel != null) noSessionsLabel.SetActive(sessionPage && sessionCount == 0 && mainPage == 0);
            for (int i = 0; i < runtimeSessionRows.Count; i++)
                runtimeSessionRows[i].SetActive(sessionPage
                    && i >= mainPage * SessionsPerPage && i < (mainPage + 1) * SessionsPerPage);
            SetActive(mainVoiceButton, settingsPage);
            SetActive(mainGesturesButton, settingsPage);
            SetActive(mainLeftHandButton, settingsPage);
            SetActive(mainRightHandButton, settingsPage);
            SetActive(mainWristLeftButton,settingsPage);SetActive(mainWristRightButton,settingsPage);
            if(mainWristLabel!=null)mainWristLabel.gameObject.SetActive(settingsPage);
            SetActive(mainCloseButton, settingsPage);
            SetActive(silenceMinusButton, audioPage); SetActive(silencePlusButton, audioPage);
            SetActive(conversationalButton, audioPage); SetActive(manualButton, audioPage);
            SetActive(subtitlesOnButton,audioPage);SetActive(subtitlesOffButton,audioPage);SetActive(editNameButton,audioPage);SetActive(replayTutorialButton,audioPage);
            if (silenceValueLabel != null) silenceValueLabel.gameObject.SetActive(audioPage);
            if (settingsTitle != null) settingsTitle.gameObject.SetActive(audioPage);
            SetLanguageSettingsVisible(mainEnglishButton,mainSpanishButton,mainFrenchButton,mainLanguageLabel,languagePage);
            SetConversationSettingsVisible(mainConversationLanguageButtons,mainConversationAutoButton,
                mainConversationLabel,languagePage);
            SetLabelVisible(mainPanel, "menu.sessions.title", sessionPage);
            SetLabelVisible(mainPanel, "settings.recording_hand", settingsPage);
            UpdateNavigation(mainPreviousButton, mainNextButton, mainPageIndicator, mainPage, mainPageCount);
        }

        private void CreateAudioSettingsPage()
        {
            if (mainPanel == null || mainCloseButton == null) return;
            RectTransform parent = mainPanel.GetComponent<RectTransform>();
            silenceMinusButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(silenceMinusButton, L("settings.decrease_silence"), new Vector2(.08f,.50f), new Vector2(.34f,.62f));
            silencePlusButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(silencePlusButton, L("settings.increase_silence"), new Vector2(.66f,.50f), new Vector2(.92f,.62f));
            conversationalButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(conversationalButton, L("settings.conversational"), new Vector2(.08f,.25f), new Vector2(.47f,.38f));
            manualButton = Instantiate(mainCloseButton, parent); ConfigureNavigationButton(manualButton, L("settings.manual"), new Vector2(.53f,.25f), new Vector2(.92f,.38f));
            subtitlesOnButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(subtitlesOnButton,L("settings.subtitles_on"),new Vector2(.08f,.39f),new Vector2(.47f,.48f));
            subtitlesOffButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(subtitlesOffButton,L("settings.subtitles_off"),new Vector2(.53f,.39f),new Vector2(.92f,.48f));
            editNameButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(editNameButton,L("settings.edit_name"),new Vector2(.08f,.12f),new Vector2(.47f,.22f));
            replayTutorialButton=Instantiate(mainCloseButton,parent);ConfigureNavigationButton(replayTutorialButton,L("settings.replay_tutorial"),new Vector2(.53f,.12f),new Vector2(.92f,.22f));
            TMP_Text source = mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if (source != null)
            {
                settingsTitle = Instantiate(source, parent); settingsTitle.text = L("settings.recording.title");
                settingsTitle.alignment = TextAlignmentOptions.Center; settingsTitle.raycastTarget = false;
                SetRect(settingsTitle.rectTransform, new Vector2(.08f,.76f), new Vector2(.92f,.91f));
                silenceValueLabel = Instantiate(source, parent); silenceValueLabel.text = L("settings.seconds_format","2.0");
                silenceValueLabel.alignment = TextAlignmentOptions.Center; silenceValueLabel.raycastTarget = false;
                SetRect(silenceValueLabel.rectTransform, new Vector2(.35f,.50f), new Vector2(.65f,.62f));
            }
        }

        private void CreateSessionAudioSettingsPage()
        {
            if (sessionPanel == null || sessionCloseButton == null) return;
            RectTransform parent=sessionPanel.GetComponent<RectTransform>();
            sessionSilenceMinusButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSilenceMinusButton,L("settings.decrease_silence"),new Vector2(.08f,.50f),new Vector2(.34f,.62f));
            sessionSilencePlusButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSilencePlusButton,L("settings.increase_silence"),new Vector2(.66f,.50f),new Vector2(.92f,.62f));
            sessionConversationalButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionConversationalButton,L("settings.conversational"),new Vector2(.08f,.25f),new Vector2(.47f,.38f));
            sessionManualButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionManualButton,L("settings.manual"),new Vector2(.53f,.25f),new Vector2(.92f,.38f));
            sessionSubtitlesOnButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSubtitlesOnButton,L("settings.subtitles_on"),new Vector2(.08f,.39f),new Vector2(.47f,.48f));
            sessionSubtitlesOffButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionSubtitlesOffButton,L("settings.subtitles_off"),new Vector2(.53f,.39f),new Vector2(.92f,.48f));
            sessionEditNameButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionEditNameButton,L("settings.edit_name"),new Vector2(.08f,.12f),new Vector2(.47f,.22f));
            sessionReplayTutorialButton=Instantiate(sessionCloseButton,parent);ConfigureNavigationButton(sessionReplayTutorialButton,L("settings.replay_tutorial"),new Vector2(.53f,.12f),new Vector2(.92f,.22f));
            TMP_Text source=sessionCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(source!=null){sessionSettingsTitle=Instantiate(source,parent);sessionSettingsTitle.text=L("settings.recording.title");sessionSettingsTitle.alignment=TextAlignmentOptions.Center;sessionSettingsTitle.raycastTarget=false;SetRect(sessionSettingsTitle.rectTransform,new Vector2(.08f,.76f),new Vector2(.92f,.91f));sessionSilenceValueLabel=Instantiate(source,parent);sessionSilenceValueLabel.text=L("settings.seconds_format",2f);sessionSilenceValueLabel.alignment=TextAlignmentOptions.Center;sessionSilenceValueLabel.raycastTarget=false;SetRect(sessionSilenceValueLabel.rectTransform,new Vector2(.35f,.50f),new Vector2(.65f,.62f));}
        }

        private void CreateWristSettings()
        {
            CreateWristSettings(mainPanel,mainCloseButton,out mainWristLeftButton,
                out mainWristRightButton,out mainWristLabel);
            CreateWristSettings(sessionPanel,sessionCloseButton,out sessionWristLeftButton,
                out sessionWristRightButton,out sessionWristLabel);
        }

        private void CreateLanguageSettings()
        {
            CreateLanguageSettings(mainPanel,mainCloseButton,out mainEnglishButton,
                out mainSpanishButton,out mainFrenchButton,out mainLanguageLabel);
            CreateLanguageSettings(sessionPanel,sessionCloseButton,out sessionEnglishButton,
                out sessionSpanishButton,out sessionFrenchButton,out sessionLanguageLabel);
        }

        private static void CreateLanguageSettings(GameObject panel,Button source,out Button english,
            out Button spanish,out Button french,out TMP_Text label)
        {
            english=spanish=french=null;label=null;if(panel==null||source==null)return;
            RectTransform parent=panel.GetComponent<RectTransform>();
            english=Instantiate(source,parent);ConfigureNavigationButton(english,L("language.english"),new Vector2(.08f,.59f),new Vector2(.34f,.70f));
            spanish=Instantiate(source,parent);ConfigureNavigationButton(spanish,L("language.spanish"),new Vector2(.37f,.59f),new Vector2(.63f,.70f));
            french=Instantiate(source,parent);ConfigureNavigationButton(french,L("language.french"),new Vector2(.66f,.59f),new Vector2(.92f,.70f));
            BindButton(english,()=>ThinkingSpaceLocalizationController.Instance?.SetInterfaceLanguage("en"));
            BindButton(spanish,()=>ThinkingSpaceLocalizationController.Instance?.SetInterfaceLanguage("es"));
            BindButton(french,()=>ThinkingSpaceLocalizationController.Instance?.SetInterfaceLanguage("fr"));
            TMP_Text sourceLabel=source.GetComponentInChildren<TMP_Text>(true);
            if(sourceLabel!=null){label=Instantiate(sourceLabel,parent);label.text=L("settings.interface_language");
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                SetRect(label.rectTransform,new Vector2(.08f,.76f),new Vector2(.92f,.87f));}
        }

        private static void CreateConversationLanguageSettings(GameObject panel,Button source,
            List<Button> languages,out Button automatic,out TMP_Text label)
        {
            automatic=null;label=null;if(panel==null||source==null)return;
            RectTransform parent=panel.GetComponent<RectTransform>();
            automatic=Instantiate(source,parent);ConfigureNavigationButton(automatic,L("settings.language.auto"),
                new Vector2(.08f,.36f),new Vector2(.31f,.47f));
            BindButton(automatic,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetConversationLanguage("auto",null,"orby-auto"));
            string[] codes={"en","es","fr","zh-CN","hi-IN"};
            BrainstormMenuController controller=FindFirstObjectByType<BrainstormMenuController>();
            for(int i=0;i<codes.Length;i++)
            {
                string code=codes[i];float width=.84f/codes.Length;float x=.08f+i*width;
                Button button=Instantiate(source,parent);button.name="Conversation Language "+code;
                ConfigureNavigationButton(button,ConversationLanguageLabel(code),new Vector2(x,.17f),new Vector2(x+width-.012f,.30f));
                button.interactable=controller!=null&&controller.MultilingualBackendContractEnabled;
                BindButton(button,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetConversationLanguage(
                    "explicit",code,VoiceProfileForLanguage(code)));
                languages.Add(button);
            }
            TMP_Text sourceLabel=source.GetComponentInChildren<TMP_Text>(true);
            if(sourceLabel!=null){label=Instantiate(sourceLabel,parent);label.text=L("settings.conversation_mode");
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                SetRect(label.rectTransform,new Vector2(.34f,.35f),new Vector2(.92f,.49f));}
        }

        private static string VoiceProfileForLanguage(string code)=>code=="es"?"orby-es":code=="fr"?"orby-fr":
            code=="zh-CN"?"orby-zh":code=="hi-IN"?"orby-hi":"orby-en";
        private static string ConversationLanguageLabel(string code)=>code=="es"?L("language.spanish"):
            code=="fr"?L("language.french"):code=="zh-CN"?L("language.mandarin"):
            code=="hi-IN"?L("language.hindi"):L("language.english");
        private static void RefreshConversationButtonLabels(List<Button> buttons)
        {for(int i=0;i<buttons.Count;i++){string[] codes={"en","es","fr","zh-CN","hi-IN"};if(i<codes.Length)SetButtonLabel(buttons[i],ConversationLanguageLabel(codes[i]));}}
        private static void SetConversationSettingsVisible(List<Button> buttons,Button automatic,TMP_Text label,bool visible)
        {SetActive(automatic,visible);for(int i=0;i<buttons.Count;i++)SetActive(buttons[i],visible);if(label!=null)label.gameObject.SetActive(visible);}

        private static void SetLanguageSettingsVisible(Button english,Button spanish,Button french,
            TMP_Text label,bool visible)
        {
            SetActive(english,visible);SetActive(spanish,visible);SetActive(french,visible);
            if(label!=null)label.gameObject.SetActive(visible);
        }

        private void CreateSessionTransferActions()
        {
            if(mainPanel==null||sessionPanel==null||startSessionButton==null
                ||challengeButton==null||sessionCloseButton==null||sessionList==null)return;

            int mainSibling=startSessionButton.transform.GetSiblingIndex();
            mainSessionActionRow=CreateHorizontalActionRow("Session Creation and Import",sessionList,mainSibling);
            startSessionButton.transform.SetParent(mainSessionActionRow.transform,false);
            importSessionButton=Instantiate(startSessionButton,mainSessionActionRow.transform);
            importSessionButton.name="Import Session";
            SetButtonLabel(importSessionButton,L("menu.session.import"));

            TMP_Text statusSource=noSessionsLabel!=null?noSessionsLabel.GetComponent<TMP_Text>():null;
            if(statusSource==null)statusSource=mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(statusSource!=null)
            {
                mainTransferStatus=Instantiate(statusSource,sessionList);
                mainTransferStatus.name="Session Import Status";
                mainTransferStatus.text=string.Empty;
                mainTransferStatus.fontSize=Mathf.Min(mainTransferStatus.fontSize,20f);
                mainTransferStatus.alignment=TextAlignmentOptions.Center;
                mainTransferStatus.raycastTarget=false;
                LayoutElement statusLayout=mainTransferStatus.GetComponent<LayoutElement>();
                if(statusLayout==null)statusLayout=mainTransferStatus.gameObject.AddComponent<LayoutElement>();
                statusLayout.preferredHeight=42f;
            }

            Transform sessionContent=challengeButton.transform.parent;
            int sessionSibling=challengeButton.transform.GetSiblingIndex();
            GameObject sessionActionRow=CreateHorizontalActionRow("Challenge and Export",sessionContent,sessionSibling);
            challengeButton.transform.SetParent(sessionActionRow.transform,false);
            exportSessionButton=Instantiate(challengeButton,sessionActionRow.transform);
            exportSessionButton.name="Export Session";
            SetButtonLabel(exportSessionButton,L("menu.session.export"));
        }

        private static GameObject CreateHorizontalActionRow(string name,Transform parent,int siblingIndex)
        {
            GameObject row=new GameObject(name,typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(LayoutElement));
            row.transform.SetParent(parent,false);row.transform.SetSiblingIndex(siblingIndex);
            HorizontalLayoutGroup layout=row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing=8f;layout.childControlWidth=true;layout.childControlHeight=true;
            layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            LayoutElement element=row.GetComponent<LayoutElement>();element.preferredHeight=56f;
            return row;
        }

        private static void SetButtonLabel(Button button,string text)
        {
            TMP_Text label=button!=null?button.GetComponentInChildren<TMP_Text>(true):null;
            if(label!=null)label.text=text;
        }

        private static void CreateWristSettings(GameObject panel,Button source,out Button left,
            out Button right,out TMP_Text label)
        {
            left=right=null;label=null;if(panel==null||source==null)return;
            RectTransform parent=panel.GetComponent<RectTransform>();
            left=Instantiate(source,parent);ConfigureNavigationButton(left,L("settings.wrist.left"),
                new Vector2(.08f,.12f),new Vector2(.47f,.22f));
            right=Instantiate(source,parent);ConfigureNavigationButton(right,L("settings.wrist.right"),
                new Vector2(.53f,.12f),new Vector2(.92f,.22f));
            TMP_Text sourceLabel=source.GetComponentInChildren<TMP_Text>(true);
            if(sourceLabel!=null){label=Instantiate(sourceLabel,parent);label.text=L("settings.wrist.title");
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                SetRect(label.rectTransform,new Vector2(.08f,.24f),new Vector2(.92f,.34f));}
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
            sessionDeleteCancel=Instantiate(mainCloseButton,sessionDeleteConfirmation.transform);ConfigureNavigationButton(sessionDeleteCancel,L("common.cancel"),new Vector2(.08f,.16f),new Vector2(.44f,.32f));
            sessionDeleteConfirm=Instantiate(mainCloseButton,sessionDeleteConfirmation.transform);ConfigureNavigationButton(sessionDeleteConfirm,L("session.delete.confirm"),new Vector2(.56f,.16f),new Vector2(.92f,.32f));
            sessionDeleteConfirmation.SetActive(false);
        }

        private void CreateLanguageConfirmation()
        {
            if(CanvasRect==null||mainCloseButton==null)return;
            languageConfirmation=new GameObject("Interface Language Confirmation",typeof(RectTransform),typeof(Image));
            languageConfirmation.transform.SetParent(CanvasRect,false);
            SetRect(languageConfirmation.GetComponent<RectTransform>(),new Vector2(.08f,.25f),new Vector2(.92f,.78f));
            languageConfirmation.GetComponent<Image>().color=new Color(.035f,.055f,.09f,.99f);
            TMP_Text source=mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(source!=null){languageConfirmationQuestion=Instantiate(source,languageConfirmation.transform);
                languageConfirmationQuestion.alignment=TextAlignmentOptions.Center;languageConfirmationQuestion.raycastTarget=false;
                SetRect(languageConfirmationQuestion.rectTransform,new Vector2(.08f,.48f),new Vector2(.92f,.86f));}
            languageConfirmationNo=Instantiate(mainCloseButton,languageConfirmation.transform);
            ConfigureNavigationButton(languageConfirmationNo,L("common.no"),new Vector2(.08f,.14f),new Vector2(.44f,.34f));
            languageConfirmationYes=Instantiate(mainCloseButton,languageConfirmation.transform);
            ConfigureNavigationButton(languageConfirmationYes,L("common.yes"),new Vector2(.56f,.14f),new Vector2(.92f,.34f));
            BindButton(languageConfirmationNo,()=>{ThinkingSpaceLocalizationController.Instance?.CancelPendingInterfaceLanguageChange();languageConfirmation.SetActive(false);});
            BindButton(languageConfirmationYes,()=>{ThinkingSpaceLocalizationController.Instance?.ConfirmPendingInterfaceLanguageChange();languageConfirmation.SetActive(false);});
            languageConfirmation.SetActive(false);
        }

        private void ShowLanguageConfirmation(string code)
        {
            if(languageConfirmation==null)return;
            string name=code=="es"?L("language.spanish"):code=="fr"?L("language.french"):L("language.english");
            if(languageConfirmationQuestion!=null)languageConfirmationQuestion.text=L("language.confirm.body",name);
            languageConfirmation.transform.SetAsLastSibling();languageConfirmation.SetActive(true);
        }

        private void ShowDeleteConfirmation(int number)
        {
            if(sessionDeleteConfirmation==null)return;
            sessionDeleteQuestion.text=L("session.delete.title",number)+"\n"+L("session.delete.body");
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
            bool languageSettings = sessionPage == 3;
            bool finish = sessionPage == 4;
            if(exportSessionButton!=null&&exportSessionButton.transform.parent!=null)
                exportSessionButton.transform.parent.gameObject.SetActive(IsExportOnSessionPage(sessionPage));
            SetActive(challengeButton, ideas);
            SetActive(expandButton, ideas);
            SetActive(connectButton, ideas);
            SetActive(whiteboardButton, ideas);
            SetActive(sessionVoiceButton, helpAndHand);
            SetActive(sessionGesturesButton, helpAndHand);
            SetActive(sessionLeftHandButton, helpAndHand);
            SetActive(sessionRightHandButton, helpAndHand);
            SetActive(sessionWristLeftButton,helpAndHand);SetActive(sessionWristRightButton,helpAndHand);
            if(sessionWristLabel!=null)sessionWristLabel.gameObject.SetActive(helpAndHand);
            SetActive(sessionSilenceMinusButton,audioSettings);SetActive(sessionSilencePlusButton,audioSettings);
            SetActive(sessionConversationalButton,audioSettings);SetActive(sessionManualButton,audioSettings);
            SetActive(sessionSubtitlesOnButton,audioSettings);SetActive(sessionSubtitlesOffButton,audioSettings);SetActive(sessionEditNameButton,audioSettings);SetActive(sessionReplayTutorialButton,audioSettings);
            if(sessionSilenceValueLabel!=null)sessionSilenceValueLabel.gameObject.SetActive(audioSettings);
            if(sessionSettingsTitle!=null)sessionSettingsTitle.gameObject.SetActive(audioSettings);
            SetLanguageSettingsVisible(sessionEnglishButton,sessionSpanishButton,sessionFrenchButton,
                sessionLanguageLabel,languageSettings);
            SetConversationSettingsVisible(sessionConversationLanguageButtons,sessionConversationAutoButton,
                sessionConversationLabel,languageSettings);
            SetLabelVisible(sessionPanel, "settings.recording_hand", helpAndHand);
            SetActive(endSessionButton, finish);
            SetActive(sessionCloseButton, finish);
            UpdateNavigation(sessionPreviousButton, sessionNextButton, sessionPageIndicator,
                sessionPage, SessionPageCount);
        }

        private static void SetActive(Button button, bool active)
        {
            if (button != null) button.gameObject.SetActive(active);
        }

        public static int CalculateMainPageCount(int sessionCount)
            =>Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(0,sessionCount)/(float)SessionsPerPage))
                +MainUtilityPageCount;

        public static bool IsImportOnMainPage(int page)=>page==0;
        public static bool IsExportOnSessionPage(int page)=>page==0;

        private static void SetLabelVisible(GameObject panel, string key, bool visible)
        {
            if (panel == null) return;
            TMP_Text[] labels = panel.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null)
                {
                    ThinkingSpaceLocalizedText localized=labels[i].GetComponent<ThinkingSpaceLocalizedText>();
                    if ((localized!=null&&localized.StringKey==key)||labels[i].text.Trim()==L(key))
                        labels[i].gameObject.SetActive(visible);
                }
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
            ConfigureNavigationButton(previous, L("common.previous"), new Vector2(0.05f, 0.02f), new Vector2(0.3f, 0.11f));
            ConfigureNavigationButton(next, L("common.next"), new Vector2(0.7f, 0.02f), new Vector2(0.95f, 0.11f));
            BindButton(previous, () => changed?.Invoke(-1));
            BindButton(next, () => changed?.Invoke(1));

            TMP_Text sourceLabel = styleSource.GetComponentInChildren<TMP_Text>(true);
            if (sourceLabel != null)
            {
                indicator = Instantiate(sourceLabel, panelRect);
                indicator.name = "Page Indicator";
                indicator.text = L("common.page_format",1,1);
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
            if (indicator != null) indicator.text = L("common.page_format",page+1,pageCount);
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

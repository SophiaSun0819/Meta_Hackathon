using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.Pairing;
using ThinkingSpace.Speech;
using ThinkingSpace.UI.DesignKit;

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
        private IReadOnlyList<BrainstormSession> displayedSessions;
        private ThumbRecordingGestureController.RecordingHand displayedRecordingHand;
        private Button mainPreviousButton, mainNextButton, sessionPreviousButton, sessionNextButton;
        private Button silenceMinusButton, silencePlusButton, conversationalButton, manualButton;
        private TMP_Text silenceValueLabel, settingsTitle;
        private Button sessionSilenceMinusButton, sessionSilencePlusButton, sessionConversationalButton, sessionManualButton;
        private Button subtitlesOnButton,subtitlesOffButton,editNameButton,replayTutorialButton;
        private Button sessionSubtitlesOnButton,sessionSubtitlesOffButton,sessionEditNameButton,sessionReplayTutorialButton;
        private Button mainWristLeftButton,mainWristRightButton,sessionWristLeftButton,sessionWristRightButton;
        private Button mainRecenterButton,sessionRecenterButton;
        private TMP_Text mainWristLabel,sessionWristLabel;
        private Button mainEnglishButton,mainSpanishButton,mainFrenchButton,mainChineseButton,mainHindiButton;
        private Button sessionEnglishButton,sessionSpanishButton,sessionFrenchButton,sessionChineseButton,sessionHindiButton;
        private TMP_Text mainLanguageLabel,sessionLanguageLabel,mainVoiceLanguageInfo,sessionVoiceLanguageInfo;
        private readonly List<Button> mainConversationLanguageButtons=new List<Button>();
        private readonly List<Button> sessionConversationLanguageButtons=new List<Button>();
        private Button mainConversationAutoButton,sessionConversationAutoButton;
        private TMP_Text mainConversationLabel,sessionConversationLabel;
        private TMP_Text mainConversationAvailability,sessionConversationAvailability;
        private Button exportSessionButton,importSessionButton;
        private Button accountConnectionButton;
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
        private GameObject pairingPanel;
        private TMP_Text pairingTitle,pairingBody,pairingCode,pairingCountdown;
        private Button pairingPreviousButton,pairingNextButton,pairingAuxiliaryButton;
        private QuestPairingSnapshot pairingSnapshot;
        private bool hasPairingSnapshot;
        private int mainPage, mainPageCount, sessionPage;
        private int sessionsPerPage=DefaultSessionsPerPage;
        private int currentSessionNumber;
        private float currentSilenceSeconds=2f;
        private const int DefaultSessionsPerPage = 3;
        private const float SessionRowHeight = 76f;
        private const float SessionRowSpacing = 12f;
        private const float SessionListFixedChromeHeight = 134f;
        private const int SessionPageCount = 5;
        private const int MainSettingsPageCount = 3;
        private QuestUIActiveMenuSkin activeMenuSkin;
        private bool designKitHome,designKitSettings;

        public Canvas Canvas { get; private set; }
        public RectTransform CanvasRect => Canvas != null ? Canvas.GetComponent<RectTransform>() : null;
        public GameObject FrontMarker => frontMarker;
        public int RuntimeSessionRowCount=>runtimeSessionRows.Count;
        public int InterfaceLanguageButtonCount=>CountButtons(mainEnglishButton,mainSpanishButton,mainFrenchButton,mainChineseButton,mainHindiButton);
        public bool IsRedundantSessionStartVisible=>startSessionButton!=null&&startSessionButton.gameObject.activeSelf;
        public bool IsSessionImportVisible=>importSessionButton!=null&&importSessionButton.gameObject.activeSelf;
        public bool IsLegacySessionThumbnailVisible
        {
            get
            {
                Transform thumbnail=mainPanel!=null?FindNamedTransform(mainPanel.transform,"SlimUI Thumbnail"):null;
                return thumbnail!=null&&thumbnail.gameObject.activeSelf;
            }
        }
        public int SessionsPerPage=>sessionsPerPage;
        public Rect SessionListNormalizedBounds=>sessionList!=null
            ?new Rect(sessionList.anchorMin,sessionList.anchorMax-sessionList.anchorMin):default;
        public QuestUIActiveMenuSkin ActiveMenuSkin=>activeMenuSkin;
        public string RuntimeVisualState
        {
            get
            {
                string panel=mainPanel!=null&&mainPanel.activeInHierarchy?"main"
                    :sessionPanel!=null&&sessionPanel.activeInHierarchy?"session"
                    :voiceHelpPanel!=null&&voiceHelpPanel.activeInHierarchy?"voice-help"
                    :gestureHelpPanel!=null&&gestureHelpPanel.activeInHierarchy?"gesture-help"
                    :pairingPanel!=null&&pairingPanel.activeInHierarchy?"pairing":"none";
                return panel+"/home:"+(designKitHome?"1":"0")+"/settings:"+(designKitSettings?"1":"0")
                    +"/skin:"+(activeMenuSkin!=null&&activeMenuSkin.IsInitialized?"1":"0")
                    +"/skin-state:"+(activeMenuSkin!=null?activeMenuSkin.RuntimeHierarchyState:"missing");
            }
        }

        private void Awake()
        {
            Canvas = GetComponent<Canvas>();
            if(CanvasRect!=null)CanvasRect.sizeDelta=new Vector2(1440f,960f);
            DisableScrolling(mainPanel);
            DisableScrolling(sessionPanel);
            DisableScrolling(voiceHelpPanel);
            DisableScrolling(gestureHelpPanel);
            CreatePageNavigation(mainPanel, mainCloseButton, out mainPreviousButton,
                out mainNextButton, out mainPageIndicator, ChangeMainPage);
            CreatePageNavigation(sessionPanel, sessionCloseButton, out sessionPreviousButton,
                out sessionNextButton, out sessionPageIndicator, ChangeSessionPage);
            CreateAudioSettingsPage();
            CreateSessionAudioSettingsPage();
            CreateWristSettings();
            CreateRecenterControls();
            ArrangeMainSettingsPage();
            ArrangeSessionHelpPage();
            CreateLanguageSettings();
            CreateSessionTransferActions();
            CreateSessionDeleteConfirmation();
            CreateLanguageConfirmation();
            CreatePairingPanel();
            ApplyFoundationStyling();
            activeMenuSkin=GetComponent<QuestUIActiveMenuSkin>();
            if(activeMenuSkin!=null)activeMenuSkin.Initialize(this,mainPanel,startSessionButton,
                mainVoiceButton,mainGesturesButton,accountConnectionButton,mainCloseButton);
            ArrangeMainSessionsPage();
            ArrangeSessionHeader();
            activeMenuSkin?.ApplyPaletteToFunctionalPanels(mainPanel,sessionPanel,voiceHelpPanel,
                gestureHelpPanel,sessionDeleteConfirmation,languageConfirmation,pairingPanel);
            SetUnavailableSessionActions();
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
            SetButtonLabel(mainRecenterButton,L("menu.recenter"));SetButtonLabel(sessionRecenterButton,L("menu.recenter"));
            SetButtonLabel(importSessionButton,L("menu.session.import"));SetButtonLabel(exportSessionButton,L("menu.session.export"));
            SetButtonLabel(accountConnectionButton,L("pairing.account.title"));
            SetButtonLabel(mainEnglishButton,L("language.english"));SetButtonLabel(mainSpanishButton,L("language.spanish"));SetButtonLabel(mainFrenchButton,L("language.french"));
            SetButtonLabel(mainChineseButton,L("language.mandarin"));SetButtonLabel(mainHindiButton,L("language.hindi"));
            SetButtonLabel(sessionEnglishButton,L("language.english"));SetButtonLabel(sessionSpanishButton,L("language.spanish"));SetButtonLabel(sessionFrenchButton,L("language.french"));
            SetButtonLabel(sessionChineseButton,L("language.mandarin"));SetButtonLabel(sessionHindiButton,L("language.hindi"));
            SetButtonLabel(sessionDeleteCancel,L("common.cancel"));SetButtonLabel(sessionDeleteConfirm,L("session.delete.confirm"));
            SetButtonLabel(languageConfirmationYes,L("common.yes"));SetButtonLabel(languageConfirmationNo,L("common.no"));
            if(settingsTitle!=null)settingsTitle.text=L("settings.recording.title");
            if(sessionSettingsTitle!=null)sessionSettingsTitle.text=L("settings.recording.title");
            if(mainWristLabel!=null)mainWristLabel.text=L("settings.wrist.title");
            if(sessionWristLabel!=null)sessionWristLabel.text=L("settings.wrist.title");
            if(mainLanguageLabel!=null)mainLanguageLabel.text=L("settings.interface_language");
            if(sessionLanguageLabel!=null)sessionLanguageLabel.text=L("settings.interface_language");
            if(mainVoiceLanguageInfo!=null)mainVoiceLanguageInfo.text=L("settings.voice_language.info");
            if(sessionVoiceLanguageInfo!=null)sessionVoiceLanguageInfo.text=L("settings.voice_language.info");
            string language=ThinkingSpaceLocalizationController.Instance?.InterfaceLanguageCode??"en";
            SetButtonSelected(mainEnglishButton,language=="en");SetButtonSelected(mainSpanishButton,language=="es");SetButtonSelected(mainFrenchButton,language=="fr");
            SetButtonSelected(mainChineseButton,language=="zh-CN");SetButtonSelected(mainHindiButton,language=="hi-IN");
            SetButtonSelected(sessionEnglishButton,language=="en");SetButtonSelected(sessionSpanishButton,language=="es");SetButtonSelected(sessionFrenchButton,language=="fr");
            SetButtonSelected(sessionChineseButton,language=="zh-CN");SetButtonSelected(sessionHindiButton,language=="hi-IN");
            if(currentSessionNumber>0&&sessionTitle!=null)sessionTitle.text=L("menu.session.format",currentSessionNumber);
            if(silenceValueLabel!=null)silenceValueLabel.text=L("settings.seconds_format",currentSilenceSeconds.ToString("0.0"));
            if(sessionSilenceValueLabel!=null)sessionSilenceValueLabel.text=L("settings.seconds_format",currentSilenceSeconds.ToString("0.0"));
            for(int i=0;i<runtimeSessionButtons.Count;i++)SetButtonLabel(runtimeSessionButtons[i],L("menu.session.format",ExtractSessionNumber(runtimeSessionButtons[i].name)));
            for(int i=0;i<runtimeSessionDeleteButtons.Count;i++)SetButtonLabel(runtimeSessionDeleteButtons[i],L("common.delete"));
            UpdateNavigation(mainPreviousButton,mainNextButton,mainPageIndicator,mainPage,Mathf.Max(1,mainPageCount));
            UpdateNavigation(sessionPreviousButton,sessionNextButton,sessionPageIndicator,sessionPage,SessionPageCount);
            if(hasPairingSnapshot)RefreshPairingPresentation();
            if(activeMenuSkin!=null)activeMenuSkin.RefreshLocalization();
            SetUnavailableSessionActions();
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
            Action wristLeft,Action wristRight,Action exportSession,Action importSession,
            Action accountConnection)
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
            BindButton(accountConnectionButton,accountConnection);
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
            displayedSessions=sessions;
            displayedRecordingHand=recordingHand;
            RebuildSessionRows();
            mainPage = 0;
            designKitHome=activeMenuSkin!=null;designKitSettings=false;
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        private void RebuildSessionRows()
        {
            IReadOnlyList<BrainstormSession> sessions=displayedSessions;
            ClearSessionButtons();
            int sessionCount=sessions?.Count??0;
            noSessionsLabel.SetActive(sessionCount == 0);
            for (int i = 0; i < sessionCount; i++)
            {
                BrainstormSession session = sessions[i];
                GameObject row = new GameObject("Session " + session.Number + " Row", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(sessionList, false);
                Image rowBackground=row.GetComponent<Image>();rowBackground.color=QuestUIDesignTokens.Raised;rowBackground.raycastTarget=false;
                Sprite rounded=activeMenuSkin!=null?activeMenuSkin.RoundedPanelSprite:null;
                if(rounded!=null){rowBackground.sprite=rounded;rowBackground.type=Image.Type.Sliced;}
                HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 12f; layout.childControlWidth = true; layout.childForceExpandWidth = true;
                layout.childControlHeight=true;layout.childForceExpandHeight=false;
                layout.padding=new RectOffset(10,10,8,8);
                LayoutElement rowLayout = row.GetComponent<LayoutElement>(); rowLayout.preferredHeight = 76f;
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
            SetHandSelection(mainLeftHandButton, mainRightHandButton, displayedRecordingHand);
            sessionsPerPage=CalculateSessionRowsPerPage(ResolveSessionListHeight(),SessionRowHeight,
                SessionRowSpacing,SessionListFixedChromeHeight);
            if(!designKitSettings)mainPageCount = CalculateMainPageCount(sessionCount,sessionsPerPage);
            mainPage=Mathf.Clamp(mainPage,0,Mathf.Max(0,mainPageCount-1));
        }

        public void ShowDesignKitHome()
        {
            designKitHome=true;designKitSettings=false;
            ShowOnly(mainPanel);
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        public void ShowDesignKitSessions()
        {
            designKitHome=false;designKitSettings=false;
            ShowOnly(mainPanel);
            RebuildSessionRows();
            mainPage=0;
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        public void ShowDesignKitSettings()
        {
            designKitHome=false;designKitSettings=true;
            ShowOnly(mainPanel);
            mainPage=0;mainPageCount=MainSettingsPageCount;
            RefreshMainPage(runtimeSessionButtons.Count);
        }

        public void ShowDesignKitHelp()
        {
            designKitHome=false;
            ShowOnly(mainPanel);
            activeMenuSkin?.SetHelpVisible();
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
        public void ShowPairing(QuestPairingSnapshot snapshot,Action previous,Action next,Action auxiliary)
        {
            pairingSnapshot=snapshot;hasPairingSnapshot=true;
            ShowOnly(pairingPanel);
            BindButton(pairingPreviousButton,previous);
            BindButton(pairingNextButton,next);
            BindButton(pairingAuxiliaryButton,auxiliary);
            RefreshPairingPresentation();
        }
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
            if(pairingPanel!=null)pairingPanel.SetActive(panel==pairingPanel);
            activeMenuSkin?.PrepareFunctionalPanelForDisplay(panel);
        }

        private void ClearSessionButtons()
        {
            for (int i = 0; i < runtimeSessionButtons.Count; i++)
                if (runtimeSessionRows[i] != null)
                {
                    runtimeSessionRows[i].SetActive(false);
                    if(Application.isPlaying)Destroy(runtimeSessionRows[i]);
                    else DestroyImmediate(runtimeSessionRows[i]);
                }
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
            if(activeMenuSkin!=null)activeMenuSkin.SetHomeVisible(designKitHome);
            sessionsPerPage=CalculateSessionRowsPerPage(ResolveSessionListHeight(),SessionRowHeight,
                SessionRowSpacing,SessionListFixedChromeHeight);
            int sessionPageCount = CalculateMainPageCount(sessionCount,sessionsPerPage);
            if(!designKitHome)mainPageCount=designKitSettings?MainSettingsPageCount:sessionPageCount;
            bool settingsPage = !designKitHome&&designKitSettings&&mainPage==0;
            bool audioPage = !designKitHome&&designKitSettings&&mainPage==1;
            bool languagePage = !designKitHome&&designKitSettings&&mainPage==2;
            bool sessionPage = !designKitHome&&!designKitSettings;
            bool firstSessionPage=sessionPage&&IsImportOnMainPage(mainPage);
            SetNamedChildActive(mainPanel,"SlimUI Thumbnail",false);
            if (sessionList != null) sessionList.gameObject.SetActive(sessionPage);
            if(mainSessionActionRow!=null)mainSessionActionRow.SetActive(firstSessionPage);
            // Session creation belongs on Home. My Sessions begins with saved/imported
            // sessions and the Import action, without a redundant second Start button.
            SetActive(startSessionButton,false);
            if(mainTransferStatus!=null)mainTransferStatus.gameObject.SetActive(firstSessionPage);
            // Account/device connection is a Home action. It must never displace saved sessions.
            SetActive(accountConnectionButton,false);
            if (noSessionsLabel != null) noSessionsLabel.SetActive(sessionPage && sessionCount == 0 && mainPage == 0);
            for (int i = 0; i < runtimeSessionRows.Count; i++)
                runtimeSessionRows[i].SetActive(sessionPage
                    && i >= mainPage * sessionsPerPage && i < (mainPage + 1) * sessionsPerPage);
            SetActive(mainVoiceButton, false);
            SetActive(mainGesturesButton, false);
            SetActive(mainLeftHandButton, false);
            SetActive(mainRightHandButton, false);
            SetActive(mainWristLeftButton,settingsPage);SetActive(mainWristRightButton,settingsPage);
            SetActive(mainRecenterButton,settingsPage);
            if(mainWristLabel!=null)mainWristLabel.gameObject.SetActive(settingsPage);
            SetActive(mainCloseButton, false);
            SetActive(silenceMinusButton, audioPage); SetActive(silencePlusButton, audioPage);
            SetActive(conversationalButton, audioPage); SetActive(manualButton, audioPage);
            SetActive(subtitlesOnButton,audioPage);SetActive(subtitlesOffButton,audioPage);SetActive(editNameButton,audioPage);SetActive(replayTutorialButton,audioPage);
            if (silenceValueLabel != null) silenceValueLabel.gameObject.SetActive(audioPage);
            if (settingsTitle != null) settingsTitle.gameObject.SetActive(audioPage);
            SetLanguageSettingsVisible(mainEnglishButton,mainSpanishButton,mainFrenchButton,mainChineseButton,
                mainHindiButton,mainLanguageLabel,mainVoiceLanguageInfo,languagePage);
            SetLabelVisible(mainPanel, "menu.sessions.title", sessionPage);
            SetLabelVisible(mainPanel, "app.brainstorm", false);
            SetLabelVisible(mainPanel, "settings.recording_hand", false);
            UpdateNavigation(mainPreviousButton, mainNextButton, mainPageIndicator, mainPage, mainPageCount);
            if(designKitHome)
            {
                SetActive(mainPreviousButton,false);SetActive(mainNextButton,false);
                if(mainPageIndicator!=null)mainPageIndicator.gameObject.SetActive(false);
            }
            else
            {
                bool showPagination=mainPageCount>1;
                SetActive(mainPreviousButton,showPagination);SetActive(mainNextButton,showPagination);
                if(mainPageIndicator!=null)mainPageIndicator.gameObject.SetActive(showPagination);
            }
            if(activeMenuSkin!=null)activeMenuSkin.SetHomeVisible(designKitHome);
        }

        private void SetUnavailableSessionActions()
        {
            Button[] unfinished={challengeButton,expandButton,connectButton,whiteboardButton};
            string[] keys={"menu.challenge","menu.expand","menu.connect","menu.whiteboard"};
            for(int i=0;i<unfinished.Length;i++)
            {
                Button button=unfinished[i];if(button==null)continue;
                button.interactable=false;
                TMP_Text label=button.GetComponentInChildren<TMP_Text>(true);
                if(label!=null)label.text=L(keys[i])+" · "+L("menu.feature.unavailable");
            }
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

        private void ArrangeSessionHelpPage()
        {
            // Authored controls used to retain anchors relative to different nested layout groups.
            // That looked valid in EditMode but collapsed into the title on Quest. Page two uses one
            // panel coordinate system and leaves the fixed header/footer regions untouched.
            ReparentButton(sessionVoiceButton,sessionPanel);ReparentButton(sessionGesturesButton,sessionPanel);
            ReparentButton(sessionLeftHandButton,sessionPanel);ReparentButton(sessionRightHandButton,sessionPanel);
            SetButtonRect(sessionVoiceButton,new Vector2(.08f,.66f),new Vector2(.47f,.76f));
            SetButtonRect(sessionGesturesButton,new Vector2(.53f,.66f),new Vector2(.92f,.76f));
            SetButtonRect(sessionLeftHandButton,new Vector2(.08f,.45f),new Vector2(.47f,.55f));
            SetButtonRect(sessionRightHandButton,new Vector2(.53f,.45f),new Vector2(.92f,.55f));
            if(sessionWristLabel!=null)SetRect(sessionWristLabel.rectTransform,
                new Vector2(.08f,.35f),new Vector2(.92f,.41f));
            SetButtonRect(sessionWristLeftButton,new Vector2(.08f,.23f),new Vector2(.47f,.33f));
            SetButtonRect(sessionWristRightButton,new Vector2(.53f,.23f),new Vector2(.92f,.33f));
            SetButtonRect(sessionRecenterButton,new Vector2(.34f,.13f),new Vector2(.66f,.20f));
            SetLabelRect(sessionPanel,"settings.recording_hand",
                new Vector2(.08f,.57f),new Vector2(.92f,.63f));
            LimitButtonText(sessionVoiceButton,16f);LimitButtonText(sessionGesturesButton,16f);
            LimitButtonText(sessionLeftHandButton,16f);LimitButtonText(sessionRightHandButton,16f);
            LimitButtonText(sessionWristLeftButton,16f);LimitButtonText(sessionWristRightButton,16f);
        }

        private void ArrangeMainSessionsPage()
        {
            if(sessionList==null||mainPanel==null)return;
            SetRect(sessionList,new Vector2(.04f,.04f),new Vector2(.96f,.82f));
            ContentSizeFitter fitter=sessionList.GetComponent<ContentSizeFitter>();
            if(fitter!=null)
            {
                fitter.horizontalFit=ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit=ContentSizeFitter.FitMode.Unconstrained;
            }
            VerticalLayoutGroup layout=sessionList.GetComponent<VerticalLayoutGroup>();
            if(layout!=null)
            {
                layout.spacing=SessionRowSpacing;layout.childAlignment=TextAnchor.UpperCenter;
                layout.childControlHeight=true;layout.childForceExpandHeight=false;
                layout.padding=new RectOffset(0,0,0,0);
            }
            SetLabelRect(mainPanel,"menu.sessions.title",new Vector2(.08f,.85f),new Vector2(.92f,.96f));
        }

        private void ArrangeSessionHeader()
        {
            if(sessionTitle!=null)SetRect(sessionTitle.rectTransform,new Vector2(.08f,.87f),new Vector2(.92f,.96f));
            if(sessionStatus!=null)SetRect(sessionStatus.rectTransform,new Vector2(.08f,.79f),new Vector2(.92f,.86f));
        }

        private void ArrangeMainSettingsPage()
        {
            ReparentButton(mainVoiceButton,mainPanel);ReparentButton(mainGesturesButton,mainPanel);
            ReparentButton(mainLeftHandButton,mainPanel);ReparentButton(mainRightHandButton,mainPanel);
            SetButtonRect(mainVoiceButton,new Vector2(.08f,.70f),new Vector2(.47f,.83f));
            SetButtonRect(mainGesturesButton,new Vector2(.53f,.70f),new Vector2(.92f,.83f));
            SetButtonRect(mainLeftHandButton,new Vector2(.08f,.46f),new Vector2(.47f,.59f));
            SetButtonRect(mainRightHandButton,new Vector2(.53f,.46f),new Vector2(.92f,.59f));
            if(mainWristLabel!=null)SetRect(mainWristLabel.rectTransform,new Vector2(.08f,.34f),new Vector2(.92f,.42f));
            SetButtonRect(mainWristLeftButton,new Vector2(.08f,.20f),new Vector2(.47f,.32f));
            SetButtonRect(mainWristRightButton,new Vector2(.53f,.20f),new Vector2(.92f,.32f));
            SetButtonRect(mainRecenterButton,new Vector2(.34f,.07f),new Vector2(.66f,.17f));
            SetLabelRect(mainPanel,"settings.recording_hand",new Vector2(.08f,.60f),new Vector2(.92f,.68f));
            LimitButtonText(mainVoiceButton,16f);LimitButtonText(mainGesturesButton,16f);
            LimitButtonText(mainLeftHandButton,16f);LimitButtonText(mainRightHandButton,16f);
            LimitButtonText(mainWristLeftButton,16f);LimitButtonText(mainWristRightButton,16f);
        }

        private static void SetButtonRect(Button button,Vector2 min,Vector2 max)
        {
            if(button==null)return;
            SetRect(button.GetComponent<RectTransform>(),min,max);
        }

        private static void ReparentButton(Button button,GameObject panel)
        {if(button!=null&&panel!=null&&button.transform.parent!=panel.transform)button.transform.SetParent(panel.transform,false);}

        private void CreateLanguageSettings()
        {
            CreateLanguageSettings(mainPanel,mainCloseButton,out mainEnglishButton,
                out mainSpanishButton,out mainFrenchButton,out mainChineseButton,out mainHindiButton,
                out mainLanguageLabel,out mainVoiceLanguageInfo);
            CreateLanguageSettings(sessionPanel,sessionCloseButton,out sessionEnglishButton,
                out sessionSpanishButton,out sessionFrenchButton,out sessionChineseButton,out sessionHindiButton,
                out sessionLanguageLabel,out sessionVoiceLanguageInfo);
        }

        private static void CreateLanguageSettings(GameObject panel,Button source,out Button english,
            out Button spanish,out Button french,out Button chinese,out Button hindi,out TMP_Text label,out TMP_Text voiceInfo)
        {
            english=spanish=french=chinese=hindi=null;label=voiceInfo=null;if(panel==null||source==null)return;
            RectTransform parent=panel.GetComponent<RectTransform>();
            english=Instantiate(source,parent);ConfigureNavigationButton(english,L("language.english"),new Vector2(.08f,.56f),new Vector2(.34f,.68f));
            spanish=Instantiate(source,parent);ConfigureNavigationButton(spanish,L("language.spanish"),new Vector2(.37f,.56f),new Vector2(.63f,.68f));
            french=Instantiate(source,parent);ConfigureNavigationButton(french,L("language.french"),new Vector2(.66f,.56f),new Vector2(.92f,.68f));
            chinese=Instantiate(source,parent);ConfigureNavigationButton(chinese,L("language.mandarin"),new Vector2(.20f,.39f),new Vector2(.48f,.51f));
            hindi=Instantiate(source,parent);ConfigureNavigationButton(hindi,L("language.hindi"),new Vector2(.52f,.39f),new Vector2(.80f,.51f));
            BindButton(english,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetInterfaceLanguage("en"));
            BindButton(spanish,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetInterfaceLanguage("es"));
            BindButton(french,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetInterfaceLanguage("fr"));
            BindButton(chinese,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetInterfaceLanguage("zh-CN"));
            BindButton(hindi,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetInterfaceLanguage("hi-IN"));
            TMP_Text sourceLabel=source.GetComponentInChildren<TMP_Text>(true);
            if(sourceLabel!=null){label=Instantiate(sourceLabel,parent);label.text=L("settings.interface_language");
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                SetRect(label.rectTransform,new Vector2(.08f,.76f),new Vector2(.92f,.87f));
                voiceInfo=Instantiate(sourceLabel,parent);voiceInfo.text=L("settings.voice_language.info");
                voiceInfo.alignment=TextAlignmentOptions.Center;voiceInfo.raycastTarget=false;
                voiceInfo.enableAutoSizing=true;voiceInfo.fontSizeMin=14f;voiceInfo.fontSizeMax=20f;
                SetRect(voiceInfo.rectTransform,new Vector2(.10f,.17f),new Vector2(.90f,.33f));}
        }

        private static void CreateConversationLanguageSettings(GameObject panel,Button source,
            List<Button> languages,out Button automatic,out TMP_Text label,out TMP_Text availability)
        {
            automatic=null;label=null;availability=null;if(panel==null||source==null)return;
            RectTransform parent=panel.GetComponent<RectTransform>();
            automatic=Instantiate(source,parent);ConfigureNavigationButton(automatic,L("settings.language.auto"),
                new Vector2(.08f,.42f),new Vector2(.31f,.52f));
            BindButton(automatic,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetConversationLanguage("auto",null,"orby-auto"));
            string[] codes={"en","es","fr","zh-CN","hi-IN"};
            BrainstormMenuController controller=FindFirstObjectByType<BrainstormMenuController>();
            for(int i=0;i<codes.Length;i++)
            {
                string code=codes[i];bool top=i<3;int column=top?i:i-3;
                float columns=top?3f:2f;float width=.84f/columns;float x=.08f+column*width;
                float minY=top?.265f:.13f;float maxY=top?.375f:.24f;
                Button button=Instantiate(source,parent);button.name="Conversation Language "+code;
                ConfigureNavigationButton(button,ConversationLanguageLabel(code),new Vector2(x,minY),new Vector2(x+width-.018f,maxY));
                button.interactable=ConversationLanguageControlEnabled(
                    controller!=null&&controller.MultilingualBackendContractEnabled,code);
                BindButton(button,()=>FindFirstObjectByType<BrainstormMenuController>()?.SetConversationLanguage(
                    "explicit",code,VoiceProfileForLanguage(code)));
                languages.Add(button);
            }
            TMP_Text sourceLabel=source.GetComponentInChildren<TMP_Text>(true);
            if(sourceLabel!=null){label=Instantiate(sourceLabel,parent);label.text=L("settings.conversation_mode");
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                SetRect(label.rectTransform,new Vector2(.34f,.42f),new Vector2(.92f,.52f));
                availability=Instantiate(sourceLabel,parent);availability.alignment=TextAlignmentOptions.Center;
                availability.raycastTarget=false;availability.enableAutoSizing=true;availability.fontSizeMin=14f;
                availability.fontSizeMax=20f;SetRect(availability.rectTransform,new Vector2(.08f,.055f),new Vector2(.92f,.12f));}
        }

        private static string VoiceProfileForLanguage(string code)=>code=="es"?"orby-es":code=="fr"?"orby-fr":
            code=="zh-CN"?"orby-zh":code=="hi-IN"?"orby-hi":"orby-en";
        private static string ConversationLanguageLabel(string code)=>code=="es"?L("language.spanish"):
            code=="fr"?L("language.french"):code=="zh-CN"?L("language.mandarin"):
            code=="hi-IN"?L("language.hindi"):L("language.english");
        private static void RefreshConversationButtonLabels(List<Button> buttons)
        {for(int i=0;i<buttons.Count;i++){string[] codes={"en","es","fr","zh-CN","hi-IN"};if(i<codes.Length)SetButtonLabel(buttons[i],ConversationLanguageLabel(codes[i]));}}

        private void RefreshConversationLanguageState()
        {
            BrainstormMenuController controller=FindFirstObjectByType<BrainstormMenuController>();
            bool enabled=controller!=null&&controller.MultilingualBackendContractEnabled;
            string mode=controller?.ConversationLanguageMode??ThinkingSpacePreferencesStore.DefaultConversationLanguageMode;
            string code=controller?.ConversationLanguageCode??string.Empty;
            // With the legacy contract, English is the actual backend behavior even if
            // an older saved preference names another language. Reflect the wire truth.
            string effectiveMode=enabled?mode:"explicit";
            string effectiveCode=enabled?code:"en";
            SetButtonSelected(mainConversationAutoButton,enabled&&mode=="auto");
            SetButtonSelected(sessionConversationAutoButton,enabled&&mode=="auto");
            if(mainConversationAutoButton!=null)mainConversationAutoButton.interactable=enabled;
            if(sessionConversationAutoButton!=null)sessionConversationAutoButton.interactable=enabled;
            RefreshExplicitConversationButtons(mainConversationLanguageButtons,enabled,effectiveMode,effectiveCode);
            RefreshExplicitConversationButtons(sessionConversationLanguageButtons,enabled,effectiveMode,effectiveCode);
            string status=enabled?L("settings.conversation.active",
                mode=="explicit"?ConversationLanguageLabel(code):L("settings.language.auto"))
                :L("settings.conversation.active",ConversationLanguageLabel("en"));
            if(mainConversationAvailability!=null)mainConversationAvailability.text=status;
            if(sessionConversationAvailability!=null)sessionConversationAvailability.text=status;
        }

        private static void RefreshExplicitConversationButtons(List<Button> buttons,bool enabled,string mode,string code)
        {
            string[] codes={"en","es","fr","zh-CN","hi-IN"};
            for(int i=0;i<buttons.Count;i++)
            {
                buttons[i].interactable=ConversationLanguageControlEnabled(enabled,codes[i]);
                SetButtonSelected(buttons[i],mode=="explicit"&&i<codes.Length&&codes[i]==code);
            }
        }

        public static bool ExplicitConversationControlsEnabled(bool backendContractEnabled)
            =>backendContractEnabled;
        public static bool ConversationLanguageControlEnabled(bool backendContractEnabled,string code)
            =>backendContractEnabled||string.Equals(code,"en",StringComparison.OrdinalIgnoreCase);
        private static void SetConversationSettingsVisible(List<Button> buttons,Button automatic,TMP_Text label,
            TMP_Text availability,bool visible)
        {SetActive(automatic,visible);for(int i=0;i<buttons.Count;i++)SetActive(buttons[i],visible);
            if(label!=null)label.gameObject.SetActive(visible);if(availability!=null)availability.gameObject.SetActive(visible);}

        private static void SetLanguageSettingsVisible(Button english,Button spanish,Button french,Button chinese,
            Button hindi,TMP_Text label,TMP_Text voiceInfo,bool visible)
        {
            SetActive(english,visible);SetActive(spanish,visible);SetActive(french,visible);
            SetActive(chinese,visible);SetActive(hindi,visible);
            if(label!=null)label.gameObject.SetActive(visible);if(voiceInfo!=null)voiceInfo.gameObject.SetActive(visible);
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

            // Keep a hidden callback source for the Home card, outside the My Sessions layout.
            accountConnectionButton=Instantiate(startSessionButton,mainPanel.transform);
            accountConnectionButton.name="Account and Device Connection";
            SetButtonLabel(accountConnectionButton,L("pairing.account.title"));
            accountConnectionButton.gameObject.SetActive(false);

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

        private void CreateRecenterControls()
        {
            if(mainPanel!=null&&mainCloseButton!=null){mainRecenterButton=Instantiate(mainCloseButton,mainPanel.transform);ConfigureNavigationButton(mainRecenterButton,L("menu.recenter"),new Vector2(.34f,.12f),new Vector2(.66f,.22f));BindButton(mainRecenterButton,()=>FindFirstObjectByType<BrainstormMenuController>()?.RecenterMenu());}
            if(sessionPanel!=null&&sessionCloseButton!=null){sessionRecenterButton=Instantiate(sessionCloseButton,sessionPanel.transform);ConfigureNavigationButton(sessionRecenterButton,L("menu.recenter"),new Vector2(.34f,.14f),new Vector2(.66f,.24f));BindButton(sessionRecenterButton,()=>FindFirstObjectByType<BrainstormMenuController>()?.RecenterMenu());}
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

        private void CreatePairingPanel()
        {
            if(CanvasRect==null||mainCloseButton==null)return;
            pairingPanel=new GameObject("Account and Device Connection Panel",typeof(RectTransform),typeof(Image));
            pairingPanel.transform.SetParent(CanvasRect,false);
            RectTransform panelRect=pairingPanel.GetComponent<RectTransform>();
            RectTransform mainRect=mainPanel!=null?mainPanel.GetComponent<RectTransform>():null;
            if(mainRect!=null)
            {
                panelRect.anchorMin=mainRect.anchorMin;panelRect.anchorMax=mainRect.anchorMax;
                panelRect.pivot=mainRect.pivot;panelRect.anchoredPosition=mainRect.anchoredPosition;
                panelRect.sizeDelta=mainRect.sizeDelta;
            }
            else SetRect(panelRect,Vector2.zero,Vector2.one);
            pairingPanel.GetComponent<Image>().color=new Color(.035f,.055f,.09f,.98f);
            Sprite rounded=activeMenuSkin!=null?activeMenuSkin.RoundedPanelSprite:null;
            if(rounded!=null){Image pairingBackground=pairingPanel.GetComponent<Image>();pairingBackground.sprite=rounded;pairingBackground.type=Image.Type.Sliced;}

            TMP_Text source=mainCloseButton.GetComponentInChildren<TMP_Text>(true);
            if(source!=null)
            {
                pairingTitle=Instantiate(source,pairingPanel.transform);pairingTitle.name="Pairing Title";
                pairingTitle.alignment=TextAlignmentOptions.Center;pairingTitle.raycastTarget=false;
                pairingTitle.fontSize=Mathf.Max(pairingTitle.fontSize,32f);
                SetRect(pairingTitle.rectTransform,new Vector2(.07f,.78f),new Vector2(.93f,.94f));
                pairingBody=Instantiate(source,pairingPanel.transform);pairingBody.name="Pairing Body";
                pairingBody.alignment=TextAlignmentOptions.Center;pairingBody.raycastTarget=false;
                pairingBody.enableWordWrapping=true;pairingBody.fontSize=Mathf.Min(pairingBody.fontSize,23f);
                SetRect(pairingBody.rectTransform,new Vector2(.08f,.45f),new Vector2(.92f,.76f));
                pairingCode=Instantiate(source,pairingPanel.transform);pairingCode.name="Six Digit Pairing Code";
                pairingCode.alignment=TextAlignmentOptions.Center;pairingCode.raycastTarget=false;
                pairingCode.fontSize=Mathf.Max(pairingCode.fontSize,46f);pairingCode.characterSpacing=8f;
                SetRect(pairingCode.rectTransform,new Vector2(.12f,.35f),new Vector2(.88f,.48f));
                pairingCountdown=Instantiate(source,pairingPanel.transform);pairingCountdown.name="Pairing Expiration Countdown";
                pairingCountdown.alignment=TextAlignmentOptions.Center;pairingCountdown.raycastTarget=false;
                pairingCountdown.fontSize=Mathf.Min(pairingCountdown.fontSize,20f);
                SetRect(pairingCountdown.rectTransform,new Vector2(.12f,.27f),new Vector2(.88f,.36f));
            }

            pairingPreviousButton=Instantiate(mainCloseButton,pairingPanel.transform);
            pairingPreviousButton.name="Pairing Previous Button";
            ConfigureNavigationButton(pairingPreviousButton,L("common.previous"),new Vector2(.05f,.05f),new Vector2(.30f,.16f));
            pairingAuxiliaryButton=Instantiate(mainCloseButton,pairingPanel.transform);
            pairingAuxiliaryButton.name="Pairing Auxiliary Button";
            ConfigureNavigationButton(pairingAuxiliaryButton,L("common.cancel"),new Vector2(.36f,.05f),new Vector2(.64f,.16f));
            pairingNextButton=Instantiate(mainCloseButton,pairingPanel.transform);
            pairingNextButton.name="Pairing Next Button";
            ConfigureNavigationButton(pairingNextButton,L("common.next"),new Vector2(.70f,.05f),new Vector2(.95f,.16f));
            pairingPanel.SetActive(false);
        }

        private void RefreshPairingPresentation()
        {
            if(!hasPairingSnapshot)return;
            QuestPairingUiState state=pairingSnapshot.State;
            string titleKey="pairing.account.title",bodyKey="pairing.account.disconnected";
            string nextKey="common.next",auxiliaryKey="common.cancel";
            bool showCode=false,showCountdown=false,showNext=true,showAuxiliary=false;
            switch(state)
            {
                case QuestPairingUiState.UnavailableOffline:
                    titleKey="pairing.unavailable.title";bodyKey=string.IsNullOrEmpty(pairingSnapshot.MessageKey)?"pairing.unavailable.body":pairingSnapshot.MessageKey;nextKey="common.retry";break;
                case QuestPairingUiState.Disconnected:
                    bodyKey="pairing.account.disconnected";nextKey="pairing.action.pair";break;
                case QuestPairingUiState.RequestingCode:
                    titleKey="pairing.requesting.title";bodyKey="pairing.requesting.body";showNext=false;showAuxiliary=true;break;
                case QuestPairingUiState.CodeReady:
                    titleKey="pairing.code.title";bodyKey="pairing.code.body";showCode=showCountdown=true;showAuxiliary=true;auxiliaryKey="pairing.action.new_code";break;
                case QuestPairingUiState.WaitingForWebsite:
                    titleKey="pairing.waiting.title";bodyKey="pairing.waiting.body";showCode=showCountdown=true;showNext=false;showAuxiliary=true;break;
                case QuestPairingUiState.AwaitingQuestConfirmation:
                    titleKey="pairing.confirm.title";bodyKey="pairing.confirm.body";nextKey="pairing.action.confirm";showCountdown=true;showAuxiliary=true;break;
                case QuestPairingUiState.Paired:
                    titleKey="pairing.paired.title";bodyKey="pairing.paired.body";nextKey="pairing.action.done";auxiliaryKey="pairing.action.unpair";showAuxiliary=true;break;
                case QuestPairingUiState.Expired:
                    titleKey="pairing.expired.title";bodyKey="pairing.expired.body";nextKey="pairing.action.new_code";break;
                case QuestPairingUiState.Failed:
                    titleKey="pairing.failed.title";bodyKey=string.IsNullOrEmpty(pairingSnapshot.MessageKey)?"pairing.failed.body":pairingSnapshot.MessageKey;nextKey="common.retry";break;
                case QuestPairingUiState.UnpairConfirmation:
                    titleKey="pairing.unpair.title";bodyKey="pairing.unpair.body";nextKey="common.yes";break;
            }
            if(pairingTitle!=null)pairingTitle.text=L(titleKey);
            if(pairingBody!=null)pairingBody.text=L(bodyKey,pairingSnapshot.AccountLabel);
            if(pairingCode!=null){pairingCode.gameObject.SetActive(showCode);pairingCode.text=FormatPairingCode(pairingSnapshot.PairingCode);}
            if(pairingCountdown!=null){pairingCountdown.gameObject.SetActive(showCountdown);pairingCountdown.text=L("pairing.expires",pairingSnapshot.RemainingSeconds);}
            SetButtonLabel(pairingPreviousButton,L("common.previous"));
            SetButtonLabel(pairingNextButton,L(nextKey));SetActive(pairingNextButton,showNext);
            SetButtonLabel(pairingAuxiliaryButton,L(auxiliaryKey));SetActive(pairingAuxiliaryButton,showAuxiliary);
        }

        private static string FormatPairingCode(string value)
            =>QuestPairingStateMachine.IsSixDigitCode(value)?value.Substring(0,3)+" "+value.Substring(3):string.Empty;

        private void ShowLanguageConfirmation(string code)
        {
            if(languageConfirmation==null)return;
            string name=code=="es"?L("language.spanish"):code=="fr"?L("language.french"):
                code=="zh-CN"?L("language.mandarin"):code=="hi-IN"?L("language.hindi"):L("language.english");
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
        {
            rect.anchorMin=min;rect.anchorMax=max;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=Vector2.zero;
            rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
            LayoutElement layout=rect.GetComponent<LayoutElement>();
            if(layout==null&&rect.parent!=null&&rect.parent.GetComponent<LayoutGroup>()!=null)
                layout=rect.gameObject.AddComponent<LayoutElement>();
            if(layout!=null)layout.ignoreLayout=true;
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
            SetActive(sessionRecenterButton,helpAndHand);
            if(sessionWristLabel!=null)sessionWristLabel.gameObject.SetActive(helpAndHand);
            SetActive(sessionSilenceMinusButton,audioSettings);SetActive(sessionSilencePlusButton,audioSettings);
            SetActive(sessionConversationalButton,audioSettings);SetActive(sessionManualButton,audioSettings);
            SetActive(sessionSubtitlesOnButton,audioSettings);SetActive(sessionSubtitlesOffButton,audioSettings);SetActive(sessionEditNameButton,audioSettings);SetActive(sessionReplayTutorialButton,audioSettings);
            if(sessionSilenceValueLabel!=null)sessionSilenceValueLabel.gameObject.SetActive(audioSettings);
            if(sessionSettingsTitle!=null)sessionSettingsTitle.gameObject.SetActive(audioSettings);
            SetLanguageSettingsVisible(sessionEnglishButton,sessionSpanishButton,sessionFrenchButton,
                sessionChineseButton,sessionHindiButton,sessionLanguageLabel,sessionVoiceLanguageInfo,languageSettings);
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

        private static void SetNamedChildActive(GameObject root,string objectName,bool active)
        {
            Transform target=root!=null?FindNamedTransform(root.transform,objectName):null;
            if(target!=null)target.gameObject.SetActive(active);
        }

        private static Transform FindNamedTransform(Transform root,string objectName)
        {
            if(root==null)return null;
            if(root.name==objectName)return root;
            for(int i=0;i<root.childCount;i++)
            {
                Transform result=FindNamedTransform(root.GetChild(i),objectName);
                if(result!=null)return result;
            }
            return null;
        }

        private float ResolveSessionListHeight()
        {
            float height=sessionList!=null?sessionList.rect.height:0f;
            if(height>=200f)return height;
            float canvasHeight=CanvasRect!=null?CanvasRect.rect.height:960f;
            // Session List occupies 78% of the functional content, which itself occupies 61%
            // of the authored panel. Use the runtime anchors even before the first layout pass.
            return canvasHeight*.61f*.78f;
        }

        public static int CalculateSessionRowsPerPage(float availableHeight,float rowHeight=SessionRowHeight,
            float spacing=SessionRowSpacing,float fixedChromeHeight=SessionListFixedChromeHeight)
        {
            float usable=Mathf.Max(rowHeight,availableHeight-Mathf.Max(0f,fixedChromeHeight));
            return Mathf.Max(1,Mathf.FloorToInt((usable+Mathf.Max(0f,spacing))/
                Mathf.Max(1f,rowHeight+Mathf.Max(0f,spacing))));
        }

        public static int CalculateMainPageCount(int sessionCount,int rowsPerPage=DefaultSessionsPerPage)
            =>Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(0,sessionCount)/(float)Mathf.Max(1,rowsPerPage)));

        public static bool IsImportOnMainPage(int page)=>page==0;
        public static bool IsAccountConnectionOnMainPage(int page)=>false;
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

        private static void SetLabelRect(GameObject panel,string key,Vector2 min,Vector2 max)
        {
            if(panel==null)return;
            TMP_Text[] labels=panel.GetComponentsInChildren<TMP_Text>(true);
            for(int i=0;i<labels.Length;i++)
            {
                TMP_Text label=labels[i];
                if(label==null)continue;
                ThinkingSpaceLocalizedText localized=label.GetComponent<ThinkingSpaceLocalizedText>();
                if((localized!=null&&localized.StringKey==key)||label.text.Trim()==L(key))
                    SetRect(label.rectTransform,min,max);
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
            RectMask2D[] clipMasks = panel.GetComponentsInChildren<RectMask2D>(true);
            for (int i = 0; i < clipMasks.Length; i++)
                clipMasks[i].enabled = false;
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
            ThinkingSpaceUIStyler.StyleButton(button, ThinkingSpaceButtonKind.Navigation);
            ColorBlock colors=button.colors;colors.highlightedColor=QuestUIDesignTokens.Hover;
            colors.pressedColor=QuestUIDesignTokens.Pressed;colors.selectedColor=QuestUIDesignTokens.Cyan;
            colors.disabledColor=QuestUIDesignTokens.Disabled;colors.fadeDuration=.08f;button.colors=colors;
            button.gameObject.SetActive(true);
        }

        private static void LimitButtonText(Button button,float minimumSize)
        {
            TMP_Text text=button!=null?button.GetComponentInChildren<TMP_Text>(true):null;
            if(text==null)return;text.enableAutoSizing=true;text.fontSizeMin=minimumSize;
            text.fontSizeMax=Mathf.Max(minimumSize,26f);text.textWrappingMode=TextWrappingModes.Normal;
        }

        private void ApplyFoundationStyling()
        {
            GameObject[] panels={mainPanel,sessionPanel,voiceHelpPanel,gestureHelpPanel,
                sessionDeleteConfirmation,languageConfirmation,pairingPanel};
            for(int i=0;i<panels.Length;i++)
            {
                if(panels[i]==null)continue;
                ThinkingSpaceUIStyler.StylePanel(panels[i],false,true);
                ThinkingSpaceUIStyler.StyleHierarchy(panels[i]);
            }
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

        private static int CountButtons(params Button[] buttons)
        {int count=0;for(int i=0;i<buttons.Length;i++)if(buttons[i]!=null)count++;return count;}
    }
}

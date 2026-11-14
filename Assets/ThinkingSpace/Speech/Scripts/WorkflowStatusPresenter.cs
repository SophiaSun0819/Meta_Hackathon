using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    public enum RecoverableWorkflowFailureKind
    { NetworkUnavailable, AiProcessingFailed, SpeechRecognitionFailed, UnsupportedCapability, GenerationFailed }

    [DisallowMultipleComponent]
    public sealed class WorkflowStatusPresenter : MonoBehaviour
    {
        [SerializeField] private Vector3 headsetLocalPosition = new Vector3(0f, -0.12f, 0.55f);
        [SerializeField, Min(0.0001f)] private float canvasScale = 0.0005f;
        [SerializeField, Min(1f)] private float automaticDismissSeconds = 8f;
        private Transform headset;
        private Canvas canvas;
        private TMP_Text heading, body;
        private Button retryButton, dismissButton;
        private Action retryAction, dismissAction;
        private float shownAt;
        private RecoverableWorkflowFailureKind visibleKind;
        private bool visibleRetainedInput;
        public bool IsVisible => canvas != null && canvas.gameObject.activeSelf;

        private void Awake(){Build();canvas.gameObject.SetActive(false);}
        private void OnEnable()=>ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=HandleLanguageChanged;
        private void OnDisable()=>ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=HandleLanguageChanged;
        public void SetHeadset(Transform value)=>headset=value;
        public void ShowRecoverable(RecoverableWorkflowFailureKind kind,Action retry,Action dismiss,bool retainedInput)
        {
            if(canvas==null)Build();retryAction=retry;dismissAction=dismiss;visibleKind=kind;visibleRetainedInput=retainedInput;
            RefreshLocalizedContent();
            ConfigureButton(retryButton,L("common.retry"),retry!=null);
            ConfigureButton(dismissButton,L("common.dismiss"),dismiss!=null);
            shownAt=Time.unscaledTime;canvas.gameObject.SetActive(true);PositionAtHeadset();
        }
        public void Hide(){retryAction=null;dismissAction=null;if(canvas!=null)canvas.gameObject.SetActive(false);}
        public static string MessageKey(RecoverableWorkflowFailureKind kind)
        {
            switch(kind){case RecoverableWorkflowFailureKind.NetworkUnavailable:return "error.network_unavailable";
                case RecoverableWorkflowFailureKind.SpeechRecognitionFailed:return "error.speech_not_understood";
                case RecoverableWorkflowFailureKind.UnsupportedCapability:return "error.unsupported_capability";
                case RecoverableWorkflowFailureKind.GenerationFailed:return "error.generation_failed";
                default:return "error.ai_processing_failed";}
        }
        public static RecoverableWorkflowFailureKind ClassifyTransportFailure(long httpStatus,string category=null)
        {
            string value=category??string.Empty;
            bool transportCategory=value.IndexOf("network",StringComparison.OrdinalIgnoreCase)>=0
                ||value.IndexOf("connection",StringComparison.OrdinalIgnoreCase)>=0
                ||value.IndexOf("timed out",StringComparison.OrdinalIgnoreCase)>=0
                ||value.IndexOf("timeout",StringComparison.OrdinalIgnoreCase)>=0;
            return httpStatus==408||httpStatus==503||httpStatus==504||(httpStatus==0&&transportCategory)
                ?RecoverableWorkflowFailureKind.NetworkUnavailable:RecoverableWorkflowFailureKind.SpeechRecognitionFailed;
        }

        public static RecoverableWorkflowFailureKind ClassifyAiFailure(long httpStatus,string stage=null)
        {
            string value=stage??string.Empty;
            bool localValidation=value.IndexOf("validation",StringComparison.OrdinalIgnoreCase)>=0
                ||value.IndexOf("schema",StringComparison.OrdinalIgnoreCase)>=0
                ||value.IndexOf("parsing",StringComparison.OrdinalIgnoreCase)>=0;
            return !localValidation&&(httpStatus==0||httpStatus==408||httpStatus==503||httpStatus==504)
                ?RecoverableWorkflowFailureKind.NetworkUnavailable:RecoverableWorkflowFailureKind.AiProcessingFailed;
        }
        private void Update(){if(!IsVisible||retryAction!=null||dismissAction!=null)return;if(Time.unscaledTime-shownAt>=automaticDismissSeconds)Hide();}
        private void LateUpdate(){if(IsVisible)PositionAtHeadset();}
        private void PositionAtHeadset(){if(headset==null||canvas==null)return;canvas.transform.position=headset.TransformPoint(headsetLocalPosition);Vector3 away=canvas.transform.position-headset.position;if(away.sqrMagnitude>.0001f)canvas.transform.rotation=Quaternion.LookRotation(away.normalized,Vector3.up);}
        private void Build()
        {
            GameObject root=new GameObject("Recoverable Workflow Status",typeof(RectTransform));root.transform.SetParent(transform,false);
            canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;canvas.sortingOrder=75;
            root.AddComponent<GraphicRaycaster>();
            RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(520f,185f);rect.localScale=Vector3.one*canvasScale;
            root.AddComponent<Image>();root.AddComponent<CompactPanel>();
            MetaHandInteractionAdapter interaction=root.AddComponent<MetaHandInteractionAdapter>();interaction.Configure(canvas,rect);
            heading=Label("Heading",rect,new Vector2(.05f,.70f),new Vector2(.95f,.93f),30f);
            body=Label("Message",rect,new Vector2(.06f,.28f),new Vector2(.94f,.70f),23f);body.alignment=TextAlignmentOptions.MidlineLeft;
            retryButton=MakeButton("Retry",rect,new Vector2(.08f,.06f),new Vector2(.46f,.24f));
            dismissButton=MakeButton("Dismiss",rect,new Vector2(.54f,.06f),new Vector2(.92f,.24f));
            retryButton.gameObject.AddComponent<PrimaryButton>();dismissButton.gameObject.AddComponent<SecondaryButton>();
            retryButton.onClick.AddListener(()=>{Action action=retryAction;Hide();action?.Invoke();});
            dismissButton.onClick.AddListener(()=>{Action action=dismissAction;Hide();action?.Invoke();});
        }
        private static TMP_Text Label(string name,RectTransform parent,Vector2 min,Vector2 max,float size){GameObject value=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));value.transform.SetParent(parent,false);RectTransform rect=value.GetComponent<RectTransform>();rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;TMP_Text text=value.GetComponent<TMP_Text>();text.fontSize=size;text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.enableAutoSizing=false;text.raycastTarget=false;return text;}
        private static Button MakeButton(string name,RectTransform parent,Vector2 min,Vector2 max){GameObject value=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));value.transform.SetParent(parent,false);RectTransform rect=value.GetComponent<RectTransform>();rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;value.GetComponent<Image>().color=new Color(.11f,.2f,.32f,1f);Label("Label",rect,Vector2.zero,Vector2.one,24f);return value.GetComponent<Button>();}
        private static void ConfigureButton(Button button,string text,bool visible){if(button==null)return;button.gameObject.SetActive(visible);button.interactable=visible;TMP_Text label=button.GetComponentInChildren<TMP_Text>(true);if(label!=null)label.text=text;}
        private void HandleLanguageChanged(string _){if(IsVisible)RefreshLocalizedContent();}
        private void RefreshLocalizedContent()
        {
            if(heading==null||body==null)return;
            heading.text=L("error.recoverable_title");
            body.text=L(MessageKey(visibleKind))+(visibleRetainedInput?"\n"+L("error.session_safe"):string.Empty);
            ConfigureButton(retryButton,L("common.retry"),retryAction!=null);
            ConfigureButton(dismissButton,L("common.dismiss"),dismissAction!=null);
        }
        private static string L(string key)=>ThinkingSpaceLocalizationController.Get(key);
    }
}

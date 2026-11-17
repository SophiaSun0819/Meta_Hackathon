using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI;
using ThinkingSpace.UI.DesignKit;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbySubtitleView : MonoBehaviour
    {
        private const int TargetCharacters=72;
        private const float MinPanelWidth=340f,MaxPanelWidth=610f,MinPanelHeight=112f,MaxPanelHeight=190f;
        private static readonly Regex TtsOnlyTag=new Regex(@"\[(laughs|curious|excited|sighs|whispers|clears throat|mischievously|snorts)\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        private readonly List<string> segments=new List<string>();
        private Canvas canvas;private RectTransform panelRect;private TMP_Text text;private float startedAt,duration;private int segmentIndex;
        public bool UsesDesignKitSpeechBubble=>GetComponent<QuestUIOrbySpeechBubble>()!=null;
        public int SubtitleTextSurfaceCount=>text==null?0:1;
        public Vector2 CurrentPanelSize=>panelRect!=null?panelRect.sizeDelta:Vector2.zero;
        private void Awake(){Build();Hide();}
        public void Show(string value,bool enabled,float clipDuration)
        {if(!enabled||string.IsNullOrWhiteSpace(value)){Hide();return;}segments.Clear();segments.AddRange(BuildSubtitleSegments(value.Trim()));if(segments.Count==0){Hide();return;}duration=Mathf.Max(.1f,clipDuration);startedAt=Time.unscaledTime;segmentIndex=-1;canvas.gameObject.SetActive(true);Refresh(0);}
        public void Hide(){segments.Clear();if(canvas!=null)canvas.gameObject.SetActive(false);}
        private void LateUpdate(){if(canvas==null||!canvas.gameObject.activeSelf)return;Transform camera=Camera.main!=null?Camera.main.transform:null;if(camera!=null)transform.rotation=Quaternion.LookRotation(transform.position-camera.position,Vector3.up);int next=Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01((Time.unscaledTime-startedAt)/duration)*segments.Count),0,segments.Count-1);if(next!=segmentIndex)Refresh(next);}
        private void Refresh(int index)
        {
            segmentIndex=index;text.text=segments[index];text.font=ResolveFontForText(text.text);
            ResizeForContent();
        }
        private void ResizeForContent()
        {
            if(panelRect==null||text==null)return;
            text.ForceMeshUpdate();
            Vector2 preferred=text.GetPreferredValues(text.text,430f,0f);
            float width=Mathf.Clamp(preferred.x+166f,MinPanelWidth,MaxPanelWidth);
            float height=Mathf.Clamp(preferred.y+58f,MinPanelHeight,MaxPanelHeight);
            panelRect.sizeDelta=new Vector2(width,height);
        }
        public static List<string> BuildSubtitleSegments(string source){var result=new List<string>();if(string.IsNullOrWhiteSpace(source))return result;string[] words=source.Split(' ');string current="";foreach(string word in words){if(string.IsNullOrEmpty(word))continue;string candidate=string.IsNullOrEmpty(current)?word:current+" "+word;if(candidate.Length>TargetCharacters&&!string.IsNullOrEmpty(current)){result.Add(current);current=word;}else current=candidate;}if(!string.IsNullOrEmpty(current))result.Add(current);return result;}
        public static string StripTtsOnlyTags(string source)
        {if(string.IsNullOrEmpty(source))return source??string.Empty;return Regex.Replace(TtsOnlyTag.Replace(source,string.Empty),@"\s+"," ").Trim();}
        public static string FontResourcePathForText(string source)
        {
            if(string.IsNullOrEmpty(source))return null;
            for(int i=0;i<source.Length;i++)
            {
                int code=source[i];
                if((code>=0x0900&&code<=0x097F)||(code>=0xA8E0&&code<=0xA8FF))
                    return "Fonts/NotoSansDevanagari-Regular SDF";
                if((code>=0x3400&&code<=0x4DBF)||(code>=0x4E00&&code<=0x9FFF)
                    ||(code>=0xF900&&code<=0xFAFF))
                    return "Fonts/NotoSansCJKsc-Regular SDF";
            }
            return null;
        }
        private static TMP_FontAsset ResolveFontForText(string source)
        {
            string path=FontResourcePathForText(source);
            return string.IsNullOrEmpty(path)?TMP_Settings.defaultFontAsset:
                Resources.Load<TMP_FontAsset>(path)??TMP_Settings.defaultFontAsset;
        }
        private void Build()
        {
            canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            canvas.overrideSorting=true;canvas.sortingOrder=90;
            panelRect=GetComponent<RectTransform>();panelRect.sizeDelta=new Vector2(MinPanelWidth,MinPanelHeight);
            panelRect.localScale=Vector3.one*.00075f;
            Image background=gameObject.AddComponent<Image>();background.color=QuestUIDesignTokens.Panel;
            Sprite rounded=FindFirstObjectByType<QuestUIActiveMenuSkin>()?.RoundedPanelSprite;
            if(rounded!=null){background.sprite=rounded;background.type=Image.Type.Sliced;}
            background.raycastTarget=false;
            gameObject.AddComponent<CompactPanel>();
            gameObject.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.OrbySubtitle);
            TMP_Text avatar=MakeText("Orby Avatar",panelRect,48,new Vector2(.035f,.18f),new Vector2(.20f,.82f));
            avatar.text="●";avatar.color=QuestUIDesignTokens.Cyan;
            text=MakeText("Subtitle",panelRect,24,new Vector2(.22f,.14f),new Vector2(.95f,.86f));
            text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.Normal;
            text.enableAutoSizing=true;text.fontSizeMin=22;text.fontSizeMax=24;
            gameObject.AddComponent<QuestUIOrbySpeechBubble>().Configure(text,false);
            ThinkingSpaceUIStyler.StylePanel(gameObject,false,false);
            background.color=QuestUIDesignTokens.Panel;
        }
        private static TMP_Text MakeText(string name,Transform parent,float size,Vector2 min,Vector2 max){GameObject go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);RectTransform r=go.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;TMP_Text value=go.AddComponent<TextMeshProUGUI>();value.font=TMP_Settings.defaultFontAsset;value.fontSize=size;value.enableAutoSizing=false;value.alignment=TextAlignmentOptions.Center;value.color=Color.white;value.raycastTarget=false;return value;}
    }
}

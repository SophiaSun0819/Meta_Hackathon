using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbySubtitleView : MonoBehaviour
    {
        private const int TargetCharacters=88;
        private static readonly Regex TtsOnlyTag=new Regex(@"\[(laughs|curious|excited|sighs|whispers|clears throat|mischievously|snorts)\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        private readonly List<string> segments=new List<string>();
        private Canvas canvas;private TMP_Text text,timingLabel;private float startedAt,duration;private int segmentIndex;
        private void Awake(){Build();Hide();}
        public void Show(string value,bool enabled,float clipDuration)
        {if(!enabled||string.IsNullOrWhiteSpace(value)){Hide();return;}segments.Clear();segments.AddRange(BuildSubtitleSegments(value.Trim()));if(segments.Count==0){Hide();return;}duration=Mathf.Max(.1f,clipDuration);startedAt=Time.unscaledTime;segmentIndex=-1;canvas.gameObject.SetActive(true);Refresh(0);}
        public void Hide(){segments.Clear();if(canvas!=null)canvas.gameObject.SetActive(false);}
        private void LateUpdate(){if(canvas==null||!canvas.gameObject.activeSelf)return;Transform camera=Camera.main!=null?Camera.main.transform:null;if(camera!=null)transform.rotation=Quaternion.LookRotation(transform.position-camera.position,Vector3.up);int next=Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01((Time.unscaledTime-startedAt)/duration)*segments.Count),0,segments.Count-1);if(next!=segmentIndex)Refresh(next);}
        private void Refresh(int index){segmentIndex=index;text.text=segments[index];timingLabel.text=ThinkingSpaceLocalizationController.Get("subtitle.timing",index+1,segments.Count);}
        public static List<string> BuildSubtitleSegments(string source){var result=new List<string>();if(string.IsNullOrWhiteSpace(source))return result;string[] words=source.Split(' ');string current="";foreach(string word in words){if(string.IsNullOrEmpty(word))continue;string candidate=string.IsNullOrEmpty(current)?word:current+" "+word;if(candidate.Length>TargetCharacters&&!string.IsNullOrEmpty(current)){result.Add(current);current=word;}else current=candidate;}if(!string.IsNullOrEmpty(current))result.Add(current);return result;}
        public static string StripTtsOnlyTags(string source)
        {if(string.IsNullOrEmpty(source))return source??string.Empty;return Regex.Replace(TtsOnlyTag.Replace(source,string.Empty),@"\s+"," ").Trim();}
        private void Build(){canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;canvas.sortingOrder=90;RectTransform root=GetComponent<RectTransform>();root.sizeDelta=new Vector2(620,145);root.localScale=Vector3.one*.0005f;Image background=gameObject.AddComponent<Image>();background.color=new Color(.02f,.035f,.06f,.9f);background.raycastTarget=false;text=MakeText("Subtitle",root,30,new Vector2(.05f,.22f),new Vector2(.95f,.94f));text.textWrappingMode=TextWrappingModes.Normal;timingLabel=MakeText("Synchronization",root,15,new Vector2(.05f,.03f),new Vector2(.95f,.20f));timingLabel.color=new Color(.65f,.75f,.85f,1);}
        private static TMP_Text MakeText(string name,Transform parent,float size,Vector2 min,Vector2 max){GameObject go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);RectTransform r=go.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;TMP_Text value=go.AddComponent<TextMeshProUGUI>();value.font=TMP_Settings.defaultFontAsset;value.fontSize=size;value.enableAutoSizing=false;value.alignment=TextAlignmentOptions.Center;value.color=Color.white;value.raycastTarget=false;return value;}
    }
}

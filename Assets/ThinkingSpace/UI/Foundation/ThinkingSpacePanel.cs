using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public enum ThinkingSpacePanelKind { Base, Compact, Modal }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public class BasePanel : MonoBehaviour
    {
        [SerializeField] private ThinkingSpacePanelKind panelKind = ThinkingSpacePanelKind.Base;
        [SerializeField] private ThinkingSpaceUITheme theme;
        [SerializeField] private bool showDecorativeFrame = true;
        [SerializeField] private bool backgroundReceivesRaycasts = true;
        private readonly Image[] borderImages = new Image[4];
        public ThinkingSpacePanelKind PanelKind=>panelKind;
        public ThinkingSpaceUITheme Theme=>theme!=null?theme:ThinkingSpaceUITheme.LoadDefault();
        protected virtual void Awake()=>ApplyVisuals();
        protected void SetPanelKind(ThinkingSpacePanelKind value)=>panelKind=value;
        public void SetTheme(ThinkingSpaceUITheme value){theme=value;ApplyVisuals();}

        public virtual void ApplyVisuals()
        {
            Image background=GetComponent<Image>();ThinkingSpaceUITheme resolved=Theme;
            background.raycastTarget=backgroundReceivesRaycasts;
            background.sprite=null;background.type=Image.Type.Simple;background.preserveAspect=false;
            Color opaque=resolved!=null?resolved.PanelColor:new Color(.025f,.045f,.075f,.96f);
            opaque.a=Mathf.Clamp(opaque.a,.92f,.96f);background.color=opaque;
            EnsureDecoration(resolved);
            if(showDecorativeFrame)EnsureBorder(resolved);
        }

        public void Configure(bool decorativeFrame,bool receivesRaycasts)
        {showDecorativeFrame=decorativeFrame;backgroundReceivesRaycasts=receivesRaycasts;ApplyVisuals();}

        private void EnsureDecoration(ThinkingSpaceUITheme resolved)
        {
            RectTransform parent=transform as RectTransform;if(parent==null)return;
            Transform existing=parent.Find("SlimUI Decoration");
            GameObject child=existing!=null?existing.gameObject:new GameObject("SlimUI Decoration",typeof(RectTransform),typeof(Image));
            child.transform.SetParent(parent,false);child.transform.SetAsFirstSibling();
            RectTransform rect=child.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;
            Image image=child.GetComponent<Image>();image.sprite=resolved!=null?resolved.PanelBackground:null;
            image.type=Image.Type.Simple;image.preserveAspect=false;image.color=Color.white;image.raycastTarget=false;
        }

        private void EnsureBorder(ThinkingSpaceUITheme resolved)
        {
            RectTransform parent=transform as RectTransform;if(parent==null)return;
            Color color=resolved!=null?resolved.BorderColor:new Color(.08f,.42f,.62f,.82f);
            CreateEdge(0,"Top",parent,new Vector2(0f,.975f),Vector2.one,color);
            CreateEdge(1,"Bottom",parent,Vector2.zero,new Vector2(1f,.025f),color);
            CreateEdge(2,"Left",parent,new Vector2(0f,.025f),new Vector2(.018f,.975f),color);
            CreateEdge(3,"Right",parent,new Vector2(.982f,.025f),new Vector2(1f,.975f),color);
        }

        private void CreateEdge(int index,string edgeName,RectTransform parent,Vector2 min,Vector2 max,Color color)
        {
            if(borderImages[index]==null)
            {
                Transform existing=parent.Find("UI Frame "+edgeName);
                GameObject edge=existing!=null?existing.gameObject:new GameObject("UI Frame "+edgeName,typeof(RectTransform),typeof(Image));
                edge.transform.SetParent(parent,false);borderImages[index]=edge.GetComponent<Image>();
            }
            RectTransform rect=borderImages[index].rectTransform;rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;
            borderImages[index].sprite=null;borderImages[index].color=color;borderImages[index].raycastTarget=false;
        }
    }

    public sealed class CompactPanel : BasePanel
    { protected override void Awake(){SetPanelKind(ThinkingSpacePanelKind.Compact);base.Awake();} }

    public sealed class ModalPanel : BasePanel
    { protected override void Awake(){SetPanelKind(ThinkingSpacePanelKind.Modal);base.Awake();} }
}

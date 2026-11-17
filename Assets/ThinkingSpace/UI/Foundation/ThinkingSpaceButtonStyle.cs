using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public enum ThinkingSpaceButtonKind { Primary, Secondary, Icon, Navigation }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform),typeof(Image),typeof(Button))]
    public class ThinkingSpaceButtonStyle : MonoBehaviour
    {
        [SerializeField] private ThinkingSpaceButtonKind buttonKind=ThinkingSpaceButtonKind.Primary;
        [SerializeField] private ThinkingSpaceUITheme theme;
        [SerializeField] private TMP_Text label;
        [SerializeField] private string localizationKey;
        private UnityAction registeredAction;
        public ThinkingSpaceButtonKind ButtonKind=>buttonKind;
        public Button Button=>GetComponent<Button>();
        public TMP_Text Label=>label!=null?label:label=GetComponentInChildren<TMP_Text>(true);
        protected virtual void Awake()=>ApplyVisuals();
        protected virtual void OnEnable(){ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=HandleLanguageChanged;RefreshLocalizedLabel();}
        protected virtual void OnDisable()=>ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=HandleLanguageChanged;
        protected void SetButtonKind(ThinkingSpaceButtonKind value)=>buttonKind=value;
        public void SetTheme(ThinkingSpaceUITheme value){theme=value;ApplyVisuals();}
        public void ConfigureLocalization(string key){localizationKey=key;RefreshLocalizedLabel();}
        public void ConfigureLabel(TMP_Text value){label=value;ApplyVisuals();RefreshLocalizedLabel();}

        public void SetSelected(bool selected)
        {
            ThinkingSpaceUITheme resolved=theme!=null?theme:ThinkingSpaceUITheme.LoadDefault();
            GetComponent<Image>().color=selected&&resolved!=null?resolved.SelectedColor:BaseColor(resolved);
        }

        public void SetAction(UnityAction action)
        {
            Button button=Button;if(registeredAction!=null)button.onClick.RemoveListener(registeredAction);
            registeredAction=action;if(registeredAction!=null)button.onClick.AddListener(registeredAction);
        }

        public virtual void ApplyVisuals()
        {
            ThinkingSpaceUITheme resolved=theme!=null?theme:ThinkingSpaceUITheme.LoadDefault();
            Image image=GetComponent<Image>();Button button=Button;
            image.raycastTarget=true;image.sprite=SafeSprite(resolved);image.color=BaseColor(resolved);
            bool sliced=image.sprite!=null&&image.sprite.border.sqrMagnitude>0f;
            image.type=sliced?Image.Type.Sliced:Image.Type.Simple;image.preserveAspect=!sliced&&image.sprite!=null;
            button.targetGraphic=image;button.transition=image.sprite!=null?Selectable.Transition.SpriteSwap:Selectable.Transition.ColorTint;
            SpriteState sprites=button.spriteState;
            sprites.highlightedSprite=resolved!=null?resolved.ButtonHover:null;
            sprites.pressedSprite=resolved!=null?resolved.ButtonPressed:null;
            sprites.selectedSprite=resolved!=null?resolved.ButtonSelected:null;
            sprites.disabledSprite=image.sprite;button.spriteState=sprites;
            ColorBlock colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.82f,.96f,1f,1f);
            colors.pressedColor=new Color(.62f,.82f,.9f,1f);colors.selectedColor=resolved!=null?resolved.SelectedColor:new Color(1f,.62f,.16f,1f);
            colors.disabledColor=resolved!=null?resolved.DisabledColor:new Color(.18f,.21f,.25f,.72f);colors.colorMultiplier=1f;colors.fadeDuration=.08f;button.colors=colors;
            LayoutElement layout=GetComponent<LayoutElement>();if(layout!=null)layout.minHeight=resolved!=null?resolved.MinimumHitHeight:72f;
            TMP_Text text=Label;if(text==null)return;text.color=resolved!=null?resolved.TextColor:Color.white;text.raycastTarget=false;
            text.enableAutoSizing=true;text.fontSizeMin=18f;text.fontSizeMax=30f;text.margin=new Vector4(18f,8f,18f,8f);
        }

        public void RefreshLocalizedLabel()
        {TMP_Text text=Label;if(text!=null&&!string.IsNullOrWhiteSpace(localizationKey))text.text=ThinkingSpaceLocalizationController.Get(localizationKey);}
        private void HandleLanguageChanged(string _)=>RefreshLocalizedLabel();
        private Color BaseColor(ThinkingSpaceUITheme resolved)
        {
            if(resolved==null)return buttonKind==ThinkingSpaceButtonKind.Primary?new Color(.08f,.52f,.72f,1f):new Color(.11f,.2f,.32f,1f);
            return buttonKind==ThinkingSpaceButtonKind.Primary?resolved.PrimaryColor:resolved.SecondaryColor;
        }
        private Sprite SafeSprite(ThinkingSpaceUITheme resolved)
        {
            if(resolved==null)return null;RectTransform rect=transform as RectTransform;
            float width=rect!=null?Mathf.Abs(rect.rect.width):0f,height=rect!=null?Mathf.Abs(rect.rect.height):0f;
            bool square=width>0f&&height>0f&&width/height>=.75f&&width/height<=1.33f;
            if(buttonKind==ThinkingSpaceButtonKind.Navigation&&square&&resolved.NavigationArrow!=null)return resolved.NavigationArrow;
            return resolved.ButtonNormal;
        }
    }

    public sealed class PrimaryButton : ThinkingSpaceButtonStyle
    { protected override void Awake(){SetButtonKind(ThinkingSpaceButtonKind.Primary);base.Awake();} }
    public sealed class SecondaryButton : ThinkingSpaceButtonStyle
    { protected override void Awake(){SetButtonKind(ThinkingSpaceButtonKind.Secondary);base.Awake();} }
    public sealed class IconButton : ThinkingSpaceButtonStyle
    { protected override void Awake(){SetButtonKind(ThinkingSpaceButtonKind.Icon);base.Awake();} }
    public sealed class NavigationButton : ThinkingSpaceButtonStyle
    { protected override void Awake(){SetButtonKind(ThinkingSpaceButtonKind.Navigation);base.Awake();} }
}

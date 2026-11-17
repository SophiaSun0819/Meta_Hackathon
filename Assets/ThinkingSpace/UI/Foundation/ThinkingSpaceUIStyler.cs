using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public static class ThinkingSpaceUIStyler
    {
        public static BasePanel StylePanel(GameObject root,bool decorativeFrame=true,bool receivesRaycasts=true)
        {
            if(root==null)return null;
            if(root.GetComponent<RectTransform>()==null)root.AddComponent<RectTransform>();
            if(root.GetComponent<Image>()==null)root.AddComponent<Image>();
            BasePanel panel=root.GetComponent<BasePanel>();if(panel==null)panel=root.AddComponent<BasePanel>();
            panel.Configure(decorativeFrame,receivesRaycasts);return panel;
        }

        public static ThinkingSpaceButtonStyle StyleButton(Button button,ThinkingSpaceButtonKind kind=ThinkingSpaceButtonKind.Primary)
        {
            if(button==null)return null;ThinkingSpaceButtonStyle style=button.GetComponent<ThinkingSpaceButtonStyle>();
            if(style==null)
            {
                style=kind==ThinkingSpaceButtonKind.Secondary?button.gameObject.AddComponent<SecondaryButton>()
                    :kind==ThinkingSpaceButtonKind.Icon?button.gameObject.AddComponent<IconButton>()
                    :kind==ThinkingSpaceButtonKind.Navigation?button.gameObject.AddComponent<NavigationButton>()
                    :button.gameObject.AddComponent<PrimaryButton>();
            }
            style.ApplyVisuals();return style;
        }

        public static void StyleHierarchy(GameObject root)
        {
            if(root==null)return;
            Button[] buttons=root.GetComponentsInChildren<Button>(true);
            for(int i=0;i<buttons.Length;i++)
            {
                string name=buttons[i].name.ToLowerInvariant();
                ThinkingSpaceButtonKind kind=name.Contains("previous")||name.Contains("next")||name.Contains("back")
                    ?ThinkingSpaceButtonKind.Navigation:ThinkingSpaceButtonKind.Primary;
                StyleButton(buttons[i],kind);
            }
            TMP_Text[] labels=root.GetComponentsInChildren<TMP_Text>(true);
            for(int i=0;i<labels.Length;i++){labels[i].raycastTarget=false;labels[i].enableAutoSizing=true;labels[i].fontSizeMin=Mathf.Min(labels[i].fontSize,18f);}
        }
    }
}

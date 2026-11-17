using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.UI.DesignKit;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class PalmMenuButtonView : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private GraphicRaycaster graphicRaycaster;
        [SerializeField] private PointableCanvas pointableCanvas;
        [SerializeField] private PlaneSurface planeSurface;
        [SerializeField] private BoundsClipper boundsClipper;
        [SerializeField] private ClippedPlaneSurface clippedPlaneSurface;
        [SerializeField] private RayInteractable rayInteractable;
        [SerializeField] private PokeInteractable pokeInteractable;
        [SerializeField] private Button menuButton;
        [SerializeField] private Image buttonImage;
        [SerializeField] private TextMeshProUGUI label;

        public Canvas WorldCanvas => worldCanvas;
        public GraphicRaycaster GraphicRaycaster => graphicRaycaster;
        public Button MenuButton => menuButton;
        public Image ButtonImage => buttonImage;
        public TextMeshProUGUI Label => label;
        public PointableCanvas PointableCanvas => pointableCanvas;
        public RayInteractable RayInteractable => rayInteractable;
        public PokeInteractable PokeInteractable => pokeInteractable;
        public bool IsEligible { get; private set; }

        public event Action<PointerEventData> UnityPointerDown;
        public event Action<PointerEventData> UnityPointerUp;
        public event Action<PointerEventData> UnityPointerClick;
        public bool InteractionComponentsValid => worldCanvas != null && graphicRaycaster != null
            && pointableCanvas != null && planeSurface != null && boundsClipper != null
            && clippedPlaneSurface != null && rayInteractable != null && pokeInteractable != null
            && menuButton != null && buttonImage != null && label != null;

        private void Awake()
        {
            ThinkingSpaceUIStyler.StyleButton(menuButton,ThinkingSpaceButtonKind.Icon);
            if(buttonImage!=null)
            {
                buttonImage.color=QuestUIDesignTokens.Panel;
                Outline outline=buttonImage.GetComponent<Outline>()??buttonImage.gameObject.AddComponent<Outline>();
                outline.effectColor=new Color(QuestUIDesignTokens.Cyan.r,QuestUIDesignTokens.Cyan.g,
                    QuestUIDesignTokens.Cyan.b,.85f);
                outline.effectDistance=new Vector2(2f,-2f);
                outline.useGraphicAlpha=true;
            }
            if(label!=null){label.color=QuestUIDesignTokens.Text;label.fontSize=Mathf.Max(22f,label.fontSize);}
            if(label!=null)label.raycastTarget=false;
        }

        public void SetEligibility(bool eligible)
        {
            IsEligible = eligible;
            if (eligible && pointableCanvas != null) pointableCanvas.enabled = true;
            if (worldCanvas != null) worldCanvas.enabled = eligible;
            if (graphicRaycaster != null) graphicRaycaster.enabled = eligible;
            if (buttonImage != null) buttonImage.raycastTarget = eligible;
            if (menuButton != null) menuButton.interactable = eligible;
            if (rayInteractable != null) rayInteractable.enabled = eligible;
            if (pokeInteractable != null) pokeInteractable.enabled = eligible;
            if (!eligible && pointableCanvas != null) pointableCanvas.enabled = false;
        }

        public void OnPointerDown(PointerEventData eventData) => UnityPointerDown?.Invoke(eventData);
        public void OnPointerUp(PointerEventData eventData) => UnityPointerUp?.Invoke(eventData);
        public void OnPointerClick(PointerEventData eventData) => UnityPointerClick?.Invoke(eventData);
    }
}

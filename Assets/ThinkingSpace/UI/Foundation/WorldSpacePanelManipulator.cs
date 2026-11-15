using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldSpacePanelManipulator : MonoBehaviour, IPointerEnterHandler,
        IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler,
        IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform panel;
        [SerializeField, Min(.001f)] private float minimumWorldTravel=.008f;
        [SerializeField] private Color idleColor=new Color(.18f,.62f,.78f,.48f);
        [SerializeField] private Color hoverColor=new Color(.3f,.88f,1f,.82f);
        [SerializeField] private Color draggingColor=new Color(1f,.66f,.2f,.92f);
        private Image visual;
        private Vector3 pressWorldPoint,pressPanelPosition;
        private bool hasPressPoint,dragCandidate,dragging;
        public bool IsDragging=>dragging;
        public bool HasManualPlacement{get;private set;}
        public event Action DragStarted;
        public event Action DragEnded;

        public static WorldSpacePanelManipulator Attach(RectTransform target)
        {
            if(target==null)return null;
            Transform existing=target.Find("Panel Grab Handle");
            GameObject handle=existing!=null?existing.gameObject:new GameObject("Panel Grab Handle",
                typeof(RectTransform),typeof(Image),typeof(WorldSpacePanelManipulator));
            handle.transform.SetParent(target,false);
            RectTransform rect=handle.GetComponent<RectTransform>();
            rect.anchorMin=new Vector2(.36f,.925f);rect.anchorMax=new Vector2(.64f,.985f);
            rect.offsetMin=rect.offsetMax=Vector2.zero;handle.transform.SetAsLastSibling();
            WorldSpacePanelManipulator result=handle.GetComponent<WorldSpacePanelManipulator>();
            result.panel=target;result.ConfigureVisual();return result;
        }

        private void Awake(){if(panel==null)panel=transform.parent as RectTransform;ConfigureVisual();}
        private void OnDisable(){FinishDrag();dragCandidate=false;hasPressPoint=false;}
        public void ResetManualPlacement(){HasManualPlacement=false;}

        public void OnPointerEnter(PointerEventData _){SetColor(dragging?draggingColor:hoverColor);}
        public void OnPointerExit(PointerEventData _){if(!dragging)SetColor(idleColor);}
        public void OnPointerDown(PointerEventData eventData)
        {
            dragCandidate=TryWorldPoint(eventData,out pressWorldPoint);hasPressPoint=dragCandidate;
            pressPanelPosition=panel!=null?panel.position:Vector3.zero;
        }
        public void OnPointerUp(PointerEventData _){if(!dragging){dragCandidate=false;hasPressPoint=false;}}
        public void OnBeginDrag(PointerEventData eventData)
        {
            if(!hasPressPoint&&TryWorldPoint(eventData,out pressWorldPoint))
            {hasPressPoint=true;pressPanelPosition=panel!=null?panel.position:Vector3.zero;}
            dragCandidate=hasPressPoint&&panel!=null;
        }
        public void OnDrag(PointerEventData eventData)
        {
            if(!dragCandidate||panel==null||!TryWorldPoint(eventData,out Vector3 current))return;
            Vector3 delta=current-pressWorldPoint;
            if(!dragging)
            {
                if(delta.sqrMagnitude<minimumWorldTravel*minimumWorldTravel)return;
                dragging=true;HasManualPlacement=true;SetColor(draggingColor);DragStarted?.Invoke();
            }
            panel.position=pressPanelPosition+delta;
        }
        public void OnEndDrag(PointerEventData _){FinishDrag();dragCandidate=false;hasPressPoint=false;}

        private void FinishDrag()
        {
            if(!dragging)return;dragging=false;SetColor(idleColor);DragEnded?.Invoke();
        }
        private void ConfigureVisual()
        {
            visual=GetComponent<Image>();if(visual==null)return;
            ThinkingSpaceUITheme theme=ThinkingSpaceUITheme.LoadDefault();
            visual.sprite=theme!=null?theme.ButtonNormal:null;
            visual.type=visual.sprite!=null&&visual.sprite.border.sqrMagnitude>0f?Image.Type.Sliced:Image.Type.Simple;
            visual.preserveAspect=visual.type==Image.Type.Simple;visual.raycastTarget=true;SetColor(idleColor);
        }
        private void SetColor(Color value){if(visual!=null)visual.color=value;}
        private static bool TryWorldPoint(PointerEventData eventData,out Vector3 point)
        {
            point=Vector3.zero;if(eventData==null)return false;
            RaycastResult current=eventData.pointerCurrentRaycast;
            if(current.gameObject!=null){point=current.worldPosition;return true;}
            RaycastResult press=eventData.pointerPressRaycast;
            if(press.gameObject!=null){point=press.worldPosition;return true;}
            return false;
        }
    }
}

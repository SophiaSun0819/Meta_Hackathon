using System;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldSpacePanelManipulator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private Color idleColor=new Color(.18f,.62f,.78f,.72f);
        [SerializeField] private Color hoverColor=new Color(.3f,.95f,1f,1f);
        [SerializeField] private Color draggingColor=new Color(.2f,1f,.45f,1f);
        private Image visual;private Grabbable grabbable;private bool dragging,subscribed;
        public bool IsDragging=>dragging;
        public bool HasManualPlacement{get;private set;}
        public event Action DragStarted;public event Action DragEnded;

        public static WorldSpacePanelManipulator Attach(RectTransform target)
        {
            if(target==null)return null;
            Transform existing=target.Find("Panel Grab Handle");
            GameObject handle=existing!=null?existing.gameObject:new GameObject("Panel Grab Handle",
                typeof(RectTransform),typeof(Image),typeof(BoxCollider),typeof(WorldSpacePanelManipulator));
            handle.transform.SetParent(target,false);
            RectTransform rect=handle.GetComponent<RectTransform>();
            rect.anchorMin=new Vector2(.075f,.875f);rect.anchorMax=new Vector2(.925f,.985f);
            rect.offsetMin=rect.offsetMax=Vector2.zero;handle.transform.SetAsLastSibling();
            WorldSpacePanelManipulator result=handle.GetComponent<WorldSpacePanelManipulator>();
            result.panel=target;result.ConfigureVisual();result.ConfigureMetaGrab();return result;
        }

        private void Awake(){if(panel==null)panel=transform.parent as RectTransform;ConfigureVisual();ConfigureMetaGrab();}
        private void OnEnable()=>Subscribe();
        private void OnDisable(){Unsubscribe();FinishDrag();}
        public void ResetManualPlacement(){HasManualPlacement=false;}
        public void OnPointerEnter(PointerEventData _){if(!dragging)SetColor(hoverColor);}
        public void OnPointerExit(PointerEventData _){if(!dragging)SetColor(idleColor);}

        private void ConfigureVisual()
        {
            visual=GetComponent<Image>();if(visual==null)return;
            ThinkingSpaceUITheme theme=ThinkingSpaceUITheme.LoadDefault();visual.sprite=theme!=null?theme.ButtonNormal:null;
            visual.type=visual.sprite!=null&&visual.sprite.border.sqrMagnitude>0f?Image.Type.Sliced:Image.Type.Simple;
            visual.preserveAspect=false;visual.raycastTarget=true;SetColor(idleColor);
        }

        private void ConfigureMetaGrab()
        {
            if(panel==null)return;
            Rigidbody body=panel.GetComponent<Rigidbody>();if(body==null)body=panel.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            grabbable=panel.GetComponent<Grabbable>();if(grabbable==null)grabbable=panel.gameObject.AddComponent<Grabbable>();
            StablePanelGrabTransformer transformer=panel.GetComponent<StablePanelGrabTransformer>();if(transformer==null)transformer=panel.gameObject.AddComponent<StablePanelGrabTransformer>();
            grabbable.InjectOptionalTargetTransform(panel);grabbable.InjectOptionalRigidbody(body);grabbable.InjectOptionalThrowWhenUnselected(false);grabbable.MaxGrabPoints=1;grabbable.InjectOptionalOneGrabTransformer(transformer);
            BoxCollider collider=GetComponent<BoxCollider>();RectTransform rect=transform as RectTransform;
            if(collider!=null&&rect!=null){collider.isTrigger=true;collider.size=new Vector3(rect.rect.width,rect.rect.height,40f);}
            HandGrabInteractable near=GetComponent<HandGrabInteractable>();if(near==null)near=gameObject.AddComponent<HandGrabInteractable>();
            near.InjectAllHandGrabInteractable(GrabTypeFlags.Pinch,body,GrabbingRule.DefaultPinchRule,GrabbingRule.DefaultPalmRule);near.InjectOptionalPointableElement(grabbable);
            DistanceGrabInteractable far=GetComponent<DistanceGrabInteractable>();if(far==null)far=gameObject.AddComponent<DistanceGrabInteractable>();
            far.InjectAllGrabInteractable(body);far.InjectOptionalGrabSource(transform);far.InjectOptionalPointableElement(grabbable);
            Subscribe();
        }

        private void Subscribe(){if(!isActiveAndEnabled||grabbable==null||subscribed)return;grabbable.WhenPointerEventRaised+=HandleGrabEvent;subscribed=true;}
        private void Unsubscribe(){if(grabbable==null||!subscribed)return;grabbable.WhenPointerEventRaised-=HandleGrabEvent;subscribed=false;}

        private void HandleGrabEvent(PointerEvent evt)
        {
            if(evt.Type==PointerEventType.Select&&!dragging){dragging=true;HasManualPlacement=true;SetColor(draggingColor);DragStarted?.Invoke();}
            else if((evt.Type==PointerEventType.Unselect||evt.Type==PointerEventType.Cancel)&&dragging)FinishDrag();
            else if(evt.Type==PointerEventType.Hover&&!dragging)SetColor(hoverColor);else if(evt.Type==PointerEventType.Unhover&&!dragging)SetColor(idleColor);
        }
        private void FinishDrag(){if(!dragging)return;dragging=false;SetColor(idleColor);DragEnded?.Invoke();}
        private void SetColor(Color value){if(visual!=null)visual.color=value;}
    }

    [DisallowMultipleComponent]
    public sealed class StablePanelGrabTransformer : MonoBehaviour,ITransformer
    {
        [SerializeField] private float minimumHeadDistance=.35f,maximumHeadDistance=1.5f,smoothing=.035f;
        private IGrabbable source;private Vector3 localOffset,velocity;private Quaternion lockedRotation;
        public void Initialize(IGrabbable value)=>source=value;
        public void BeginTransform(){if(source==null||source.GrabPoints.Count==0)return;Transform t=source.Transform;lockedRotation=t.rotation;localOffset=t.position-source.GrabPoints[0].position;velocity=Vector3.zero;}
        public void UpdateTransform(){if(source==null||source.GrabPoints.Count==0)return;Transform t=source.Transform;Vector3 target=source.GrabPoints[0].position+localOffset;Camera camera=Camera.main;if(camera!=null){Vector3 delta=target-camera.transform.position;float distance=delta.magnitude;if(distance>.001f)target=camera.transform.position+delta/distance*Mathf.Clamp(distance,minimumHeadDistance,maximumHeadDistance);}t.position=Vector3.SmoothDamp(t.position,target,ref velocity,smoothing,Mathf.Infinity,Time.unscaledDeltaTime);t.rotation=lockedRotation;}
        public void EndTransform(){velocity=Vector3.zero;}
    }
}

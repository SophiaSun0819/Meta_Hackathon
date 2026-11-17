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
        [SerializeField,Range(.12f,.25f)] private float externalProximityWorldRange=.18f;
        private Image visual;private Grabbable grabbable;private bool dragging,subscribed;
        private readonly System.Collections.Generic.List<Image> proximityHighlights=new System.Collections.Generic.List<Image>(4);
        private Vector3 dragStartPosition;
        public bool IsDragging=>dragging;
        public bool HasActiveDragMoved=>dragging&&panel!=null&&Vector3.Distance(dragStartPosition,panel.position)>.008f;
        public float ExternalProximityWorldRange=>externalProximityWorldRange;
        public bool HasExternalProximityZone=>transform.Find("External Proximity Grab Zone")!=null;
        public bool HasDistinctProximityHighlight=>transform.Find("Grab Range Highlight")!=null;
        public bool HasManualPlacement{get;private set;}
        public bool LastDragMoved{get;private set;}
        public event Action DragStarted;public event Action DragEnded;

        public static WorldSpacePanelManipulator Attach(RectTransform target)
        {
            if(target==null)return null;
            Transform existing=target.Find("Panel Grab Handle");
            GameObject handle=existing!=null?existing.gameObject:new GameObject("Panel Grab Handle",
                typeof(RectTransform),typeof(Image),typeof(WorldSpacePanelManipulator));
            handle.transform.SetParent(target,false);
            RectTransform rect=handle.GetComponent<RectTransform>();
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;handle.transform.SetAsLastSibling();
            WorldSpacePanelManipulator result=handle.GetComponent<WorldSpacePanelManipulator>();
            result.panel=target;result.ConfigureVisual();result.ConfigureMetaGrab();return result;
        }

        private void Awake(){if(panel==null)panel=transform.parent as RectTransform;ConfigureVisual();ConfigureMetaGrab();}
        private void OnEnable()=>Subscribe();
        private void OnDisable(){Unsubscribe();FinishDrag();}
        public void ResetManualPlacement(){HasManualPlacement=false;}
        public void OnPointerEnter(PointerEventData _){if(!dragging){SetColor(hoverColor);SetProximityHighlight(true);}}
        public void OnPointerExit(PointerEventData _){if(!dragging){SetColor(idleColor);SetProximityHighlight(false);}}

        public void ConfigureExternalProximityWorldRange(float value)
        {
            externalProximityWorldRange=Mathf.Clamp(value,.12f,.25f);
            if(panel!=null)ConfigureExternalProximityZone(panel.GetComponent<Rigidbody>());
        }

        private void ConfigureVisual()
        {
            visual=GetComponent<Image>();if(visual==null)return;
            visual.sprite=null;visual.type=Image.Type.Simple;
            visual.preserveAspect=false;visual.raycastTarget=false;visual.color=Color.clear;
            Transform existing=transform.Find("Grab Range Highlight");
            GameObject highlight=existing!=null?existing.gameObject:new GameObject("Grab Range Highlight",typeof(RectTransform),typeof(Image));
            highlight.transform.SetParent(transform,false);highlight.transform.SetAsFirstSibling();
            RectTransform highlightRect=highlight.GetComponent<RectTransform>();highlightRect.anchorMin=Vector2.zero;
            highlightRect.anchorMax=Vector2.one;highlightRect.offsetMin=highlightRect.offsetMax=Vector2.zero;
            Image highlightRoot=highlight.GetComponent<Image>();highlightRoot.color=Color.clear;highlightRoot.raycastTarget=false;
            proximityHighlights.Clear();
            ConfigureHighlightEdge(highlight.transform,"Top",new Vector2(0f,.93f),Vector2.one);
            ConfigureHighlightEdge(highlight.transform,"Bottom",Vector2.zero,new Vector2(1f,.04f));
            ConfigureHighlightEdge(highlight.transform,"Left",new Vector2(0f,.04f),new Vector2(.06f,.93f));
            ConfigureHighlightEdge(highlight.transform,"Right",new Vector2(.94f,.04f),new Vector2(1f,.93f));
            SetColor(idleColor);SetProximityHighlight(false);
        }

        private void ConfigureHighlightEdge(Transform parent,string edgeName,Vector2 min,Vector2 max)
        {
            Transform existing=parent.Find(edgeName);
            GameObject edge=existing!=null?existing.gameObject:new GameObject(edgeName,typeof(RectTransform),typeof(Image));
            edge.transform.SetParent(parent,false);RectTransform rect=edge.GetComponent<RectTransform>();
            rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;
            Image image=edge.GetComponent<Image>();image.raycastTarget=false;image.color=Color.clear;
            proximityHighlights.Add(image);
        }

        private void ConfigureMetaGrab()
        {
            if(panel==null)return;
            Rigidbody body=panel.GetComponent<Rigidbody>();if(body==null)body=panel.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            grabbable=panel.GetComponent<Grabbable>();if(grabbable==null)grabbable=panel.gameObject.AddComponent<Grabbable>();
            StablePanelGrabTransformer transformer=panel.GetComponent<StablePanelGrabTransformer>();if(transformer==null)transformer=panel.gameObject.AddComponent<StablePanelGrabTransformer>();
            grabbable.InjectOptionalTargetTransform(panel);grabbable.InjectOptionalRigidbody(body);grabbable.InjectOptionalThrowWhenUnselected(false);grabbable.MaxGrabPoints=1;grabbable.InjectOptionalOneGrabTransformer(transformer);
            ConfigureExternalProximityZone(body);
            Subscribe();
        }

        private void ConfigureExternalProximityZone(Rigidbody body)
        {
            Transform existing=transform.Find("External Proximity Grab Zone");
            GameObject zone=existing!=null?existing.gameObject:new GameObject("External Proximity Grab Zone",typeof(RectTransform),typeof(PanelGrabProximityZone));
            zone.transform.SetParent(transform,false);
            RectTransform zoneRect=zone.GetComponent<RectTransform>();
            float scaleY=Mathf.Max(.00001f,Mathf.Abs(panel.lossyScale.y));
            float localHeight=externalProximityWorldRange/scaleY;
            float scaleX=Mathf.Max(.00001f,Mathf.Abs(panel.lossyScale.x));
            float localWidth=externalProximityWorldRange/scaleX;
            zoneRect.anchorMin=Vector2.zero;zoneRect.anchorMax=Vector2.one;
            zoneRect.offsetMin=zoneRect.offsetMax=Vector2.zero;
            ConfigureGrabEdge(zone.transform,"Top",new Vector2(0f,1f),new Vector2(1f,1f),new Vector2(0f,0f),new Vector2(0f,8f),new Vector2(0f,localHeight),body);
            ConfigureGrabEdge(zone.transform,"Bottom",Vector2.zero,new Vector2(1f,0f),new Vector2(0f,1f),new Vector2(0f,-8f),new Vector2(0f,localHeight),body);
            ConfigureGrabEdge(zone.transform,"Left",Vector2.zero,new Vector2(0f,1f),new Vector2(1f,0f),new Vector2(-8f,0f),new Vector2(localWidth,0f),body);
            ConfigureGrabEdge(zone.transform,"Right",new Vector2(1f,0f),Vector2.one,new Vector2(0f,0f),new Vector2(8f,0f),new Vector2(localWidth,0f),body);
            zone.GetComponent<PanelGrabProximityZone>().Configure(externalProximityWorldRange);
        }

        private void ConfigureGrabEdge(Transform parent,string edgeName,Vector2 anchorMin,Vector2 anchorMax,
            Vector2 pivot,Vector2 position,Vector2 size,Rigidbody body)
        {
            Transform existing=parent.Find(edgeName);
            GameObject edge=existing!=null?existing.gameObject:new GameObject(edgeName,typeof(RectTransform),typeof(BoxCollider));
            edge.transform.SetParent(parent,false);RectTransform rect=edge.GetComponent<RectTransform>();
            rect.anchorMin=anchorMin;rect.anchorMax=anchorMax;rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;
            BoxCollider collider=edge.GetComponent<BoxCollider>();collider.isTrigger=true;
            collider.size=new Vector3(Mathf.Max(1f,rect.rect.width),Mathf.Max(1f,rect.rect.height),40f);
            collider.center=new Vector3(rect.rect.center.x,rect.rect.center.y,0f);
            HandGrabInteractable near=edge.GetComponent<HandGrabInteractable>()??edge.AddComponent<HandGrabInteractable>();
            near.InjectAllHandGrabInteractable(GrabTypeFlags.Pinch,body,GrabbingRule.DefaultPinchRule,GrabbingRule.DefaultPalmRule);
            near.InjectOptionalPointableElement(grabbable);
        }

        private void Subscribe(){if(!isActiveAndEnabled||grabbable==null||subscribed)return;grabbable.WhenPointerEventRaised+=HandleGrabEvent;subscribed=true;}
        private void Unsubscribe(){if(grabbable==null||!subscribed)return;grabbable.WhenPointerEventRaised-=HandleGrabEvent;subscribed=false;}

        private void HandleGrabEvent(PointerEvent evt)
        {
            if(evt.Type==PointerEventType.Select&&!dragging){dragging=true;LastDragMoved=false;dragStartPosition=panel!=null?panel.position:transform.position;HasManualPlacement=true;SetColor(draggingColor);DragStarted?.Invoke();}
            else if((evt.Type==PointerEventType.Unselect||evt.Type==PointerEventType.Cancel)&&dragging)FinishDrag();
            else if(evt.Type==PointerEventType.Hover&&!dragging){SetColor(hoverColor);SetProximityHighlight(true);}
            else if(evt.Type==PointerEventType.Unhover&&!dragging){SetColor(idleColor);SetProximityHighlight(false);}
        }
        private void FinishDrag()
        {
            bool wasDragging=dragging;
            if(wasDragging&&panel!=null)LastDragMoved=Vector3.Distance(dragStartPosition,panel.position)>.008f;
            dragging=false;
            SetColor(idleColor);
            SetProximityHighlight(false);
            if(wasDragging)DragEnded?.Invoke();
        }
        private void SetColor(Color value){}
        private void SetProximityHighlight(bool visible)
        {for(int i=0;i<proximityHighlights.Count;i++)if(proximityHighlights[i]!=null)proximityHighlights[i].color=visible?new Color(.2f,1f,.48f,.72f):Color.clear;}
    }

    [DisallowMultipleComponent]
    public sealed class PanelGrabProximityZone : MonoBehaviour
    {
        [SerializeField,Range(.12f,.25f)] private float worldRange=.18f;
        public float WorldRange=>worldRange;
        public void Configure(float value)=>worldRange=Mathf.Clamp(value,.12f,.25f);
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

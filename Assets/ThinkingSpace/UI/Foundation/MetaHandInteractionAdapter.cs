using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster))]
    public sealed class MetaHandInteractionAdapter : MonoBehaviour
    {
        [SerializeField,Min(.001f)] private float surfaceDepth=.035f;
        [SerializeField,Min(0f)] private float selectionPadding=28f;
        private PointableCanvas pointableCanvas;private PlaneSurface planeSurface;private BoundsClipper boundsClipper;
        private ClippedPlaneSurface clippedPlaneSurface;private RayInteractable rayInteractable;private PokeInteractable pokeInteractable;
        public PointableCanvas PointableCanvas=>pointableCanvas;public RayInteractable RayInteractable=>rayInteractable;public PokeInteractable PokeInteractable=>pokeInteractable;
        public bool IsConfigured=>pointableCanvas!=null&&planeSurface!=null&&boundsClipper!=null&&clippedPlaneSurface!=null&&rayInteractable!=null&&pokeInteractable!=null;
        private void Awake()=>Configure(GetComponent<Canvas>(),GetComponent<RectTransform>());

        public void Configure(Canvas canvas,RectTransform rect)
        {
            if(canvas==null||rect==null)return;
            pointableCanvas=GetOrAdd<PointableCanvas>();pointableCanvas.InjectAllPointableCanvas(canvas);
            planeSurface=GetOrAdd<PlaneSurface>();planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward,false);
            boundsClipper=GetOrAdd<BoundsClipper>();Vector2 size=rect.sizeDelta;
            boundsClipper.Size=CalculateBoundsSize(size,selectionPadding,surfaceDepth);
            clippedPlaneSurface=GetOrAdd<ClippedPlaneSurface>();clippedPlaneSurface.InjectAllClippedPlaneSurface(planeSurface,new List<IBoundsClipper>{boundsClipper});
            rayInteractable=GetOrAdd<RayInteractable>();rayInteractable.InjectAllRayInteractable(clippedPlaneSurface);rayInteractable.InjectOptionalPointableElement(pointableCanvas);
            pokeInteractable=GetOrAdd<PokeInteractable>();pokeInteractable.InjectAllPokeInteractable(clippedPlaneSurface);pokeInteractable.InjectOptionalPointableElement(pointableCanvas);
        }
        public static Vector3 CalculateBoundsSize(Vector2 size,float padding,float depth)
            =>new Vector3(Mathf.Max(1f,Mathf.Abs(size.x)+Mathf.Max(0f,padding)*2f),
                Mathf.Max(1f,Mathf.Abs(size.y)+Mathf.Max(0f,padding)*2f),Mathf.Max(.001f,depth));
        private T GetOrAdd<T>() where T:Component{T component=GetComponent<T>();return component!=null?component:gameObject.AddComponent<T>();}
    }
}

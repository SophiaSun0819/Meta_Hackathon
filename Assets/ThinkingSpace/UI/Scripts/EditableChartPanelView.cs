using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public enum EditableChartType { Bar,Line,Pie }

    [Serializable] public sealed class EditableChartPoint { public string Label=string.Empty;public float Value; }
    [Serializable] public sealed class EditableChartRevision
    {
        public int SchemaVersion=1;
        public string ChartId=string.Empty,RevisionId=string.Empty,ParentRevisionId=string.Empty;
        public string SessionId=string.Empty,IdeaId=string.Empty,Title=string.Empty;
        public EditableChartType Type=EditableChartType.Bar;
        public List<EditableChartPoint> Points=new List<EditableChartPoint>();
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster))]
    public sealed class EditableChartPanelView : MonoBehaviour
    {
        public static readonly Vector2 InitialPanelSize=new Vector2(720f,520f);
        [SerializeField] private EditableChartRevision revision;
        private RectTransform plotRoot;private WorldSpacePanelManipulator manipulator;
        public EditableChartRevision Revision=>revision;
        public WorldSpacePanelManipulator Manipulator=>manipulator;

        private void Awake()
        {
            RectTransform rect=transform as RectTransform;if(rect.sizeDelta.sqrMagnitude<1f)rect.sizeDelta=InitialPanelSize;
            manipulator=WorldSpacePanelManipulator.Attach(rect);EnsurePlotRoot();
        }

        public void Bind(EditableChartRevision value)
        {
            Validate(value);revision=Clone(value);manipulator=manipulator??WorldSpacePanelManipulator.Attach(transform as RectTransform);Render();
        }

        public EditableChartRevision CreateRevision(string revisionId,string title,IReadOnlyList<EditableChartPoint> points)
        {
            if(revision==null)throw new InvalidOperationException("Bind a chart before editing it.");
            var next=new EditableChartRevision{ChartId=revision.ChartId,RevisionId=revisionId,ParentRevisionId=revision.RevisionId,
                SessionId=revision.SessionId,IdeaId=revision.IdeaId,Title=title,Type=revision.Type,Points=CopyPoints(points)};
            Validate(next);Bind(next);return Clone(next);
        }

        public string ToJson()=>revision==null?string.Empty:JsonUtility.ToJson(revision,false);
        public static EditableChartRevision FromJson(string json)
        {if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("Chart JSON is required.",nameof(json));EditableChartRevision value=JsonUtility.FromJson<EditableChartRevision>(json);Validate(value);return value;}

        private void Render()
        {
            EnsurePlotRoot();for(int i=plotRoot.childCount-1;i>=0;i--)DestroyImmediate(plotRoot.GetChild(i).gameObject);
            if(revision.Type!=EditableChartType.Bar)return;
            float max=0f;foreach(EditableChartPoint point in revision.Points)max=Mathf.Max(max,Mathf.Abs(point.Value));max=Mathf.Max(max,.0001f);
            float width=1f/revision.Points.Count;
            for(int i=0;i<revision.Points.Count;i++)
            {
                var bar=new GameObject("Bar "+i,typeof(RectTransform),typeof(Image));RectTransform rect=bar.GetComponent<RectTransform>();rect.SetParent(plotRoot,false);
                float height=Mathf.Clamp01(Mathf.Abs(revision.Points[i].Value)/max);rect.anchorMin=new Vector2(i*width+.08f*width,0f);rect.anchorMax=new Vector2((i+1)*width-.08f*width,height);rect.offsetMin=rect.offsetMax=Vector2.zero;
                Image image=bar.GetComponent<Image>();image.color=new Color(.05f,.78f,1f,.9f);image.raycastTarget=false;
            }
        }

        private void EnsurePlotRoot()
        {
            if(plotRoot!=null)return;Transform existing=transform.Find("Chart Plot");
            if(existing!=null)plotRoot=existing as RectTransform;else{var plot=new GameObject("Chart Plot",typeof(RectTransform));plotRoot=plot.GetComponent<RectTransform>();plotRoot.SetParent(transform,false);plotRoot.anchorMin=new Vector2(.08f,.12f);plotRoot.anchorMax=new Vector2(.92f,.82f);plotRoot.offsetMin=plotRoot.offsetMax=Vector2.zero;}
        }
        private static void Validate(EditableChartRevision value)
        {
            if(value==null||value.SchemaVersion!=1)throw new ArgumentException("Chart schema version 1 is required.");
            if(string.IsNullOrWhiteSpace(value.ChartId)||string.IsNullOrWhiteSpace(value.RevisionId)||string.IsNullOrWhiteSpace(value.SessionId))throw new ArgumentException("Chart, revision, and session identities are required.");
            if(value.Points==null||value.Points.Count==0||value.Points.Count>24)throw new ArgumentException("Charts require 1-24 points.");
            foreach(EditableChartPoint point in value.Points)if(point==null||string.IsNullOrWhiteSpace(point.Label)||float.IsNaN(point.Value)||float.IsInfinity(point.Value))throw new ArgumentException("Every point needs a finite value and label.");
        }
        private static List<EditableChartPoint> CopyPoints(IReadOnlyList<EditableChartPoint> source){var result=new List<EditableChartPoint>();if(source!=null)for(int i=0;i<source.Count;i++)result.Add(new EditableChartPoint{Label=source[i].Label,Value=source[i].Value});return result;}
        private static EditableChartRevision Clone(EditableChartRevision source)=>JsonUtility.FromJson<EditableChartRevision>(JsonUtility.ToJson(source));
    }
}

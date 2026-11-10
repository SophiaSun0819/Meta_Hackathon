using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using ThinkingSpace.UI;

namespace ThinkingSpace.Speech
{
    public sealed class IdeaDetailsPanel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        private const int CharactersPerPage = 500;
        private BrainstormIdeaRecord idea;
        private Transform headset, bubble;
        private Canvas canvas;
        private TMP_Text heading, body, pageLabel;
        private Button previous, next, close, delete, cancelDelete, confirmDelete, playSummary, returnBubble;
        private readonly List<string> pages = new List<string>();
        private Action<IdeaDetailsPanel> closed;
        private Action<IdeaDetailsPanel> focused;
        private Func<IdeaDetailsPanel, string> deleteRequested;
        private Func<BrainstormIdeaRecord, Action<bool>, string> playRequested;
        private Action<string> stopRequested;
        private Action<string> returnRequested;
        private string summaryRequestId;
        private int page, slot;
        private bool hovered, pressed, confirmingDelete, returnAvailable;
        private Vector3 stablePosition;
        private Quaternion stableRotation;
        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public int SlotIndex => slot;
        public bool IsInteracting => hovered || pressed;

        public void Configure(BrainstormIdeaRecord value, Transform bubbleTransform, Transform headsetTransform,
            int panelSlot, Action<IdeaDetailsPanel> onClosed, Action<IdeaDetailsPanel> onFocused,
            Func<IdeaDetailsPanel, string> onDelete,
            Func<BrainstormIdeaRecord, Action<bool>, string> onPlaySummary,
            Action<string> onStopSummary, Action<string> onReturnBubble)
        {
            idea=value; bubble=bubbleTransform; headset=headsetTransform; slot=panelSlot;
            closed=onClosed; focused=onFocused; deleteRequested=onDelete; playRequested=onPlaySummary;
            stopRequested=onStopSummary; Build(); ShowDetails(); PlaceImmediately(); focused?.Invoke(this);
            returnRequested=onReturnBubble;
        }

        public void FocusPanel() { if (canvas != null) { canvas.gameObject.SetActive(true); canvas.sortingOrder=80+slot; focused?.Invoke(this); } }
        private void ShowDetails()
        {
            confirmingDelete=false; heading.text=string.IsNullOrWhiteSpace(idea.Title)?"Idea Details":idea.Title;
            SetPages("Summary\n"+idea.Summary+"\n\nCategory\n"+idea.Category); SetNormalControls();
        }
        private void AskDelete()
        {
            confirmingDelete=true; heading.text="Delete Idea?";
            body.text="Delete “"+(idea.Title??"this idea")+"”? References to it will be removed. Derived ideas are preserved.";
            pageLabel.gameObject.SetActive(false); previous.gameObject.SetActive(false); next.gameObject.SetActive(false);
            close.gameObject.SetActive(false); delete.gameObject.SetActive(false);
            playSummary.gameObject.SetActive(false);
            returnBubble.gameObject.SetActive(false);
            cancelDelete.gameObject.SetActive(true); confirmDelete.gameObject.SetActive(true);
        }
        private void ConfirmDelete()
        {
            string error=deleteRequested!=null?deleteRequested(this):"Deletion is unavailable.";
            if (string.IsNullOrEmpty(error)) return;
            heading.text="Delete Failed"; body.text=error; confirmDelete.gameObject.SetActive(false);
        }
        private void SetNormalControls()
        {
            close.gameObject.SetActive(true); delete.gameObject.SetActive(true); pageLabel.gameObject.SetActive(true);
            playSummary.gameObject.SetActive(true);
            previous.gameObject.SetActive(true); next.gameObject.SetActive(true);
            cancelDelete.gameObject.SetActive(false); confirmDelete.gameObject.SetActive(false);
            returnBubble.gameObject.SetActive(returnAvailable);
        }
        public void SetReturnAvailable(bool available)
        {
            returnAvailable = available;
            if (returnBubble != null) returnBubble.gameObject.SetActive(available && !confirmingDelete);
        }
        private void ClosePanel()=>closed?.Invoke(this);

        private void ToggleSummaryPlayback()
        {
            if (!string.IsNullOrEmpty(summaryRequestId))
            {
                string request = summaryRequestId;
                stopRequested?.Invoke(request);
                if (summaryRequestId == request) SetSummaryPlaybackState(null);
                return;
            }
            string accepted = playRequested?.Invoke(idea, _ =>
            {
                if (this != null) SetSummaryPlaybackState(null);
            });
            if (!string.IsNullOrEmpty(accepted)) SetSummaryPlaybackState(accepted);
        }

        private void SetSummaryPlaybackState(string requestId)
        {
            summaryRequestId = requestId;
            if (playSummary == null) return;
            TMP_Text label = playSummary.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = string.IsNullOrEmpty(requestId) ? "Play Summary" : "Stop Summary";
            playSummary.interactable = true;
        }

        private void LateUpdate()
        {
            if (canvas==null || !canvas.gameObject.activeSelf || hovered || pressed) return;
            float bob=Mathf.Sin(Time.unscaledTime*1.1f+slot)*.004f;
            transform.position=stablePosition+Vector3.up*bob;
            transform.rotation=stableRotation;
        }
        private Vector3 DesiredPosition()
        {
            if (headset==null && Camera.main!=null) headset=Camera.main.transform;
            Vector3 anchor=bubble!=null?bubble.position:transform.position;
            Vector3 toward=headset!=null?Vector3.ProjectOnPlane(headset.position-anchor,Vector3.up).normalized:Vector3.back;
            if(toward.sqrMagnitude<.01f)toward=Vector3.back;
            Vector3 right=headset!=null?headset.right:Vector3.right;
            float x=slot%2==0?-.18f:.18f, y=slot<2?.16f:-.16f;
            return anchor+toward*.22f+right*x+Vector3.up*y;
        }
        private void PlaceImmediately()
        {
            stablePosition=DesiredPosition(); transform.position=stablePosition; FaceViewer(); stableRotation=transform.rotation;
        }

        public void NudgeToAvoid(IReadOnlyList<Vector3> occupied)
        {
            if (occupied == null) return;
            for (int attempt=0; attempt<6; attempt++)
            {
                bool overlap=false;
                for(int i=0;i<occupied.Count;i++)
                    if((stablePosition-occupied[i]).sqrMagnitude<.28f*.28f){overlap=true;break;}
                if(!overlap)break;
                stablePosition += Vector3.up*.18f;
            }
            transform.position=stablePosition; FaceViewer(); stableRotation=transform.rotation;
        }
        private void FaceViewer()
        {
            if(headset==null)return; Vector3 away=transform.position-headset.position;
            if(away.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(away.normalized,Vector3.up);
        }
        private void SetPages(string value)
        {
            pages.Clear(); string text=value??string.Empty;
            for(int offset=0;offset<text.Length;){int count=Mathf.Min(CharactersPerPage,text.Length-offset);
                if(offset+count<text.Length){int split=text.LastIndexOfAny(new[]{' ','\n','\t'},offset+count-1,count);if(split>=offset+CharactersPerPage/2)count=split-offset+1;}
                pages.Add(text.Substring(offset,count).Trim());offset+=count;}
            if(pages.Count==0)pages.Add(string.Empty);page=0;Refresh();
        }
        private void Refresh(){if(confirmingDelete)return;body.text=pages[page];pageLabel.text="Page "+(page+1)+" / "+pages.Count;previous.interactable=page>0;next.interactable=page<pages.Count-1;}

        private void Build()
        {
            canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;canvas.sortingOrder=80+slot;
            canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
            gameObject.AddComponent<GraphicRaycaster>();PointableCanvas pointable=gameObject.AddComponent<PointableCanvas>();pointable.InjectAllPointableCanvas(canvas);
            RectTransform rect=GetComponent<RectTransform>();rect.sizeDelta=new Vector2(600,400);rect.localScale=Vector3.one*.0005f;
            Image background=gameObject.AddComponent<Image>();background.color=new Color(.035f,.055f,.09f,.98f);background.raycastTarget=true;
            PlaneSurface plane=gameObject.AddComponent<PlaneSurface>();plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward,false);
            BoundsClipper clipper=gameObject.AddComponent<BoundsClipper>();clipper.Size=new Vector3(600,400,.01f);
            ClippedPlaneSurface clipped=gameObject.AddComponent<ClippedPlaneSurface>();clipped.InjectAllClippedPlaneSurface(plane,new List<IBoundsClipper>{clipper});
            RayInteractable ray=gameObject.AddComponent<RayInteractable>();ray.InjectAllRayInteractable(clipped);ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke=gameObject.AddComponent<PokeInteractable>();poke.InjectAllPokeInteractable(clipped);poke.InjectOptionalPointableElement(pointable);
            heading=Text("Heading",rect,36,FontStyles.Bold,TextAlignmentOptions.Center,new Vector2(.06f,.82f),new Vector2(.94f,.96f));
            body=Text("Body",rect,26,FontStyles.Normal,TextAlignmentOptions.TopLeft,new Vector2(.07f,.30f),new Vector2(.93f,.79f));body.textWrappingMode=TextWrappingModes.Normal;body.overflowMode=TextOverflowModes.Ellipsis;
            previous=Button("Previous",rect,new Vector2(.05f,.19f),new Vector2(.27f,.27f));previous.onClick.AddListener(()=>{page=Mathf.Max(0,page-1);Refresh();});
            pageLabel=Text("Page",rect,22,FontStyles.Normal,TextAlignmentOptions.Center,new Vector2(.29f,.19f),new Vector2(.71f,.27f));
            next=Button("Next",rect,new Vector2(.73f,.19f),new Vector2(.95f,.27f));next.onClick.AddListener(()=>{page=Mathf.Min(pages.Count-1,page+1);Refresh();});
            playSummary=Button("Play Summary",rect,new Vector2(.03f,.05f),new Vector2(.25f,.16f));playSummary.onClick.AddListener(ToggleSummaryPlayback);
            returnBubble=Button("Return",rect,new Vector2(.27f,.05f),new Vector2(.49f,.16f));returnBubble.onClick.AddListener(()=>returnRequested?.Invoke(IdeaId));returnBubble.gameObject.SetActive(false);
            delete=Button("Delete Idea",rect,new Vector2(.51f,.05f),new Vector2(.73f,.16f));delete.onClick.AddListener(AskDelete);
            close=Button("Close",rect,new Vector2(.75f,.05f),new Vector2(.97f,.16f));close.onClick.AddListener(ClosePanel);
            cancelDelete=Button("Cancel",rect,new Vector2(.06f,.05f),new Vector2(.44f,.16f));cancelDelete.onClick.AddListener(ShowDetails);
            confirmDelete=Button("Confirm Delete",rect,new Vector2(.56f,.05f),new Vector2(.94f,.16f));confirmDelete.onClick.AddListener(ConfirmDelete);
        }
        public void OnPointerEnter(PointerEventData _)=>hovered=true; public void OnPointerExit(PointerEventData _)=>hovered=false;
        public void OnPointerDown(PointerEventData _){pressed=true;focused?.Invoke(this);} public void OnPointerUp(PointerEventData _)=>pressed=false;
        private static TMP_Text Text(string name,Transform parent,float size,FontStyles style,TextAlignmentOptions align,Vector2 min,Vector2 max)
        {GameObject v=new GameObject(name,typeof(RectTransform));v.transform.SetParent(parent,false);RectTransform r=v.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;TextMeshProUGUI t=v.AddComponent<TextMeshProUGUI>();t.font=TMP_Settings.defaultFontAsset;t.fontSize=size;t.fontStyle=style;t.alignment=align;t.color=Color.white;t.raycastTarget=false;return t;}
        private Button Button(string label,Transform parent,Vector2 min,Vector2 max)
        {GameObject v=new GameObject(label+" Button",typeof(RectTransform));v.transform.SetParent(parent,false);RectTransform r=v.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;Image i=v.AddComponent<Image>();i.color=new Color(.08f,.52f,.72f,1);Button b=v.AddComponent<Button>();b.targetGraphic=i;b.onClick.AddListener(()=>focused?.Invoke(this));TMP_Text t=Text("Label",r,25,FontStyles.Bold,TextAlignmentOptions.Center,Vector2.zero,Vector2.one);t.text=label;return b;}
    }
}

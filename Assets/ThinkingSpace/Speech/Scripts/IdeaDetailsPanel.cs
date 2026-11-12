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
        private const float ViewerFacingSpeed = 8f;
        private BrainstormIdeaRecord idea;
        private Transform headset;
        private IdeaBubbleView bubble;
        private Canvas canvas;
        private RayInteractable rayInteractable;
        private PokeInteractable pokeInteractable;
        private TMP_Text heading, body, pageLabel;
        private Button previous, next, close, delete, cancelDelete, confirmDelete, playSummary, returnBubble,color;
        private readonly List<Button> colorButtons=new List<Button>();
        private readonly List<string> pages = new List<string>();
        private Action<IdeaDetailsPanel> closed;
        private Action<IdeaDetailsPanel> focused;
        private Func<IdeaDetailsPanel, string> deleteRequested;
        private Func<BrainstormIdeaRecord, Action<bool>, string> playRequested;
        private Action<string> stopRequested;
        private Action<string> returnRequested;
        private Action<IdeaDetailsPanel, bool> dragStateChanged;
        private Func<IdeaDetailsPanel,string,string> colorRequested;
        private string summaryRequestId;
        private int page, slot;
        private bool hovered, pressed, confirmingDelete, returnAvailable, movementHidden,colorPage;
        private bool borderProximity, dragging, hasManualOffset;
        private Vector3 stablePosition;
        private Vector3 bubbleRelativeOffset, dragHandOffset, dragVelocity;
        private Quaternion stableRotation;
        private readonly List<Image> borderImages = new List<Image>();
        public string IdeaId => idea != null ? idea.IdeaId : string.Empty;
        public int SlotIndex => slot;
        public bool IsInteracting => hovered || pressed;
        public bool IsPressed => pressed;
        public bool IsDragging => dragging;
        public bool HasManualOffset => hasManualOffset;
        public bool IsMovementHidden => movementHidden;

        public void Configure(BrainstormIdeaRecord value, IdeaBubbleView bubbleView, Transform headsetTransform,
            int panelSlot, Action<IdeaDetailsPanel> onClosed, Action<IdeaDetailsPanel> onFocused,
            Func<IdeaDetailsPanel, string> onDelete,
            Func<BrainstormIdeaRecord, Action<bool>, string> onPlaySummary,
            Action<string> onStopSummary, Action<string> onReturnBubble,
            Action<IdeaDetailsPanel, bool> onDragStateChanged = null,
            Func<IdeaDetailsPanel,string,string> onColorRequested=null)
        {
            idea=value; bubble=bubbleView; headset=headsetTransform; slot=panelSlot;
            closed=onClosed; focused=onFocused; deleteRequested=onDelete; playRequested=onPlaySummary;
            stopRequested=onStopSummary; dragStateChanged=onDragStateChanged;
            colorRequested=onColorRequested;
            Build(); ShowDetails(); PlaceImmediately(); focused?.Invoke(this);
            returnRequested=onReturnBubble;
        }

        public void FocusPanel()
        {
            if (canvas == null) return;
            if (!movementHidden) canvas.gameObject.SetActive(true);
            canvas.sortingOrder=80+slot;
            focused?.Invoke(this);
        }
        private void ShowDetails()
        {
            confirmingDelete=false;colorPage=false; heading.text=string.IsNullOrWhiteSpace(idea.Title)?"Idea Details":idea.Title;
            SetPages("Summary\n"+idea.Summary+"\n\nCategory\n"+idea.Category); SetNormalControls();
        }
        private void AskDelete()
        {
            confirmingDelete=true; heading.text="Delete Idea?";
            body.text="Delete “"+(idea.Title??"this idea")+"”? References to it will be removed. Derived ideas are preserved.";
            pageLabel.gameObject.SetActive(false); previous.gameObject.SetActive(false); next.gameObject.SetActive(false);
            close.gameObject.SetActive(false); delete.gameObject.SetActive(false);
            playSummary.gameObject.SetActive(false);
            color.gameObject.SetActive(false);SetColorButtonsVisible(false);
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
            color.gameObject.SetActive(true);SetButtonLabel(color,"Color");SetColorButtonsVisible(false);body.gameObject.SetActive(true);
            previous.gameObject.SetActive(true); next.gameObject.SetActive(true);
            cancelDelete.gameObject.SetActive(false); confirmDelete.gameObject.SetActive(false);
            returnBubble.gameObject.SetActive(returnAvailable);
        }
        public void SetReturnAvailable(bool available)
        {
            returnAvailable = available;
            if (returnBubble != null) returnBubble.gameObject.SetActive(available && !confirmingDelete);
        }

        public void SetMovementHidden(bool hidden)
        {
            movementHidden = hidden;
            if (hidden && dragging) EndBorderDrag();
            hovered = pressed = false;
            if (rayInteractable != null) rayInteractable.enabled = !hidden;
            if (pokeInteractable != null) pokeInteractable.enabled = !hidden;
            if (canvas != null)
            {
                canvas.enabled = !hidden;
                canvas.gameObject.SetActive(!hidden);
            }
        }

        public void RelocateAfterBubbleMovement(IReadOnlyList<Vector3> occupiedPositions)
        {
            if (bubble == null) return;
            if (hasManualOffset)
            {
                stablePosition = bubble.transform.position + bubbleRelativeOffset;
                transform.position = stablePosition;
                transform.rotation = stableRotation;
            }
            else
            {
                PlaceImmediately();
                NudgeToAvoid(occupiedPositions);
            }
            SetMovementHidden(false);
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

        private void ToggleColorPage()
        {if(colorPage)ShowDetails();else ShowColorPage();}

        private void ShowColorPage()
        {
            colorPage=true;confirmingDelete=false;heading.text="Bubble Color · "+IdeaVisualColorPalette.Normalize(idea.VisualColor);
            body.gameObject.SetActive(false);pageLabel.gameObject.SetActive(false);previous.gameObject.SetActive(false);next.gameObject.SetActive(false);
            playSummary.gameObject.SetActive(false);returnBubble.gameObject.SetActive(false);delete.gameObject.SetActive(false);
            cancelDelete.gameObject.SetActive(false);confirmDelete.gameObject.SetActive(false);close.gameObject.SetActive(true);
            color.gameObject.SetActive(true);SetButtonLabel(color,"Back");SetColorButtonsVisible(true);RefreshColorButtons();
        }

        private void SelectColor(string value)
        {
            string error=colorRequested!=null?colorRequested(this,value):"Color changes are unavailable.";
            if(!string.IsNullOrEmpty(error)){heading.text="Color Change Failed";return;}
            idea.VisualColor=IdeaVisualColorPalette.Normalize(value);heading.text="Bubble Color · "+idea.VisualColor;RefreshColorButtons();
        }

        private void SetColorButtonsVisible(bool visible)
        {for(int i=0;i<colorButtons.Count;i++)if(colorButtons[i]!=null)colorButtons[i].gameObject.SetActive(visible);}

        private void RefreshColorButtons()
        {
            string selected=IdeaVisualColorPalette.Normalize(idea.VisualColor);
            for(int i=0;i<colorButtons.Count&&i<IdeaVisualColorPalette.Names.Length;i++)
            {string name=IdeaVisualColorPalette.Names[i];SetButtonLabel(colorButtons[i],name+(selected==name?" ✓":""));}
        }

        private static void SetButtonLabel(Button button,string value)
        {TMP_Text label=button!=null?button.GetComponentInChildren<TMP_Text>(true):null;if(label!=null)label.text=value;}

        private void LateUpdate()
        {
            if(canvas==null||!canvas.gameObject.activeSelf)return;
            transform.position=stablePosition;
            if(hovered||pressed||dragging)return;
            if(TryGetViewerFacingRotation(transform.position,headset,transform.rotation,out Quaternion target))
            {
                float blend=1f-Mathf.Exp(-ViewerFacingSpeed*Time.unscaledDeltaTime);
                stableRotation=Quaternion.Slerp(transform.rotation,target,blend);
                transform.rotation=stableRotation;
            }
        }
        private Vector3 DesiredPosition()
        {
            if (headset==null && Camera.main!=null) headset=Camera.main.transform;
            Vector3 anchor=bubble!=null?bubble.transform.position:transform.position;
            Vector3 toward=headset!=null?Vector3.ProjectOnPlane(headset.position-anchor,Vector3.up).normalized:Vector3.back;
            if(toward.sqrMagnitude<.01f)toward=Vector3.back;
            float radius=bubble!=null?bubble.WorldRadius:.07f;
            const float titleClearance=.08f, panelHalfHeight=.10f, gap=.035f;
            return anchor+toward*.07f+Vector3.up*(radius+titleClearance+panelHalfHeight+gap);
        }
        private void PlaceImmediately()
        {
            stablePosition=DesiredPosition(); transform.position=stablePosition; FaceViewer(); stableRotation=transform.rotation;
        }

        public void NudgeToAvoid(IReadOnlyList<Vector3> occupied)
        {
            if (occupied == null || hasManualOffset) return;
            Vector3 origin=stablePosition;
            Vector3 right=headset!=null?Vector3.ProjectOnPlane(headset.right,Vector3.up).normalized:Vector3.right;
            Vector3[] alternatives={Vector3.zero,right*.32f,-right*.32f,Vector3.up*.24f,
                right*.32f+Vector3.up*.24f,-right*.32f+Vector3.up*.24f};
            for (int attempt=0; attempt<alternatives.Length; attempt++)
            {
                stablePosition=origin+alternatives[attempt];
                bool overlap=false;
                for(int i=0;i<occupied.Count;i++)
                    if((stablePosition-occupied[i]).sqrMagnitude<.28f*.28f){overlap=true;break;}
                if(!overlap)break;
            }
            transform.position=stablePosition; FaceViewer(); stableRotation=transform.rotation;
        }

        public float DistanceToBorder(Vector3 worldPoint)
        {
            RectTransform rect=transform as RectTransform;
            if(rect==null)return float.PositiveInfinity;
            Vector3 a=rect.TransformPoint(new Vector3(-300f,-200f,0f));
            Vector3 b=rect.TransformPoint(new Vector3(300f,-200f,0f));
            Vector3 c=rect.TransformPoint(new Vector3(300f,200f,0f));
            Vector3 d=rect.TransformPoint(new Vector3(-300f,200f,0f));
            return Mathf.Min(DistanceToSegment(worldPoint,a,b),DistanceToSegment(worldPoint,b,c),
                DistanceToSegment(worldPoint,c,d),DistanceToSegment(worldPoint,d,a));
        }

        public void SetBorderProximity(bool near)
        {
            borderProximity=near;
            RefreshBorderVisual();
        }

        public void BeginBorderDrag(Vector3 handPosition)
        {
            if(dragging||movementHidden)return;
            dragging=true; dragVelocity=Vector3.zero; dragHandOffset=stablePosition-handPosition;
            focused?.Invoke(this); dragStateChanged?.Invoke(this,true); RefreshBorderVisual();
        }

        public void UpdateBorderDrag(Vector3 handPosition,float smoothTime)
        {
            if(!dragging)return;
            stablePosition=Vector3.SmoothDamp(stablePosition,handPosition+dragHandOffset,
                ref dragVelocity,Mathf.Max(.001f,smoothTime),Mathf.Infinity,Time.unscaledDeltaTime);
            transform.position=stablePosition; transform.rotation=stableRotation;
        }

        public void EndBorderDrag()
        {
            if(!dragging)return;
            dragging=false; dragVelocity=Vector3.zero; hasManualOffset=bubble!=null;
            if(bubble!=null)bubbleRelativeOffset=stablePosition-bubble.transform.position;
            dragStateChanged?.Invoke(this,false); RefreshBorderVisual();
        }

        private static float DistanceToSegment(Vector3 point,Vector3 start,Vector3 end)
        {
            Vector3 segment=end-start; float lengthSquared=segment.sqrMagnitude;
            if(lengthSquared<.000001f)return Vector3.Distance(point,start);
            float t=Mathf.Clamp01(Vector3.Dot(point-start,segment)/lengthSquared);
            return Vector3.Distance(point,start+segment*t);
        }
        private void FaceViewer()
        {
            if(TryGetViewerFacingRotation(transform.position,headset,transform.rotation,out Quaternion target))
                transform.rotation=target;
        }
        public static bool TryGetViewerFacingRotation(Vector3 panelPosition,Transform viewer,
            Quaternion current,out Quaternion target)
        {
            target=current;if(viewer==null)return false;
            Vector3 away=Vector3.ProjectOnPlane(panelPosition-viewer.position,Vector3.up);
            if(away.sqrMagnitude<.0001f)return false;
            target=Quaternion.LookRotation(away.normalized,Vector3.up);return true;
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
            BuildBorder(rect);
            PlaneSurface plane=gameObject.AddComponent<PlaneSurface>();plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward,false);
            BoundsClipper clipper=gameObject.AddComponent<BoundsClipper>();clipper.Size=new Vector3(600,400,.01f);
            ClippedPlaneSurface clipped=gameObject.AddComponent<ClippedPlaneSurface>();clipped.InjectAllClippedPlaneSurface(plane,new List<IBoundsClipper>{clipper});
            rayInteractable=gameObject.AddComponent<RayInteractable>();rayInteractable.InjectAllRayInteractable(clipped);rayInteractable.InjectOptionalPointableElement(pointable);
            pokeInteractable=gameObject.AddComponent<PokeInteractable>();pokeInteractable.InjectAllPokeInteractable(clipped);pokeInteractable.InjectOptionalPointableElement(pointable);
            heading=Text("Heading",rect,36,FontStyles.Bold,TextAlignmentOptions.Center,new Vector2(.06f,.82f),new Vector2(.94f,.96f));
            body=Text("Body",rect,26,FontStyles.Normal,TextAlignmentOptions.TopLeft,new Vector2(.07f,.30f),new Vector2(.93f,.79f));body.textWrappingMode=TextWrappingModes.Normal;body.overflowMode=TextOverflowModes.Ellipsis;
            previous=Button("Previous",rect,new Vector2(.05f,.19f),new Vector2(.27f,.27f));previous.onClick.AddListener(()=>{page=Mathf.Max(0,page-1);Refresh();});
            pageLabel=Text("Page",rect,22,FontStyles.Normal,TextAlignmentOptions.Center,new Vector2(.29f,.19f),new Vector2(.71f,.27f));
            next=Button("Next",rect,new Vector2(.73f,.19f),new Vector2(.95f,.27f));next.onClick.AddListener(()=>{page=Mathf.Min(pages.Count-1,page+1);Refresh();});
            playSummary=Button("Play Summary",rect,new Vector2(.03f,.05f),new Vector2(.20f,.16f));playSummary.onClick.AddListener(ToggleSummaryPlayback);
            returnBubble=Button("Return",rect,new Vector2(.21f,.05f),new Vector2(.38f,.16f));returnBubble.onClick.AddListener(()=>returnRequested?.Invoke(IdeaId));returnBubble.gameObject.SetActive(false);
            color=Button("Color",rect,new Vector2(.39f,.05f),new Vector2(.55f,.16f));color.onClick.AddListener(ToggleColorPage);
            delete=Button("Delete",rect,new Vector2(.56f,.05f),new Vector2(.73f,.16f));delete.onClick.AddListener(AskDelete);
            close=Button("Close",rect,new Vector2(.74f,.05f),new Vector2(.97f,.16f));close.onClick.AddListener(ClosePanel);
            cancelDelete=Button("Cancel",rect,new Vector2(.06f,.05f),new Vector2(.44f,.16f));cancelDelete.onClick.AddListener(ShowDetails);
            confirmDelete=Button("Confirm Delete",rect,new Vector2(.56f,.05f),new Vector2(.94f,.16f));confirmDelete.onClick.AddListener(ConfirmDelete);
            for(int i=0;i<IdeaVisualColorPalette.Names.Length;i++)
            {
                int row=i/3,column=i%3;Vector2 min=new Vector2(.06f+column*.31f,.64f-row*.15f);
                Button palette=Button(IdeaVisualColorPalette.Names[i],rect,min,min+new Vector2(.27f,.11f));
                string choice=IdeaVisualColorPalette.Names[i];palette.onClick.AddListener(()=>SelectColor(choice));
                Image image=palette.targetGraphic as Image;if(image!=null)image.color=IdeaVisualColorPalette.Resolve(choice,new Color(.12f,.2f,.3f));
                colorButtons.Add(palette);
            }
        }

        private void BuildBorder(RectTransform parent)
        {
            CreateBorderEdge("Top Border",parent,new Vector2(0f,.965f),new Vector2(1f,1f));
            CreateBorderEdge("Bottom Border",parent,new Vector2(0f,0f),new Vector2(1f,.035f));
            CreateBorderEdge("Left Border",parent,new Vector2(0f,.035f),new Vector2(.023f,.965f));
            CreateBorderEdge("Right Border",parent,new Vector2(.977f,.035f),new Vector2(1f,.965f));
            RefreshBorderVisual();
        }

        private void CreateBorderEdge(string edgeName,RectTransform parent,Vector2 min,Vector2 max)
        {
            GameObject edge=new GameObject(edgeName,typeof(RectTransform),typeof(Image));
            edge.transform.SetParent(parent,false); RectTransform rect=edge.GetComponent<RectTransform>();
            rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=rect.offsetMax=Vector2.zero;
            Image image=edge.GetComponent<Image>();image.raycastTarget=false;borderImages.Add(image);
        }

        private void RefreshBorderVisual()
        {
            Color color=dragging?new Color(.35f,.95f,1f,1f):borderProximity
                ?new Color(.2f,.75f,1f,.95f):new Color(.08f,.32f,.48f,.72f);
            for(int i=0;i<borderImages.Count;i++)if(borderImages[i]!=null)borderImages[i].color=color;
        }
        public void OnPointerEnter(PointerEventData _)=>hovered=true; public void OnPointerExit(PointerEventData _)=>hovered=false;
        public void OnPointerDown(PointerEventData _){pressed=true;focused?.Invoke(this);} public void OnPointerUp(PointerEventData _)=>pressed=false;
        private static TMP_Text Text(string name,Transform parent,float size,FontStyles style,TextAlignmentOptions align,Vector2 min,Vector2 max)
        {GameObject v=new GameObject(name,typeof(RectTransform));v.transform.SetParent(parent,false);RectTransform r=v.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;TextMeshProUGUI t=v.AddComponent<TextMeshProUGUI>();t.font=TMP_Settings.defaultFontAsset;t.fontSize=size;t.fontStyle=style;t.alignment=align;t.color=Color.white;t.raycastTarget=false;return t;}
        private Button Button(string label,Transform parent,Vector2 min,Vector2 max)
        {GameObject v=new GameObject(label+" Button",typeof(RectTransform));v.transform.SetParent(parent,false);RectTransform r=v.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;Image i=v.AddComponent<Image>();i.color=new Color(.08f,.52f,.72f,1);Button b=v.AddComponent<Button>();b.targetGraphic=i;b.onClick.AddListener(()=>focused?.Invoke(this));TMP_Text t=Text("Label",r,25,FontStyles.Bold,TextAlignmentOptions.Center,Vector2.zero,Vector2.one);t.text=label;return b;}
    }
}

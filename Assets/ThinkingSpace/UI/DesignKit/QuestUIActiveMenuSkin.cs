using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI.DesignKit
{
    [DisallowMultipleComponent]
    public sealed class QuestUIActiveMenuSkin : MonoBehaviour
    {
        private BrainstormMenuView view;
        private Transform activePanel;
        private GameObject homeRoot;
        private GameObject homeContent,functionalContent,helpContent;
        private GameObject headerRoot,footerRoot;
        private Image shellBackground;
        private TMP_Text statusLabel;
        private Button startCard,sessionsCard,accountCard,gesturesCard,helpVoiceCard,helpGesturesCard,settingsButton,helpButton,closeButton,homeButton;
        [SerializeField] private Sprite roundedPanel,orbIcon,sessionsIcon,voiceIcon,gesturesIcon;
        public Sprite RoundedPanelSprite=>roundedPanel;

        public bool IsInitialized=>homeRoot!=null;
        public int AuthoritativeShellCount=>homeRoot==null?0:1;
        public bool IsHelpVisible=>helpContent!=null&&helpContent.activeSelf;
        public bool HasAccountConnectionCard=>accountCard!=null;
        public bool HasRedundantSinglePageIndicator=>footerRoot!=null&&footerRoot.transform.Find("Home Page Indicator")!=null;
        public string RuntimeHierarchyState=>"root:"+Active(homeRoot)+"/header:"+Active(headerRoot)
            +"/home:"+Active(homeContent)+"/functional:"+Active(functionalContent)
            +"/help:"+Active(helpContent)+"/footer:"+Active(footerRoot);
        public bool LegacyFrameBehindContent
        {
            get
            {
                Transform frame=view!=null&&view.CanvasRect!=null?FindDeep(view.CanvasRect,"SlimUI Template Frame"):null;
                return frame==null||activePanel==null||frame.parent!=activePanel.parent||frame.GetSiblingIndex()<activePanel.GetSiblingIndex();
            }
        }

        public void ConfigureVisualAssets(Sprite rounded,Sprite orb,Sprite sessions,Sprite voice,Sprite gestures)
        {roundedPanel=rounded;orbIcon=orb;sessionsIcon=sessions;voiceIcon=voice;gesturesIcon=gestures;}

        public void Initialize(BrainstormMenuView owner,GameObject mainPanel,Button startSource,
            Button voiceSource,Button gesturesSource,Button accountSource,Button closeSource)
        {
            if(homeRoot!=null||owner==null||mainPanel==null||startSource==null||closeSource==null)return;
            view=owner;
            activePanel=mainPanel.transform;
            PlaceLegacyFrameBehindActiveContent(owner.CanvasRect);
            RectTransform parent=mainPanel.GetComponent<RectTransform>();
            List<Transform> existing=new List<Transform>();
            for(int i=0;i<parent.childCount;i++)existing.Add(parent.GetChild(i));
            homeRoot=new GameObject("DesignKit Active Home",typeof(RectTransform),typeof(Image),typeof(QuestUIDesignComponent));
            homeRoot.transform.SetParent(parent,false);
            RectTransform root=homeRoot.GetComponent<RectTransform>();Stretch(root,Vector2.zero,Vector2.one);
            shellBackground=homeRoot.GetComponent<Image>();shellBackground.color=QuestUIDesignTokens.Panel;shellBackground.raycastTarget=false;
            homeRoot.GetComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.PanelShell);

            RectTransform functional=CreateRegion("Functional Page Content",root,new Vector2(.035f,.18f),new Vector2(.965f,.79f),Color.clear);
            functionalContent=functional.gameObject;
            for(int i=0;i<existing.Count;i++)existing[i].SetParent(functional,false);

            RectTransform header=CreateRegion("Fixed Header",root,new Vector2(.025f,.81f),new Vector2(.975f,.975f),QuestUIDesignTokens.Raised);
            headerRoot=header.gameObject;
            AddOutline(header.gameObject,QuestUIDesignTokens.Cyan,2f);
            CreateIcon("Brand Orb",header,orbIcon,new Vector2(.018f,.12f),new Vector2(.10f,.88f));
            CreateText("Title",header,"THINKING SPACE",30,new Vector2(.11f,.18f),new Vector2(.42f,.82f),TextAlignmentOptions.MidlineLeft,QuestUIDesignTokens.Text);
            statusLabel=CreateText("Status",header,string.Empty,20,new Vector2(.43f,.2f),new Vector2(.62f,.8f),TextAlignmentOptions.Center,QuestUIDesignTokens.Mint);
            settingsButton=CloneButton(closeSource,header,"Settings",new Vector2(.63f,.16f),new Vector2(.78f,.84f));
            helpButton=CloneButton(closeSource,header,"Help",new Vector2(.79f,.16f),new Vector2(.89f,.84f));
            closeButton=CloneButton(closeSource,header,"Close",new Vector2(.90f,.16f),new Vector2(.985f,.84f));
            Bind(settingsButton,owner.ShowDesignKitSettings);Bind(helpButton,owner.ShowDesignKitHelp);
            Bind(closeButton,()=>closeSource.onClick.Invoke());

            RectTransform content=CreateRegion("Fixed Content",root,new Vector2(.035f,.18f),new Vector2(.965f,.79f),Color.clear);
            homeContent=content.gameObject;
            startCard=CreateCard(startSource,content,"Start New Session",orbIcon,QuestUIDesignTokens.Cyan,new Vector2(.02f,.53f),new Vector2(.49f,.98f),()=>startSource.onClick.Invoke());
            sessionsCard=CreateCard(startSource,content,"My Sessions",sessionsIcon,QuestUIDesignTokens.Mint,new Vector2(.51f,.53f),new Vector2(.98f,.98f),owner.ShowDesignKitSessions);
            accountCard=CreateCard(accountSource??startSource,content,"Account / Device Connection",sessionsIcon,QuestUIDesignTokens.Orange,new Vector2(.02f,.02f),new Vector2(.49f,.47f),()=>accountSource?.onClick.Invoke());
            gesturesCard=CreateCard(gesturesSource??startSource,content,"Hand Gestures",gesturesIcon,QuestUIDesignTokens.Coral,new Vector2(.51f,.02f),new Vector2(.98f,.47f),()=>gesturesSource.onClick.Invoke());

            RectTransform help=CreateRegion("DesignKit Help Hub",root,new Vector2(.035f,.18f),new Vector2(.965f,.79f),Color.clear);
            helpContent=help.gameObject;
            helpVoiceCard=CreateCard(voiceSource??startSource,help,"Voice Commands",voiceIcon,QuestUIDesignTokens.Orange,new Vector2(.02f,.16f),new Vector2(.49f,.84f),()=>voiceSource?.onClick.Invoke());
            helpGesturesCard=CreateCard(gesturesSource??startSource,help,"Hand Gestures",gesturesIcon,QuestUIDesignTokens.Coral,new Vector2(.51f,.16f),new Vector2(.98f,.84f),()=>gesturesSource?.onClick.Invoke());

            RectTransform footer=CreateRegion("Fixed Footer",root,new Vector2(.025f,.025f),new Vector2(.975f,.15f),QuestUIDesignTokens.Raised);
            footerRoot=footer.gameObject;
            AddOutline(footer.gameObject,new Color(QuestUIDesignTokens.Cyan.r,QuestUIDesignTokens.Cyan.g,QuestUIDesignTokens.Cyan.b,.6f),1.5f);
            homeButton=CloneButton(closeSource,footer,"Home",new Vector2(.39f,.14f),new Vector2(.61f,.86f));
            Bind(homeButton,owner.ShowDesignKitHome);
            AdoptNavigation(mainPanel.transform,footer,"Previous Page Button",new Vector2(.02f,.14f),new Vector2(.27f,.86f));
            AdoptNavigation(mainPanel.transform,footer,"Next Page Button",new Vector2(.73f,.14f),new Vector2(.98f,.86f));
            AdoptNavigation(mainPanel.transform,footer,"Page Indicator",new Vector2(.28f,.14f),new Vector2(.38f,.86f));
            ThinkingSpaceUIStyler.StyleHierarchy(homeRoot);
            helpContent.SetActive(false);
            RefreshLocalization();
        }

        public void SetHomeVisible(bool visible)
        {
            if(homeRoot==null)return;
            homeRoot.SetActive(true);homeRoot.transform.SetAsLastSibling();
            if(headerRoot!=null){headerRoot.SetActive(true);headerRoot.transform.SetAsLastSibling();}
            if(footerRoot!=null){footerRoot.SetActive(true);footerRoot.transform.SetAsLastSibling();}
            if(homeContent!=null)homeContent.SetActive(visible);
            if(helpContent!=null)helpContent.SetActive(false);
            if(functionalContent!=null)functionalContent.SetActive(!visible);
            if(shellBackground!=null)shellBackground.color=QuestUIDesignTokens.Panel;
            if(homeButton!=null)homeButton.gameObject.SetActive(!visible);
        }

        public void SetHelpVisible()
        {
            if(homeRoot==null)return;
            homeRoot.SetActive(true);homeContent.SetActive(false);functionalContent.SetActive(false);helpContent.SetActive(true);
            homeButton.gameObject.SetActive(true);
            if(footerRoot!=null)foreach(Transform child in footerRoot.transform)
                if(child.name=="Previous Page Button"||child.name=="Next Page Button"||child.name=="Page Indicator")child.gameObject.SetActive(false);
        }

        public void RefreshLocalization()
        {
            if(homeRoot==null)return;
            SetLocalizedCard(startCard,"menu.session.start","menu.start.description");
            SetLocalizedCard(sessionsCard,"menu.my_sessions","menu.sessions.description");
            SetLocalizedCard(accountCard,"pairing.account.title","pairing.account.disconnected");
            SetLocalizedCard(gesturesCard,"menu.hand_gestures","menu.gestures.description");
            SetLocalizedCard(helpVoiceCard,"menu.voice_commands","menu.voice.description");
            SetLocalizedCard(helpGesturesCard,"menu.hand_gestures","menu.gestures.description");
            SetLocalizedButton(settingsButton,"menu.settings");SetLocalizedButton(helpButton,"menu.help");
            SetLocalizedButton(closeButton,"common.close");SetLocalizedButton(homeButton,"menu.home");
            if(statusLabel!=null)statusLabel.text="● "+ThinkingSpaceLocalizationController.Get("menu.ready").ToUpperInvariant();
        }

        public void ApplyPaletteToFunctionalPanels(params GameObject[] panels)
        {
            if(panels==null)return;
            for(int i=0;i<panels.Length;i++)
            {
                GameObject panel=panels[i];if(panel==null)continue;
                Image background=panel.GetComponent<Image>();
                if(background!=null){ApplyRoundedSurface(background,QuestUIDesignTokens.Panel);background.raycastTarget=true;}
                // Keep one subtle full-panel edge. Applying an Outline to the panel as well as
                // the dedicated border doubled the cyan frame on Quest and made it look heavy.
                Outline duplicatePanelOutline=panel.GetComponent<Outline>();
                if(duplicatePanelOutline!=null)
                {
                    duplicatePanelOutline.enabled=false;
                    if(Application.isPlaying)Object.Destroy(duplicatePanelOutline);
                    else Object.DestroyImmediate(duplicatePanelOutline);
                }
                DisableRedundantInsetBorder(panel);
                Image[] surfaces=panel.GetComponentsInChildren<Image>(true);
                for(int j=0;j<surfaces.Length;j++)
                {
                    Image surface=surfaces[j];
                    if(surface==null||surface==background||surface.GetComponent<Button>()!=null)continue;
                    string surfaceName=surface.name.ToLowerInvariant();
                    if(surfaceName=="designkit thin border")
                    {
                        surface.color=Color.clear;surface.raycastTarget=false;continue;
                    }
                    if(surfaceName.Contains("decoration")||surfaceName.Contains("functional page content"))
                    {
                        surface.color=Color.clear;surface.raycastTarget=false;continue;
                    }
                    RectTransform surfaceRect=surface.rectTransform;
                    Vector2 anchorSpan=surfaceRect.anchorMax-surfaceRect.anchorMin;
                    bool largeSurface=anchorSpan.x>=.5f&&anchorSpan.y>=.3f
                        ||surfaceRect.sizeDelta.x>=300f&&surfaceRect.sizeDelta.y>=180f;
                    bool structural=surface.sprite==null||surfaceName.Contains("background")
                        ||surfaceName.Contains("panel")||surfaceName.Contains("frame")
                        ||surfaceName.Contains("container")||surfaceName.Contains("row")||largeSurface;
                    if(!structural)continue;
                    ApplyRoundedSurface(surface,surfaceName.Contains("row")?QuestUIDesignTokens.Raised:
                        new Color(QuestUIDesignTokens.Panel.r,QuestUIDesignTokens.Panel.g,
                            QuestUIDesignTokens.Panel.b,.97f));
                    surface.raycastTarget=false;
                }
                Button[] buttons=panel.GetComponentsInChildren<Button>(true);
                for(int j=0;j<buttons.Length;j++)
                {
                    ThinkingSpaceUIStyler.StyleButton(buttons[j],
                        buttons[j].name.Contains("Previous")||buttons[j].name.Contains("Next")
                            ?ThinkingSpaceButtonKind.Navigation:ThinkingSpaceButtonKind.Primary);
                    Image image=buttons[j].GetComponent<Image>();
                    if(image!=null)ApplyRoundedSurface(image,Color.white);
                    ApplyButtonStates(buttons[j]);
                    AddOutline(buttons[j].gameObject,new Color(QuestUIDesignTokens.Cyan.r,
                        QuestUIDesignTokens.Cyan.g,QuestUIDesignTokens.Cyan.b,.60f),.8f);
                }
                TMP_Text[] labels=panel.GetComponentsInChildren<TMP_Text>(true);
                for(int j=0;j<labels.Length;j++)
                {
                    labels[j].color=QuestUIDesignTokens.Text;
                    labels[j].raycastTarget=false;
                }
            }
        }

        public void PrepareFunctionalPanelForDisplay(GameObject panel)
        {
            if(panel==null)return;
            ApplyPaletteToFunctionalPanels(panel);
            panel.transform.SetAsLastSibling();
        }

        private Button CreateCard(Button source,RectTransform parent,string name,Sprite icon,Color accent,Vector2 min,Vector2 max,UnityEngine.Events.UnityAction action)
        {
            Button button=CloneButton(source,parent,name,min,max);button.gameObject.AddComponent<QuestUIActionCard>().Configure(name,button,button.GetComponentInChildren<TMP_Text>(true));
            Bind(button,action);
            Image background=button.GetComponent<Image>();if(background!=null)ApplyRoundedSurface(background,Color.white);
            AddOutline(button.gameObject,accent,2.5f);CreateIcon("Icon",button.transform as RectTransform,icon,new Vector2(.055f,.54f),new Vector2(.19f,.90f));
            TMP_Text title=button.GetComponentInChildren<TMP_Text>(true);if(title!=null){title.alignment=TextAlignmentOptions.TopLeft;title.enableAutoSizing=false;title.fontSize=28;title.margin=new Vector4(118,20,24,58);}
            TMP_Text description=Object.Instantiate(title,button.transform);description.name="Description";description.fontSize=18;description.color=QuestUIDesignTokens.Muted;description.alignment=TextAlignmentOptions.BottomLeft;description.margin=new Vector4(118,62,24,20);
            return button;
        }

        private void ApplyRoundedSurface(Image image,Color color)
        {
            if(image==null)return;
            image.material=null;image.color=color;
            if(roundedPanel!=null){image.sprite=roundedPanel;image.type=Image.Type.Sliced;}
            else{image.sprite=null;image.type=Image.Type.Simple;}
        }

        private static void DisableRedundantInsetBorder(GameObject panel)
        {
            Transform existing=panel.transform.Find("DesignKit Thin Border");
            if(existing==null)return;
            Image image=existing.GetComponent<Image>();if(image!=null){image.raycastTarget=false;image.color=Color.clear;}
            Outline outline=existing.GetComponent<Outline>();if(outline!=null)outline.enabled=false;
            existing.gameObject.SetActive(false);
        }

        private static void ApplyButtonStates(Button button)
        {
            if(button==null)return;
            ColorBlock colors=button.colors;
            colors.normalColor=QuestUIDesignTokens.Raised;
            colors.highlightedColor=QuestUIDesignTokens.Hover;
            colors.pressedColor=QuestUIDesignTokens.Pressed;
            colors.selectedColor=QuestUIDesignTokens.Cyan;
            colors.disabledColor=QuestUIDesignTokens.Disabled;
            colors.colorMultiplier=1f;colors.fadeDuration=.08f;
            button.colors=colors;
        }

        private static void AddOutline(GameObject target,Color color,float width){Outline outline=target.GetComponent<Outline>()??target.AddComponent<Outline>();outline.effectColor=color;outline.effectDistance=new Vector2(width,-width);outline.useGraphicAlpha=true;}
        private static void PlaceLegacyFrameBehindActiveContent(RectTransform canvas)
        {
            if(canvas==null)return;
            Transform frame=FindDeep(canvas,"SlimUI Template Frame");
            if(frame==null)return;
            Image image=frame.GetComponent<Image>();if(image!=null){image.raycastTarget=false;image.color=Color.clear;}
            Outline outline=frame.GetComponent<Outline>();if(outline!=null)outline.enabled=false;
            int backgroundIndex=0;Transform background=FindDeep(canvas,"SlimUI Template Background");
            if(background!=null)backgroundIndex=background.GetSiblingIndex()+1;
            frame.SetSiblingIndex(Mathf.Clamp(backgroundIndex,0,Mathf.Max(0,frame.parent.childCount-1)));
        }
        private static void CreateIcon(string name,RectTransform parent,Sprite sprite,Vector2 min,Vector2 max){GameObject go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);RectTransform rect=go.GetComponent<RectTransform>();Stretch(rect,min,max);Image image=go.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;image.color=sprite==null?new Color(QuestUIDesignTokens.Cyan.r,QuestUIDesignTokens.Cyan.g,QuestUIDesignTokens.Cyan.b,.3f):Color.white;}

        private static void AdoptNavigation(Transform sourceRoot,RectTransform footer,string name,Vector2 min,Vector2 max)
        {
            Transform item=FindDeep(sourceRoot,name);if(item==null)return;item.SetParent(footer,false);
            RectTransform rect=item as RectTransform;if(rect!=null)Stretch(rect,min,max);
        }

        private static Transform FindDeep(Transform root,string name)
        {if(root.name==name)return root;for(int i=0;i<root.childCount;i++){Transform found=FindDeep(root.GetChild(i),name);if(found!=null)return found;}return null;}

        private static Button CloneButton(Button source,RectTransform parent,string name,Vector2 min,Vector2 max)
        {
            Button button=Object.Instantiate(source,parent);button.name=name;button.gameObject.SetActive(true);button.interactable=true;
            RectTransform rect=button.GetComponent<RectTransform>();Stretch(rect,min,max);button.onClick.RemoveAllListeners();
            ThinkingSpaceUIStyler.StyleButton(button,name=="Close"?ThinkingSpaceButtonKind.Icon:ThinkingSpaceButtonKind.Primary);
            Image image=button.GetComponent<Image>();if(image!=null)image.color=Color.white;
            ApplyButtonStates(button);
            return button;
        }

        private static void Bind(Button button,UnityEngine.Events.UnityAction action){button.onClick.RemoveAllListeners();if(action!=null)button.onClick.AddListener(action);}
        private static void SetLocalizedButton(Button button,string key)
        {TMP_Text text=button!=null?button.GetComponentInChildren<TMP_Text>(true):null;if(text==null)return;ThinkingSpaceLocalizedText marker=text.GetComponent<ThinkingSpaceLocalizedText>();if(marker==null)marker=text.gameObject.AddComponent<ThinkingSpaceLocalizedText>();marker.Configure(key);}
        private static void SetLocalizedCard(Button button,string titleKey,string descriptionKey)
        {if(button==null)return;TMP_Text[] labels=button.GetComponentsInChildren<TMP_Text>(true);for(int i=0;i<labels.Length;i++){string key=labels[i].name=="Description"?descriptionKey:titleKey;ThinkingSpaceLocalizedText marker=labels[i].GetComponent<ThinkingSpaceLocalizedText>();if(marker==null)marker=labels[i].gameObject.AddComponent<ThinkingSpaceLocalizedText>();marker.Configure(key);}}
        private static RectTransform CreateRegion(string name,RectTransform parent,Vector2 min,Vector2 max,Color color)
        {GameObject go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);RectTransform rect=go.GetComponent<RectTransform>();Stretch(rect,min,max);Image image=go.GetComponent<Image>();image.color=color;image.raycastTarget=false;return rect;}
        private static TMP_Text CreateText(string name,RectTransform parent,string value,float size,Vector2 min,Vector2 max,TextAlignmentOptions alignment,Color color)
        {GameObject go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);RectTransform rect=go.GetComponent<RectTransform>();Stretch(rect,min,max);TMP_Text text=go.GetComponent<TMP_Text>();text.text=value;text.fontSize=size;text.color=color;text.alignment=alignment;text.raycastTarget=false;return text;}
        private static void Stretch(RectTransform rect,Vector2 min,Vector2 max){rect.anchorMin=min;rect.anchorMax=max;rect.anchoredPosition=Vector2.zero;rect.sizeDelta=Vector2.zero;rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;}
        private static string Active(GameObject value)=>value!=null&&value.activeInHierarchy?"1":"0";
    }
}

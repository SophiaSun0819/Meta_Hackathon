using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI.DesignKit;

namespace ThinkingSpace.UI.Editor
{
    public static class QuestUIDesignKitPrefabBuilder
    {
        public const string PrefabRoot="Assets/ThinkingSpace/UI/Prefabs/DesignKit";
        public const string PreviewPath=PrefabRoot+"/QuestUIDesignKitPreview.prefab";
        private const string GraphicsRoot="Assets/ThinkingSpace/UI/Graphics/DesignKit";
        private static TMP_FontAsset regular,bold;

        [MenuItem("Thinking Space/UI/Build approved Quest design kit")]
        public static void Build()
        {
            EnsureFolder("Assets/ThinkingSpace/UI/Graphics","DesignKit");
            EnsureFolder("Assets/ThinkingSpace/UI/Prefabs","DesignKit");
            EnsureSpriteImports();
            LoadFonts();
            SaveComponent("QuestUIPanelShell",CreatePanelShell());
            SaveComponent("QuestUIHeader",CreateHeader());
            SaveComponent("QuestUIFooter",CreateFooter());
            SaveComponent("QuestUIActionCard",CreateActionCard("new-session","New Session"));
            SaveComponent("QuestUISegmentedControl",CreateSegmentedControl());
            SaveComponent("QuestUIStatus",CreateStatus());
            SaveComponent("QuestUIConfirmation",CreateConfirmation());
            SaveComponent("QuestUIIdeaDetail",CreateIdeaDetail());
            SaveComponent("QuestUIOrbySpeechBubble",CreateOrbySpeechBubble());
            SaveComponent("QuestUIMicrophoneIndicator",CreateMicrophoneIndicator());
            GameObject preview=CreatePreview();
            PrefabUtility.SaveAsPrefabAsset(preview,PreviewPath);
            UnityEngine.Object.DestroyImmediate(preview);
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("UNITY-027 staging prefabs generated; no scene references were changed.");
        }

        public static void RenderPreview()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PreviewPath);
            if(prefab==null)throw new InvalidOperationException("Build the staging preview before rendering it.");
            GameObject instance=PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            GameObject cameraObject=new GameObject("UNITY-027 Preview Camera",typeof(Camera));Camera camera=cameraObject.GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.008f,.018f,.03f,1f);camera.orthographic=true;camera.orthographicSize=.255f;
            camera.transform.position=new Vector3(0,0,-2f);camera.transform.rotation=Quaternion.identity;
            Canvas canvas=instance.GetComponent<Canvas>();canvas.worldCamera=camera;canvas.sortingOrder=25;
            Canvas.ForceUpdateCanvases();
            RenderTexture target=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();
            RenderTexture.active=target;Texture2D image=new Texture2D(960,640,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();
            File.WriteAllBytes("C:/Users/Henry/Documents/Codex/UNITY027-design-kit-preview.png",image.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("UNITY-027 preview screenshot rendered.");
        }

        private static GameObject CreatePanelShell()
        {
            GameObject root=Node("Quest UI Panel Shell",new Vector2(QuestUIDesignTokens.CanvasWidth,QuestUIDesignTokens.CanvasHeight));
            Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Panel;
            root.AddComponent<BasePanel>().Configure(true,true);image.color=QuestUIDesignTokens.Panel;
            Transform decoration=root.transform.Find("SlimUI Decoration");if(decoration!=null)decoration.GetComponent<Image>().color=new Color(1f,1f,1f,.08f);
            root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.PanelShell);
            return root;
        }

        private static GameObject CreatePreview()
        {
            GameObject root=CreatePanelShell();root.name="Quest UI Design Kit Preview (Unreferenced)";
            Canvas canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.sortingOrder=25;
            root.AddComponent<GraphicRaycaster>();root.AddComponent<MetaHandInteractionAdapter>();
            RectTransform rr=root.GetComponent<RectTransform>();rr.localScale=Vector3.one*QuestUIDesignTokens.WorldScale;

            GameObject header=Node("Header",new Vector2(904,82),root.transform);Anchor(header,new Vector2(0,1),new Vector2(1,1),new Vector2(28,-96),new Vector2(-28,-14));
            header.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Header);
            Image topLine=ImageNode("Decorative Line",new Vector2(904,3),header.transform,null);topLine.color=new Color(QuestUIDesignTokens.Cyan.r,QuestUIDesignTokens.Cyan.g,QuestUIDesignTokens.Cyan.b,.72f);topLine.raycastTarget=false;Place(topLine.rectTransform,new Vector2(0,40),new Vector2(904,3));
            Image orb=ImageNode("Brand Orb",new Vector2(58,58),header.transform,LoadSprite("thinking-space-orb"));orb.raycastTarget=false;Place(orb.rectTransform,new Vector2(-414,0),new Vector2(58,58));
            TMP_Text heading=TextNode("Title","THINKING SPACE\nMain menu",header.transform,28,TextAlignmentOptions.Left,bold);Place(heading.rectTransform,new Vector2(-235,0),new Vector2(280,76));
            Button settings=AddButton("Settings",header.transform,"Settings",false,112,58);Place(settings.GetComponent<RectTransform>(),new Vector2(72,0),new Vector2(112,58));SetFixedButtonText(settings,17);
            Button help=AddButton("Help",header.transform,"Help",false,82,58);Place(help.GetComponent<RectTransform>(),new Vector2(182,0),new Vector2(82,58));SetFixedButtonText(help,17);
            TMP_Text chip=TextNode("Status Chip","●  READY",header.transform,15,TextAlignmentOptions.Center,bold);chip.color=QuestUIDesignTokens.Mint;Place(chip.rectTransform,new Vector2(294,0),new Vector2(118,48));
            Button close=AddButton("Close",header.transform,"×",false,52,52);Place(close.GetComponent<RectTransform>(),new Vector2(405,0),new Vector2(52,52));SetFixedButtonText(close,26);

            GameObject content=Node("Content - Four Primary Actions",new Vector2(904,398),root.transform);Anchor(content,new Vector2(0,0),new Vector2(1,1),new Vector2(28,112),new Vector2(-28,-118));
            GridLayoutGroup grid=content.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(440,184);grid.spacing=new Vector2(14,14);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;
            AddActionCard("new-session","New Session","Begin a new spatial brainstorm","thinking-space-orb","START",content.transform);
            AddActionCard("my-sessions","My Sessions","Open, import, or export saved work","status","LOCAL",content.transform);
            AddActionCard("voice-commands","Voice Commands","See what you can ask Orby","speech","GUIDE",content.transform);
            AddActionCard("hand-gestures","Hand Gestures","Learn select, move, and record gestures","gesture-hand","GUIDE",content.transform);

            GameObject footer=Node("Footer",new Vector2(904,76),root.transform);Anchor(footer,new Vector2(0,0),new Vector2(1,0),new Vector2(28,18),new Vector2(-28,94));
            footer.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Footer);
            HorizontalLayoutGroup footerLayout=footer.AddComponent<HorizontalLayoutGroup>();footerLayout.spacing=22;footerLayout.childAlignment=TextAnchor.MiddleCenter;footerLayout.childForceExpandWidth=false;
            AddButton("Previous",footer.transform,"Previous",false,190,64);
            TMP_Text page=TextNode("Page Indicator","PAGE 1 OF 1",footer.transform,16,TextAlignmentOptions.Center,bold);page.color=QuestUIDesignTokens.Muted;AddLayout(page.gameObject,260,64);
            AddButton("Next",footer.transform,"Next",true,190,64);
            return root;
        }

        private static GameObject CreateActionCard(string id,string label,string description=null,string iconName=null,string badge=null)
        {
            GameObject card=Node("Action Card - "+label,new Vector2(440,184));Image background=card.AddComponent<Image>();background.color=Color.white;background.sprite=LoadSprite("rounded-panel");background.type=Image.Type.Sliced;
            Button button=card.AddComponent<Button>();button.targetGraphic=background;
            SecondaryButton style=card.AddComponent<SecondaryButton>();
            background.sprite=LoadSprite("rounded-panel");background.type=Image.Type.Sliced;background.color=Color.white;button.targetGraphic=background;
            Image icon=ImageNode("Icon",new Vector2(56,56),card.transform,LoadSprite(iconName??"status"));icon.raycastTarget=false;Place(icon.rectTransform,new Vector2(-168,38),new Vector2(56,56));
            TMP_Text text=TextNode("Label",label,card.transform,25,TextAlignmentOptions.Left,bold);Place(text.rectTransform,new Vector2(38,48),new Vector2(330,42));
            TMP_Text body=TextNode("Description",description??"Reusable action",card.transform,16,TextAlignmentOptions.TopLeft,regular);body.color=QuestUIDesignTokens.Muted;Place(body.rectTransform,new Vector2(38,-5),new Vector2(330,54));
            TMP_Text badgeText=TextNode("Badge",badge??"ACTION",card.transform,13,TextAlignmentOptions.Center,bold);badgeText.color=QuestUIDesignTokens.Cyan;Place(badgeText.rectTransform,new Vector2(130,-66),new Vector2(126,28));
            QuestUIActionCard action=card.AddComponent<QuestUIActionCard>();action.Configure(id,button,text);
            card.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.ActionCard);
            return card;
        }

        private static void AddActionCard(string id,string label,string description,string icon,string badge,Transform parent)
        {CreateActionCard(id,label,description,icon,badge).transform.SetParent(parent,false);}

        private static GameObject CreateHeader()
        {
            GameObject root=Node("Quest UI Header",new Vector2(904,82));root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Header);
            HorizontalLayoutGroup layout=root.AddComponent<HorizontalLayoutGroup>();layout.spacing=12;layout.childAlignment=TextAnchor.MiddleLeft;layout.childForceExpandWidth=false;
            TMP_Text title=TextNode("Title","THINKING SPACE",root.transform,32,TextAlignmentOptions.Left,bold);AddLayout(title.gameObject,500,82,1);
            AddButton("Settings",root.transform,"Settings",false,120,64);AddButton("Help",root.transform,"Help",false,94,64);return root;
        }

        private static GameObject CreateFooter()
        {
            GameObject root=Node("Quest UI Footer",new Vector2(904,76));root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Footer);
            HorizontalLayoutGroup layout=root.AddComponent<HorizontalLayoutGroup>();layout.spacing=22;layout.childAlignment=TextAnchor.MiddleCenter;layout.childForceExpandWidth=false;
            AddButton("Previous",root.transform,"Previous",false,190,64);TMP_Text page=TextNode("Page","PAGE 1 OF 2",root.transform,16,TextAlignmentOptions.Center,bold);AddLayout(page.gameObject,260,64);AddButton("Next",root.transform,"Next",true,190,64);return root;
        }

        private static GameObject CreateSegmentedControl()
        {
            GameObject root=Node("Quest UI Segmented Control",new Vector2(560,72));root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.SegmentedControl);
            HorizontalLayoutGroup layout=root.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childForceExpandWidth=false;
            Button first=AddButton("First",root.transform,"Interface",true,270,72);Button second=AddButton("Second",root.transform,"Conversation",false,270,72);
            root.AddComponent<QuestUISegmentedControl>().Configure(new[]{first,second});return root;
        }

        private static GameObject CreateStatus()
        {
            GameObject root=Node("Quest UI Status",new Vector2(436,126));Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Raised;root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Status);
            TMP_Text title=TextNode("Title","Session saved",root.transform,23,TextAlignmentOptions.Left,bold);Place(title.rectTransform,new Vector2(-62,33),new Vector2(270,38));
            TMP_Text body=TextNode("Body","Your local session is safe.",root.transform,16,TextAlignmentOptions.Left,regular);Place(body.rectTransform,new Vector2(-62,-14),new Vector2(270,48));
            TMP_Text state=TextNode("State","READY",root.transform,15,TextAlignmentOptions.Center,bold);state.color=QuestUIDesignTokens.Mint;Place(state.rectTransform,new Vector2(158,0),new Vector2(96,48));
            root.AddComponent<QuestUIStatusView>().Configure(title,body,state);return root;
        }

        private static GameObject CreateMicrophoneIndicator()
        {
            GameObject root=Node("Quest UI Microphone Indicator",new Vector2(680,150));Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Panel;root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.MicrophoneIndicator);
            Image icon=ImageNode("Microphone Activity",new Vector2(96,96),root.transform,LoadSprite("waveform"));icon.raycastTarget=false;Place(icon.rectTransform,new Vector2(-248,0),new Vector2(96,96));
            TMP_Text state=TextNode("State","LISTENING",root.transform,28,TextAlignmentOptions.Left,bold);Place(state.rectTransform,new Vector2(88,0),new Vector2(500,80));
            root.AddComponent<QuestUIMicrophoneIndicator>().Configure(state);return root;
        }

        private static GameObject CreateConfirmation()
        {
            GameObject root=Node("Quest UI Confirmation",new Vector2(620,320));Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Panel;root.AddComponent<ModalPanel>();
            root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.Confirmation);
            TMP_Text title=TextNode("Title","End this session?",root.transform,32,TextAlignmentOptions.Center,bold);Place(title.rectTransform,new Vector2(0,96),new Vector2(560,52));
            TMP_Text body=TextNode("Body","Your ideas are safe. Choose whether to keep working or save and end.",root.transform,20,TextAlignmentOptions.Center,regular);Place(body.rectTransform,new Vector2(0,32),new Vector2(540,76));
            Button keep=AddButton("Keep Working",root.transform,"Keep Working",false,250,72);Place(keep.GetComponent<RectTransform>(),new Vector2(-142,-92),new Vector2(250,72));
            Button end=AddButton("Save and End",root.transform,"Save & End Session",true,250,72);Place(end.GetComponent<RectTransform>(),new Vector2(142,-92),new Vector2(250,72));
            root.AddComponent<QuestUIConfirmationView>().Configure(end,keep);return root;
        }

        private static GameObject CreateIdeaDetail()
        {
            GameObject root=Node("Quest UI Idea Detail",new Vector2(900,350));Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Panel;root.AddComponent<CompactPanel>();
            root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.IdeaDetail);
            TMP_Text title=TextNode("Title","A resilient city garden",root.transform,32,TextAlignmentOptions.Left,bold);Place(title.rectTransform,new Vector2(-10,132),new Vector2(830,52));
            TMP_Text meta=TextNode("Metadata","ENVIRONMENT  •  JUST NOW",root.transform,15,TextAlignmentOptions.Left,bold);meta.color=QuestUIDesignTokens.Cyan;Place(meta.rectTransform,new Vector2(-10,94),new Vector2(830,30));
            TMP_Text description=TextNode("Description","Use modular raised beds, shared water storage, and neighborhood stewardship to keep food growing through disruption.",root.transform,20,TextAlignmentOptions.TopLeft,regular);Place(description.rectTransform,new Vector2(-10,18),new Vector2(830,105));
            Button challenge=AddButton("Challenge",root.transform,"Challenge",false,250,58);Place(challenge.GetComponent<RectTransform>(),new Vector2(-278,-125),new Vector2(250,58));
            Button expand=AddButton("Expand",root.transform,"Expand",false,250,58);Place(expand.GetComponent<RectTransform>(),new Vector2(0,-125),new Vector2(250,58));
            Button connect=AddButton("Connect",root.transform,"Connect",true,250,58);Place(connect.GetComponent<RectTransform>(),new Vector2(278,-125),new Vector2(250,58));
            root.AddComponent<QuestUIIdeaDetailView>().Configure(title,meta,description,challenge,expand,connect);return root;
        }

        private static GameObject CreateOrbySpeechBubble()
        {
            GameObject root=Node("Quest UI Orby Speech Bubble - Subtitle",new Vector2(600,170));Image image=root.AddComponent<Image>();image.color=QuestUIDesignTokens.Panel;root.AddComponent<CompactPanel>();
            root.AddComponent<QuestUIDesignComponent>().Configure(QuestUIComponentKind.OrbySubtitle);
            Image orb=ImageNode("Orby Avatar",new Vector2(92,92),root.transform,LoadSprite("thinking-space-orb"));orb.raycastTarget=false;Place(orb.rectTransform,new Vector2(-234,0),new Vector2(92,92));
            TMP_Text subtitle=TextNode("Subtitle","I found a useful connection between these ideas.",root.transform,24,TextAlignmentOptions.MidlineLeft,regular);Place(subtitle.rectTransform,new Vector2(66,0),new Vector2(430,116));
            root.AddComponent<QuestUIOrbySpeechBubble>().Configure(subtitle,false);return root;
        }

        private static Button AddButton(string name,Transform parent,string label,bool primary,float width,float height)
        {
            GameObject go=Node(name,new Vector2(width,height),parent);Image image=go.AddComponent<Image>();image.color=primary?QuestUIDesignTokens.Cyan:QuestUIDesignTokens.Raised;
            Button button=go.AddComponent<Button>();button.targetGraphic=image;ThinkingSpaceButtonStyle style=primary?(ThinkingSpaceButtonStyle)go.AddComponent<PrimaryButton>():go.AddComponent<SecondaryButton>();
            TMP_Text text=TextNode("Label",label,go.transform,22,TextAlignmentOptions.Center,bold);Stretch(text.rectTransform,12,8);style.ConfigureLabel(text);AddLayout(go,width,height);return button;
        }

        private static TMP_Text TextNode(string name,string value,Transform parent,float size,TextAlignmentOptions alignment,TMP_FontAsset font)
        {GameObject go=Node(name,new Vector2(100,40),parent);TextMeshProUGUI text=go.AddComponent<TextMeshProUGUI>();text.text=value;text.font=font;text.fontSize=size;text.color=QuestUIDesignTokens.Text;text.alignment=alignment;text.enableWordWrapping=true;text.raycastTarget=false;return text;}
        private static Image ImageNode(string name,Vector2 size,Transform parent,Sprite sprite){GameObject go=Node(name,size,parent);Image image=go.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;return image;}
        private static GameObject Node(string name,Vector2 size,Transform parent=null){GameObject go=new GameObject(name,typeof(RectTransform));if(parent!=null)go.transform.SetParent(parent,false);go.GetComponent<RectTransform>().sizeDelta=size;return go;}
        private static void SaveComponent(string name,GameObject go){PrefabUtility.SaveAsPrefabAsset(go,PrefabRoot+"/"+name+".prefab");UnityEngine.Object.DestroyImmediate(go);}
        private static void LoadFonts(){regular=FindFont("Rubik Light SDF");bold=FindFont("Rubik Bold SDF");if(regular==null)regular=TMP_Settings.defaultFontAsset;if(bold==null)bold=regular;}
        private static TMP_FontAsset FindFont(string name){string[] ids=AssetDatabase.FindAssets(name+" t:TMP_FontAsset");return ids.Length>0?AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(ids[0])):null;}
        private static Sprite LoadSprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(GraphicsRoot+"/"+name+".png");
        private static void EnsureSpriteImports()
        {
            foreach(string file in Directory.GetFiles(GraphicsRoot,"*.png"))
            {
                string path=file.Replace('\\','/');TextureImporter importer=AssetImporter.GetAtPath(path) as TextureImporter;if(importer==null)continue;
                importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
                if(Path.GetFileNameWithoutExtension(path)=="rounded-panel")
                {importer.spriteBorder=new Vector4(24,24,24,24);importer.SaveAndReimport();}
            }
        }
        private static void EnsureFolder(string parent,string child){string path=parent+"/"+child;if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(parent,child);}
        private static void AddLayout(GameObject go,float width,float height,float flexible=0){LayoutElement item=go.GetComponent<LayoutElement>()??go.AddComponent<LayoutElement>();item.preferredWidth=width;item.preferredHeight=height;item.flexibleWidth=flexible;}
        private static void SetFixedButtonText(Button button,float size){TMP_Text text=button.GetComponentInChildren<TMP_Text>();if(text==null)return;text.enableAutoSizing=false;text.fontSize=size;text.enableWordWrapping=false;}
        private static void Stretch(RectTransform rect,float horizontal,float vertical){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(horizontal,vertical);rect.offsetMax=new Vector2(-horizontal,-vertical);}
        private static void Place(RectTransform rect,Vector2 position,Vector2 size){rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;}
        private static void Anchor(GameObject go,Vector2 min,Vector2 max,Vector2 offsetMin,Vector2 offsetMax){RectTransform rect=go.GetComponent<RectTransform>();rect.anchorMin=min;rect.anchorMax=max;rect.offsetMin=offsetMin;rect.offsetMax=offsetMax;}
    }
}

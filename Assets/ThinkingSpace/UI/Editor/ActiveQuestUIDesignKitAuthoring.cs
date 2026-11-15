using ThinkingSpace.UI.DesignKit;
using ThinkingSpace.Speech;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ThinkingSpace.UI.Editor
{
    public static class ActiveQuestUIDesignKitAuthoring
    {
        private const string MenuPrefab="Assets/ThinkingSpace/UI/Prefabs/BrainstormMenuTemplate.prefab";
        private const string CaptureFolder="C:/Users/Henry/Documents/Codex/UNITY-030-active-menu";

        [MenuItem("Thinking Space/UI/Integrate Active Quest Design Kit")]
        public static void Integrate()
        {
            GameObject root=PrefabUtility.LoadPrefabContents(MenuPrefab);
            try
            {
                QuestUIActiveMenuSkin skin=root.GetComponent<QuestUIActiveMenuSkin>()??root.AddComponent<QuestUIActiveMenuSkin>();
                skin.ConfigureVisualAssets(LoadSprite("rounded-panel"),LoadSprite("thinking-space-orb"),
                    LoadSprite("status"),LoadSprite("speech"),LoadSprite("gesture-hand"));
                PrefabUtility.SaveAsPrefabAsset(root,MenuPrefab);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Thinking Space/UI/Capture Active Quest Menu")]
        public static void CaptureActiveMenu()
        {
            Directory.CreateDirectory(CaptureFolder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefab);
            GameObject root=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            BrainstormMenuView view=root.GetComponent<BrainstormMenuView>();
            MethodInfo awake=typeof(BrainstormMenuView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic);
            awake?.Invoke(view,null);
            int starts=0,voices=0,gestures=0,closes=0;
            view.Bind(()=>starts++,()=>voices++,()=>gestures++,()=>{},()=>{},()=>closes++,
                ()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},
                ()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{});
            List<BrainstormSession> sessions=new List<BrainstormSession>{new BrainstormSession(1),new BrainstormSession(2)};
            view.ShowMain(sessions,ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
            LogLargeActiveImages(root);

            Camera camera=CreateCaptureCamera();
            PrepareCanvas(view.Canvas,camera);
            Capture(camera,"01-active-home.png");
            ValidateHome(root,starts,voices,gestures,closes);
            view.ShowDesignKitSessions();Capture(camera,"02-active-sessions.png");
            view.ShowDesignKitSettings();LogActiveButtons(root,"SETTINGS");Capture(camera,"03-active-settings.png");
            view.ShowDesignKitHelp();Capture(camera,"04-active-help.png");
            view.ShowSession(1,ThumbRecordingGestureController.RecordingHand.Right);
            Capture(camera,"05-active-session-disabled-actions.png");
            view.ShowMain(sessions,ThumbRecordingGestureController.RecordingHand.Right,_=>{},_=>{});
            Capture(camera,"06-session-end-reopen-home.png");
            Debug.Log("UNITY030_ACTIVE_CAPTURE_OK folder="+CaptureFolder);
            Object.DestroyImmediate(root);Object.DestroyImmediate(camera.gameObject);
        }

        private static Sprite LoadSprite(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/ThinkingSpace/UI/Graphics/DesignKit/"+name+".png");

        private static Camera CreateCaptureCamera()
        {
            GameObject go=new GameObject("UNITY-030 Capture Camera",typeof(Camera));
            Camera camera=go.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.015f,.025f,.045f,1f);camera.orthographic=true;
            camera.allowHDR=false;camera.allowMSAA=false;
            return camera;
        }

        private static void PrepareCanvas(Canvas canvas,Camera camera)
        {
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1f;
            CanvasScaler scaler=canvas.GetComponent<CanvasScaler>();if(scaler==null)scaler=canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440f,960f);
        }

        private static void Capture(Camera camera,string fileName)
        {
            RenderTexture target=new RenderTexture(1440,960,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.Create();camera.targetTexture=target;
            RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
            GL.Clear(true,true,camera.backgroundColor);
            Canvas.ForceUpdateCanvases();camera.Render();
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
            Texture2D image=new Texture2D(1440,960,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,1440,960),0,0);image.Apply();
            File.WriteAllBytes(Path.Combine(CaptureFolder,fileName),image.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);target.Release();Object.DestroyImmediate(target);
        }

        private static void ValidateHome(GameObject root,int starts,int voices,int gestures,int closes)
        {
            Transform home=FindDeep(root.transform,"DesignKit Active Home");
            if(home==null)throw new MissingReferenceException("Active DesignKit home was not generated.");
            string[] required={"Start New Session","My Sessions","Voice Commands","Hand Gestures","Settings","Help","Close","Home"};
            foreach(string name in required)
            {
                Button button=FindButton(root.transform,name);if(button==null)throw new MissingReferenceException("Missing active button: "+name);
            }
            if(root.GetComponentsInChildren<Oculus.Interaction.PointableCanvas>(true).Length!=1)
                throw new System.InvalidOperationException("Active menu must retain exactly one PointableCanvas.");
            if(starts+voices+gestures+closes!=0)throw new System.InvalidOperationException("Capture unexpectedly invoked a callback.");
        }

        private static Button FindButton(Transform root,string name)
        {
            Button[] buttons=root.GetComponentsInChildren<Button>(true);
            for(int i=0;i<buttons.Length;i++)if(buttons[i].name==name)return buttons[i];
            return null;
        }

        private static Transform FindDeep(Transform root,string name)
        {
            if(root.name==name)return root;
            for(int i=0;i<root.childCount;i++){Transform found=FindDeep(root.GetChild(i),name);if(found!=null)return found;}
            return null;
        }

        private static void LogActiveButtons(GameObject root,string state)
        {
            foreach(Button button in root.GetComponentsInChildren<Button>(false))
            {
                RectTransform rect=button.transform as RectTransform;
                Debug.Log("UNITY030_RECT state="+state+" name="+button.name+" parent="+button.transform.parent.name+
                    " min="+rect.anchorMin+" max="+rect.anchorMax+" pos="+rect.anchoredPosition+" size="+rect.sizeDelta);
            }
        }

        private static void LogLargeActiveImages(GameObject root)
        {
            foreach(Image image in root.GetComponentsInChildren<Image>(false))
            {
                RectTransform rect=image.rectTransform;
                if(rect.rect.width<800f&&rect.rect.height<500f)continue;
                Debug.Log("UNITY033_IMAGE path="+GetPath(image.transform,root.transform)+" size="+rect.rect.size+
                    " color="+image.color+" material="+(image.material!=null?image.material.name:"none"));
            }
            foreach(CanvasGroup group in root.GetComponentsInChildren<CanvasGroup>(false))
                Debug.Log("UNITY033_CANVAS_GROUP path="+GetPath(group.transform,root.transform)+" alpha="+group.alpha);
        }

        private static string GetPath(Transform value,Transform root)
        {string path=value.name;while(value.parent!=null&&value.parent!=root){value=value.parent;path=value.name+"/"+path;}return path;}
    }
}

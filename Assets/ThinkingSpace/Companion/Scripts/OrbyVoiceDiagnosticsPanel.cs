using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using System.Collections;
using System.Collections.Generic;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbyVoiceDiagnosticsPanel : MonoBehaviour
    {
        private Canvas canvas;
        private GameObject diagnosticsRoot;
        private TMP_Text status;
        private OrbyVoiceDirector director;
        private AudioSource testSource;private AudioClip testClip;private Coroutine testRoutine;
        private string outputTest="Independent output test not run.";private float nextTextUpdate;

        private void Awake()
        {
            director=GetComponent<OrbyVoiceDirector>();
            if(!Debug.isDebugBuild&&!Application.isEditor){enabled=false;return;}
            Build();
        }

        private void Update()
        {
            if(status!=null&&director!=null&&Time.unscaledTime>=nextTextUpdate){nextTextUpdate=Time.unscaledTime+.25f;string resetText="";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                ThinkingSpaceDevelopmentReset reset=FindFirstObjectByType<ThinkingSpaceDevelopmentReset>();resetText=reset!=null?"\n\nRESET\n"+reset.LastStatus:"";
#endif
                status.text=director.DiagnosticText+"\n\nUNITY OUTPUT TEST\n"+outputTest+resetText;}
            if(canvas!=null&&Camera.main!=null)
                canvas.transform.rotation=Quaternion.LookRotation(canvas.transform.position-Camera.main.transform.position,Vector3.up);
        }

        private void Build()
        {
            GameObject root=new GameObject("Voice Diagnostics (Development Only)",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster),typeof(Image));
            diagnosticsRoot=root;
            root.transform.SetParent(transform,false);root.transform.localPosition=new Vector3(.28f,.02f,0);
            canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.overrideSorting=true;canvas.sortingOrder=105;
            RectTransform rect=root.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(660,680);rect.localScale=Vector3.one*.00042f;
            root.GetComponent<Image>().color=new Color(.015f,.025f,.05f,.94f);
            PointableCanvas pointable=root.AddComponent<PointableCanvas>();pointable.InjectAllPointableCanvas(canvas);
            PlaneSurface plane=root.AddComponent<PlaneSurface>();plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward,false);
            BoundsClipper clipper=root.AddComponent<BoundsClipper>();clipper.Size=new Vector3(660,680,.01f);
            ClippedPlaneSurface clipped=root.AddComponent<ClippedPlaneSurface>();clipped.InjectAllClippedPlaneSurface(plane,new List<IBoundsClipper>{clipper});
            RayInteractable ray=root.AddComponent<RayInteractable>();ray.InjectAllRayInteractable(clipped);ray.InjectOptionalPointableElement(pointable);
            PokeInteractable poke=root.AddComponent<PokeInteractable>();poke.InjectAllPokeInteractable(clipped);poke.InjectOptionalPointableElement(pointable);
            status=MakeText(rect,21,new Vector2(.04f,.35f),new Vector2(.96f,.96f));status.alignment=TextAlignmentOptions.TopLeft;
            Button output=MakeButton("Test Unity Audio Output",rect,new Vector2(.04f,.25f),new Vector2(.48f,.33f));output.onClick.AddListener(StartOutputTest);
            Button test=MakeButton("Test Orby Voice",rect,new Vector2(.52f,.25f),new Vector2(.96f,.33f));test.onClick.AddListener(()=>{bool accepted=director!=null&&director.DebugPlayPrerecorded();outputTest="Orby test callback received · request "+(accepted?"accepted":"rejected");});
            Button stop=MakeButton("Stop Audio",rect,new Vector2(.28f,.16f),new Vector2(.72f,.23f));stop.onClick.AddListener(StopAllAudio);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThinkingSpaceDevelopmentReset reset=FindFirstObjectByType<ThinkingSpaceDevelopmentReset>();
            Button onboarding=MakeButton("Reset Onboarding",rect,new Vector2(.03f,.05f),new Vector2(.31f,.13f));onboarding.onClick.AddListener(()=>reset?.RequestOnboardingReset());
            Button full=MakeButton("Request Full Reset",rect,new Vector2(.36f,.05f),new Vector2(.64f,.13f));full.onClick.AddListener(()=>reset?.RequestFullReset());
            Button confirm=MakeButton("Confirm Reset",rect,new Vector2(.69f,.05f),new Vector2(.97f,.13f));confirm.onClick.AddListener(()=>reset?.ConfirmRequestedReset());
#endif
            diagnosticsRoot.SetActive(false);
        }

        public void SetVisible(bool visible)
        {if(diagnosticsRoot!=null)diagnosticsRoot.SetActive(visible&&(Debug.isDebugBuild||Application.isEditor));}

        private void StartOutputTest()
        {
            if(testRoutine!=null)StopCoroutine(testRoutine);CleanupTestSource();testRoutine=StartCoroutine(RunOutputTest());
        }
        private IEnumerator RunOutputTest()
        {
            const int rate=48000;float[] pcm=CreateOutputTestSamples(rate,1.25f,.22f);int samples=pcm.Length;float peak=0f;double sum=0d;
            for(int i=0;i<samples;i++){float value=pcm[i];peak=Mathf.Max(peak,Mathf.Abs(value));sum+=value*value;}
            testClip=AudioClip.Create("Thinking Space 440 Hz Output Test",samples,1,rate,false);testClip.SetData(pcm,0);
            testSource=gameObject.AddComponent<AudioSource>();testSource.playOnAwake=false;testSource.loop=false;testSource.spatialBlend=0;testSource.volume=1;testSource.mute=false;testSource.enabled=true;testSource.priority=0;testSource.ignoreListenerPause=true;testSource.spatialize=false;testSource.outputAudioMixerGroup=null;testSource.clip=testClip;
            int listeners=FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;float rms=Mathf.Sqrt((float)(sum/samples));testSource.Play();yield return null;
            outputTest="Callback received · generated 440 Hz PCM\nSignal RMS/peak: "+rms.ToString("0.000")+"/"+peak.ToString("0.000")+"\nListeners: "+listeners+" · source active/enabled: "+gameObject.activeInHierarchy+"/"+testSource.enabled+"\nPlay called · isPlaying: "+testSource.isPlaying+" · time: "+testSource.time.ToString("0.00");
            float previous=testSource.time;yield return new WaitForSecondsRealtime(.25f);bool progressed=testSource!=null&&testSource.time>previous;
            if(testSource!=null)outputTest+="\nAfter 0.25s · isPlaying: "+testSource.isPlaying+" · time: "+testSource.time.ToString("0.00")+" · progressed: "+progressed;
            while(testSource!=null&&testSource.isPlaying)yield return null;
            outputTest+="\nCompleted; temporary AudioSource removed.";CleanupTestSource();testRoutine=null;
        }
        private void StopAllAudio(){director?.CancelAllSpeech();if(testRoutine!=null){StopCoroutine(testRoutine);testRoutine=null;}outputTest="Stopped explicitly.";CleanupTestSource();}
        private void CleanupTestSource(){if(testSource!=null){testSource.Stop();Destroy(testSource);testSource=null;}if(testClip!=null){Destroy(testClip);testClip=null;}}
        private void OnDestroy(){CleanupTestSource();}
        public static float[] CreateOutputTestSamples(int sampleRate,float duration,float amplitude)
        {int count=Mathf.Max(1,Mathf.RoundToInt(sampleRate*Mathf.Max(.01f,duration)));float[] samples=new float[count];for(int i=0;i<count;i++)samples[i]=Mathf.Clamp01(amplitude)*Mathf.Sin(2f*Mathf.PI*440f*i/sampleRate);return samples;}

        private static TMP_Text MakeText(Transform parent,float size,Vector2 min,Vector2 max)
        {GameObject go=new GameObject("Text",typeof(RectTransform));go.transform.SetParent(parent,false);RectTransform r=go.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;TMP_Text text=go.AddComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;text.fontSize=size;text.enableAutoSizing=false;text.color=Color.white;text.textWrappingMode=TextWrappingModes.Normal;text.raycastTarget=false;return text;}
        private static Button MakeButton(string label,Transform parent,Vector2 min,Vector2 max)
        {GameObject go=new GameObject(label,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);RectTransform r=go.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;go.GetComponent<Image>().color=new Color(.08f,.48f,.72f,1);Button button=go.GetComponent<Button>();TMP_Text text=MakeText(r,22,Vector2.zero,Vector2.one);text.text=label;text.alignment=TextAlignmentOptions.Center;return button;}
    }
}

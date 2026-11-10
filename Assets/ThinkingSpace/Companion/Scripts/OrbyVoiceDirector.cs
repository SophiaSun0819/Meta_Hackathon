using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbyVoiceDirector : MonoBehaviour
    {
        private sealed class Request
        { public string Id,SessionId,Text,CacheKey,EventKey,Delivery;public AudioClip Clip;public OrbyVoicePriority Priority;public bool Idle,CancelOnSessionChange;public Action<bool> Completed; }
        public const string CacheIdentity="orby-voice-contract-v3|voice:E584YCmNqnu35GrWlrqy|primary:eleven_v4_turbo|fallback:eleven_flash_v2_5|mp3_44100_128|tags-v1";
        public static OrbyVoiceDirector Instance { get; private set; }
        public event Action<string,string> SpeakingStarted;
        public event Action<string,bool> SpeakingFinished;
        private readonly List<Request> queue=new List<Request>();
        private readonly HashSet<string> pendingKeys=new HashSet<string>();
        private AudioSource source;private OrbyConversationStateController stateController;private OrbySubtitleView subtitles;
        private OrbyPrerecordedManifest manifest;private OrbyVoiceCache cache;private Coroutine worker;
        private SpeechTranscriptionCoordinator workflow;private ConversationalListeningController listening;
        private ThinkingSpacePreferencesStore preferences;private BrainstormMenuController menu;private Request current;private string activeSessionId="";
        private bool tutorialBlock,paused;private float listeningIdleSince=-1f,lastReminder=-1000f;
        private float playbackStartedAt,lastObservedTime,nextDiagnosticUpdate,nextInstanceAudit,lastPauseAt,lastResumeDuration,maxFrameMilliseconds,ttsRequestStartedAt,lastTtsSeconds,lastDecodeSeconds,lastTimeToPlaybackSeconds;private string lastStopReason="Not started",lastMime="None",lastDecode="Not attempted",lastCacheStatus="Not checked",lastEndpoint="None",lastEvent="None",lastSubmission="None",lastFailure="None",lastRequestId="None",lastRequestSession="none",lastLifecycle="Initialized",lastLifecycleException="None",lastVoiceModel="Unknown",lastVoiceFallback="false",lastServerCacheIdentity="Unknown";private long lastHttpStatus;private int lastByteCount,activeNetworkRequests,directorInstances,workflowInstances,recorderInstances;
        private AudioListener cachedListener;private QuestMicrophoneRecorder recorder;
        public bool IsSpeaking=>current!=null||source!=null&&source.isPlaying;
        public bool TutorialRecordingBlocked=>tutorialBlock;
        public string ManifestStatus=>manifest!=null?"Loaded":"Missing or invalid";
        public string LastVoiceEvent=>lastEvent;
        public string LastTtsEndpoint=>lastEndpoint;
        public string LastTtsFailure=>lastFailure;
        public string PlaybackState=>source!=null&&source.isPlaying?"Playing":current!=null?"Resolving/queued":"Idle";
        public string DiagnosticText { get; private set; }="Voice director has not initialized.";

        private void Awake()
        {
            if(Instance!=null&&Instance!=this){Destroy(this);return;}Instance=this;
            stateController=GetComponent<OrbyConversationStateController>();
            source=gameObject.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;source.volume=1;source.mute=false;source.enabled=true;source.priority=64;source.ignoreListenerPause=true;source.outputAudioMixerGroup=null;source.spatialize=false;source.spatializePostEffects=false;
            GameObject sub=new GameObject("Orby Subtitles",typeof(RectTransform));sub.transform.SetParent(transform,false);sub.transform.localPosition=new Vector3(0,-.20f,0);subtitles=sub.AddComponent<OrbySubtitleView>();
            manifest=Resources.Load<OrbyPrerecordedManifest>("ThinkingSpace/OrbyPrerecordedManifest");
            cache=new OrbyVoiceCache(System.IO.Path.Combine(Application.persistentDataPath,"ThinkingSpace"));
            Resolve();if(preferences==null)preferences=ThinkingSpacePreferencesStore.CreateForApplication();
            if(Debug.isDebugBuild)Application.logMessageReceived+=HandleLogMessage;
            RefreshDiagnostics("Initialized");
        }
        private void Resolve(){if(workflow==null)workflow=FindFirstObjectByType<SpeechTranscriptionCoordinator>();if(listening==null)listening=FindFirstObjectByType<ConversationalListeningController>();if(recorder==null)recorder=FindFirstObjectByType<QuestMicrophoneRecorder>();if(menu==null)menu=FindFirstObjectByType<BrainstormMenuController>();if(menu!=null)preferences=menu.PreferencesStore;}
        public void SetSubtitlesEnabled(bool value){Resolve();preferences?.SetSubtitlesEnabled(value);if(!value)subtitles?.Hide();}
        public void ClearVoiceCache()=>cache?.Clear();
        public void SetActiveSession(string sessionId)
        {if(activeSessionId==sessionId)return;string next=sessionId??"";CancelSession(activeSessionId);if(!string.IsNullOrEmpty(next)){queue.RemoveAll(value=>value.CancelOnSessionChange);if(current!=null&&current.CancelOnSessionChange){lastStopReason="Session lifecycle speech superseded by session change";AbortCurrent(false);}}activeSessionId=next;ReportEvent("session_changed",true,true,OrbyVoicePriority.Acknowledgement,next);}
        public void CancelSession(string sessionId)
        {queue.RemoveAll(value=>{if(value.SessionId!=sessionId)return false;if(!string.IsNullOrEmpty(value.CacheKey))pendingKeys.Remove(value.CacheKey);return true;});if(current!=null&&current.SessionId==sessionId){lastStopReason="Session ended or switched";AbortCurrent(false);}}
        public void RemoveSessionCache(string sessionId){CancelSession(sessionId);cache.RemoveSession(sessionId);}
        public void SetTutorialRecordingBlock(bool value){tutorialBlock=value;Resolve();workflow?.SetTutorialRecordingBlocked(value);}

        public bool PlayPrerecorded(string eventKey,string sessionId=null,Action<bool> completed=null,bool cancelOnSessionChange=false)
        {
            if(manifest==null){lastFailure="Manifest missing or invalid";ReportEvent(eventKey,true,false,OrbyVoicePriority.Acknowledgement,sessionId??activeSessionId);RefreshDiagnostics("Manifest missing or invalid at Resources/ThinkingSpace/OrbyPrerecordedManifest");return false;}
            if(!manifest.TryGet(eventKey,out OrbyPrerecordedEntry entry)){lastFailure="Manifest clip missing";ReportEvent(eventKey,true,false,OrbyVoicePriority.Acknowledgement,sessionId??activeSessionId);RefreshDiagnostics("Manifest clip missing: "+eventKey);return false;}
            Request request=new Request{Id="pre-"+Guid.NewGuid().ToString("N"),SessionId=sessionId??activeSessionId,EventKey=eventKey,
                Text=entry.TranscriptVerified?entry.SubtitleText:string.Empty,Clip=entry.Clip,Priority=entry.Priority,Idle=entry.Priority==OrbyVoicePriority.IdleReminder,Completed=completed,CancelOnSessionChange=cancelOnSessionChange};lastRequestId=request.Id;lastRequestSession=request.SessionId;bool accepted=Enqueue(request);ReportEvent(eventKey,true,accepted,entry.Priority,request.SessionId);return accepted;
        }
        public bool SpeakDynamic(string text,string sessionId,OrbyVoicePriority priority=OrbyVoicePriority.AIResponse,Action<bool> completed=null,string eventName="dynamic_tts",string delivery="neutral")
            =>SpeakDynamicTracked(text,sessionId,priority,completed,eventName,delivery)!=null;
        public string SpeakDynamicTracked(string text,string sessionId,OrbyVoicePriority priority=OrbyVoicePriority.AIResponse,Action<bool> completed=null,string eventName="dynamic_tts",string delivery="neutral")
        {
            if(string.IsNullOrWhiteSpace(text)||string.IsNullOrWhiteSpace(sessionId)){lastFailure="Dynamic text or session ID missing";ReportEvent(eventName,true,false,priority,sessionId);return null;}
            delivery=NormalizeDelivery(delivery);string key=OrbyVoiceCache.CreateKey(text,CacheIdentity+"|delivery:"+delivery,sessionId);if(pendingKeys.Contains(key)){lastFailure="Duplicate dynamic request already pending";ReportEvent(eventName,true,false,priority,sessionId);return null;}pendingKeys.Add(key);
            Request request=new Request{Id="voice-"+Guid.NewGuid().ToString("N"),SessionId=sessionId,Text=text,Delivery=delivery,CacheKey=key,EventKey=eventName,Priority=priority,Completed=completed};lastRequestId=request.Id;lastRequestSession=sessionId;bool accepted=Enqueue(request);if(!accepted)pendingKeys.Remove(key);ReportEvent(eventName,true,accepted,priority,sessionId);return accepted?request.Id:null;
        }
        public bool SpeakOnboardingGreeting(string text,Action<bool> completed)
        {string owner=string.IsNullOrEmpty(activeSessionId)?"onboarding":activeSessionId;return SpeakDynamic(text,owner,OrbyVoicePriority.Tutorial,completed,"personalized_greeting");}
        private bool Enqueue(Request request)
        {
            if(request.Idle){queue.RemoveAll(value=>value.Idle);if(current!=null){lastFailure="Idle reminder dropped while speech active";return false;}}
            if(queue.Count>=12){int index=queue.FindIndex(value=>value.Priority==OrbyVoicePriority.IdleReminder);if(index>=0)queue.RemoveAt(index);else{if(!string.IsNullOrEmpty(request.CacheKey))pendingKeys.Remove(request.CacheKey);request.Completed?.Invoke(false);lastFailure="Voice queue full";return false;}}
            queue.Add(request);queue.Sort((a,b)=>b.Priority.CompareTo(a.Priority));if(worker==null)worker=StartCoroutine(ProcessQueue());return true;
        }
        public void CancelAllSpeech()
        {queue.Clear();pendingKeys.Clear();if(current!=null){lastStopReason="Cancelled by controller";AbortCurrent(false);}}
        public bool CancelSpeech(string requestId)
        {
            if(string.IsNullOrEmpty(requestId))return false;
            if(current!=null&&current.Id==requestId)
            {lastStopReason="Cancelled requested voice item only";AbortCurrent(false);return true;}
            int index=queue.FindIndex(value=>value.Id==requestId);
            if(index<0)return false;
            Request request=queue[index];queue.RemoveAt(index);
            if(!string.IsNullOrEmpty(request.CacheKey))pendingKeys.Remove(request.CacheKey);
            request.Completed?.Invoke(false);SpeakingFinished?.Invoke(request.Id,false);
            lastStopReason="Cancelled queued requested voice item only";RefreshDiagnostics(lastStopReason);return true;
        }
        private IEnumerator ProcessQueue()
        {
            while(queue.Count>0)
            {
                Resolve();
                while(ShouldDeferForQualifiedCapture(listening!=null&&listening.OwnsCapture,
                    listening!=null&&listening.SpeechWasDetected))
                {
                    lastStopReason="Voice deferred until qualified user utterance finalizes";
                    RefreshDiagnostics(lastStopReason);
                    yield return null;
                    Resolve();
                }
                current=queue[0];queue.RemoveAt(0);
                if(!string.IsNullOrEmpty(current.SessionId)&&!string.IsNullOrEmpty(activeSessionId)&&current.SessionId!=activeSessionId){lastStopReason="Rejected stale session ownership";lastFailure=lastStopReason;RefreshDiagnostics(lastStopReason);Finish(false);continue;}
                AudioClip clip=current.Clip;
                if(clip==null)yield return ResolveDynamicClip(current,value=>clip=value);
                if(clip==null){lastStopReason="Clip resolution or MP3 decoding failed";RefreshDiagnostics(lastStopReason);Finish(false);continue;}
                if(clip.loadState==AudioDataLoadState.Unloaded)clip.LoadAudioData();
                float loadDeadline=Time.realtimeSinceStartup+5f;
                while(clip.loadState==AudioDataLoadState.Loading&&Time.realtimeSinceStartup<loadDeadline)yield return null;
                if(clip.loadState==AudioDataLoadState.Failed){lastStopReason="AudioClip data load failed";RefreshDiagnostics(lastStopReason);Finish(false);continue;}
                Resolve();workflow?.SetVoicePlaybackBlocked(true);stateController?.SetState(OrbyConversationState.Speaking);
                string subtitleText=OrbySubtitleView.StripTtsOnlyTags(current.Text);subtitles.Show(subtitleText,preferences.Data.SubtitlesEnabled,clip.length);source.enabled=true;source.mute=false;source.volume=1f;source.pitch=1f;source.clip=clip;source.Play();playbackStartedAt=Time.realtimeSinceStartup;lastTimeToPlaybackSeconds=ttsRequestStartedAt>0?playbackStartedAt-ttsRequestStartedAt:0;lastObservedTime=source.time;lastStopReason="Playing";RefreshDiagnostics("Play invoked");SpeakingStarted?.Invoke(current.Id,subtitleText);
                yield return null;
                if(!source.isPlaying){lastStopReason="AudioSource did not enter isPlaying after Play";RefreshDiagnostics(lastStopReason);Finish(false);continue;}
                while(source.isPlaying&&!paused)yield return null;
                if(paused){source.Stop();lastStopReason="Application paused or lost focus";}else lastStopReason="Playback completed";Finish(!paused);
            }
            worker=null;
        }
        public static bool ShouldDeferForQualifiedCapture(bool ownsCapture,bool qualifiedSpeech)
            =>ownsCapture&&qualifiedSpeech;
        private IEnumerator ResolveDynamicClip(Request request,Action<AudioClip> completed)
        {
            string path;if(cache.TryGetPath(request.CacheKey,out path)){lastCacheStatus="Hit";RefreshDiagnostics("Voice cache hit");yield return LoadClip(path,completed);yield break;}lastCacheStatus="Miss";
            SpeechTranscriptionService config=FindFirstObjectByType<SpeechTranscriptionService>();
            if(config==null||string.IsNullOrEmpty(config.BackendBaseUrl)){lastFailure="TTS backend base URL is unavailable";RefreshDiagnostics(lastFailure);completed(null);yield break;}
            string token=config.GetDevelopmentTokenForCurrentRun();if(string.IsNullOrEmpty(token)){lastFailure="Development authentication token is unavailable";RefreshDiagnostics(lastFailure);completed(null);yield break;}
            string json=JsonUtility.ToJson(new VoiceWireRequest{request_id=request.Id,session_id=request.SessionId,text=request.Text,delivery=request.Delivery});ttsRequestStartedAt=Time.realtimeSinceStartup;lastTtsSeconds=lastDecodeSeconds=lastTimeToPlaybackSeconds=0;
            lastEndpoint=BuildVoiceEndpoint(config.BackendBaseUrl);activeNetworkRequests++;using(UnityWebRequest web=new UnityWebRequest(lastEndpoint,UnityWebRequest.kHttpVerbPOST))
            {web.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));web.downloadHandler=new DownloadHandlerBuffer();web.timeout=30;web.SetRequestHeader("Content-Type","application/json");web.SetRequestHeader("X-ThinkingSpace-Token",token);yield return web.SendWebRequest();
                activeNetworkRequests=Mathf.Max(0,activeNetworkRequests-1);lastTtsSeconds=Time.realtimeSinceStartup-ttsRequestStartedAt;string mime=web.GetResponseHeader("Content-Type")??"";byte[] bytes=web.downloadHandler?.data;lastHttpStatus=web.responseCode;lastMime=string.IsNullOrEmpty(mime)?"None":mime;lastByteCount=bytes?.Length??0;lastVoiceModel=web.GetResponseHeader("X-ThinkingSpace-Voice-Model")??"Unknown";lastVoiceFallback=web.GetResponseHeader("X-ThinkingSpace-Voice-Fallback")??"false";lastServerCacheIdentity=web.GetResponseHeader("X-ThinkingSpace-Voice-Cache")??"Unknown";RefreshDiagnostics("TTS response received");
                if(web.result!=UnityWebRequest.Result.Success){lastFailure="TTS HTTP request failed: "+web.responseCode;RefreshDiagnostics(lastFailure);completed(null);yield break;}
                if(!mime.StartsWith("audio/mpeg",StringComparison.OrdinalIgnoreCase)){lastFailure="Unexpected TTS MIME type";RefreshDiagnostics(lastFailure);completed(null);yield break;}
                if(bytes==null||bytes.Length<4||bytes.Length>5*1024*1024){lastFailure="TTS audio byte count is invalid";RefreshDiagnostics(lastFailure);completed(null);yield break;}
                if(!cache.Store(request.CacheKey,request.SessionId,bytes)||!cache.TryGetPath(request.CacheKey,out path)){lastFailure="TTS cache write or readback failed";lastCacheStatus="Write failed";RefreshDiagnostics(lastFailure);completed(null);yield break;}lastCacheStatus="Stored";}
            yield return LoadClip(path,completed);
        }
        private IEnumerator LoadClip(string path,Action<AudioClip> completed)
        {float started=Time.realtimeSinceStartup;using(UnityWebRequest request=UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri,AudioType.MPEG)){request.timeout=15;DownloadHandlerAudioClip handler=request.downloadHandler as DownloadHandlerAudioClip;if(handler!=null)handler.streamAudio=false;lastDecode="Loading MP3";yield return request.SendWebRequest();lastDecodeSeconds=Time.realtimeSinceStartup-started;if(request.result!=UnityWebRequest.Result.Success){lastDecode="Failed: "+request.error;lastStopReason="Cached MP3 decode failed: "+request.error;RefreshDiagnostics(lastStopReason);completed(null);}else{AudioClip clip=DownloadHandlerAudioClip.GetContent(request);lastDecode=clip!=null?"Decoded · "+clip.length.ToString("0.00")+"s":"Decoder returned null";RefreshDiagnostics(lastDecode);completed(clip);}}}
        private void Finish(bool success)
        {string id=current?.Id;string key=current?.CacheKey;AudioClip played=source!=null?source.clip:null;bool dynamic=current!=null&&current.Clip==null;if(!string.IsNullOrEmpty(key))pendingKeys.Remove(key);current?.Completed?.Invoke(success);SpeakingFinished?.Invoke(id,success);current=null;if(source!=null)source.clip=null;if(dynamic&&played!=null)Destroy(played);subtitles.Hide();RefreshDiagnostics(lastStopReason);Resolve();workflow?.SetVoicePlaybackBlocked(false);if(!tutorialBlock)stateController?.SetState(listening!=null&&listening.ListeningEnabled?OrbyConversationState.Listening:OrbyConversationState.Idle);}
        private void StopCurrent(bool success){if(source!=null)source.Stop();if(current!=null)Finish(success);}
        private void AbortCurrent(bool success){StopAllCoroutines();activeNetworkRequests=0;worker=null;StopCurrent(success);if(queue.Count>0&&!paused)worker=StartCoroutine(ProcessQueue());}
        private void Update()
        {
            maxFrameMilliseconds=Mathf.Max(maxFrameMilliseconds,Time.unscaledDeltaTime*1000f);if(Debug.isDebugBuild&&Time.unscaledTime>=nextInstanceAudit){nextInstanceAudit=Time.unscaledTime+2f;directorInstances=FindObjectsByType<OrbyVoiceDirector>(FindObjectsSortMode.None).Length;workflowInstances=FindObjectsByType<SpeechTranscriptionCoordinator>(FindObjectsSortMode.None).Length;recorderInstances=FindObjectsByType<QuestMicrophoneRecorder>(FindObjectsSortMode.None).Length;}if(source!=null&&source.isPlaying){lastObservedTime=source.time;if(Time.unscaledTime>=nextDiagnosticUpdate){nextDiagnosticUpdate=Time.unscaledTime+.25f;RefreshDiagnostics("Playing");}}
            Resolve();if(listening!=null&&listening.ListeningEnabled&&!IsSpeaking&&!tutorialBlock&&stateController!=null&&(stateController.State==OrbyConversationState.Listening||stateController.State==OrbyConversationState.Idle))
            {if(listening.State==ConversationalListeningState.WaitingForSpeech){if(listeningIdleSince<0)listeningIdleSince=Time.unscaledTime;if(Time.unscaledTime-listeningIdleSince>=20&&Time.unscaledTime-lastReminder>=60){if(PlayPrerecorded("listening_ready",activeSessionId))lastReminder=Time.unscaledTime;listeningIdleSince=Time.unscaledTime;}}else listeningIdleSince=Time.unscaledTime;}
            else listeningIdleSince=-1;
        }
        private void OnApplicationPause(bool value){paused=value;lastLifecycle="Pause="+value+" at "+Time.realtimeSinceStartup.ToString("0.00");if(value){lastPauseAt=Time.realtimeSinceStartup;lastStopReason="Application paused";activeNetworkRequests=0;queue.Clear();AbortCurrent(false);}else if(lastPauseAt>0)lastResumeDuration=Time.realtimeSinceStartup-lastPauseAt;RefreshDiagnostics(lastLifecycle);}
        private void OnApplicationFocus(bool value){lastLifecycle="Focus="+value+" at "+Time.realtimeSinceStartup.ToString("0.00");if(!value){paused=true;lastPauseAt=Time.realtimeSinceStartup;lastStopReason="Application focus lost";activeNetworkRequests=0;queue.Clear();AbortCurrent(false);}else{paused=false;if(lastPauseAt>0)lastResumeDuration=Time.realtimeSinceStartup-lastPauseAt;}RefreshDiagnostics(lastLifecycle);}
        private void OnDestroy(){Application.logMessageReceived-=HandleLogMessage;if(Instance==this)Instance=null;}
        private void HandleLogMessage(string condition,string stackTrace,LogType type){if(type!=LogType.Exception)return;lastLifecycleException=string.IsNullOrEmpty(condition)?"Exception":condition.Substring(0,Mathf.Min(160,condition.Length));}
        public bool DebugPlayPrerecorded(string eventKey="greeting_welcome")
        {if(!Debug.isDebugBuild&&!Application.isEditor)return false;CancelAllSpeech();return PlayPrerecorded(eventKey);}
        public static string BuildVoiceEndpoint(string baseUrl)=>(baseUrl??string.Empty).TrimEnd('/')+"/api/voice/synthesize";
        public static string NormalizeDelivery(string value)
        {switch((value??"").Trim().ToLowerInvariant()){case "laughs":case "curious":case "excited":case "sighs":case "whispers":case "clears_throat":case "mischievously":case "snorts":return value.Trim().ToLowerInvariant();case "thinking":case "attentive":case "confused":return "curious";case "happy":return "mischievously";default:return "neutral";}}
        public void ReportEvent(string eventName,bool submitted,bool accepted,OrbyVoicePriority priority,string sessionId)
        {lastEvent=eventName??"Unknown";lastSubmission=(submitted?"Submitted":"Not submitted")+" · "+(accepted?"accepted":"rejected")+" · priority "+priority+" · session "+(string.IsNullOrEmpty(sessionId)?"none":sessionId);RefreshDiagnostics("Event: "+lastEvent);}
        private void RefreshDiagnostics(string stage)
        {
            if(cachedListener==null)cachedListener=FindFirstObjectByType<AudioListener>();AudioListener listener=cachedListener;AudioClip clip=source!=null?source.clip:null;
            DiagnosticText="ORBY VOICE (development)\nStage: "+stage+"\nManifest: "+(manifest!=null?"Loaded":"Missing/invalid")+
                "\nEvent: "+lastEvent+" · "+lastSubmission+"\nRequest: "+lastRequestId+" · session: "+lastRequestSession+
                "\nClip: "+(clip!=null?clip.name+" · "+clip.length.ToString("0.00")+"s · "+clip.frequency+"Hz/"+clip.channels+"ch":"None")+
                "\nSource active/enabled: "+gameObject.activeInHierarchy+"/"+(source!=null&&source.enabled)+
                " · volume/mute: "+(source!=null?source.volume.ToString("0.00")+"/"+source.mute:"n/a")+
                "\nListener: "+(listener!=null&&listener.isActiveAndEnabled)+" · global pause/volume: "+AudioListener.pause+"/"+AudioListener.volume.ToString("0.00")+
                "\nPlay/isPlaying/time/pitch: "+(playbackStartedAt>0)+"/"+(source!=null&&source.isPlaying)+"/"+lastObservedTime.ToString("0.00")+"/"+(source!=null?source.pitch.ToString("0.00"):"n/a")+" · output "+AudioSettings.outputSampleRate+"Hz"+
                "\nEndpoint: "+lastEndpoint+" · cache: "+lastCacheStatus+"\nTTS HTTP/MIME/bytes: "+lastHttpStatus+"/"+lastMime+"/"+lastByteCount+" · decode: "+lastDecode+
                "\nVoice model/fallback/cache: "+lastVoiceModel+"/"+lastVoiceFallback+"/"+lastServerCacheIdentity+" · request/decode/first-play: "+lastTtsSeconds.ToString("0.000")+"/"+lastDecodeSeconds.ToString("0.000")+"/"+lastTimeToPlaybackSeconds.ToString("0.000")+"s"+
                "\nLifecycle: "+lastLifecycle+" · resume "+lastResumeDuration.ToString("0.00")+"s · max frame "+maxFrameMilliseconds.ToString("0.0")+"ms · network "+activeNetworkRequests+" · mic starts "+(recorder!=null?recorder.StartAttemptCount:0)+
                "\nInstances voice/workflow/recorder: "+directorInstances+"/"+workflowInstances+"/"+recorderInstances+" · exception: "+lastLifecycleException+
                "\nStop/cancel: "+lastStopReason+" · failure: "+lastFailure;
        }
        [Serializable] private sealed class VoiceWireRequest{public string request_id,session_id,text,delivery;}
    }
}

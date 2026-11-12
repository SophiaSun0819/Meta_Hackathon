using TMPro;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    [DisallowMultipleComponent]
    public sealed class OrbyPlaceholderView : MonoBehaviour
    {
        [SerializeField] private OrbyConversationStateController stateController;
        [SerializeField] private Transform headset;
        [SerializeField] private Vector3 headsetOffset = new Vector3(0f, 0f, 0.90f);
        [SerializeField, Min(0.3f)] private float followTriggerRadius = 1.35f;
        [SerializeField, Min(0.3f)] private float comfortableFollowDistance = 0.9f;
        [SerializeField, Min(0.01f)] private float followStopDistance = 0.06f;
        [SerializeField, Min(0.1f)] private float maximumFollowSpeed = 1.2f;
        [SerializeField, Min(0.1f)] private float maximumFollowAcceleration = 2.5f;
        [SerializeField, Range(30f, 120f)] private float viewRecoveryAngle = 65f;
        [SerializeField, Min(0.1f)] private float viewRecoveryDelay = 2f;
        [SerializeField, Min(0.05f)] private float heightRecoveryThreshold = 0.18f;
        [SerializeField, Min(0.1f)] private float headsetReadyHoldDuration = 0.6f;
        [SerializeField, Min(0f)] private float floatAmplitude = 0.025f;
        [SerializeField, Min(0f)] private float floatSpeed = 1.5f;
        private Transform visual;
        private TextMeshPro label;
        private Material material;
        private OrbyModelVisualAdapter modelAdapter;
        private Vector3 velocity;
        private OrbyConversationState currentState;
        private float liveAudioLevel, smoothedAudioLevel, silenceProgress;
        private bool silenceCountdown;
        private bool followInitialized,followingUser;
        private bool labelFacingInitialized;
        private Vector3 followDirection,followTarget;
        private float headsetReadyTimer,outOfViewTimer;

        public bool IsUsingAuthoredModel => modelAdapter != null && modelAdapter.IsBound;
        public OrbyModelVisualAdapter ModelAdapter => modelAdapter;

        private void Awake()
        {
            if (stateController == null) stateController = GetComponent<OrbyConversationStateController>();
            BuildReplaceableVisual();
            ApplyState(stateController != null ? stateController.State : OrbyConversationState.Idle);
        }
        private void OnEnable()
        {
            if (stateController != null) stateController.StateChanged += HandleStateChanged;
            if (modelAdapter != null) modelAdapter.enabled = true;
        }
        private void OnDisable()
        {
            if (stateController != null) stateController.StateChanged -= HandleStateChanged;
            if (modelAdapter != null) modelAdapter.enabled = false;
        }

        private void LateUpdate()
        {
            if (headset == null && Camera.main != null) headset = Camera.main.transform;
            if (headset == null||!IsValidTrackedHeadPose(headset.position)) return;
            if(currentState==OrbyConversationState.Paused)return;
            if(!followInitialized)
            {
                if(!IsHeadsetUserPresent()) { headsetReadyTimer=0f; return; }
                headsetReadyTimer+=Time.unscaledDeltaTime;
                if(headsetReadyTimer<headsetReadyHoldDuration)return;
                followTarget=CalculateFollowTarget(headset.position,headset.forward,
                    comfortableFollowDistance,headsetOffset.y);
                transform.position=followTarget;velocity=Vector3.zero;followInitialized=true;
                SetCompanionVisualVisible(true);
            }
            Vector3 horizontalDelta=Vector3.ProjectOnPlane(transform.position-headset.position,Vector3.up);
            Vector3 horizontalForward=Vector3.ProjectOnPlane(headset.forward,Vector3.up).normalized;
            float viewingAngle=CalculateHorizontalViewingAngle(horizontalForward,horizontalDelta);
            if(!followingUser&&viewingAngle>viewRecoveryAngle)outOfViewTimer+=Time.unscaledDeltaTime;
            else if(!followingUser)outOfViewTimer=0f;
            bool physicalRecovery=ShouldBeginSpatialFollow(horizontalDelta.magnitude,followTriggerRadius);
            bool heightRecovery=Mathf.Abs(transform.position.y-(headset.position.y+headsetOffset.y))
                >=heightRecoveryThreshold;
            bool viewRecovery=outOfViewTimer>=viewRecoveryDelay;
            if(!followingUser&&(physicalRecovery||heightRecovery||viewRecovery))
            {
                followDirection=horizontalForward;
                if(followDirection.sqrMagnitude<.001f)followDirection=Vector3.forward;
                followingUser=true;outOfViewTimer=0f;
            }
            if(followingUser)
            {
                followTarget=CalculateFollowTarget(headset.position,followDirection,
                    comfortableFollowDistance,headsetOffset.y);
                transform.position=StepBoundedFollow(transform.position,followTarget,ref velocity,
                    maximumFollowSpeed,maximumFollowAcceleration,Time.unscaledDeltaTime);
                if(Vector3.Distance(transform.position,followTarget)<=followStopDistance)
                {followingUser=false;velocity=Vector3.zero;}
            }
            Vector3 towardViewer=Vector3.ProjectOnPlane(headset.position-transform.position,Vector3.up);
            if(towardViewer.sqrMagnitude>.001f)
            {
                Quaternion desiredFacing=Quaternion.LookRotation(towardViewer.normalized,Vector3.up);
                float facingBlend=1f-Mathf.Exp(-8f*Time.unscaledDeltaTime);
                transform.rotation=Quaternion.Slerp(transform.rotation,desiredFacing,facingBlend);
            }
            if(label!=null)
            {
                Quaternion desiredLabel=CalculateReadableLabelRotation(label.transform.position,headset.position);
                if(!labelFacingInitialized){label.transform.rotation=desiredLabel;labelFacingInitialized=true;}
                else label.transform.rotation=Quaternion.Slerp(label.transform.rotation,desiredLabel,
                    1f-Mathf.Exp(-10f*Time.unscaledDeltaTime));
            }
            float bob = Mathf.Sin(Time.unscaledTime * floatSpeed * Mathf.PI * 2f) * floatAmplitude;
            if (visual != null) visual.localPosition = Vector3.up * bob;
            if (modelAdapter != null && modelAdapter.IsBound)
                modelAdapter.UpdatePose(transform.position + Vector3.up * bob, transform.rotation);
            if (visual != null)
            {
                smoothedAudioLevel = Mathf.Lerp(smoothedAudioLevel, liveAudioLevel,
                    1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.08f));
                float pulse = currentState == OrbyConversationState.SpeechActive
                    ? Mathf.Clamp01(smoothedAudioLevel * 35f) * 0.16f : 0f;
                if (silenceCountdown) pulse += (1f - silenceProgress) * 0.04f;
                visual.localScale = Vector3.one * 0.13f * (1f + pulse);
                if (currentState == OrbyConversationState.Thinking)
                    visual.localRotation = Quaternion.Euler(0f, Time.unscaledTime * 75f, 0f);
                else visual.localRotation = Quaternion.identity;
            }
        }

        public void SetHeadset(Transform value) => headset = value;
        public void RecenterOrby()
        {
            if(headset==null&&Camera.main!=null)headset=Camera.main.transform;
            if(headset==null||!IsValidTrackedHeadPose(headset.position))return;
            followDirection=Vector3.ProjectOnPlane(headset.forward,Vector3.up).normalized;
            if(followDirection.sqrMagnitude<.001f)followDirection=Vector3.forward;
            followingUser=true;outOfViewTimer=0f;
        }
        public static bool ShouldBeginSpatialFollow(float horizontalDistance,float triggerRadius)
            =>horizontalDistance>Mathf.Max(.01f,triggerRadius);
        public static float CalculateHorizontalViewingAngle(Vector3 horizontalForward,Vector3 horizontalToOrby)
        {
            if(horizontalForward.sqrMagnitude<.000001f||horizontalToOrby.sqrMagnitude<.000001f)return 0f;
            return Vector3.Angle(horizontalForward,horizontalToOrby);
        }
        public static Vector3 StepBoundedFollow(Vector3 current,Vector3 target,ref Vector3 currentVelocity,
            float maximumSpeed,float maximumAcceleration,float deltaTime)
        {
            float dt=Mathf.Max(0f,deltaTime);Vector3 delta=target-current;
            if(dt<=0f||delta.sqrMagnitude<.000001f){currentVelocity=Vector3.zero;return current;}
            float speed=Mathf.Min(Mathf.Max(.01f,maximumSpeed),delta.magnitude/Mathf.Max(.001f,dt));
            Vector3 desiredVelocity=delta.normalized*speed;
            currentVelocity=Vector3.MoveTowards(currentVelocity,desiredVelocity,
                Mathf.Max(.01f,maximumAcceleration)*dt);
            Vector3 step=currentVelocity*dt;
            if(step.sqrMagnitude>=delta.sqrMagnitude){currentVelocity=Vector3.zero;return target;}
            return current+step;
        }
        public static bool IsValidTrackedHeadPose(Vector3 position)
            =>float.IsFinite(position.x)&&float.IsFinite(position.y)&&float.IsFinite(position.z)
                &&position.y>.5f;
        public static Quaternion CalculateReadableLabelRotation(Vector3 labelPosition,Vector3 viewerPosition)
        {
            Vector3 awayFromViewer=Vector3.ProjectOnPlane(labelPosition-viewerPosition,Vector3.up);
            if(awayFromViewer.sqrMagnitude<.000001f)awayFromViewer=Vector3.forward;
            // TextMeshPro's readable face is local -Z, so +Z points away from the viewer.
            return Quaternion.LookRotation(awayFromViewer.normalized,Vector3.up);
        }
        public static Vector3 CalculateFollowTarget(Vector3 headsetPosition,Vector3 stableDirection,
            float distance,float verticalOffset)
        {
            Vector3 direction=Vector3.ProjectOnPlane(stableDirection,Vector3.up).normalized;
            if(direction.sqrMagnitude<.001f)direction=Vector3.forward;
            return headsetPosition+direction*Mathf.Max(0f,distance)+Vector3.up*verticalOffset;
        }
        private static bool IsHeadsetUserPresent()
        {
#if UNITY_EDITOR
            return true;
#else
            try{return OVRPlugin.userPresent;}catch{return false;}
#endif
        }
        public void SetLiveAudioLevel(float rms, bool isSilenceCountdown, float remainingNormalized)
        {
            liveAudioLevel = Mathf.Max(0f, rms);
            silenceCountdown = isSilenceCountdown;
            silenceProgress = Mathf.Clamp01(remainingNormalized);
            if (label != null && isSilenceCountdown)
                label.text = "Orby · Finishing thought…";
        }
        private void HandleStateChanged(OrbyConversationState previous, OrbyConversationState next) => ApplyState(next);
        private void ApplyState(OrbyConversationState state)
        {
            currentState = state;
            if (state != OrbyConversationState.SpeechActive)
            { liveAudioLevel = 0f; silenceCountdown = false; }
            if (label != null) label.text = "Orby · " + state;
            if (modelAdapter != null)
                modelAdapter.ApplyExpression(OrbyModelVisualAdapter.FromConversationState(state));
            if (material == null) return;
            Color color = state == OrbyConversationState.Listening ? new Color(0.2f, 0.85f, 1f) :
                state == OrbyConversationState.SpeechActive ? new Color(0.2f, 1f, 0.72f) :
                state == OrbyConversationState.Thinking ? new Color(0.75f, 0.45f, 1f) :
                state == OrbyConversationState.Speaking ? new Color(0.25f, 1f, 0.55f) :
                state == OrbyConversationState.Paused ? new Color(1f, 0.7f, 0.2f) :
                state == OrbyConversationState.Reviewing ? new Color(0.25f, 0.65f, 1f) :
                state == OrbyConversationState.Error ? new Color(1f, 0.2f, 0.2f) : new Color(0.45f, 0.7f, 1f);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private void BuildReplaceableVisual()
        {
            Transform authoredModel = FindAuthoredModel();
            if (authoredModel != null)
            {
                modelAdapter = GetComponent<OrbyModelVisualAdapter>();
                if (modelAdapter == null) modelAdapter = gameObject.AddComponent<OrbyModelVisualAdapter>();
                if (!modelAdapter.Bind(authoredModel)) modelAdapter = null;
            }

            if (modelAdapter == null)
                BuildFallbackSphere();
            BuildStatusLabel();
            SetCompanionVisualVisible(false);
        }

        private void BuildFallbackSphere()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Placeholder Visual (Replace With Final Orby Model)";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(transform, false); sphere.transform.localScale = Vector3.one * 0.13f;
            visual = sphere.transform;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            material = new Material(shader) { name = "Orby Placeholder Runtime Material" };
            sphere.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void BuildStatusLabel()
        {
            GameObject textObject = new GameObject("Status Label");
            textObject.transform.SetParent(transform, false);
            float labelY = modelAdapter != null && modelAdapter.IsBound
                ? -Mathf.Max(0.12f, modelAdapter.AuthoredWorldBounds.extents.y + 0.035f)
                : -0.12f;
            textObject.transform.localPosition = new Vector3(0f, labelY, 0f);
            label = textObject.AddComponent<TextMeshPro>();
            label.fontSize = 0.9f;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.35f, 0.04f);
            label.transform.localScale=Vector3.one;
        }

        private void SetCompanionVisualVisible(bool visible)
        {
            if(modelAdapter!=null&&modelAdapter.IsBound)modelAdapter.SetVisualVisible(visible);
            if(visual!=null)visual.gameObject.SetActive(visible);
            if(label!=null)label.gameObject.SetActive(visible);
        }

        private Transform FindAuthoredModel()
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Transform match = null;
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate == transform || candidate.parent != null || candidate.name != "Orbi") continue;
                if (candidate.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                if (match != null)
                {
                    Debug.LogWarning("Multiple root-level Orbi models were found; keeping the placeholder visible.");
                    return null;
                }
                match = candidate;
            }
            return match;
        }

        private void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }

    public static class OrbyPlaceholderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsurePlaceholderForRuntime()
        {
            OrbyConversationStateController existing=Object.FindFirstObjectByType<OrbyConversationStateController>();
            GameObject root=existing!=null?existing.gameObject:new GameObject("Orby Placeholder Companion");
            if (Application.isPlaying) Object.DontDestroyOnLoad(root);
            if(existing==null)existing=root.AddComponent<OrbyConversationStateController>();
            if(root.GetComponent<OrbyPlaceholderView>()==null)root.AddComponent<OrbyPlaceholderView>();
            if(root.GetComponent<OrbyVoiceDirector>()==null)root.AddComponent<OrbyVoiceDirector>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ThinkingSpaceDevelopmentReset reset=Object.FindFirstObjectByType<ThinkingSpaceDevelopmentReset>();
            GameObject tools=GameObject.Find("ThinkingSpace Development Tools")??new GameObject("ThinkingSpace Development Tools");
            if(Application.isPlaying)Object.DontDestroyOnLoad(tools);
            if(reset==null)reset=tools.AddComponent<ThinkingSpaceDevelopmentReset>();
            if(tools.GetComponent<ThinkingSpaceDevelopmentTools>()==null)tools.AddComponent<ThinkingSpaceDevelopmentTools>();
#endif
            if(root.GetComponent<OrbyVoiceDiagnosticsPanel>()==null)root.AddComponent<OrbyVoiceDiagnosticsPanel>();
            if(root.GetComponent<OrbyOnboardingController>()==null)root.AddComponent<OrbyOnboardingController>();
        }
    }
}

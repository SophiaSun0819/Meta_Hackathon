using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThinkingSpace.Companion
{
    public enum OrbyMaterialExpression
    {
        Idle,
        Listening,
        Thinking,
        Speaking,
        Excited,
        Curious,
        Error
    }

    /// <summary>
    /// Adapts the authored scene model to the existing Orby companion root without
    /// changing the FBX hierarchy, imported materials, bones, or blend shapes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrbyModelVisualAdapter : MonoBehaviour
    {
        [Header("Subtle material expressions")]
        [SerializeField, Range(0f, 1f)] private float tintStrength = 0.24f;
        [SerializeField, Range(0f, 2f)] private float emissionStrength = 0.16f;
        [SerializeField, Min(0f)] private float thinkingPulseSpeed = 1.25f;
        [SerializeField, Range(0f, 1f)] private float thinkingPulseAmplitude = 0.35f;
        [SerializeField] private Color listeningColor = new Color(0.20f, 0.88f, 1f);
        [SerializeField] private Color thinkingColor = new Color(0.38f, 0.36f, 1f);
        [SerializeField] private Color speakingColor = new Color(0.55f, 0.90f, 1f);
        [SerializeField] private Color excitedColor = new Color(1f, 0.68f, 0.18f);
        [SerializeField] private Color curiousColor = new Color(0.12f, 0.78f, 0.72f);
        [SerializeField] private Color errorColor = new Color(0.85f, 0.52f, 0.20f);
        [Header("Model orientation")]
        [Tooltip("Local-space axis that points out of Orby's visible face. The authored FBX uses +Z.")]
        [SerializeField] private Vector3 visibleFrontLocalAxis = Vector3.forward;

        private sealed class RendererMaterials
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Runtime;
            public Color[] OriginalBaseColors;
            public Color[] OriginalEmissionColors;
            public string[] BaseColorProperties;
            public bool[] SupportsEmission;
        }

        private readonly List<RendererMaterials> rendererMaterials = new List<RendererMaterials>();
        private Transform modelRoot;
        private Vector3 authoredPosition;
        private Quaternion authoredRotation;
        private Vector3 authoredScale;
        private Vector3 boundsCenterInModelSpace;
        private Bounds authoredWorldBounds;
        private bool bound;
        private OrbyMaterialExpression expression = OrbyMaterialExpression.Idle;

        public bool IsBound => bound && modelRoot != null;
        public Transform ModelRoot => modelRoot;
        public Bounds AuthoredWorldBounds => authoredWorldBounds;
        public Vector3 AuthoredPosition => authoredPosition;
        public Quaternion AuthoredRotation => authoredRotation;
        public Vector3 AuthoredScale => authoredScale;
        public OrbyMaterialExpression Expression => expression;
        public event Action<OrbyMaterialExpression> ExpressionChanged;

        public void SetVisualVisible(bool visible)
        {
            for(int i=0;i<rendererMaterials.Count;i++)
                if(rendererMaterials[i].Renderer!=null)rendererMaterials[i].Renderer.enabled=visible;
        }

        public bool Bind(Transform authoredModel)
        {
            if (authoredModel == null || authoredModel == transform) return false;
            if (IsBound && modelRoot == authoredModel) return true;
            RestoreAndRelease();

            Renderer[] renderers = authoredModel.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            modelRoot = authoredModel;
            authoredPosition = modelRoot.position;
            authoredRotation = modelRoot.rotation;
            authoredScale = modelRoot.localScale;
            authoredWorldBounds = CalculateBounds(renderers);
            boundsCenterInModelSpace = modelRoot.InverseTransformPoint(authoredWorldBounds.center);

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                Material[] originals = renderer.sharedMaterials;
                Material[] runtime = new Material[originals.Length];
                Color[] baseColors = new Color[originals.Length];
                Color[] emissionColors = new Color[originals.Length];
                string[] baseProperties = new string[originals.Length];
                bool[] supportsEmission = new bool[originals.Length];

                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    if (source == null) continue;
                    Material instance = new Material(source)
                    {
                        name = source.name + " (Orby Runtime Expression)"
                    };
                    runtime[i] = instance;
                    string baseProperty = instance.HasProperty("_BaseColor") ? "_BaseColor"
                        : instance.HasProperty("_Color") ? "_Color" : null;
                    baseProperties[i] = baseProperty;
                    baseColors[i] = baseProperty != null ? instance.GetColor(baseProperty) : Color.white;
                    supportsEmission[i] = instance.HasProperty("_EmissionColor");
                    if (supportsEmission[i])
                    {
                        emissionColors[i] = instance.GetColor("_EmissionColor");
                        instance.EnableKeyword("_EMISSION");
                    }
                }

                renderer.sharedMaterials = runtime;
                rendererMaterials.Add(new RendererMaterials
                {
                    Renderer = renderer,
                    Originals = originals,
                    Runtime = runtime,
                    OriginalBaseColors = baseColors,
                    OriginalEmissionColors = emissionColors,
                    BaseColorProperties = baseProperties,
                    SupportsEmission = supportsEmission
                });
            }

            bound = true;
            ApplyExpression(OrbyMaterialExpression.Idle, true);
            return true;
        }

        public void UpdatePose(Vector3 desiredBoundsCenter, Quaternion companionRotation)
        {
            if (!IsBound) return;
            modelRoot.rotation = companionRotation * LocalFrontCorrection(visibleFrontLocalAxis);
            modelRoot.position = desiredBoundsCenter - modelRoot.TransformVector(boundsCenterInModelSpace);
        }

        public static Quaternion LocalFrontCorrection(Vector3 localFrontAxis)
        {
            Vector3 axis=localFrontAxis.sqrMagnitude<.000001f?Vector3.forward:localFrontAxis.normalized;
            return Quaternion.FromToRotation(axis,Vector3.forward);
        }

        public void ApplyExpression(OrbyMaterialExpression next, bool force = false)
        {
            if (!force && expression == next) return;
            expression = next;
            ApplyMaterialValues(CurrentPulse());
            ExpressionChanged?.Invoke(expression);
        }

        private void Update()
        {
            if (IsBound && expression == OrbyMaterialExpression.Thinking)
                ApplyMaterialValues(CurrentPulse());
        }

        private void OnEnable()
        {
            if (!bound) return;
            for (int r = 0; r < rendererMaterials.Count; r++)
            {
                RendererMaterials group = rendererMaterials[r];
                if (group.Renderer != null) group.Renderer.sharedMaterials = group.Runtime;
            }
            ApplyMaterialValues(CurrentPulse());
        }

        private float CurrentPulse()
        {
            if (expression != OrbyMaterialExpression.Thinking) return 1f;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * thinkingPulseSpeed * Mathf.PI * 2f);
            return 1f - thinkingPulseAmplitude + thinkingPulseAmplitude * wave;
        }

        private void ApplyMaterialValues(float pulse)
        {
            Color target = ExpressionColor(expression);
            float tint = expression == OrbyMaterialExpression.Idle ? 0f : tintStrength;
            float emission = expression == OrbyMaterialExpression.Idle ? 0f : emissionStrength * pulse;

            for (int r = 0; r < rendererMaterials.Count; r++)
            {
                RendererMaterials group = rendererMaterials[r];
                for (int i = 0; i < group.Runtime.Length; i++)
                {
                    Material material = group.Runtime[i];
                    if (material == null) continue;
                    string baseProperty = group.BaseColorProperties[i];
                    if (baseProperty != null)
                    {
                        Color original = group.OriginalBaseColors[i];
                        Color value = Color.Lerp(original, new Color(target.r, target.g, target.b, original.a), tint);
                        value.a = original.a;
                        material.SetColor(baseProperty, value);
                        if (baseProperty == "_BaseColor" && material.HasProperty("_Color"))
                            material.SetColor("_Color", value);
                    }
                    if (group.SupportsEmission[i])
                    {
                        Color originalEmission = group.OriginalEmissionColors[i];
                        material.SetColor("_EmissionColor", originalEmission + target * emission);
                    }
                }
            }
        }

        private Color ExpressionColor(OrbyMaterialExpression value)
        {
            switch (value)
            {
                case OrbyMaterialExpression.Listening: return listeningColor;
                case OrbyMaterialExpression.Thinking: return thinkingColor;
                case OrbyMaterialExpression.Speaking: return speakingColor;
                case OrbyMaterialExpression.Excited: return excitedColor;
                case OrbyMaterialExpression.Curious: return curiousColor;
                case OrbyMaterialExpression.Error: return errorColor;
                default: return Color.white;
            }
        }

        public static OrbyMaterialExpression FromConversationState(OrbyConversationState state)
        {
            switch (state)
            {
                case OrbyConversationState.Listening:
                case OrbyConversationState.SpeechActive:
                    return OrbyMaterialExpression.Listening;
                case OrbyConversationState.Thinking:
                    return OrbyMaterialExpression.Thinking;
                case OrbyConversationState.Speaking:
                    return OrbyMaterialExpression.Speaking;
                case OrbyConversationState.Reviewing:
                    return OrbyMaterialExpression.Curious;
                case OrbyConversationState.Error:
                    return OrbyMaterialExpression.Error;
                default:
                    return OrbyMaterialExpression.Idle;
            }
        }

        private static Bounds CalculateBounds(Renderer[] renderers)
        {
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        private void OnDisable() => RestoreMaterials();

        private void OnDestroy() => RestoreAndRelease();

        private void RestoreMaterials()
        {
            for (int r = 0; r < rendererMaterials.Count; r++)
            {
                RendererMaterials group = rendererMaterials[r];
                if (group.Renderer != null) group.Renderer.sharedMaterials = group.Originals;
            }
        }

        private void RestoreAndRelease()
        {
            RestoreMaterials();
            for (int r = 0; r < rendererMaterials.Count; r++)
            {
                Material[] runtime = rendererMaterials[r].Runtime;
                for (int i = 0; i < runtime.Length; i++)
                {
                    if (runtime[i] == null) continue;
                    if (Application.isPlaying) Destroy(runtime[i]); else DestroyImmediate(runtime[i]);
                }
            }
            rendererMaterials.Clear();

            if (modelRoot != null)
            {
                modelRoot.position = authoredPosition;
                modelRoot.rotation = authoredRotation;
                modelRoot.localScale = authoredScale;
            }
            modelRoot = null;
            bound = false;
        }
    }
}

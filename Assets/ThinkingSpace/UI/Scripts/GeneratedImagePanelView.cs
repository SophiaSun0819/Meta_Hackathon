using System;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public enum GeneratedImagePanelState
    {
        Unavailable,
        Loading,
        Ready,
        Error
    }

    [Serializable]
    public struct GeneratedImageAssetRequest
    {
        public string AccountId;
        public string SessionId;
        public string IdeaId;
        public string AssetId;
        public string RevisionId;
    }

    [Serializable]
    public struct GeneratedImagePanelBinding
    {
        public string AccountId;
        public string SessionId;
        public string IdeaId;
        public string AssetId;
        public string RevisionId;
        public string ParentRevisionId;
        public string SourcePrompt;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster))]
    public sealed class GeneratedImagePanelView : MonoBehaviour
    {
        public static readonly Vector2 InitialPanelSize=new Vector2(720f,720f);
        public static readonly Vector2 MinimumPanelSize=new Vector2(360f,360f);
        public static readonly Vector2 MaximumPanelSize=new Vector2(1440f,1440f);

        [SerializeField] private RawImage image;
        [SerializeField] private GeneratedImagePanelBinding binding;
        [SerializeField] private GeneratedImagePanelState state=GeneratedImagePanelState.Unavailable;
        [SerializeField] private string statusCode="unavailable";
        private WorldSpacePanelManipulator manipulator;

        public GeneratedImagePanelBinding Binding=>binding;
        public bool IsBound=>!string.IsNullOrWhiteSpace(binding.AssetId)
            &&!string.IsNullOrWhiteSpace(binding.SessionId)
            &&!string.IsNullOrWhiteSpace(binding.AccountId);
        public WorldSpacePanelManipulator Manipulator=>manipulator;
        public GeneratedImagePanelState State=>state;
        public string StatusCode=>statusCode;
        public event Action<GeneratedImageAssetRequest> AssetRequested;

        private void Awake()
        {
            RectTransform rect=transform as RectTransform;
            if(rect.sizeDelta.sqrMagnitude<1f)rect.sizeDelta=InitialPanelSize;
            image=image??GetComponentInChildren<RawImage>(true);
            if(image!=null)image.raycastTarget=false;
            manipulator=WorldSpacePanelManipulator.Attach(rect);
        }

        public void Bind(GeneratedImagePanelBinding value,Texture texture)
        {
            if(string.IsNullOrWhiteSpace(value.AccountId))throw new ArgumentException("Account ownership is required.",nameof(value));
            if(string.IsNullOrWhiteSpace(value.SessionId))throw new ArgumentException("Session ownership is required.",nameof(value));
            if(string.IsNullOrWhiteSpace(value.AssetId))throw new ArgumentException("Asset identity is required.",nameof(value));
            if(string.IsNullOrWhiteSpace(value.RevisionId))throw new ArgumentException("Immutable revision identity is required.",nameof(value));
            if(IsBound&&(binding.AssetId!=value.AssetId||binding.RevisionId!=value.RevisionId))
                throw new InvalidOperationException("A panel revision is immutable. Create a new panel for a regeneration or variation.");
            binding=value;
            image=image??GetComponentInChildren<RawImage>(true);
            if(image!=null){image.raycastTarget=false;image.texture=texture;}
            state=texture!=null?GeneratedImagePanelState.Ready:GeneratedImagePanelState.Unavailable;
            statusCode=texture!=null?"ready":"unavailable";
            manipulator=manipulator??WorldSpacePanelManipulator.Attach(transform as RectTransform);
        }

        public void RequestAsset()
        {
            if(!IsBound)throw new InvalidOperationException("Bind an owned image revision before requesting its asset.");
            state=GeneratedImagePanelState.Loading;
            statusCode="loading";
            AssetRequested?.Invoke(new GeneratedImageAssetRequest
            {
                AccountId=binding.AccountId,
                SessionId=binding.SessionId,
                IdeaId=binding.IdeaId,
                AssetId=binding.AssetId,
                RevisionId=binding.RevisionId
            });
        }

        public void ResolveAsset(GeneratedImageAssetRequest request,Texture texture)
        {
            EnsureMatchingRequest(request);
            if(texture==null)throw new ArgumentNullException(nameof(texture));
            image=image??GetComponentInChildren<RawImage>(true);
            if(image!=null){image.raycastTarget=false;image.texture=texture;}
            state=GeneratedImagePanelState.Ready;
            statusCode="ready";
        }

        public void SetAssetFailure(GeneratedImageAssetRequest request,string code,bool unavailable=false)
        {
            EnsureMatchingRequest(request);
            state=unavailable?GeneratedImagePanelState.Unavailable:GeneratedImagePanelState.Error;
            statusCode=NormalizeStatusCode(code,unavailable?"unavailable":"provider_error");
        }

        public void RevokeAccount(string accountId)
        {
            if(string.IsNullOrWhiteSpace(accountId)||binding.AccountId!=accountId)return;
            image=image??GetComponentInChildren<RawImage>(true);
            if(image!=null)image.texture=null;
            state=GeneratedImagePanelState.Unavailable;
            statusCode="auth_required";
        }

        private void EnsureMatchingRequest(GeneratedImageAssetRequest request)
        {
            if(request.AccountId!=binding.AccountId||request.SessionId!=binding.SessionId
                ||request.AssetId!=binding.AssetId||request.RevisionId!=binding.RevisionId)
                throw new InvalidOperationException("Asset responses must match the bound account, session, asset, and immutable revision.");
        }

        private static string NormalizeStatusCode(string code,string fallback)
        {
            if(string.IsNullOrWhiteSpace(code))return fallback;
            string normalized=code.Trim().ToLowerInvariant();
            foreach(char value in normalized)
                if(!(char.IsLetterOrDigit(value)||value=='_'||value=='-'))return fallback;
            return normalized.Length<=48?normalized:fallback;
        }

        public void SetPanelSize(Vector2 requested)
        {
            RectTransform rect=transform as RectTransform;
            rect.sizeDelta=new Vector2(
                Mathf.Clamp(requested.x,MinimumPanelSize.x,MaximumPanelSize.x),
                Mathf.Clamp(requested.y,MinimumPanelSize.y,MaximumPanelSize.y));
        }
    }
}

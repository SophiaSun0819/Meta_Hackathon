using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.UI
{
    public sealed class MemoryGeneratedImageAccessTokenSource:IGeneratedImageAccessTokenSource
    {
        private string accountId=string.Empty,token=string.Empty;
        public string AccountId=>accountId;
        public bool TryGetAccessToken(out string value){value=token;return !string.IsNullOrWhiteSpace(accountId)&&!string.IsNullOrWhiteSpace(token);}
        public void SetAuthenticatedSession(string ownedAccountId,string accessToken)
        {if(string.IsNullOrWhiteSpace(ownedAccountId)||string.IsNullOrWhiteSpace(accessToken))throw new ArgumentException("An authenticated account and access token are required.");accountId=ownedAccountId;token=accessToken;}
        public void Clear(){accountId=string.Empty;token=string.Empty;}
    }

    [DisallowMultipleComponent]
    public sealed class QuestEphemeralImageRuntime:MonoBehaviour
    {
        [SerializeField] private string backendOrigin=string.Empty;
        private readonly List<GeneratedImagePanelAssetLoader> activeLoaders=new List<GeneratedImagePanelAssetLoader>();
        private IGeneratedImageAccessTokenSource tokenSource;
        public string BackendOrigin=>backendOrigin;
        public int ActivePanelCount=>activeLoaders.Count;

        public void Configure(string origin,IGeneratedImageAccessTokenSource authenticatedTokenSource)
        {
            backendOrigin=NormalizeBackendOrigin(origin);
            tokenSource=authenticatedTokenSource??throw new ArgumentNullException(nameof(authenticatedTokenSource));
        }

        public GeneratedImagePanelView GenerateForIdea(string sessionId,string ideaId,string prompt,Vector3 worldPosition,Quaternion worldRotation,bool requestImmediately=true)
        {
            if(string.IsNullOrWhiteSpace(backendOrigin)||tokenSource==null||!tokenSource.TryGetAccessToken(out string ignored)||string.IsNullOrWhiteSpace(tokenSource.AccountId))throw new InvalidOperationException("Authenticated image generation is unavailable.");
            if(string.IsNullOrWhiteSpace(sessionId)||string.IsNullOrWhiteSpace(ideaId)||string.IsNullOrWhiteSpace(prompt))throw new ArgumentException("Session, idea, and prompt bindings are required.");
            var root=new GameObject("Generated Image Panel",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            root.transform.SetPositionAndRotation(worldPosition,worldRotation);root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var imageObject=new GameObject("Generated Image",typeof(RectTransform),typeof(RawImage));var imageRect=imageObject.GetComponent<RectTransform>();imageRect.SetParent(root.transform,false);imageRect.anchorMin=Vector2.zero;imageRect.anchorMax=Vector2.one;imageRect.offsetMin=imageRect.offsetMax=Vector2.zero;imageObject.GetComponent<RawImage>().raycastTarget=false;
            var panel=root.AddComponent<GeneratedImagePanelView>();string requestId=Guid.NewGuid().ToString("N");panel.Bind(new GeneratedImagePanelBinding{AccountId=tokenSource.AccountId,SessionId=sessionId,IdeaId=ideaId,AssetId=requestId,RevisionId=requestId,SourcePrompt=prompt},null);
            var loader=root.AddComponent<GeneratedImagePanelAssetLoader>();loader.Configure(panel,backendOrigin,tokenSource);activeLoaders.Add(loader);if(requestImmediately)panel.RequestAsset();return panel;
        }

        public void RevokeAuthenticatedAccount(string accountId)
        {for(int i=activeLoaders.Count-1;i>=0;i--){var loader=activeLoaders[i];if(loader==null){activeLoaders.RemoveAt(i);continue;}loader.RevokeAccount(accountId);}if(tokenSource is MemoryGeneratedImageAccessTokenSource memory&&memory.AccountId==accountId)memory.Clear();}

        public static string NormalizeBackendOrigin(string value)
        {
            if(string.IsNullOrWhiteSpace(value)||!Uri.TryCreate(value.Trim(),UriKind.Absolute,out Uri uri)||uri.Scheme!=Uri.UriSchemeHttps||string.IsNullOrWhiteSpace(uri.Host)||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0||uri.AbsolutePath!="/")throw new ArgumentException("A bare HTTPS backend origin is required.",nameof(value));
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
    }
}

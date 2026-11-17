using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ThinkingSpace.UI
{
    public interface IGeneratedImageAccessTokenSource { string AccountId{get;} bool TryGetAccessToken(out string token); }
    [Serializable] public sealed class EphemeralImageSourceWireRequest { public string kind="idea";public string idea_id;public string[] source_idea_ids; }
    [Serializable] public sealed class EphemeralImageGenerateWireRequest { public string contract_version="1.0",request_id,session_id,operation="generate",user_request;public EphemeralImageSourceWireRequest source; }
    [Serializable] public sealed class EphemeralImageGenerationWireResponse { public string generation_id,owner_id,session_id,idea_id,operation,mime_type,asset_sha256,created_at;public string[] source_idea_ids;public int width,height;public float estimated_cost_cents;public bool ephemeral; }
    [Serializable] public sealed class EphemeralImageAssetWireResponse { public string encoding,data; }
    [Serializable] public sealed class EphemeralImageGenerateWireResponse { public string contract_version;public EphemeralImageGenerationWireResponse generation;public EphemeralImageAssetWireResponse asset; }
    [Serializable] public sealed class EphemeralImageErrorWireResponse { public string error,code; }

    public static class QuestWebPDecoder
    {
        private const string JavaClass="com.meta_hackathon.thinkingspace.media.QuestWebPDecoder";
        public static Texture2D Decode(byte[] webp)
        {
            if(!HasWebPHeader(webp))throw new ArgumentException("A valid WebP payload is required.",nameof(webp));
#if UNITY_ANDROID && !UNITY_EDITOR
            byte[] png;using(var bridge=new AndroidJavaClass(JavaClass))png=bridge.CallStatic<byte[]>("decodeToPng",webp);
            if(png==null||png.Length==0)throw new InvalidOperationException("Quest WebP decoding failed.");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            if(!texture.LoadImage(png,true)){UnityEngine.Object.Destroy(texture);throw new InvalidOperationException("Quest image decoding failed.");}
            return texture;
#else
            throw new PlatformNotSupportedException("WebP decoding is performed by the Quest Android runtime.");
#endif
        }
        public static bool HasWebPHeader(byte[] value)=>value!=null&&value.Length>=12&&value[0]=='R'&&value[1]=='I'&&value[2]=='F'&&value[3]=='F'&&value[8]=='W'&&value[9]=='E'&&value[10]=='B'&&value[11]=='P';
    }

    [DisallowMultipleComponent]
    public sealed class GeneratedImagePanelAssetLoader : MonoBehaviour
    {
        public const int MaximumInlineImageBytes=8*1024*1024;
        [SerializeField] private string backendBaseUrl=string.Empty;
        [SerializeField,Range(5,45)] private int timeoutSeconds=45;
        private readonly Dictionary<string,Texture2D> accountScopedCache=new Dictionary<string,Texture2D>();
        private IGeneratedImageAccessTokenSource tokenSource;private GeneratedImagePanelView panel;
        public int CachedAssetCount=>accountScopedCache.Count;
        public string BackendOrigin=>backendBaseUrl;
        public void Configure(GeneratedImagePanelView target,string baseUrl,IGeneratedImageAccessTokenSource accessTokenSource)
        {if(target==null)throw new ArgumentNullException(nameof(target));if(string.IsNullOrWhiteSpace(baseUrl)||!Uri.TryCreate(baseUrl,UriKind.Absolute,out Uri uri)||uri.Scheme!="https")throw new ArgumentException("A trusted HTTPS backend URL is required.",nameof(baseUrl));if(accessTokenSource==null)throw new ArgumentNullException(nameof(accessTokenSource));if(panel!=null)panel.AssetRequested-=HandleAssetRequested;panel=target;backendBaseUrl=baseUrl.TrimEnd('/');tokenSource=accessTokenSource;panel.AssetRequested+=HandleAssetRequested;}
        public void ClearAccountCache(string accountId){if(string.IsNullOrWhiteSpace(accountId))return;string prefix=accountId+"|";var keys=new List<string>();foreach(string key in accountScopedCache.Keys)if(key.StartsWith(prefix,StringComparison.Ordinal))keys.Add(key);foreach(string key in keys){Texture2D texture=accountScopedCache[key];accountScopedCache.Remove(key);if(texture!=null)Destroy(texture);}}
        public void RevokeAccount(string accountId){StopAllCoroutines();ClearAccountCache(accountId);if(panel!=null)panel.RevokeAccount(accountId);if(tokenSource!=null&&tokenSource.AccountId==accountId)tokenSource=null;}
        private void OnDestroy(){if(panel!=null)panel.AssetRequested-=HandleAssetRequested;foreach(Texture2D texture in accountScopedCache.Values)if(texture!=null)Destroy(texture);accountScopedCache.Clear();}
        private void HandleAssetRequested(GeneratedImageAssetRequest request)=>StartCoroutine(GenerateOwnedAsset(request));
        private IEnumerator GenerateOwnedAsset(GeneratedImageAssetRequest request)
        {
            if(tokenSource==null||tokenSource.AccountId!=request.AccountId||!tokenSource.TryGetAccessToken(out string token)||string.IsNullOrWhiteSpace(token)){panel.SetAssetFailure(request,"auth_required",true);yield break;}
            string prompt=panel.Binding.SourcePrompt?.Trim();if(string.IsNullOrWhiteSpace(prompt)){panel.SetAssetFailure(request,"request_invalid");yield break;}
            var payload=new EphemeralImageGenerateWireRequest{request_id=request.AssetId,session_id=request.SessionId,user_request=prompt,source=new EphemeralImageSourceWireRequest{kind=string.IsNullOrWhiteSpace(request.IdeaId)?"session":"idea",idea_id=string.IsNullOrWhiteSpace(request.IdeaId)?null:request.IdeaId}};
            byte[] body=Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using(var web=new UnityWebRequest(backendBaseUrl+"/api/images/generate",UnityWebRequest.kHttpVerbPOST))
            {
                web.uploadHandler=new UploadHandlerRaw(body);web.downloadHandler=new DownloadHandlerBuffer();web.timeout=timeoutSeconds;web.SetRequestHeader("Authorization","Bearer "+token);web.SetRequestHeader("Content-Type","application/json");web.SetRequestHeader("Accept","application/json");yield return web.SendWebRequest();token=string.Empty;
                if(web.result!=UnityWebRequest.Result.Success){string failure=MapHttpFailure(web.responseCode,web.downloadHandler?.text);panel.SetAssetFailure(request,failure,failure=="auth_required"||failure=="image_generation_disabled"||failure=="quota_exhausted");yield break;}
                EphemeralImageGenerateWireResponse response;try{response=JsonUtility.FromJson<EphemeralImageGenerateWireResponse>(web.downloadHandler.text);}catch{response=null;}
                byte[] webp;try{webp=ValidateResponse(request,response);}catch{panel.SetAssetFailure(request,"invalid_asset_response");yield break;}
                Texture2D texture;try{texture=QuestWebPDecoder.Decode(webp);}catch{panel.SetAssetFailure(request,"invalid_image");yield break;}
                string key=CacheKey(response.generation);if(accountScopedCache.TryGetValue(key,out Texture2D prior)&&prior!=null)Destroy(prior);accountScopedCache[key]=texture;panel.ResolveAsset(request,texture);
            }
        }
        public static byte[] ValidateResponse(GeneratedImageAssetRequest request,EphemeralImageGenerateWireResponse response)
        {if(response==null||response.contract_version!="1.0"||response.generation==null||response.asset==null)throw new InvalidOperationException();var generation=response.generation;if(!generation.ephemeral||string.IsNullOrWhiteSpace(generation.generation_id)||generation.owner_id!=request.AccountId||generation.session_id!=request.SessionId||(generation.idea_id??string.Empty)!=(request.IdeaId??string.Empty)||generation.operation!="generate"||generation.mime_type!="image/webp"||generation.width!=1024||generation.height!=1024||float.IsNaN(generation.estimated_cost_cents)||float.IsInfinity(generation.estimated_cost_cents)||generation.estimated_cost_cents<0f||generation.estimated_cost_cents>30f||string.IsNullOrWhiteSpace(generation.asset_sha256)||response.asset.encoding!="base64"||string.IsNullOrWhiteSpace(response.asset.data))throw new InvalidOperationException();if(response.asset.data.Length>((MaximumInlineImageBytes+2)/3)*4+4)throw new InvalidOperationException();byte[] bytes;try{bytes=Convert.FromBase64String(response.asset.data);}catch{throw new InvalidOperationException();}if(bytes.Length==0||bytes.Length>MaximumInlineImageBytes||!QuestWebPDecoder.HasWebPHeader(bytes)||!FixedEquals(Sha256(bytes),generation.asset_sha256))throw new InvalidOperationException();return bytes;}
        public static string CacheKey(EphemeralImageGenerationWireResponse generation)=>generation.owner_id+"|"+generation.session_id+"|"+generation.generation_id+"|"+generation.asset_sha256.ToLowerInvariant();
        public static string Sha256(byte[] bytes){using(SHA256 hash=SHA256.Create()){var value=hash.ComputeHash(bytes);var builder=new StringBuilder(value.Length*2);foreach(byte item in value)builder.Append(item.ToString("x2"));return builder.ToString();}}
        private static bool FixedEquals(string left,string right){if(left==null||right==null||left.Length!=right.Length)return false;int diff=0;for(int i=0;i<left.Length;i++)diff|=char.ToLowerInvariant(left[i])^char.ToLowerInvariant(right[i]);return diff==0;}
        public static string MapHttpFailure(long status,string body=null)
        {
            if(status==400)return "invalid_request";if(status==401)return "auth_required";if(status==403||status==404)return "access_denied";
            if(status==429){string code=SafeErrorCode(body);return code.Contains("quota")||code.Contains("budget")?"quota_exhausted":"rate_limited";}
            if(status==503)return "image_generation_disabled";if(status==504)return "timeout";return "provider_error";
        }
        private static string SafeErrorCode(string body)
        {if(string.IsNullOrWhiteSpace(body)||body.Length>4096)return string.Empty;try{var value=JsonUtility.FromJson<EphemeralImageErrorWireResponse>(body);return ((value?.code??value?.error)??string.Empty).Trim().ToLowerInvariant();}catch{return string.Empty;}}
    }
}

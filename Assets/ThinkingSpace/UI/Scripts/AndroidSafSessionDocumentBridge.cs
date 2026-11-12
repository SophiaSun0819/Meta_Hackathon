using System;
using System.IO;
using UnityEngine;

namespace ThinkingSpace.UI
{
    public static class AndroidSafSessionDocumentBridge
    {
        private const string BridgeClass="com.thinkingspace.sessiontransfer.ThinkingSpaceDocumentPickerActivity";

        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using var bridge=new AndroidJavaClass(BridgeClass);
                    return bridge.CallStatic<bool>("isPickerAvailable");
                }
                catch(Exception){return false;}
#else
                return false;
#endif
            }
        }

        public static bool BeginExport(string callbackObject,string sourcePath,string suggestedName)
            =>Begin("export",callbackObject,sourcePath,suggestedName,
                ThinkingSpaceSessionTransferService.MaximumFileBytes);

        public static bool BeginImport(string callbackObject,string destinationDirectory)
            =>Begin("import",callbackObject,destinationDirectory,string.Empty,
                ThinkingSpaceSessionTransferService.MaximumFileBytes);

        private static bool Begin(string operation,string callbackObject,string path,string suggestedName,int maximumBytes)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(string.IsNullOrWhiteSpace(callbackObject)||string.IsNullOrWhiteSpace(path))return false;
            try
            {
                using var bridge=new AndroidJavaClass(BridgeClass);
                bridge.CallStatic("begin",operation,callbackObject,path,suggestedName??string.Empty,maximumBytes);
                return true;
            }
            catch(Exception exception)
            {
                Debug.LogWarning("Thinking Space document picker could not start: "+exception.GetType().Name);
                return false;
            }
#else
            return false;
#endif
        }

        public static bool TryParseResult(string payload,out SafDocumentResult result)
        {
            result=default;
            if(string.IsNullOrWhiteSpace(payload))return false;
            string[] parts=payload.Split('|');
            if(parts.Length!=4)return false;
            try
            {
                result=new SafDocumentResult(parts[0],parts[1],Decode(parts[2]),Decode(parts[3]));
                return result.Operation=="export"||result.Operation=="import";
            }
            catch(FormatException){return false;}
        }

        private static string Decode(string value)
            =>System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value??string.Empty));
    }

    public readonly struct SafDocumentResult
    {
        public string Operation{get;}
        public string Status{get;}
        public string Message{get;}
        public string FilePath{get;}
        public bool Success=>Status=="success";
        public bool Cancelled=>Status=="cancelled";
        public SafDocumentResult(string operation,string status,string message,string filePath)
        {Operation=operation;Status=status;Message=message;FilePath=filePath;}
    }
}

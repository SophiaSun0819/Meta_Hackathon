using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ThinkingSpace.UI
{
    public interface ISessionExportFormat
    {
        string Name { get; }
        string Extension { get; }
        string Serialize(ThinkingSpaceSessionDocument document);
    }

    [Serializable]
    public sealed class ThinkingSpaceSessionDocument
    {
        public string FormatIdentifier=ThinkingSpaceSessionTransferService.FormatIdentifier;
        public int FormatVersion=ThinkingSpaceSessionTransferService.CurrentFormatVersion;
        public string ExportedUtc=string.Empty;
        public string SessionId=string.Empty;
        public string SessionTitle=string.Empty;
        public int SessionNumber;
        public string CreatedUtc=string.Empty;
        public string UpdatedUtc=string.Empty;
        public List<BrainstormIdeaRecord> Ideas=new List<BrainstormIdeaRecord>();
        public List<IdeaRelationshipRecord> Relationships=new List<IdeaRelationshipRecord>();
        public List<ConversationRecord> Conversation=new List<ConversationRecord>();
    }

    public sealed class SessionTransferResult
    {
        public bool Success { get; }
        public string Message { get; }
        public string FilePath { get; }
        public BrainstormSession ImportedSession { get; }
        private SessionTransferResult(bool success,string message,string path,BrainstormSession session)
        {Success=success;Message=message;FilePath=path;ImportedSession=session;}
        public static SessionTransferResult Ok(string message,string path="",BrainstormSession session=null)
            =>new SessionTransferResult(true,message,path,session);
        public static SessionTransferResult Fail(string message)
            =>new SessionTransferResult(false,message,string.Empty,null);
    }

    public sealed class ThinkingSpaceSessionTransferService
    {
        public const string FormatIdentifier="thinking-space-session";
        public const int CurrentFormatVersion=1;
        public const int MaximumFileBytes=4*1024*1024;
        private const int MaximumIdeas=512,MaximumRelationships=2048,MaximumConversation=4096;
        private static readonly Regex SafeId=new Regex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",RegexOptions.CultureInvariant);

        private sealed class JsonExportFormat : ISessionExportFormat
        {
            public string Name=>"json";
            public string Extension=>".json";
            public string Serialize(ThinkingSpaceSessionDocument document)=>JsonUtility.ToJson(document,true);
        }

        private readonly BrainstormSessionStore store;
        private readonly string exportDirectory,importDirectory;
        private readonly Dictionary<string,ISessionExportFormat> formats=
            new Dictionary<string,ISessionExportFormat>(StringComparer.OrdinalIgnoreCase);
        public string ExportDirectory=>exportDirectory;
        public string ImportDirectory=>importDirectory;

        public static ThinkingSpaceSessionTransferService CreateForApplication(BrainstormSessionStore sessionStore)
        {
            string root=Path.Combine(Application.persistentDataPath,"ThinkingSpace");
            return new ThinkingSpaceSessionTransferService(sessionStore,
                Path.Combine(root,"Exports"),Path.Combine(root,"Imports"));
        }

        public ThinkingSpaceSessionTransferService(BrainstormSessionStore sessionStore,
            string exports,string imports)
        {
            store=sessionStore??throw new ArgumentNullException(nameof(sessionStore));
            exportDirectory=exports??throw new ArgumentNullException(nameof(exports));
            importDirectory=imports??throw new ArgumentNullException(nameof(imports));
            Register(new JsonExportFormat());
        }

        private void Register(ISessionExportFormat format)=>formats[format.Name]=format;
        public string[] ListSupportedExportFormats()
        {var values=new string[formats.Count];formats.Keys.CopyTo(values,0);Array.Sort(values,StringComparer.Ordinal);return values;}

        public SessionTransferResult ExportCurrentSession(string format="json")
        {
            BrainstormSession session=store.ActiveSession;
            if(session==null)return SessionTransferResult.Fail("No active session is selected.");
            if(!formats.TryGetValue(format??string.Empty,out ISessionExportFormat serializer))
                return SessionTransferResult.Fail("Unsupported export format.");
            try
            {
                ThinkingSpaceSessionDocument document=CreateDocument(session);
                string validation=Validate(document);
                if(!string.IsNullOrEmpty(validation))return SessionTransferResult.Fail("Export validation failed: "+validation);
                string json=serializer.Serialize(document);
                byte[] bytes=new UTF8Encoding(false).GetBytes(json);
                if(bytes.Length>MaximumFileBytes)return SessionTransferResult.Fail("Session export exceeds the 4 MB limit.");
                Directory.CreateDirectory(exportDirectory);
                string baseName="thinking-space-session-"+session.Number.ToString(CultureInfo.InvariantCulture)
                    +"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture);
                string path=UniquePath(exportDirectory,baseName,serializer.Extension);
                WriteAtomicNew(path,bytes);
                return SessionTransferResult.Ok("Exported "+Path.GetFileName(path),path);
            }
            catch(Exception exception){return SessionTransferResult.Fail("Session export failed: "+exception.Message);}
        }

        public SessionTransferResult ImportSession(string fileReference,bool confirmed=false)
        {
            if(store.ActiveSession!=null&&!confirmed)
                return SessionTransferResult.Fail("Import confirmation is required before switching the active session.");
            if(string.IsNullOrWhiteSpace(fileReference))return SessionTransferResult.Fail("No import file was selected.");
            string fileName=Path.GetFileName(fileReference);
            if(!string.Equals(fileName,fileReference,StringComparison.Ordinal)||!fileName.EndsWith(".json",StringComparison.OrdinalIgnoreCase))
                return SessionTransferResult.Fail("Unsafe or unsupported import file reference.");
            try
            {
                string root=Path.GetFullPath(importDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string path=Path.GetFullPath(Path.Combine(importDirectory,fileName));
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))return SessionTransferResult.Fail("Unsafe import path.");
                if(!File.Exists(path))return SessionTransferResult.Fail("Selected import file was not found.");
                FileInfo info=new FileInfo(path);
                if(info.Length<=0||info.Length>MaximumFileBytes)return SessionTransferResult.Fail("Import file is empty or exceeds the 4 MB limit.");
                string json;
                using(var reader=new StreamReader(path,new UTF8Encoding(false,true),true))json=reader.ReadToEnd();
                ThinkingSpaceSessionDocument document=JsonUtility.FromJson<ThinkingSpaceSessionDocument>(json);
                string validation=Validate(document);
                if(!string.IsNullOrEmpty(validation))return SessionTransferResult.Fail("Import validation failed: "+validation);
                if(!store.TryImportSession(document.CreatedUtc,document.Ideas,document.Relationships,
                    document.Conversation,out BrainstormSession imported))
                    return SessionTransferResult.Fail(string.IsNullOrEmpty(store.LastPersistenceError)
                        ?"Imported session could not be saved.":store.LastPersistenceError);
                return SessionTransferResult.Ok("Imported as Session "+imported.Number,path,imported);
            }
            catch(Exception exception){return SessionTransferResult.Fail("Session import failed: "+exception.Message);}
        }

        public bool DeleteStagedImport(string fileReference)
        {
            if(string.IsNullOrWhiteSpace(fileReference))return false;
            string fileName=Path.GetFileName(fileReference);
            if(!string.Equals(fileName,fileReference,StringComparison.Ordinal))return false;
            try
            {
                string root=Path.GetFullPath(importDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string path=Path.GetFullPath(Path.Combine(importDirectory,fileName));
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return false;
                File.Delete(path);return true;
            }
            catch(Exception){return false;}
        }

        private static ThinkingSpaceSessionDocument CreateDocument(BrainstormSession session)
        {
            return new ThinkingSpaceSessionDocument{ExportedUtc=DateTime.UtcNow.ToString("O"),
                SessionId=session.SessionId,SessionTitle="Session "+session.Number,SessionNumber=session.Number,
                CreatedUtc=session.CreatedUtc,UpdatedUtc=session.UpdatedUtc,
                Ideas=CloneIdeas(session.Ideas),Relationships=CloneRelationships(session.Relationships),
                Conversation=CloneConversation(session.Conversation)};
        }

        public static string Validate(ThinkingSpaceSessionDocument document)
        {
            if(document==null)return "Document is malformed.";
            if(document.FormatIdentifier!=FormatIdentifier)return "Format identifier is invalid.";
            if(document.FormatVersion!=CurrentFormatVersion)return "Format version is unsupported.";
            if(!ValidId(document.SessionId))return "Session ID is invalid.";
            if(string.IsNullOrWhiteSpace(document.SessionTitle)||document.SessionTitle.Length>128)return "Session title is invalid.";
            if(document.SessionNumber<=0)return "Session number is invalid.";
            if(!ValidTimestamp(document.ExportedUtc)||!ValidTimestamp(document.CreatedUtc)||!ValidTimestamp(document.UpdatedUtc))return "Session timestamp is invalid.";
            if(document.Ideas==null||document.Relationships==null||document.Conversation==null)return "Required collections are missing.";
            if(document.Ideas.Count>MaximumIdeas||document.Relationships.Count>MaximumRelationships||document.Conversation.Count>MaximumConversation)return "Session collection limit exceeded.";
            var ids=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<document.Ideas.Count;i++)
            {
                BrainstormIdeaRecord idea=document.Ideas[i];
                if(idea==null||!ValidId(idea.IdeaId)||!ids.Add(idea.IdeaId))return "Idea IDs are invalid or duplicated.";
                if(!ValidRequiredText(idea.Title,256)||!ValidRequiredText(idea.Summary,16000)
                    ||!ValidRequiredText(idea.Category,128)||!ValidText(idea.SourceTranscript,32000)
                    ||!IdeaVisualColorPalette.IsSupported(idea.VisualColor))return "Idea text or visual color is invalid.";
                if(!string.IsNullOrEmpty(idea.CreatedUtc)&&!ValidTimestamp(idea.CreatedUtc))return "Idea timestamp is invalid.";
                if(idea.Bubble==null||!Finite(idea.Bubble.Position)||!Finite(idea.Bubble.Rotation)||!Finite(idea.Bubble.Scale)
                    ||Quaternion.Dot(idea.Bubble.Rotation,idea.Bubble.Rotation)<.0001f
                    ||idea.Bubble.Scale.x<=0f||idea.Bubble.Scale.y<=0f||idea.Bubble.Scale.z<=0f)return "Bubble transform is invalid.";
            }
            for(int i=0;i<document.Ideas.Count;i++)
            {
                List<string> related=document.Ideas[i].RelatedIdeaIds;
                if(related==null)return "Idea relationship references are missing.";
                var unique=new HashSet<string>(StringComparer.Ordinal);
                for(int j=0;j<related.Count;j++)if(!ids.Contains(related[j])||!unique.Add(related[j]))return "Idea relationship reference is invalid.";
            }
            for(int i=0;i<document.Relationships.Count;i++)
            {
                IdeaRelationshipRecord relation=document.Relationships[i];
                if(relation==null||!ids.Contains(relation.SourceIdeaId)||!ids.Contains(relation.TargetIdeaId)
                    ||!ValidRequiredText(relation.RelationshipType,128))return "Relationship is invalid or broken.";
            }
            var messageIds=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<document.Conversation.Count;i++)
            {
                ConversationRecord message=document.Conversation[i];
                if(message==null||!ValidId(message.MessageId)||!messageIds.Add(message.MessageId)
                    ||!ValidRequiredText(message.Speaker,64)||!ValidRequiredText(message.Text,32000)
                    ||!ValidText(message.MessageType,128)||!ValidText(message.RelatedRequestId,128)
                    ||!ValidTimestamp(message.CreatedUtc))return "Conversation record is invalid.";
                if(message.RelatedIdeaIds==null)return "Conversation["+i+"].related_idea_ids are missing.";
                var historicalIds=new HashSet<string>(StringComparer.Ordinal);
                for(int j=0;j<message.RelatedIdeaIds.Count;j++)
                {
                    string reference=message.RelatedIdeaIds[j];
                    if(!ValidId(reference))return "Conversation["+i+"].related_idea_ids["+j+"] is malformed.";
                    if(!historicalIds.Add(reference))return "Conversation["+i+"].related_idea_ids["+j+"] is duplicated.";
                    // Conversation references are immutable historical metadata. A referenced
                    // idea may have been deliberately deleted from the live graph; retain that
                    // well-formed ID for history. Live idea/relationship references above remain
                    // membership-validated against the current idea inventory.
                }
            }
            return string.Empty;
        }

        private static bool ValidId(string value)=>!string.IsNullOrEmpty(value)&&SafeId.IsMatch(value);
        private static bool ValidText(string value,int maximum)=>value!=null&&value.Length<=maximum&&value.IndexOf('\0')<0;
        private static bool ValidRequiredText(string value,int maximum)=>ValidText(value,maximum)&&!string.IsNullOrWhiteSpace(value);
        private static bool ValidTimestamp(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out _);
        private static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        private static bool Finite(Quaternion value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z)&&Finite(value.w);
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        private static string UniquePath(string directory,string baseName,string extension)
        {for(int i=0;i<1000;i++){string suffix=i==0?string.Empty:"-"+i;string path=Path.Combine(directory,baseName+suffix+extension);if(!File.Exists(path))return path;}throw new IOException("No unique export filename was available.");}
        private static void WriteAtomicNew(string path,byte[] bytes)
        {
            string temporary=path+".temporary";
            try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}File.Move(temporary,path);}
            catch{if(File.Exists(temporary))File.Delete(temporary);throw;}
        }
        private static List<BrainstormIdeaRecord> CloneIdeas(IReadOnlyList<BrainstormIdeaRecord> source)
        {var result=new List<BrainstormIdeaRecord>();for(int i=0;i<source.Count;i++){BrainstormIdeaRecord v=source[i];BubbleTransformRecord b=v.Bubble??new BubbleTransformRecord();result.Add(new BrainstormIdeaRecord{IdeaId=v.IdeaId,Title=v.Title,Summary=v.Summary,Category=v.Category,VisualColor=IdeaVisualColorPalette.Normalize(v.VisualColor),SourceTranscript=v.SourceTranscript,CreatedUtc=v.CreatedUtc,RelatedIdeaIds=v.RelatedIdeaIds==null?new List<string>():new List<string>(v.RelatedIdeaIds),Bubble=new BubbleTransformRecord{Position=b.Position,Rotation=b.Rotation,Scale=b.Scale}});}return result;}
        private static List<IdeaRelationshipRecord> CloneRelationships(IReadOnlyList<IdeaRelationshipRecord> source)
        {var result=new List<IdeaRelationshipRecord>();for(int i=0;i<source.Count;i++){IdeaRelationshipRecord v=source[i];result.Add(new IdeaRelationshipRecord{SourceIdeaId=v.SourceIdeaId,TargetIdeaId=v.TargetIdeaId,RelationshipType=v.RelationshipType});}return result;}
        private static List<ConversationRecord> CloneConversation(IReadOnlyList<ConversationRecord> source)
        {var result=new List<ConversationRecord>();for(int i=0;i<source.Count;i++){ConversationRecord v=source[i];result.Add(new ConversationRecord{MessageId=v.MessageId,Speaker=v.Speaker,Text=v.Text,CreatedUtc=v.CreatedUtc,MessageType=v.MessageType,RelatedRequestId=v.RelatedRequestId,RelatedIdeaIds=v.RelatedIdeaIds==null?new List<string>():new List<string>(v.RelatedIdeaIds)});}return result;}
    }
}

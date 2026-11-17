using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ThinkingSpace.UI;

namespace ThinkingSpace.Pairing
{
    public sealed class LocalSessionSyncStore
    {
        private sealed class Entry { public int Revision;public string CanonicalJson;public string Fingerprint;public SessionTombstoneV2 Tombstone; }
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private sealed class Operation { public string Fingerprint;public SessionMutationResultStatus Result; }
        private readonly Dictionary<string,Operation> operations=new Dictionary<string,Operation>(StringComparer.Ordinal);

        public SessionMutationResultStatus Apply(SessionSyncMutationV2 mutation)
        {
            if(mutation==null||!Enum.TryParse(mutation.Kind,false,out SessionMutationKind kind))return SessionMutationResultStatus.REJECTED;
            if(kind==SessionMutationKind.UPSERT)
            {
                if(mutation.Snapshot==null||mutation.Snapshot.CanonicalDocument==null)return SessionMutationResultStatus.REJECTED;
                string expectedFingerprint=QuestSessionSyncAdapter.Sha256Hex(new UTF8Encoding(false).GetBytes(
                    QuestSessionSyncAdapter.CanonicalJson(mutation.Snapshot.CanonicalDocument)));
                if(!string.Equals(expectedFingerprint,mutation.Snapshot.CanonicalFingerprint,StringComparison.Ordinal))
                    return SessionMutationResultStatus.REJECTED;
            }
            string operationFingerprint=QuestSessionSyncAdapter.Sha256Hex(new UTF8Encoding(false).GetBytes(
                JsonConvert.SerializeObject(mutation,Formatting.None)));
            if(operations.TryGetValue(mutation.OperationId,out Operation replay))
                return string.Equals(replay.Fingerprint,operationFingerprint,StringComparison.Ordinal)
                    ?replay.Result:SessionMutationResultStatus.REJECTED;
            entries.TryGetValue(mutation.CloudSessionId,out Entry current);
            int revision=current?.Revision??0;
            if(revision!=mutation.ExpectedRevision){operations[mutation.OperationId]=new Operation{Fingerprint=operationFingerprint,Result=SessionMutationResultStatus.CONFLICT};return SessionMutationResultStatus.CONFLICT;}
            if(kind==SessionMutationKind.UPSERT)
                entries[mutation.CloudSessionId]=new Entry{Revision=mutation.Snapshot.Revision,
                    CanonicalJson=mutation.Snapshot.CanonicalDocument.ToString(Formatting.None),Fingerprint=mutation.Snapshot.CanonicalFingerprint};
            else if(current==null)return SessionMutationResultStatus.REJECTED;
            else if(kind==SessionMutationKind.RENAME)
            {
                JObject document=JObject.Parse(current.CanonicalJson);document["SessionTitle"]=mutation.Title??string.Empty;
                current.CanonicalJson=document.ToString(Formatting.None);current.Fingerprint=QuestSessionSyncAdapter.Fingerprint(document);current.Revision++;
            }
            else if(kind==SessionMutationKind.DELETE_EVERYWHERE)
            {
                current.Revision++;
                current.Tombstone=new SessionTombstoneV2{CloudSessionId=mutation.CloudSessionId,
                    PortableSessionId=mutation.PortableSessionId,DeletedRevision=current.Revision,
                    DeletedAt=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),
                    RecoverUntil=DateTime.UtcNow.AddDays(30).ToString("O",CultureInfo.InvariantCulture)};
            }
            else if(kind==SessionMutationKind.RESTORE)
            { if(current.Tombstone==null)return SessionMutationResultStatus.REJECTED;current.Revision++;current.Tombstone=null; }
            operations[mutation.OperationId]=new Operation{Fingerprint=operationFingerprint,Result=SessionMutationResultStatus.APPLIED};
            return SessionMutationResultStatus.APPLIED;
        }

        public bool TryDownload(string cloudSessionId,out string canonicalJson,out int revision)
        {if(entries.TryGetValue(cloudSessionId??string.Empty,out Entry value)&&value.Tombstone==null){canonicalJson=value.CanonicalJson;revision=value.Revision;return true;}canonicalJson=string.Empty;revision=0;return false;}
        public bool TryGetTombstone(string cloudSessionId,out SessionTombstoneV2 tombstone)
        {if(entries.TryGetValue(cloudSessionId??string.Empty,out Entry value)&&value.Tombstone!=null){tombstone=value.Tombstone;return true;}tombstone=null;return false;}
    }

    public sealed class QuestSessionSyncAdapter
    {
        private readonly ThinkingSpaceSessionTransferService transfer;
        public QuestSessionSyncAdapter(ThinkingSpaceSessionTransferService transferService)
            =>transfer=transferService??throw new ArgumentNullException(nameof(transferService));

        public SessionSyncMutationV2 CreateUpload(string canonicalJson,string operationId,string cloudSessionId,
            string portableSessionId,string deviceLocalSessionId,int expectedRevision)
        {
            if(string.IsNullOrWhiteSpace(canonicalJson))throw new ArgumentException("Canonical session JSON is required.",nameof(canonicalJson));
            byte[] bytes=new UTF8Encoding(false,true).GetBytes(canonicalJson);
            if(bytes.Length>JudgeAccountSyncContract.MaximumRequestBytes)throw new InvalidDataException("Sync request exceeds the 1 MiB contract limit.");
            JObject document=JObject.Parse(canonicalJson);
            if((string)document["FormatIdentifier"]!=ThinkingSpaceSessionTransferService.FormatIdentifier
                ||(int?)document["FormatVersion"]!=ThinkingSpaceSessionTransferService.CurrentFormatVersion)
                throw new InvalidDataException("Portable session contract is unsupported.");
            string updatedAt=(string)document["UpdatedUtc"]??string.Empty;
            string title=(string)document["SessionTitle"]??"Thinking Space Session";
            byte[] canonicalBytes=new UTF8Encoding(false).GetBytes(CanonicalJson(document));
            var snapshot=new PortableSessionSnapshotV2{CloudSessionId=RequireUuid(cloudSessionId),
                PortableSessionId=RequireId(portableSessionId),DeviceLocalSessionId=RequireId(deviceLocalSessionId),
                Revision=checked(expectedRevision+1),Title=title,UpdatedAt=updatedAt,CanonicalDocument=document,
                CanonicalFingerprint=Sha256Hex(canonicalBytes)};
            return new SessionSyncMutationV2{OperationId=RequireUuid(operationId),CloudSessionId=snapshot.CloudSessionId,
                ExpectedRevision=expectedRevision,PortableSessionId=snapshot.PortableSessionId,
                DeviceLocalSessionId=snapshot.DeviceLocalSessionId,Snapshot=snapshot};
        }

        public SessionTransferResult ImportDownloadedAsNew(string canonicalJson,bool confirmed)
        {
            if(!confirmed)return SessionTransferResult.Fail("Import confirmation is required before creating a downloaded session.");
            byte[] bytes=new UTF8Encoding(false,true).GetBytes(canonicalJson??string.Empty);
            if(bytes.Length==0||bytes.Length>ThinkingSpaceSessionTransferService.MaximumFileBytes)
                return SessionTransferResult.Fail("Downloaded session is empty or exceeds the 4 MB import limit.");
            Directory.CreateDirectory(transfer.ImportDirectory);
            string fileName="downloaded-session-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N")+".json";
            string path=Path.Combine(transfer.ImportDirectory,fileName);
            try
            {
                using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                return transfer.ImportSession(fileName,true);
            }
            catch(Exception exception){return SessionTransferResult.Fail("Downloaded session could not be staged: "+exception.Message);}
            finally{transfer.DeleteStagedImport(fileName);}
        }

        public SessionSyncMutationV2 CreateRename(string operationId,string cloudSessionId,string portableSessionId,
            string deviceLocalSessionId,int expectedRevision,string title)
            =>CreateMetadataMutation(SessionMutationKind.RENAME,operationId,cloudSessionId,portableSessionId,
                deviceLocalSessionId,expectedRevision,title);

        public SessionSyncMutationV2 CreateDelete(string operationId,string cloudSessionId,string portableSessionId,
            string deviceLocalSessionId,int expectedRevision)
            =>CreateMetadataMutation(SessionMutationKind.DELETE_EVERYWHERE,operationId,cloudSessionId,portableSessionId,
                deviceLocalSessionId,expectedRevision,null);

        public SessionSyncMutationV2 CreateRestore(string operationId,string cloudSessionId,string portableSessionId,
            string deviceLocalSessionId,int expectedRevision)
            =>CreateMetadataMutation(SessionMutationKind.RESTORE,operationId,cloudSessionId,portableSessionId,
                deviceLocalSessionId,expectedRevision,null);

        private static SessionSyncMutationV2 CreateMetadataMutation(SessionMutationKind kind,string operationId,
            string cloudSessionId,string portableSessionId,string deviceLocalSessionId,int expectedRevision,string title)
            =>new SessionSyncMutationV2{OperationId=RequireUuid(operationId),CloudSessionId=RequireUuid(cloudSessionId),
                ExpectedRevision=expectedRevision,Kind=kind.ToString(),PortableSessionId=RequireId(portableSessionId),
                DeviceLocalSessionId=RequireId(deviceLocalSessionId),Title=title};

        public static string Fingerprint(JToken document)
            =>Sha256Hex(new UTF8Encoding(false).GetBytes(CanonicalJson(document)));

        public static string Sha256Hex(byte[] bytes)
        {using(SHA256 sha=SHA256.Create()){byte[] hash=sha.ComputeHash(bytes);var builder=new StringBuilder(64);for(int i=0;i<hash.Length;i++)builder.Append(hash[i].ToString("x2",CultureInfo.InvariantCulture));return builder.ToString();}}
        public static string CanonicalJson(JToken token)
        {
            if(token is JObject value)
            {
                var properties=new List<JProperty>(value.Properties());
                properties.Sort((left,right)=>string.CompareOrdinal(left.Name,right.Name));
                var builder=new StringBuilder("{");
                for(int i=0;i<properties.Count;i++){if(i>0)builder.Append(',');builder.Append(JsonConvert.ToString(properties[i].Name));builder.Append(':');builder.Append(CanonicalJson(properties[i].Value));}
                return builder.Append('}').ToString();
            }
            if(token is JArray array)
            {var builder=new StringBuilder("[");for(int i=0;i<array.Count;i++){if(i>0)builder.Append(',');builder.Append(CanonicalJson(array[i]));}return builder.Append(']').ToString();}
            return token.ToString(Formatting.None);
        }
        private static string RequireId(string value){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("Stable identifier is required.");return value;}
        private static string RequireUuid(string value){if(!Guid.TryParse(value,out _))throw new ArgumentException("Stable UUID is required.");return value;}
    }
}

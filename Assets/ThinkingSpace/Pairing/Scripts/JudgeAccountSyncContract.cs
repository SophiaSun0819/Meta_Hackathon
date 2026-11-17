using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ThinkingSpace.Pairing
{
    public static class JudgeAccountSyncContract
    {
        public const string Identifier = "thinking-space-account-sync";
        public const string PinnedDocumentSha256 = "1342b9079b41e834d30439f7f9724f0f75aeb0257f49843e7965a1b66a44267b";
        public const int PairingVersion = 1;
        public const int SyncVersion = 2;
        public const int PairingTtlSeconds = 300;
        public const int PollAfterMilliseconds = 1000;
        public const int MaximumRequestBytes = 1024 * 1024;
        public const int MaximumPushMutations = 50;
        public const int MaximumPullEvents = 200;
        public const string PortableFormatIdentifier = "thinking-space-session";
        public const int PortableFormatVersion = 1;
        public const string PairingBasePath = "/api/device/v1/pairing/";
        public const string SyncBasePath = "/api/device/v2/sync/";
    }

    public enum PairingChallengeStatus { PENDING, CLAIMED, DEVICE_CONFIRMED, ACTIVE, CANCELLED, EXPIRED }
    public enum SessionMutationKind { UPSERT, RENAME, DELETE_EVERYWHERE, RESTORE }
    public enum SessionMutationResultStatus { APPLIED, CONFLICT, REJECTED }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PairingChallengeStartV1
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 1;
        [JsonProperty("deviceLocalId", Required = Required.Always)] public string DeviceLocalId = string.Empty;
        [JsonProperty("devicePublicKey", Required = Required.Always)] public string DevicePublicKey = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PairingChallengeStartResultV1
    {
        [JsonProperty("challengeId", Required = Required.Always)] public string ChallengeId = string.Empty;
        [JsonProperty("code", Required = Required.Always)] public string Code = string.Empty;
        [JsonProperty("pollingSecret", Required = Required.Always)] public string PollingSecret = string.Empty;
        [JsonProperty("expiresAt", Required = Required.Always)] public string ExpiresAt = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PairingStatusRequestV1
    {
        [JsonProperty("challengeId", Required = Required.Always)] public string ChallengeId = string.Empty;
        [JsonProperty("pollingSecret", Required = Required.Always)] public string PollingSecret = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PairingStatusResultV1
    {
        [JsonProperty("challengeId", Required = Required.Always)] public string ChallengeId = string.Empty;
        [JsonProperty("status", Required = Required.Always)] public string Status = "PENDING";
        [JsonProperty("expiresAt", Required = Required.Always)] public string ExpiresAt = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DeviceConfirmationV1
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 1;
        [JsonProperty("challengeId", Required = Required.Always)] public string ChallengeId = string.Empty;
        [JsonProperty("pollingSecret", Required = Required.Always)] public string PollingSecret = string.Empty;
        [JsonProperty("confirmed", Required = Required.Always)] public bool Confirmed;
        [JsonProperty("deviceProof", Required = Required.Always)] public string DeviceProof = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DeviceActivationResultV1
    {
        [JsonProperty("deviceId", Required = Required.Always)] public string DeviceId = string.Empty;
        [JsonProperty("accountId", Required = Required.Always)] public string AccountId = string.Empty;
        [JsonProperty("scope", Required = Required.Always)] public string Scope = string.Empty;
        [JsonProperty("credentialVersion", Required = Required.Always)] public int CredentialVersion;
        [JsonProperty("token", Required = Required.Always)] public string Token = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DeviceSignedRequestV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("deviceId", Required = Required.Always)] public string DeviceId = string.Empty;
        [JsonProperty("credentialVersion", Required = Required.Always)] public int CredentialVersion;
        [JsonProperty("requestId", Required = Required.Always)] public string RequestId = string.Empty;
        [JsonProperty("issuedAt", Required = Required.Always)] public string IssuedAt = string.Empty;
        [JsonProperty("expiresAt", Required = Required.Always)] public string ExpiresAt = string.Empty;
        [JsonProperty("method", Required = Required.Always)] public string Method = "POST";
        [JsonProperty("path", Required = Required.Always)] public string Path = string.Empty;
        [JsonProperty("bodyHash", Required = Required.Always)] public string BodyHash = string.Empty;
        [JsonProperty("deviceProof", Required = Required.Always)] public string DeviceProof = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionMutationResultV2
    {
        [JsonProperty("operationId", Required = Required.Always)] public string OperationId = string.Empty;
        [JsonProperty("status", Required = Required.Always)] public string Status = "REJECTED";
        [JsonProperty("cloudSessionId", Required = Required.Always)] public string CloudSessionId = string.Empty;
        [JsonProperty("revision", NullValueHandling = NullValueHandling.Ignore)] public int? Revision;
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionSyncPushResultV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("results", Required = Required.Always)] public List<SessionMutationResultV2> Results = new List<SessionMutationResultV2>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PortableSessionSnapshotV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("cloudSessionId", Required = Required.Always)] public string CloudSessionId = string.Empty;
        [JsonProperty("portableSessionId", Required = Required.Always)] public string PortableSessionId = string.Empty;
        [JsonProperty("deviceLocalSessionId", Required = Required.Always)] public string DeviceLocalSessionId = string.Empty;
        [JsonProperty("revision", Required = Required.Always)] public int Revision;
        [JsonProperty("title", Required = Required.Always)] public string Title = string.Empty;
        [JsonProperty("updatedAt", Required = Required.Always)] public string UpdatedAt = string.Empty;
        [JsonProperty("formatIdentifier", Required = Required.Always)] public string FormatIdentifier = JudgeAccountSyncContract.PortableFormatIdentifier;
        [JsonProperty("formatVersion", Required = Required.Always)] public int FormatVersion = 1;
        [JsonProperty("canonicalDocument", Required = Required.Always)] public JObject CanonicalDocument = new JObject();
        [JsonProperty("canonicalFingerprint", Required = Required.Always)] public string CanonicalFingerprint = string.Empty;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionSyncMutationV2
    {
        [JsonProperty("operationId", Required = Required.Always)] public string OperationId = string.Empty;
        [JsonProperty("cloudSessionId", Required = Required.Always)] public string CloudSessionId = string.Empty;
        [JsonProperty("expectedRevision", Required = Required.Always)] public int ExpectedRevision;
        [JsonProperty("kind", Required = Required.Always)] public string Kind = "UPSERT";
        [JsonProperty("portableSessionId", Required = Required.Always)] public string PortableSessionId = string.Empty;
        [JsonProperty("deviceLocalSessionId", Required = Required.Always)] public string DeviceLocalSessionId = string.Empty;
        [JsonProperty("snapshot", NullValueHandling = NullValueHandling.Ignore)] public PortableSessionSnapshotV2 Snapshot;
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)] public string Title;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionTombstoneV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("cloudSessionId", Required = Required.Always)] public string CloudSessionId = string.Empty;
        [JsonProperty("portableSessionId", Required = Required.Always)] public string PortableSessionId = string.Empty;
        [JsonProperty("deletedRevision", Required = Required.Always)] public int DeletedRevision;
        [JsonProperty("deletedAt", Required = Required.Always)] public string DeletedAt = string.Empty;
        [JsonProperty("recoverUntil", Required = Required.Always)] public string RecoverUntil = string.Empty;
        [JsonProperty("purgedAt", NullValueHandling = NullValueHandling.Ignore)] public string PurgedAt;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionSyncPushV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("deviceId", Required = Required.Always)] public string DeviceId = string.Empty;
        [JsonProperty("mutations", Required = Required.Always)] public List<SessionSyncMutationV2> Mutations = new List<SessionSyncMutationV2>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionSyncPullV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("deviceId", Required = Required.Always)] public string DeviceId = string.Empty;
        [JsonProperty("afterCursor", Required = Required.Always)] public long AfterCursor;
        [JsonProperty("limit", Required = Required.Always)] public int Limit = 100;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SessionSyncAckV2
    {
        [JsonProperty("contractVersion", Required = Required.Always)] public int ContractVersion = 2;
        [JsonProperty("deviceId", Required = Required.Always)] public string DeviceId = string.Empty;
        [JsonProperty("ackCursor", Required = Required.Always)] public long AckCursor;
    }
}

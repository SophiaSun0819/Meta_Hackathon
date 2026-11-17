using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ThinkingSpace.Pairing
{
    public readonly struct Backend034HttpRequest
    {
        public readonly string Method, Path, Body;
        public Backend034HttpRequest(string method, string path, string body = null)
        { Method = method; Path = path; Body = body; }
    }

    public readonly struct Backend034HttpResponse
    {
        public readonly long StatusCode;
        public readonly string Body;
        public readonly int RetryAfterMilliseconds;
        public Backend034HttpResponse(long statusCode, string body, int retryAfterMilliseconds = 0)
        { StatusCode = statusCode; Body = body ?? string.Empty; RetryAfterMilliseconds = retryAfterMilliseconds; }
    }

    public interface IBackend034HttpExecutor
    {
        void Send(Backend034HttpRequest request, Action<Backend034HttpResponse> completed);
    }

    public interface IBackend034PairingSigner
    {
        void EnsureKey(string alias);
        string GetPublicJwk(string alias);
        string GetPublicKeyThumbprint(string alias);
        string SignCanonicalPayload(string alias, byte[] canonicalPayload);
    }

    public sealed class Backend034PairingTransport : IQuestPairingTransport
    {
        public const string ContractSha256 = "40df9d6581615a478af581352ca8dcf3c5cce23119fb33eb041279552b8d91ea";
        private const string CapabilitiesPath = "/api/device/capabilities";
        private const string PairingPath = "/api/device/v1/pairing/";
        private static readonly HashSet<string> TerminalCredentialErrors = new HashSet<string>(StringComparer.Ordinal)
            { "DEVICE_REVOKED", "DEVICE_UNAUTHORIZED" };

        private readonly IBackend034HttpExecutor http;
        private readonly IQuestCredentialVault vault;
        private readonly IBackend034PairingSigner signingKeys;
        private readonly string signingAlias, deviceLocalId;
        private readonly bool liveEnabled;
        private CapabilityManifest capabilities;
        private StartResult active;

        public Backend034PairingTransport(IBackend034HttpExecutor executor, IQuestCredentialVault credentialVault,
            IBackend034PairingSigner keyStore, string keyAlias, string localDeviceId, bool enableLiveTransport = false)
        {
            http = executor ?? throw new ArgumentNullException(nameof(executor));
            vault = credentialVault ?? throw new ArgumentNullException(nameof(credentialVault));
            signingKeys = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
            signingAlias = keyAlias ?? throw new ArgumentNullException(nameof(keyAlias));
            deviceLocalId = localDeviceId ?? throw new ArgumentNullException(nameof(localDeviceId));
            liveEnabled = enableLiveTransport;
        }

        // Availability means the operator explicitly enabled the live transport. The
        // capability manifest is fetched and pinned before the first pairing request.
        public bool IsAvailable => liveEnabled;
        public bool HasStoredCredential => vault.HasCredential;
        public string UnavailableMessageKey => "pairing.unavailable.body";

        public void DeleteStoredCredential()
        {
            if (vault.HasCredential) vault.Delete();
        }

        public void RequestPairingCode(Action<PairingCodeResult> completed)
        {
            if (!liveEnabled) { completed?.Invoke(PairingCodeResult.Failed(UnavailableMessageKey)); return; }
            http.Send(new Backend034HttpRequest("GET", CapabilitiesPath), response =>
            {
                if (!TryCapabilities(response, out capabilities))
                { completed?.Invoke(PairingCodeResult.Failed(UnavailableMessageKey)); return; }
                signingKeys.EnsureKey(signingAlias);
                string body = StrictJson(new JObject
                {
                    ["contractVersion"] = 1, ["deviceLocalId"] = deviceLocalId,
                    ["devicePublicKey"] = signingKeys.GetPublicJwk(signingAlias)
                });
                http.Send(new Backend034HttpRequest("POST", PairingPath + "start", body), startResponse =>
                {
                    if (!Success(startResponse) || !TryParseStart(startResponse.Body, out active))
                    { completed?.Invoke(PairingCodeResult.Failed("pairing.failed.body")); return; }
                    completed?.Invoke(PairingCodeResult.Ready(active.challengeId, active.code,
                        DateTime.Parse(active.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
                });
            });
        }

        public void CancelPairing(string requestId)
        {
            if (!IsAvailable || active == null || !string.Equals(active.challengeId, requestId, StringComparison.Ordinal)) return;
            JObject body = ChallengeBody(active, true);
            http.Send(new Backend034HttpRequest("POST", PairingPath + "cancel", StrictJson(body)), _ => { });
            active = null;
        }

        public void PollStatus(Action<string, int, string> completed)
        {
            if (!IsAvailable || active == null) { completed?.Invoke("OFFLINE", 0, "CONTRACT_UNSUPPORTED"); return; }
            http.Send(new Backend034HttpRequest("POST", PairingPath + "status", StrictJson(ChallengeBody(active, false))), response =>
            {
                if (!Success(response)) { completed?.Invoke("FAILED", Backoff(0, response.RetryAfterMilliseconds), StableError(response)); return; }
                if (!TryParseStatus(response.Body, out StatusResult result))
                { completed?.Invoke("FAILED", 0, "CONTRACT_UNSUPPORTED"); return; }
                completed?.Invoke(result.state, Math.Max(result.pollAfterMs, response.RetryAfterMilliseconds), result.errorCode);
            });
        }

        public void Confirm(PairingDeviceProofPayloadV1 proof, bool confirmed, Action<string> completed)
        {
            if (!IsAvailable || active == null) { completed?.Invoke("CONTRACT_UNSUPPORTED"); return; }
            byte[] canonical = PairingProofCryptography.CanonicalDeviceProof(proof);
            string signature = confirmed ? signingKeys.SignCanonicalPayload(signingAlias, canonical) : string.Empty;
            JObject body = ChallengeBody(active, true);
            body["deviceProof"] = signature; body["confirmed"] = confirmed;
            http.Send(new Backend034HttpRequest("POST", PairingPath + "confirm", StrictJson(body)), response =>
                completed?.Invoke(Success(response) ? null : StableError(response)));
        }

        public void RequestConfirmationContext(Action<ConfirmationContext, string> completed)
        {
            if (!IsAvailable || active == null) { completed?.Invoke(null, "CONTRACT_UNSUPPORTED"); return; }
            string challengeBody = StrictJson(ChallengeBody(active, false));
            http.Send(new Backend034HttpRequest("POST", PairingPath + "display", challengeBody), displayResponse =>
            {
                if (!Success(displayResponse) || !TryStrict(displayResponse.Body,
                    new[] { "contractVersion", "challengeId", "label", "confirmationNonce" }, out DisplayResult display)
                    || display.contractVersion != 1 || display.challengeId != active.challengeId)
                { completed?.Invoke(null, StableError(displayResponse)); return; }
                http.Send(new Backend034HttpRequest("POST", PairingPath + "proof-payload", challengeBody), proofResponse =>
                {
                    if (!Success(proofResponse) || !TryStrict(proofResponse.Body,
                        new[] { "contractIdentifier", "contractVersion", "purpose", "challengeId", "publicKeyThumbprint", "confirmationNonce", "accountId", "expiresAt" },
                        out ProofResult proof) || proof.contractIdentifier != JudgeAccountSyncContract.Identifier
                        || proof.contractVersion != 1 || proof.purpose != "PAIRING_DEVICE_CONFIRMATION"
                        || proof.challengeId != active.challengeId || proof.confirmationNonce != display.confirmationNonce
                        || proof.publicKeyThumbprint != signingKeys.GetPublicKeyThumbprint(signingAlias))
                    { completed?.Invoke(null, StableError(proofResponse)); return; }
                    var payload = new PairingDeviceProofPayloadV1
                    {
                        ChallengeId = proof.challengeId, PublicKeyThumbprint = proof.publicKeyThumbprint,
                        ConfirmationNonce = proof.confirmationNonce, AccountId = proof.accountId, ExpiresAt = proof.expiresAt
                    };
                    try { PairingProofCryptography.CanonicalDeviceProof(payload); }
                    catch (ArgumentException) { completed?.Invoke(null, "DEVICE_PROOF_INVALID"); return; }
                    completed?.Invoke(new ConfirmationContext(display.label, payload), null);
                });
            });
        }

        public void Activate(Action<string> completed)
        {
            if (!IsAvailable || active == null) { completed?.Invoke("CONTRACT_UNSUPPORTED"); return; }
            http.Send(new Backend034HttpRequest("POST", PairingPath + "activate", StrictJson(ChallengeBody(active, false))), response =>
            {
                if (!Success(response) || !TryParseCredential(response.Body, out DeviceCredential credential))
                { completed?.Invoke(StableError(response) ?? "CONTRACT_UNSUPPORTED"); return; }
                vault.Store(credential);
                active = null;
                completed?.Invoke(null);
            });
        }

        public void RotateCredential(Action<string> completed)
        {
            if (!IsAvailable || !vault.TryLoad(out DeviceCredential current))
            { completed?.Invoke("DEVICE_UNAUTHORIZED"); return; }
            JObject begin = new JObject
            {
                ["contractVersion"] = 1, ["deviceId"] = current.DeviceId,
                ["credentialVersion"] = current.Version, ["currentRefreshToken"] = current.Token
            };
            http.Send(new Backend034HttpRequest("POST", PairingPath + "refresh-start", StrictJson(begin)), response =>
            {
                if (!Success(response) || !TryParseRefreshChallenge(response.Body, out RefreshChallenge challenge))
                { FinishCredentialError(StableError(response), completed); return; }
                string idempotencyKey = Guid.NewGuid().ToString("D");
                string nextToken = CreateToken();
                var proof = new PairingRefreshProofPayloadV1
                {
                    RefreshChallengeId = challenge.refreshChallengeId, DeviceId = current.DeviceId,
                    PublicKeyThumbprint = signingKeys.GetPublicKeyThumbprint(signingAlias), CredentialVersion = current.Version,
                    Nonce = challenge.nonce, IdempotencyKey = idempotencyKey, ExpiresAt = challenge.expiresAt
                };
                string signature = signingKeys.SignCanonicalPayload(signingAlias,
                    PairingProofCryptography.CanonicalRefreshProof(proof));
                JObject rotate = new JObject
                {
                    ["contractVersion"] = 1, ["refreshChallengeId"] = challenge.refreshChallengeId,
                    ["deviceId"] = current.DeviceId, ["credentialVersion"] = current.Version,
                    ["nonce"] = challenge.nonce, ["idempotencyKey"] = idempotencyKey,
                    ["currentRefreshToken"] = current.Token, ["nextRefreshToken"] = nextToken,
                    ["deviceProof"] = signature
                };
                http.Send(new Backend034HttpRequest("POST", PairingPath + "refresh", StrictJson(rotate)), rotated =>
                {
                    if (!Success(rotated) || !TryParseCredential(rotated.Body, out DeviceCredential replacement)
                        || replacement.Token != nextToken || replacement.Version <= current.Version)
                    { FinishCredentialError(StableError(rotated), completed); return; }
                    vault.Store(replacement);
                    completed?.Invoke(null);
                });
            });
        }

        public static bool TryCapabilities(Backend034HttpResponse response, out CapabilityManifest manifest)
        {
            manifest = null;
            if (!Success(response)) return false;
            JObject value;
            try { value = JObject.Parse(response.Body); } catch (JsonException) { return false; }
            string[] keys = { "contractIdentifier", "pairingContractVersion", "syncContractVersion", "portableSessionFormat",
                "pairingTtlSeconds", "pollAfterMs", "proof", "syncMutations", "maxPushMutations", "maxPullEvents", "maxRequestBytes", "enabled" };
            if (!Exact(value, keys)) return false;
            JObject portable = value["portableSessionFormat"] as JObject;
            JArray mutations = value["syncMutations"] as JArray;
            if (portable == null || !Exact(portable, "identifier", "version") || mutations == null || mutations.Count != 1
                || (string)mutations[0] != "UPSERT" || (string)value["contractIdentifier"] != JudgeAccountSyncContract.Identifier
                || (int?)value["pairingContractVersion"] != 1 || (int?)value["syncContractVersion"] != 2
                || (string)portable["identifier"] != JudgeAccountSyncContract.PortableFormatIdentifier || (int?)portable["version"] != 1
                || (string)value["proof"] != "P-256/SHA-256/P1363" || (bool?)value["enabled"] != true) return false;
            int[] positive = { (int?)value["pairingTtlSeconds"] ?? 0, (int?)value["pollAfterMs"] ?? 0,
                (int?)value["maxPushMutations"] ?? 0, (int?)value["maxPullEvents"] ?? 0, (int?)value["maxRequestBytes"] ?? 0 };
            foreach (int number in positive) if (number <= 0) return false;
            manifest = value.ToObject<CapabilityManifest>();
            return true;
        }

        public static int Backoff(int attempt, int retryAfterMs)
        {
            int exponential = Math.Min(30000, 1000 * (1 << Math.Min(Math.Max(attempt, 0), 5)));
            return Math.Max(retryAfterMs, exponential + 137 * (attempt + 1));
        }

        private static JObject ChallengeBody(StartResult value, bool version)
        {
            var body = new JObject { ["challengeId"] = value.challengeId, ["pollingSecret"] = value.pollingSecret };
            if (version) body.AddFirst(new JProperty("contractVersion", 1));
            return body;
        }

        private void FinishCredentialError(string error, Action<string> completed)
        {
            string stable = string.IsNullOrEmpty(error) ? "CONTRACT_UNSUPPORTED" : error;
            if (TerminalCredentialErrors.Contains(stable)) vault.Delete();
            completed?.Invoke(stable);
        }

        private static string StableError(Backend034HttpResponse response)
        {
            try
            {
                JObject value = JObject.Parse(response.Body);
                if (!Exact(value, "error")) return "CONTRACT_UNSUPPORTED";
                string error = (string)value["error"];
                return !string.IsNullOrEmpty(error) && System.Text.RegularExpressions.Regex.IsMatch(error, "^[A-Z][A-Z0-9_]*$")
                    ? error : "CONTRACT_UNSUPPORTED";
            }
            catch (JsonException) { return "CONTRACT_UNSUPPORTED"; }
        }

        private static bool TryParseStart(string json, out StartResult value)
            => TryStrict(json, new[] { "challengeId", "code", "pollingSecret", "expiresAt" }, out value)
               && Guid.TryParse(value.challengeId, out _) && value.code?.Length == 6
               && DateTime.TryParse(value.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
        private static bool TryParseStatus(string json, out StatusResult value)
            => TryStrict(json, new[] { "contractVersion", "challengeId", "state", "expiresAt", "serverTime", "pollAfterMs" },
                new[] { "retryAfterMs", "errorCode" }, out value) && value.contractVersion == 1 && value.pollAfterMs > 0;
        private static bool TryParseCredential(string json, out DeviceCredential credential)
        {
            credential = default;
            if (!TryStrict(json, new[] { "deviceId", "accountId", "scope", "credentialVersion", "token" }, out CredentialResult value)
                || value.scope != "quest-session-sync" || !Guid.TryParse(value.deviceId, out _) || !Guid.TryParse(value.accountId, out _)
                || value.credentialVersion < 1 || string.IsNullOrEmpty(value.token)) return false;
            credential = new DeviceCredential(value.deviceId, value.accountId, value.credentialVersion, value.token); return true;
        }
        private static bool TryParseRefreshChallenge(string json, out RefreshChallenge value)
            => TryStrict(json, new[] { "contractVersion", "refreshChallengeId", "deviceId", "credentialVersion", "nonce", "expiresAt" }, out value)
               && value.contractVersion == 1 && value.credentialVersion > 0 && Guid.TryParse(value.refreshChallengeId, out _)
               && Guid.TryParse(value.deviceId, out _) && !string.IsNullOrEmpty(value.nonce)
               && DateTime.TryParse(value.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
        private static string CreateToken()
        {
            byte[] bytes = new byte[32]; using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return PairingProofCryptography.Base64UrlEncode(bytes);
        }
        private static bool TryStrict<T>(string json, string[] required, out T value) where T : class
            => TryStrict(json, required, Array.Empty<string>(), out value);
        private static bool TryStrict<T>(string json, string[] required, string[] optional, out T value) where T : class
        {
            value = null; JObject parsed;
            try { parsed = JObject.Parse(json); } catch (JsonException) { return false; }
            var allowed = new HashSet<string>(required, StringComparer.Ordinal); foreach (string key in optional) allowed.Add(key);
            foreach (JProperty property in parsed.Properties()) if (!allowed.Remove(property.Name)) return false;
            foreach (string key in required) if (parsed[key] == null) return false;
            try { value = parsed.ToObject<T>(); return value != null; } catch (JsonException) { return false; }
        }
        private static bool Exact(JObject value, params string[] keys)
        {
            var expected = new HashSet<string>(keys, StringComparer.Ordinal);
            foreach (JProperty property in value.Properties()) if (!expected.Remove(property.Name)) return false;
            return expected.Count == 0;
        }
        private static bool Success(Backend034HttpResponse response) => response.StatusCode >= 200 && response.StatusCode < 300;
        private static string StrictJson(JObject value) => value.ToString(Formatting.None);

        public sealed class CapabilityManifest
        {
            public int pairingTtlSeconds, pollAfterMs, maxPushMutations, maxPullEvents, maxRequestBytes;
        }
        public sealed class ConfirmationContext
        {
            public string SafeAccountLabel { get; }
            public PairingDeviceProofPayloadV1 ProofPayload { get; }
            public ConfirmationContext(string label, PairingDeviceProofPayloadV1 proof)
            { SafeAccountLabel = label ?? string.Empty; ProofPayload = proof; }
        }
        private sealed class StartResult { public string challengeId, code, pollingSecret, expiresAt; }
        private sealed class StatusResult { public int contractVersion, pollAfterMs, retryAfterMs; public string challengeId, state, expiresAt, serverTime, errorCode; }
        private sealed class CredentialResult { public string deviceId, accountId, scope, token; public int credentialVersion; }
        private sealed class RefreshChallenge { public int contractVersion, credentialVersion; public string refreshChallengeId, deviceId, nonce, expiresAt; }
        private sealed class DisplayResult { public int contractVersion; public string challengeId, label, confirmationNonce; }
        private sealed class ProofResult { public int contractVersion; public string contractIdentifier, purpose, challengeId, publicKeyThumbprint, confirmationNonce, accountId, expiresAt; }
    }
}

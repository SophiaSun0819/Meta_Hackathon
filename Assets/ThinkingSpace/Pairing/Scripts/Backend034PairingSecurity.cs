using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ThinkingSpace.Pairing
{
    public sealed class AccountSyncCapabilityManifest
    {
        public string ContractIdentifier { get; internal set; }
        public int PairingContractVersion { get; internal set; }
        public int SyncContractVersion { get; internal set; }
        public int PairingTtlSeconds { get; internal set; }
        public int PollAfterMs { get; internal set; }
        public int MaxPushMutations { get; internal set; }
        public int MaxPullEvents { get; internal set; }
        public int MaxRequestBytes { get; internal set; }
    }

    public static class Backend034CapabilityParser
    {
        public const string ContractDocumentSha256 =
            "40df9d6581615a478af581352ca8dcf3c5cce23119fb33eb041279552b8d91ea";

        private static readonly HashSet<string> RootFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "contractIdentifier", "pairingContractVersion", "syncContractVersion",
            "portableSessionFormat", "pairingTtlSeconds", "pollAfterMs", "proof",
            "syncMutations", "maxPushMutations", "maxPullEvents", "maxRequestBytes", "enabled"
        };

        public static bool TryParseCompatible(string json, out AccountSyncCapabilityManifest manifest)
        {
            manifest = null;
            JObject root;
            try { root = JObject.Parse(json ?? string.Empty); }
            catch (JsonException) { return false; }

            if (!HasExactFields(root, RootFields)
                || !ExactString(root, "contractIdentifier", JudgeAccountSyncContract.Identifier)
                || !ExactInteger(root, "pairingContractVersion", 1)
                || !ExactInteger(root, "syncContractVersion", 2)
                || !ExactInteger(root, "pairingTtlSeconds", 300)
                || !ExactInteger(root, "pollAfterMs", 1000)
                || !ExactString(root, "proof", "P-256/SHA-256/P1363")
                || !ExactInteger(root, "maxPushMutations", 50)
                || !ExactInteger(root, "maxPullEvents", 200)
                || !ExactInteger(root, "maxRequestBytes", 1048576)
                || root["enabled"]?.Type != JTokenType.Boolean || !(bool)root["enabled"])
                return false;

            if (!(root["portableSessionFormat"] is JObject portable)
                || !HasExactFields(portable, new HashSet<string>(new[] { "identifier", "version" }, StringComparer.Ordinal))
                || !ExactString(portable, "identifier", JudgeAccountSyncContract.PortableFormatIdentifier)
                || !ExactInteger(portable, "version", 1))
                return false;

            if (!(root["syncMutations"] is JArray mutations) || mutations.Count != 1
                || mutations[0].Type != JTokenType.String || (string)mutations[0] != "UPSERT")
                return false;

            manifest = new AccountSyncCapabilityManifest
            {
                ContractIdentifier = JudgeAccountSyncContract.Identifier,
                PairingContractVersion = 1,
                SyncContractVersion = 2,
                PairingTtlSeconds = 300,
                PollAfterMs = 1000,
                MaxPushMutations = 50,
                MaxPullEvents = 200,
                MaxRequestBytes = 1048576
            };
            return true;
        }

        private static bool HasExactFields(JObject value, HashSet<string> expected)
        {
            int count = 0;
            foreach (JProperty property in value.Properties())
            {
                count++;
                if (!expected.Contains(property.Name)) return false;
            }
            return count == expected.Count;
        }

        private static bool ExactString(JObject value, string name, string expected)
            => value[name]?.Type == JTokenType.String && (string)value[name] == expected;

        private static bool ExactInteger(JObject value, string name, int expected)
            => value[name]?.Type == JTokenType.Integer && (long)value[name] == expected;
    }

    public sealed class PairingDeviceProofPayloadV1
    {
        public string ContractIdentifier = JudgeAccountSyncContract.Identifier;
        public int ContractVersion = 1;
        public string Purpose = "PAIRING_DEVICE_CONFIRMATION";
        public string ChallengeId = string.Empty;
        public string PublicKeyThumbprint = string.Empty;
        public string ConfirmationNonce = string.Empty;
        public string AccountId = string.Empty;
        public string ExpiresAt = string.Empty;
    }

    public sealed class PairingRefreshProofPayloadV1
    {
        public string ContractIdentifier = JudgeAccountSyncContract.Identifier;
        public int ContractVersion = 1;
        public string Purpose = "DEVICE_CREDENTIAL_REFRESH";
        public string RefreshChallengeId = string.Empty;
        public string DeviceId = string.Empty;
        public string PublicKeyThumbprint = string.Empty;
        public int CredentialVersion;
        public string Nonce = string.Empty;
        public string IdempotencyKey = string.Empty;
        public string ExpiresAt = string.Empty;
    }

    public static class PairingProofCryptography
    {
        private static readonly byte[] P256Order =
        {
            0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00,
            0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
            0xbc, 0xe6, 0xfa, 0xad, 0xa7, 0x17, 0x9e, 0x84,
            0xf3, 0xb9, 0xca, 0xc2, 0xfc, 0x63, 0x25, 0x51
        };
        private static readonly Regex Uuid = new Regex(
            "^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Base64Url = new Regex("^[A-Za-z0-9_-]+$",
            RegexOptions.CultureInvariant);

        public static byte[] CanonicalDeviceProof(PairingDeviceProofPayloadV1 value)
        {
            if (value == null || value.ContractIdentifier != JudgeAccountSyncContract.Identifier
                || value.ContractVersion != 1 || value.Purpose != "PAIRING_DEVICE_CONFIRMATION"
                || !Uuid.IsMatch(value.ChallengeId ?? string.Empty)
                || !Uuid.IsMatch(value.AccountId ?? string.Empty)
                || !Base64Url.IsMatch(value.PublicKeyThumbprint ?? string.Empty)
                || !Base64Url.IsMatch(value.ConfirmationNonce ?? string.Empty)
                || !DateTimeOffset.TryParse(value.ExpiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
                throw new ArgumentException("DEVICE_PROOF_CONTEXT_INVALID");

            string json = "{\"contractIdentifier\":" + JsonConvert.ToString(value.ContractIdentifier)
                + ",\"contractVersion\":1,\"purpose\":" + JsonConvert.ToString(value.Purpose)
                + ",\"challengeId\":" + JsonConvert.ToString(value.ChallengeId)
                + ",\"publicKeyThumbprint\":" + JsonConvert.ToString(value.PublicKeyThumbprint)
                + ",\"confirmationNonce\":" + JsonConvert.ToString(value.ConfirmationNonce)
                + ",\"accountId\":" + JsonConvert.ToString(value.AccountId)
                + ",\"expiresAt\":" + JsonConvert.ToString(value.ExpiresAt) + "}";
            return new UTF8Encoding(false, true).GetBytes(json);
        }

        public static byte[] CanonicalRefreshProof(PairingRefreshProofPayloadV1 value)
        {
            if (value == null || value.ContractIdentifier != JudgeAccountSyncContract.Identifier
                || value.ContractVersion != 1 || value.Purpose != "DEVICE_CREDENTIAL_REFRESH"
                || !Guid.TryParse(value.RefreshChallengeId, out _) || !Guid.TryParse(value.DeviceId, out _)
                || !Guid.TryParse(value.IdempotencyKey, out _) || value.CredentialVersion < 1
                || !Base64Url.IsMatch(value.PublicKeyThumbprint ?? string.Empty)
                || !Base64Url.IsMatch(value.Nonce ?? string.Empty)
                || !DateTimeOffset.TryParse(value.ExpiresAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out _))
                throw new ArgumentException("DEVICE_PROOF_CONTEXT_INVALID");
            string json = "{\"contractIdentifier\":" + JsonConvert.ToString(value.ContractIdentifier)
                + ",\"contractVersion\":1,\"purpose\":\"DEVICE_CREDENTIAL_REFRESH\",\"refreshChallengeId\":"
                + JsonConvert.ToString(value.RefreshChallengeId) + ",\"deviceId\":" + JsonConvert.ToString(value.DeviceId)
                + ",\"publicKeyThumbprint\":" + JsonConvert.ToString(value.PublicKeyThumbprint)
                + ",\"credentialVersion\":" + value.CredentialVersion.ToString(CultureInfo.InvariantCulture)
                + ",\"nonce\":" + JsonConvert.ToString(value.Nonce) + ",\"idempotencyKey\":"
                + JsonConvert.ToString(value.IdempotencyKey) + ",\"expiresAt\":" + JsonConvert.ToString(value.ExpiresAt) + "}";
            return new UTF8Encoding(false, true).GetBytes(json);
        }

        public static string PublicKeyThumbprint(string publicJwkJson)
        {
            JObject value;
            try { value = JObject.Parse(publicJwkJson ?? string.Empty); }
            catch (JsonException exception) { throw new ArgumentException("DEVICE_KEY_INVALID", exception); }
            string x = RequireCoordinate(value, "x");
            string y = RequireCoordinate(value, "y");
            if ((string)value["kty"] != "EC" || (string)value["crv"] != "P-256")
                throw new ArgumentException("DEVICE_KEY_INVALID");
            string canonical = "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":"
                + JsonConvert.ToString(x) + ",\"y\":" + JsonConvert.ToString(y) + "}";
            using (SHA256 sha = SHA256.Create())
                return Base64UrlEncode(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }

        public static byte[] DerToP1363(byte[] signature)
        {
            if (signature == null || signature.Length < 8 || signature.Length > 72) InvalidProof();
            int offset = 0;
            if (signature[offset++] != 0x30) InvalidProof();
            int sequenceLength = signature[offset++];
            if (sequenceLength >= 0x80 || sequenceLength != signature.Length - 2) InvalidProof();
            byte[] result = new byte[64];
            ReadDerScalar(signature, ref offset, result, 0);
            ReadDerScalar(signature, ref offset, result, 32);
            if (offset != signature.Length) InvalidProof();
            return result;
        }

        public static byte[] P1363ToDer(byte[] signature)
        {
            if (signature == null || signature.Length != 64) InvalidProof();
            byte[] r = EncodeDerScalar(signature, 0);
            byte[] s = EncodeDerScalar(signature, 32);
            byte[] result = new byte[2 + r.Length + s.Length];
            result[0] = 0x30;
            result[1] = checked((byte)(r.Length + s.Length));
            Buffer.BlockCopy(r, 0, result, 2, r.Length);
            Buffer.BlockCopy(s, 0, result, 2 + r.Length, s.Length);
            return result;
        }

        public static string Base64UrlEncode(byte[] bytes)
            => Convert.ToBase64String(bytes ?? throw new ArgumentNullException(nameof(bytes)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Base64UrlDecode(string value)
        {
            if (string.IsNullOrEmpty(value) || !Base64Url.IsMatch(value)) InvalidProof();
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            try { return Convert.FromBase64String(padded); }
            catch (FormatException) { InvalidProof(); return null; }
        }

        private static string RequireCoordinate(JObject value, string name)
        {
            if (value[name]?.Type != JTokenType.String) throw new ArgumentException("DEVICE_KEY_INVALID");
            string encoded = (string)value[name];
            byte[] bytes;
            try { bytes = Base64UrlDecode(encoded); }
            catch (ArgumentException exception) { throw new ArgumentException("DEVICE_KEY_INVALID", exception); }
            if (bytes.Length != 32) throw new ArgumentException("DEVICE_KEY_INVALID");
            return encoded;
        }

        private static void ReadDerScalar(byte[] source, ref int offset, byte[] destination, int destinationOffset)
        {
            if (offset + 2 > source.Length || source[offset++] != 0x02) InvalidProof();
            int length = source[offset++];
            if (length < 1 || length > 33 || offset + length > source.Length) InvalidProof();
            int start = offset;
            offset += length;
            if ((source[start] & 0x80) != 0) InvalidProof();
            if (length > 1 && source[start] == 0)
            {
                if ((source[start + 1] & 0x80) == 0) InvalidProof();
                start++;
                length--;
            }
            if (length > 32 || IsZero(source, start, length)) InvalidProof();
            Buffer.BlockCopy(source, start, destination, destinationOffset + 32 - length, length);
            ValidateScalar(destination, destinationOffset);
        }

        private static byte[] EncodeDerScalar(byte[] source, int offset)
        {
            ValidateScalar(source, offset);
            int first = 0;
            while (first < 31 && source[offset + first] == 0) first++;
            int length = 32 - first;
            if (IsZero(source, offset + first, length)) InvalidProof();
            bool prefix = (source[offset + first] & 0x80) != 0;
            byte[] result = new byte[2 + length + (prefix ? 1 : 0)];
            result[0] = 0x02;
            result[1] = checked((byte)(length + (prefix ? 1 : 0)));
            Buffer.BlockCopy(source, offset + first, result, prefix ? 3 : 2, length);
            return result;
        }

        private static bool IsZero(byte[] value, int offset, int length)
        {
            for (int i = 0; i < length; i++) if (value[offset + i] != 0) return false;
            return true;
        }

        private static void ValidateScalar(byte[] value, int offset)
        {
            if (IsZero(value, offset, 32)) InvalidProof();
            for (int i = 0; i < 32; i++)
            {
                if (value[offset + i] < P256Order[i]) return;
                if (value[offset + i] > P256Order[i]) InvalidProof();
            }
            InvalidProof();
        }

        private static void InvalidProof() => throw new ArgumentException("DEVICE_PROOF_INVALID");
    }

    public sealed class AndroidQuestP256KeyStore : IBackend034PairingSigner
    {
        private const string JavaClassName = "com.thinkingspace.security.QuestP256KeyStore";
        private static readonly Regex Alias = new Regex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);

        public bool IsSupported => Application.platform == RuntimePlatform.Android && !Application.isEditor;

        public void EnsureKey(string alias) => CallVoid("ensureKey", RequireAlias(alias));
        public bool ContainsKey(string alias) => Call<bool>("containsKey", RequireAlias(alias));
        public string GetPublicJwk(string alias) => Call<string>("getPublicJwk", RequireAlias(alias));
        public string GetPublicKeyThumbprint(string alias)
        {
            string jwk = GetPublicJwk(alias);
            string native = Call<string>("getPublicKeyThumbprint", RequireAlias(alias));
            string managed = PairingProofCryptography.PublicKeyThumbprint(jwk);
            if (!string.Equals(native, managed, StringComparison.Ordinal))
                throw new InvalidOperationException("DEVICE_KEY_INVALID");
            return native;
        }

        public string SignCanonicalPayload(string alias, byte[] canonicalPayload)
        {
            if (canonicalPayload == null || canonicalPayload.Length == 0)
                throw new ArgumentException("Canonical payload is required.", nameof(canonicalPayload));
            string signature = Call<string>("signP1363", RequireAlias(alias), Convert.ToBase64String(canonicalPayload));
            if (PairingProofCryptography.Base64UrlDecode(signature).Length != 64)
                throw new InvalidOperationException("DEVICE_PROOF_INVALID");
            return signature;
        }

        public void DeleteKey(string alias) => CallVoid("deleteKey", RequireAlias(alias));

        public AndroidQuestP256DiagnosticResult RunDiagnostics(string alias,
            byte[] authoritativeCanonicalPayload, bool deleteAtEnd)
        {
            if (authoritativeCanonicalPayload == null || authoritativeCanonicalPayload.Length == 0)
                throw new ArgumentException("Canonical payload is required.", nameof(authoritativeCanonicalPayload));
            string json = Call<string>("runDiagnostics", RequireAlias(alias),
                Convert.ToBase64String(authoritativeCanonicalPayload), deleteAtEnd);
            AndroidQuestP256DiagnosticResult result =
                JsonUtility.FromJson<AndroidQuestP256DiagnosticResult>(json);
            if (result == null || result.signatureLength != 64 || !result.signatureVerified
                || result.privateKeyExportable || string.IsNullOrEmpty(result.securityLevel))
                throw new InvalidOperationException("DEVICE_KEY_DIAGNOSTIC_FAILED");
            return result;
        }

        private static string RequireAlias(string alias)
        {
            if (!Alias.IsMatch(alias ?? string.Empty)) throw new ArgumentException("DEVICE_KEY_ALIAS_INVALID");
            return alias;
        }

        private T Call<T>(string method, params object[] args)
        {
            if (!IsSupported) throw new PlatformNotSupportedException("Android Keystore is available only on an Android player.");
            using (var bridge = new AndroidJavaClass(JavaClassName)) return bridge.CallStatic<T>(method, args);
        }

        private void CallVoid(string method, params object[] args)
        {
            if (!IsSupported) throw new PlatformNotSupportedException("Android Keystore is available only on an Android player.");
            using (var bridge = new AndroidJavaClass(JavaClassName)) bridge.CallStatic(method, args);
        }
    }

    [Serializable]
    public sealed class AndroidQuestP256DiagnosticResult
    {
        public bool keyExistedBefore;
        public bool keyExistsAfterGeneration;
        public int signatureLength;
        public bool signatureVerified;
        public bool privateKeyExportable;
        public bool insideSecureHardware;
        public string securityLevel;
        public bool deleted;
    }
}

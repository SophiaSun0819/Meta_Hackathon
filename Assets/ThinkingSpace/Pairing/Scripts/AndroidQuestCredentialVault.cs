using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ThinkingSpace.Pairing
{
    /// Stores the complete refresh credential as AES-GCM ciphertext. The AES key is non-exportable
    /// and remains in Android Keystore; no credential is placed in PlayerPrefs or Unity assets.
    public sealed class AndroidQuestCredentialVault : IQuestCredentialVault
    {
        private const string JavaClassName = "com.thinkingspace.security.QuestEncryptedCredentialStore";
        private readonly string alias;

        public AndroidQuestCredentialVault(string credentialAlias = "quest-pairing-v1")
        {
            if (string.IsNullOrWhiteSpace(credentialAlias)) throw new ArgumentException("DEVICE_KEY_ALIAS_INVALID");
            alias = credentialAlias;
        }

        public bool IsSupported => Application.platform == RuntimePlatform.Android && !Application.isEditor;
        public bool HasCredential => IsSupported && Call<bool>("contains", alias);

        public void Store(DeviceCredential credential)
        {
            RequireSupported();
            Validate(credential);
            CallVoid("store", alias, JsonConvert.SerializeObject(new StoredCredential
            {
                deviceId = credential.DeviceId,
                accountId = credential.AccountId,
                credentialVersion = credential.Version,
                refreshToken = credential.Token
            }, Formatting.None));
        }

        public bool TryLoad(out DeviceCredential credential)
        {
            credential = default;
            RequireSupported();
            string json = Call<string>("load", alias);
            if (string.IsNullOrEmpty(json)) return false;
            StoredCredential stored;
            try { stored = JsonConvert.DeserializeObject<StoredCredential>(json); }
            catch (JsonException exception) { throw new InvalidOperationException("DEVICE_CREDENTIAL_INVALID", exception); }
            credential = new DeviceCredential(stored?.deviceId, stored?.accountId,
                stored?.credentialVersion ?? 0, stored?.refreshToken);
            Validate(credential);
            return true;
        }

        public void Delete()
        {
            RequireSupported();
            CallVoid("delete", alias);
        }

        public bool IsKeyMaterialExportable()
        {
            RequireSupported();
            return Call<bool>("privateKeyMaterialExportable", alias);
        }

        public string GetSecurityLevel()
        {
            RequireSupported();
            return Call<string>("getSecurityLevel", alias);
        }

        public bool IsInsideSecureHardware()
        {
            RequireSupported();
            return Call<bool>("isInsideSecureHardware", alias);
        }

        private void RequireSupported()
        {
            if (!IsSupported) throw new PlatformNotSupportedException("Encrypted credential storage requires an Android player.");
        }

        private static void Validate(DeviceCredential value)
        {
            if (!Guid.TryParse(value.DeviceId, out _) || !Guid.TryParse(value.AccountId, out _)
                || value.Version < 1 || string.IsNullOrWhiteSpace(value.Token) || value.Token.Length < 32)
                throw new ArgumentException("DEVICE_CREDENTIAL_INVALID");
        }

        private static T Call<T>(string method, params object[] args)
        {
            using (var bridge = new AndroidJavaClass(JavaClassName)) return bridge.CallStatic<T>(method, args);
        }

        private static void CallVoid(string method, params object[] args)
        {
            using (var bridge = new AndroidJavaClass(JavaClassName)) bridge.CallStatic(method, args);
        }

        [Serializable]
        private sealed class StoredCredential
        {
            public string deviceId;
            public string accountId;
            public int credentialVersion;
            public string refreshToken;
        }
    }
}

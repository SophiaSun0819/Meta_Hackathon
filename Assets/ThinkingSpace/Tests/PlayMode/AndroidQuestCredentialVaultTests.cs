using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThinkingSpace.Tests.PlayMode
{
    public sealed class AndroidQuestCredentialVaultTests
    {
        private const string MainAlias = "thinking-space-credential-diagnostic-v1";
        private const string CorruptAlias = "thinking-space-credential-diagnostic-corrupt-v1";
        private const string DeviceId = "20000000-0000-4000-8000-000000000040";
        private const string AccountId = "30000000-0000-4000-8000-000000000040";
        private const string TokenOne = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string TokenTwo = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        private const string JavaClass = "com.thinkingspace.security.QuestEncryptedCredentialStore";

        [UnityTest]
        [UnityPlatform(RuntimePlatform.Android)]
        public IEnumerator DedicatedAliasEncryptsRotatesPersistsRejectsCorruptionAndDeletes()
        {
            using var vault = new AndroidJavaClass(JavaClass);
            bool existedBefore = vault.CallStatic<bool>("contains", MainAlias);

            if (!existedBefore)
            {
                vault.CallStatic("store", MainAlias, CredentialJson(1, TokenOne));
                AssertCredential(vault, 1, TokenOne);
                Assert.That(vault.CallStatic<bool>("privateKeyMaterialExportable", MainAlias), Is.False);

                vault.CallStatic("store", MainAlias, CredentialJson(2, TokenTwo));
                AssertCredential(vault, 2, TokenTwo);

                RunCorruptionRejection();
                string securityLevel = vault.CallStatic<string>("getSecurityLevel", MainAlias);
                bool hardwareBacked = vault.CallStatic<bool>("isInsideSecureHardware", MainAlias);
                Assert.That(securityLevel, Is.Not.Empty);
                Debug.Log("UNITY040_CREDENTIAL_VAULT PHASE1_PASS encrypted=true rotated=true "
                    + "corruptionRejected=true keyExportable=false retainedForColdLaunch=true securityLevel="
                    + securityLevel + " insideSecureHardware=" + hardwareBacked);
            }
            else
            {
                AssertCredential(vault, 2, TokenTwo);
                Assert.That(vault.CallStatic<bool>("privateKeyMaterialExportable", MainAlias), Is.False);
                string securityLevel = vault.CallStatic<string>("getSecurityLevel", MainAlias);
                bool hardwareBacked = vault.CallStatic<bool>("isInsideSecureHardware", MainAlias);
                vault.CallStatic("delete", MainAlias);
                Assert.That(vault.CallStatic<bool>("contains", MainAlias), Is.False);
                Debug.Log("UNITY040_CREDENTIAL_VAULT PHASE2_PASS coldLaunchPersistence=true "
                    + "rotationPersisted=true deleted=true keyExportable=false securityLevel="
                    + securityLevel + " insideSecureHardware=" + hardwareBacked);
            }

            yield return null;
        }

        private static void AssertCredential(AndroidJavaClass vault, int version, string token)
        {
            Assert.That(vault.CallStatic<string>("load", MainAlias), Is.EqualTo(CredentialJson(version, token)));
        }

        private static void RunCorruptionRejection()
        {
            using var corrupt = new AndroidJavaClass(JavaClass);
            if (corrupt.CallStatic<bool>("contains", CorruptAlias)) corrupt.CallStatic("delete", CorruptAlias);
            corrupt.CallStatic("store", CorruptAlias, CredentialJson(1, TokenOne));
            corrupt.CallStatic("corruptForDiagnostics", CorruptAlias);
            Assert.Throws<AndroidJavaException>(() => corrupt.CallStatic<string>("load", CorruptAlias));
            corrupt.CallStatic("delete", CorruptAlias);
            Assert.That(corrupt.CallStatic<bool>("contains", CorruptAlias), Is.False);
        }

        private static string CredentialJson(int version, string token)
            => "{\"deviceId\":\"" + DeviceId + "\",\"accountId\":\"" + AccountId
                + "\",\"credentialVersion\":" + version + ",\"refreshToken\":\"" + token + "\"}";
    }
}

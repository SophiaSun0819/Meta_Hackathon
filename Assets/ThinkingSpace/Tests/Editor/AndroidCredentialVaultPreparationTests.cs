using System;
using System.IO;
using NUnit.Framework;
using ThinkingSpace.Pairing;
using UnityEditor;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class AndroidCredentialVaultPreparationTests
    {
        [Test]
        public void VaultIsUnavailableInEditorAndRejectsOperations()
        {
            var vault = new AndroidQuestCredentialVault("thinking-space-credential-diagnostic-editor");
            Assert.That(vault.IsSupported, Is.False);
            Assert.That(vault.HasCredential, Is.False);
            Assert.Throws<PlatformNotSupportedException>(() => vault.Store(new DeviceCredential(
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), 1, new string('A', 43))));
        }

        [Test]
        public void AndroidSourceRestrictsCorruptionHookAndNeverLogsSecrets()
        {
            const string path = "Assets/Plugins/Android/com.meta_hackathon.thinkingspace.security/QuestEncryptedCredentialStore.java";
            PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.Android), Is.True);
            Assert.That(importer.GetCompatibleWithEditor(), Is.False);
            string source = File.ReadAllText(path);
            StringAssert.Contains("AES/GCM/NoPadding", source);
            StringAssert.Contains("AndroidKeyStore", source);
            StringAssert.Contains("DIAGNOSTIC_ALIAS_REQUIRED", source);
            StringAssert.Contains("thinking-space-credential-diagnostic-", source);
            StringAssert.Contains("getSecurityLevel()", source);
            StringAssert.DoesNotContain("Log.", source);
            StringAssert.DoesNotContain("System.out", source);
        }
    }
}

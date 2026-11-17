using System;
using System.IO;
using NUnit.Framework;
using ThinkingSpace.UI;
using ThinkingSpace.Speech;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ThinkingSpaceLocalizationTests
    {
        private sealed class DevAgentSettingsFixture : ScriptableObject
        {
            public bool enabled;
            public string accessToken;
            public string preservedSetting;
        }

        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory=Path.Combine(Application.temporaryCachePath,"ThinkingSpaceLocalizationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }

        [Test]
        public void NewPreferencesUseIndependentLanguageDefaultsAndPersistChanges()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.Data.SchemaVersion,Is.EqualTo(5));
            Assert.That(preferences.Data.InterfaceLanguageCode,Is.EqualTo("en"));
            Assert.That(preferences.Data.ConversationLanguageMode,Is.EqualTo("auto"));
            Assert.That(preferences.Data.ConversationLanguageCode,Is.Empty);
            Assert.That(preferences.Data.VoiceProfileId,Is.EqualTo("orby-auto"));

            Assert.That(preferences.SetInterfaceLanguage("ES"),Is.True);
            Assert.That(preferences.SetConversationLanguage("explicit","es"),Is.True);
            Assert.That(preferences.SetVoiceProfileId("orby-es"),Is.True);

            var restored=new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.InterfaceLanguageCode,Is.EqualTo("es"));
            Assert.That(restored.Data.ConversationLanguageMode,Is.EqualTo("explicit"));
            Assert.That(restored.Data.ConversationLanguageCode,Is.EqualTo("es"));
            Assert.That(restored.Data.VoiceProfileId,Is.EqualTo("orby-es"));
        }

        [Test]
        public void SchemaFourPreferencesMigrateWithoutChangingWristPreference()
        {
            File.WriteAllText(Path.Combine(directory,ThinkingSpacePreferencesStore.FileName),
                "{\"SchemaVersion\":4,\"SilenceDurationSeconds\":3.5,\"WristMenuHand\":1}");
            var restored=new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.SchemaVersion,Is.EqualTo(5));
            Assert.That(restored.Data.WristMenuHand,Is.EqualTo(WristMenuHand.Right));
            Assert.That(restored.Data.SilenceDurationSeconds,Is.EqualTo(3.5f));
            Assert.That(restored.Data.InterfaceLanguageCode,Is.EqualTo("en"));
            Assert.That(restored.Data.ConversationLanguageMode,Is.EqualTo("auto"));
            Assert.That(restored.Data.VoiceProfileId,Is.EqualTo("orby-auto"));
        }

        [Test]
        public void FiveInterfaceLocalesAreValidatedAndAvailable()
        {
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("fr"),Is.EqualTo("fr"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("zh-CN"),Is.EqualTo("zh-CN"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("hi-IN"),Is.EqualTo("hi-IN"));
            CollectionAssert.AreEquivalent(new[]{"en","es","fr","zh-CN","hi-IN"},
                ThinkingSpaceLocalizationController.SupportedInterfaceLanguages);
            CollectionAssert.IsEmpty(
                ThinkingSpaceLocalizationController.PreparedInterfaceLanguages);
        }

        [Test]
        public void CatalogTranslatesAccentsAndFallsBackToEnglish()
        {
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("es","common.next"),
                Is.EqualTo("Siguiente"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("fr","common.previous"),
                Is.EqualTo("Précédent"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("en","menu.session.format",7),
                Is.EqualTo("Session 7"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("zh-CN","common.next"),
                Is.EqualTo("下一页"));
            Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("zh-CN","transfer.busy"),
                Is.EqualTo("Another document operation is already open."));
        }

        [Test]
        public void EveryCatalogEntryHasCompleteInitialLocaleText()
        {
            foreach(ThinkingSpaceStringEntry entry in ThinkingSpaceStringCatalog.Entries)
            {
                Assert.That(entry.English,Is.Not.Empty,$"English: {entry.Key}");
                Assert.That(entry.Spanish,Is.Not.Empty,$"Spanish: {entry.Key}");
                Assert.That(entry.French,Is.Not.Empty,$"French: {entry.Key}");
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("zh-CN",entry.Key),
                    Is.Not.Empty,$"Mandarin/fallback: {entry.Key}");
            }
        }

        [Test]
        public void PreparedMandarinFontAndLicenseArePresentWithRepresentativeGlyphs()
        {
            const string fontPath="Assets/ThinkingSpace/Resources/Fonts/NotoSansCJKsc-Regular SDF.asset";
            TMP_FontAsset font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            Assert.That(font,Is.Not.Null);
            Assert.That(font.atlasPopulationMode,Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(font.HasCharacter('下',false,true),Is.True);
            Assert.That(font.HasCharacter('页',false,true),Is.True);
            Assert.That(File.Exists(Path.Combine(Application.dataPath,
                "ThinkingSpace/UI/Fonts/NotoSansSC/OFL.txt")),Is.True);
        }

        [Test]
        public void DevanagariRuntimeFontAndLicenseProvideRepresentativeGlyphs()
        {
            const string fontPath="Assets/ThinkingSpace/Resources/Fonts/NotoSansDevanagari-Regular SDF.asset";
            TMP_FontAsset font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            Assert.That(font,Is.Not.Null);
            Assert.That(font.atlasPopulationMode,Is.EqualTo(AtlasPopulationMode.Dynamic));
            foreach(char value in "नमस्तेक्षित्र")Assert.That(font.HasCharacter(value,false,true),Is.True,$"U+{(int)value:X4}");
            Assert.That(File.Exists(Path.Combine(Application.dataPath,
                "ThinkingSpace/UI/Fonts/NotoSansDevanagari/OFL.txt")),Is.True);
            Assert.That(ThinkingSpaceLocalizationController.RuntimeFontResourcePathForLanguage("hi-IN"),
                Is.EqualTo("Fonts/NotoSansDevanagari-Regular SDF"));
            Assert.That(ThinkingSpaceLocalizationController.RuntimeFontForLanguage("hi-IN"),Is.SameAs(font),
                "Hindi menu labels must resolve the packaged Devanagari font, not the Latin default fallback.");
        }

        [Test]
        public void VoiceCacheIdentitySeparatesEveryApprovedLanguageProfile()
        {
            string[] languages={"en","es","fr","zh-CN","hi-IN"};
            string[] profiles={"orby-en","orby-es","orby-fr","orby-zh","orby-hi"};
            var identities=new System.Collections.Generic.HashSet<string>();
            for(int i=0;i<languages.Length;i++)
                Assert.That(identities.Add(ThinkingSpace.Companion.OrbyVoiceDirector.BuildLocalizedCacheIdentity(
                    "neutral",languages[i],profiles[i])),Is.True);
            Assert.That(identities.Count,Is.EqualTo(5));
        }

        [TestCase("en","orby-en")]
        [TestCase("es","orby-es")]
        [TestCase("fr","orby-fr")]
        [TestCase("zh-CN","orby-zh")]
        [TestCase("hi-IN","orby-hi")]
        public void VoiceRequestForwardsApprovedLanguageAndProfile(string language,string profile)
        {
            string json=ThinkingSpace.Companion.OrbyVoiceDirector.SerializeVoiceRequest(
                "voice-test","Session 1","Safe synthetic text","neutral",language,profile);
            Assert.That(json,Does.Contain("\"response_language\":\""+language+"\""));
            Assert.That(json,Does.Contain("\"voice_profile_id\":\""+profile+"\""));
        }

        [Test]
        public void LiveLanguageSwitchPersistsAndRaisesRefreshEvent()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            GameObject root=new GameObject("Localization Test");
            root.AddComponent<ThinkingSpaceLocalizationController>();
            ThinkingSpaceLocalizationController controller=ThinkingSpaceLocalizationController.Ensure(preferences);
            string changed=null;
            ThinkingSpaceLocalizationController.InterfaceLanguageChanged+=Capture;
            try
            {
                Assert.That(controller.SetInterfaceLanguage("fr"),Is.True);
                Assert.That(changed,Is.EqualTo("fr"));
                Assert.That(controller.InterfaceLanguageCode,Is.EqualTo("fr"));
                Assert.That(new ThinkingSpacePreferencesStore(directory).Data.InterfaceLanguageCode,Is.EqualTo("fr"));
            }
            finally
            {
                ThinkingSpaceLocalizationController.InterfaceLanguageChanged-=Capture;
                UnityEngine.Object.DestroyImmediate(root);
            }
            void Capture(string code)=>changed=code;
        }

        [Test]
        public void InterfaceLanguageSwitchDoesNotChangeConversationOrVoicePreferences()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.SetConversationLanguageAndVoiceProfile("explicit","fr","orby-fr"),Is.True);
            GameObject root=new GameObject("Independent Localization Test");
            root.AddComponent<ThinkingSpaceLocalizationController>();
            ThinkingSpaceLocalizationController controller=ThinkingSpaceLocalizationController.Ensure(preferences);
            try
            {
                Assert.That(controller.SetInterfaceLanguage("es"),Is.True);
                var restored=new ThinkingSpacePreferencesStore(directory).Data;
                Assert.That(restored.InterfaceLanguageCode,Is.EqualTo("es"));
                Assert.That(restored.ConversationLanguageCode,Is.EqualTo("fr"));
                Assert.That(restored.VoiceProfileId,Is.EqualTo("orby-fr"));
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test]
        public void SessionAndTransferVersionsRemainUnchanged()
        {
            Assert.That(BrainstormSessionStore.CurrentSchemaVersion,Is.EqualTo(3));
            Assert.That(ThinkingSpaceSessionTransferService.CurrentFormatVersion,Is.EqualTo(1));
        }

        [Test]
        public void ConversationLanguageAndProfilesUseBackendAllowlist()
        {
            Assert.That(ThinkingSpacePreferencesStore.NormalizeConversationLanguage("zh_cn"),Is.EqualTo("zh-CN"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeConversationLanguage("pt-BR"),Is.Null);
            Assert.That(ThinkingSpacePreferencesStore.NormalizeVoiceProfile("orby-hi"),Is.EqualTo("orby-hi"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeVoiceProfile("raw-voice-id"),Is.Null);
        }

        [TestCase("explicit","en","orby-en")]
        [TestCase("explicit","es","orby-es")]
        [TestCase("explicit","fr","orby-fr")]
        [TestCase("explicit","zh-CN","orby-zh")]
        [TestCase("explicit","hi-IN","orby-hi")]
        [TestCase("auto",null,"orby-auto")]
        public void ConversationLanguageAndVoiceProfilePersistAtomically(string mode,string language,string profile)
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.SetConversationLanguageAndVoiceProfile(mode,language,profile),Is.True);
            var restored=new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.ConversationLanguageMode,Is.EqualTo(mode));
            Assert.That(restored.Data.ConversationLanguageCode,Is.EqualTo(language??string.Empty));
            Assert.That(restored.Data.VoiceProfileId,Is.EqualTo(profile));
        }

        [Test]
        public void MismatchedConversationVoiceProfileIsRejectedWithoutPartialSave()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            Assert.That(preferences.SetConversationLanguageAndVoiceProfile("explicit","es","orby-fr"),Is.False);
            var restored=new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.ConversationLanguageMode,Is.EqualTo("auto"));
            Assert.That(restored.Data.VoiceProfileId,Is.EqualTo("orby-auto"));
        }

        [Test]
        public void ExistingMismatchedLanguageProfileIsRepairedOnLoad()
        {
            File.WriteAllText(Path.Combine(directory,ThinkingSpacePreferencesStore.FileName),
                "{\"SchemaVersion\":5,\"ConversationLanguageMode\":\"explicit\","+
                "\"ConversationLanguageCode\":\"es\",\"VoiceProfileId\":\"orby-fr\"}");
            var restored=new ThinkingSpacePreferencesStore(directory);
            Assert.That(restored.Data.ConversationLanguageCode,Is.EqualTo("es"));
            Assert.That(restored.Data.VoiceProfileId,Is.EqualTo("orby-es"));
        }

        [TestCase("你好，欢迎回来。","Fonts/NotoSansCJKsc-Regular SDF")]
        [TestCase("नमस्ते, फिर मिलते हैं।","Fonts/NotoSansDevanagari-Regular SDF")]
        [TestCase("Bonjour",null)]
        public void SubtitleTextSelectsScriptSpecificRuntimeFont(string value,string expected)
            =>Assert.That(ThinkingSpace.Companion.OrbySubtitleView.FontResourcePathForText(value),Is.EqualTo(expected));

        [Test]
        public void DefaultAutoRequestOmitsLanguageFieldsForLegacyBackend()
        {
            var context=new ConversationContextPayload();
            ThinkingSpaceAIWorkflowController.ApplyLanguagePreferences(context,
                new ThinkingSpacePreferencesData());
            string json=AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {request_id="request-legacy",session_id="Session 1",raw_transcript="Hello",context=context});
            Assert.That(json,Does.Not.Contain("response_language"));
            Assert.That(json,Does.Not.Contain("voice_profile_id"));
            Assert.That(json,Does.Not.Contain("input_language"));
        }

        [Test]
        public void MultilingualBackendCapabilityDefaultsOffUntilExplicitlyVerified()
        {
            GameObject root = new GameObject("Multilingual Capability Gate Test");
            try
            {
                var controller = root.AddComponent<BrainstormMenuController>();
                Assert.That(controller.MultilingualBackendContractEnabled, Is.False);

                typeof(BrainstormMenuController).GetField("multilingualBackendContractEnabled",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(controller, true);
                Assert.That(controller.MultilingualBackendContractEnabled, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void LegacyCleanedRequestIncludesCleanedTranscriptWithoutMultilingualMetadata()
        {
            var context = new ConversationContextPayload();
            string json = AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {
                request_id = "request-cleaned-legacy",
                session_id = "Session 1",
                raw_transcript = "Synthetic raw input.",
                cleaned_transcript = "Synthetic cleaned input.",
                transcript_source = "cleaned",
                context = context
            });

            Assert.That(json, Does.Contain("\"cleaned_transcript\":\"Synthetic cleaned input.\""));
            Assert.That(json, Does.Not.Contain("response_language"));
            Assert.That(json, Does.Not.Contain("response_language_mode"));
            Assert.That(json, Does.Not.Contain("voice_profile_id"));
            Assert.That(json, Does.Not.Contain("input_language"));
        }

        [TestCase("en","orby-en")]
        [TestCase("es","orby-es")]
        [TestCase("fr","orby-fr")]
        [TestCase("zh-CN","orby-zh")]
        [TestCase("hi-IN","orby-hi")]
        public void ExplicitConversationSettingsSerializeStageAContract(string language,string profile)
        {
            var settings=new ThinkingSpacePreferencesData
            {ConversationLanguageMode="explicit",ConversationLanguageCode=language,VoiceProfileId=profile};
            var context=new ConversationContextPayload();
            ThinkingSpaceAIWorkflowController.ApplyLanguagePreferences(context,settings);
            string json=AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {request_id="request-language",session_id="Session 1",raw_transcript="Synthetic",context=context});
            Assert.That(json,Does.Contain("\"response_language\":\""+language+"\""));
            Assert.That(json,Does.Contain("\"response_language_mode\":\"explicit\""));
            Assert.That(json,Does.Contain("\"voice_profile_id\":\""+profile+"\""));
            Assert.That(json,Does.Not.Contain("\"input_language\":\""+language+"\""));
        }

        [Test]
        public void InterfaceLanguageVoiceRequestRequiresBoundedConfirmation()
        {
            var preferences=new ThinkingSpacePreferencesStore(directory);
            GameObject root=new GameObject("Localization Confirmation Test");
            root.AddComponent<ThinkingSpaceLocalizationController>();
            ThinkingSpaceLocalizationController controller=ThinkingSpaceLocalizationController.Ensure(preferences);
            try
            {
                Assert.That(controller.RequestInterfaceLanguageChange("es"),Is.True);
                Assert.That(controller.InterfaceLanguageCode,Is.EqualTo("en"));
                controller.CancelPendingInterfaceLanguageChange();
                Assert.That(controller.InterfaceLanguageCode,Is.EqualTo("en"));
                Assert.That(controller.RequestInterfaceLanguageChange("fr"),Is.True);
                Assert.That(controller.ConfirmPendingInterfaceLanguageChange(),Is.True);
                Assert.That(controller.InterfaceLanguageCode,Is.EqualTo("fr"));
                Assert.That(controller.RequestInterfaceLanguageChange("zh-CN"),Is.True);
                controller.CancelPendingInterfaceLanguageChange();
                Assert.That(controller.RequestInterfaceLanguageChange("hi-IN"),Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void FriendlyWorkflowErrorsAreLocalizedWithoutProviderDetails()
        {
            string[] keys={"error.recoverable_title","error.network_unavailable",
                "error.ai_processing_failed","error.speech_not_understood",
                "error.unsupported_capability","error.generation_failed","error.session_safe",
                "common.dismiss","hud.mic_off_finishing"};
            foreach(string key in keys)
            {
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("en",key),Is.Not.Empty,key);
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("es",key),Is.Not.Empty,key);
                Assert.That(ThinkingSpaceLocalizationController.TranslateForLanguage("fr",key),Is.Not.Empty,key);
            }
            Assert.That(WorkflowStatusPresenter.MessageKey(
                RecoverableWorkflowFailureKind.NetworkUnavailable),Is.EqualTo("error.network_unavailable"));
            Assert.That(WorkflowStatusPresenter.ClassifyTransportFailure(503),
                Is.EqualTo(RecoverableWorkflowFailureKind.NetworkUnavailable));
            Assert.That(WorkflowStatusPresenter.ClassifyTransportFailure(422),
                Is.EqualTo(RecoverableWorkflowFailureKind.SpeechRecognitionFailed));
            Assert.That(WorkflowStatusPresenter.ClassifyTransportFailure(0, "WAV payload is invalid"),
                Is.EqualTo(RecoverableWorkflowFailureKind.SpeechRecognitionFailed));
            Assert.That(WorkflowStatusPresenter.ClassifyTransportFailure(0, "Network connection failed"),
                Is.EqualTo(RecoverableWorkflowFailureKind.NetworkUnavailable));
            Assert.That(WorkflowStatusPresenter.ClassifyAiFailure(0, "response_validation"),
                Is.EqualTo(RecoverableWorkflowFailureKind.AiProcessingFailed));
        }

        [Test]
        public void DisabledDevAgentCredentialGuardClearsOnlyDisabledCredentials()
        {
            var fixture=ScriptableObject.CreateInstance<DevAgentSettingsFixture>();
            try
            {
                fixture.enabled=false;
                fixture.accessToken="placeholder-local-token";
                fixture.preservedSetting="unchanged";
                var serialized=new SerializedObject(fixture);
                Assert.That(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard
                    .TrySanitizeSerializedObject(serialized,out bool enabled,out bool changed),Is.True);
                Assert.That(enabled,Is.False);
                Assert.That(changed,Is.True);
                Assert.That(fixture.accessToken,Is.Empty);
                Assert.That(fixture.preservedSetting,Is.EqualTo("unchanged"));

                fixture.enabled=true;
                fixture.accessToken="placeholder-enabled-token";
                serialized.Update();
                Assert.That(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard
                    .TrySanitizeSerializedObject(serialized,out enabled,out changed),Is.True);
                Assert.That(enabled,Is.True);
                Assert.That(changed,Is.False);
                Assert.That(fixture.accessToken,Is.EqualTo("placeholder-enabled-token"));
                Assert.That(new ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard().callbackOrder,
                    Is.EqualTo(int.MaxValue));
                Assert.That(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard
                    .IsProjectCredentialEmpty(),Is.True,
                    "The disabled project DevAgent asset must remain credential-free after Editor initialization.");
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }

        [Test]
        public void DisabledDevAgentPrebuildGuardRemovesInjectedPlaceholder()
        {
            ScriptableObject settings=AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard.SettingsAssetPath);
            Assert.That(settings,Is.Not.Null);
            var serialized=new SerializedObject(settings);
            SerializedProperty enabled=serialized.FindProperty("enabled");
            SerializedProperty token=serialized.FindProperty("accessToken");
            Assert.That(enabled,Is.Not.Null);
            Assert.That(token,Is.Not.Null);
            Assert.That(enabled.boolValue,Is.False,"This safeguard test requires DevAgent to be disabled.");
            try
            {
                token.stringValue="placeholder-build-injection";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                new ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard().OnPreprocessBuild(null);
                Assert.That(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard
                    .IsProjectCredentialEmpty(),Is.True);
            }
            finally
            {
                serialized.Update();
                token=serialized.FindProperty("accessToken");
                if(token!=null){token.stringValue=string.Empty;serialized.ApplyModifiedPropertiesWithoutUndo();}
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        [Test]
        public void DisabledDevAgentSerializationCleanupPreservesOtherBytesAndNewlines()
        {
            string path=Path.Combine(directory,"DevAgentSettings.asset");
            byte[] original=System.Text.Encoding.UTF8.GetBytes(
                "enabled: 0\r\n  accessToken: \r\n  serverPort: 48735\r\n");
            File.WriteAllBytes(path,original);
            Assert.That(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard
                .NormalizeEmptyCredentialLine(path),Is.True);
            byte[] expected=System.Text.Encoding.UTF8.GetBytes(
                "enabled: 0\r\n  accessToken:\r\n  serverPort: 48735\r\n");
            Assert.That(File.ReadAllBytes(path),Is.EqualTo(expected));
        }

        [Test]
        public void DisabledDevAgentLiveSanitizationDoesNotPerformSecondRawFileRewrite()
        {
            Type guard=typeof(ThinkingSpace.Editor.DisabledMetaDevAgentCredentialGuard);
            var live=guard.GetMethod("TrySanitizeProjectAsset",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var rawRewrite=guard.GetMethod("NormalizeEmptyCredentialLine",
                System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            Assert.That(live,Is.Not.Null);
            Assert.That(rawRewrite,Is.Not.Null);
            byte[] il=live.GetMethodBody()?.GetILAsByteArray();
            Assert.That(il,Is.Not.Null.And.Not.Empty);
            byte[] token=BitConverter.GetBytes(rawRewrite.MetadataToken);
            bool callsRawRewrite=false;
            for(int index=0;index+token.Length<il.Length;index++)
            {
                if(il[index]!=(byte)System.Reflection.Emit.OpCodes.Call.Value)continue;
                bool matches=true;
                for(int offset=0;offset<token.Length;offset++)
                    if(il[index+1+offset]!=token[offset]){matches=false;break;}
                if(matches){callsRawRewrite=true;break;}
            }
            Assert.That(callsRawRewrite,Is.False,
                "Live sanitization must use Unity serialization only; a second raw disk write races Asset Import Workers.");
        }
    }
}

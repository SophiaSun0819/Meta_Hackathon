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
        public void InterfaceLocalesAreValidatedAndPreparedLocalesRemainHidden()
        {
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("fr"),Is.EqualTo("fr"));
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("zh-CN"),Is.Null);
            Assert.That(ThinkingSpacePreferencesStore.NormalizeInterfaceLanguage("hi-IN"),Is.Null);
            CollectionAssert.AreEquivalent(new[]{"en","es","fr"},
                ThinkingSpaceLocalizationController.SupportedInterfaceLanguages);
            CollectionAssert.AreEquivalent(new[]{"zh-CN","hi-IN"},
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
            const string fontPath="Assets/ThinkingSpace/UI/Fonts/NotoSansSC/NotoSansCJKsc-Regular SDF.asset";
            TMP_FontAsset font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            Assert.That(font,Is.Not.Null);
            Assert.That(font.atlasPopulationMode,Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(font.HasCharacter('下',false,true),Is.True);
            Assert.That(font.HasCharacter('页',false,true),Is.True);
            Assert.That(File.Exists(Path.Combine(Application.dataPath,
                "ThinkingSpace/UI/Fonts/NotoSansSC/OFL.txt")),Is.True);
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
        public void ExplicitConversationSettingsSerializeStageAContract()
        {
            var settings=new ThinkingSpacePreferencesData
            {ConversationLanguageMode="explicit",ConversationLanguageCode="fr",VoiceProfileId="orby-fr"};
            var context=new ConversationContextPayload();
            ThinkingSpaceAIWorkflowController.ApplyLanguagePreferences(context,settings);
            string json=AIRequestJsonSerializer.Serialize(new ConversationProcessRequest
            {request_id="request-fr",session_id="Session 1",raw_transcript="Bonjour",context=context});
            Assert.That(json,Does.Contain("\"response_language\":\"fr\""));
            Assert.That(json,Does.Contain("\"response_language_mode\":\"explicit\""));
            Assert.That(json,Does.Contain("\"voice_profile_id\":\"orby-fr\""));
            Assert.That(json,Does.Not.Contain("input_language\":\"fr"));
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
                Assert.That(controller.RequestInterfaceLanguageChange("zh-CN"),Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}

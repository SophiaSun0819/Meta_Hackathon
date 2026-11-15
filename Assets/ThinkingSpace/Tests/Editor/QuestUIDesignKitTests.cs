using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ThinkingSpace.UI;
using ThinkingSpace.UI.DesignKit;
using ThinkingSpace.UI.Editor;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class QuestUIDesignKitTests
    {
        [OneTimeSetUp] public void BuildStagingAssets()=>QuestUIDesignKitPrefabBuilder.Build();

        [Test] public void PreviewUsesApprovedWorldSpaceDimensionsAndMetaAdapter()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(QuestUIDesignKitPrefabBuilder.PreviewPath);
            Assert.That(prefab,Is.Not.Null);RectTransform rect=prefab.GetComponent<RectTransform>();
            Assert.That(rect.sizeDelta,Is.EqualTo(new Vector2(960,640)));
            Assert.That(rect.localScale.x,Is.EqualTo(.00075f).Within(.000001f));
            Assert.That(prefab.GetComponent<Canvas>(),Is.Not.Null);
            Assert.That(prefab.GetComponent<GraphicRaycaster>(),Is.Not.Null);
            Assert.That(prefab.GetComponent<MetaHandInteractionAdapter>(),Is.Not.Null);
        }

        [Test] public void MainMenuHasExactlyFourApprovedRealActions()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(QuestUIDesignKitPrefabBuilder.PreviewPath);
            QuestUIActionCard[] cards=prefab.GetComponentsInChildren<QuestUIActionCard>(true);
            CollectionAssert.AreEquivalent(new[]{"new-session","my-sessions","voice-commands","hand-gestures"},cards.Select(card=>card.ActionId));
            Assert.That(cards.Length,Is.EqualTo(4));Assert.That(cards.All(card=>card.Button!=null&&card.Label!=null),Is.True);
            string[] labels=cards.Select(card=>card.Label.text).ToArray();
            CollectionAssert.AreEquivalent(new[]{"New Session","My Sessions","Voice Commands","Hand Gestures"},labels);
        }

        [Test] public void HeaderKeepsSettingsAndHelpOutsidePrimaryGrid()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(QuestUIDesignKitPrefabBuilder.PreviewPath);
            Transform header=prefab.transform.Find("Header");Assert.That(header,Is.Not.Null);
            Assert.That(header.Find("Settings")?.GetComponent<Button>(),Is.Not.Null);
            Assert.That(header.Find("Help")?.GetComponent<Button>(),Is.Not.Null);
            Assert.That(header.GetComponentsInChildren<QuestUIActionCard>(true),Is.Empty);
        }

        [Test] public void OrbySpeechBubbleIsSingleSubtitleSurfaceWithoutUserTranscript()
        {
            GameObject prefab=AssetDatabase.LoadAssetAtPath<GameObject>(QuestUIDesignKitPrefabBuilder.PrefabRoot+"/QuestUIOrbySpeechBubble.prefab");
            QuestUIOrbySpeechBubble bubble=prefab.GetComponent<QuestUIOrbySpeechBubble>();Assert.That(bubble,Is.Not.Null);Assert.That(bubble.Subtitle,Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<TMP_Text>(true).Count(text=>text.name.Contains("Transcript")),Is.Zero);
            Assert.That(QuestUIOrbySpeechBubble.DisplayDuration("one two three four five"),Is.EqualTo(3.5f).Within(.01f));
        }

        [Test] public void StagingPrefabsAreNotSceneDependencies()
        {
            string[] dependencies=AssetDatabase.GetDependencies("Assets/Scenes/Sandbox.unity",true);
            Assert.That(dependencies.Any(path=>path.StartsWith(QuestUIDesignKitPrefabBuilder.PrefabRoot)),Is.False);
        }
    }
}

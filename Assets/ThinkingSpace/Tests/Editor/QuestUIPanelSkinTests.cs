using NUnit.Framework;
using TMPro;
using ThinkingSpace.UI;
using ThinkingSpace.UI.DesignKit;
using UnityEngine;
using UnityEngine.UI;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class QuestUIPanelSkinTests
    {
        [Test]
        public void ButtonRolesFollowTheirPurpose()
        {
            Assert.That(QuestUIPanelSkin.RoleFor("Send to Orby Button"), Is.EqualTo(QuestUIPanelSkin.ButtonRole.Primary));
            Assert.That(QuestUIPanelSkin.RoleFor("Retry"), Is.EqualTo(QuestUIPanelSkin.ButtonRole.Primary));
            Assert.That(QuestUIPanelSkin.RoleFor("Confirm Delete Button"), Is.EqualTo(QuestUIPanelSkin.ButtonRole.Danger));
            Assert.That(QuestUIPanelSkin.RoleFor("Discard Button"), Is.EqualTo(QuestUIPanelSkin.ButtonRole.DangerQuiet));
            Assert.That(QuestUIPanelSkin.RoleFor("Close Button"), Is.EqualTo(QuestUIPanelSkin.ButtonRole.Secondary));
        }

        [Test]
        public void ApplyRoundsPanelHidesLegacyFrameAndStylesButtons()
        {
            GameObject root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            try
            {
                root.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 400f);
                ThinkingSpaceUIStyler.StylePanel(root, true, true);
                GameObject buttonObject = new GameObject("Send to Orby Button", typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(root.transform, false);
                GameObject label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(buttonObject.transform, false);
                GameObject heading = new GameObject("Heading", typeof(RectTransform), typeof(TextMeshProUGUI));
                heading.transform.SetParent(root.transform, false);

                QuestUIPanelSkin.Apply(root);

                Image background = root.GetComponent<Image>();
                Assert.That(background.sprite, Is.SameAs(QuestUIPanelSkin.RoundedFill));
                Assert.That(background.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(QuestUIPanelSkin.RoundedFill.border.x, Is.GreaterThan(0f));
                Image rim = root.transform.Find(QuestUIPanelSkin.OutlineName).GetComponent<Image>();
                Assert.That(rim.raycastTarget, Is.False);
                Assert.That(root.transform.Find("SlimUI Decoration").GetComponent<Image>().color.a, Is.Zero);
                Assert.That(root.transform.Find("UI Frame Top").gameObject.activeSelf, Is.False);
                Button button = buttonObject.GetComponent<Button>();
                Assert.That(button.transition, Is.EqualTo(Selectable.Transition.ColorTint));
                Assert.That(button.colors.normalColor, Is.EqualTo(QuestUIDesignTokens.Cyan));
                Assert.That(label.GetComponent<TMP_Text>().color, Is.EqualTo(QuestUIDesignTokens.Panel));
                Assert.That(heading.GetComponent<TMP_Text>().color, Is.EqualTo(QuestUIDesignTokens.Text));
                Assert.That(root.GetComponent<QuestUIAppear>(), Is.Not.Null);
                Assert.That(root.GetComponent<RectTransform>().localScale, Is.EqualTo(Vector3.one),
                    "Edit mode must not leave panels shrunk or transparent.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AppearEaseStartsAtZeroAndEndsAtOne()
        {
            Assert.That(QuestUIAppear.Ease(0f), Is.EqualTo(0f));
            Assert.That(QuestUIAppear.Ease(1f), Is.EqualTo(1f));
            Assert.That(QuestUIAppear.Ease(.5f), Is.GreaterThan(.5f));
        }
    }
}

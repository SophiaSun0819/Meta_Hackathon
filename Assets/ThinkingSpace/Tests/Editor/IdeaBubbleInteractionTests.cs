using NUnit.Framework;
using ThinkingSpace.Speech;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class IdeaBubbleInteractionTests
    {
        [TestCase(true, false, false, IdeaBubbleVisualState.Idle)]
        [TestCase(true, true, false, IdeaBubbleVisualState.Hovered)]
        [TestCase(true, false, true, IdeaBubbleVisualState.Selected)]
        [TestCase(true, true, true, IdeaBubbleVisualState.Selected)]
        [TestCase(false, true, true, IdeaBubbleVisualState.Unavailable)]
        public void VisualStateUsesDeterministicPrecedence(bool available, bool hovered,
            bool selected, IdeaBubbleVisualState expected)
        {
            Assert.That(IdeaBubbleView.ResolveVisualState(available, hovered, selected), Is.EqualTo(expected));
        }

        [Test]
        public void OpenBubbleRemainsHighlightedWhenItIsNotActive()
        {
            Assert.That(IdeaBubbleView.ResolveVisualState(true, false, true, false),
                Is.EqualTo(IdeaBubbleVisualState.Open));
            Assert.That(IdeaBubbleView.ResolveVisualState(true, true, true, true),
                Is.EqualTo(IdeaBubbleVisualState.Selected));
        }

        [TestCase(true, true, true, false, false, false, true)]
        [TestCase(true, true, true, true, false, false, false)]
        [TestCase(true, true, true, false, true, false, false)]
        [TestCase(true, true, true, false, false, true, false)]
        [TestCase(true, true, false, false, false, false, false)]
        [TestCase(true, false, true, false, false, false, false)]
        public void MenuCloseGateRequiresAnUninterruptedOwnerFist(bool menuVisible,
            bool ownerTracked, bool ownerFist, bool menuInteraction, bool otherInteraction,
            bool recordingLocked, bool expected)
        {
            Assert.That(ThinkingSpace.UI.BrainstormMenuController.CanAccumulateMenuClose(
                menuVisible, ownerTracked, ownerFist, menuInteraction, otherInteraction,
                recordingLocked), Is.EqualTo(expected));
        }

        [TestCase(3, .08f, .08f, false, true)]
        [TestCase(4, .12f, .08f, false, true)]
        [TestCase(2, .12f, .08f, false, false)]
        [TestCase(4, .02f, .08f, false, false)]
        [TestCase(3, -.08f, .08f, true, true)]
        [TestCase(3, .08f, .08f, true, false)]
        public void DeliberateGestureRequiresThreeFingersAndDirectionalTravel(int fingers,
            float travel, float minimum, bool returning, bool expected)
        {
            Assert.That(IdeaBubbleManager.IsDeliberateGesture(fingers, travel, minimum, returning),
                Is.EqualTo(expected));
        }
    }
}

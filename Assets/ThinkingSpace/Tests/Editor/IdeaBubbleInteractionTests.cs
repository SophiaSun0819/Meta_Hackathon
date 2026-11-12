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

        [TestCase(4, false, true)]
        [TestCase(3, false, false)]
        [TestCase(4, true, false)]
        public void CompleteFistRequiresAllFingersAndRejectsPinch(int fingers,
            bool pinching, bool expected)
        {
            Assert.That(IdeaBubbleManager.IsCompleteFist(fingers, pinching), Is.EqualTo(expected));
        }

        [Test]
        public void PoseHoldResetsOnAnyInvalidFrame()
        {
            float timer = 0f;
            Assert.That(IdeaBubbleManager.AccumulatePose(ref timer, true, .35f, .2f), Is.False);
            Assert.That(IdeaBubbleManager.AccumulatePose(ref timer, false, .35f, .2f), Is.False);
            Assert.That(timer, Is.Zero);
            Assert.That(IdeaBubbleManager.AccumulatePose(ref timer, true, .35f, .35f), Is.True);
        }

        [Test]
        public void RepeatedPhysicalSelectionIsRejectedAcrossPointerRelays()
        {
            Assert.That(IdeaBubbleManager.ShouldAcceptSelection("idea-1", 12, 1f,
                "idea-1", 12, 1.1f), Is.False);
            Assert.That(IdeaBubbleManager.ShouldAcceptSelection("idea-1", 12, 1f,
                "idea-1", 13, 1.1f), Is.False);
            Assert.That(IdeaBubbleManager.ShouldAcceptSelection("idea-1", 12, 1f,
                "idea-2", 12, 1.1f), Is.True);
            Assert.That(IdeaBubbleManager.ShouldAcceptSelection("idea-1", 12, 1f,
                "idea-1", 13, 1.21f), Is.True);
        }

        [Test]
        public void SameActiveBubbleTogglesOffButDifferentBubbleDoesNot()
        {
            Assert.That(IdeaBubbleManager.ShouldToggleSelectionOff("idea-1", true, "idea-1"), Is.True);
            Assert.That(IdeaBubbleManager.ShouldToggleSelectionOff("idea-1", true, "idea-2"), Is.False);
            Assert.That(IdeaBubbleManager.ShouldToggleSelectionOff("idea-1", false, "idea-1"), Is.False);
        }

        [Test]
        public void SelectionOwnershipIsHandSpecificAndUnknownCannotBeckon()
        {
            Assert.That(IdeaBubbleManager.IsGestureOwner(BubbleInteractionHand.Left,
                BubbleInteractionHand.Left), Is.True);
            Assert.That(IdeaBubbleManager.IsGestureOwner(BubbleInteractionHand.Left,
                BubbleInteractionHand.Right), Is.False);
            Assert.That(IdeaBubbleManager.IsGestureOwner(BubbleInteractionHand.Right,
                BubbleInteractionHand.Right), Is.True);
            Assert.That(IdeaBubbleManager.IsGestureOwner(BubbleInteractionHand.Unknown,
                BubbleInteractionHand.Left), Is.False);
        }

        [Test]
        public void BeckonRequiresNeutralThenStableFistAndHonorsNearGrabExclusion()
        {
            var phase = IdeaBubbleManager.GesturePhase.AwaitBeckonNeutral;
            float timer = 0f;
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, false, true, false, false, false, .2f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(phase, Is.EqualTo(IdeaBubbleManager.GesturePhase.AwaitBeckonFist));
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, false, false, true, true, false, .4f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(timer, Is.Zero);
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, false, false, true, false, false, .35f), Is.EqualTo(IdeaBubbleManager.GestureAction.Beckon));
        }

        [Test]
        public void ReturnRequiresFreshNeutralFistAndOpenSequence()
        {
            var phase = IdeaBubbleManager.GesturePhase.AwaitReturnNeutral;
            float timer = 0f;
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, true, true, false, false, false, .2f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(phase, Is.EqualTo(IdeaBubbleManager.GesturePhase.AwaitReturnFist));
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, true, true, false, false, false, .5f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(phase, Is.EqualTo(IdeaBubbleManager.GesturePhase.AwaitReturnFist));
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, true, false, true, false, false, .35f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(phase, Is.EqualTo(IdeaBubbleManager.GesturePhase.AwaitReturnOpen));
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, true, true, false, false, false, .15f), Is.EqualTo(IdeaBubbleManager.GestureAction.Return));
        }

        [Test]
        public void TrackingLossClearsGestureProgressWithoutReturning()
        {
            var phase = IdeaBubbleManager.GesturePhase.AwaitReturnOpen;
            float timer = .1f;
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                false, true, true, false, false, false, .2f), Is.EqualTo(IdeaBubbleManager.GestureAction.None));
            Assert.That(phase, Is.EqualTo(IdeaBubbleManager.GesturePhase.AwaitReturnNeutral));
            Assert.That(timer, Is.Zero);
        }

        [Test]
        public void ThreeInspectionSlotsAreDistinctAndFormAShallowArc()
        {
            UnityEngine.Vector3 anchor = new UnityEngine.Vector3(2f, 1.6f, -1f);
            UnityEngine.Vector3 forward = UnityEngine.Vector3.forward;
            UnityEngine.Vector3 center = IdeaBubbleManager.CalculateInspectionSlotPosition(
                anchor, forward, 0, .6f, .22f, .04f);
            UnityEngine.Vector3 left = IdeaBubbleManager.CalculateInspectionSlotPosition(
                anchor, forward, 1, .6f, .22f, .04f);
            UnityEngine.Vector3 right = IdeaBubbleManager.CalculateInspectionSlotPosition(
                anchor, forward, 2, .6f, .22f, .04f);
            Assert.That(UnityEngine.Vector3.Distance(center, left), Is.GreaterThan(.20f));
            Assert.That(UnityEngine.Vector3.Distance(center, right), Is.GreaterThan(.20f));
            Assert.That(left.z, Is.GreaterThan(center.z));
            Assert.That(right.z, Is.GreaterThan(center.z));
        }

        [Test]
        public void SlotPlacementUsesStableWorkspaceDirectionNotLaterHeadYaw()
        {
            UnityEngine.Vector3 anchor = UnityEngine.Vector3.zero;
            UnityEngine.Vector3 initialForward = UnityEngine.Vector3.forward;
            UnityEngine.Vector3 before = IdeaBubbleManager.CalculateInspectionSlotPosition(
                anchor, initialForward, 0, .6f, .22f, .04f);
            UnityEngine.Vector3 afterWalking = IdeaBubbleManager.CalculateInspectionSlotPosition(
                anchor, initialForward, 0, .6f, .22f, .04f);
            Assert.That(afterWalking, Is.EqualTo(before));
        }

        [Test]
        public void SlotAllocationAndEvictionAreDeterministicOldestFirst()
        {
            Assert.That(IdeaBubbleManager.FindFirstFreeSlot(new[] { 0, 2 }), Is.EqualTo(1));
            Assert.That(IdeaBubbleManager.FindFirstFreeSlot(new[] { 0, 1, 2 }), Is.EqualTo(-1));
            Assert.That(IdeaBubbleManager.OldestInspectionId(
                new[] { "idea-oldest", "idea-middle", "idea-newest" }), Is.EqualTo("idea-oldest"));
        }

        [Test]
        public void DistantInspectedBubbleCanBeResummonedWithoutChangingHomeData()
        {
            Assert.That(IdeaBubbleManager.ShouldResummon(UnityEngine.Vector3.zero,
                new UnityEngine.Vector3(.31f, 0f, 0f), .30f), Is.True);
            Assert.That(IdeaBubbleManager.ShouldResummon(UnityEngine.Vector3.zero,
                new UnityEngine.Vector3(.1f, 0f, 0f), .30f), Is.False);
        }

        [Test]
        public void InspectedFistCanResummonInsteadOfArmingReturnWhenUserMoved()
        {
            var phase = IdeaBubbleManager.GesturePhase.AwaitReturnFist;
            float timer = 0f;
            Assert.That(IdeaBubbleManager.AdvanceGestureState(ref phase, ref timer,
                true, true, false, true, false, true, .35f),
                Is.EqualTo(IdeaBubbleManager.GestureAction.Resummon));
        }

        [Test]
        public void NewSummonUsesCurrentUserAnchorWithoutMovingOldDestination()
        {
            UnityEngine.Vector3 stableForward = UnityEngine.Vector3.forward;
            UnityEngine.Vector3 positionA = UnityEngine.Vector3.zero;
            UnityEngine.Vector3 positionB = new UnityEngine.Vector3(2f, 0f, 1f);
            UnityEngine.Vector3 oldDestination = IdeaBubbleManager.CalculateInspectionSlotPosition(
                positionA, stableForward, 0, .6f, .22f, .04f);
            UnityEngine.Vector3 newDestination = IdeaBubbleManager.CalculateInspectionSlotPosition(
                positionB, stableForward, 1, .6f, .22f, .04f);
            Assert.That(oldDestination, Is.EqualTo(new UnityEngine.Vector3(0f, -.08f, .6f)));
            Assert.That(newDestination.x, Is.GreaterThan(1.7f));
            Assert.That(newDestination.z, Is.GreaterThan(1.6f));
        }

        [Test]
        public void DynamicDestinationMovesForwardToAvoidAnOccupiedBubble()
        {
            UnityEngine.Vector3 candidate = new UnityEngine.Vector3(0f, 1f, .6f);
            UnityEngine.Vector3 result = IdeaBubbleManager.FindNonOverlappingDestination(
                candidate, UnityEngine.Vector3.forward, new[] { candidate }, .2f);
            Assert.That(UnityEngine.Vector3.Distance(result, candidate), Is.GreaterThanOrEqualTo(.2f));
        }

        [Test]
        public void MovedPanelRestoresOnlyForTheSameStillOpenPanelInstance()
        {
            Assert.That(IdeaBubbleManager.ShouldRestoreMovedPanel(42, 42), Is.True);
            Assert.That(IdeaBubbleManager.ShouldRestoreMovedPanel(42, 43), Is.False);
            Assert.That(IdeaBubbleManager.ShouldRestoreMovedPanel(0, 0), Is.False);
        }

        [Test]
        public void HeadPoseRejectsUninitializedCenterEyeAndAcceptsTrackedFloorOriginPose()
        {
            Assert.That(IdeaBubbleManager.IsHeadPoseInitialized(UnityEngine.Vector3.zero,
                UnityEngine.Vector3.zero, true), Is.False);
            Assert.That(IdeaBubbleManager.IsHeadPoseInitialized(
                new UnityEngine.Vector3(0f, 1.65f, 0f),
                new UnityEngine.Vector3(0f, 1.65f, 0f), true), Is.True);
            Assert.That(IdeaBubbleManager.IsHeadPoseInitialized(
                new UnityEngine.Vector3(0f, 1.65f, 0f),
                new UnityEngine.Vector3(0f, 1.65f, 0f), false), Is.False);
        }

        [Test]
        public void ValidFirstSummonDestinationPreservesTrackedHeadHeight()
        {
            UnityEngine.Vector3 head = new UnityEngine.Vector3(3f, 1.65f, -2f);
            UnityEngine.Vector3 destination = IdeaBubbleManager.CalculateInspectionSlotPosition(
                head, UnityEngine.Vector3.forward, 0, .6f, .22f, .04f);
            Assert.That(destination.y, Is.EqualTo(1.57f).Within(.001f));
            Assert.That(destination.y, Is.GreaterThan(1f));
        }

        [Test]
        public void NaturalFistAcceptsThreeCurledAndOneNearCurledFingerWithHysteresis()
        {
            Assert.That(IdeaBubbleManager.IsNaturalFist(
                new[] { .55f, .60f, .62f, .72f }, false, false), Is.True);
            Assert.That(IdeaBubbleManager.IsNaturalFist(
                new[] { .68f, .70f, .76f, .80f }, false, true), Is.True);
            Assert.That(IdeaBubbleManager.IsNaturalFist(
                new[] { .55f, .60f, .62f, .72f }, true, false), Is.False);
            Assert.That(IdeaBubbleManager.IsNaturalFist(
                new[] { .55f, .60f, .78f, .84f }, false, false), Is.False);
        }

        [Test]
        public void SphereSurfaceDistanceAccountsForBubbleRadius()
        {
            Assert.That(IdeaBubbleManager.DistanceToSphereSurface(
                new UnityEngine.Vector3(.10f, 0f, 0f), UnityEngine.Vector3.zero, .075f),
                Is.EqualTo(.025f).Within(.0001f));
            Assert.That(IdeaBubbleManager.DistanceToSphereSurface(
                UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, .075f), Is.Zero);
        }

        [Test]
        public void BoundingBoxHasFourDeterministicOppositeCornerPairs()
        {
            for (int corner = 0; corner < 8; corner++)
            {
                int opposite = IdeaBubbleView.OppositeCorner(corner);
                Assert.That(opposite, Is.InRange(0, 7));
                Assert.That(opposite, Is.Not.EqualTo(corner));
                Assert.That(IdeaBubbleView.OppositeCorner(opposite), Is.EqualTo(corner));
            }
            Assert.That(IdeaBubbleView.OppositeCorner(-1), Is.EqualTo(-1));
        }

        [Test]
        public void UniformResizeUsesHandDistanceAndClampsLimits()
        {
            Assert.That(IdeaBubbleManager.CalculateUniformResize(1f, .4f, .6f, .6f, 2f),
                Is.EqualTo(1.5f).Within(.001f));
            Assert.That(IdeaBubbleManager.CalculateUniformResize(1f, .4f, 2f, .6f, 2f),
                Is.EqualTo(2f));
            Assert.That(IdeaBubbleManager.CalculateUniformResize(1f, .4f, .1f, .6f, 2f),
                Is.EqualTo(.6f));
        }

        [Test]
        public void ScaleAwareSurfaceToleranceSupportsSmallAndLargeBubblesWithinBounds()
        {
            float small = IdeaBubbleManager.ScaleAwareSurfaceTolerance(.045f, .05f, .035f, .075f);
            float large = IdeaBubbleManager.ScaleAwareSurfaceTolerance(.045f, .30f, .035f, .075f);
            Assert.That(large, Is.GreaterThan(small));
            Assert.That(small, Is.InRange(.035f, .075f));
            Assert.That(large, Is.InRange(.035f, .075f));
        }

        [Test]
        public void NearSelectionIsOneEntryAndCooldownGuarded()
        {
            Assert.That(IdeaBubbleManager.IsNearSelectionEligible(.01f, .015f, false, 0f), Is.True);
            Assert.That(IdeaBubbleManager.IsNearSelectionEligible(.01f, .015f, true, 0f), Is.False);
            Assert.That(IdeaBubbleManager.IsNearSelectionEligible(.01f, .015f, false, .2f), Is.False);
            Assert.That(IdeaBubbleManager.IsNearSelectionEligible(.03f, .015f, false, 0f), Is.False);
        }

        [Test]
        public void ResizeDeadbandRejectsJitterButAcceptsIntentionalChange()
        {
            Assert.That(IdeaBubbleManager.ApplyDeadband(1f, 1.01f, .015f), Is.EqualTo(1f));
            Assert.That(IdeaBubbleManager.ApplyDeadband(1f, 1.03f, .015f), Is.EqualTo(1.03f));
        }

        [Test]
        public void ScaleAwareInspectionClearanceUsesBothRadiiAndMargin()
        {
            UnityEngine.Vector3 occupied = UnityEngine.Vector3.zero;
            UnityEngine.Vector3 result = IdeaBubbleManager.FindScaleAwareDestination(
                new UnityEngine.Vector3(.2f, 0f, 0f), UnityEngine.Vector3.forward,
                .15f, new[] { occupied }, new[] { .20f }, .05f);
            Assert.That(UnityEngine.Vector3.Distance(result, occupied),
                Is.GreaterThanOrEqualTo(.399f));
        }

        [Test]
        public void RecordingGestureSuppressionIncludesReleaseCooldown()
        {
            Assert.That(ThumbRecordingGestureController.ShouldSuppressRecordingGesture(
                true, 10f, 0f), Is.True);
            Assert.That(ThumbRecordingGestureController.ShouldSuppressRecordingGesture(
                false, 10f, 10.3f), Is.True);
            Assert.That(ThumbRecordingGestureController.ShouldSuppressRecordingGesture(
                false, 10.4f, 10.3f), Is.False);
        }

        [Test]
        public void AnyOppositeSideCornerPairCanResizeRegardlessOfHandAssignment()
        {
            Assert.That(IdeaBubbleView.IsOppositeSide(0, 1), Is.True);
            Assert.That(IdeaBubbleView.IsOppositeSide(0, 3), Is.True);
            Assert.That(IdeaBubbleView.IsOppositeSide(6, 5), Is.True);
            Assert.That(IdeaBubbleView.IsOppositeSide(7, 2), Is.True);
            Assert.That(IdeaBubbleView.IsOppositeSide(0, 2), Is.False);
            Assert.That(IdeaBubbleView.IsOppositeSide(5, 7), Is.False);
            Assert.That(IdeaBubbleView.IsOppositeSide(1, 0), Is.True);
        }

        [Test]
        public void ResizeReservationRequiresBothHandsAtHandles()
        {
            Assert.That(IdeaBubbleManager.ShouldReserveResize(true, true), Is.True);
            Assert.That(IdeaBubbleManager.ShouldReserveResize(true, false), Is.False);
            Assert.That(IdeaBubbleManager.ShouldReserveResize(false, true), Is.False);
        }

        [Test]
        public void TwoNearbyHandsCanAutoSelectWithoutOpeningAPanel()
        {
            Assert.That(IdeaBubbleManager.ShouldAutoSelectForManipulation(
                true, true, .2f, .3f, false), Is.True);
            Assert.That(IdeaBubbleManager.ShouldAutoSelectForManipulation(
                true, false, .2f, .3f, false), Is.False);
            Assert.That(IdeaBubbleManager.ShouldAutoSelectForManipulation(
                true, true, .4f, .3f, false), Is.False);
            Assert.That(IdeaBubbleManager.ShouldAutoSelectForManipulation(
                true, true, .2f, .3f, true), Is.False);
        }

        [Test]
        public void NonModalAutomaticAIThinkingAllowsLocalBubbleInteraction()
        {
            Assert.That(SpeechTranscriptionCoordinator.ShouldBlockLocalBubbleInteraction(
                RecordingWorkflowState.Transcribing, true, false), Is.False);
            Assert.That(SpeechTranscriptionCoordinator.ShouldBlockLocalBubbleInteraction(
                RecordingWorkflowState.Transcribing, true, true), Is.True);
            Assert.That(SpeechTranscriptionCoordinator.ShouldBlockLocalBubbleInteraction(
                RecordingWorkflowState.Transcribing, false, false), Is.True);
            Assert.That(SpeechTranscriptionCoordinator.ShouldBlockLocalBubbleInteraction(
                RecordingWorkflowState.Ready, false, false), Is.False);
        }

        [Test]
        public void ZoneMembershipUsesOnlyHorizontalDistance()
        {
            UnityEngine.Vector2 zone = new UnityEngine.Vector2(2f, 3f);
            Assert.That(IdeaBubbleManager.IsWithinInspectionZone(
                new UnityEngine.Vector3(3f, 20f, 3f), zone, 1.5f), Is.True);
            Assert.That(IdeaBubbleManager.IsWithinInspectionZone(
                new UnityEngine.Vector3(4f, 1f, 3f), zone, 1.5f), Is.False);
            Assert.That(IdeaBubbleManager.HorizontalDistance(
                new UnityEngine.Vector3(0f, 1f, 0f),
                new UnityEngine.Vector3(0f, 8f, 1f)), Is.EqualTo(1f));
        }

        [Test]
        public void EachLocalZoneCanAllocateThreeIndependentSlots()
        {
            int[] zoneA = { 0, 1 };
            int[] zoneB = { 0 };
            Assert.That(IdeaBubbleManager.FindFirstFreeSlot(zoneA), Is.EqualTo(2));
            Assert.That(IdeaBubbleManager.FindFirstFreeSlot(zoneB), Is.EqualTo(1));
            Assert.That(IdeaBubbleManager.OldestInspectionId(
                new[] { "zone-a-old", "zone-a-new" }), Is.EqualTo("zone-a-old"));
            Assert.That(IdeaBubbleManager.OldestInspectionId(
                new[] { "zone-b-old", "zone-b-new" }), Is.EqualTo("zone-b-old"));
        }

        [Test]
        public void ScalingNeverReleasesSlotButDistantManualMoveDoes()
        {
            UnityEngine.Vector3 slot = UnityEngine.Vector3.zero;
            UnityEngine.Vector3 distant = new UnityEngine.Vector3(1f, 0f, 0f);
            Assert.That(IdeaBubbleManager.ShouldReleaseInspectionSlot(distant, slot, .28f, true),
                Is.False);
            Assert.That(IdeaBubbleManager.ShouldReleaseInspectionSlot(distant, slot, .28f, false),
                Is.True);
            Assert.That(IdeaBubbleManager.ShouldReleaseInspectionSlot(
                new UnityEngine.Vector3(.1f, 0f, 0f), slot, .28f, false), Is.False);
        }

        [Test]
        public void PlatformAppearsOnlyAfterSuccessfulOwnedSlotArrival()
        {
            Assert.That(IdeaBubbleManager.ShouldShowInspectionPlatform(false, true, false), Is.False);
            Assert.That(IdeaBubbleManager.ShouldShowInspectionPlatform(true, false, false), Is.False);
            Assert.That(IdeaBubbleManager.ShouldShowInspectionPlatform(true, true, true), Is.False);
            Assert.That(IdeaBubbleManager.ShouldShowInspectionPlatform(true, true, false), Is.True);
        }
    }
}

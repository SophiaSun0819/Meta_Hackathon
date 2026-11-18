using System.Collections.Generic;
using NUnit.Framework;
using ThinkingSpace.Speech;
using ThinkingSpace.UI;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ComfortLayoutTests
    {
        private static readonly Vector3 Head = new Vector3(0f, 1.6f, 0f);

        [Test]
        public void OrbyZoneIsLeftOfCenterAtEyeLevel()
        {
            Vector3 orby = ComfortLayout.PointInView(Head, Vector3.forward, ComfortLayout.Orby);
            Assert.That(orby.x, Is.LessThan(-0.5f));
            Assert.That(orby.z, Is.GreaterThan(0.7f));
            Assert.That(orby.y, Is.EqualTo(Head.y).Within(0.001f));
            Assert.That(Vector3.Distance(orby, Head), Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void MicPillIsCenteredBelowEyeLevel()
        {
            Vector3 pill = ComfortLayout.PointInView(Head, Vector3.forward, ComfortLayout.MicPill);
            Assert.That(pill.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(pill.y, Is.LessThan(Head.y - 0.2f));
        }

        [Test]
        public void PitchedHeadDoesNotTiltZones()
        {
            Vector3 lookingDown = new Vector3(0f, -0.7f, 0.7f);
            Vector3 level = ComfortLayout.PointInView(Head, Vector3.forward, ComfortLayout.Dialog);
            Vector3 pitched = ComfortLayout.PointInView(Head, lookingDown, ComfortLayout.Dialog);
            Assert.That(Vector3.Distance(level, pitched), Is.LessThan(0.001f));
        }

        [Test]
        public void BubbleArcFillsLeftToRightThenDropsARow()
        {
            Vector3 first = ComfortLayout.BubbleArcSlot(Head, Vector3.forward, 0);
            Vector3 second = ComfortLayout.BubbleArcSlot(Head, Vector3.forward, 1);
            Vector3 nextRow = ComfortLayout.BubbleArcSlot(Head, Vector3.forward, ComfortLayout.BubblesPerRow);
            Assert.That(second.x, Is.GreaterThan(first.x));
            Assert.That(nextRow.y, Is.LessThan(first.y - 0.2f));
            Assert.That(first.y, Is.LessThan(Head.y));
            Vector3 flat = Vector3.ProjectOnPlane(first - Head, Vector3.up);
            Assert.That(flat.magnitude, Is.EqualTo(ComfortLayout.BubbleArcDistance).Within(0.05f));
        }

        [Test]
        public void ArcSlotsDoNotOverlapOrby()
        {
            Vector3 orby = ComfortLayout.PointInView(Head, Vector3.forward, ComfortLayout.Orby);
            for (int i = 0; i < ComfortLayout.BubblesPerRow; i++)
                Assert.That(Vector3.Distance(ComfortLayout.BubbleArcSlot(Head, Vector3.forward, i), orby),
                    Is.GreaterThan(0.3f), "slot " + i);
        }

        [Test]
        public void FirstFreeArcSlotSkipsOccupiedSlots()
        {
            Vector3 slot0 = ComfortLayout.BubbleArcSlot(Head, Vector3.forward, 0);
            Vector3 slot1 = ComfortLayout.BubbleArcSlot(Head, Vector3.forward, 1);
            Vector3 chosen = IdeaBubbleManager.FirstFreeArcSlot(Head, Vector3.forward,
                new List<Vector3> { slot0 + new Vector3(0.02f, 0f, 0f) });
            Assert.That(Vector3.Distance(chosen, slot1), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(
                IdeaBubbleManager.FirstFreeArcSlot(Head, Vector3.forward, new List<Vector3>()), slot0),
                Is.LessThan(0.001f));
        }

        [Test]
        public void ArcReanchorsOnlyAfterTurningOrWalkingAway()
        {
            Assert.That(IdeaBubbleManager.ShouldReanchorBubbleArc(false, Head, Vector3.forward,
                Head, Vector3.forward), Is.True);
            Assert.That(IdeaBubbleManager.ShouldReanchorBubbleArc(true, Head, Vector3.forward,
                Head, Quaternion.AngleAxis(30f, Vector3.up) * Vector3.forward), Is.False);
            Assert.That(IdeaBubbleManager.ShouldReanchorBubbleArc(true, Head, Vector3.forward,
                Head, Vector3.right), Is.True);
            Assert.That(IdeaBubbleManager.ShouldReanchorBubbleArc(true, Head, Vector3.forward,
                Head + new Vector3(1.5f, 0f, 0f), Vector3.forward), Is.True);
        }

        [Test]
        public void ContentStaysPutForSmallHeadTurns()
        {
            Vector3 dialog = ComfortLayout.PointInView(Head, Vector3.forward, ComfortLayout.Dialog);
            Vector3 smallTurn = Quaternion.AngleAxis(20f, Vector3.up) * Vector3.forward;
            Vector3 bigTurn = Quaternion.AngleAxis(60f, Vector3.up) * Vector3.forward;
            Assert.That(ComfortLayout.NeedsRecenter(Head, smallTurn, dialog, ComfortLayout.Dialog), Is.False);
            Assert.That(ComfortLayout.NeedsRecenter(Head, bigTurn, dialog, ComfortLayout.Dialog), Is.True);
        }

        [Test]
        public void StepLazyFollowGlidesBackAndSettles()
        {
            GameObject head = new GameObject("Head");
            GameObject content = new GameObject("Content");
            try
            {
                head.transform.position = Head;
                bool recentering = false;
                ComfortLayout.StepLazyFollow(content.transform, head.transform, ComfortLayout.Dialog,
                    ref recentering, true, 0f);
                Vector3 placed = content.transform.position;
                head.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                for (int i = 0; i < 200; i++)
                    ComfortLayout.StepLazyFollow(content.transform, head.transform, ComfortLayout.Dialog,
                        ref recentering, false, 1f / 60f);
                Vector3 target = ComfortLayout.PointInView(Head, Vector3.right, ComfortLayout.Dialog);
                Assert.That(Vector3.Distance(content.transform.position, target), Is.LessThan(0.02f));
                Assert.That(Vector3.Distance(content.transform.position, placed), Is.GreaterThan(0.5f));
                Assert.That(recentering, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(content);
            }
        }
    }
}

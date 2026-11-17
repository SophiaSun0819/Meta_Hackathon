using NUnit.Framework;
using ThinkingSpace.Audio;
using UnityEngine;

namespace ThinkingSpace.Tests.Editor
{
    public sealed class ThinkingSpaceSfxTests
    {
        [Test]
        public void CatalogRoutesAllRuntimeCuesToImportedClips()
        {
            Assert.That(ThinkingSpaceSfxManager.CueCount, Is.EqualTo(17));
            foreach (ThinkingSpaceSfxCue cue in System.Enum.GetValues(typeof(ThinkingSpaceSfxCue)))
            {
                Assert.That(ThinkingSpaceSfxManager.ResourceName(cue), Is.Not.Empty, cue.ToString());
                Assert.That(Resources.Load<AudioClip>("ThinkingSpaceSFX/" +
                    ThinkingSpaceSfxManager.ResourceName(cue)), Is.Not.Null, cue.ToString());
                Assert.That(ThinkingSpaceSfxManager.Cooldown(cue), Is.GreaterThanOrEqualTo(.04f));
            }
        }

        [Test]
        public void BubbleRoutingIsSpatialAndStatusRoutingIsNonSpatial()
        {
            Assert.That(ThinkingSpaceSfxManager.IsSpatial(ThinkingSpaceSfxCue.BubbleCreate), Is.True);
            Assert.That(ThinkingSpaceSfxManager.IsSpatial(ThinkingSpaceSfxCue.BubbleDelete), Is.True);
            Assert.That(ThinkingSpaceSfxManager.IsSpatial(ThinkingSpaceSfxCue.UiHover), Is.False);
            Assert.That(ThinkingSpaceSfxManager.IsSpatial(ThinkingSpaceSfxCue.RecordingStart), Is.False);
            Assert.That(ThinkingSpaceSfxManager.IsSpatial(ThinkingSpaceSfxCue.GentleError), Is.False);
        }
    }
}

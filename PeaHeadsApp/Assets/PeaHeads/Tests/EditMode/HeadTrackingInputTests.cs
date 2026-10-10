using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class HeadTrackingInputTests
    {
        [Test]
        public void InsideDeadZoneGivesZero() => Assert.AreEqual(0f, HeadTrackingInput.Shape(0.02f, 0.03f, 2.5f));

        [Test]
        public void ScalesDistancePastDeadZone() =>
            Assert.AreEqual(0.25f, HeadTrackingInput.Shape(0.13f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void KeepsSign() => Assert.AreEqual(-0.25f, HeadTrackingInput.Shape(-0.13f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void DoesNotClamp() => Assert.AreEqual(4.925f, HeadTrackingInput.Shape(2f, 0.03f, 2.5f), 1e-5f);

        [Test]
        public void ZeroStaysZero() => Assert.AreEqual(0f, HeadTrackingInput.Shape(0f, 0f, 3f));
    }
}

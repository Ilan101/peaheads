using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class HeadTrackingInputTests
    {
        [Test]
        public void RecenterOffCentreStillReachesBothEnds()
        {
            Assert.AreEqual(-1f, HeadTrackingInput.Recenter(-1f, -0.4f), 1e-5f);
            Assert.AreEqual(1f, HeadTrackingInput.Recenter(1f, -0.4f), 1e-5f);
        }

        [Test]
        public void RecenterNeutralGivesZero() => Assert.AreEqual(0f, HeadTrackingInput.Recenter(-0.4f, -0.4f), 1e-5f);

        [Test]
        public void RecenterAtZeroIsIdentity() => Assert.AreEqual(0.3f, HeadTrackingInput.Recenter(0.3f, 0f), 1e-5f);
    }
}

using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class PlantVisualsTests
    {
        [Test]
        public void GroundedY_KeepsBottomOnGround()
        {
            // Leaf base cylinder: centre 0.08, half-height 0.08 (bottom at 0). Flattened to 0.6x.
            float y = PlantVisuals.GroundedY(0.08f, 0.08f, 0.048f);

            Assert.AreEqual(0f, y - 0.048f, 1e-5f);
        }

        [Test]
        public void GroundedY_UnchangedAtRest()
        {
            Assert.AreEqual(0.08f, PlantVisuals.GroundedY(0.08f, 0.08f, 0.08f), 1e-5f);
        }
    }
}

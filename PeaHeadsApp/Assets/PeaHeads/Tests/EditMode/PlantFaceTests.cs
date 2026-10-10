using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantFaceTests
    {
        private static Vector3 Targets(bool mouth, bool wink) => PlantFace.Targets(mouth, wink, 1.22f, 1.3f, 0.15f);

        [Test]
        public void Targets_Neutral_IsRest() => Assert.AreEqual(Vector3.one, Targets(false, false));

        [Test]
        public void Targets_MouthOpen_FlaresSnoutAndHole() =>
            Assert.AreEqual(new Vector3(1.22f, 1.3f, 1f), Targets(true, false));

        [Test]
        public void Targets_Wink_SquintsEyeOnly() =>
            Assert.AreEqual(new Vector3(1f, 1f, 0.15f), Targets(false, true));

        [Test]
        public void Targets_Both() => Assert.AreEqual(new Vector3(1.22f, 1.3f, 0.15f), Targets(true, true));

        [TestCase(false, false, ExpectedResult = 0)]
        [TestCase(true, false, ExpectedResult = 1)]
        [TestCase(false, true, ExpectedResult = 2)]
        [TestCase(true, true, ExpectedResult = 3)]
        public int FaceFlags_PackBitLayout(bool mouth, bool wink) => FaceFlags.Pack(mouth, wink);

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FaceFlags_RoundTrip(bool mouth, bool wink) =>
            Assert.AreEqual((mouth, wink), FaceFlags.Unpack(FaceFlags.Pack(mouth, wink)));
    }
}

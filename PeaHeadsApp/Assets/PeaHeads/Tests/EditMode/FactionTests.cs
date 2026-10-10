using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class FactionTests
    {
        // Heads are tinted with these on an otherwise green plant, so neither may be green.
        [Test]
        public void Color_BloomIsYellow() => Assert.AreEqual(new Color(0.96f, 0.83f, 0.19f), Faction.Color(0));

        [Test]
        public void Color_BlightIsPurple() => Assert.AreEqual(new Color(0.62f, 0.3f, 0.82f), Faction.Color(1));
    }
}

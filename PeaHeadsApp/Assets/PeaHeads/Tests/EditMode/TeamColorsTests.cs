using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class TeamColorsTests
    {
        [Test]
        public void Of_HostIsPurple() => Assert.AreEqual(new Color(0.61f, 0.35f, 0.82f), TeamColors.Of(0));

        [Test]
        public void Of_ClientIsYellow() => Assert.AreEqual(new Color(0.96f, 0.83f, 0.19f), TeamColors.Of(1));
    }
}

using System.Collections.Generic;
using NUnit.Framework;

namespace PeaHeads.Tests
{
    public class KillClipTests
    {
        [Test]
        public void SerializeRoundTripsFramesAndTeams()
        {
            var clip = new KillClip { KillerTeam = 1, VictimTeam = 0 };
            clip.View.Add(new ClipFrame(-1.5f, new byte[] { 1, 2, 3 }));
            clip.Face.Add(new ClipFrame(-0.2f, new byte[] { 9 }));
            clip.Face.Add(new ClipFrame(0f, new byte[0]));

            KillClip copy = KillClip.Deserialize(clip.Serialize());

            Assert.AreEqual(1, copy.KillerTeam);
            Assert.AreEqual(0, copy.VictimTeam);
            Assert.AreEqual(1, copy.View.Count);
            Assert.AreEqual(-1.5f, copy.View[0].Time);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, copy.View[0].Jpeg);
            Assert.AreEqual(2, copy.Face.Count);
            CollectionAssert.AreEqual(new byte[] { 9 }, copy.Face[0].Jpeg);
        }

        [Test]
        public void ChunksReassembleInAnyOrder()
        {
            var data = new byte[10];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
            List<byte[]> chunks = KillClip.Split(data, 4);
            Assert.AreEqual(3, chunks.Count);

            var assembler = new ClipAssembler();
            Assert.IsNull(assembler.Add(7, 2, 3, chunks[2]));
            Assert.IsNull(assembler.Add(7, 0, 3, chunks[0]));
            Assert.IsNull(assembler.Add(7, 0, 3, chunks[0])); // duplicate ignored
            CollectionAssert.AreEqual(data, assembler.Add(7, 1, 3, chunks[1]));
        }

        [Test]
        public void NewClipIdDropsUnfinishedOne()
        {
            var assembler = new ClipAssembler();
            assembler.Add(1, 0, 2, new byte[] { 1 });
            Assert.IsNull(assembler.Add(2, 0, 2, new byte[] { 5 }));
            CollectionAssert.AreEqual(new byte[] { 5, 6 }, assembler.Add(2, 1, 2, new byte[] { 6 }));
        }

        [Test]
        public void WindowPicksFramesAroundTheKillOldestFirst()
        {
            var times = new List<double> { 10.0, 7.5, 9.9, 6.0, 10.4 };
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, KillClip.Window(times, 10.0, 3.0, 0.3));
        }
    }
}

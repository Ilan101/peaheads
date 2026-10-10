using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PeaHeads
{
    // One timestamped JPEG (seconds relative to the kill once inside a clip).
    public readonly struct ClipFrame
    {
        public readonly float Time;
        public readonly byte[] Jpeg;

        public ClipFrame(float time, byte[] jpeg)
        {
            Time = time;
            Jpeg = jpeg;
        }
    }

    // Killcam payload: the killer's view and face around the kill, plus who killed whom. Pure C#: serialisation,
    // chunking for RPCs and frame-window selection (EditMode-tested).
    public sealed class KillClip
    {
        private const int Version = 1;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        // Thread-safe clock shared by the face feed (background thread) and the view recorder.
        public static double Now => Clock.Elapsed.TotalSeconds;

        public int KillerTeam;
        public int VictimTeam;
        public readonly List<ClipFrame> View = new();
        public readonly List<ClipFrame> Face = new();

        // Indices of frames whose absolute time is in [kill - before, kill + after], oldest first.
        public static List<int> Window(IReadOnlyList<double> times, double kill, double before, double after)
        {
            var picked = new List<int>();
            for (int i = 0; i < times.Count; i++)
                if (times[i] >= kill - before && times[i] <= kill + after) picked.Add(i);
            picked.Sort((a, b) => times[a].CompareTo(times[b]));
            return picked;
        }

        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write((byte)KillerTeam);
            writer.Write((byte)VictimTeam);
            WriteFrames(writer, View);
            WriteFrames(writer, Face);
            return stream.ToArray();
        }

        public static KillClip Deserialize(byte[] data)
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            if (reader.ReadInt32() != Version) throw new InvalidDataException("Unknown killcam version");
            var clip = new KillClip { KillerTeam = reader.ReadByte(), VictimTeam = reader.ReadByte() };
            ReadFrames(reader, clip.View);
            ReadFrames(reader, clip.Face);
            return clip;
        }

        public static List<byte[]> Split(byte[] data, int chunkSize)
        {
            var chunks = new List<byte[]>();
            for (int offset = 0; offset < data.Length; offset += chunkSize)
            {
                var chunk = new byte[Math.Min(chunkSize, data.Length - offset)];
                Buffer.BlockCopy(data, offset, chunk, 0, chunk.Length);
                chunks.Add(chunk);
            }
            if (chunks.Count == 0) chunks.Add(Array.Empty<byte>());
            return chunks;
        }

        private static void WriteFrames(BinaryWriter writer, List<ClipFrame> frames)
        {
            writer.Write(frames.Count);
            foreach (ClipFrame frame in frames)
            {
                writer.Write(frame.Time);
                writer.Write(frame.Jpeg.Length);
                writer.Write(frame.Jpeg);
            }
        }

        private static void ReadFrames(BinaryReader reader, List<ClipFrame> frames)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                float time = reader.ReadSingle();
                frames.Add(new ClipFrame(time, reader.ReadBytes(reader.ReadInt32())));
            }
        }
    }

    // Collects chunks of one clip (any order); a new clip id discards an unfinished older one.
    public sealed class ClipAssembler
    {
        private int _clipId = -1;
        private byte[][] _chunks;
        private int _received;

        // Returns the full payload once the last missing chunk arrives, else null.
        public byte[] Add(int clipId, int index, int count, byte[] chunk)
        {
            if (count <= 0 || index < 0 || index >= count) return null;
            if (clipId != _clipId || _chunks == null || _chunks.Length != count)
            {
                _clipId = clipId;
                _chunks = new byte[count][];
                _received = 0;
            }
            if (_chunks[index] != null) return null;
            _chunks[index] = chunk;
            if (++_received < count) return null;

            int length = 0;
            foreach (byte[] c in _chunks) length += c.Length;
            var data = new byte[length];
            int offset = 0;
            foreach (byte[] c in _chunks)
            {
                Buffer.BlockCopy(c, 0, data, offset, c.Length);
                offset += c.Length;
            }
            _chunks = null;
            return data;
        }
    }
}

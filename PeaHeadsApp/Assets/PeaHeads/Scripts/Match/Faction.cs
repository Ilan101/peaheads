using UnityEngine;

namespace PeaHeads
{
    // Story identity of the two teams: team 0 (host) is Bloom, team 1 (client) is Blight.
    public static class Faction
    {
        public static readonly Color BloomColor = new(0.45f, 0.9f, 0.3f);
        public static readonly Color BlightColor = new(0.62f, 0.3f, 0.82f);

        public static string Name(int team) => team == 0 ? "BLOOM" : "BLIGHT";
        public static Color Color(int team) => team == 0 ? BloomColor : BlightColor;

        public static string Goal(int team) => team == 0
            ? "Drink the Sporelights. Heal the Grove.\nStop the Blight before it spreads."
            : "Drain the Sporelights. Feed the Rot.\nBury the Bloom before it can heal.";

        public static string WinLine(int team) => team == 0
            ? "The Sporelights blaze. The Grove blossoms again!"
            : "The Sporelights gutter out. The Grove withers into rot.";
    }
}

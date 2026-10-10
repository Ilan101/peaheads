using UnityEngine;

namespace PeaHeads
{
    // Team 0 = host, team 1 = client. Heads and score bars both read from here so they can't drift apart.
    public static class TeamColors
    {
        public static readonly Color Host = new(0.61f, 0.35f, 0.82f);
        public static readonly Color Client = new(0.96f, 0.83f, 0.19f);

        public static Color Of(int team) => team == 0 ? Host : Client;
    }
}

using UnityEngine;

namespace PeaHeads
{
    public enum MenuScreen { None, Start, Connecting, Pause }

    public class GameMenu : MonoBehaviour
    {
        public static MenuScreen ScreenFor(bool sessionActive, bool connected, bool pauseOpen)
        {
            if (!sessionActive) return MenuScreen.Start;
            if (!connected) return MenuScreen.Connecting;
            return pauseOpen ? MenuScreen.Pause : MenuScreen.None;
        }

        public static bool ShowRestart(bool isHost, bool matchExists) => isHost && matchExists;
    }
}

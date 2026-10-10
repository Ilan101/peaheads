using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PeaHeads
{
    public enum MenuScreen { None, Start, Connecting, Pause, Story }

    // Mouse-driven start / connecting / pause menu, built in code so the scene only needs this component. Plays the
    // StoryIntro once a session connects; the local plant is paused and can't be hit until it ends.
    [RequireComponent(typeof(SessionController))]
    public class GameMenu : MonoBehaviour
    {
        private const float PanelWidth = 440f;

        [SerializeField] private Font _font;
        [SerializeField] private Color _backdropColor = new(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _panelColor = new(0.1f, 0.17f, 0.1f, 0.95f);
        [SerializeField] private Color _buttonColor = new(0.3f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color _buttonHoverColor = new(0.45f, 0.75f, 0.3f, 1f);
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _accentColor = new(0.65f, 0.95f, 0.35f, 1f);

        private SessionController _session;
        private StoryIntro _story;
        private bool _storyStarted;
        private MatchManager _match;
        private GameObject _backdrop;
        private GameObject _startPanel;
        private GameObject _connectingPanel;
        private GameObject _pausePanel;
        private GameObject _restartButton;
        private Text _musicLabel;
        private Text _startInfo;
        private Text _startStatus;
        private Text _connectingText;
        private Text _connectingStatus;
        private Text _pauseInfo;
        private Text _pauseStatus;
        private bool _pauseOpen;
        private MenuScreen? _shown;

        public static MenuScreen ScreenFor(bool sessionActive, bool connected, bool pauseOpen)
        {
            if (!sessionActive) return MenuScreen.Start;
            if (!connected) return MenuScreen.Connecting;
            return pauseOpen ? MenuScreen.Pause : MenuScreen.None;
        }

        public static bool ShowRestart(bool isHost, bool matchExists) => isHost && matchExists;

        // Shutdown is deferred to the end of the frame; treat it as already over so Leave goes straight to Start.
        public static bool SessionActive(bool isClient, bool isServer, bool shuttingDown) =>
            (isClient || isServer) && !shuttingDown;

        private void Awake() => _session = GetComponent<SessionController>();

        // Start, not Awake: SessionController fills Address/LocalIps in its Awake.
        private void Start()
        {
            if (!_font) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!TryGetComponent(out _story)) _story = gameObject.AddComponent<StoryIntro>();
            if (!TryGetComponent(out Minimap _)) gameObject.AddComponent<Minimap>();
            if (!TryGetComponent(out MusicPlayer _)) gameObject.AddComponent<MusicPlayer>();
            EnsureEventSystem();
            BuildCanvas();
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !_backdrop) return;

            bool active = SessionActive(nm.IsClient, nm.IsServer, nm.ShutdownInProgress);
            bool connected = nm.IsHost || nm.IsConnectedClient;
            if (!active)
            {
                _pauseOpen = false;
                _storyStarted = false;
                _story.Stop();
            }
            else if (connected && !_storyStarted)
            {
                _storyStarted = true;
                _story.Play(MatchManager.TeamOf(nm.LocalClientId), FindFirstObjectByType<MatchManager>(), null);
            }

            bool story = StoryIntro.IsPlaying;
            Keyboard kb = Keyboard.current;
            if (connected && !story && kb != null && kb.escapeKey.wasPressedThisFrame) _pauseOpen = !_pauseOpen;

            MenuScreen screen = story ? MenuScreen.Story : ScreenFor(active, connected, _pauseOpen);
            if (screen != _shown) Show(screen, nm);

            NetworkObject player = nm.LocalClient?.PlayerObject;
            if (player && player.TryGetComponent(out PlayerInput input))
                input.Paused = screen is MenuScreen.Pause or MenuScreen.Story;
            if (player && player.TryGetComponent(out PlayerHealth health)) health.SetShielded(story);
            RefreshText(screen, nm);
        }

        private void Show(MenuScreen screen, NetworkManager nm)
        {
            bool wasOverlay = _shown is MenuScreen.Pause or MenuScreen.Story;
            _shown = screen;
            _backdrop.SetActive(screen is not (MenuScreen.None or MenuScreen.Story));
            _startPanel.SetActive(screen == MenuScreen.Start);
            _connectingPanel.SetActive(screen == MenuScreen.Connecting);
            _pausePanel.SetActive(screen == MenuScreen.Pause);

            if (screen == MenuScreen.Pause)
            {
                _match = FindFirstObjectByType<MatchManager>();
                _restartButton.SetActive(ShowRestart(nm.IsHost, _match));
            }

            if (screen != MenuScreen.None) SetCursorLocked(false);
            else if (wasOverlay) SetCursorLocked(true);
        }

        private void RefreshText(MenuScreen screen, NetworkManager nm)
        {
            switch (screen)
            {
                case MenuScreen.Start:
                    _startInfo.text = $"This PC: {_session.LocalIps}  ·  port {_session.Port}";
                    _startStatus.text = _session.Status;
                    break;
                case MenuScreen.Connecting:
                    _connectingText.text = $"Connecting to {_session.Address}…";
                    _connectingStatus.text = _session.Status;
                    break;
                case MenuScreen.Pause:
                    _pauseInfo.text = nm.IsHost
                        ? $"Hosting on {_session.LocalIps}:{_session.Port}  ·  players {nm.ConnectedClientsIds.Count}"
                        : $"Connected to {_session.Address}:{_session.Port}";
                    _pauseStatus.text = _session.Status;
                    break;
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void OnHost() => _session.Host();
        private void OnJoin() => _session.Join();
        private void OnResume() => _pauseOpen = false;

        private void OnLeave()
        {
            _pauseOpen = false;
            _session.Leave();
        }

        private void OnRestart()
        {
            _pauseOpen = false;
            if (_match) _match.RestartNow();
        }

        private void OnToggleMusic()
        {
            MusicPlayer.Muted = !MusicPlayer.Muted;
            _musicLabel.text = MusicLabel();
        }

        private static string MusicLabel() => MusicPlayer.Muted ? "Music: Off" : "Music: On";

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>()) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("GameMenuCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            _backdrop.transform.SetParent(canvasGo.transform, false);
            Stretch(_backdrop.GetComponent<RectTransform>());
            _backdrop.GetComponent<Image>().color = _backdropColor;

            _startPanel = AddPanel("Start");
            AddLabel(_startPanel, "PeaHeads", 44, _accentColor, FontStyle.Bold);
            AddLabel(_startPanel, "Bloom vs Blight", 22, _textColor, FontStyle.Italic);
            _startInfo = AddLabel(_startPanel, "", 16, _textColor);
            AddLabel(_startPanel, "Host IP", 18, _textColor);
            AddAddressField(_startPanel);
            AddButton(_startPanel, "Host  (play as Bloom)", OnHost);
            AddButton(_startPanel, "Join  (play as Blight)", OnJoin);
            _startStatus = AddLabel(_startPanel, "", 16, _textColor);

            _connectingPanel = AddPanel("Connecting");
            _connectingText = AddLabel(_connectingPanel, "", 22, _textColor);
            _connectingStatus = AddLabel(_connectingPanel, "", 16, _textColor);
            AddButton(_connectingPanel, "Cancel", OnLeave);

            _pausePanel = AddPanel("Pause");
            AddLabel(_pausePanel, "Paused", 40, _accentColor, FontStyle.Bold);
            _pauseInfo = AddLabel(_pausePanel, "", 16, _textColor);
            AddButton(_pausePanel, "Resume", OnResume);
            _restartButton = AddButton(_pausePanel, "Restart Match", OnRestart);
            _musicLabel = AddButton(_pausePanel, MusicLabel(), OnToggleMusic).GetComponentInChildren<Text>();
            AddButton(_pausePanel, "Leave", OnLeave);
            _pauseStatus = AddLabel(_pausePanel, "", 16, _textColor);
        }

        private GameObject AddPanel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            go.transform.SetParent(_backdrop.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, 0f);
            go.GetComponent<Image>().color = _panelColor;
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            go.SetActive(false);
            return go;
        }

        private Text AddLabel(GameObject parent, string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent.transform, false);
            Text label = go.GetComponent<Text>();
            ConfigureText(label, text, size, color);
            label.fontStyle = style;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            go.GetComponent<LayoutElement>().minHeight = size * 1.4f;
            return label;
        }

        private GameObject AddButton(GameObject parent, string text, UnityAction onClick)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent.transform, false);
            go.GetComponent<LayoutElement>().preferredHeight = 48f;
            var image = go.GetComponent<Image>();
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = _buttonColor;
            colors.highlightedColor = _buttonHoverColor;
            colors.selectedColor = _buttonColor;
            colors.pressedColor = _buttonColor * 0.8f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => GameAudio.Play(Sfx.Click, 0.7f, 0.04f));

            var labelGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            Stretch(labelGo.GetComponent<RectTransform>());
            ConfigureText(labelGo.GetComponent<Text>(), text, 22, _textColor);
            return go;
        }

        private void AddAddressField(GameObject parent)
        {
            var go = new GameObject("Address", typeof(RectTransform), typeof(Image), typeof(InputField),
                typeof(LayoutElement));
            go.transform.SetParent(parent.transform, false);
            go.GetComponent<LayoutElement>().preferredHeight = 44f;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.92f);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            Stretch(textRect);
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);
            Text text = textGo.GetComponent<Text>();
            ConfigureText(text, "", 22, Color.black);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = _session.Address;
            field.onValueChanged.AddListener(value => _session.Address = value);
        }

        private void ConfigureText(Text text, string value, int size, Color color)
        {
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}

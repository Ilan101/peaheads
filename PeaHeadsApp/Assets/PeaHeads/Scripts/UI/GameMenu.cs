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
        private const float PanelGap = 16f;

        private static readonly (string keys, string action)[] ControlLines =
        {
            ("Mouse", "Look"),
            ("WASD", "Move"),
            ("Space", "Plant / Unplant"),
            ("Lean Your Head", "Peek / Dodge"),
            ("Open Mouth", "Fire"),
            ("Wink", "Zoom"),
            ("C", "Recenter Head"),
            ("M", "Big Map"),
            ("Esc", "Menu"),
        };

        [SerializeField] private Font _font;
        [Tooltip("Dim behind the pause menu (over gameplay).")]
        [SerializeField] private Color _backdropColor = new(0f, 0f, 0f, 0.55f);
        [Tooltip("Dim behind the start / connecting screens: light, so the lit arena shows through.")]
        [SerializeField] private Color _startBackdropColor = new(0f, 0f, 0f, 0.15f);
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
        private GameObject _controlsPanel;
        private GameObject _restartButton;
        private Text _musicLabel;
        private Text _faceLabel;
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
            if (!TryGetComponent(out FaceFeed _)) gameObject.AddComponent<FaceFeed>();
            if (!TryGetComponent(out ViewRecorder _)) gameObject.AddComponent<ViewRecorder>();
            if (!TryGetComponent(out KillcamView _)) gameObject.AddComponent<KillcamView>();
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
            // Teams are reshuffled when a client joins; wait for ours so the story tells the right side.
            else if (connected && !_storyStarted &&
                     (!FindFirstObjectByType<MatchManager>() || MatchManager.HasTeam(nm.LocalClientId)))
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
            _backdrop.GetComponent<Image>().color = screen == MenuScreen.Pause ? _backdropColor : _startBackdropColor;
            _startPanel.SetActive(screen == MenuScreen.Start);
            _connectingPanel.SetActive(screen == MenuScreen.Connecting);
            _pausePanel.SetActive(screen == MenuScreen.Pause);
            _controlsPanel.SetActive(screen == MenuScreen.Pause);

            if (screen == MenuScreen.Pause)
            {
                _match = FindFirstObjectByType<MatchManager>();
                _restartButton.SetActive(ShowRestart(nm.IsHost, _match));
                MatchPausePanels();
            }

            if (screen != MenuScreen.None) SetCursorLocked(false);
            else if (wasOverlay) SetCursorLocked(true);
        }

        // Pause and Controls side by side at the same size: both as tall as the taller one.
        private void MatchPausePanels()
        {
            var pause = (RectTransform)_pausePanel.transform;
            var controls = (RectTransform)_controlsPanel.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(pause);
            LayoutRebuilder.ForceRebuildLayoutImmediate(controls);
            float height = Mathf.Max(LayoutUtility.GetPreferredHeight(pause), LayoutUtility.GetPreferredHeight(controls));
            pause.sizeDelta = new Vector2(PanelWidth, height);
            controls.sizeDelta = new Vector2(PanelWidth, height);
        }

        private void RefreshText(MenuScreen screen, NetworkManager nm)
        {
            switch (screen)
            {
                case MenuScreen.Start:
                    _startInfo.text = $"This PC: {_session.LocalIps}";
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

        private void OnToggleFace()
        {
            KillcamView.ShareFace = !KillcamView.ShareFace;
            _faceLabel.text = FaceLabel();
        }

        private static string FaceLabel() => KillcamView.ShareFace ? "Killcam Face: On" : "Killcam Face: Off";

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
            AddTitle(_startPanel);
            AddLabel(_startPanel, "Bloom vs Blight", 22, _textColor, FontStyle.Italic);
            _startInfo = AddLabel(_startPanel, "", 16, _textColor);
            AddAddressField(_startPanel);
            AddButton(_startPanel, "Join", OnJoin);
            AddButton(_startPanel, "Host", OnHost);
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
            _faceLabel = AddButton(_pausePanel, FaceLabel(), OnToggleFace).GetComponentInChildren<Text>();
            AddButton(_pausePanel, "Leave", OnLeave);
            _pauseStatus = AddLabel(_pausePanel, "", 16, _textColor);

            // Controls sit to the right of the pause panel; the pair is centred together.
            _controlsPanel = AddPanel("Controls");
            AddLabel(_controlsPanel, "Controls", 24, _accentColor, FontStyle.Bold);
            foreach (var (keys, action) in ControlLines) AddControlRow(_controlsPanel, keys, action);
            _pausePanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-(PanelWidth + PanelGap) * 0.5f, 0f);
            _controlsPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2((PanelWidth + PanelGap) * 0.5f, 0f);
            // Heights are matched in MatchPausePanels instead of each panel fitting its own content.
            _pausePanel.GetComponent<ContentSizeFitter>().enabled = false;
            _controlsPanel.GetComponent<ContentSizeFitter>().enabled = false;
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

        // Key (bold, accent) on the left, action on the right.
        private void AddControlRow(GameObject parent, string keys, string action)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent.transform, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            AddLabel(row, keys, 16, _accentColor, FontStyle.Bold).alignment = TextAnchor.MiddleLeft;
            AddLabel(row, action, 16, _textColor).alignment = TextAnchor.MiddleRight;
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

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            placeholderGo.transform.SetParent(go.transform, false);
            RectTransform placeholderRect = placeholderGo.GetComponent<RectTransform>();
            Stretch(placeholderRect);
            placeholderRect.offsetMin = textRect.offsetMin;
            placeholderRect.offsetMax = textRect.offsetMax;
            Text placeholder = placeholderGo.GetComponent<Text>();
            ConfigureText(placeholder, "Host IP", 22, new Color(0f, 0f, 0f, 0.4f));
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.fontStyle = FontStyle.Italic;

            // Starts empty so the grey "Host IP" shows; an empty box joins the default (local) address.
            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.onValueChanged.AddListener(value => _session.Address = value);
        }

        // "PeaHeads" in Chewy (Resources/Fonts, Apache 2.0): bubbly, plant-cartoon title with a dark outline.
        private void AddTitle(GameObject parent)
        {
            Text title = AddLabel(parent, "PeaHeads", 64, _accentColor);
            Font chewy = Resources.Load<Font>("Fonts/Chewy-Regular");
            if (chewy) title.font = chewy;
            title.GetComponent<LayoutElement>().minHeight = 80f;
            Outline outline = title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.2f, 0.05f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
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

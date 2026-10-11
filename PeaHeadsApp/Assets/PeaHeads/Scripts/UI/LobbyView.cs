using System.Collections.Generic;
using PeaHeads.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PeaHeads
{
    // Holding room after the story (and for mid-round joiners until the next round): the real plants stand in
    // LobbyStage's slots facing a mirrored camera, heads live, nothing else moves. Story-styled uGUI on top: PeaHeads
    // title card, slot names, the HUD's head tracker, host START. Added by GameMenu, which opens and closes it.
    public class LobbyView : MonoBehaviour
    {
        private const int SporeCount = 30;
        private static readonly Color Gold = new(1f, 0.84f, 0.42f);
        private static readonly Color Cream = new(0.95f, 0.9f, 0.8f);
        private static readonly Color Ink = new(0.05f, 0.04f, 0.03f, 0.85f);
        private static readonly Vector2 Centre = new(0.5f, 0.5f);

        public static bool IsOpen { get; private set; }
        // Story or lobby covers the game: HUDs, minimap and killcam stay hidden.
        public static bool HidesGame => StoryIntro.IsPlaying || IsOpen;

        private LobbyStage _stage;
        private RectTransform _canvas;
        private RawImage _view;
        private Text _subtitle;
        private Text _status;
        private GameObject _startButton;
        private readonly Text[] _names = new Text[LobbyStage.SlotCount];
        private TrackerPanel _tracker;
        private RectTransform _title;
        private RectTransform _bloomBadge;
        private RectTransform _blightBadge;
        private RectTransform[] _spores;
        private float[] _sporeSpeed;
        private NetworkObject _modePlayer;
        private int _appliedSlot = -1;

        public static bool ShouldOpen(bool storyDone, bool matchIsLobby, bool waiting) =>
            waiting || (storyDone && matchIsLobby);

        private void OnDisable() => IsOpen = false;

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            if (!_canvas) Build();
            _canvas.gameObject.SetActive(open);
            _stage.SetRendering(open);
            _appliedSlot = -1;
            if (open)
            {
                Refresh(); // now, not in the next Update: the first frame shows the full lobby, not a blank one
                return;
            }

            NetworkObject player = _modePlayer;
            if (player) SetPlayerMode(player, false);
            // START normally moves us via RespawnOnSideRpc; this covers a lobby closed any other way.
            NetworkManager nm = NetworkManager.Singleton;
            if (player && nm && nm.IsConnectedClient && !nm.ShutdownInProgress &&
                LobbyStage.Contains(player.transform.position) &&
                player.TryGetComponent(out PlayerNetSync sync)) sync.Respawn();
        }

        private void SetPlayerMode(NetworkObject player, bool lobby)
        {
            if (player.TryGetComponent(out PlayerInput input)) input.LobbyMode = lobby;
            if (player.TryGetComponent(out PlayerCameraRig rig)) rig.SetFirstPerson(!lobby);
            _modePlayer = lobby ? player : null;
        }

        private void Update()
        {
            if (IsOpen) Refresh();
        }

        private void Refresh()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            NetworkManager nm = NetworkManager.Singleton;
            ulong localId = nm ? nm.LocalClientId : 0;
            NetworkObject player = nm ? nm.LocalClient?.PlayerObject : null;
            if (player && player != _modePlayer)
            {
                SetPlayerMode(player, true);
                _appliedSlot = -1;
            }

            List<ulong> members = MatchManager.LobbyMembers();
            int slot = members.IndexOf(localId);
            if (player && slot >= 0 && slot != _appliedSlot && player.TryGetComponent(out PlayerNetSync sync))
            {
                Pose pose = _stage.Slot(slot);
                sync.MoveToLobby(pose.position, pose.rotation);
                _appliedSlot = slot;
            }

            _stage.SetRendering(true);
            _view.texture = _stage.Texture;
            UpdateNames(members, localId);
            UpdateFooter(members.Count, nm && nm.IsHost);
            _tracker.Refresh(player ? player.GetComponent<HeadTrackingInput>() : null);
            UiBuild.AnimateTitle(_title, _bloomBadge, _blightBadge, Time.unscaledTime);
            UpdateSpores(Time.unscaledDeltaTime);
        }

        // Name under each slot in team colour, or a dim placeholder until that pea is standing there.
        private void UpdateNames(List<ulong> members, ulong localId)
        {
            PlayerNetSync[] plants = FindObjectsByType<PlayerNetSync>(FindObjectsSortMode.None);
            for (int i = 0; i < _names.Length; i++)
            {
                Text label = _names[i];
                Pose pose = _stage.Slot(i);
                label.rectTransform.anchoredPosition = CanvasPoint(pose.position + Vector3.down * 0.35f);
                bool present = i < members.Count && StandsAt(plants, members[i], pose.position);
                _stage.SetSlot(i, present, present ? Faction.Color(MatchManager.TeamOf(members[i])) : Color.white);
                if (present)
                {
                    ulong id = members[i];
                    label.text = id == localId ? "YOU" : $"PEA {i + 1}";
                    label.color = Faction.Color(MatchManager.TeamOf(id));
                    label.fontSize = 32;
                }
                else
                {
                    label.text = "waiting for a pea…";
                    label.color = UiBuild.WithAlpha(Cream, 0.45f);
                    label.fontSize = 20;
                }
            }
        }

        private static bool StandsAt(PlayerNetSync[] plants, ulong clientId, Vector3 slot)
        {
            foreach (PlayerNetSync plant in plants)
            {
                if (plant.OwnerClientId != clientId) continue;
                Vector3 d = plant.transform.position - slot;
                return d.x * d.x + d.z * d.z < 1.5f * 1.5f;
            }
            return false;
        }

        // World point → canvas position on the mirrored view.
        private Vector2 CanvasPoint(Vector3 world)
        {
            Vector3 v = _stage.Camera.WorldToViewportPoint(world);
            Vector2 size = _canvas.rect.size;
            return new Vector2((0.5f - v.x) * size.x, (v.y - 0.5f) * size.y);
        }

        private void UpdateFooter(int count, bool isHost)
        {
            MatchManager match = MatchManager.Instance;
            bool lobby = match && match.IsLobby;
            _subtitle.text = $"Waiting for peas  ·  {count}/{LobbyStage.SlotCount}";
            _startButton.SetActive(isHost && lobby);
            _status.text = isHost && lobby ? ""
                : lobby ? "Waiting for the host to start…"
                : "Next round starts when this match ends";
        }

        private void UpdateSpores(float dt)
        {
            Rect area = _canvas.rect;
            float time = Time.unscaledTime;
            for (int i = 0; i < SporeCount; i++)
            {
                Vector2 p = _spores[i].anchoredPosition;
                p.y += _sporeSpeed[i] * dt;
                p.x += Mathf.Sin(time * 0.7f + i * 1.7f) * 12f * dt;
                if (p.y > area.yMax + 20f) p = new Vector2(Random.Range(area.xMin, area.xMax), area.yMin - 20f);
                _spores[i].anchoredPosition = p;
            }
        }

        private void OnStart()
        {
            if (MatchManager.Instance) MatchManager.Instance.StartMatch();
        }

        // ---------- Build ----------

        private void Build()
        {
            _stage = new GameObject("LobbyStage").AddComponent<LobbyStage>();
            _canvas = UiBuild.Canvas("LobbyCanvas", 90); // under GameMenu (100): pause opens over the lobby
            _canvas.SetParent(transform, false);
            _canvas.gameObject.AddComponent<GraphicRaycaster>();

            _view = UiBuild.Node(_canvas, "View", Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            UiBuild.Stretch(_view.rectTransform);
            _view.uvRect = new Rect(1f, 0f, -1f, 1f); // mirror: lean right and your pea moves right
            _view.raycastTarget = false;
            BuildSpores();
            Image vignette = UiBuild.Image(_canvas, UiBuild.Vignette, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
            UiBuild.Stretch(vignette.rectTransform);

            var top = new Vector2(0.5f, 1f);
            UiBuild.Image(_canvas, UiBuild.Glow, top, new Vector2(0f, 35f), new Vector2(560f, 230f),
                UiBuild.WithAlpha(UiBuild.Accent, 0.22f));
            Text title = UiBuild.Label(_canvas, "PeaHeads", 92, UiBuild.Accent, TextAnchor.MiddleCenter, top,
                new Vector2(0f, -28f), new Vector2(700f, 104f), FontStyle.Normal, false);
            UiBuild.TitleCard(title, out _bloomBadge, out _blightBadge);
            _title = title.rectTransform;
            _subtitle = UiBuild.Label(_canvas, "", 22, Cream, TextAnchor.MiddleCenter, top, new Vector2(0f, -140f),
                new Vector2(800f, 34f));
            for (int i = 0; i < _names.Length; i++)
            {
                _names[i] = DisplayLabel(_canvas, "", 32, Cream, Centre, Vector2.zero, new Vector2(320f, 44f));
                UiBuild.Outline(_names[i], Ink, 2f);
            }

            _tracker = new TrackerPanel(_canvas, new Vector2(6f, -6f), 256f); // same spot and size as the HUD's
            var bottom = new Vector2(0.5f, 0f);
            _startButton = Button(_canvas, "START", bottom, new Vector2(0f, 60f), new Vector2(260f, 68f), 38, OnStart);
            _status = DisplayLabel(_canvas, "", 26, Cream, bottom, new Vector2(0f, 76f), new Vector2(900f, 40f));
            UiBuild.Outline(_status, Ink, 2f);
            DisplayLabel(_canvas, "ESC: MENU", 16, UiBuild.WithAlpha(Color.white, 0.6f), bottom,
                new Vector2(0f, 22f), new Vector2(600f, 26f));
            _canvas.gameObject.SetActive(false);
        }

        private void BuildSpores()
        {
            _spores = new RectTransform[SporeCount];
            _sporeSpeed = new float[SporeCount];
            for (int i = 0; i < SporeCount; i++)
            {
                float size = Random.Range(6f, 14f);
                var pos = new Vector2(Random.Range(-640f, 640f), Random.Range(-360f, 360f));
                Image spore = UiBuild.Image(_canvas, UiBuild.Glow, pos, new Vector2(size, size),
                    UiBuild.WithAlpha(Gold, Random.Range(0.2f, 0.5f)));
                _spores[i] = spore.rectTransform;
                _sporeSpeed[i] = Random.Range(15f, 45f);
            }
        }

        private static Text DisplayLabel(Transform parent, string value, int size, Color color, Vector2 anchor,
            Vector2 pos, Vector2 box)
        {
            Text label = UiBuild.Label(parent, value, size, color, TextAnchor.MiddleCenter, anchor, pos, box, FontStyle.Normal);
            label.font = UiBuild.DisplayFont;
            return label;
        }

        // Same green buttons as GameMenu, display font, hover grow + click sound.
        private static GameObject Button(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size,
            int fontSize, UnityAction onClick)
        {
            Image image = UiBuild.Panel(parent, anchor, pos, size, Color.white);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.3f, 0.55f, 0.2f);
            colors.highlightedColor = new Color(0.45f, 0.75f, 0.3f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = colors.normalColor * 0.8f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            button.onClick.AddListener(() => GameAudio.Play(Sfx.Click, 0.7f, 0.04f));
            image.gameObject.AddComponent<UiHover>();
            Text label = DisplayLabel(image.transform, text, fontSize, Color.white, Centre, Vector2.zero, size);
            UiBuild.Outline(label, new Color(0f, 0f, 0f, 0.35f), 1.2f);
            return image.gameObject;
        }
    }
}

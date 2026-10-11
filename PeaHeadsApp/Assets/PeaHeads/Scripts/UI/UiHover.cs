using UnityEngine;
using UnityEngine.EventSystems;

namespace PeaHeads
{
    // Code-built buttons: grow a little under the pointer, dip when pressed. Unscaled time, so it works in menus.
    public class UiHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField, Range(1f, 1.3f)] private float _hoverScale = 1.06f;
        [SerializeField, Range(0.8f, 1f)] private float _pressScale = 0.96f;
        [SerializeField, Range(1f, 30f)] private float _speed = 14f;

        private bool _over;
        private bool _down;

        private void OnDisable()
        {
            _over = _down = false;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float target = _down ? _pressScale : _over ? _hoverScale : 1f;
            float scale = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-_speed * Time.unscaledDeltaTime));
            transform.localScale = Vector3.one * scale;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _over = true;
            GameAudio.Play(Sfx.Hover, 0.35f, 0.05f);
        }
        public void OnPointerExit(PointerEventData eventData) => _over = _down = false;
        public void OnPointerDown(PointerEventData eventData) => _down = true;
        public void OnPointerUp(PointerEventData eventData) => _down = false;
    }
}

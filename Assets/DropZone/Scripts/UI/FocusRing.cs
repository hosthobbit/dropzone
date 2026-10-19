using UnityEngine;
using UnityEngine.UI;

namespace DropZone
{
    /// <summary>Shows a highlight ring while its control has keyboard/gamepad focus.</summary>
    public class FocusRing : MonoBehaviour
    {
        public Selectable Target;
        Image img;
        void Awake() { img = GetComponent<Image>(); }
        void LateUpdate()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            bool on = es && Target && es.currentSelectedGameObject == Target.gameObject;
            img.enabled = on;
            if (on) img.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
        }
    }
}

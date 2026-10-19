using UnityEngine;

namespace DropZone
{
    /// <summary>
    /// Mostly fixed, slightly elevated front view that always fits the whole board (and the preview
    /// counter) between the HUD bands, on any screen shape. Adds a short shake or push-in on big
    /// moves, unless camera shake or motion is turned off.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        [Tooltip("Centre of the board area to frame.")] public Vector3 Focus = new Vector3(0f, 4.1f, 0f);
        [Tooltip("Width and height (metres) that must stay visible.")] public Vector2 Frame = new Vector2(8.6f, 8.4f);
        [Tooltip("Degrees the camera looks down.")] public float Pitch = 8f;
        [Tooltip("Screen fraction kept for the HUD at the top and bottom.")] public float TopBand = 0.15f, BottomBand = 0.17f;
        public float SideMargin = 0.03f;
        public float ShakeStrength = 0.12f;
        [Tooltip("Screen fraction to slide the board left (result panels sit on the right).")] public float SideShift;

        Camera cam;
        float shake, kick, shift;

        void Awake() { cam = GetComponent<Camera>(); }

        public void Shake(float amount)
        {
            if (!Settings.CameraShake || Settings.ReducedMotion) return;
            shake = Mathf.Max(shake, amount);
        }

        /// <summary>A brief push towards the board (big moves).</summary>
        public void Kick(float amount)
        {
            if (Settings.ReducedMotion) return;
            kick = Mathf.Max(kick, amount);
        }

        void LateUpdate()
        {
            float aspect = Mathf.Max(0.3f, cam.aspect);
            // portrait screens: the HUD is proportionally taller
            float top = aspect < 1f ? TopBand * 1.25f : TopBand, bottom = aspect < 1f ? BottomBand * 1.3f : BottomBand;
            float usable = 1f - top - bottom;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distV = Frame.y * 0.5f / (tanV * usable);
            float distH = Frame.x * 0.5f / (tanV * aspect * (1f - 2f * SideMargin));
            float dist = Mathf.Max(distV, distH);
            // shift the view so the board centre lands in the middle of the usable band
            float shiftY = (top - bottom) * tanV * dist;
            shift = Mathf.MoveTowards(shift, SideShift, Time.unscaledDeltaTime * (Settings.ReducedMotion ? 10f : 0.8f));
            float shiftX = shift * 2f * tanV * aspect * dist;

            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            var forward = rot * Vector3.forward;
            var up = rot * Vector3.up;
            float dt = Time.unscaledDeltaTime;
            shake = Mathf.MoveTowards(shake, 0f, dt * 1.8f);
            kick = Mathf.MoveTowards(kick, 0f, dt * 1.2f);
            float t = Time.unscaledTime;
            var jitter = new Vector3(Mathf.PerlinNoise(t * 23f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t * 23f) - 0.5f, 0f) * (shake * ShakeStrength * 4f);
            var pos = Focus + Vector3.right * shiftX + up * shiftY - forward * (dist * (1f - kick * 0.06f)) + jitter;
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}

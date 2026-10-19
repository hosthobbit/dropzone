using DropZone.Core;
using UnityEngine;

namespace DropZone
{
    /// <summary>One counter on screen. Purely visual: where it sits is decided by the logical board.</summary>
    public class CounterView : MonoBehaviour
    {
        public Player Owner;
        [Tooltip("Child that squashes and pulses, so the root can be moved freely.")] public Transform Visual;

        float pulse;      // win-line pulse, 0 = off
        float squash;     // landing squash, decays to 0

        public void Squash(float amount) => squash = amount;
        public void Pulse(bool on) => pulse = on ? 1f : 0f;

        void Update()
        {
            if (!Visual) return;
            squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 1.4f);
            float s = 1f + (pulse > 0f ? 0.07f * Mathf.Sin(Time.time * 7f) + 0.06f : 0f);
            // squash: wider and shorter for a moment after landing
            Visual.localScale = new Vector3(s * (1f + squash), s * (1f - squash), s);
        }

        public void ResetVisual()
        {
            pulse = 0f; squash = 0f;
            if (Visual) { Visual.localScale = Vector3.one; Visual.localRotation = Quaternion.identity; Visual.localPosition = Vector3.zero; }
        }
    }
}

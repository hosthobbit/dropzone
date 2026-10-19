using System;
using System.Collections.Generic;
using UnityEngine;

namespace DropZone
{
    /// <summary>
    /// Every sound in the game, synthesised at start-up (original, no audio files): drops and
    /// landings, power effects, timer ticks, the win reveal, victory jingle and a synth music loop.
    /// Browsers only start audio after the first click or key press; Unity handles that unlock.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public enum Kind { Release, Land, Hover, Select, Cancel, Invalid, Bomb, Swap, Tick, TimeUp, Reveal, Victory, Defeat, Draw, Click, RoundStart, Fall }

        public static Sfx Instance { get; private set; }
        const int Rate = 44100;

        [Range(0, 1)] public float SoundVolume = 0.85f;
        [Range(0, 1)] public float MusicVolume = 0.5f;
        [Tooltip("Music ducks to this fraction while a result jingle plays.")] public float Duck = 0.3f;

        readonly Dictionary<Kind, AudioClip> clips = new Dictionary<Kind, AudioClip>();
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource music;
        float duckUntil;
        readonly System.Random rng = new System.Random(7);

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < 14; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                pool.Add(s);
            }
            clips[Kind.Release] = Make(0.22f, Release);
            clips[Kind.Land] = Make(0.3f, Land);
            clips[Kind.Fall] = Make(0.18f, d => Clack(d, 900f, 0.3f));
            clips[Kind.Hover] = Make(0.05f, d => Tone(d, 1800f, 0.08f, 90f));
            clips[Kind.Click] = Make(0.07f, d => Tone(d, 1400f, 0.22f, 60f));
            clips[Kind.Select] = Make(0.35f, d => Sweep(d, 500f, 1400f, 0.25f));
            clips[Kind.Cancel] = Make(0.3f, d => Sweep(d, 1100f, 380f, 0.22f));
            clips[Kind.Invalid] = Make(0.25f, Invalid);
            clips[Kind.Bomb] = Make(1.2f, Boom);
            clips[Kind.Swap] = Make(0.6f, SwapZip);
            clips[Kind.Tick] = Make(0.12f, d => Tone(d, 1320f, 0.3f, 30f));
            clips[Kind.TimeUp] = Make(0.6f, d => { Tone(d, 660f, 0.3f, 6f); Tone(d, 440f, 0.25f, 6f); });
            clips[Kind.Reveal] = Make(1.2f, Reveal);
            clips[Kind.Victory] = Make(2.6f, Victory);
            clips[Kind.Defeat] = Make(1.6f, Defeat);
            clips[Kind.Draw] = Make(1.0f, d => Arp(d, new[] { 523f, 659f, 523f }, 0.14f, 0.2f));
            clips[Kind.RoundStart] = Make(0.7f, d => Arp(d, new[] { 392f, 523f, 784f }, 0.09f, 0.22f));

            music = gameObject.AddComponent<AudioSource>();
            music.clip = Make(19.2f, Music);
            music.loop = true; music.playOnAwake = false; music.volume = MusicVolume;
            music.Play();
        }

        void Update()
        {
            if (music) music.volume = Mathf.MoveTowards(music.volume, MusicVolume * (Time.unscaledTime < duckUntil ? Duck : 1f), Time.unscaledDeltaTime * 1.5f);
        }

        public static void Play(Kind k, float volume = 1f, float pitch = 1f)
        {
            var me = Instance;
            if (!me || !me.clips.TryGetValue(k, out var clip)) return;
            if (k == Kind.Victory || k == Kind.Defeat) me.duckUntil = Time.unscaledTime + clip.length;
            foreach (var s in me.pool)
            {
                if (s.isPlaying) continue;
                s.clip = clip;
                s.volume = volume * me.SoundVolume;
                s.pitch = pitch;
                s.Play();
                return;
            }
        }

        // ------------------------------------------------------------------ synthesis

        AudioClip Make(float seconds, Action<float[]> synth)
        {
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            synth(data);
            var c = AudioClip.Create("sfx", data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        float Noise() => (float)(rng.NextDouble() * 2 - 1);
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) * decay);

        void Tone(float[] d, float f, float vol, float decay)
        {
            for (int i = 0; i < d.Length; i++) { float t = (float)i / Rate; d[i] += Mathf.Sin(2 * Mathf.PI * f * t) * vol * Env(t, 0.004f, decay); }
        }

        void Sweep(float[] d, float f0, float f1, float vol)
        {
            float ph = 0, len = (float)d.Length / Rate;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate, k = t / len;
                ph += 2 * Mathf.PI * Mathf.Lerp(f0, f1, k * k) / Rate;
                d[i] += (Mathf.Sin(ph) * 0.7f + Mathf.Sin(ph * 2f) * 0.2f) * vol * Env(t, 0.01f, 9f);
            }
        }

        void Release(float[] d)
        {
            // a soft "fwip" as the counter leaves the preview
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate, k = t / 0.22f;
                lp += (Noise() - lp) * Mathf.Lerp(0.4f, 0.05f, k);
                d[i] = lp * Mathf.Sin(Mathf.Min(1f, k) * Mathf.PI) * 0.45f;
            }
        }

        void Clack(float[] d, float f, float vol)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                lp += (Noise() - lp) * 0.5f;
                d[i] += (lp * 0.6f + Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2.7f * t) * 0.2f) * vol * Env(t, 0.001f, 45f);
            }
        }

        void Land(float[] d)
        {
            // a chunky plastic clack plus a low thump
            Clack(d, 620f, 0.55f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                d[i] += Mathf.Sin(2 * Mathf.PI * (120f - t * 120f) * t) * 0.5f * Env(t, 0.002f, 22f);
            }
        }

        void Invalid(float[] d)
        {
            for (int b = 0; b < 2; b++)
            {
                int s0 = (int)(b * 0.1f * Rate);
                for (int i = s0; i < Mathf.Min(d.Length, s0 + (int)(0.08f * Rate)); i++)
                {
                    float t = (float)(i - s0) / Rate;
                    d[i] += Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 180f * t)) * 0.12f * Mathf.Min(1f, t * 300f);
                }
            }
        }

        void Boom(float[] d)
        {
            float lp = 0, ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                lp += (Noise() - lp) * Mathf.Lerp(0.35f, 0.02f, Mathf.Min(1f, t * 2f));
                ph += 2 * Mathf.PI * Mathf.Lerp(90f, 35f, Mathf.Min(1f, t * 3f)) / Rate;
                d[i] = (lp * 0.9f * Env(t, 0.002f, 4f) + Mathf.Sin(ph) * 0.7f * Env(t, 0.003f, 5f));
            }
            // crackle
            for (int k = 0; k < 14; k++)
            {
                int s0 = (int)((0.05f + (float)rng.NextDouble() * 0.5f) * Rate);
                for (int i = s0; i < Mathf.Min(d.Length, s0 + 600); i++) d[i] += Noise() * 0.25f * Mathf.Exp(-(i - s0) / 120f);
            }
        }

        void SwapZip(float[] d)
        {
            // two tones crossing over each other
            float p1 = 0, p2 = 0, len = (float)d.Length / Rate;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate, k = t / len;
                p1 += 2 * Mathf.PI * Mathf.Lerp(400f, 1200f, k) / Rate;
                p2 += 2 * Mathf.PI * Mathf.Lerp(1200f, 400f, k) / Rate;
                float e = Mathf.Sin(k * Mathf.PI);
                d[i] = (Mathf.Sin(p1) + Mathf.Sin(p2)) * 0.18f * e;
            }
        }

        void Arp(float[] d, float[] notes, float step, float vol)
        {
            for (int n = 0; n < notes.Length; n++)
            {
                int s0 = (int)(n * step * Rate);
                for (int i = s0; i < d.Length; i++)
                {
                    float t = (float)(i - s0) / Rate;
                    float f = notes[n];
                    // bright synth: saw-ish from a few harmonics
                    float v = Mathf.Sin(2 * Mathf.PI * f * t) + 0.45f * Mathf.Sin(4 * Mathf.PI * f * t) + 0.2f * Mathf.Sin(6 * Mathf.PI * f * t);
                    d[i] += v * vol * Env(t, 0.006f, 5f);
                }
            }
        }

        void Reveal(float[] d)
        {
            Arp(d, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.07f, 0.16f);
            // shimmer
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                d[i] += Mathf.Sin(2 * Mathf.PI * 2093f * t) * Mathf.Sin(2 * Mathf.PI * 7f * t) * 0.05f * Env(t, 0.3f, 2.5f);
            }
        }

        void Victory(float[] d)
        {
            // short fanfare: C E G C' (hold) G C'
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 783.99f, 1046.5f };
            float[] starts = { 0f, 0.13f, 0.26f, 0.39f, 0.9f, 1.05f };
            for (int n = 0; n < notes.Length; n++)
            {
                int s0 = (int)(starts[n] * Rate);
                for (int i = s0; i < d.Length; i++)
                {
                    float t = (float)(i - s0) / Rate, f = notes[n];
                    float v = Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(4 * Mathf.PI * f * t) + 0.25f * Mathf.Sin(6 * Mathf.PI * f * t);
                    d[i] += v * 0.13f * Env(t, 0.01f, n == 3 || n == 5 ? 1.6f : 4f);
                }
            }
        }

        void Defeat(float[] d)
        {
            Arp(d, new[] { 392f, 349.23f, 311.13f, 261.63f }, 0.22f, 0.16f);
        }

        void Music(float[] d)
        {
            // original dark synthwave loop, 100 bpm, 8 bars: pad chords, pulsing bass, soft arpeggio, kick and hats
            const float bpm = 100f, beat = 60f / bpm;
            int[] roots = { 9, 9, 5, 5, 0, 0, 7, 7 };  // Am Am F F C C G G (semitones above C)
            float C3 = 130.81f;
            for (int bar = 0; bar < 8; bar++)
            {
                int root = roots[bar];
                bool minor = root == 9;
                int[] chord = { 0, minor ? 3 : 4, 7 };
                float t0 = bar * 4 * beat;
                // pad
                foreach (int iv in chord) Pad(d, t0, 4 * beat, C3 * Mathf.Pow(2, (root + iv) / 12f), 0.05f);
                for (int s = 0; s < 8; s++)
                {
                    float ts = t0 + s * beat * 0.5f;
                    // bass eighths
                    Bass(d, ts, C3 / 2f * Mathf.Pow(2, root / 12f), 0.16f);
                    // arpeggio
                    int iv = chord[s % 3] + (s >= 4 ? 12 : 0);
                    Pluck(d, ts, C3 * 2f * Mathf.Pow(2, (root + iv) / 12f), 0.05f);
                    if (s % 2 == 1) Hat(d, ts, 0.035f);
                }
                for (int b = 0; b < 4; b++) Kick(d, t0 + b * beat, b % 2 == 0 ? 0.35f : 0.22f);
            }
            float peak = 0.001f;
            foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= 0.55f / peak;
        }

        void Pad(float[] d, float start, float length, float f, float vol)
        {
            int s0 = (int)(start * Rate), n = (int)(length * Rate);
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate, k = (float)i / n;
                float env = Mathf.Min(1f, k * 6f) * Mathf.Min(1f, (1 - k) * 6f);
                float v = Mathf.Sin(2 * Mathf.PI * f * t) + Mathf.Sin(2 * Mathf.PI * f * 1.006f * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * f * t);
                d[s0 + i] += v * vol * env;
            }
        }

        void Bass(float[] d, float start, float f, float vol)
        {
            int s0 = (int)(start * Rate);
            for (int i = 0; i < (int)(0.28f * Rate) && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float ph = (f * t) % 1f;
                d[s0 + i] += (ph * 2f - 1f) * 0.6f * vol * Env(t, 0.004f, 9f) + Mathf.Sin(2 * Mathf.PI * f * t) * vol * Env(t, 0.004f, 6f);
            }
        }

        void Pluck(float[] d, float start, float f, float vol)
        {
            int s0 = (int)(start * Rate);
            for (int i = 0; i < (int)(0.35f * Rate) && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                d[s0 + i] += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(6 * Mathf.PI * f * t)) * vol * Env(t, 0.003f, 12f);
            }
        }

        void Kick(float[] d, float start, float vol)
        {
            int s0 = (int)(start * Rate);
            float ph = 0;
            for (int i = 0; i < (int)(0.3f * Rate) && s0 + i < d.Length; i++)
            {
                float t = (float)i / Rate;
                ph += 2 * Mathf.PI * (50f + 110f * Mathf.Exp(-t * 30f)) / Rate;
                d[s0 + i] += Mathf.Sin(ph) * vol * Env(t, 0.002f, 14f);
            }
        }

        void Hat(float[] d, float start, float vol)
        {
            int s0 = (int)(start * Rate);
            float prev = 0;
            for (int i = 0; i < Mathf.Min(d.Length - s0, 2500); i++)
            {
                float t = (float)i / Rate, nz = Noise();
                d[s0 + i] += (nz - prev) * vol * Mathf.Exp(-t * 70f);
                prev = nz;
            }
        }
    }
}

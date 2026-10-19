using UnityEngine;

namespace DropZone
{
    /// <summary>
    /// Particle effects, built in code from the materials the builder assigns: landing sparks, the
    /// Bomb blast (kept small and brief so the board stays readable), the Swap swirl and restrained
    /// celebration confetti. Reduced motion uses fewer, shorter particles.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public Material Spark, Smoke, Confetti, Flash;
        public Light FlashLight;

        ParticleSystem sparks, smoke, confetti, ring;

        void Awake()
        {
            sparks = Make("Sparks", Spark, 0.5f, new Vector2(2.5f, 6f), new Vector2(0.04f, 0.1f), 2f, ParticleSystemRenderMode.Stretch);
            smoke = Make("Smoke", Smoke, 0.9f, new Vector2(0.4f, 1.4f), new Vector2(0.5f, 1.1f), -0.15f, ParticleSystemRenderMode.Billboard);
            confetti = Make("Confetti", Confetti, 2.6f, new Vector2(3f, 7f), new Vector2(0.06f, 0.12f), 1.1f, ParticleSystemRenderMode.Billboard);
            var cm = confetti.main; cm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var rot = confetti.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var noise = confetti.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.6f;
            ring = Make("Shockwave", Flash, 0.35f, Vector2.zero, new Vector2(0.4f, 0.4f), 0f, ParticleSystemRenderMode.Billboard);
            var size = ring.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(6f, AnimationCurve.EaseInOut(0, 0.2f, 1, 1));
            if (FlashLight) FlashLight.intensity = 0f;
        }

        ParticleSystem Make(string name, Material mat, float life, Vector2 speed, Vector2 size, float gravity, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.15f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.06f; r.lengthScale = 1.5f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Burst(ParticleSystem ps, Vector3 at, int count, Color a, Color b)
        {
            if (!ps) return;
            var p = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true };
            for (int i = 0; i < count; i++)
            {
                p.startColor = Color.Lerp(a, b, Random.value);
                ps.Emit(p, 1);
            }
        }

        int Scale(int n) => Settings.ReducedMotion ? Mathf.Max(1, n / 3) : Settings.Quality == 0 ? n / 2 : n;

        public void Land(Vector3 at, Color colour)
        {
            Burst(sparks, at + Vector3.back * 0.3f, Scale(10), colour, Color.white);
        }

        public void Blast(Vector3 at)
        {
            var hot = new Color(1f, 0.55f, 0.15f);
            Burst(sparks, at + Vector3.back * 0.4f, Scale(36), hot, new Color(1f, 0.9f, 0.5f));
            Burst(smoke, at + Vector3.back * 0.5f, Scale(8), new Color(0.35f, 0.3f, 0.4f, 0.6f), new Color(0.2f, 0.18f, 0.25f, 0.5f));
            if (!Settings.ReducedMotion) Burst(ring, at + Vector3.back * 0.5f, 1, new Color(1f, 0.6f, 0.25f, 0.8f), new Color(1f, 0.6f, 0.25f, 0.8f));
            if (FlashLight) { FlashLight.transform.position = at + new Vector3(0, 0, -1.5f); FlashLight.intensity = Settings.ReducedMotion ? 3f : 12f; }
        }

        public void SwapSwirl(Vector3 a, Vector3 b, Color ca, Color cb)
        {
            Burst(sparks, a + Vector3.back * 0.4f, Scale(12), ca, Color.white);
            Burst(sparks, b + Vector3.back * 0.4f, Scale(12), cb, Color.white);
        }

        public void Celebrate(Vector3 at, Color a, Color b)
        {
            Burst(confetti, at, Scale(70), a, b);
        }

        public void WinSparkle(Vector3 at, Color colour)
        {
            Burst(sparks, at + Vector3.back * 0.45f, Scale(6), colour, Color.white);
        }

        void Update()
        {
            if (FlashLight && FlashLight.intensity > 0f) FlashLight.intensity = Mathf.MoveTowards(FlashLight.intensity, 0f, Time.deltaTime * 40f);
        }
    }
}

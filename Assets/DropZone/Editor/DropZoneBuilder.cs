using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DropZone.EditorTools
{
    /// <summary>
    /// Builds the whole game from code: project settings, quality presets, textures, materials,
    /// meshes, prefabs, the arena scene, and the desktop / browser players.
    ///   Menu:   DropZone > Build Everything (assets + scene)
    ///   Batch:  Unity -batchmode -nographics -projectPath . -executeMethod DropZone.EditorTools.DropZoneBuilder.GenerateAndBuildLinux -quit
    /// </summary>
    public static class DropZoneBuilder
    {
        public const string Root = "Assets/DropZone/";
        public const string Gen = Root + "Generated/";
        public const string PrefabDir = Root + "Prefabs/";
        public const string ScenePath = Root + "Scenes/DropZone.unity";
        const string LinuxPath = "Build/Linux/DropZone.x86_64";
        public const string WebGLPath = "Build/WebGL";

        // board geometry (shared with BoardView defaults)
        public const float CellSize = 1f, HoleR = 0.43f, FrontZ0 = -0.3f, FrontZ1 = -0.18f, BackZ0 = 0.18f, BackZ1 = 0.3f;
        public static readonly Vector2 FirstCell = new Vector2(-3f, 1.05f);
        public const float CounterR = 0.4f, CounterHalf = 0.11f;

        [MenuItem("DropZone/Build Everything (assets + scene)")]
        public static void GenerateAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ConfigureProject();
            foreach (var d in new[] { Gen, Gen + "Meshes", Gen + "Materials", PrefabDir, Root + "Scenes", Root + "Settings" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            Mats.Make();
            MakePipelines();
            var profile = MakeVolumeProfile();
            var counters = MakeCounters();
            SceneBuilder.Build(profile, counters.one, counters.two);
            AssetDatabase.SaveAssets();
            Debug.Log($"DZ_GENERATE_OK in {sw.Elapsed.TotalSeconds:F1}s");
        }

        [MenuItem("DropZone/Build Linux Player")]
        public static void BuildLinux()
        {
            ConfigureProject();
            Directory.CreateDirectory("Build/Linux");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = LinuxPath,
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
            Debug.Log($"DZ_BUILD_OK size={report.summary.totalSize / (1024 * 1024)}MB");
        }

        public static void GenerateAndBuildLinux() { GenerateAll(); BuildLinux(); }

        [MenuItem("DropZone/Build Web (WebGL) Player")]
        public static void BuildWebGL()
        {
            ConfigureProject();
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;   // works on GitHub Pages (no Content-Encoding headers)
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.template = "PROJECT:DropZone";
            PlayerSettings.runInBackground = false;
            if (Directory.Exists(WebGLPath)) Directory.Delete(WebGLPath, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WebGLPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("WebGL build failed: " + report.summary.result);
            Debug.Log("DZ_WEBGL_OK");
        }

        public static void GenerateAndBuildWebGL() { GenerateAll(); BuildWebGL(); }

        // ------------------------------------------------------------------ project settings

        static void ConfigureProject()
        {
            PlayerSettings.companyName = "Host Hobbit";
            PlayerSettings.productName = "DropZone";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            try { PlayerSettings.SplashScreen.show = false; } catch { }
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = so.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); } // Input System only
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ------------------------------------------------------------------ render pipelines + quality presets

        static void MakePipelines()
        {
            var renderer = Recreate(Root + "Settings/Renderer.asset", () => ScriptableObject.CreateInstance<UniversalRendererData>());
            renderer.renderingMode = RenderingMode.Forward;
            EditorUtility.SetDirty(renderer);

            UniversalRenderPipelineAsset Pipe(string name, int msaa, int shadowRes, bool soft, bool hdr, float scale, int lights)
            {
                string path = Root + $"Settings/URP_{name}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
                var urp = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(urp, path);
                urp.renderScale = scale;
                urp.msaaSampleCount = msaa;
                urp.supportsHDR = hdr;
                urp.shadowDistance = 30f;
                urp.shadowCascadeCount = 1;
                urp.mainLightShadowmapResolution = shadowRes;
                urp.maxAdditionalLightsCount = lights;
                var so = new SerializedObject(urp);
                SetBool(so, "m_SoftShadowsSupported", soft);
                SetInt(so, "m_SoftShadowQuality", soft ? 2 : 1);
                SetBool(so, "m_MainLightShadowsSupported", shadowRes > 0);
                SetBool(so, "m_AdditionalLightShadowsSupported", false);
                SetBool(so, "m_SupportsTerrainHoles", false);
                SetFloat(so, "m_ShadowDepthBias", 0.6f);
                SetFloat(so, "m_ShadowNormalBias", 0.4f);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(urp);
                return urp;
            }
            var low = Pipe("Low", 1, 1024, false, true, 0.8f, 4);
            var med = Pipe("Medium", 2, 2048, true, true, 1f, 6);
            var high = Pipe("High", 4, 4096, true, true, 1f, 8);
            GraphicsSettings.defaultRenderPipeline = high;

            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var list = qs.FindProperty("m_QualitySettings");
            while (list.arraySize < 3) list.InsertArrayElementAtIndex(list.arraySize);
            while (list.arraySize > 3) list.DeleteArrayElementAtIndex(list.arraySize - 1);
            var pipes = new[] { low, med, high };
            string[] names = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = names[i];
                e.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipes[i];
                var v = e.FindPropertyRelative("vSyncCount"); if (v != null) v.intValue = 1;
                var aniso = e.FindPropertyRelative("anisotropicTextures"); if (aniso != null) aniso.intValue = i == 0 ? 0 : 2;
                var soft = e.FindPropertyRelative("softParticles"); if (soft != null) soft.boolValue = false;
                var tex = e.FindPropertyRelative("globalTextureMipmapLimit"); if (tex != null) tex.intValue = i == 0 ? 1 : 0;
            }
            qs.FindProperty("m_CurrentQuality").intValue = 2;
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            if (per != null)
                for (int i = 0; i < per.arraySize; i++)
                {
                    var e = per.GetArrayElementAtIndex(i);
                    string plat = e.FindPropertyRelative("first").stringValue;
                    e.FindPropertyRelative("second").intValue = plat == "WebGL" ? 1 : plat == "Android" || plat == "iPhone" ? 0 : 2;
                }
            qs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        static T Recreate<T>(string path, Func<T> make) where T : UnityEngine.Object
        {
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var a = make();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void SetBool(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
        static void SetFloat(SerializedObject so, string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
        static void SetInt(SerializedObject so, string n, int v) { var p = so.FindProperty(n); if (p == null) return; if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = v; else p.intValue = v; }

        static VolumeProfile MakeVolumeProfile()
        {
            string path = Root + "Settings/ArenaGrade.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.0f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.62f);
            var ca = profile.Add<ColorAdjustments>();
            ca.postExposure.Override(0.35f); ca.contrast.Override(12f); ca.saturation.Override(12f);
            var vig = profile.Add<Vignette>(); vig.intensity.Override(0.3f); vig.smoothness.Override(0.5f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        public static Mesh SaveMesh(Mesh m, string name)
        {
            string path = Gen + "Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>A GameObject with a saved mesh and its materials.</summary>
        public static GameObject MeshObject(string name, MeshKit kit, Transform parent, bool shadows = true)
        {
            var (mesh, mats) = kit.Build(name);
            mesh = SaveMesh(mesh, name);
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        // ------------------------------------------------------------------ counters

        /// <summary>Five-point star outline, counter-clockwise.</summary>
        public static Vector2[] StarOutline(float outer, float inner)
        {
            var pts = new Vector2[10];
            for (int k = 0; k < 10; k++)
            {
                float a = Mathf.PI / 2f + k * Mathf.PI / 5f;
                float r = k % 2 == 0 ? outer : inner;
                pts[k] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return pts;
        }

        static (CounterView one, CounterView two) MakeCounters()
        {
            CounterView Make(string name, Material body, Material rim, Material symbol, bool star)
            {
                var kit = new MeshKit();
                kit.Cylinder(body, new Vector3(0, 0, -CounterHalf), new Vector3(0, 0, CounterHalf), CounterR, 48);
                // raised rims on both faces, like a real counter
                kit.Torus(rim, new Vector3(0, 0, -CounterHalf), Vector3.forward, CounterR - 0.035f, 0.032f, 48, 10);
                kit.Torus(rim, new Vector3(0, 0, CounterHalf), Vector3.forward, CounterR - 0.035f, 0.032f, 48, 10);
                kit.Torus(body, Vector3.zero, Vector3.forward, CounterR - 0.012f, 0.06f, 48, 10); // rounded edge band
                // embossed symbol on both faces
                float s0 = CounterHalf, s1 = CounterHalf + 0.034f;
                if (star)
                {
                    var outline = StarOutline(0.23f, 0.095f);
                    kit.Prism(symbol, outline, -s1, -s0, true, false);
                    kit.Prism(symbol, outline, s0, s1, false, true);
                }
                else
                {
                    kit.Annulus(symbol, Vector3.zero, 0.13f, 0.22f, -s1, -s0, 40);
                    kit.Annulus(symbol, Vector3.zero, 0.13f, 0.22f, s0, s1, 40);
                    kit.Cylinder(symbol, new Vector3(0, 0, -s1), new Vector3(0, 0, -s0), 0.065f, 24);
                    kit.Cylinder(symbol, new Vector3(0, 0, s0), new Vector3(0, 0, s1), 0.065f, 24);
                }
                var root = new GameObject(name);
                var visual = MeshObject(name + " Mesh", kit, root.transform);
                visual.name = "Visual";
                var cv = root.AddComponent<CounterView>();
                cv.Visual = visual.transform;
                string path = PrefabDir + name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                UnityEngine.Object.DestroyImmediate(root);
                return prefab.GetComponent<CounterView>();
            }
            var one = Make("Counter One", Mats.CounterOne, Mats.CounterOneRim, Mats.CounterOneSymbol, true);
            one.Owner = Core.Player.One;
            var two = Make("Counter Two", Mats.CounterTwo, Mats.CounterTwoRim, Mats.CounterTwoSymbol, false);
            two.Owner = Core.Player.Two;
            EditorUtility.SetDirty(one); EditorUtility.SetDirty(two);
            AssetDatabase.SaveAssets();
            return (one, two);
        }
    }

    /// <summary>Every material, made once. URP Lit for solid things, Unlit for glows and highlights.</summary>
    public static class Mats
    {
        static Shader lit, unlit, particles;
        public static Material BoardFront, BoardBack, Frame, HoleRim, TrimCyan, TrimPurple, TrimOrange, Platform, Floor, Wall, WallDark,
            CounterOne, CounterOneRim, CounterOneSymbol, CounterTwo, CounterTwoRim, CounterTwoSymbol,
            Ring, Beam, WinBeam, Spark, Smoke, Confetti, Shock, Backing;

        public static Material Lit(string name, Color colour, Texture2D tex = null, float smooth = 0.5f, float metallic = 0f, Color? emission = null, Texture2D emissionMap = null)
        {
            var m = new Material(lit) { name = name };
            m.SetColor("_BaseColor", colour);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                if (emissionMap) m.SetTexture("_EmissionMap", emissionMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            return Save(m);
        }

        public static Material Unlit(string name, Color colour, Texture2D tex = null, bool transparent = false, bool additive = false)
        {
            var m = new Material(unlit) { name = name };
            m.SetColor("_BaseColor", colour);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", additive ? 2 : 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            return Save(m);
        }

        static Material Particle(string name, Texture2D tex, bool additive)
        {
            var m = new Material(particles) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return Save(m);
        }

        static Material Save(Material m)
        {
            string path = DropZoneBuilder.Gen + "Materials/" + m.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); existing.shaderKeywords = m.shaderKeywords; existing.renderQueue = m.renderQueue; EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void Make()
        {
            lit = Shader.Find("Universal Render Pipeline/Lit");
            unlit = Shader.Find("Universal Render Pipeline/Unlit");
            particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!lit || !unlit || !particles) throw new Exception("URP shaders not found");

            var metal = TextureKit.Metal("Metal");
            var floor = TextureKit.FloorGrid("FloorGrid");
            var wall = TextureKit.WallPanel("WallPanel");
            var soft = TextureKit.Soft("Soft");
            var square = TextureKit.Square("Square");
            var ring = TextureKit.HighlightRing("HighlightRing");
            var shock = TextureKit.Shock("Shock");
            var beam = TextureKit.Beam("Beam");

            BoardFront = Lit("BoardFront", new Color(0.13f, 0.14f, 0.3f), metal, 0.78f, 0.55f);
            BoardBack = Lit("BoardBack", new Color(0.07f, 0.07f, 0.16f), metal, 0.6f, 0.5f);
            Frame = Lit("Frame", new Color(0.17f, 0.18f, 0.34f), metal, 0.72f, 0.7f);
            HoleRim = Lit("HoleRim", new Color(0.15f, 0.5f, 0.7f), null, 0.8f, 0.3f, new Color(0.1f, 0.55f, 0.85f) * 1.1f);
            TrimCyan = Lit("TrimCyan", new Color(0.2f, 0.9f, 1f), null, 0.8f, 0f, new Color(0.2f, 0.9f, 1f) * 1.7f);
            TrimPurple = Lit("TrimPurple", new Color(0.6f, 0.35f, 1f), null, 0.8f, 0f, new Color(0.6f, 0.3f, 1f) * 1.7f);
            TrimOrange = Lit("TrimOrange", new Color(1f, 0.55f, 0.15f), null, 0.8f, 0f, new Color(1f, 0.5f, 0.12f) * 1.6f);
            Platform = Lit("Platform", new Color(0.1f, 0.1f, 0.2f), metal, 0.85f, 0.6f);
            Floor = Lit("ArenaFloor", Color.white, floor, 0.82f, 0.2f, new Color(0.55f, 0.45f, 1f) * 0.35f, floor);
            Floor.mainTextureScale = new Vector2(0.25f, 0.25f);
            Floor.SetTextureScale("_EmissionMap", new Vector2(0.25f, 0.25f));
            EditorUtility.SetDirty(Floor);
            Wall = Lit("ArenaWall", new Color(0.12f, 0.12f, 0.24f), wall, 0.55f, 0.4f);
            WallDark = Lit("ArenaWallDark", new Color(0.05f, 0.05f, 0.11f), null, 0.4f, 0.3f);
            Backing = Lit("BoardBacking", new Color(0.03f, 0.03f, 0.07f), null, 0.3f, 0f, new Color(0.035f, 0.03f, 0.1f));

            // glossy counters with a little self-light so they read clearly on the dark board
            CounterOne = Lit("CounterOne", new Color(1f, 0.5f, 0.1f), null, 0.9f, 0.05f, new Color(0.75f, 0.24f, 0.02f));
            CounterOneRim = Lit("CounterOneRim", new Color(0.95f, 0.38f, 0.06f), null, 0.92f, 0.1f, new Color(0.5f, 0.15f, 0f));
            CounterOneSymbol = Lit("CounterOneSymbol", new Color(1f, 0.86f, 0.5f), null, 0.92f, 0.1f, new Color(0.9f, 0.55f, 0.15f));
            CounterTwo = Lit("CounterTwo", new Color(0.08f, 0.68f, 1f), null, 0.9f, 0.05f, new Color(0f, 0.3f, 0.5f));
            CounterTwoRim = Lit("CounterTwoRim", new Color(0.04f, 0.42f, 0.75f), null, 0.92f, 0.1f, new Color(0f, 0.1f, 0.2f));
            CounterTwoSymbol = Lit("CounterTwoSymbol", new Color(0.85f, 1f, 1f), null, 0.92f, 0.1f, new Color(0.35f, 0.85f, 1f));

            Ring = Unlit("HighlightRing", Color.white, ring, true, true);
            Beam = Unlit("ColumnBeam", new Color(1f, 1f, 1f, 0.25f), beam, true, true);
            WinBeam = Unlit("WinBeam", Color.white * 2f);
            Spark = Particle("Spark", soft, true);
            Smoke = Particle("Smoke", soft, false);
            Confetti = Particle("Confetti", square, false);
            Shock = Particle("Shock", shock, true);
            AssetDatabase.SaveAssets();
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DropZone.Core;
using static DropZone.EditorTools.DropZoneBuilder;

namespace DropZone.EditorTools
{
    /// <summary>Assembles the arena scene: board, stand, arena, lights, camera, effects and game objects.</summary>
    public static class SceneBuilder
    {
        public static void Build(VolumeProfile profile, CounterView counterOne, CounterView counterTwo)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------- lighting ----------
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.16f, 0.32f);
            RenderSettings.ambientEquatorColor = new Color(0.1f, 0.08f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.fogColor = new Color(0.03f, 0.02f, 0.08f);
            RenderSettings.skybox = null;

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.88f, 0.9f, 1f);
            key.intensity = 1.25f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;
            key.transform.rotation = Quaternion.Euler(38f, 18f, 0f);

            Point("Cyan Light", new Vector3(-7.5f, 5.5f, -3.5f), new Color(0.2f, 0.85f, 1f), 3.2f, 18f);
            Point("Purple Light", new Vector3(7.5f, 5.5f, -3f), new Color(0.65f, 0.3f, 1f), 3.4f, 18f);
            Point("Floor Cyan", new Vector3(-5f, 0.6f, -4f), new Color(0.2f, 0.85f, 1f), 1.2f, 9f);
            Point("Floor Purple", new Vector3(5f, 0.6f, -4f), new Color(0.65f, 0.3f, 1f), 1.2f, 9f);
            var rim = new GameObject("Warm Rim Light").AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(1f, 0.55f, 0.2f);
            rim.intensity = 7f;
            rim.range = 22f;
            rim.spotAngle = 70f;
            rim.transform.position = new Vector3(0f, 11f, 6f);
            rim.transform.LookAt(new Vector3(0f, 3.5f, 0f));

            // ---------- board ----------
            var board = new GameObject("Board");
            var view = board.AddComponent<BoardView>();
            view.Origin = new Vector3(FirstCell.x, FirstCell.y, 0f);
            view.CellSize = CellSize;
            view.PreviewY = FirstCell.y + 5f * CellSize + 1.35f;
            view.MarkerZ = -0.42f;
            BuildBoardMeshes(board.transform);

            var ringKit = new MeshKit();
            ringKit.FrontQuad(Mats.Ring, Vector3.zero, new Vector2(1f, 1f));
            var ringGo = MeshObject("Ring Template", ringKit, board.transform, false);
            var beamKit = new MeshKit();
            beamKit.FrontQuad(Mats.Beam, Vector3.zero, new Vector2(0.92f, 6.3f));
            var beamGo = MeshObject("Column Beam", beamKit, board.transform, false);
            beamGo.transform.localPosition = new Vector3(0f, FirstCell.y + 2.5f + 0.4f, FrontZ0 - 0.035f);
            var winKit = new MeshKit();
            winKit.Box(Mats.WinBeam, Vector3.zero, new Vector3(1f, 0.16f, 0.06f));
            var winGo = MeshObject("Win Beam Template", winKit, board.transform, false);
            view.CounterOne = counterOne;
            view.CounterTwo = counterTwo;
            view.Ring = ringGo.transform;
            view.ColumnBeam = beamGo.transform;
            view.WinBeam = winGo.transform;

            BuildArena();

            // ---------- effects ----------
            var fxGo = new GameObject("Effects");
            var fx = fxGo.AddComponent<Fx>();
            fx.Spark = Mats.Spark; fx.Smoke = Mats.Smoke; fx.Confetti = Mats.Confetti; fx.Flash = Mats.Shock;
            var flash = new GameObject("Blast Light").AddComponent<Light>();
            flash.transform.SetParent(fxGo.transform, false);
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.6f, 0.25f);
            flash.range = 7f;
            flash.intensity = 0f;
            fx.FlashLight = flash;
            view.Effects = fx;

            // ---------- camera + post ----------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.015f, 0.05f);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<CameraRig>();
            rig.Focus = new Vector3(0f, 3.95f, 0f);
            rig.Frame = new Vector2(8.7f, 7.9f);
            camGo.transform.position = new Vector3(0f, 5.6f, -16f);
            camGo.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
            view.Rig = rig;

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // ---------- game ----------
            var game = new GameObject("Game");
            game.AddComponent<Sfx>();
            var ui = game.AddComponent<UIManager>();
            var gc = game.AddComponent<GameController>();
            gc.View = view; gc.Rig = rig; gc.Cam = cam; gc.Ui = ui;
            new GameObject("QA").AddComponent<QaDriver>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("DZ_SCENE ok");
        }

        static Light Point(string name, Vector3 pos, Color c, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            l.transform.position = pos;
            return l;
        }

        static void BuildBoardMeshes(Transform board)
        {
            float gridL = FirstCell.x - 0.5f * CellSize, gridR = FirstCell.x + 6.5f * CellSize;
            float gridB = FirstCell.y - 0.5f * CellSize, gridT = FirstCell.y + 5.5f * CellSize;
            const float border = 0.36f, topBar = 0.28f, bottomBar = 0.36f;

            // front face with holes, glowing hole rims, and its border
            var front = new MeshKit();
            front.HolePlate(Mats.BoardFront, Board.Columns, Board.Rows, CellSize, HoleR, FirstCell, FrontZ0, FrontZ1);
            void Border(MeshKit k, Material m, float z0, float z1)
            {
                float zc = (z0 + z1) * 0.5f, d = z1 - z0;
                k.Box(m, new Vector3(gridL - border * 0.5f, (gridB - bottomBar + gridT + topBar) * 0.5f, zc), new Vector3(border, gridT - gridB + topBar + bottomBar, d), 0.5f, true);
                k.Box(m, new Vector3(gridR + border * 0.5f, (gridB - bottomBar + gridT + topBar) * 0.5f, zc), new Vector3(border, gridT - gridB + topBar + bottomBar, d), 0.5f, true);
                k.Box(m, new Vector3(0f, gridB - bottomBar * 0.5f, zc), new Vector3(gridR - gridL, bottomBar, d), 0.5f, true);
                k.Box(m, new Vector3(0f, gridT + topBar * 0.5f, zc), new Vector3(gridR - gridL, topBar, d), 0.5f, true);
            }
            Border(front, Mats.BoardFront, FrontZ0, FrontZ1);
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                    front.Torus(Mats.HoleRim, new Vector3(FirstCell.x + c * CellSize, FirstCell.y + r * CellSize, FrontZ0 - 0.004f), Vector3.forward, HoleR + 0.012f, 0.022f, 32, 6);
            MeshObject("Front Plate", front, board);

            var back = new MeshKit();
            back.HolePlate(Mats.BoardBack, Board.Columns, Board.Rows, CellSize, HoleR, FirstCell, BackZ0, BackZ1);
            Border(back, Mats.BoardBack, BackZ0, BackZ1);
            // dark backing behind the grid so empty slots read as empty (not the floor showing through)
            back.Box(Mats.Backing, new Vector3(0f, (gridB + gridT) * 0.5f, BackZ1 + 0.03f), new Vector3(gridR - gridL + 0.1f, gridT - gridB + 0.1f, 0.05f));
            MeshObject("Back Plate", back, board);

            // frame: side pillars, bottom tray, feet, glowing trims
            var frame = new MeshKit();
            float pillarX = gridR + border + 0.24f;
            float top = gridT + topBar;
            foreach (float s in new[] { -1f, 1f })
            {
                frame.Box(Mats.Frame, new Vector3(s * pillarX, (top + 0.35f) * 0.5f, 0f), new Vector3(0.48f, top + 0.35f, 0.86f), 0.5f);
                frame.Box(Mats.Frame, new Vector3(s * pillarX, 0.16f, 0f), new Vector3(1.0f, 0.32f, 2.6f), 0.5f);              // foot
                frame.Box(Mats.Frame, new Vector3(s * pillarX, top + 0.42f, 0f), new Vector3(0.62f, 0.14f, 0.96f), 0.5f);         // cap
                frame.Box(s < 0 ? Mats.TrimCyan : Mats.TrimPurple, new Vector3(s * pillarX, top * 0.5f + 0.2f, -0.435f), new Vector3(0.07f, top - 0.6f, 0.02f));
                frame.Box(s < 0 ? Mats.TrimCyan : Mats.TrimPurple, new Vector3(s * pillarX, 0.33f, 0f), new Vector3(1.02f, 0.025f, 2.5f));
                // braces from foot to pillar
                frame.BoxRotated(Mats.Frame, new Vector3(s * pillarX, 0.75f, -0.75f), new Vector3(0.22f, 0.22f, 1.3f), Quaternion.Euler(-40f, 0f, 0f), 0.5f);
                frame.BoxRotated(Mats.Frame, new Vector3(s * pillarX, 0.75f, 0.75f), new Vector3(0.22f, 0.22f, 1.3f), Quaternion.Euler(40f, 0f, 0f), 0.5f);
            }
            frame.Box(Mats.Frame, new Vector3(0f, gridB - bottomBar * 0.5f, 0f), new Vector3(gridR - gridL + border * 2f, bottomBar, BackZ0 - FrontZ1 + 0.02f), 0.5f); // tray between plates
            frame.Box(Mats.TrimOrange, new Vector3(0f, top + 0.012f, (FrontZ0 + FrontZ1) * 0.5f), new Vector3(gridR - gridL, 0.025f, FrontZ1 - FrontZ0 + 0.01f));
            frame.Box(Mats.TrimOrange, new Vector3(0f, gridB - bottomBar - 0.02f, FrontZ0 - 0.01f), new Vector3(gridR - gridL + border * 2f, 0.04f, 0.02f));
            MeshObject("Frame", frame, board);

            // platform under the board
            var plat = new MeshKit();
            plat.Cylinder(Mats.Platform, new Vector3(0f, -0.15f, 0f), new Vector3(0f, 0.0f, 0f), 6.6f, 64);
            plat.Torus(Mats.TrimOrange, new Vector3(0f, 0.0f, 0f), Vector3.up, 6.6f, 0.045f, 96, 6);
            plat.Torus(Mats.TrimCyan, new Vector3(0f, 0.005f, 0f), Vector3.up, 5.1f, 0.025f, 96, 6);
            MeshObject("Platform", plat, board);
        }

        static void BuildArena()
        {
            var arena = new GameObject("Arena");
            var floor = new MeshKit();
            floor.Plane(Mats.Floor, new Vector3(0f, -0.15f, 10f), new Vector2(90f, 90f), Vector3.up, Vector3.forward, false);
            var f = MeshObject("Floor", floor, arena.transform, false);
            f.GetComponent<MeshRenderer>().receiveShadows = true;

            // a curved wall of tall panels behind the board, with glowing seams
            var wall = new MeshKit();
            var centre = new Vector3(0f, 0f, 2f);
            const float radius = 21f;
            for (int i = -8; i <= 8; i++)
            {
                float ang = i * 10f;
                var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                var pos = centre + dir * radius;
                var rot = Quaternion.Euler(0f, ang, 0f);
                wall.BoxRotated(Mats.Wall, pos + Vector3.up * 7f, new Vector3(3.55f, 14.4f, 0.6f), rot, 0.25f);
                var strip = i % 2 == 0 ? Mats.TrimPurple : Mats.TrimCyan;
                wall.BoxRotated(strip, pos + Vector3.up * 5.5f - dir * 0.32f + rot * Vector3.right * 1.78f, new Vector3(0.06f, 9f, 0.04f), rot);
                if (i % 3 == 0) wall.BoxRotated(Mats.TrimOrange, pos + Vector3.up * 1.2f - dir * 0.32f, new Vector3(3.2f, 0.05f, 0.04f), rot);
            }
            MeshObject("Back Wall", wall, arena.transform, false);

            // a huge glowing portal ring framing the board
            var ring = new MeshKit();
            ring.Torus(Mats.TrimPurple, new Vector3(0f, 4.2f, 11f), Vector3.forward, 8.2f, 0.16f, 128, 10);
            ring.Torus(Mats.TrimCyan, new Vector3(0f, 4.2f, 11.3f), Vector3.forward, 9.1f, 0.05f, 128, 6);
            ring.Torus(Mats.WallDark, new Vector3(0f, 4.2f, 11.6f), Vector3.forward, 8.65f, 0.55f, 96, 10);
            MeshObject("Portal Ring", ring, arena.transform, false);

            // light towers at the sides
            var towers = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 2; k++)
                {
                    var p = new Vector3(s * (10.5f + k * 4f), 0f, 1f + k * 5f);
                    towers.Box(Mats.WallDark, p + Vector3.up * 5f, new Vector3(0.9f, 10f, 0.9f), 0.5f);
                    towers.Box(k == 0 ? Mats.TrimCyan : Mats.TrimPurple, p + Vector3.up * 5f + new Vector3(-s * 0.46f, 0f, 0f), new Vector3(0.03f, 9.2f, 0.12f));
                    towers.Box(Mats.TrimOrange, p + Vector3.up * 10.05f, new Vector3(0.95f, 0.08f, 0.95f));
                }
            MeshObject("Light Towers", towers, arena.transform, false);
            foreach (Transform t in arena.transform) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
    }
}

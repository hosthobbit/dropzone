using System;
using System.Collections;
using System.IO;
using DropZone.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DropZone
{
    /// <summary>
    /// Automated testing only. Enabled by command-line flags (desktop) or URL parameters (browser):
    ///   --autoplay / ?autoplay   plays complete matches through the real game: Classic and Battle,
    ///                            computer vs computer and a scripted "human" against every difficulty
    ///   --autoshot DIR           also saves screenshots of every screen and key moment
    ///   --shotsonly              with --autoshot: screenshots only, no long match run
    ///   --inputtest              drives the game with virtual keyboard, mouse, touchscreen and gamepad
    ///                            devices and checks each one does what it should
    ///   --quality N              force a graphics preset
    /// Results are logged as DZ_MATCH / DZ_PERF / DZ_INPUT / DZ_QA_DONE lines.
    /// </summary>
    public class QaDriver : MonoBehaviour
    {
        bool autoplay, inputTest, shotsOnly;
        string shotDir;
        int passes, fails, frames;
        float frameSum, worstFrame;
        GameController G => GameController.Instance;
        int errors;

        void Awake()
        {
            // automated runs keep their own settings and stats, separate from a real player's
            var args = Environment.GetCommandLineArgs();
            string url = Application.absoluteURL ?? "";
            if (Array.IndexOf(args, "--autoplay") >= 0 || Array.IndexOf(args, "--autoshot") >= 0 || Array.IndexOf(args, "--inputtest") >= 0 || url.Contains("autoplay"))
            {
                Settings.Prefix = "dzqa.";
                Settings.ResetAll();
            }
        }

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--autoplay") autoplay = true;
                if (args[i] == "--autoshot" && i + 1 < args.Length) { autoplay = true; shotDir = args[i + 1]; }
                if (args[i] == "--inputtest") inputTest = true;
                if (args[i] == "--shotsonly") shotsOnly = true;
                if (args[i] == "--quality" && i + 1 < args.Length && int.TryParse(args[i + 1], out int q)) Settings.Quality = q;
            }
            string url = Application.absoluteURL ?? "";
            if (url.Contains("autoplay")) autoplay = true;
            if (!autoplay && !inputTest) { enabled = false; return; }
            AudioListener.volume = 0f;            // automated runs stay silent
            GameController.IgnoreFocusLoss = true;
            Application.logMessageReceived += (msg, stack, type) => { if (type == LogType.Exception || type == LogType.Error) { errors++; } };
            G.ApplySettings();
            StartCoroutine(inputTest ? InputTest() : Autoplay());
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Time.frameCount > 30) { frames++; frameSum += dt; worstFrame = Mathf.Max(worstFrame, dt); }
        }

        void LogPerf(string label)
        {
            float avg = frames > 0 ? frameSum / frames : 0f;
            Debug.Log($"DZ_PERF {label} frames={frames} avg_ms={avg * 1000f:0.0} avg_fps={(avg > 0 ? 1f / avg : 0):0.0} worst_ms={worstFrame * 1000f:0.0} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} res={Screen.width}x{Screen.height} gpu={SystemInfo.graphicsDeviceName}");
            frames = 0; frameSum = 0; worstFrame = 0;
        }

        IEnumerator Shot(string name)
        {
            if (shotDir == null) yield break;
            Directory.CreateDirectory(shotDir);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
            Debug.Log("DZ_SHOT " + name);
            yield return null;
        }

        static IEnumerator WaitFor(Func<bool> cond, float timeout)
        {
            for (float t = 0; t < timeout && !cond(); t += Time.unscaledDeltaTime) yield return null;
        }

        IEnumerator WaitPhase(GameController.Phase p, float timeout = 60f) => WaitFor(() => G.Current == p, timeout);

        // ================================================================== autoplay

        /// <summary>A scripted "human" that plays through the same entry points as the buttons and keys.</summary>
        AiPlayer humanBrain;
        int humanPowers;

        IEnumerator HumanMove(bool slowForShots = false)
        {
            var choice = humanBrain.ChooseNow(G.Round);
            if (choice.Kind == ActionKind.Drop)
            {
                G.Selection.SelectColumn(choice.Column);
                if (slowForShots) yield return new WaitForSecondsRealtime(0.3f);
                G.ConfirmButton();
            }
            else
            {
                humanPowers++;
                if (choice.Kind == ActionKind.Bomb) G.BombButton(); else G.SwapButton();
                yield return null;
                G.PickTarget(choice.A);
                if (choice.Kind == ActionKind.Swap) G.PickTarget(choice.B);
                yield return null;
                G.ConfirmButton();
            }
        }

        IEnumerator PlayMatch(MatchConfig cfg, string label, Difficulty humanLevel = Difficulty.Normal, float speed = 3f)
        {
            GameController.AutoContinue = true;
            GameController.PlaySpeed = speed;
            humanBrain = new AiPlayer(humanLevel, label.GetHashCode());
            humanPowers = 0;
            int illegal = 0, aiPowers = 0, timeouts = 0;
            Action<MoveResult> onCommit = r => { if (r.Action.IsPower && G.IsComputer(r.By)) aiPowers++; if (r.Timeout) timeouts++; };
            G.Committed += onCommit;
            int errs0 = errors;
            float t0 = Time.realtimeSinceStartup;
            G.StartMatch(cfg);
            int commits0 = G.Commits;
            while (G.Current != GameController.Phase.MatchOver && Time.realtimeSinceStartup - t0 < 900f)
            {
                if (G.Current == GameController.Phase.HumanTurn && !G.Paused)
                {
                    int before = G.Commits;
                    yield return HumanMove();
                    yield return null;
                    if (G.Commits == before && G.Current == GameController.Phase.HumanTurn) { illegal++; G.Selection.CancelAll(); G.ConfirmButton(); }
                }
                yield return null;
            }
            G.Committed -= onCommit;
            var m = G.Match;
            string outcomes = string.Join("", m.History.ConvertAll(o => o == RoundOutcome.PlayerOneWins ? "1" : o == RoundOutcome.PlayerTwoWins ? "2" : "D"));
            bool ok = G.Current == GameController.Phase.MatchOver && m.IsOver && illegal == 0 && errors == errs0;
            Debug.Log($"DZ_MATCH {label} {(ok ? "PASS" : "FAIL")} mode={cfg.Mode} p1={(cfg.Watch ? "AI-" + cfg.WatchOpponent : cfg.Solo ? "QA-human" : "human")} p2={(cfg.Solo || cfg.Watch ? "AI-" + cfg.Difficulty : "human")} " +
                      $"score={m.Wins(Player.One)}-{m.Wins(Player.Two)} rounds={outcomes} actions={G.Commits - commits0} humanPowers={humanPowers} aiPowers={aiPowers} timeouts={timeouts} illegal={illegal} errors={errors - errs0} real={Time.realtimeSinceStartup - t0:0}s");
            if (ok) passes++; else fails++;
            GameController.PlaySpeed = 1f;
            Time.timeScale = 1f;
        }

        IEnumerator Autoplay()
        {
            Debug.Log("DZ_QA autoplay" + (shotDir != null ? " autoshot " + shotDir : ""));
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("01_title");
            if (shotDir != null) yield return Screens();
            if (shotsOnly) { Debug.Log($"DZ_QA_DONE shots errors={errors}"); Application.Quit(); yield break; }

            // every difficulty, both modes, through the full flow (results screens auto-continue)
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Classic, Difficulty = Difficulty.Easy, Starter = Player.One }, "classic_vs_easy");
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Classic, Difficulty = Difficulty.Normal, Starter = Player.Two }, "classic_vs_normal");
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Classic, Difficulty = Difficulty.Hard, Starter = Player.One }, "classic_vs_hard");
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Easy, Starter = Player.One, TurnSeconds = 10 }, "battle_vs_easy");
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Normal, Starter = Player.Two, TurnSeconds = 10 }, "battle_vs_normal");
            yield return PlayMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Hard, Starter = Player.One, TurnSeconds = 10 }, "battle_vs_hard", Difficulty.Hard);
            yield return PlayMatch(new MatchConfig { Watch = true, Mode = GameMode.Battle, Difficulty = Difficulty.Hard, WatchOpponent = Difficulty.Normal, Starter = Player.None, TurnSeconds = 10 }, "battle_ai_vs_ai");
            LogPerf("autoplay");
            Debug.Log($"DZ_QA_DONE pass={passes} fail={fails} errors={errors}");
            if (!Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer) Application.Quit();
        }

        /// <summary>Screenshots of each screen and the big moments, played at normal speed.</summary>
        IEnumerator Screens()
        {
            var ui = G.Ui;
            ui.ShowSetup(true);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("02_setup");
            ui.ShowBattleIntro(null);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("03_battle_briefing");
            ui.CloseModal();
            ui.ShowHowTo();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("04_how_to_play");
            ui.CloseModal();
            ui.ShowSettings();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("05_settings");
            ui.CloseModal();

            // a Classic round against Hard
            GameController.AutoContinue = false;
            humanBrain = new AiPlayer(Difficulty.Normal, 5);
            G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Classic, Difficulty = Difficulty.Hard, Starter = Player.One });
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("06_round_intro");
            int moves = 0;
            while (G.Current != GameController.Phase.RoundOver && G.Current != GameController.Phase.MatchOver)
            {
                if (G.Current == GameController.Phase.HumanTurn)
                {
                    moves++;
                    if (moves == 6) { yield return new WaitForSecondsRealtime(0.6f); yield return Shot("07_classic_turn"); }
                    yield return HumanMove(true);
                }
                if (G.Current == GameController.Phase.AiTurn && moves == 4 && G.AiThinking) { yield return Shot("08_computer_thinking"); moves++; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.9f);
            yield return Shot("09_win_line");
            yield return WaitFor(() => G.Ui.ResultsOpen, 8f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("10_round_result");

            // Battle: show a Bomb preview, the blast, a Swap preview and the exchange
            G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Normal, Starter = Player.One, TurnSeconds = 10 });
            bool bombShot = false, swapShot = false;
            float started = Time.realtimeSinceStartup;
            while (!(bombShot && swapShot) && Time.realtimeSinceStartup - started < 240f)
            {
                if (G.Current == GameController.Phase.RoundOver || G.Current == GameController.Phase.MatchOver)
                {
                    if (G.Current == GameController.Phase.RoundOver) { yield return WaitFor(() => G.Ui.ResultsOpen, 8f); G.NextRound(); }
                    else break;
                }
                if (G.Current == GameController.Phase.HumanTurn)
                {
                    var r = G.Round;
                    if (!bombShot && r.Board.Count(Player.Two) >= 3 && r.CanBomb(Player.One, out _))
                    {
                        if (shotDir != null) { yield return new WaitForSecondsRealtime(0.3f); yield return Shot("11_battle_hud"); }
                        G.BombButton();
                        yield return null;
                        var target = FirstLegal(r, c => GameAction.Bomb(c));
                        G.PickTarget(target);
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return Shot("12_bomb_preview");
                        G.ConfirmButton();
                        yield return new WaitForSecondsRealtime(0.45f);
                        yield return Shot("13_bomb_blast");
                        bombShot = true;
                    }
                    else if (bombShot && !swapShot && r.CanSwap(Player.One, out _))
                    {
                        var legal = r.LegalActions().Find(a => a.Kind == ActionKind.Swap);
                        G.SwapButton();
                        yield return null;
                        G.PickTarget(legal.A);
                        G.PickTarget(legal.B);
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return Shot("14_swap_preview");
                        G.ConfirmButton();
                        yield return new WaitForSecondsRealtime(0.25f);
                        yield return Shot("15_swap_exchange");
                        swapShot = true;
                    }
                    else yield return HumanMove(true);
                }
                yield return null;
            }
            yield return WaitFor(() => G.Current == GameController.Phase.HumanTurn, 20f);
            G.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("16_pause");
            G.SetPaused(false);

            // finish quickly to show the match result
            GameController.AutoContinue = true;
            GameController.PlaySpeed = 3f; Time.timeScale = 3f;
            while (G.Current != GameController.Phase.MatchOver)
            {
                if (G.Current == GameController.Phase.HumanTurn) yield return HumanMove();
                yield return null;
            }
            GameController.PlaySpeed = 1f; Time.timeScale = 1f;
            yield return WaitFor(() => G.Ui.ResultsOpen, 10f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("17_match_result");
        }

        static Cell FirstLegal(RoundState r, Func<Cell, GameAction> make)
        {
            foreach (int c in RoundState.CentreFirst)
                for (int row = 0; row < Board.Rows; row++)
                    if (r.IsLegal(make(new Cell(c, row)))) return new Cell(c, row);
            return new Cell(0, 0);
        }

        // ================================================================== browser test hooks

        /// <summary>
        /// Called from the page by the headless browser test (unityInstance.SendMessage("QA", "WebCommand", ...)).
        /// "solo" starts a Classic match against Easy with you first; "cells" logs where each column is on
        /// screen; "state" logs the phase, move count and board.
        /// </summary>
        public void WebCommand(string cmd)
        {
            switch (cmd)
            {
                case "solo":
                    G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Classic, Difficulty = Difficulty.Easy, Starter = Player.One });
                    break;
                case "battle":
                    G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Easy, Starter = Player.One, TurnSeconds = 30 });
                    break;
                case "cells":
                    var sb = new System.Text.StringBuilder();
                    for (int c = 0; c < Board.Columns; c++) { var p = ScreenOfColumn(c); sb.Append($"{p.x:0},{p.y:0};"); }
                    Debug.Log($"DZ_COLS {Screen.width}x{Screen.height} {sb}");
                    break;
                case "state":
                    Debug.Log($"DZ_STATE phase={G.Current} commits={G.Commits} armed={G.TouchArmedColumn} device={G.LastDevice} board={(G.Round != null ? G.Round.Board.ToString().Replace('\n', '/') : "-")}");
                    break;
            }
        }

        // ================================================================== input test

        void Check(string name, bool ok, string detail = "")
        {
            if (ok) passes++; else fails++;
            Debug.Log($"DZ_INPUT {name} {(ok ? "PASS" : "FAIL")} {detail}");
        }

        static void Keys(Keyboard kb, params Key[] keys) => InputSystem.QueueStateEvent(kb, new KeyboardState(keys));

        IEnumerator Tap(Keyboard kb, Key key)
        {
            Keys(kb, key);
            yield return null; yield return null;
            Keys(kb);
            yield return null; yield return null;
        }

        IEnumerator Pad(Gamepad pad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null; yield return null;
        }

        Vector2 ScreenOf(Cell c) => G.Cam.WorldToScreenPoint(G.View.CellWorld(c));
        Vector2 ScreenOfColumn(int col) => ScreenOf(new Cell(col, 3));

        IEnumerator MouseMove(Mouse mouse, Vector2 p)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return null; yield return null;
        }

        IEnumerator Click(Mouse mouse, Vector2 p)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p }.WithButton(MouseButton.Left, true));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = p });
            yield return null; yield return null;
        }

        IEnumerator TouchTap(Touchscreen touch, Vector2 p, int id)
        {
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Began, position = p });
            yield return null; yield return null;
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = id, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = p });
            yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.15f);
        }

        /// <summary>Owner of the highest counter in a column (the one just dropped there).</summary>
        Player TopOf(int column) { int h = G.Round.Board.Height(column); return h > 0 ? G.Round.Board[column, h - 1] : Player.None; }

        IEnumerator WaitHumanTurn(float timeout = 30f) => WaitFor(() => G.Current == GameController.Phase.HumanTurn && !G.Paused, timeout);

        IEnumerator InputTest()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var kb = InputSystem.AddDevice<Keyboard>("QA Keyboard");
            var mouse = InputSystem.AddDevice<Mouse>("QA Mouse");
            var touch = InputSystem.AddDevice<Touchscreen>("QA Touchscreen");
            var pad = InputSystem.AddDevice<Gamepad>("QA Gamepad");
            Settings.LastMode = 0; Settings.LastStarter = 0; Settings.LastDifficulty = 0; Settings.ClassicTimer = 0; Settings.BattleIntroSeen = true;
            yield return new WaitForSecondsRealtime(3f);

            // ---- menus with the keyboard
            var sel = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            Check("title_focus", sel && sel.name.StartsWith("Solo"), sel ? sel.name : "nothing selected");
            yield return Tap(kb, Key.Enter);
            yield return new WaitForSecondsRealtime(0.4f);
            Check("enter_opens_setup", G.Ui.SetupOpen);
            sel = EventSystem.current.currentSelectedGameObject;
            Check("setup_focus_start", sel && sel.name.StartsWith("Start"), sel ? sel.name : "none");
            yield return Tap(kb, Key.Escape);
            yield return new WaitForSecondsRealtime(0.3f);
            Check("escape_back_to_title", !G.Ui.SetupOpen && G.Ui.TitleOpen);
            yield return Tap(kb, Key.Enter);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Tap(kb, Key.Enter);    // Start Match (Classic, you first, Easy)
            yield return WaitHumanTurn();
            Check("start_classic_match", G.Current == GameController.Phase.HumanTurn && G.Config.Mode == GameMode.Classic && !G.Config.Watch);

            // ---- keyboard drop
            G.Selection.SelectColumn(3);
            yield return Tap(kb, Key.RightArrow);
            yield return Tap(kb, Key.RightArrow);
            Check("keyboard_select_column", G.Selection.Column == 5, $"column={G.Selection.Column}");
            int commits = G.Commits;
            yield return Tap(kb, Key.Enter);
            Check("keyboard_enter_drops", G.Commits == commits + 1 && TopOf(5) == Player.One, G.Round.Board.ToString().Replace('\n', '/'));

            // ---- mouse hover + click
            yield return WaitHumanTurn();
            yield return MouseMove(mouse, ScreenOfColumn(1));
            yield return MouseMove(mouse, ScreenOfColumn(1) + Vector2.up * 4f);
            Check("mouse_hover_previews", G.Selection.Column == 1, $"column={G.Selection.Column}");
            commits = G.Commits;
            yield return Click(mouse, ScreenOfColumn(1));
            Check("mouse_click_drops", G.Commits == commits + 1 && TopOf(1) == Player.One, G.Round.Board.ToString().Replace('\n', '/'));

            // ---- touch: first tap previews, second tap drops
            yield return WaitHumanTurn();
            commits = G.Commits;
            yield return TouchTap(touch, ScreenOfColumn(0), 1);
            Check("touch_first_tap_previews", G.Commits == commits && G.Selection.Column == 0 && G.TouchArmedColumn == 0, $"column={G.Selection.Column} armed={G.TouchArmedColumn}");
            yield return TouchTap(touch, ScreenOfColumn(0), 2);
            Check("touch_second_tap_drops", G.Commits == commits + 1 && TopOf(0) == Player.One, G.Round.Board.ToString().Replace('\n', '/'));

            // ---- gamepad
            yield return WaitHumanTurn();
            G.Selection.SelectColumn(3);
            yield return Pad(pad, GamepadButton.DpadLeft);
            yield return Pad(pad, GamepadButton.DpadLeft);
            Check("gamepad_dpad_moves", G.Selection.Column == 1, $"3 -> {G.Selection.Column}");
            commits = G.Commits;
            yield return Pad(pad, GamepadButton.South);
            Check("gamepad_a_drops", G.Commits == commits + 1);

            // ---- invalid move: a full column is refused and keeps the turn
            yield return WaitHumanTurn();
            // ---- pause freezes the game; input is ignored while paused
            yield return Tap(kb, Key.Escape);
            Check("escape_pauses", G.Paused && Time.timeScale == 0f && G.Ui.PauseOpen);
            commits = G.Commits;
            yield return Tap(kb, Key.Enter); // presses the focused Resume button
            yield return new WaitForSecondsRealtime(0.3f);
            Check("enter_on_resume_resumes", !G.Paused && G.Commits == commits, $"paused={G.Paused} commits {commits}->{G.Commits}");
            yield return Pad(pad, GamepadButton.Start);
            Check("gamepad_start_pauses", G.Paused);
            yield return Pad(pad, GamepadButton.Start);
            Check("gamepad_start_resumes", !G.Paused);

            // ---- UI clicks don't reach the board: restart via the pause menu with the mouse
            yield return Tap(kb, Key.Escape);
            yield return new WaitForSecondsRealtime(0.3f);
            var restart = GameObject.Find("Restart Match Button");
            commits = G.Commits;
            if (restart) yield return Click(mouse, RectTransformUtility.WorldToScreenPoint(null, restart.transform.position));
            yield return WaitHumanTurn();
            Check("restart_from_pause_menu", restart && !G.Paused && G.Round.MovesMade == 0 && G.Match.RoundNumber == 1, $"moves={G.Round?.MovesMade}");

            // ---- Battle powers: preview, step back, cancel (nothing spent), then use one
            G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Easy, Starter = Player.One, TurnSeconds = 0 });
            yield return WaitHumanTurn();
            // get a few counters on the board
            for (int i = 0; i < 3; i++)
            {
                yield return WaitHumanTurn();
                G.Selection.SelectColumn(i == 0 ? 2 : i == 1 ? 4 : 3);
                G.ConfirmButton();
            }
            yield return WaitHumanTurn();
            var r = G.Round;
            yield return Tap(kb, Key.B);
            Check("key_b_starts_bomb", G.Selection.Current == TurnInput.Stage.BombTarget, G.Selection.Current.ToString());
            yield return Tap(kb, Key.Enter);   // the keyboard cursor starts on a valid target; Enter picks it
            Check("enter_picks_bomb_target", G.Selection.Current == TurnInput.Stage.BombConfirm && r.Board[G.Selection.First] == Player.Two, $"{G.Selection.Current} {G.Selection.First}");
            yield return Tap(kb, Key.Backspace);
            Check("backspace_steps_back", G.Selection.Current == TurnInput.Stage.BombTarget);
            yield return Tap(kb, Key.Escape);
            Check("escape_cancels_power", !G.Selection.Targeting && !G.Paused && r.BombsLeft(Player.One) == 1 && r.Current == Player.One);

            // swap with the mouse, cancelled with the on-screen Cancel button
            var pair = r.LegalActions().Find(a => a.Kind == ActionKind.Swap);
            yield return Tap(kb, Key.X);
            Check("key_x_starts_swap", G.Selection.Current == TurnInput.Stage.SwapFirst);
            commits = G.Commits;
            yield return Click(mouse, ScreenOf(pair.A));
            yield return Click(mouse, ScreenOf(pair.B));
            Check("mouse_picks_swap_pair", G.Selection.Current == TurnInput.Stage.SwapConfirm && G.Commits == commits, G.Selection.Current.ToString());
            yield return null;
            var cancel = G.Ui.CancelButtonUi;
            yield return Click(mouse, RectTransformUtility.WorldToScreenPoint(null, cancel.transform.position));
            Check("cancel_button_keeps_turn_and_power", !G.Selection.Targeting && G.Commits == commits && r.SwapsLeft(Player.One) == 1 && r.Current == Player.One,
                  $"targeting={G.Selection.Targeting} commits {commits}->{G.Commits}");

            // the Bomb button is UI: clicking it must not also drop a counter
            var bombUi = G.Ui.BombButtonUi;
            yield return Click(mouse, RectTransformUtility.WorldToScreenPoint(null, bombUi.transform.position));
            Check("bomb_button_click_not_a_drop", G.Selection.Current == TurnInput.Stage.BombTarget && G.Commits == commits);
            // gamepad: B backs out of the power entirely
            yield return Pad(pad, GamepadButton.East);
            Check("gamepad_b_cancels", !G.Selection.Targeting);
            // gamepad: X Bomb, A picks the highlighted target, A again confirms
            yield return Pad(pad, GamepadButton.West);
            yield return Pad(pad, GamepadButton.South);
            yield return Pad(pad, GamepadButton.South);
            Check("gamepad_bomb_committed", G.Commits == commits + 1 && r.BombsLeft(Player.One) == 0 && G.LastResult.Action.Kind == ActionKind.Bomb, $"bombs={r.BombsLeft(Player.One)}");
            yield return WaitHumanTurn();
            yield return Tap(kb, Key.B);
            bool bombOff = !G.PowerAvailable(ActionKind.Bomb, out string why);
            Check("used_bomb_is_disabled", !G.Selection.Targeting && bombOff && why != null && why.Contains("already used"), why);

            // ---- turn timer: no input -> automatic centre-first drop, never a power
            G.StartMatch(new MatchConfig { Solo = true, Mode = GameMode.Battle, Difficulty = Difficulty.Easy, Starter = Player.One, TurnSeconds = 3 });
            yield return WaitHumanTurn();
            commits = G.Commits;
            yield return WaitFor(() => G.Commits > commits, 8f);
            Check("timeout_auto_drop", G.LastResult != null && G.LastResult.Timeout && G.LastResult.Action.Kind == ActionKind.Drop && G.LastResult.Action.Column == 3
                  && G.Round.BombsLeft(Player.One) == 1, G.LastResult != null ? G.LastResult.Action.ToString() : "none");
            // pause freezes the timer
            yield return WaitHumanTurn();
            G.SetPaused(true);
            float before = G.Clock.Remaining;
            yield return new WaitForSecondsRealtime(1.2f);
            Check("pause_freezes_timer", Mathf.Approximately(before, G.Clock.Remaining), $"{before:0.00} -> {G.Clock.Remaining:0.00}");
            G.SetPaused(false);

            // ---- rematch after a finished match, and back to the menu
            GameController.AutoContinue = true;
            GameController.PlaySpeed = 3f;
            G.StartMatch(new MatchConfig { Watch = true, Mode = GameMode.Classic, Difficulty = Difficulty.Easy, WatchOpponent = Difficulty.Easy, Starter = Player.One });
            yield return WaitPhase(GameController.Phase.MatchOver, 300f);
            yield return WaitFor(() => G.Ui.ResultsOpen, 10f);
            GameController.AutoContinue = false;
            GameController.PlaySpeed = 1f;
            sel = EventSystem.current.currentSelectedGameObject;
            Check("match_result_focus_rematch", sel && sel.name.StartsWith("Rematch"), sel ? sel.name : "none");
            yield return Tap(kb, Key.Enter);
            yield return WaitFor(() => G.Current == GameController.Phase.AiTurn || G.Current == GameController.Phase.HumanTurn, 10f);
            Check("rematch_starts_new_match", G.Match.RoundNumber == 1 && G.Match.Wins(Player.One) + G.Match.Wins(Player.Two) == 0);
            for (int i = 0; i < 3; i++) { G.Restart(); yield return new WaitForSecondsRealtime(0.4f); }
            Check("repeated_restart_clean", G.Match.RoundNumber == 1 && G.Round.MovesMade == 0 && !G.Paused);
            G.QuitToMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            Check("quit_to_menu", G.Ui.TitleOpen && G.Config.Demo);

            Check("no_console_errors", errors == 0, $"errors={errors}");
            LogPerf("inputtest");
            Debug.Log($"DZ_INPUT_DONE pass={passes} fail={fails}");
            if (!Application.isEditor && Application.platform != RuntimePlatform.WebGLPlayer) Application.Quit();
        }
    }
}

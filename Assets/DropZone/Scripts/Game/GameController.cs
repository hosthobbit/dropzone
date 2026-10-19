using System;
using System.Collections;
using DropZone.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DropZone
{
    public enum Seat { Human, Computer }
    public enum InputKind { Mouse, Touch, Keyboard, Gamepad }

    /// <summary>Everything chosen on the setup screen.</summary>
    [Serializable]
    public class MatchConfig
    {
        public bool Solo = true;
        public GameMode Mode = GameMode.Classic;
        public Difficulty Difficulty = Difficulty.Normal;
        /// <summary>Who starts round 1 (None = random).</summary>
        public Player Starter = Player.One;
        public int TurnSeconds;
        /// <summary>Both seats played by the computer (title-screen demo, automated QA).</summary>
        public bool Watch;
        public Difficulty WatchOpponent = Difficulty.Normal;
        public bool Demo;

        public MatchConfig Clone() => (MatchConfig)MemberwiseClone();
    }

    /// <summary>
    /// The match manager: one authoritative Match/RoundState, explicit phases, and the glue between
    /// input, AI, the board view, audio and the UI. Moves are validated actions; the view only ever
    /// animates what the rules have already decided.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public enum Phase { Title, RoundIntro, HumanTurn, AiTurn, Resolving, RoundOver, MatchOver }

        [Header("Links")]
        public BoardView View;
        public CameraRig Rig;
        public Camera Cam;
        public UIManager Ui;

        [Header("Pacing")]
        [Tooltip("The computer always takes at least this long, so its moves are readable.")] public float MinAiThink = 0.6f;
        public float AiShowChoice = 0.35f;
        public float RoundIntroTime = 1.4f;
        [Tooltip("Search slice per frame while the computer thinks (ms).")] public float AiSliceMs = 12f;

        public static GameController Instance { get; private set; }
        public Phase Current { get; private set; } = Phase.Title;
        public bool Paused { get; private set; }
        public MatchConfig Config { get; private set; } = new MatchConfig();
        public Match Match { get; private set; }
        public RoundState Round => Match?.Round;
        public readonly TurnClock Clock = new TurnClock();
        public readonly TurnInput Selection = new TurnInput();
        public GameInput Input { get; private set; }
        public InputKind LastDevice { get; private set; } = InputKind.Mouse;
        public Cell Cursor { get; private set; } = new Cell(3, 0);
        public bool CursorVisible { get; private set; }
        public string Feedback { get; private set; }
        public float FeedbackUntil { get; private set; }
        public int TouchArmedColumn { get; private set; } = -1;
        public MoveResult LastResult { get; private set; }
        public int Commits { get; private set; }
        public bool AiThinking { get; private set; }

        /// <summary>QA: ignore window focus loss (virtual displays never have focus).</summary>
        public static bool IgnoreFocusLoss;
        /// <summary>QA: skip waiting on result screens.</summary>
        public static bool AutoContinue;
        /// <summary>QA: run animations faster.</summary>
        public static float PlaySpeed = 1f;
        public event Action<MoveResult> Committed;
        public event Action<Phase> PhaseChanged;

        readonly AiPlayer[] ai = new AiPlayer[3];
        int epoch, turnSerial, lastTickShown;
        readonly int[] lastColumn = { 3, 3, 3 };
        float lastPressTime;
        Vector2 lastPointer, lastMousePos;
        int quietUntilFrame;   // board ignores input briefly after menus close (the same key press shouldn't also play)

        public bool IsComputer(Player p) => ai[(int)p] != null;
        public bool HumanToMove => Current == Phase.HumanTurn;
        public bool InMatch => Current != Phase.Title && !Config.Demo;

        void Awake()
        {
            Instance = this;
            Input = new GameInput();
            if (!View || !Rig || !Cam || !Ui) Debug.LogError("DZ_SCENE_ERROR GameController is missing a reference");
            ApplySettings();
        }

        void Start()
        {
            Ui.Build(this);
            GoToTitle();
        }

        public void ApplySettings()
        {
            QualitySettings.SetQualityLevel(Mathf.Clamp(Settings.Quality, 0, QualitySettings.names.Length - 1), true);
            if (Sfx.Instance) { Sfx.Instance.MusicVolume = Settings.Music; Sfx.Instance.SoundVolume = Settings.Sound; }
            Application.targetFrameRate = 60;
        }

        void SetPhase(Phase p) { Current = p; PhaseChanged?.Invoke(p); }

        // ------------------------------------------------------------------ flow

        public void GoToTitle()
        {
            CancelEverything();
            SetPhase(Phase.Title);
            Ui.ShowTitle();
            // the board plays itself quietly behind the title menu
            StartMatchInternal(new MatchConfig { Demo = true, Watch = true, Difficulty = Difficulty.Normal, WatchOpponent = Difficulty.Easy, Mode = GameMode.Classic, Starter = Player.One }, showHud: false);
        }

        public void StartMatch(MatchConfig config)
        {
            CancelEverything();
            Config = config.Clone();
            Ui.ShowGame();
            StartMatchInternal(Config, showHud: true);
        }

        void StartMatchInternal(MatchConfig config, bool showHud)
        {
            Config = config;
            var rules = config.Mode == GameMode.Battle ? RuleSet.Battle(config.TurnSeconds) : RuleSet.Classic(config.TurnSeconds);
            var starter = config.Starter == Player.None ? (UnityEngine.Random.value < 0.5f ? Player.One : Player.Two) : config.Starter;
            Match = new Match(rules, starter);
            int seed = Environment.TickCount;
            ai[1] = config.Watch ? new AiPlayer(config.WatchOpponent, seed + 1) { SliceMs = AiSliceMs } : null;
            ai[2] = config.Watch || config.Solo ? new AiPlayer(config.Difficulty, seed + 2) { SliceMs = AiSliceMs } : null;
            StartCoroutine(StartRound(epoch));
        }

        public void Rematch() => StartMatch(Config);
        public void Restart() { SetPaused(false); StartMatch(Config); }
        public void QuitToMenu() { SetPaused(false); GoToTitle(); }

        /// <summary>Stop everything in flight: AI search, animations, timers, pending coroutines.</summary>
        void CancelEverything()
        {
            epoch++;
            turnSerial++;
            StopAllCoroutines();
            foreach (var a in ai) a?.Cancel();
            AiThinking = false;
            View.ClearAll();
            Paused = false;
            Time.timeScale = PlaySpeed;
            Clock.Reset(0);
            Selection.Reset();
            Feedback = null;
            Ui.HideResults();
        }

        IEnumerator StartRound(int e)
        {
            View.ClearAll();
            var round = Match.StartRound();
            View.Sync(round.Board);
            SetPhase(Phase.RoundIntro);
            if (!Config.Demo)
            {
                Ui.RoundIntro(Match.RoundNumber, round.Current);
                Sfx.Play(Sfx.Kind.RoundStart, 0.8f);
                float t = 0f, dur = Settings.ReducedMotion ? RoundIntroTime * 0.6f : RoundIntroTime;
                if (AutoContinue) dur = 0.3f;
                while (t < dur) { if (!Paused) t += Time.unscaledDeltaTime; yield return null; }
            }
            else yield return new WaitForSeconds(0.8f);
            if (e != epoch) yield break;
            BeginTurn();
        }

        void BeginTurn()
        {
            turnSerial++;
            var me = Round.Current;
            Selection.Reset(lastColumn[(int)me]);
            TouchArmedColumn = -1;
            CursorVisible = false;
            Clock.Reset(Config.Demo ? 0 : Round.Rules.TurnSeconds);
            Clock.Resume();
            lastTickShown = 99;
            if (IsComputer(me)) { SetPhase(Phase.AiTurn); StartCoroutine(AiTurn(epoch, turnSerial)); }
            else SetPhase(Phase.HumanTurn);
        }

        IEnumerator AiTurn(int e, int serial)
        {
            var me = Round.Current;
            var brain = ai[(int)me];
            float waited = 0f;
            AiThinking = true;
            var it = brain.Think(Round.Clone());
            while (true)
            {
                if (e != epoch || serial != turnSerial) { AiThinking = false; yield break; }
                if (Paused) { yield return null; continue; }
                if (!it.MoveNext()) break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            float min = Config.Demo ? 1.1f : AutoContinue ? 0.05f : MinAiThink;
            while (waited < min) { if (!Paused) waited += Time.unscaledDeltaTime; yield return null; }
            AiThinking = false;
            if (e != epoch || serial != turnSerial || Current != Phase.AiTurn) yield break;

            var a = brain.Choice;
            if (!Round.IsLegal(a)) a = Round.TimeoutAction();
            // show what it's about to do
            float show = AutoContinue ? 0.05f : AiShowChoice;
            if (a.Kind == ActionKind.Drop) Selection.SelectColumn(a.Column);
            else if (a.Kind == ActionKind.Bomb) { Selection.BeginBomb(Round); Selection.PickCell(Round, a.A); Sfx.Play(Sfx.Kind.Select, 0.7f); show *= 2f; }
            else { Selection.BeginSwap(Round); Selection.PickCell(Round, a.A); Selection.PickCell(Round, a.B); Sfx.Play(Sfx.Kind.Select, 0.7f); show *= 2f; }
            float s = 0f;
            while (s < show) { if (!Paused) s += Time.unscaledDeltaTime; yield return null; }
            if (e != epoch || serial != turnSerial || Current != Phase.AiTurn) yield break;
            Commit(a, false);
        }

        /// <summary>Commit a validated action: lock input, update the logical state, animate, then resolve.</summary>
        public bool Commit(GameAction a, bool timeout)
        {
            if (Current != Phase.HumanTurn && Current != Phase.AiTurn) return false;
            if (!Round.IsLegal(a, out string why)) { Say(why); Sfx.Play(Sfx.Kind.Invalid); return false; }
            SetPhase(Phase.Resolving);
            turnSerial++;
            Clock.Freeze();
            var me = Round.Current;
            if (a.Kind == ActionKind.Drop) lastColumn[(int)me] = a.Column;
            Selection.Reset(Selection.Column);
            CursorVisible = false;
            TouchArmedColumn = -1;
            if (a.Kind != ActionKind.Drop) View.HidePreview();
            var res = Round.Apply(a, timeout);
            LastResult = res;
            Commits++;
            if (a.Kind == ActionKind.Bomb) Say($"{Ui.NameOf(me)} used a Bomb!");
            else if (a.Kind == ActionKind.Swap) Say($"{Ui.NameOf(me)} used a Swap!");
            Committed?.Invoke(res);
            StartCoroutine(Resolve(res, epoch));
            return true;
        }

        IEnumerator Resolve(MoveResult res, int e)
        {
            yield return View.Play(res, Round.Board);
            if (e != epoch) yield break;
            if (Round.IsOver) yield return RoundEnd(res, e);
            else BeginTurn();
        }

        IEnumerator RoundEnd(MoveResult res, int e)
        {
            SetPhase(Phase.RoundOver);
            Match.RecordRound();
            if (res.Lines.Count > 0) yield return View.ShowWin(res.Lines);
            if (e != epoch) yield break;
            var winner = Round.Winner;

            if (Config.Demo)
            {
                yield return new WaitForSeconds(2.6f);
                if (e != epoch) yield break;
                if (Match.IsOver) StartMatchInternal(Config, false);
                else StartCoroutine(StartRound(e));
                yield break;
            }

            bool soloHuman = Config.Solo && !Config.Watch;
            if (soloHuman)
            {
                Settings.AddStat(Config.Difficulty, winner == Player.One ? "roundWon" : winner == Player.Two ? "roundLost" : "roundDraw");
            }
            if (winner != Player.None)
            {
                var mid = View.CellWorld(res.Lines.Find(l => l.Owner == winner).Cells[1]);
                if (!Settings.ReducedMotion) View.Effects.Celebrate(mid + Vector3.up * 2f, View.ColourOf(winner), Color.white);
                Rig.Shake(0.25f);
            }

            if (Match.IsOver)
            {
                SetPhase(Phase.MatchOver);
                var champ = Match.Winner;
                bool humanLost = soloHuman && champ == Player.Two;
                Sfx.Play(humanLost ? Sfx.Kind.Defeat : Sfx.Kind.Victory);
                if (soloHuman) { Settings.AddStat(Config.Difficulty, humanLost ? "matchLost" : "matchWon"); }
                Settings.Save();
                if (!Settings.ReducedMotion && !humanLost) View.Effects.Celebrate(View.transform.TransformPoint(new Vector3(0, 6.5f, -0.5f)), View.ColourOf(champ), Color.white);
                yield return new WaitForSeconds(AutoContinue ? 0.2f : 1.0f);
                if (e != epoch) yield break;
                Ui.ShowMatchResult(this, res);
            }
            else
            {
                Sfx.Play(winner == Player.None ? Sfx.Kind.Draw : (Config.Solo && !Config.Watch && winner == Player.Two ? Sfx.Kind.Defeat : Sfx.Kind.Victory), 0.7f);
                Settings.Save();
                yield return new WaitForSeconds(AutoContinue ? 0.2f : 0.9f);
                if (e != epoch) yield break;
                Ui.ShowRoundResult(this, res);
                if (AutoContinue) { yield return new WaitForSeconds(0.3f); if (e == epoch) NextRound(); }
            }
        }

        public void NextRound()
        {
            if (Current != Phase.RoundOver || Match.IsOver) return;
            Ui.HideResults();
            StartCoroutine(StartRound(epoch));
        }

        // ------------------------------------------------------------------ pause and focus

        public bool CanPause => InMatch && Current != Phase.MatchOver;

        public void SetPaused(bool on)
        {
            if (on && !CanPause) return;
            if (Paused == on) return;
            Paused = on;
            Time.timeScale = on ? 0f : PlaySpeed;
            if (on) Clock.Freeze();
            else if (Current == Phase.HumanTurn || Current == Phase.AiTurn) Clock.Resume();
            Ui.ShowPause(on);
            quietUntilFrame = Time.frameCount + 2;
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && !IgnoreFocusLoss && CanPause) SetPaused(true);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && !IgnoreFocusLoss && CanPause) SetPaused(true);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            TrackDevice();
            if (Input.Pause.WasPressedThisFrame() && InMatch && !Ui.ModalOpen)
            {
                if (Paused) SetPaused(false);
                else if (Current == Phase.HumanTurn && Selection.Targeting) CancelStep();
                else SetPaused(true);
            }
            if (Paused || Round == null) { Ui.UpdateHud(this); return; }

            if (Current == Phase.HumanTurn || Current == Phase.AiTurn)
            {
                if (Clock.Tick(Time.unscaledDeltaTime)) TimeUp();
                else if (Clock.Enabled && Clock.Running)
                {
                    int secs = Clock.DisplaySeconds;
                    if (secs <= 3 && secs < lastTickShown) { lastTickShown = secs; Sfx.Play(Sfx.Kind.Tick, 0.8f, secs == 1 ? 1.25f : 1f); }
                }
            }
            if (Current == Phase.HumanTurn && !Ui.ModalOpen && Time.frameCount > quietUntilFrame) HumanInput();
            else if (Current == Phase.HumanTurn) { lastPointer = Input.Point.ReadValue<Vector2>(); Input.MoveStep(Time.unscaledTime); }

            // board highlights
            if (Current == Phase.HumanTurn || Current == Phase.AiTurn)
            {
                if (Selection.Targeting) View.ShowTargets(Round, Selection, CursorVisible ? Cursor : (Cell?)null);
                else
                {
                    int col = Selection.Column;
                    View.ShowPreview(true, Round.Current, col, Round.Board.CanDrop(col) ? Round.Board.Height(col) : -1);
                }
            }
            Ui.UpdateHud(this);
        }

        void TimeUp()
        {
            Selection.CancelAll();
            Say("Time up — automatic drop.", 2.5f);
            Sfx.Play(Sfx.Kind.TimeUp);
            Commit(Round.TimeoutAction(), true);
        }

        public void Say(string text, float seconds = 2.2f)
        {
            Feedback = text;
            FeedbackUntil = Time.unscaledTime + seconds;
        }

        void TrackDevice()
        {
            var kb = Keyboard.current; var gp = Gamepad.current; var ms = Mouse.current; var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) LastDevice = InputKind.Touch;
            else if (ms != null && ((ms.position.ReadValue() - lastMousePos).sqrMagnitude > 9f || ms.delta.ReadValue().sqrMagnitude > 4f) && LastDevice != InputKind.Touch) LastDevice = InputKind.Mouse;
            else if (ms != null && ms.leftButton.wasPressedThisFrame) LastDevice = InputKind.Mouse;
            else if (kb != null && kb.anyKey.wasPressedThisFrame) LastDevice = InputKind.Keyboard;
            else if (gp != null && AnyGamepad(gp)) LastDevice = InputKind.Gamepad;
            if (ms != null) lastMousePos = ms.position.ReadValue();
        }

        static bool AnyGamepad(Gamepad gp)
        {
            if (gp.leftStick.ReadValue().sqrMagnitude > 0.25f) return true;
            foreach (var c in gp.allControls)
                if (c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame) return true;
            return false;
        }

        // ------------------------------------------------------------------ human input

        void HumanInput()
        {
            var step = Input.MoveStep(Time.unscaledTime);
            Vector2 pointer = Input.Point.ReadValue<Vector2>();
            bool pointerMoved = (pointer - lastPointer).sqrMagnitude > 1f;
            lastPointer = pointer;
            bool press = Input.Press.WasPressedThisFrame() && Time.unscaledTime - lastPressTime > 0.12f;
            if (press) lastPressTime = Time.unscaledTime;
            bool overUi = Ui.PointerOverUi(pointer);
            bool isTouch = press ? Input.PointerIsTouch : LastDevice == InputKind.Touch;
            int column = -1; Cell cell = new Cell(-1, -1);
            bool onBoard = !overUi && View.Pick(Cam.ScreenPointToRay(pointer), out column, out cell);

            if (Input.Bomb.WasPressedThisFrame()) { BombButton(); return; }
            if (Input.Swap.WasPressedThisFrame()) { SwapButton(); return; }

            if (!Selection.Targeting)
            {
                if (step.x != 0)
                {
                    Selection.SelectColumn(Mathf.Clamp(Selection.Column + step.x, 0, Board.Columns - 1));
                    Sfx.Play(Sfx.Kind.Hover, 0.6f);
                    TouchArmedColumn = -1;
                }
                if (onBoard && pointerMoved && !isTouch && LastDevice == InputKind.Mouse && column != Selection.Column)
                {
                    Selection.SelectColumn(column);
                    Sfx.Play(Sfx.Kind.Hover, 0.4f);
                }
                if (press && onBoard)
                {
                    if (isTouch && TouchArmedColumn != column)
                    {
                        // touch: the first tap previews, a second tap on the same column drops
                        Selection.SelectColumn(column);
                        TouchArmedColumn = column;
                        Sfx.Play(Sfx.Kind.Hover, 0.6f);
                        Say("Tap the same column again to drop.");
                    }
                    else { Selection.SelectColumn(column); TryDrop(); }
                    return;
                }
                if (Input.Confirm.WasPressedThisFrame()) TryDrop();
                return;
            }

            // choosing a Bomb or Swap target
            if (step != Vector2Int.zero)
            {
                if (!CursorVisible) { CursorVisible = true; Cursor = DefaultCursor(); }
                else Cursor = new Cell(Mathf.Clamp(Cursor.Column + step.x, 0, Board.Columns - 1), Mathf.Clamp(Cursor.Row + step.y, 0, Board.Rows - 1));
                Sfx.Play(Sfx.Kind.Hover, 0.6f);
            }
            if (onBoard && pointerMoved && !isTouch && Board.InBounds(cell)) { Cursor = cell; CursorVisible = true; }
            if (press && onBoard && Board.InBounds(cell)) { Cursor = cell; PickOrConfirm(cell); return; }
            if (Input.Confirm.WasPressedThisFrame())
            {
                if (!CursorVisible) { CursorVisible = true; Cursor = DefaultCursor(); return; }
                PickOrConfirm(Cursor);
            }
            if (Input.Cancel.WasPressedThisFrame()) CancelStep();
        }

        /// <summary>Pick (or re-pick to confirm) a Bomb/Swap target, exactly as a click on the board would.</summary>
        public void PickTarget(Cell cell) { if (Current == Phase.HumanTurn && Selection.Targeting) { Cursor = cell; PickOrConfirm(cell); } }

        void PickOrConfirm(Cell cell)
        {
            // pressing the already-chosen target again confirms it
            bool repeat = (Selection.Current == TurnInput.Stage.BombConfirm && cell == Selection.First)
                       || (Selection.Current == TurnInput.Stage.SwapConfirm && cell == Selection.Second);
            if (repeat) { ConfirmButton(); return; }
            if (Selection.PickCell(Round, cell)) { Sfx.Play(Sfx.Kind.Select, 0.6f); Say(Selection.Message, 3f); }
            else { Sfx.Play(Sfx.Kind.Invalid, 0.7f); Say(Selection.Message, 2.5f); }
        }

        Cell DefaultCursor()
        {
            // start on a sensible target near the middle of the board
            var best = new Cell(3, 0); int bestD = int.MaxValue;
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                {
                    var cell = new Cell(c, r);
                    bool ok = Selection.Current == TurnInput.Stage.BombTarget ? Round.IsLegal(GameAction.Bomb(cell))
                            : Selection.Current == TurnInput.Stage.SwapSecond ? Round.IsLegal(GameAction.Swap(Selection.First, cell))
                            : Round.Board[cell] != Player.None;
                    int d = Math.Abs(c - 3) * 2 + r;
                    if (Selection.Current == TurnInput.Stage.SwapSecond) d = Math.Abs(c - Selection.First.Column) + Math.Abs(r - Selection.First.Row);
                    if (ok && d < bestD) { bestD = d; best = cell; }
                }
            return best;
        }

        void TryDrop()
        {
            var a = GameAction.Drop(Selection.Column);
            if (!Round.IsLegal(a, out string why)) { Sfx.Play(Sfx.Kind.Invalid, 0.8f); Say(why); return; }
            Commit(a, false);
        }

        // ------------------------------------------------------------------ buttons (also used by keys)

        public void BombButton()
        {
            if (Current != Phase.HumanTurn) return;
            if (Selection.Current == TurnInput.Stage.BombTarget || Selection.Current == TurnInput.Stage.BombConfirm) { CancelAll(); return; }
            Selection.CancelAll();
            if (Selection.BeginBomb(Round)) { Sfx.Play(Sfx.Kind.Select); CursorVisible = LastDevice == InputKind.Keyboard || LastDevice == InputKind.Gamepad; Cursor = DefaultCursor(); Say(Selection.Message, 4f); }
            else { Sfx.Play(Sfx.Kind.Invalid); Say(Selection.Message, 3f); }
        }

        public void SwapButton()
        {
            if (Current != Phase.HumanTurn) return;
            if (Selection.Current == TurnInput.Stage.SwapFirst || Selection.Current == TurnInput.Stage.SwapSecond || Selection.Current == TurnInput.Stage.SwapConfirm) { CancelAll(); return; }
            Selection.CancelAll();
            if (Selection.BeginSwap(Round)) { Sfx.Play(Sfx.Kind.Select); CursorVisible = LastDevice == InputKind.Keyboard || LastDevice == InputKind.Gamepad; Cursor = DefaultCursor(); Say(Selection.Message, 4f); }
            else { Sfx.Play(Sfx.Kind.Invalid); Say(Selection.Message, 3f); }
        }

        public void ConfirmButton()
        {
            if (Current != Phase.HumanTurn) return;
            if (!Selection.Targeting) { TryDrop(); return; }
            if (Selection.TryConfirm(Round, out var a)) Commit(a, false);
            else { Sfx.Play(Sfx.Kind.Invalid, 0.7f); Say(Selection.Message); }
        }

        public void CancelStep()
        {
            if (Selection.Cancel()) { Sfx.Play(Sfx.Kind.Cancel, 0.8f); Say(Selection.Message, 2.5f); if (!Selection.Targeting) CursorVisible = false; }
        }

        public void CancelAll()
        {
            if (!Selection.Targeting) return;
            Selection.CancelAll();
            CursorVisible = false;
            Sfx.Play(Sfx.Kind.Cancel, 0.8f);
            Say(Selection.Message, 2f);
        }

        /// <summary>Can the human seat to move use this power right now? (For the HUD buttons.)</summary>
        public bool PowerAvailable(ActionKind kind, out string why)
        {
            why = null;
            if (Round == null || !Round.Rules.PowersEnabled) { why = ""; return false; }
            var me = Round.Current;
            return kind == ActionKind.Bomb ? Round.CanBomb(me, out why) : Round.CanSwap(me, out why);
        }
    }
}

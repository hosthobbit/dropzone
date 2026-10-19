using System;
using System.Collections;
using System.Collections.Generic;
using DropZone.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DropZone
{
    /// <summary>
    /// The whole interface, built in code at start-up: title, match setup, how to play, settings,
    /// stats, the Battle briefing, HUD, round and match results, and pause. Mouse, touch, keyboard
    /// and gamepad all work; keyboard/gamepad focus is always visible.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public const string Credit = "Developed by Host Hobbit · hosthobbit.com";

        Canvas canvas;
        CanvasScaler scaler;
        RectTransform root;
        GameObject title, setup, hud, pause, results, modal, intro;
        GameController game;
        GameObject modalReturnFocus;
        Action modalClosed;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        PointerEventData pointerData;

        // HUD parts
        class SeatCard
        {
            public Image Edge, Symbol, BombIcon, SwapIcon;
            public Text Name, Role, BombCount, SwapCount;
            public Image[] Pips;
            public GameObject Powers;
        }
        readonly SeatCard[] cards = new SeatCard[3];
        Image bannerEdge, bannerSymbol, timerFill;
        Text bannerText, timerText, promptText, introTitle, introSub;
        RectTransform timerBar;
        GameObject actionBar, powerGroup, targetGroup;
        Button bombButton, swapButton, confirmButton, cancelButton, pauseButton;
        Text bombReason, swapReason;
        CanvasGroup introGroup;
        RectTransform titleShade, titleLogo, titleTag, titleMenu, titleCredit, bannerRect, pauseRect, promptRect, actionRect, setupPanel;
        bool? portraitLayout;

        // setup screen state
        MatchConfig pending = new MatchConfig();
        bool pendingSolo;

        int modalClosedFrame = -1;
        /// <summary>A modal is open (or was closed by this frame's Escape, so the game shouldn't also react).</summary>
        public bool ModalOpen => modal != null || modalClosedFrame == Time.frameCount;
        public Button BombButtonUi => bombButton;
        public Button CancelButtonUi => cancelButton;
        public bool SetupOpen => setup && setup.activeSelf;
        public bool TitleOpen => title && title.activeSelf;
        public bool PauseOpen => pause && pause.activeSelf;
        public bool ResultsOpen => results && results.activeSelf;
        public bool InMenus => (title && title.activeSelf) || (setup && setup.activeSelf);

        static Color Col(Player p) => p == Player.One ? UiKit.Orange : UiKit.Cyan;
        static Sprite Sym(Player p) => p == Player.One ? UiKit.Star : UiKit.Target;

        public string NameOf(Player p)
        {
            if (game == null) return p == Player.One ? "Player 1" : "Player 2";
            var c = game.Config;
            if (c.Watch) return p == Player.One ? $"Computer ({c.WatchOpponent})" : $"Computer ({c.Difficulty})";
            if (c.Solo) return p == Player.One ? "You" : $"Computer";
            return p == Player.One ? "Player 1" : "Player 2";
        }

        string Possessive(Player p) { var n = NameOf(p); return n == "You" ? "Your" : n + "'s"; }

        // ================================================================== build

        public void Build(GameController g)
        {
            game = g;
            UiKit.Init();
            var cgo = new GameObject("UI Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            cgo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)cgo.transform;

            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
            pointerData = new PointerEventData(EventSystem.current);

            BuildHud();
            BuildIntro();
            BuildTitle();
            BuildSetup();
            BuildPause();
            results = UiKit.Rect("Results", root).Fill().gameObject;
            results.SetActive(false);
            Relayout(Screen.height > Screen.width);
        }

        void Update()
        {
            // keep the UI the same physical size on landscape and portrait screens
            bool portrait = Screen.height > Screen.width;
            if (portraitLayout != portrait) Relayout(portrait);
            // Escape / B / right-click = back in menus
            bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (back)
            {
                if (modal) CloseModal();
                else if (setup.activeSelf) ShowTitle();
            }
            // never lose keyboard/gamepad focus in menus
            var es = EventSystem.current;
            if (es && (modal || title.activeSelf || setup.activeSelf || pause.activeSelf || results.activeSelf) && (es.currentSelectedGameObject == null || !es.currentSelectedGameObject.activeInHierarchy))
            {
                bool nav = (Keyboard.current != null && (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame))
                         || (Gamepad.current != null && (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0.2f || Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.3f));
                if (nav) Select(FirstButton(modal ? modal : pause.activeSelf ? pause : results.activeSelf ? results : setup.activeSelf ? setup : title));
            }
        }

        /// <summary>
        /// Landscape: menus on the left, HUD in one row across the top. Portrait (phones): bigger UI,
        /// title menu below the board, turn banner on a second row under the player cards.
        /// </summary>
        void Relayout(bool portrait)
        {
            portraitLayout = portrait;
            scaler.referenceResolution = portrait ? new Vector2(1300, 2000) : new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = portrait ? 0f : 1f;
            if (portrait)
            {
                titleShade.Place(new Vector2(0, 0), new Vector2(1, 0.4f), new Vector2(-40, -40), new Vector2(40, 0));
                titleLogo.Place(new Vector2(0, 0.86f), new Vector2(1, 0.97f), Vector2.zero, Vector2.zero);
                titleTag.Place(new Vector2(0.05f, 0.81f), new Vector2(0.95f, 0.87f), Vector2.zero, Vector2.zero);
                titleMenu.Place(new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.38f), Vector2.zero, Vector2.zero);
                titleCredit.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 18), new Vector2(0, 60));
                bannerRect.anchoredPosition = new Vector2(-50, -220);
                pauseRect.anchoredPosition = new Vector2(330, -224);
                actionRect.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 40), new Vector2(-30, 170));
                promptRect.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 180), new Vector2(-30, 240));
            }
            else
            {
                titleShade.Place(Vector2.zero, new Vector2(0.42f, 1f), new Vector2(-40, -40), new Vector2(0, 40));
                titleLogo.Place(new Vector2(0.02f, 0.72f), new Vector2(0.4f, 0.97f), Vector2.zero, Vector2.zero);
                titleTag.Place(new Vector2(0.02f, 0.66f), new Vector2(0.4f, 0.74f), Vector2.zero, Vector2.zero);
                titleMenu.Place(new Vector2(0.07f, 0.1f), new Vector2(0.35f, 0.64f), Vector2.zero, Vector2.zero);
                titleCredit.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 14), new Vector2(0, 54));
                bannerRect.anchoredPosition = new Vector2(0, -24);
                pauseRect.anchoredPosition = new Vector2(370, -28);
                actionRect.Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-560, 22), new Vector2(560, 140));
                promptRect.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 150), new Vector2(-40, 200));
            }
            if (setupPanel) setupPanel.sizeDelta = new Vector2(portrait ? 1240 : 1180, portrait ? 1100 : 940);
        }

        static Selectable FirstButton(GameObject panel)
        {
            foreach (var s in panel.GetComponentsInChildren<Selectable>()) if (s.interactable && s.gameObject.activeInHierarchy) return s;
            return null;
        }

        public static void Select(Selectable s)
        {
            if (!s || !EventSystem.current) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        /// <summary>Is the pointer over a UI element that should block board input?</summary>
        public bool PointerOverUi(Vector2 screen)
        {
            if (!EventSystem.current) return false;
            pointerData.position = screen;
            hits.Clear();
            EventSystem.current.RaycastAll(pointerData, hits);
            return hits.Count > 0;
        }

        // ================================================================== title

        void BuildTitle()
        {
            title = UiKit.Rect("Title", root).Fill().gameObject;
            var shade = UiKit.Box(title.transform, "Shade", new Color(0.02f, 0.02f, 0.08f, 0.72f), UiKit.RoundedSmall);
            titleShade = shade.rectTransform;

            var logo = UiKit.Label(title.transform, "DROPZONE", 150, UiKit.Orange, UiKit.Title);
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleLogo = logo.rectTransform;
            logo.rectTransform.Place(new Vector2(0.02f, 0.72f), new Vector2(0.4f, 0.97f), Vector2.zero, Vector2.zero);
            logo.Outlined(new Color(0.35f, 0.1f, 0.5f), 5f);
            logo.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -10);
            var tag = UiKit.Label(title.transform, "Four in a row. One move can change everything.", 32, UiKit.Cyan, UiKit.Bold);
            titleTag = tag.rectTransform;
            tag.rectTransform.Place(new Vector2(0.02f, 0.66f), new Vector2(0.4f, 0.74f), Vector2.zero, Vector2.zero);

            var menu = UiKit.Rect("Menu", title.transform);
            titleMenu = menu;
            UiKit.Stack(menu, 16, 0, TextAnchor.MiddleCenter);
            var solo = UiKit.Button(menu, "Solo vs Computer", UiKit.Orange, () => ShowSetup(true), 40);
            solo.Size(88);
            UiKit.Button(menu, "Local Multiplayer", UiKit.Cyan, () => ShowSetup(false), 40).Size(88);
            UiKit.Button(menu, "How to Play", UiKit.ButtonColour, ShowHowTo, 32).Size(70);
            UiKit.Button(menu, "Settings", UiKit.ButtonColour, ShowSettings, 32).Size(70);
            UiKit.Button(menu, "Stats", UiKit.ButtonColour, ShowStats, 32).Size(70);
            if (Application.platform != RuntimePlatform.WebGLPlayer) UiKit.Button(menu, "Quit", UiKit.ButtonColour, Application.Quit, 32).Size(70);

            var credit = UiKit.Label(title.transform, Credit, 26, UiKit.Muted, UiKit.Bold);
            titleCredit = credit.rectTransform;
            credit.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 14), new Vector2(0, 54));
        }

        public void ShowTitle()
        {
            CloseModal();
            title.SetActive(true);
            setup.SetActive(false);
            hud.SetActive(false);
            pause.SetActive(false);
            results.SetActive(false);
            intro.SetActive(false);
            Select(FirstButton(title));
        }

        public void ShowGame()
        {
            CloseModal();
            title.SetActive(false);
            setup.SetActive(false);
            pause.SetActive(false);
            hud.SetActive(true);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null); // Enter must drop a counter, not press a button
        }

        // ================================================================== setup

        RectTransform setupBody;

        void BuildSetup()
        {
            setup = UiKit.Rect("Setup", root).Fill().gameObject;
            var shade = UiKit.Box(setup.transform, "Shade", UiKit.Shade, UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var panel = UiKit.Panel(setup.transform, "Panel");
            panel.rectTransform.Center(new Vector2(1180, 940));
            setupPanel = panel.rectTransform;
            setupBody = UiKit.Rect("Body", panel.transform).Place(Vector2.zero, Vector2.one, new Vector2(50, 40), new Vector2(-50, -30));
            setup.SetActive(false);
        }

        public void ShowSetup(bool solo)
        {
            pendingSolo = solo;
            pending = new MatchConfig
            {
                Solo = solo,
                Mode = (GameMode)Mathf.Clamp(Settings.LastMode, 0, 1),
                Difficulty = (Difficulty)Mathf.Clamp(Settings.LastDifficulty, 0, 2),
                Starter = Settings.LastStarter == 1 ? Player.Two : Settings.LastStarter == 2 ? Player.None : Player.One,
            };
            pending.TurnSeconds = pending.Mode == GameMode.Battle ? Settings.BattleTimer : Settings.ClassicTimer;
            title.SetActive(false);
            setup.SetActive(true);
            RebuildSetup(-1);
        }

        void RebuildSetup(int focusRow)
        {
            foreach (Transform c in setupBody) Destroy(c.gameObject);
            var stack = UiKit.Rect("Stack", setupBody).Fill();
            UiKit.Stack(stack, 14);
            var head = UiKit.Label(stack, pendingSolo ? "Solo vs Computer" : "Local Multiplayer", 64, pendingSolo ? UiKit.Orange : UiKit.Cyan, UiKit.Title);
            head.Size(84);
            var rows = new List<Selectable>();

            rows.Add(Segmented(stack, "Mode", new[] { "Classic", "Battle" }, (int)pending.Mode, i =>
            {
                pending.Mode = (GameMode)i;
                pending.TurnSeconds = pending.Mode == GameMode.Battle ? Settings.BattleTimer : Settings.ClassicTimer;
                RebuildSetup(0);
            }));
            var desc = UiKit.Label(stack, pending.Mode == GameMode.Battle
                ? "Ten-second turns. Each player gets one <b>Bomb</b> and one <b>Swap</b> per round. A power uses your whole turn."
                : "The pure game: drop counters, connect four. No powers, no timer unless you want one.", 26, UiKit.Muted, UiKit.Bold);
            desc.Size(70);
            if (pendingSolo)
                rows.Add(Segmented(stack, "Difficulty", new[] { "Easy", "Normal", "Hard" }, (int)pending.Difficulty, i => { pending.Difficulty = (Difficulty)i; RebuildSetup(1); }));
            string[] starters = pendingSolo ? new[] { "You", "Computer", "Random" } : new[] { "Player 1", "Player 2", "Random" };
            int si = pending.Starter == Player.One ? 0 : pending.Starter == Player.Two ? 1 : 2;
            rows.Add(Segmented(stack, "First move", starters, si, i => { pending.Starter = i == 0 ? Player.One : i == 1 ? Player.Two : Player.None; RebuildSetup(pendingSolo ? 2 : 1); }));
            int[] timers = { 0, 10, 15, 20, 30 };
            int ti = Mathf.Max(0, Array.IndexOf(timers, pending.TurnSeconds));
            rows.Add(Segmented(stack, "Turn timer", new[] { "Off", "10s", "15s", "20s", "30s" }, ti, i =>
            {
                pending.TurnSeconds = timers[i];
                RebuildSetup(pendingSolo ? 3 : 2);
            }));
            var gap = UiKit.Rect("Gap", stack); gap.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var buttons = UiKit.Rect("Buttons", stack); buttons.Size(96);
            UiKit.Row(buttons, 24);
            var back = UiKit.Button(buttons, "Back", UiKit.ButtonColour, ShowTitle, 34);
            var start = UiKit.Button(buttons, "Start Match", UiKit.Orange, StartFromSetup, 40);
            var le = start.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 2;
            Select(focusRow >= 0 && focusRow < rows.Count ? rows[focusRow] : start);
        }

        /// <summary>A row of options; the chosen one is highlighted. Returns the selected option's button.</summary>
        Selectable Segmented(RectTransform parent, string label, string[] options, int selected, Action<int> onPick)
        {
            var row = UiKit.Rect(label, parent);
            row.Size(84);
            var l = UiKit.Label(row, label, 32, UiKit.TextColour, UiKit.Bold, TextAnchor.MiddleLeft);
            l.rectTransform.Place(Vector2.zero, new Vector2(0.27f, 1f), Vector2.zero, Vector2.zero);
            var group = UiKit.Rect("Options", row).Place(new Vector2(0.27f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
            UiKit.Row(group, 12);
            Selectable chosen = null;
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                bool on = i == selected;
                var b = UiKit.Button(group, options[i], on ? UiKit.Cyan : UiKit.ButtonColour, () => onPick(k), 30, on ? UiKit.Ink : UiKit.TextColour);
                if (on) chosen = b;
            }
            return chosen;
        }

        void StartFromSetup()
        {
            Settings.LastMode = (int)pending.Mode;
            Settings.LastDifficulty = (int)pending.Difficulty;
            Settings.LastStarter = pending.Starter == Player.One ? 0 : pending.Starter == Player.Two ? 1 : 2;
            if (pending.Mode == GameMode.Battle) Settings.BattleTimer = pending.TurnSeconds; else Settings.ClassicTimer = pending.TurnSeconds;
            Settings.Save();
            var cfg = pending.Clone();
            if (cfg.Mode == GameMode.Battle && !Settings.BattleIntroSeen)
            {
                ShowBattleIntro(() => { Settings.BattleIntroSeen = true; Settings.Save(); game.StartMatch(cfg); });
                return;
            }
            game.StartMatch(cfg);
        }

        // ================================================================== modals

        RectTransform OpenModal(string heading, float width, float height, string doneLabel = "Done", Action onClosed = null)
        {
            CloseModal();
            modalReturnFocus = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            modal = UiKit.Rect(heading, root).Fill().gameObject;
            var shade = UiKit.Box(modal.transform, "Shade", UiKit.Shade, UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var panel = UiKit.Panel(modal.transform, "Panel");
            panel.rectTransform.Center(new Vector2(Mathf.Min(width, root.rect.width - 40f), height));
            var h = UiKit.Label(panel.transform, heading, 60, UiKit.Orange, UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -100), new Vector2(0, -16));
            var body = UiKit.Rect("Body", panel.transform).Place(Vector2.zero, Vector2.one, new Vector2(50, 110), new Vector2(-50, -110));
            modalClosed = onClosed;
            var close = UiKit.Button(panel.transform, doneLabel, UiKit.Cyan, CloseModal, 32);
            close.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 24), new Vector2(150, 92));
            Select(close);
            return body;
        }

        public void CloseModal()
        {
            if (!modal) return;
            Destroy(modal);
            modal = null;
            modalClosedFrame = Time.frameCount;
            var then = modalClosed;
            modalClosed = null;
            if (modalReturnFocus && modalReturnFocus.activeInHierarchy) Select(modalReturnFocus.GetComponent<Selectable>());
            then?.Invoke();
        }

        public void ShowHowTo()
        {
            var body = OpenModal("How to Play", 1400, 980);
            var text =
                "<b>Classic.</b> Take turns dropping counters into the seven columns. They fall to the lowest free space. " +
                "Connect <b>four in a row</b> across, up or diagonally to win the round. A full board with no line is a draw. " +
                "Matches are <b>first to three</b> round wins, and the first move swaps every round.\n\n" +
                "<b>Battle.</b> Ten-second turns (adjustable). Each player has one <b>Bomb</b> and one <b>Swap</b> per round, shown on both player cards. A power uses your whole turn.\n" +
                "  <b>Bomb</b> removes one opponent counter; everything above it drops down.\n" +
                "  <b>Swap</b> exchanges two touching counters, one of each colour, side by side or above and below (not diagonal).\n" +
                "After every move both players are checked. If only your opponent has four, they win, even if your move made it. " +
                "If both have four at once, it's a draw. When the timer runs out a counter drops automatically (centre first); powers are never used for you.\n\n" +
                "<b>Mouse</b>  point at a column and click.   <b>Touch</b>  tap a column, tap again to drop.\n" +
                "<b>Keyboard</b>  ← → or A D choose a column, Enter or Space drop. B or 1 Bomb, X or 2 Swap, arrows move the target, Enter picks and confirms, Backspace steps back, Esc cancels or pauses.\n" +
                "<b>Gamepad</b>  d-pad or stick choose, A drop or confirm, X Bomb, Y Swap, B cancel, Start pause.\n\n" +
                "<color=#FF9429>Orange counters carry a star</color>; <color=#33E0FF>cyan counters carry a ring</color>, so colour is never the only clue.";
            var t = UiKit.Label(body, text, 28, UiKit.TextColour, UiKit.Bold, TextAnchor.UpperLeft);
            t.rectTransform.Fill();
            t.lineSpacing = 1.08f;
        }

        public void ShowBattleIntro(Action then)
        {
            var body = OpenModal("Battle Briefing", 1300, 900, "Let's Battle", then);
            var stack = UiKit.Rect("Stack", body).Fill();
            UiKit.Stack(stack, 18);
            void Item(Sprite icon, Color c, string head, string text)
            {
                var row = UiKit.Rect(head, stack); row.Size(170);
                var ic = UiKit.Icon(row, icon, c, 120); ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0, 0.5f); ic.rectTransform.anchoredPosition = new Vector2(70, 0);
                var h = UiKit.Label(row, head, 40, c, UiKit.Title, TextAnchor.UpperLeft); h.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(160, 0), new Vector2(0, -6));
                var tx = UiKit.Label(row, text, 28, UiKit.TextColour, UiKit.Bold, TextAnchor.UpperLeft); tx.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(160, 0), new Vector2(0, -56));
            }
            Item(UiKit.BombIcon, UiKit.Red, "Bomb ×1", "Pick any opponent counter and confirm. It's removed and the counters above it fall down.");
            Item(UiKit.SwapIcon, UiKit.Cyan, "Swap ×1", "Pick two touching counters, one of each colour (side by side or above/below) and confirm. They change places.");
            var rules = UiKit.Label(stack, "Powers reset every round, use your whole turn, and can be cancelled before you confirm. " +
                "Careful: if a power gives your opponent four in a row, they win. Both four at once is a draw. Turns last 10 seconds; " +
                "if time runs out you drop a counter automatically.", 28, UiKit.Muted, UiKit.Bold, TextAnchor.UpperLeft);
            rules.Size(170);
        }

        public void ShowSettings()
        {
            var body = OpenModal("Settings", 1200, 880);
            var stack = UiKit.Rect("Stack", body).Fill();
            UiKit.Stack(stack, 18);
            void SliderRow(string label, float value, Action<float> set)
            {
                var row = UiKit.Rect(label, stack); row.Size(70);
                var l = UiKit.Label(row, label, 32, UiKit.TextColour, UiKit.Bold, TextAnchor.MiddleLeft);
                l.rectTransform.Place(Vector2.zero, new Vector2(0.35f, 1f), Vector2.zero, Vector2.zero);
                var s = UiKit.Slider(row, value, set);
                s.GetComponent<RectTransform>().Place(new Vector2(0.38f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0, -20), new Vector2(-10, 20));
            }
            SliderRow("Music", Settings.Music, v => { Settings.Music = v; game.ApplySettings(); });
            SliderRow("Sound effects", Settings.Sound, v => { Settings.Sound = v; game.ApplySettings(); Sfx.Play(Sfx.Kind.Hover, 0.8f); });
            Button quality = null, motion = null, shake = null;
            quality = UiKit.Button(stack, "", UiKit.ButtonColour, () =>
            {
                Settings.Quality = (Settings.Quality + 1) % 3; game.ApplySettings(); Settings.Save();
                UiKit.LabelOf(quality).text = $"Graphics: {Settings.QualityNames[Settings.Quality]}";
            }, 32);
            UiKit.LabelOf(quality).text = $"Graphics: {Settings.QualityNames[Mathf.Clamp(Settings.Quality, 0, 2)]}";
            quality.Size(74);
            motion = UiKit.Button(stack, "", UiKit.ButtonColour, () =>
            {
                Settings.ReducedMotion = !Settings.ReducedMotion; Settings.Save();
                UiKit.LabelOf(motion).text = "Reduced motion: " + (Settings.ReducedMotion ? "On" : "Off");
            }, 32);
            UiKit.LabelOf(motion).text = "Reduced motion: " + (Settings.ReducedMotion ? "On" : "Off");
            motion.Size(74);
            shake = UiKit.Button(stack, "", UiKit.ButtonColour, () =>
            {
                Settings.CameraShake = !Settings.CameraShake; Settings.Save();
                UiKit.LabelOf(shake).text = "Camera shake: " + (Settings.CameraShake ? "On" : "Off");
            }, 32);
            UiKit.LabelOf(shake).text = "Camera shake: " + (Settings.CameraShake ? "On" : "Off");
            shake.Size(74);
            var note = UiKit.Label(stack, "Reduced motion shortens animations and turns off bouncing, camera movement and most particles.", 24, UiKit.Muted, UiKit.Bold);
            note.Size(60);
            Select(quality);
        }

        public void ShowStats()
        {
            var body = OpenModal("Solo Stats", 1100, 760);
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>Against          Matches won / lost          Rounds won / lost / drawn</b>\n\n");
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
                sb.Append($"{d,-12}           {Settings.Stat(d, "matchWon")}  /  {Settings.Stat(d, "matchLost")}" +
                          $"                          {Settings.Stat(d, "roundWon")}  /  {Settings.Stat(d, "roundLost")}  /  {Settings.Stat(d, "roundDraw")}\n\n");
            sb.Append("<color=#9EA8CC>Stats are saved on this device.</color>");
            var t = UiKit.Label(body, sb.ToString(), 32, UiKit.TextColour, UiKit.Bold, TextAnchor.UpperCenter);
            t.rectTransform.Fill();
        }

        // ================================================================== pause

        void BuildPause()
        {
            pause = UiKit.Rect("Pause", root).Fill().gameObject;
            var shade = UiKit.Box(pause.transform, "Shade", UiKit.Shade, UiKit.RoundedSmall);
            shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            var panel = UiKit.Panel(pause.transform, "Panel");
            panel.rectTransform.Center(new Vector2(640, 720));
            var col = UiKit.Rect("Menu", panel.transform).Place(Vector2.zero, Vector2.one, new Vector2(60, 50), new Vector2(-60, -40));
            UiKit.Stack(col, 16);
            UiKit.Label(col, "Paused", 70, UiKit.Cyan, UiKit.Title).Size(100);
            UiKit.Button(col, "Resume", UiKit.Orange, () => game.SetPaused(false), 38).Size(84);
            UiKit.Button(col, "Restart Match", UiKit.ButtonColour, () => game.Restart(), 32).Size(72);
            UiKit.Button(col, "How to Play", UiKit.ButtonColour, ShowHowTo, 32).Size(72);
            UiKit.Button(col, "Settings", UiKit.ButtonColour, ShowSettings, 32).Size(72);
            UiKit.Button(col, "Quit to Menu", UiKit.ButtonColour, () => game.QuitToMenu(), 32).Size(72);
            pause.SetActive(false);
        }

        public void ShowPause(bool on)
        {
            if (!on) CloseModal();
            pause.SetActive(on);
            if (on) Select(FirstButton(pause));
            else if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
        }

        // ================================================================== HUD

        void BuildHud()
        {
            hud = UiKit.Rect("HUD", root).Fill().gameObject;
            cards[1] = Card(Player.One, new Vector2(0, 1), new Vector2(24, -24));
            cards[2] = Card(Player.Two, new Vector2(1, 1), new Vector2(-24, -24));

            // turn banner + timer
            var banner = UiKit.Box(hud.transform, "Banner", UiKit.Edge);
            banner.rectTransform.anchorMin = banner.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            banner.rectTransform.pivot = new Vector2(0.5f, 1f);
            banner.rectTransform.sizeDelta = new Vector2(620, 92);
            banner.rectTransform.anchoredPosition = new Vector2(0, -24);
            banner.raycastTarget = false;
            bannerEdge = banner;
            bannerRect = banner.rectTransform;
            var inner = UiKit.Box(banner.transform, "Inner", UiKit.PanelColour);
            inner.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            inner.raycastTarget = false;
            bannerSymbol = UiKit.Icon(banner.transform, UiKit.Star, UiKit.Orange, 56);
            bannerSymbol.rectTransform.anchorMin = bannerSymbol.rectTransform.anchorMax = new Vector2(0, 0.5f);
            bannerSymbol.rectTransform.anchoredPosition = new Vector2(52, 4);
            bannerText = UiKit.Label(banner.transform, "", 40, UiKit.TextColour, UiKit.Title);
            bannerText.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(90, 14), new Vector2(-20, -4));
            timerBar = UiKit.Rect("Timer", banner.transform).Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(18, 10), new Vector2(-90, 22));
            var track = UiKit.Box(timerBar, "Track", new Color(0.2f, 0.22f, 0.38f), UiKit.RoundedSmall); track.rectTransform.Fill(); track.raycastTarget = false;
            timerFill = UiKit.Box(timerBar, "Fill", UiKit.Cyan, UiKit.RoundedSmall); timerFill.rectTransform.Fill(); timerFill.raycastTarget = false;
            timerText = UiKit.Label(banner.transform, "", 34, UiKit.TextColour, UiKit.Title, TextAnchor.MiddleRight);
            timerText.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 0), new Vector2(-14, 34));

            pauseButton = UiKit.Button(hud.transform, "II", UiKit.ButtonColour, () => game.SetPaused(true), 34);
            var pr = pauseButton.GetComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f); pr.pivot = new Vector2(0.5f, 1f);
            pr.sizeDelta = new Vector2(84, 84); pr.anchoredPosition = new Vector2(370, -28);
            pauseButton.navigation = new Navigation { mode = Navigation.Mode.None };
            pauseRect = pr;

            // prompt + action buttons along the bottom
            promptText = UiKit.Label(hud.transform, "", 34, UiKit.TextColour, UiKit.Bold);
            promptText.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 150), new Vector2(-40, 200));
            promptText.Outlined(UiKit.Ink, 2f);
            promptRect = promptText.rectTransform;

            actionBar = UiKit.Rect("Actions", hud.transform).Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-560, 22), new Vector2(560, 140)).gameObject;
            actionRect = (RectTransform)actionBar.transform;
            powerGroup = UiKit.Rect("Powers", actionBar.transform).Fill().gameObject;
            UiKit.Row((RectTransform)powerGroup.transform, 40);
            bombButton = PowerButton(powerGroup.transform, "BOMB", UiKit.BombIcon, UiKit.Red, () => game.BombButton(), out bombReason);
            swapButton = PowerButton(powerGroup.transform, "SWAP", UiKit.SwapIcon, UiKit.Cyan, () => game.SwapButton(), out swapReason);
            targetGroup = UiKit.Rect("Targeting", actionBar.transform).Fill().gameObject;
            UiKit.Row((RectTransform)targetGroup.transform, 40);
            confirmButton = UiKit.Button(targetGroup.transform, "CONFIRM", UiKit.Green, () => game.ConfirmButton(), 40);
            cancelButton = UiKit.Button(targetGroup.transform, "CANCEL", UiKit.ButtonColour, () => game.CancelAll(), 40);
            foreach (var b in new[] { bombButton, swapButton, confirmButton, cancelButton }) b.navigation = new Navigation { mode = Navigation.Mode.None };
            hud.SetActive(false);
        }

        Button PowerButton(Transform parent, string label, Sprite icon, Color colour, UnityEngine.Events.UnityAction onClick, out Text reason)
        {
            var b = UiKit.Button(parent, "", UiKit.ButtonColour, onClick, 36);
            var edge = UiKit.Box(b.transform, "Edge", colour, UiKit.Rounded);
            edge.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(10, 4), new Vector2(-10, 10));
            edge.raycastTarget = false;
            var ic = UiKit.Icon(b.transform, icon, colour, 64);
            ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0, 0.5f);
            ic.rectTransform.anchoredPosition = new Vector2(56, 6);
            var t = UiKit.LabelOf(b);
            t.text = label;
            t.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(90, 34), new Vector2(-10, 0));
            reason = UiKit.Label(b.transform, "", 20, UiKit.Muted, UiKit.Bold);
            reason.rectTransform.Place(Vector2.zero, new Vector2(1, 0), new Vector2(90, 10), new Vector2(-12, 46));
            return b;
        }

        SeatCard Card(Player p, Vector2 anchor, Vector2 pos)
        {
            var c = new SeatCard();
            var edge = UiKit.Box(hud.transform, $"Card {p}", UiKit.Edge);
            var r = edge.rectTransform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = anchor;
            r.sizeDelta = new Vector2(470, 176);
            r.anchoredPosition = pos;
            edge.raycastTarget = false;
            c.Edge = edge;
            var inner = UiKit.Box(edge.transform, "Inner", UiKit.PanelColour);
            inner.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            inner.raycastTarget = false;
            c.Symbol = UiKit.Icon(edge.transform, Sym(p), Col(p), 72);
            c.Symbol.rectTransform.anchorMin = c.Symbol.rectTransform.anchorMax = new Vector2(0, 1);
            c.Symbol.rectTransform.anchoredPosition = new Vector2(60, -58);
            c.Name = UiKit.Label(edge.transform, "", 40, UiKit.TextColour, UiKit.Title, TextAnchor.UpperLeft);
            c.Name.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(112, -66), new Vector2(-14, -14));
            c.Role = UiKit.Label(edge.transform, "", 22, UiKit.Muted, UiKit.Bold, TextAnchor.UpperLeft);
            c.Role.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(114, -96), new Vector2(-14, -62));
            // round wins
            c.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var pip = UiKit.Box(edge.transform, "Pip", Color.white, UiKit.Circle);
                pip.raycastTarget = false;
                pip.rectTransform.anchorMin = pip.rectTransform.anchorMax = new Vector2(0, 0);
                pip.rectTransform.sizeDelta = new Vector2(30, 30);
                pip.rectTransform.anchoredPosition = new Vector2(40 + i * 40, 36);
                c.Pips[i] = pip;
            }
            // powers in hand (visible to both players)
            c.Powers = UiKit.Rect("Powers", edge.transform).Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(170, 12), new Vector2(-14, 62)).gameObject;
            c.BombIcon = UiKit.Icon(c.Powers.transform, UiKit.BombIcon, UiKit.Red, 42);
            c.BombIcon.rectTransform.anchorMin = c.BombIcon.rectTransform.anchorMax = new Vector2(0, 0.5f);
            c.BombIcon.rectTransform.anchoredPosition = new Vector2(30, 0);
            c.BombCount = UiKit.Label(c.Powers.transform, "", 30, UiKit.TextColour, UiKit.Title, TextAnchor.MiddleLeft);
            c.BombCount.rectTransform.Place(Vector2.zero, new Vector2(0.5f, 1), new Vector2(56, 0), Vector2.zero);
            c.SwapIcon = UiKit.Icon(c.Powers.transform, UiKit.SwapIcon, UiKit.Cyan, 42);
            c.SwapIcon.rectTransform.anchorMin = c.SwapIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            c.SwapIcon.rectTransform.anchoredPosition = new Vector2(20, 0);
            c.SwapCount = UiKit.Label(c.Powers.transform, "", 30, UiKit.TextColour, UiKit.Title, TextAnchor.MiddleLeft);
            c.SwapCount.rectTransform.Place(new Vector2(0.5f, 0), Vector2.one, new Vector2(48, 0), Vector2.zero);
            return c;
        }

        void BuildIntro()
        {
            intro = UiKit.Rect("Round Intro", root).Fill().gameObject;
            introGroup = intro.AddComponent<CanvasGroup>();
            introGroup.blocksRaycasts = false;
            var band = UiKit.Box(intro.transform, "Band", new Color(0.02f, 0.02f, 0.08f, 0.75f), UiKit.RoundedSmall);
            band.rectTransform.Place(new Vector2(0, 0.42f), new Vector2(1, 0.62f), new Vector2(-20, 0), new Vector2(20, 0));
            band.raycastTarget = false;
            introTitle = UiKit.Label(intro.transform, "", 110, UiKit.TextColour, UiKit.Title);
            introTitle.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(1, 0.62f), Vector2.zero, Vector2.zero);
            introTitle.Outlined(new Color(0.35f, 0.1f, 0.5f), 4f);
            introSub = UiKit.Label(intro.transform, "", 40, UiKit.TextColour, UiKit.Bold);
            introSub.rectTransform.Place(new Vector2(0, 0.42f), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
            intro.SetActive(false);
        }

        public void RoundIntro(int round, Player starter)
        {
            StopCoroutine(nameof(IntroAnim));
            intro.SetActive(true);
            introTitle.text = $"ROUND {round}";
            introSub.text = $"<color=#{ColorUtility.ToHtmlStringRGB(Col(starter))}>{NameOf(starter)}</color> {(NameOf(starter) == "You" ? "go" : "goes")} first";
            StartCoroutine(nameof(IntroAnim));
        }

        IEnumerator IntroAnim()
        {
            float dur = Settings.ReducedMotion ? 0.9f : 1.4f;
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                introGroup.alpha = Mathf.Clamp01(Mathf.Min(k * 6f, (1f - k) * 4f));
                float s = Settings.ReducedMotion ? 1f : 1.15f - 0.15f * Mathf.Clamp01(k * 4f);
                introTitle.rectTransform.localScale = Vector3.one * s;
                yield return null;
            }
            intro.SetActive(false);
        }

        /// <summary>Refresh the HUD from the controller's state (every frame; cheap).</summary>
        public void UpdateHud(GameController g)
        {
            if (!hud.activeSelf || g.Round == null || g.Config.Demo) return;
            var round = g.Round;
            var match = g.Match;
            bool battle = round.Rules.PowersEnabled;
            bool live = g.Current == GameController.Phase.HumanTurn || g.Current == GameController.Phase.AiTurn || g.Current == GameController.Phase.Resolving;
            for (int i = 1; i <= 2; i++)
            {
                var p = (Player)i;
                var c = cards[i];
                bool active = live && round.Current == p && !round.IsOver;
                c.Edge.color = active ? Col(p) * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 4f)) : new Color(0.25f, 0.28f, 0.45f, 0.8f);
                c.Name.text = NameOf(p);
                c.Role.text = (p == Player.One ? "Orange · star" : "Cyan · ring") + (g.IsComputer(p) && !g.Config.Watch ? $" · {g.Config.Difficulty}" : "");
                for (int k = 0; k < 3; k++) c.Pips[k].color = k < match.Wins(p) ? Col(p) : new Color(0.22f, 0.24f, 0.4f);
                c.Powers.SetActive(battle);
                if (battle)
                {
                    int b = round.BombsLeft(p), s = round.SwapsLeft(p);
                    c.BombCount.text = $"×{b}"; c.BombIcon.color = b > 0 ? UiKit.Red : new Color(0.3f, 0.3f, 0.4f);
                    c.SwapCount.text = $"×{s}"; c.SwapIcon.color = s > 0 ? UiKit.Cyan : new Color(0.3f, 0.3f, 0.4f);
                    c.BombCount.color = b > 0 ? UiKit.TextColour : UiKit.Muted; c.SwapCount.color = s > 0 ? UiKit.TextColour : UiKit.Muted;
                }
            }

            // banner
            var me = round.Current;
            bannerSymbol.sprite = Sym(me);
            bannerSymbol.color = Col(me);
            bannerEdge.color = live ? Col(me) : UiKit.Edge;
            if (round.IsOver) bannerText.text = round.Winner == Player.None ? "DRAW" : $"{NameOf(round.Winner).ToUpper()} {(NameOf(round.Winner) == "You" ? "WIN" : "WINS")}";
            else if (g.Current == GameController.Phase.AiTurn && g.AiThinking) bannerText.text = "THINKING" + new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
            else bannerText.text = NameOf(me) == "You" ? "YOUR TURN" : $"{NameOf(me).ToUpper()}'S TURN";
            bool timer = g.Clock.Enabled && !round.IsOver;
            timerBar.gameObject.SetActive(timer);
            timerText.gameObject.SetActive(timer);
            if (timer)
            {
                float k = g.Clock.Limit > 0 ? g.Clock.Remaining / g.Clock.Limit : 0f;
                timerFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(k), 1f);
                bool low = g.Clock.Remaining <= 3.01f;
                timerFill.color = low ? UiKit.Red : UiKit.Cyan;
                timerText.text = g.Clock.DisplaySeconds.ToString();
                timerText.color = low ? UiKit.Red : UiKit.TextColour;
            }

            // prompt: recent feedback first, otherwise what to do now
            string prompt;
            if (g.Feedback != null && Time.unscaledTime < g.FeedbackUntil) prompt = g.Feedback;
            else prompt = Prompt(g);
            promptText.text = prompt;

            // action buttons (human turn only)
            bool human = g.Current == GameController.Phase.HumanTurn && !g.Paused;
            actionBar.SetActive(human && (battle || g.Selection.Targeting || g.LastDevice == InputKind.Touch));
            bool targeting = g.Selection.Targeting;
            powerGroup.SetActive(human && !targeting && battle);
            targetGroup.SetActive(human && targeting);
            if (human && !targeting && battle)
            {
                bombButton.interactable = g.PowerAvailable(ActionKind.Bomb, out string whyB);
                swapButton.interactable = g.PowerAvailable(ActionKind.Swap, out string whyS);
                bombReason.text = bombButton.interactable ? (g.LastDevice == InputKind.Gamepad ? "X" : g.LastDevice == InputKind.Keyboard ? "B" : "") : whyB;
                swapReason.text = swapButton.interactable ? (g.LastDevice == InputKind.Gamepad ? "Y" : g.LastDevice == InputKind.Keyboard ? "X" : "") : whyS;
            }
            if (human && targeting) confirmButton.interactable = g.Selection.AwaitingConfirm;
            pauseButton.gameObject.SetActive(g.CanPause && !g.Paused);
        }

        string Prompt(GameController g)
        {
            var dev = g.LastDevice;
            switch (g.Current)
            {
                case GameController.Phase.AiTurn: return "";
                case GameController.Phase.HumanTurn:
                    if (g.Selection.Targeting)
                    {
                        string msg = g.Selection.Message ?? "";
                        string keys = dev == InputKind.Gamepad ? "  (A pick · B back)" : dev == InputKind.Keyboard ? "  (arrows · Enter · Backspace)" : "";
                        return msg + keys;
                    }
                    switch (dev)
                    {
                        case InputKind.Touch: return g.TouchArmedColumn >= 0 ? "Tap the same column again to drop" : "Tap a column to aim";
                        case InputKind.Keyboard: return "← → choose a column · Enter drop" + (g.Round.Rules.PowersEnabled ? " · B Bomb · X Swap" : "");
                        case InputKind.Gamepad: return "D-pad choose · A drop" + (g.Round.Rules.PowersEnabled ? " · X Bomb · Y Swap" : "");
                        default: return "Click a column to drop" + (g.Round.Rules.PowersEnabled ? " · or use a power" : "");
                    }
            }
            return "";
        }

        // ================================================================== results

        public void HideResults()
        {
            if (game && game.Rig) game.Rig.SideShift = 0f;
            foreach (Transform c in results.transform) Destroy(c.gameObject);
            results.SetActive(false);
        }

        /// <summary>Result panels sit beside the board (landscape) or below it (portrait) so the winning line stays visible.</summary>
        RectTransform ResultPanel(float w, float h, bool dim)
        {
            HideResults();
            results.SetActive(true);
            if (dim)
            {
                var shade = UiKit.Box(results.transform, "Shade", new Color(0.01f, 0.01f, 0.05f, 0.45f), UiKit.RoundedSmall);
                shade.rectTransform.Place(Vector2.zero, Vector2.one, new Vector2(-50, -50), new Vector2(50, 50));
            }
            var panel = UiKit.Panel(results.transform, "Panel");
            var r = panel.rectTransform;
            bool landscape = Screen.width >= Screen.height * 1.25f;
            if (game && game.Rig) game.Rig.SideShift = landscape ? 0.15f : 0f;
            if (landscape) { r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(1f, 0.5f); r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(-30, -40); }
            else { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f); r.pivot = new Vector2(0.5f, 0f); r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(0, 30); }
            return r;
        }

        string ScoreLine(Match m) =>
            $"<color=#FF9429>{NameOf(Player.One)}  {m.Wins(Player.One)}</color>   —   <color=#33E0FF>{m.Wins(Player.Two)}  {NameOf(Player.Two)}</color>";

        static string Reason(MoveResult r)
        {
            if (r.Outcome == RoundOutcome.Draw) return r.Lines.Count > 1 ? "Both players made four in a row with the same move." : "The board is full with no four in a row.";
            if (r.GiftedWin) return r.Action.Kind == ActionKind.Swap ? "That Swap handed over four in a row!" : "That Bomb handed over four in a row!";
            if (r.Timeout) return "Time ran out, and the automatic drop made four!";
            return r.Action.Kind == ActionKind.Bomb ? "Four in a row, thanks to a Bomb!" : r.Action.Kind == ActionKind.Swap ? "Four in a row, thanks to a Swap!" : "Four in a row!";
        }

        public void ShowRoundResult(GameController g, MoveResult r)
        {
            var panel = ResultPanel(560, 470, false);
            var w = g.Round.Winner;
            string head = w == Player.None ? "Round drawn" : NameOf(w) == "You" ? "You win the round!" : $"{NameOf(w)} wins the round";
            var h = UiKit.Label(panel, head, 52, w == Player.None ? UiKit.TextColour : Col(w), UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -130), new Vector2(-20, -20));
            var d = UiKit.Label(panel, Reason(r), 28, UiKit.Muted, UiKit.Bold);
            d.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -210), new Vector2(-30, -130));
            var s = UiKit.Label(panel, ScoreLine(g.Match), 34, UiKit.TextColour, UiKit.Title);
            s.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -290), new Vector2(-20, -220));
            var next = UiKit.Button(panel, "Next Round", UiKit.Orange, () => g.NextRound(), 40);
            next.GetComponent<RectTransform>().Place(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-190, 40), new Vector2(190, 130));
            Select(next);
        }

        public void ShowMatchResult(GameController g, MoveResult r)
        {
            var panel = ResultPanel(600, 640, false);
            var w = g.Match.Winner;
            bool you = NameOf(w) == "You";
            string head = you ? "You win the match!" : g.Config.Solo && !g.Config.Watch ? "The computer wins" : $"{NameOf(w)} wins the match!";
            var h = UiKit.Label(panel, head, 56, Col(w), UiKit.Title);
            h.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -150), new Vector2(-20, -20));
            var s = UiKit.Label(panel, ScoreLine(g.Match), 34, UiKit.TextColour, UiKit.Title);
            s.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -230), new Vector2(-20, -160));
            string detail = $"{g.Match.RoundNumber} rounds · {g.Config.Mode}" + (g.Config.Solo && !g.Config.Watch
                ? $" · {g.Config.Difficulty}\nYour record against {g.Config.Difficulty}: {Settings.Stat(g.Config.Difficulty, "matchWon")} won, {Settings.Stat(g.Config.Difficulty, "matchLost")} lost"
                : "");
            var d = UiKit.Label(panel, detail, 28, UiKit.Muted, UiKit.Bold);
            d.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -400), new Vector2(-30, -250));
            var buttons = UiKit.Rect("Buttons", panel).Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(40, 40), new Vector2(-40, 210));
            UiKit.Stack(buttons, 16);
            var again = UiKit.Button(buttons, "Rematch", UiKit.Orange, () => g.Rematch(), 40);
            again.Size(80);
            UiKit.Button(buttons, "Main Menu", UiKit.ButtonColour, () => g.QuitToMenu(), 34).Size(70);
            Select(again);
        }
    }
}

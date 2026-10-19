using System.Collections;
using System.Collections.Generic;
using DropZone.Core;
using UnityEngine;

namespace DropZone
{
    /// <summary>
    /// Draws the logical board. It never decides anything: the controller applies an action to the
    /// RoundState first, then asks this view to animate the result, then re-syncs from the state so
    /// the screen always matches the rules. No physics decides where a counter goes.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [Header("Layout (local space)")]
        [Tooltip("Centre of cell (0,0), the bottom-left slot.")] public Vector3 Origin = new Vector3(-3f, 1.05f, 0f);
        public float CellSize = 1f;
        [Tooltip("Height of the preview counter above the board (local Y).")] public float PreviewY = 7.35f;
        [Tooltip("Z of highlight rings, just in front of the front plate.")] public float MarkerZ = -0.42f;

        [Header("Prefabs")]
        public CounterView CounterOne;
        public CounterView CounterTwo;
        public Transform Ring;           // flat emissive ring used for every highlight
        public Transform ColumnBeam;     // soft vertical glow over the selected column
        public Transform WinBeam;        // glowing bar drawn through a winning line

        [Header("Highlight colours")]
        public Color CursorColour = new Color(1f, 1f, 1f, 0.9f);
        public Color BombColour = new Color(1f, 0.32f, 0.2f, 1f);
        public Color SwapColour = new Color(0.35f, 0.95f, 1f, 1f);
        public Color OneColour = new Color(1f, 0.55f, 0.12f);
        public Color TwoColour = new Color(0.15f, 0.85f, 1f);

        [Header("Animation")]
        public float Gravity = 42f;
        public float BounceHeight = 0.14f;
        public float LandSquash = 0.22f;
        public float SwapTime = 0.55f;
        public float BombWindup = 0.35f;
        public float WinBeamTime = 0.45f;

        [Header("Links")]
        public Fx Effects;
        public CameraRig Rig;

        readonly CounterView[,] grid = new CounterView[Board.Columns, Board.Rows];
        readonly Stack<CounterView> poolOne = new Stack<CounterView>(), poolTwo = new Stack<CounterView>();
        readonly List<Transform> rings = new List<Transform>();
        readonly List<Transform> winBeams = new List<Transform>();
        CounterView preview;
        int previewColumn = 3;
        float previewX;
        bool previewVisible;

        public bool Busy { get; private set; }
        static bool Reduced => Settings.ReducedMotion;

        public Color ColourOf(Player p) => p == Player.One ? OneColour : TwoColour;

        void Awake()
        {
            Validate();
            if (ColumnBeam) ColumnBeam.gameObject.SetActive(false);
            if (Ring) Ring.gameObject.SetActive(false);
            if (WinBeam) WinBeam.gameObject.SetActive(false);
        }

        /// <summary>Scene references are checked once at start-up so a broken scene fails loudly.</summary>
        public void Validate()
        {
            if (!CounterOne || !CounterTwo || !Ring || !ColumnBeam || !WinBeam || !Effects || !Rig)
                Debug.LogError("DZ_SCENE_ERROR BoardView is missing a reference");
        }

        // ------------------------------------------------------------------ positions

        public Vector3 CellLocal(int c, int r) => Origin + new Vector3(c * CellSize, r * CellSize, 0f);
        public Vector3 CellWorld(Cell cell) => transform.TransformPoint(CellLocal(cell.Column, cell.Row));
        Vector3 PreviewLocal(int column) => new Vector3(Origin.x + column * CellSize, PreviewY, 0f);

        /// <summary>Which column (and cell, if over the grid) a screen ray points at. Columns extend above the board.</summary>
        public bool Pick(Ray worldRay, out int column, out Cell cell)
        {
            column = -1; cell = new Cell(-1, -1);
            var plane = new Plane(transform.forward * -1f, transform.TransformPoint(new Vector3(0, 0, MarkerZ)));
            if (!plane.Raycast(worldRay, out float dist)) return false;
            var local = transform.InverseTransformPoint(worldRay.GetPoint(dist));
            float fc = (local.x - Origin.x) / CellSize, fr = (local.y - Origin.y) / CellSize;
            int c = Mathf.RoundToInt(fc), r = Mathf.RoundToInt(fr);
            if (c < 0 || c >= Board.Columns || fr < -0.9f || local.y > PreviewY + 1.2f) return false;
            column = c;
            if (r >= 0 && r < Board.Rows && Mathf.Abs(fc - c) < 0.48f && Mathf.Abs(fr - r) < 0.48f) cell = new Cell(c, r);
            return true;
        }

        // ------------------------------------------------------------------ counters

        CounterView Spawn(Player p)
        {
            var pool = p == Player.One ? poolOne : poolTwo;
            var cv = pool.Count > 0 ? pool.Pop() : Instantiate(p == Player.One ? CounterOne : CounterTwo, transform);
            cv.gameObject.SetActive(true);
            cv.Owner = p;
            cv.ResetVisual();
            return cv;
        }

        void Recycle(CounterView cv)
        {
            if (!cv) return;
            cv.ResetVisual();
            cv.gameObject.SetActive(false);
            (cv.Owner == Player.One ? poolOne : poolTwo).Push(cv);
        }

        /// <summary>Make the screen match the board exactly (after animations, restarts, or anything unexpected).</summary>
        public void Sync(Board board)
        {
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                {
                    var want = board[c, r];
                    var have = grid[c, r];
                    if (have && have.Owner != want) { Recycle(have); have = grid[c, r] = null; }
                    if (want != Player.None && !have) have = grid[c, r] = Spawn(want);
                    if (have)
                    {
                        have.transform.localPosition = CellLocal(c, r);
                        have.transform.localRotation = Quaternion.identity;
                    }
                }
        }

        public void ClearAll()
        {
            StopAllCoroutines();
            Busy = false;
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++) { Recycle(grid[c, r]); grid[c, r] = null; }
            ClearWin();
            ClearMarkers();
            ShowPreview(false, Player.One, previewColumn, -1);
        }

        public CounterView At(Cell c) => grid[c.Column, c.Row];

        // ------------------------------------------------------------------ preview + highlights

        /// <summary>The counter hovering above the selected column, plus a glow down the column and a ring where it would land.</summary>
        public void ShowPreview(bool visible, Player p, int column, int landingRow)
        {
            if (visible && (!preview || preview.Owner != p))
            {
                if (preview) Recycle(preview);
                preview = Spawn(p);
                previewX = PreviewLocal(column).x;
            }
            previewVisible = visible;
            previewColumn = column;
            if (preview) preview.gameObject.SetActive(visible);
            if (ColumnBeam)
            {
                ColumnBeam.gameObject.SetActive(visible);
                ColumnBeam.localPosition = new Vector3(PreviewLocal(column).x, ColumnBeam.localPosition.y, ColumnBeam.localPosition.z);
                SetColour(ColumnBeam, landingRow >= 0 ? new Color(ColourOf(p).r, ColourOf(p).g, ColourOf(p).b, 0.22f) : new Color(1f, 0.25f, 0.2f, 0.18f));
            }
            HideRingsFrom(0);
            if (visible && landingRow >= 0) RingAt(0, new Cell(column, landingRow), new Color(ColourOf(p).r, ColourOf(p).g, ColourOf(p).b, 0.75f), 0.95f);
        }

        public void HidePreview() => ShowPreview(false, preview ? preview.Owner : Player.One, previewColumn, -1);

        /// <summary>Highlights for Bomb and Swap selection: valid targets, the chosen cells and the keyboard cursor.</summary>
        public void ShowTargets(RoundState s, TurnInput input, Cell? cursor)
        {
            HidePreview();
            int n = 0;
            bool bomb = input.Current == TurnInput.Stage.BombTarget || input.Current == TurnInput.Stage.BombConfirm;
            var me = s.Current;
            // faint rings on every counter that can be picked
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                {
                    var cell = new Cell(c, r);
                    bool pickable = bomb ? s.Board[cell] == me.Other()
                        : input.Current == TurnInput.Stage.SwapFirst ? s.Board[cell] != Player.None && HasOpposingNeighbour(s, cell)
                        : s.IsLegal(GameAction.Swap(input.First, cell));
                    if (!pickable) continue;
                    var col = bomb ? BombColour : SwapColour;
                    RingAt(n++, cell, new Color(col.r, col.g, col.b, 0.35f), 0.82f);
                }
            if (input.Current == TurnInput.Stage.BombConfirm) RingAt(n++, input.First, BombColour, 1.15f, pulse: true);
            if (input.Current == TurnInput.Stage.SwapSecond || input.Current == TurnInput.Stage.SwapConfirm) RingAt(n++, input.First, SwapColour, 1.12f, pulse: true);
            if (input.Current == TurnInput.Stage.SwapConfirm) RingAt(n++, input.Second, SwapColour, 1.12f, pulse: true);
            if (cursor.HasValue && Board.InBounds(cursor.Value)) RingAt(n++, cursor.Value, CursorColour, 1.28f);
            HideRingsFrom(n);
        }

        static bool HasOpposingNeighbour(RoundState s, Cell c)
        {
            var o = s.Board[c].Other();
            return (c.Column > 0 && s.Board[c.Column - 1, c.Row] == o) || (c.Column < Board.Columns - 1 && s.Board[c.Column + 1, c.Row] == o)
                || (c.Row > 0 && s.Board[c.Column, c.Row - 1] == o) || (c.Row < Board.Rows - 1 && s.Board[c.Column, c.Row + 1] == o);
        }

        public void ClearMarkers() { HideRingsFrom(0); if (ColumnBeam) ColumnBeam.gameObject.SetActive(false); }

        void RingAt(int index, Cell cell, Color colour, float scale, bool pulse = false)
        {
            while (rings.Count <= index)
            {
                var r = Instantiate(Ring, transform);
                r.name = "Ring " + rings.Count;
                rings.Add(r);
            }
            var ring = rings[index];
            ring.gameObject.SetActive(true);
            ring.localPosition = CellLocal(cell.Column, cell.Row) + new Vector3(0, 0, MarkerZ);
            ring.localScale = Vector3.one * scale * (pulse && !Reduced ? 1f + 0.06f * Mathf.Sin(Time.time * 8f) : 1f);
            SetColour(ring, colour);
        }

        void HideRingsFrom(int i) { for (; i < rings.Count; i++) rings[i].gameObject.SetActive(false); }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock block;
        static void SetColour(Transform t, Color c)
        {
            block = block ?? new MaterialPropertyBlock();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, c);
                r.SetPropertyBlock(block);
            }
        }

        void Update()
        {
            if (preview && previewVisible)
            {
                // glide between columns and bob gently
                float target = PreviewLocal(previewColumn).x;
                previewX = Reduced ? target : Mathf.Lerp(previewX, target, 1f - Mathf.Exp(-Time.deltaTime * 22f));
                float bob = Reduced ? 0f : Mathf.Sin(Time.time * 2.6f) * 0.06f;
                preview.transform.localPosition = new Vector3(previewX, PreviewY + bob, 0f);
            }
        }

        // ------------------------------------------------------------------ move animations

        /// <summary>Animate a resolved action. The board state has already changed; this just shows it.</summary>
        public IEnumerator Play(MoveResult r, Board settled)
        {
            Busy = true;
            ClearMarkers();
            switch (r.Action.Kind)
            {
                case ActionKind.Drop: yield return Drop(r.Cell, r.By); break;
                case ActionKind.Bomb: yield return BombAnim(r); break;
                case ActionKind.Swap: yield return SwapAnim(r.Action.A, r.Action.B); break;
            }
            Sync(settled);
            Busy = false;
        }

        IEnumerator Drop(Cell cell, Player p)
        {
            // the preview counter is the one that falls
            CounterView cv;
            Vector3 start;
            if (preview && preview.Owner == p && previewVisible)
            {
                cv = preview; preview = null; previewVisible = false;
                start = cv.transform.localPosition;
                start.x = PreviewLocal(cell.Column).x;
            }
            else
            {
                cv = Spawn(p);
                start = PreviewLocal(cell.Column);
            }
            Sfx.Play(Sfx.Kind.Release, 0.7f);
            var end = CellLocal(cell.Column, cell.Row);
            if (grid[cell.Column, cell.Row]) Recycle(grid[cell.Column, cell.Row]);
            grid[cell.Column, cell.Row] = cv;
            yield return Fall(cv, start, end, Gravity);
            Sfx.Play(Sfx.Kind.Land, 0.9f, 1.15f - cell.Row * 0.04f);
            Effects?.Land(transform.TransformPoint(end) + Vector3.down * 0.35f, ColourOf(p));
            if (!Reduced) { cv.Squash(LandSquash); yield return Bounce(cv, end); }
        }

        IEnumerator Fall(CounterView cv, Vector3 from, Vector3 to, float g)
        {
            cv.transform.localPosition = from;
            float v = 0f, y = from.y;
            if (Reduced) g *= 1.6f;
            while (y > to.y)
            {
                v += g * Time.deltaTime;
                y -= v * Time.deltaTime;
                cv.transform.localPosition = new Vector3(to.x, Mathf.Max(y, to.y), to.z);
                yield return null;
            }
            cv.transform.localPosition = to;
        }

        IEnumerator Bounce(CounterView cv, Vector3 rest)
        {
            float t = 0f, dur = 0.16f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                cv.transform.localPosition = rest + Vector3.up * (BounceHeight * 4f * k * (1f - k));
                yield return null;
            }
            cv.transform.localPosition = rest;
        }

        IEnumerator BombAnim(MoveResult r)
        {
            var target = grid[r.Cell.Column, r.Cell.Row];
            var at = transform.TransformPoint(CellLocal(r.Cell.Column, r.Cell.Row));
            // wind-up: the target shakes and the ring flashes
            RingAt(0, r.Cell, BombColour, 1.2f);
            float t = 0f, windup = Reduced ? BombWindup * 0.5f : BombWindup;
            var basePos = CellLocal(r.Cell.Column, r.Cell.Row);
            while (t < windup)
            {
                t += Time.deltaTime;
                if (target && !Reduced) target.transform.localPosition = basePos + (Vector3)Random.insideUnitCircle * 0.05f * (t / windup);
                yield return null;
            }
            HideRingsFrom(0);
            Sfx.Play(Sfx.Kind.Bomb);
            Effects?.Blast(at);
            Rig?.Shake(0.7f);
            Rig?.Kick(0.6f);
            if (target) { Recycle(target); grid[r.Cell.Column, r.Cell.Row] = null; }
            yield return new WaitForSeconds(Reduced ? 0.1f : 0.22f);
            // everything above falls one slot, bottom first
            int c = r.Cell.Column;
            for (int row = r.Cell.Row + 1; row < Board.Rows; row++)
            {
                var cv = grid[c, row];
                if (!cv) break;
                grid[c, row] = null;
                grid[c, row - 1] = cv;
                StartCoroutine(FallAndClack(cv, CellLocal(c, row), CellLocal(c, row - 1)));
                yield return new WaitForSeconds(Reduced ? 0f : 0.04f);
            }
            yield return new WaitForSeconds(r.Fallen > 0 ? 0.3f : 0.1f);
        }

        IEnumerator FallAndClack(CounterView cv, Vector3 from, Vector3 to)
        {
            yield return Fall(cv, from, to, Gravity * 0.6f);
            Sfx.Play(Sfx.Kind.Fall, 0.7f, 1f + Random.value * 0.1f);
            if (!Reduced) cv.Squash(LandSquash * 0.6f);
        }

        IEnumerator SwapAnim(Cell a, Cell b)
        {
            var ca = grid[a.Column, a.Row];
            var cb = grid[b.Column, b.Row];
            var pa = CellLocal(a.Column, a.Row);
            var pb = CellLocal(b.Column, b.Row);
            Sfx.Play(Sfx.Kind.Swap);
            Effects?.SwapSwirl(transform.TransformPoint(pa), transform.TransformPoint(pb), ca ? ColourOf(ca.Owner) : SwapColour, cb ? ColourOf(cb.Owner) : SwapColour);
            Rig?.Kick(0.35f);
            var mid = (pa + pb) * 0.5f;
            float dur = Reduced ? SwapTime * 0.5f : SwapTime, t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                // the two counters orbit their midpoint, lifting out of the board to pass each other
                float ang = k * Mathf.PI;
                var offA = pa - mid;
                var rot = new Vector3(offA.x * Mathf.Cos(ang) - offA.y * Mathf.Sin(ang), offA.x * Mathf.Sin(ang) + offA.y * Mathf.Cos(ang), 0f);
                float lift = Reduced ? 0f : -0.75f * Mathf.Sin(k * Mathf.PI);
                if (ca) { ca.transform.localPosition = mid + rot + new Vector3(0, 0, lift); ca.transform.localRotation = Quaternion.Euler(0, 0, k * 180f); }
                if (cb) { cb.transform.localPosition = mid - rot + new Vector3(0, 0, lift * 0.6f); cb.transform.localRotation = Quaternion.Euler(0, 0, k * 180f); }
                yield return null;
            }
            grid[a.Column, a.Row] = cb;
            grid[b.Column, b.Row] = ca;
            if (ca) { ca.transform.localPosition = pb; ca.transform.localRotation = Quaternion.identity; ca.Squash(Reduced ? 0f : LandSquash * 0.5f); }
            if (cb) { cb.transform.localPosition = pa; cb.transform.localRotation = Quaternion.identity; cb.Squash(Reduced ? 0f : LandSquash * 0.5f); }
            yield return new WaitForSeconds(0.12f);
        }

        // ------------------------------------------------------------------ win lines

        /// <summary>Draw a glowing bar through each winning line and pulse its counters.</summary>
        public IEnumerator ShowWin(List<WinLine> lines)
        {
            ClearWin();
            Sfx.Play(Sfx.Kind.Reveal);
            foreach (var line in lines)
            {
                var beam = Instantiate(WinBeam, transform);
                beam.gameObject.SetActive(true);
                winBeams.Add(beam);
                var glow = Color.Lerp(ColourOf(line.Owner), Color.white, 0.15f) * 1.6f; glow.a = 1f;
                SetColour(beam, glow);
                StartCoroutine(GrowBeam(beam, line));
                foreach (var c in line.Cells) { var cv = grid[c.Column, c.Row]; if (cv) cv.Pulse(!Reduced); }
            }
            yield return new WaitForSeconds(Reduced ? 0.2f : WinBeamTime + 0.25f);
        }

        IEnumerator GrowBeam(Transform beam, WinLine line)
        {
            var a = CellLocal(line.First.Column, line.First.Row) + new Vector3(0, 0, MarkerZ - 0.05f);
            var b = CellLocal(line.Last.Column, line.Last.Row) + new Vector3(0, 0, MarkerZ - 0.05f);
            var dir = b - a;
            float len = dir.magnitude;
            beam.localRotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
            float t = 0f, dur = Reduced ? 0.01f : WinBeamTime;
            int lit = 0;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float l = len * Mathf.SmoothStep(0, 1, k) + 0.5f;
                beam.localScale = new Vector3(l, 1f, 1f);
                beam.localPosition = a + dir.normalized * ((l - 0.5f) * 0.5f);
                // a little sparkle as the bar reaches each counter
                int reached = Mathf.FloorToInt(k * (line.Cells.Length - 1) + 0.001f);
                while (lit <= reached && lit < line.Cells.Length) { Effects?.WinSparkle(CellWorld(line.Cells[lit]), ColourOf(line.Owner)); lit++; }
                yield return null;
            }
            beam.localScale = new Vector3(len + 0.5f, 1f, 1f);
            beam.localPosition = (a + b) * 0.5f;
        }

        public void ClearWin()
        {
            foreach (var b in winBeams) if (b) Destroy(b.gameObject);
            winBeams.Clear();
            foreach (var cv in grid) if (cv) cv.Pulse(false);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace DropZone.Core
{
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>
    /// Compact copy of a round for searching: two bitboards (7 bits per column, bit 6 of each column
    /// always empty), powers left and who's to move. A value type, so the search copies instead of
    /// undoing moves.
    /// </summary>
    public struct SearchPos
    {
        public ulong P1, P2;
        public byte Bombs1, Bombs2, Swaps1, Swaps2;
        public Player ToMove;

        public const int H = 7;
        public const ulong BottomRow = 0x0040810204081UL;          // bit 0 of every column
        public const ulong Full = BottomRow * 0x3FUL;                // rows 0..5 of every column
        public static ulong ColumnMask(int c) => 0x3FUL << (c * H);
        public static ulong Bit(int c, int r) => 1UL << (c * H + r);

        public ulong Mine => ToMove == Player.One ? P1 : P2;
        public ulong Theirs => ToMove == Player.One ? P2 : P1;
        public ulong Occupied => P1 | P2;
        public int BombsOf(Player p) => p == Player.One ? Bombs1 : Bombs2;
        public int SwapsOf(Player p) => p == Player.One ? Swaps1 : Swaps2;

        public static SearchPos From(RoundState s)
        {
            var p = new SearchPos { ToMove = s.Current };
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                {
                    if (s.Board[c, r] == Player.One) p.P1 |= Bit(c, r);
                    else if (s.Board[c, r] == Player.Two) p.P2 |= Bit(c, r);
                }
            p.Bombs1 = (byte)s.BombsLeft(Player.One); p.Bombs2 = (byte)s.BombsLeft(Player.Two);
            p.Swaps1 = (byte)s.SwapsLeft(Player.One); p.Swaps2 = (byte)s.SwapsLeft(Player.Two);
            return p;
        }

        public static bool HasFour(ulong b)
        {
            ulong m = b & (b >> H); if ((m & (m >> 2 * H)) != 0) return true;           // horizontal
            m = b & (b >> (H - 1)); if ((m & (m >> 2 * (H - 1))) != 0) return true;     // diagonal \
            m = b & (b >> (H + 1)); if ((m & (m >> 2 * (H + 1))) != 0) return true;     // diagonal /
            m = b & (b >> 1); return (m & (m >> 2)) != 0;                               // vertical
        }

        public static int PopCount(ulong x)
        {
            x -= (x >> 1) & 0x5555555555555555UL;
            x = (x & 0x3333333333333333UL) + ((x >> 2) & 0x3333333333333333UL);
            x = (x + (x >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
            return (int)((x * 0x0101010101010101UL) >> 56);
        }

        public int Height(int c) => PopCount(Occupied & ColumnMask(c));
        public bool CanDrop(int c) => (Occupied & Bit(c, Board.Rows - 1)) == 0;
        public bool IsFull => (Occupied & Full) == Full;

        void SetMine(ulong v) { if (ToMove == Player.One) P1 = v; else P2 = v; }

        public void Drop(int c) => SetMine(Mine | Bit(c, Height(c)));

        public void Bomb(int c, int r)
        {
            ulong col = ColumnMask(c), cell = Bit(c, r);
            ulong below = cell - 1, above = ~((cell << 1) - 1);
            P1 = Settle(P1, col, below, above);
            P2 = Settle(P2, col, below, above);
            if (ToMove == Player.One) Bombs1--; else Bombs2--;
        }

        static ulong Settle(ulong b, ulong col, ulong below, ulong above)
        {
            ulong inCol = b & col;
            ulong moved = (inCol & below) | ((inCol & above) >> 1);
            return (b & ~col) | (moved & col);
        }

        public void Swap(ulong cells)
        {
            P1 ^= cells; P2 ^= cells; // one cell belongs to each player, so this exchanges them
            if (ToMove == Player.One) Swaps1--; else Swaps2--;
        }
    }

    /// <summary>
    /// Computer opponent. Separate from rendering: give it a RoundState, step Think() (one step per
    /// frame from a coroutine, or run it to the end in tests) and read Choice. The search is
    /// iterative-deepening alpha-beta on bitboards with move ordering, under a time and node budget,
    /// and it yields between root moves so the browser never freezes (no threads needed).
    /// </summary>
    public sealed class AiPlayer
    {
        public readonly Difficulty Level;
        readonly Random rng;
        public GameAction Choice { get; private set; }
        public int Nodes { get; private set; }
        public int DepthReached { get; private set; }
        public double Milliseconds { get; private set; }
        public bool Cancelled { get; private set; }

        /// <summary>Each step returns after about this long so a frame stays smooth.</summary>
        public double SliceMs = 8;

        const int Win = 1_000_000;
        const int MaxPly = 24;
        readonly ulong[][] moveBits = new ulong[MaxPly][];
        readonly byte[][] moveKinds = new byte[MaxPly][];
        readonly byte[][] moveCols = new byte[MaxPly][];
        readonly byte[][] moveRows = new byte[MaxPly][];
        static readonly ulong[] Windows = BuildWindows();
        static readonly int[] CentreFirst = RoundState.CentreFirst;
        Stopwatch clock;
        long nodeBudget;
        double timeBudget;
        bool abort;
        int powerPlies;

        public AiPlayer(Difficulty level, int seed)
        {
            Level = level;
            rng = new Random(seed);
            for (int i = 0; i < MaxPly; i++)
            {
                moveBits[i] = new ulong[160]; moveKinds[i] = new byte[160];
                moveCols[i] = new byte[160]; moveRows[i] = new byte[160];
            }
        }

        public void Cancel() => Cancelled = true;

        /// <summary>Run to completion (tests, tools).</summary>
        public GameAction ChooseNow(RoundState s)
        {
            var it = Think(s);
            while (it.MoveNext()) { }
            return Choice;
        }

        public IEnumerator Think(RoundState state)
        {
            Cancelled = false;
            clock = Stopwatch.StartNew(); // paused while yielded, so it measures search time only
            sliceStart = 0;
            Nodes = 0; DepthReached = 0;
            var root = SearchPos.From(state);
            bool powers = state.Rules.PowersEnabled;

            int maxDepth;
            switch (Level)
            {
                case Difficulty.Easy: maxDepth = 2; timeBudget = 200; nodeBudget = 20_000; powerPlies = 1; break;
                case Difficulty.Normal: maxDepth = powers ? 3 : 4; timeBudget = 400; nodeBudget = 120_000; powerPlies = 2; break;
                default: maxDepth = 14; timeBudget = powers ? 1100 : 900; nodeBudget = 1_500_000; powerPlies = 2; break;
            }
            if (!powers) powerPlies = 0;

            // root moves
            int n = Generate(ref root, 0, true);
            var rootMoves = new List<int>(n);
            for (int i = 0; i < n; i++) rootMoves.Add(i);
            var rootScores = new int[n];
            var copyBits = (ulong[])moveBits[0].Clone(); var copyKinds = (byte[])moveKinds[0].Clone();
            var copyCols = (byte[])moveCols[0].Clone(); var copyRows = (byte[])moveRows[0].Clone();

            int tolerance = Tolerance;
            int[] best = null;
            int[] lastScores = null;
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                abort = false;
                var scores = new int[n];
                int bestSoFar = -Win - 1;
                foreach (int i in rootMoves)
                {
                    var child = root;
                    // the window stays open a little below the best so near-equal moves get exact scores
                    int lower = bestSoFar <= -Win ? -Win - 1 : bestSoFar - tolerance - 1;
                    int sc = PlayAndScore(ref child, copyKinds[i], copyCols[i], copyRows[i], copyBits[i], depth - 1, lower, Win + 1, 0);
                    scores[i] = sc;
                    if (sc > bestSoFar) bestSoFar = sc;
                    if (abort) break;
                    if (Cancelled) yield break;
                    if (clock.Elapsed.TotalMilliseconds > SliceMsMark)
                    {
                        clock.Stop();
                        yield return null;
                        if (Cancelled) yield break;
                        clock.Start();
                        MarkSlice();
                    }
                }
                if (abort && best != null) break;   // incomplete depth: keep the last full one
                lastScores = scores;
                best = scores;
                DepthReached = depth;
                if (abort) break;
                // order the next iteration best-first
                rootMoves.Sort((a, b) => scores[b].CompareTo(scores[a]));
                if (scores[rootMoves[0]] >= Win - 100 || scores[rootMoves[0]] <= -Win + 100) break; // forced result found
                if (clock.Elapsed.TotalMilliseconds > timeBudget * 0.35) break; // the next depth wouldn't finish in time
            }
            Choice = Pick(lastScores, copyBits, copyKinds, copyCols, copyRows, n, state);
            Milliseconds = clock.Elapsed.TotalMilliseconds;
        }

        double sliceStart;
        double SliceMsMark => sliceStart + SliceMs;
        void MarkSlice() => sliceStart = clock.Elapsed.TotalMilliseconds;

        int Tolerance => Level == Difficulty.Hard ? 0 : Level == Difficulty.Normal ? 6 : 40;

        GameAction Pick(int[] scores, ulong[] bitsArr, byte[] kinds, byte[] cols, byte[] rows, int n, RoundState state)
        {
            int bestScore = int.MinValue;
            for (int i = 0; i < n; i++) bestScore = Math.Max(bestScore, scores[i]);
            var candidates = new List<int>();
            int tolerance = Tolerance;
            for (int i = 0; i < n; i++)
            {
                // never "tolerate" walking past a found win or into a found loss
                bool decisive = Math.Abs(bestScore) >= Win - 100 || Math.Abs(scores[i]) >= Win - 100;
                if (scores[i] == bestScore || (!decisive && scores[i] >= bestScore - tolerance)) candidates.Add(i);
            }
            int pick = candidates[rng.Next(candidates.Count)];

            if (Level == Difficulty.Easy && rng.NextDouble() < 0.3)
            {
                // an occasional careless move: any legal drop, leaning towards the middle
                var drops = new List<int>();
                for (int i = 0; i < n; i++) if (kinds[i] == 0) drops.Add(i);
                if (drops.Count > 0) pick = drops[Math.Min(drops.Count - 1, (int)(Math.Abs(rng.NextDouble() - rng.NextDouble()) * drops.Count))];
            }
            var action = kinds[pick] == 0 ? GameAction.Drop(cols[pick]) : kinds[pick] == 1 ? GameAction.Bomb(cols[pick], rows[pick]) : SwapAction(bitsArr[pick]);
            // safety net: the model is the authority
            if (!state.IsLegal(action)) action = state.TimeoutAction();
            return action;
        }

        static GameAction SwapAction(ulong b)
        {
            int first = -1, second = -1;
            for (int bit = 0; bit < 64; bit++) if ((b >> bit & 1) != 0) { if (first < 0) first = bit; else second = bit; }
            return GameAction.Swap(new Cell(first / SearchPos.H, first % SearchPos.H), new Cell(second / SearchPos.H, second % SearchPos.H));
        }

        /// <summary>Make a move and score it from the mover's point of view.</summary>
        int PlayAndScore(ref SearchPos pos, byte kind, byte col, byte row, ulong bits, int depth, int alpha, int beta, int ply)
        {
            Nodes++;
            if ((Nodes & 1023) == 0 && (Nodes > nodeBudget || clock.Elapsed.TotalMilliseconds > timeBudget)) abort = true;
            var mover = pos.ToMove;
            switch (kind)
            {
                case 0: pos.Drop(col); break;
                case 1: pos.Bomb(col, row); break;
                default: pos.Swap(bits); break;
            }
            bool meWin = SearchPos.HasFour(mover == Player.One ? pos.P1 : pos.P2);
            bool themWin = kind != 0 && SearchPos.HasFour(mover == Player.One ? pos.P2 : pos.P1); // a drop can't make the opponent a line
            if (meWin && themWin) return 0;
            if (meWin) return Win - ply;
            if (themWin) return -(Win - ply);
            if (pos.IsFull) return 0;
            pos.ToMove = mover.Other();
            if (depth <= 0 || abort) return -Evaluate(ref pos);
            return -Negamax(ref pos, depth, -beta, -alpha, ply + 1);
        }

        int Negamax(ref SearchPos pos, int depth, int alpha, int beta, int ply)
        {
            if (ply >= MaxPly - 1) return Evaluate(ref pos);
            int n = Generate(ref pos, ply, ply < powerPlies);
            int best = -Win - 1;
            var bits = moveBits[ply]; var kinds = moveKinds[ply]; var cols = moveCols[ply]; var rows = moveRows[ply];
            for (int i = 0; i < n; i++)
            {
                var child = pos;
                int sc = PlayAndScore(ref child, kinds[i], cols[i], rows[i], bits[i], depth - 1, alpha, beta, ply);
                if (sc > best) best = sc;
                if (sc > alpha) alpha = sc;
                if (alpha >= beta || abort) break;
            }
            return best;
        }

        /// <summary>Moves for the side to move into this ply's buffers: drops centre-first, then powers.</summary>
        int Generate(ref SearchPos pos, int ply, bool withPowers)
        {
            var bits = moveBits[ply]; var kinds = moveKinds[ply]; var cols = moveCols[ply]; var rows = moveRows[ply];
            int n = 0;
            foreach (int c in CentreFirst)
                if (pos.CanDrop(c)) { kinds[n] = 0; cols[n] = (byte)c; rows[n] = 0; bits[n] = 0; n++; }
            if (!withPowers) return n;
            var me = pos.ToMove;
            ulong theirs = pos.Theirs;
            if (pos.BombsOf(me) > 0)
                foreach (int c in CentreFirst)
                    for (int r = 0; r < Board.Rows; r++)
                        if ((theirs & SearchPos.Bit(c, r)) != 0) { kinds[n] = 1; cols[n] = (byte)c; rows[n] = (byte)r; bits[n] = 0; n++; }
            if (pos.SwapsOf(me) > 0)
            {
                ulong a = pos.P1, b = pos.P2;
                // horizontal pairs (cell and the one to its right) and vertical pairs (cell and the one above)
                ulong h1 = a & (b >> SearchPos.H), h2 = b & (a >> SearchPos.H);
                ulong v1 = a & (b >> 1), v2 = b & (a >> 1);
                n = AddSwaps(h1 | h2, SearchPos.H, n, bits, kinds, cols, rows);
                n = AddSwaps((v1 | v2) & SearchPos.Full, 1, n, bits, kinds, cols, rows);
            }
            return n;
        }

        static int AddSwaps(ulong starts, int shift, int n, ulong[] bits, byte[] kinds, byte[] cols, byte[] rows)
        {
            for (int bit = 0; bit < 63 && starts != 0; bit++)
            {
                if ((starts >> bit & 1) == 0) continue;
                starts &= ~(1UL << bit);
                if (n >= bits.Length) break;
                kinds[n] = 2; cols[n] = (byte)(bit / SearchPos.H); rows[n] = (byte)(bit % SearchPos.H);
                bits[n] = (1UL << bit) | (1UL << (bit + shift));
                n++;
            }
            return n;
        }

        static readonly int[] Weights = { 0, 1, 5, 40, 0 };

        /// <summary>Score for the side to move: open windows of four, centre control and powers in hand.</summary>
        static int Evaluate(ref SearchPos pos)
        {
            ulong me = pos.Mine, them = pos.Theirs;
            ulong empty = SearchPos.Full & ~(me | them);
            // cells that could be played right now (the lowest empty cell of each column)
            ulong playable = (pos.Occupied + SearchPos.BottomRow) & SearchPos.Full & empty;
            int score = 0;
            foreach (ulong w in Windows)
            {
                ulong m = me & w, t = them & w;
                if (m != 0 && t != 0) continue;
                if (m != 0)
                {
                    int k = SearchPos.PopCount(m);
                    score += Weights[k];
                    if (k == 3 && (w & playable & empty) != 0) score += 30; // a threat that can be finished next move
                }
                else if (t != 0)
                {
                    int k = SearchPos.PopCount(t);
                    score -= Weights[k];
                    if (k == 3 && (w & playable & empty) != 0) score -= 30;
                }
            }
            ulong centre = SearchPos.ColumnMask(3);
            score += 4 * (SearchPos.PopCount(me & centre) - SearchPos.PopCount(them & centre));
            var p = pos.ToMove; var o = p.Other();
            score += 35 * (pos.BombsOf(p) - pos.BombsOf(o)) + 25 * (pos.SwapsOf(p) - pos.SwapsOf(o));
            return score;
        }

        static ulong[] BuildWindows()
        {
            var list = new List<ulong>();
            int[,] dirs = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 } };
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                    for (int d = 0; d < 4; d++)
                    {
                        int ec = c + dirs[d, 0] * 3, er = r + dirs[d, 1] * 3;
                        if (!Board.InBounds(ec, er)) continue;
                        ulong w = 0;
                        for (int i = 0; i < 4; i++) w |= SearchPos.Bit(c + dirs[d, 0] * i, r + dirs[d, 1] * i);
                        list.Add(w);
                    }
            return list.ToArray(); // 69 windows
        }
    }
}

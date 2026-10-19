using System.Linq;
using DropZone.Core;
using NUnit.Framework;

namespace DropZone.Tests
{
    public class RulesTests
    {
        static RoundState Round(GameMode mode, Player starter, params string[] rows)
        {
            var rules = mode == GameMode.Battle ? RuleSet.Battle() : RuleSet.Classic();
            return new RoundState(rules, starter, rows.Length > 0 ? Board.Parse(rows) : null);
        }

        // ---------- wins ----------

        [Test] public void HorizontalWin()
        {
            var s = Round(GameMode.Classic, Player.One, "XXX.OOO");
            var r = s.Apply(GameAction.Drop(3));
            Assert.AreEqual(RoundOutcome.PlayerOneWins, r.Outcome);
            Assert.AreEqual(1, r.Lines.Count);
            Assert.AreEqual(4, r.Lines[0].Cells.Length);
        }

        [Test] public void VerticalWin()
        {
            var s = Round(GameMode.Classic, Player.Two, "O......", "O......", "OX.....", "XX.....");
            Assert.AreEqual(RoundOutcome.PlayerTwoWins, s.Apply(GameAction.Drop(0)).Outcome);
        }

        [Test] public void DiagonalUpRightWin()
        {
            var s = Round(GameMode.Classic, Player.One,
                "..XO...",
                ".XOO...",
                "XOOX...");
            Assert.AreEqual(RoundOutcome.InProgress, s.Outcome);
            var r = s.Apply(GameAction.Drop(3)); // lands on row 3 at column 3
            Assert.AreEqual(new Cell(3, 3), r.Cell);
            Assert.AreEqual(RoundOutcome.PlayerOneWins, r.Outcome);
        }

        [Test] public void DiagonalDownRightWin()
        {
            var s = Round(GameMode.Classic, Player.Two,
                "...XO..",
                "...XOO.",
                "...XXXO");
            var r = s.Apply(GameAction.Drop(3)); // row 3 col 3 completes (3,3)(4,2)(5,1)(6,0)
            Assert.AreEqual(RoundOutcome.PlayerTwoWins, r.Outcome);
            CollectionAssert.AreEquivalent(new[] { new Cell(3, 3), new Cell(4, 2), new Cell(5, 1), new Cell(6, 0) }, r.Lines[0].Cells);
        }

        [Test] public void LongerLineIsOneLineOfFive()
        {
            var s = Round(GameMode.Classic, Player.One, "XX.XX..");
            var r = s.Apply(GameAction.Drop(2));
            Assert.AreEqual(1, r.Lines.Count);
            Assert.AreEqual(5, r.Lines[0].Cells.Length);
        }

        [Test] public void ThreeIsNotAWin()
        {
            var s = Round(GameMode.Classic, Player.One, "XX.....");
            Assert.AreEqual(RoundOutcome.InProgress, s.Apply(GameAction.Drop(2)).Outcome);
            Assert.AreEqual(Player.Two, s.Current);
        }

        // ---------- drops ----------

        [Test] public void FullColumnAndOutOfRangeDropsAreRejected()
        {
            var s = Round(GameMode.Classic, Player.One, "X", "O", "X", "O", "X", "O");
            Assert.IsFalse(s.IsLegal(GameAction.Drop(0), out string why));
            StringAssert.Contains("full", why);
            Assert.IsFalse(s.IsLegal(GameAction.Drop(-1)));
            Assert.IsFalse(s.IsLegal(GameAction.Drop(7)));
            Assert.Throws<System.InvalidOperationException>(() => s.Apply(GameAction.Drop(0)));
            Assert.AreEqual(Player.One, s.Current, "a rejected move doesn't use the turn");
        }

        [Test] public void CountersStackFromTheBottom()
        {
            var s = Round(GameMode.Classic, Player.One);
            Assert.AreEqual(0, s.Apply(GameAction.Drop(4)).Cell.Row);
            Assert.AreEqual(1, s.Apply(GameAction.Drop(4)).Cell.Row);
            Assert.AreEqual(Player.One, s.Board[4, 0]);
            Assert.AreEqual(Player.Two, s.Board[4, 1]);
        }

        [Test] public void FullBoardWithoutLineIsDraw()
        {
            // pattern with no four in a row anywhere; the last cell (6,5) is left for the final drop
            var s = Round(GameMode.Classic, Player.One,
                "OOXXOO.",
                "XXOOXXO",
                "OOXXOOX",
                "XXOOXXO",
                "OOXXOOX",
                "XXOOXXO");
            Assert.IsFalse(s.Board.HasLine(Player.One));
            Assert.IsFalse(s.Board.HasLine(Player.Two));
            var r = s.Apply(GameAction.Drop(6));
            Assert.AreEqual(RoundOutcome.Draw, r.Outcome);
            Assert.IsTrue(s.Board.IsFull);
            Assert.AreEqual(0, r.Lines.Count);
        }

        [Test] public void NoMovesAfterTheRoundEnds()
        {
            var s = Round(GameMode.Battle, Player.One, "XXX.OOO");
            s.Apply(GameAction.Drop(3));
            Assert.IsTrue(s.IsOver);
            Assert.IsFalse(s.IsLegal(GameAction.Drop(0)));
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(4, 0)), "no powers after the round ends");
            Assert.AreEqual(0, s.LegalActions().Count);
        }

        // ---------- Bomb ----------

        [Test] public void BombRemovesOpponentCounterAndColumnFalls()
        {
            var s = Round(GameMode.Battle, Player.One,
                "X......",
                "O......",
                "X......",
                "O......");
            var r = s.Apply(GameAction.Bomb(0, 0));
            Assert.AreEqual(Player.Two, r.Removed);
            Assert.AreEqual(3, r.Fallen);
            Assert.AreEqual("XOX", $"{Sym(s.Board[0, 0])}{Sym(s.Board[0, 1])}{Sym(s.Board[0, 2])}");
            Assert.AreEqual(Player.None, s.Board[0, 3]);
            Assert.AreEqual(3, s.Board.Height(0));
        }

        [Test] public void BombCannotTargetEmptyOrOwnCounter()
        {
            var s = Round(GameMode.Battle, Player.One, "XO.....");
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(0, 0), out string own));
            StringAssert.Contains("own", own);
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(3, 0), out string empty));
            StringAssert.Contains("empty", empty);
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(9, 9)));
            Assert.IsTrue(s.IsLegal(GameAction.Bomb(1, 0)));
        }

        [Test] public void BombIsDisabledWithNoTargetAndExplained()
        {
            var s = Round(GameMode.Battle, Player.One, "X......");
            Assert.IsFalse(s.CanBomb(Player.One, out string why));
            StringAssert.Contains("No opponent counters", why);
        }

        [Test] public void PowersAreNotAvailableInClassic()
        {
            var s = Round(GameMode.Classic, Player.One, "XO.....");
            Assert.AreEqual(0, s.BombsLeft(Player.One));
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(1, 0)));
            Assert.IsFalse(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(1, 0))));
            Assert.IsTrue(s.LegalActions().All(a => a.Kind == ActionKind.Drop));
        }

        // ---------- Swap ----------

        [Test] public void SwapExchangesAdjacentOpposingCounters()
        {
            var s = Round(GameMode.Battle, Player.Two, "XO.....");
            s.Apply(GameAction.Swap(new Cell(0, 0), new Cell(1, 0)));
            Assert.AreEqual(Player.Two, s.Board[0, 0]);
            Assert.AreEqual(Player.One, s.Board[1, 0]);
        }

        [Test] public void SwapVerticalIsAllowed()
        {
            var s = Round(GameMode.Battle, Player.One, "O......", "X......");
            Assert.IsTrue(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(0, 1))));
        }

        [Test] public void SwapRejectsDiagonalEmptySameOwnerAndDistant()
        {
            var s = Round(GameMode.Battle, Player.One,
                ".O.....",
                "XXO....");
            Assert.IsFalse(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(1, 1)), out string diag));
            StringAssert.Contains("not diagonally", diag);
            Assert.IsFalse(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(0, 1))), "empty cell");
            Assert.IsFalse(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(1, 0))), "same owner");
            Assert.IsFalse(s.IsLegal(GameAction.Swap(new Cell(0, 0), new Cell(2, 0))), "not touching");
            Assert.IsTrue(s.IsLegal(GameAction.Swap(new Cell(1, 0), new Cell(2, 0))));
            Assert.IsTrue(s.IsLegal(GameAction.Swap(new Cell(1, 0), new Cell(1, 1))));
        }

        // ---------- powers consumed once, turn handling ----------

        [Test] public void PowersAreConsumedOnceAndUseTheTurn()
        {
            var s = Round(GameMode.Battle, Player.One, "XO.....", "OX.....");
            s.Apply(GameAction.Bomb(0, 0));
            Assert.AreEqual(0, s.BombsLeft(Player.One));
            Assert.AreEqual(1, s.BombsLeft(Player.Two));
            Assert.AreEqual(Player.Two, s.Current, "a power uses the whole turn");
            s.Apply(GameAction.Drop(5));
            Assert.IsFalse(s.IsLegal(GameAction.Bomb(5, 0), out string why));
            StringAssert.Contains("already used", why);
            Assert.IsFalse(s.LegalActions().Any(a => a.Kind == ActionKind.Bomb));
        }

        [Test] public void PowersResetEachRound()
        {
            var m = new Match(RuleSet.Battle(), Player.One);
            var r1 = m.StartRound();
            r1.Apply(GameAction.Drop(0));
            r1.Apply(GameAction.Bomb(0, 0));
            Assert.AreEqual(0, r1.BombsLeft(Player.Two));
            // finish the round quickly
            ForceWin(r1);
            m.RecordRound();
            var r2 = m.StartRound();
            Assert.AreEqual(1, r2.BombsLeft(Player.One));
            Assert.AreEqual(1, r2.BombsLeft(Player.Two));
            Assert.AreEqual(1, r2.SwapsLeft(Player.Two));
        }

        [Test] public void CancellingAPowerKeepsTheTurnAndThePower()
        {
            var s = Round(GameMode.Battle, Player.One, "XO.....");
            var input = new TurnInput();
            Assert.IsTrue(input.BeginBomb(s));
            Assert.IsTrue(input.PickCell(s, new Cell(1, 0)));
            Assert.IsTrue(input.AwaitingConfirm);
            input.CancelAll();
            Assert.IsFalse(input.Targeting);
            Assert.AreEqual(1, s.BombsLeft(Player.One));
            Assert.AreEqual(Player.One, s.Current);
            Assert.AreEqual(0, s.MovesMade);

            Assert.IsTrue(input.BeginSwap(s));
            Assert.IsTrue(input.PickCell(s, new Cell(0, 0)));
            Assert.IsTrue(input.PickCell(s, new Cell(1, 0)));
            Assert.IsTrue(input.Cancel());   // back to picking the second counter
            Assert.IsTrue(input.Cancel());   // back to the first
            Assert.IsTrue(input.Cancel());   // out of Swap
            Assert.IsFalse(input.Cancel(), "nothing left to cancel");
            Assert.AreEqual(1, s.SwapsLeft(Player.One));
            Assert.AreEqual(Player.One, s.Current);
            Assert.AreEqual("XO.....", s.Board.ToString().Split('\n').Last());
        }

        [Test] public void SelectionRejectsBadTargetsWithReasons()
        {
            var s = Round(GameMode.Battle, Player.One, "XO.X...");
            var input = new TurnInput();
            input.BeginBomb(s);
            Assert.IsFalse(input.PickCell(s, new Cell(0, 0)));
            StringAssert.Contains("own", input.Message);
            input.CancelAll();
            input.BeginSwap(s);
            Assert.IsFalse(input.PickCell(s, new Cell(3, 0)), "no opposing neighbour");
            Assert.IsTrue(input.PickCell(s, new Cell(0, 0)));
            Assert.IsFalse(input.TryConfirm(s, out _), "needs the second counter");
            Assert.IsTrue(input.PickCell(s, new Cell(1, 0)));
            Assert.IsTrue(input.TryConfirm(s, out var action));
            Assert.AreEqual(GameAction.Swap(new Cell(0, 0), new Cell(1, 0)), action);
        }

        // ---------- resolution of both players' lines ----------

        [Test] public void ActionThatCreatesOnlyTheOpponentsLineGivesThemTheWin()
        {
            // O has three on the bottom row with X at column 3; X swaps vertically, completing O's row
            var s = Round(GameMode.Battle, Player.One,
                "...O...",
                "OOOX...");
            var r = s.Apply(GameAction.Swap(new Cell(3, 0), new Cell(3, 1)));
            Assert.AreEqual(RoundOutcome.PlayerTwoWins, r.Outcome);
            Assert.IsTrue(r.GiftedWin);
        }

        [Test] public void BombCanCompleteTheOpponentsLine()
        {
            // X bombs the O under its own counter; the X falls into the gap and completes the row
            var s = Round(GameMode.Battle, Player.One,
                "...X...",
                "XXXO...");
            var r = s.Apply(GameAction.Bomb(3, 0));
            Assert.AreEqual(Player.One, s.Board[3, 0]);
            Assert.AreEqual(RoundOutcome.PlayerOneWins, r.Outcome);
            Assert.IsFalse(r.GiftedWin);
        }

        [Test] public void BothPlayersLinesAfterOneActionIsADraw()
        {
            // swapping (3,0)O and (3,1)X gives X row 0 (XXXX) and O row 1 (OOOO) at once
            var s = Round(GameMode.Battle, Player.One,
                "OOOX...",
                "XXXO...");
            var r = s.Apply(GameAction.Swap(new Cell(3, 0), new Cell(3, 1)));
            Assert.AreEqual(RoundOutcome.Draw, r.Outcome);
            Assert.AreEqual(2, r.Lines.Count, "both lines are highlighted");
            CollectionAssert.AreEquivalent(new[] { Player.One, Player.Two }, r.Lines.Select(l => l.Owner));
        }

        // ---------- timeout ----------

        [Test] public void TimeoutDropsCentreFirstAndNeverUsesAPower()
        {
            var s = Round(GameMode.Battle, Player.One,
                "...X...",
                "...O...",
                "...X...",
                "...O...",
                "...X...",
                "...O...");
            var a = s.TimeoutAction();
            Assert.AreEqual(ActionKind.Drop, a.Kind);
            Assert.AreEqual(2, a.Column, "column 3 is full, so the next in centre-first order");
            var r = s.Apply(a, timeout: true);
            Assert.IsTrue(r.Timeout);
            Assert.AreEqual(1, s.BombsLeft(Player.One));
            Assert.AreEqual(1, s.SwapsLeft(Player.One));
        }

        [Test] public void TimeoutOrderIsDeterministic()
        {
            CollectionAssert.AreEqual(new[] { 3, 2, 4, 1, 5, 0, 6 }, RoundState.CentreFirst);
            var s = Round(GameMode.Classic, Player.Two);
            Assert.AreEqual(3, s.TimeoutAction().Column);
        }

        [Test] public void TurnClockFreezesAndExpiresOnce()
        {
            var clock = new TurnClock();
            clock.Reset(10);
            Assert.IsFalse(clock.Tick(5), "not running until resumed");
            clock.Resume();
            Assert.IsFalse(clock.Tick(4));
            clock.Freeze();
            Assert.IsFalse(clock.Tick(100), "frozen during animations and pause");
            Assert.AreEqual(6f, clock.Remaining, 1e-4);
            clock.Resume();
            Assert.AreEqual(6, clock.DisplaySeconds);
            Assert.IsTrue(clock.Tick(7));
            Assert.IsFalse(clock.Tick(1), "expires once");
            var off = new TurnClock();
            off.Reset(0); off.Resume();
            Assert.IsFalse(off.Tick(1000), "no timer in Classic by default");
        }

        [Test] public void TimeoutOnTheLastSpaceEndsInADraw()
        {
            var s = Round(GameMode.Classic, Player.One,
                "OOXXOO.",
                "XXOOXXO",
                "OOXXOOX",
                "XXOOXXO",
                "OOXXOOX",
                "XXOOXXO");
            var r = s.Apply(s.TimeoutAction(), timeout: true);
            Assert.AreEqual(RoundOutcome.Draw, r.Outcome);
        }

        // ---------- match ----------

        [Test] public void MatchIsFirstToThreeWithAlternatingStartersAndDrawsScoreNothing()
        {
            var m = new Match(RuleSet.Classic(), Player.Two);
            Assert.AreEqual(Player.Two, m.StarterFor(1));
            Assert.AreEqual(Player.One, m.StarterFor(2));
            Assert.AreEqual(Player.Two, m.StarterFor(3));

            var r = m.StartRound(); Assert.AreEqual(Player.Two, r.Current);
            ForceWin(r); m.RecordRound();
            var winner1 = r.Winner;
            Assert.AreEqual(1, m.Wins(winner1));

            r = m.StartRound(); Assert.AreEqual(Player.One, r.Current, "starter alternates");
            ForceDraw(r); m.RecordRound();
            Assert.AreEqual(1, m.Wins(Player.One) + m.Wins(Player.Two), "a draw awards no win");

            r = m.StartRound(); Assert.AreEqual(Player.Two, r.Current, "alternates after a draw too");
            Assert.Throws<System.InvalidOperationException>(() => m.StartRound(), "can't skip an unfinished round");
            while (!m.IsOver)
            {
                if (r.IsOver) r = m.StartRound();
                ForceWin(r); m.RecordRound();
            }
            Assert.AreEqual(3, m.Wins(m.Winner));
            Assert.Throws<System.InvalidOperationException>(() => m.StartRound());
        }

        // ---------- helpers ----------

        static char Sym(Player p) => p == Player.One ? 'X' : p == Player.Two ? 'O' : '.';

        /// <summary>The player to move stacks column 0 while the other plays column 1 or 2.</summary>
        static void ForceWin(RoundState r)
        {
            var me = r.Current;
            int other = 1;
            while (!r.IsOver)
            {
                if (r.Current == me) r.Apply(GameAction.Drop(r.Board.CanDrop(6) ? 6 : r.TimeoutAction().Column));
                else { r.Apply(GameAction.Drop(other)); other = other == 1 ? 2 : 1; }
            }
        }

        /// <summary>Fill the board, in a legal turn order, into a pattern with no line of four ('X' = the round's starter).</summary>
        static void ForceDraw(RoundState r)
        {
            string[] topToBottom = { "OOXXOOX", "XXOOXXO", "OOXXOOX", "XXOOXXO", "OOXXOOX", "XXOOXXO" };
            var order = new System.Collections.Generic.List<int>();
            var heights = new int[Board.Columns];
            Assert.IsTrue(FindOrder(topToBottom, heights, 0, order), "draw pattern is reachable");
            foreach (int c in order) r.Apply(GameAction.Drop(c));
            Assert.AreEqual(RoundOutcome.Draw, r.Outcome);
        }

        static bool FindOrder(string[] target, int[] heights, int move, System.Collections.Generic.List<int> order)
        {
            if (move == Board.Columns * Board.Rows) return true;
            char mine = move % 2 == 0 ? 'X' : 'O';
            foreach (int c in RoundState.CentreFirst)
            {
                int h = heights[c];
                if (h >= Board.Rows || target[Board.Rows - 1 - h][c] != mine) continue;
                heights[c]++; order.Add(c);
                if (FindOrder(target, heights, move + 1, order)) return true;
                heights[c]--; order.RemoveAt(order.Count - 1);
            }
            return false;
        }
    }
}

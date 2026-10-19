using DropZone.Core;
using NUnit.Framework;

namespace DropZone.Tests
{
    public class AiTests
    {
        static readonly Difficulty[] All = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };

        static RoundState Round(GameMode mode, Player starter, params string[] rows) =>
            new RoundState(mode == GameMode.Battle ? RuleSet.Battle() : RuleSet.Classic(), starter, rows.Length > 0 ? Board.Parse(rows) : null);

        [Test] public void EveryDifficultyPlaysOnlyLegalActionsThroughWholeGames([Values(GameMode.Classic, GameMode.Battle)] GameMode mode)
        {
            foreach (var a in All)
                foreach (var b in All)
                {
                    var s = new RoundState(mode == GameMode.Battle ? RuleSet.Battle() : RuleSet.Classic(), Player.One);
                    var p1 = new AiPlayer(a, 11); var p2 = new AiPlayer(b, 22);
                    int guard = 0;
                    while (!s.IsOver && guard++ < 200)
                    {
                        var ai = s.Current == Player.One ? p1 : p2;
                        var action = ai.ChooseNow(s);
                        Assert.IsTrue(s.IsLegal(action, out string why), $"{ai.Level} chose {action}: {why}\n{s.Board}");
                        s.Apply(action);
                    }
                    Assert.IsTrue(s.IsOver, $"{a} vs {b} finished");
                }
        }

        [Test] public void TakesAnImmediateWin([Values(Difficulty.Normal, Difficulty.Hard)] Difficulty level)
        {
            for (int seed = 0; seed < 5; seed++)
            {
                var s = Round(GameMode.Classic, Player.One,
                    "OO.....",
                    "XXX.O..");
                var choice = new AiPlayer(level, seed).ChooseNow(s);
                Assert.AreEqual(GameAction.Drop(3), choice, $"seed {seed}");
            }
        }

        [Test] public void BlocksAnImmediateLoss([Values(Difficulty.Normal, Difficulty.Hard)] Difficulty level)
        {
            for (int seed = 0; seed < 5; seed++)
            {
                // O threatens column 4 on the bottom row; X must block it
                var s = Round(GameMode.Classic, Player.One,
                    "X......",
                    "XOOO...");
                var choice = new AiPlayer(level, seed).ChooseNow(s);
                Assert.AreEqual(GameAction.Drop(4), choice, $"seed {seed}");
            }
        }

        [Test] public void HardBombsAwayAThreatItCannotBlockByDropping()
        {
            // O has two separate finishing spots (an open three on the bottom row): a drop can only
            // block one, but bombing the middle O breaks the line
            var s = Round(GameMode.Battle, Player.One,
                "X......",
                "X.OOO..");
            // X to move with a Bomb and Swap in hand
            var choice = new AiPlayer(Difficulty.Hard, 3).ChooseNow(s);
            Assert.IsTrue(s.IsLegal(choice));
            var after = s.Clone();
            after.Apply(choice);
            // whatever it chose, O must not have an immediate winning drop left
            foreach (var a in after.LegalActions(includePowers: false))
            {
                var t = after.Clone();
                t.Apply(a);
                Assert.AreNotEqual(RoundOutcome.PlayerTwoWins, t.Outcome, $"after {choice}, O wins with {a}\n{after.Board}");
            }
        }

        [Test] public void UsesAPowerToWinWhenThatIsTheOnlyWin()
        {
            // X can't win by dropping, but bombing the O at (3,0) lets X's counter fall into the row
            var s = Round(GameMode.Battle, Player.One,
                "...X...",
                "XXXO.O.");
            foreach (var level in new[] { Difficulty.Normal, Difficulty.Hard })
            {
                var choice = new AiPlayer(level, 1).ChooseNow(s);
                var t = s.Clone();
                t.Apply(choice);
                Assert.AreEqual(RoundOutcome.PlayerOneWins, t.Outcome, $"{level} chose {choice}");
            }
        }

        [Test] public void AvoidsASwapThatHandsTheOpponentTheWin()
        {
            var s = Round(GameMode.Battle, Player.One,
                "...O...",
                "OOOX...");
            var choice = new AiPlayer(Difficulty.Hard, 2).ChooseNow(s);
            Assert.AreNotEqual(GameAction.Swap(new Cell(3, 0), new Cell(3, 1)), choice);
        }

        [Test] public void HardStaysWithinItsBudget()
        {
            var s = Round(GameMode.Battle, Player.One, "...O...", "..XOX..", "..OXO..");
            var ai = new AiPlayer(Difficulty.Hard, 5);
            ai.ChooseNow(s);
            Assert.Less(ai.Milliseconds, 2500, $"searched {ai.Nodes} nodes to depth {ai.DepthReached}");
            Assert.GreaterOrEqual(ai.DepthReached, 3);
        }

        [Test] public void ThinkingYieldsInSlices()
        {
            var s = Round(GameMode.Classic, Player.One);
            var ai = new AiPlayer(Difficulty.Hard, 9) { SliceMs = 2 };
            int steps = 0;
            var it = ai.Think(s);
            while (it.MoveNext()) steps++;
            Assert.Greater(steps, 0, "a hard search spreads over several frames");
            Assert.IsTrue(s.IsLegal(ai.Choice));
        }

        [Test] public void CancelStopsThinking()
        {
            var s = Round(GameMode.Battle, Player.One);
            var ai = new AiPlayer(Difficulty.Hard, 4) { SliceMs = 0.01 };
            var it = ai.Think(s);
            it.MoveNext();
            ai.Cancel();
            int steps = 0;
            while (it.MoveNext()) steps++;
            Assert.AreEqual(0, steps);
        }

        [Test] public void HardBeatsEasyMostOfTheTime()
        {
            int hardWins = 0, games = 6;
            for (int g = 0; g < games; g++)
            {
                var s = new RoundState(RuleSet.Classic(), g % 2 == 0 ? Player.One : Player.Two);
                var hard = new AiPlayer(Difficulty.Hard, 100 + g); var easy = new AiPlayer(Difficulty.Easy, 200 + g);
                while (!s.IsOver) s.Apply((s.Current == Player.One ? hard : easy).ChooseNow(s));
                if (s.Winner == Player.One) hardWins++;
            }
            Assert.GreaterOrEqual(hardWins, 5, $"hard won {hardWins}/{games}");
        }
    }
}

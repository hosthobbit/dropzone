using DropZone.Core;
using UnityEngine;

namespace DropZone
{
    /// <summary>Preferences and solo statistics, kept in PlayerPrefs (works in browsers too).</summary>
    public static class Settings
    {
        /// <summary>Key prefix. Automated test runs use their own so they never touch a player's settings or stats.</summary>
        public static string Prefix = "dz.";

        public static float Music { get => PlayerPrefs.GetFloat(Prefix + "music", 0.5f); set => PlayerPrefs.SetFloat(Prefix + "music", value); }
        public static float Sound { get => PlayerPrefs.GetFloat(Prefix + "sound", 0.85f); set => PlayerPrefs.SetFloat(Prefix + "sound", value); }
        /// <summary>0 Low, 1 Medium, 2 High.</summary>
        public static int Quality { get => PlayerPrefs.GetInt(Prefix + "quality", DefaultQuality); set => PlayerPrefs.SetInt(Prefix + "quality", value); }
        public static bool CameraShake { get => PlayerPrefs.GetInt(Prefix + "shake", 1) == 1; set => PlayerPrefs.SetInt(Prefix + "shake", value ? 1 : 0); }
        public static bool ReducedMotion { get => PlayerPrefs.GetInt(Prefix + "reduced", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "reduced", value ? 1 : 0); }
        public static bool BattleIntroSeen { get => PlayerPrefs.GetInt(Prefix + "battleIntro", 0) == 1; set => PlayerPrefs.SetInt(Prefix + "battleIntro", value ? 1 : 0); }

        // last-used match options
        public static int LastMode { get => PlayerPrefs.GetInt(Prefix + "mode", 0); set => PlayerPrefs.SetInt(Prefix + "mode", value); }
        public static int LastDifficulty { get => PlayerPrefs.GetInt(Prefix + "difficulty", 1); set => PlayerPrefs.SetInt(Prefix + "difficulty", value); }
        public static int LastStarter { get => PlayerPrefs.GetInt(Prefix + "starter", 0); set => PlayerPrefs.SetInt(Prefix + "starter", value); }
        public static int ClassicTimer { get => PlayerPrefs.GetInt(Prefix + "classicTimer", 0); set => PlayerPrefs.SetInt(Prefix + "classicTimer", value); }
        public static int BattleTimer { get => PlayerPrefs.GetInt(Prefix + "battleTimer", 10); set => PlayerPrefs.SetInt(Prefix + "battleTimer", value); }

        static int DefaultQuality => Application.isMobilePlatform ? 0 : Application.platform == RuntimePlatform.WebGLPlayer ? 1 : 2;
        public static readonly string[] QualityNames = { "Low", "Medium", "High" };

        // solo statistics: matches won / lost against each difficulty, and rounds
        static string Key(Difficulty d, string what) => $"{Prefix}stats.{d}.{what}";
        public static int Stat(Difficulty d, string what) => PlayerPrefs.GetInt(Key(d, what), 0);
        public static void AddStat(Difficulty d, string what) => PlayerPrefs.SetInt(Key(d, what), Stat(d, what) + 1);

        /// <summary>Forget everything stored under the current prefix (used to start QA runs clean).</summary>
        public static void ResetAll()
        {
            foreach (var k in new[] { "music", "sound", "quality", "shake", "reduced", "battleIntro", "mode", "difficulty", "starter", "classicTimer", "battleTimer" }) PlayerPrefs.DeleteKey(Prefix + k);
            foreach (Difficulty d in System.Enum.GetValues(typeof(Difficulty)))
                foreach (var w in new[] { "matchWon", "matchLost", "roundWon", "roundLost", "roundDraw" }) PlayerPrefs.DeleteKey(Key(d, w));
        }

        public static void Save() => PlayerPrefs.Save();
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.fmodaudio
{
    internal static class SoundLog
    {
        private const string Prefix = "[kinatraa.fmodaudio] ";
        private static readonly HashSet<string> logged = new HashSet<string>();

        internal static SoundLogLevel Level = SoundLogLevel.Warning;

        internal static void Reset()
        {
            logged.Clear();
            Level = SoundLogLevel.Warning;
        }

        internal static void Error(string message)
        {
            if (Level >= SoundLogLevel.Error) Debug.LogError(Prefix + message);
        }

        internal static void Warning(string message)
        {
            if (Level >= SoundLogLevel.Warning) Debug.LogWarning(Prefix + message);
        }

        internal static void Info(string message)
        {
            if (Level >= SoundLogLevel.Info) Debug.Log(Prefix + message);
        }

        /// <summary>Logs a warning only the first time <paramref name="key"/> is seen (per play session).</summary>
        internal static void WarningOnce(string key, string message)
        {
            if (logged.Add(key)) Warning(message);
        }

        internal static void ErrorOnce(string key, string message)
        {
            if (logged.Add(key)) Error(message);
        }
    }
}

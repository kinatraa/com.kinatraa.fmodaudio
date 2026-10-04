using System;
using System.Collections.Generic;
using UnityEngine;

namespace kinatraa.fmodaudio
{
    internal static class SoundLog
    {
        private const string Prefix = "[kinatraa.fmodaudio] ";

        // Category -> ids already reported. Looking up a seen (category, id) pair does not allocate, so "log once" paths
        // stay allocation free when they are hit every frame (e.g. SetParameter with a missing parameter in Update).
        private static readonly Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        internal static SoundLogLevel Level = SoundLogLevel.Warning;

        internal static void Reset()
        {
            seen.Clear();
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

        /// <summary>Logs an exception thrown by game code called from the sound system (hooks, events, callbacks).</summary>
        internal static void Exception(string context, Exception e)
        {
            if (Level >= SoundLogLevel.Error) Debug.LogError($"{Prefix}{context} threw {e.GetType().Name}: {e.Message}\n{e}");
        }

        /// <summary>
        /// True the first time (<paramref name="category"/>, <paramref name="id"/>) is seen in this play session. Build the
        /// message only when this returns true.
        /// </summary>
        internal static bool First(string category, string id)
        {
            if (!seen.TryGetValue(category, out HashSet<string> ids))
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                seen[category] = ids;
            }
            return ids.Add(id ?? string.Empty);
        }

        /// <summary>Logs a warning only the first time <paramref name="key"/> is seen (per play session).</summary>
        internal static void WarningOnce(string key, string message)
        {
            if (First(string.Empty, key)) Warning(message);
        }

        internal static void ErrorOnce(string key, string message)
        {
            if (First(string.Empty, key)) Error(message);
        }
    }
}

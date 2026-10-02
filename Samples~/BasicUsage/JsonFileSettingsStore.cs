using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace kinatraa.fmodaudio.Samples
{
    /// <summary>
    /// Example custom settings store: volumes and mutes in a JSON file under Application.persistentDataPath.
    /// Install it early (e.g. from a bootstrap script): <c>Sound.SetSettingsStore(new JsonFileSettingsStore("audio.json"));</c>
    /// </summary>
    public sealed class JsonFileSettingsStore : ISoundSettingsStore
    {
        [Serializable]
        private class Entry
        {
            public string Key;
            public float Volume = 1f;
            public bool HasVolume;
            public bool Muted;
            public bool HasMuted;
        }

        [Serializable]
        private class Data
        {
            public List<Entry> Entries = new List<Entry>();
        }

        private readonly string path;
        private readonly Data data;

        public JsonFileSettingsStore(string fileName)
        {
            path = Path.Combine(Application.persistentDataPath, fileName);
            data = File.Exists(path) ? JsonUtility.FromJson<Data>(File.ReadAllText(path)) ?? new Data() : new Data();
        }

        private Entry Find(string key, bool create)
        {
            Entry e = data.Entries.Find(x => x.Key == key);
            if (e == null && create) data.Entries.Add(e = new Entry { Key = key });
            return e;
        }

        public bool TryLoad(string key, out float value)
        {
            Entry e = Find(key, false);
            value = e != null && e.HasVolume ? e.Volume : 1f;
            return e != null && e.HasVolume;
        }

        public void Save(string key, float value)
        {
            Entry e = Find(key, true);
            e.Volume = value;
            e.HasVolume = true;
            Flush();
        }

        public bool TryLoadMuted(string key, out bool muted)
        {
            Entry e = Find(key, false);
            muted = e != null && e.Muted;
            return e != null && e.HasMuted;
        }

        public void SaveMuted(string key, bool muted)
        {
            Entry e = Find(key, true);
            e.Muted = muted;
            e.HasMuted = true;
            Flush();
        }

        // ponytail: writes on every change; debounce if sliders save every frame on slow storage.
        private void Flush() => File.WriteAllText(path, JsonUtility.ToJson(data, true));
    }
}

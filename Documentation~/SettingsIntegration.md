# Settings integration

The package does not own your settings or save format. Volumes are **linear 0..1 per channel name** (`"Master"`,
`"Music"`, `"SFX"`, `"UI"`, `"Voice"`, `"Ambient"` and any custom channel from the config). Mutes are booleans per channel.

```csharp
public interface ISoundSettingsStore
{
    bool TryLoad(string key, out float value);      // key = channel name, e.g. "Music"
    void Save(string key, float value);
    bool TryLoadMuted(string key, out bool muted);
    void SaveMuted(string key, bool muted);
}
```

How it is used:
- On `Sound.Initialize` every configured channel loads its volume/mute from the store (or the channel's default volume).
- `Sound.SetBusVolume` / `SetBusMuted` apply to the FMOD bus, call `Save` / `SaveMuted`, and raise `OnBusVolumeChanged` / `OnBusMuteChanged`.
- `Sound.SetSettingsStore(store)` swaps the store and **reloads and applies every channel immediately**.
- `Sound.ApplyVolumes(dict, save: false)` / `Sound.GetAllVolumes()` for games that serialize their own settings object.
- `Sound.ResetVolumes(save: true)` restores every channel's configured *Default Volume* and unmutes it (a "Reset to defaults" button).
- `Sound.SaveSettings()` flushes a store that buffers writes (see below). It also happens automatically on focus loss, app
  suspension (mobile background), quit and `Sound.Shutdown()`.
- Values set with `SetBusVolume` / `SetBusMuted` before the system initializes (only possible with *Auto Initialize* off) are
  kept and applied when it starts.
- Config > *Persist Settings* off and no custom store: nothing is saved.
- A store that throws is logged and treated as "nothing saved"; it never breaks playback.

### Stores that buffer writes

Implement `IFlushableSoundSettingsStore` (an `ISoundSettingsStore` with `void Flush()`) when writing on every `Save` is too
expensive, e.g. a file written once per settings change instead of once per slider tick:

```csharp
public sealed class BufferedJsonStore : IFlushableSoundSettingsStore
{
    // ... TryLoad / Save / TryLoadMuted / SaveMuted update an in-memory object and set dirty = true ...
    public void Flush() { if (dirty) { File.WriteAllText(path, JsonUtility.ToJson(data)); dirty = false; } }
}
```

## Example 1: PlayerPrefs (default)

Nothing to do. With *Persist Settings* on, the system uses `PlayerPrefsSoundSettingsStore` with the config's
*Settings Key Prefix* (`kinatraa.fmodaudio.` by default): keys `kinatraa.fmodaudio.Music` (float) and
`kinatraa.fmodaudio.Music.muted` (int). Use another prefix to keep several profiles:

```csharp
Sound.SetSettingsStore(new PlayerPrefsSoundSettingsStore("profile2.audio."));
```

The store implements `IFlushableSoundSettingsStore`: `PlayerPrefs.Save()` runs when something changed and the app loses focus,
goes to the background, quits or the system shuts down, and whenever you call `Sound.SaveSettings()` (e.g. when the settings
menu closes). Mobile apps killed while in the foreground lose only the changes since the last flush.

## Example 2: JSON save file

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using kinatraa.fmodaudio;
using UnityEngine;

public sealed class JsonAudioSettingsStore : ISoundSettingsStore
{
    [Serializable] class Entry { public string Key; public float Volume = 1f; public bool HasVolume; public bool Muted; public bool HasMuted; }
    [Serializable] class Data { public List<Entry> Entries = new List<Entry>(); }

    readonly string path;
    readonly Data data;

    public JsonAudioSettingsStore(string fileName = "audio-settings.json")
    {
        path = Path.Combine(Application.persistentDataPath, fileName);
        data = File.Exists(path) ? JsonUtility.FromJson<Data>(File.ReadAllText(path)) ?? new Data() : new Data();
    }

    Entry Get(string key, bool create)
    {
        Entry e = data.Entries.Find(x => x.Key == key);
        if (e == null && create) data.Entries.Add(e = new Entry { Key = key });
        return e;
    }

    public bool TryLoad(string key, out float value)
    {
        Entry e = Get(key, false);
        value = e != null && e.HasVolume ? e.Volume : 1f;
        return e != null && e.HasVolume;
    }

    public void Save(string key, float value) { Entry e = Get(key, true); e.Volume = value; e.HasVolume = true; Write(); }

    public bool TryLoadMuted(string key, out bool muted)
    {
        Entry e = Get(key, false);
        muted = e != null && e.Muted;
        return e != null && e.HasMuted;
    }

    public void SaveMuted(string key, bool muted) { Entry e = Get(key, true); e.Muted = muted; e.HasMuted = true; Write(); }

    void Write() => File.WriteAllText(path, JsonUtility.ToJson(data, true));   // simple: writes on every change
}

// Install it once, as early as possible (e.g. in your boot scene):
public class AudioBoot : MonoBehaviour
{
    void Awake() => Sound.SetSettingsStore(new JsonAudioSettingsStore());
}
```

If your game already has a save system, the delegate adapter is often enough:

```csharp
Sound.BindSettings(
    load: key => save.Audio.TryGetValue(key, out float v) ? v : (float?)null,   // null = not saved yet
    save: (key, v) => { save.Audio[key] = v; save.MarkDirty(); },
    loadMuted: key => save.Muted.Contains(key),
    saveMuted: (key, m) => { if (m) save.Muted.Add(key); else save.Muted.Remove(key); });
```

## Example 3: ScriptableObject settings asset

Useful for prototypes, or as the in-memory model behind your own save code.

```csharp
using System;
using System.Collections.Generic;
using kinatraa.fmodaudio;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Audio Settings")]
public class AudioSettingsAsset : ScriptableObject, ISoundSettingsStore
{
    [Serializable] public class Channel { public string Name; [Range(0, 1)] public float Volume = 1f; public bool Muted; }
    public List<Channel> Channels = new List<Channel>();

    Channel Find(string key, bool create)
    {
        Channel c = Channels.Find(x => x.Name == key);
        if (c == null && create) Channels.Add(c = new Channel { Name = key });
        return c;
    }

    public bool TryLoad(string key, out float value) { Channel c = Find(key, false); value = c?.Volume ?? 1f; return c != null; }
    public void Save(string key, float value) => Find(key, true).Volume = value;
    public bool TryLoadMuted(string key, out bool muted) { Channel c = Find(key, false); muted = c != null && c.Muted; return c != null; }
    public void SaveMuted(string key, bool muted) => Find(key, true).Muted = muted;

    // Edit values in the inspector during Play Mode and press "Apply" (context menu) to push them to FMOD.
    [ContextMenu("Apply")] void Apply() => Sound.SetSettingsStore(this);
}

public class AudioSettingsInstaller : MonoBehaviour
{
    public AudioSettingsAsset Settings;
    void Awake() => Sound.SetSettingsStore(Settings);
}
```

Note: changes to a ScriptableObject asset at runtime persist only in the editor. In a player, serialize the values
(e.g. with `JsonUtility.ToJson(Settings)`) through your save system.

## Games that serialize their own settings object

```csharp
[Serializable] public class GameSettings { public float Master = 1, Music = 0.8f, Sfx = 1; }

void ApplyAudio(GameSettings s) => Sound.ApplyVolumes(new Dictionary<string, float>
{
    { "Master", s.Master }, { "Music", s.Music }, { "SFX", s.Sfx },
});   // save: false by default, nothing is written to the store

GameSettings Capture()
{
    Dictionary<string, float> v = Sound.GetAllVolumes();
    return new GameSettings { Master = v["Master"], Music = v["Music"], Sfx = v["SFX"] };
}
```

Set *Persist Settings* off in the config so the default PlayerPrefs store stays unused.

## Policy hook (veto / modify playback)

```csharp
public sealed class CutscenePolicy : ISoundPolicy
{
    public bool InCutscene;

    public bool OnBeforePlay(ref SoundPlayRequest request)
    {
        if (InCutscene && request.Channel == SoundChannel.SFX) return false;    // no SFX during cutscenes
        if (request.Channel == SoundChannel.UI) request.Volume *= 0.8f;          // tweak
        return true;
    }

    public void OnFocusChanged(bool hasFocus) => Sound.SetGlobalParameter("Focused", hasFocus ? 1 : 0);
}

Sound.SetPolicy(new CutscenePolicy());
```

Built-in focus handling is configured in the config (*Focus Loss Behavior*: None / Pause / Mute); the policy is called after it.

## Bank provider hook

```csharp
public sealed class LevelBanks : ISoundBankProvider
{
    public void GetBanksForScene(UnityEngine.SceneManagement.Scene scene, List<string> banks)
    {
        if (scene.name.StartsWith("Level")) banks.Add(scene.name);     // Level3 scene -> Level3.bank
        if (scene.name == "Boss") { banks.Add("Boss"); banks.Add("BossMusic"); }
    }
}

Sound.SetBankProvider(new LevelBanks());   // null restores the config-based provider
```

Banks returned for a scene are loaded when it loads and released when it unloads (reference counted).

## Events

```csharp
Sound.OnBusVolumeChanged += (channel, volume) => ui.SetSlider(channel.Name, volume);
Sound.OnBusMuteChanged   += (channel, muted) => ui.SetMuteIcon(channel.Name, muted);
Sound.OnCuePlayed        += (cueId, handle) => analytics.Count(cueId);
Sound.OnMusicChanged     += cueId => nowPlaying.text = cueId ?? "";
Sound.OnBankLoaded       += bank => Debug.Log("Loaded " + bank);
Sound.OnBankUnloaded     += bank => Debug.Log("Unloaded " + bank);
```

All events are cleared when entering Play Mode (supports Enter Play Mode Options with domain reload disabled), so
subscribe from your own initialization code.

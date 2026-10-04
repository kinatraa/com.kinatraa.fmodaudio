# API reference

Namespace `kinatraa.fmodaudio` (runtime) and `kinatraa.fmodaudio.Editor` (editor). Every public type and member also has
XML documentation in the source. **Main thread only.** Nothing throws on missing data: it logs once and returns an invalid
handle / default value. Exceptions thrown by your own code that `Sound` calls (policy, event handlers, bank provider,
settings store, `LoadBankAsync` callbacks) are logged as errors and never escape from `Sound` calls.

## `Sound` (static)

### Lifecycle
| Member | Description |
|---|---|
| `bool Initialize(SoundSystemConfig config = null)` | Initializes (defaults when null). Called automatically before the first scene loads with `Resources/kinatraaFmodAudioConfig` unless its *Auto Initialize* is off. Returns false if FMOD failed (API stays safe and silent). |
| `void Shutdown()` | Stops and releases every instance and snapshot, unloads banks loaded through `Sound`, flushes the settings store and completes pending `LoadBankAsync` callbacks with `false`. `OnMusicChanged` is not raised. |
| `bool IsInitialized`, `bool IsReady` | Ready = initialized and no FMOD bank load in flight (only relevant on WebGL / Android OBB). |
| `SoundSystemConfig Config` | Active config. |

### Playback
| Member | Description |
|---|---|
| `SoundHandle Play(string cueId)` | 2D. A 3D event played this way is placed on the listener. |
| `SoundHandle Play(string cueId, Vector3 position)` | 3D at a position. |
| `SoundHandle Play(string cueId, Transform follow)` | Attached: 3D attributes follow the transform (central updater, only while attached sounds exist). If the transform is destroyed the sound keeps its last position. |
| `SoundHandle Play(EventReference e[, Vector3 / Transform])` | Direct event, no cue rules. |
| `void StopChannel(SoundChannel channel, bool allowFadeOut = true)` | Stops tracked sounds of that channel and everything routed through its bus (a VCA channel stops only its tracked sounds). Master = `StopAll`. |
| `void StopAll(bool allowFadeOut = true)` | Stops everything, including music. |
| `int ActiveSoundCount`, `int QueuedSoundCount` | Tracked handles (playing/paused/fading/queued) and queued plays. |
| `int GetEventInstanceCount(string cueId)` | FMOD's live instance count of the cue's event (diagnostics, leak checks). |

Cue rules applied by `Play(cueId, ...)`: policy veto, cooldown, max instances (Reject / StopOldest), default parameters,
auto-release. Plays requested while FMOD banks load (or while a `LoadBankAsync` is pending and the event is not yet
available) are queued and start automatically; queued plays expire after 10 s. If FMOD still reports a bank load after 30 s
(a failed asynchronous load never clears FMOD's own counter), playback stops waiting for it and a warning is logged.

### Music
| Member | Description |
|---|---|
| `SoundHandle PlayMusic(string cueId, bool restartIfPlaying = false)` | Single music slot. The previous track stops with fade-out (FMOD AHDSR / release). Same cue = no-op. If the new cue cannot play, the current music continues. |
| `OnMusicChanged` | Raised with the new cue id on `PlayMusic`, and with `null` whenever the slot ends: `StopMusic`, `StopAll`, `StopChannel(Music)`, `MusicHandle.Stop()`, a non-looping track finishing or a queued track being dropped. Switching tracks raises only the new id. |
| `void StopMusic(bool allowFadeOut = true)`, `PauseMusic()`, `ResumeMusic()` | |
| `void SetMusicParameter(string name, float value)` | |
| `SoundHandle MusicHandle`, `string CurrentMusic` | |

### Mix, parameters, snapshots
| Member | Description |
|---|---|
| `void SetBusVolume(SoundChannel channel, float volume)` | Linear 0..1 on the channel's bus or VCA (multiplies with the Studio fader; Master affects all). Saved through the store, raises `OnBusVolumeChanged`. Called before `Initialize` (Auto Initialize off), the value is kept and applied at startup. |
| `float GetBusVolume(SoundChannel channel)` | 1 for unknown channels. |
| `void SetBusMuted(SoundChannel channel, bool muted)`, `bool IsBusMuted(SoundChannel channel)` | VCA channels have no mute in FMOD: muting sets the VCA volume to 0 and unmuting restores the channel volume. |
| `void ResetVolumes(bool save = true)` | Every channel back to its configured default volume, unmuted; raises the change events; with `save` writes and flushes the store. |
| `void SaveSettings()` | Flushes an `IFlushableSoundSettingsStore` (the default PlayerPrefs store calls `PlayerPrefs.Save`). Also done automatically on focus loss, app suspension, quit and shutdown. |
| `void ApplyVolumes(IDictionary<string, float> volumes, bool save = false)` | Bulk apply (keys = channel names). |
| `Dictionary<string, float> GetAllVolumes()`, `void GetChannels(List<SoundChannel> into)` | |
| `void SetGlobalParameter(string name, float value)`, `float GetGlobalParameter(string name)` | FMOD global parameters. |
| `SnapshotHandle PushSnapshot(string nameOrPath)` | Config key ("Pause"), name (`snapshot:/<name>`) or full path. `Release()` the handle to pop it. |
| `void PauseAll()`, `void ResumeAll()`, `bool IsPaused` | Per config *Pause Mode*: master bus pause, Pause snapshot, or both. Re-applied after mobile app resume. |

### Banks
| Member | Description |
|---|---|
| `bool LoadBank(string bank, bool loadSampleData = false)` | Reference counted, through FMOD's RuntimeManager (honours FMOD's bank folder / sub-folder settings). |
| `void LoadBankAsync(string bank, Action<bool> onComplete = null, bool loadSampleData = false)` | Callback on the main thread, at the earliest next frame, when the bank and optionally its sample data are loaded. It runs immediately with `false` when the request is rejected up front (not initialized, empty name, bank file missing). It always completes: `false` after 30 s without the bank, or when still pending at `Shutdown`. No Task allocation. |
| `void UnloadBank(string bank)` | Releases one reference. |
| `bool IsBankLoaded(string bank)` | |

### Settings, hooks, libraries
| Member | Description |
|---|---|
| `ISoundSettingsStore SettingsStore`, `void SetSettingsStore(ISoundSettingsStore store)` | Swapping reloads and applies all channels. |
| `void BindSettings(Func<string, float?> load, Action<string, float> save, Func<string, bool?> loadMuted = null, Action<string, bool> saveMuted = null)` | Delegate adapter. |
| `void SetPolicy(ISoundPolicy policy)`, `void SetBankProvider(ISoundBankProvider provider)` | null clears / restores defaults. |
| `void RegisterLibrary(SoundCueLibrary)`, `void UnregisterLibrary(SoundCueLibrary)` | Last registered wins on duplicate ids (logged). |
| `bool HasCue(string id)`, `bool TryGetCue(string id, out SoundCue cue)` | |

### Events
`OnBusVolumeChanged (SoundChannel, float)`, `OnBusMuteChanged (SoundChannel, bool)`, `OnCuePlayed (string cueId, SoundHandle)`,
`OnMusicChanged (string cueIdOrNull)`, `OnBankLoaded (string)`, `OnBankUnloaded (string)`. Reset at the start of every play session.

## `SoundHandle` (readonly struct)
| Member | Description |
|---|---|
| `IsValid`, `IsPlaying`, `IsPaused`, `State`, `CueId` | `State` is a `SoundPlaybackState`: Stopped, Queued, Starting, Playing, Sustaining, Stopping. |
| `Stop(bool allowFadeOut = true)`, `Pause()`, `Resume()` | |
| `SetParameter(string, float)`, `SetParameter(SoundParameterId, float)`, `GetParameterId(string)` | Local event parameters; set by name while queued, they apply when the sound starts. |
| `bool TryGetParameter(string name, out float value)` | Value set by code (or queued). False when the parameter does not exist. |
| `SetVolume(float)`, `SetPitch(float)` | Instance gain / pitch multiplier. |
| `SetPosition(Vector3)` | Moves the sound; detaches it if it was following a transform. |
| `AttachTo(Transform)` | Makes it follow a transform from now on (updated every frame while it plays). |
| `int TimelinePosition { get; set; }` | Milliseconds. Set on a queued sound, it applies when the sound starts. |

`SoundHandle.Invalid` / `default` is invalid. Handles carry a generation id: after the sound ends they become stale, and every
call is a safe no-op even if the slot is reused.

## `SnapshotHandle` (readonly struct)
`IsValid`, `Release(bool allowFadeOut = true)`.

## Data
| Type | Fields |
|---|---|
| `SoundCue` (serializable class) | `Id`, `Event` (FMOD `EventReference`, GUID based), `Channel`, `DefaultParameters`, `Cooldown`, `MaxInstances`, `LimitPolicy`, `AutoRelease` (default true; set false for loops so the handle owns the instance), `Tags` |
| `SoundCueLibrary` (ScriptableObject) | `Cues`, `Find(id)` |
| `SoundChannel` (struct) | `Name`, `IsValid`, built-ins `Master`, `Music`, `SFX`, `UI`, `Voice`, `Ambient`; implicit from string |
| `SoundSystemConfig` (ScriptableObject) | `AutoInitialize`, `Channels` (`SoundChannelDefinition`: `Name`, `BusPath` = `bus:/...` or `vca:/...`, `DefaultVolume`), `Libraries`, `Banks`, `SceneBanks`, `LoadAllBanksOnStart`, `Snapshots` (`SnapshotMapping`: `Key`, `Path`), `PauseMode`, `FocusLossBehavior`, `PersistSettings`, `SettingsKeyPrefix`, `LogLevel`, `TryGetSnapshotPath`, `CreateDefault()`, `CreateDefaultChannels()` |

## Hooks
`ISoundSettingsStore` (`TryLoad`, `Save`, `TryLoadMuted`, `SaveMuted`), `IFlushableSoundSettingsStore` (adds `Flush()`),
`PlayerPrefsSoundSettingsStore(prefix)` (flushable),
`DelegateSoundSettingsStore`, `ISoundPolicy` (`OnBeforePlay(ref SoundPlayRequest)`, `OnFocusChanged(bool)`),
`SoundPlayRequest` (`CueId`, `Channel`, `Volume`, `Pitch`), `ISoundBankProvider` (`GetBanksForScene(Scene, List<string>)`),
`[SoundCueId]` attribute for string fields.

## Components
| Component | Main fields |
|---|---|
| `SoundEmitter` | `CueId`, `PlayOn` (None, Start, OnEnable, TriggerEnter, TriggerEnter2D), `TriggerTag`, `Space` (Attached, AtPosition, TwoD), `StopOn`, `AllowFadeOut`, `Parameters`; methods `Play()`, `PlayCue(string)` (UnityEvents, animation events), `Stop()`, `SetParameter()`, property `Handle` |
| `SoundBankLoader` | `Banks` (FMOD bank picker), `LoadOn`, `UnloadOn`, `LoadSampleData`, `Load()`, `Unload()`, `IsLoaded`, `LoadedBanks`. With *Unload On = OnDisable* the banks load again when the object is re-enabled. `Load()` does nothing (warning) while the system is not initialized. |
| `SoundListenerGuard` | `AddListenerIfMissing`, `DisableUnityAudioListeners`, `Check()` |

## Editor (`kinatraa.fmodaudio.Editor`)
| Type | Purpose |
|---|---|
| `SoundSetupWizardWindow` | `Open()`, `FixAllChecks()` |
| `SetupValidator` | `Validate()`, `Run()` -> `Result` (`Passed`, `MissingCount`, `WarningCount`, `Entries`, `ExitCode`), `RunAndExit()` for `-executeMethod` |
| `SetupCheck`, `SetupCheckRegistry`, `SetupStatus`, `SetupCheckResult` | Add your own checks to the wizard: subclass `SetupCheck` (override `HasDetails` when `DrawGUI` draws something) and `SetupCheckRegistry.Register(...)` from an `[InitializeOnLoad]` class |
| `FmodDetector`, `DefineSync` | FMOD detection and `KINATRAA_FMOD` define management |
| `CueLibraryWindow` | `Open()`, `Open(SoundCueLibrary)` |
| `SoundIdsGenerator` | `Generate()`, `BuildSource(ids, namespace, className)`, `Validate(path, namespace, className)`, `IsValidIdentifier`, `IsValidNamespace`; settings (path, namespace, class, *Auto-generate*) in Project Settings > kinatraa FMOD Audio |
| `RuntimeDebuggerWindow` | `Open()` |

Menus: Tools > kinatraa > FMOD Audio > Setup Wizard / Validate Setup / Cue Library / Runtime Debugger / Generate SoundIds.cs,
and Assets > Create > kinatraa > FMOD Audio > Sound System Config / Sound Cue Library.

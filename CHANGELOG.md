# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-10-21

### Changed
- **Breaking:** namespaces `kinatraa.fmodaudio` and `kinatraa.fmodaudio.Editor` are now `kinatraa.FmodAudio` and
  `kinatraa.FmodAudio.Editor`, and the assemblies are renamed to match (`kinatraa.FmodAudio`, `kinatraa.FmodAudio.Editor`,
  `kinatraa.FmodAudio.Bootstrap.Editor`), following the other kinatraa packages (`kinatraa.AudioSystem`,
  `kinatraa.PlayerPrefEditor`, ...). To migrate:
  - replace `using kinatraa.fmodaudio;` with `using kinatraa.FmodAudio;` (and any fully qualified names);
  - in your asmdefs, rename references to `kinatraa.fmodaudio` to `kinatraa.FmodAudio` (references by GUID keep working);
  - update CI calls to `-executeMethod kinatraa.FmodAudio.Editor.SetupValidator.RunAndExit`;
  - re-import the *Basic Usage* sample, or apply the two steps above to your imported copy.
- Unchanged: saved volume and mute settings (the default key prefix stays `kinatraa.fmodaudio.`), editor preferences,
  the `KINATRAA_FMOD` define, log labels, and serialized components and assets (script GUIDs are the same).

## [1.1.0] - 2026-10-04

### Added
- **VCA channels**: a channel's *Bus Path* may be `vca:/<Name>`; volume and mute (volume 0) go to the VCA. The wizard checks
  VCAs exist in the Studio project.
- `SoundHandle.SetPosition(Vector3)`, `AttachTo(Transform)`, `TimelinePosition` (get/set in ms, applied on start for queued
  sounds) and `TryGetParameter(name, out value)`.
- `Sound.ResetVolumes(bool save = true)` (configured defaults, unmuted) and `Sound.SaveSettings()`.
- `IFlushableSoundSettingsStore`: stores that buffer writes are flushed on `SaveSettings`, focus loss, app suspension, quit and
  shutdown. `PlayerPrefsSoundSettingsStore` implements it (`PlayerPrefs.Save` only when something changed).
- `SoundBankLoader.LoadedBanks`; a custom inspector with validation and Play Mode Load/Unload.
- Setup Wizard: toolbar (Re-scan, Fix All with count, *Only problems* filter, links to the Cue Library, Runtime Debugger and docs),
  status summary, *Details* only where a check has extra controls.
- Cue Library: *Add to config* banner for libraries the runtime would not register, row menu (copy id, copy `Sound.Play` call,
  duplicate, delete), clear-search button, "showing N of M" count, tooltips explaining each problem, *no channel* problem,
  select-all applies to the visible (filtered) cues only.
- Runtime Debugger: toolbar, sound filter, per-channel *Stop*, *Reset volumes* / *Save settings*, push and release config
  snapshots.
- SoundIds generator: namespace / class name validation, output must be inside `Assets/`, optional *Auto-generate* when a cue
  library is saved, cue count preview and *Ping file* in Project Settings.
- Package tests: Edit Mode tests (`Tests/Editor`) and Play Mode tests (`Tests/Runtime`, using the Minimal Studio Project sample).
- Samples: reset/save buttons in `VolumeSliders`, a moving one-shot in `SfxExample`.

### Fixed
- WebGL / Android OBB: a bank that failed to load asynchronously blocked every queued play forever (FMOD for Unity 2.03.15 never
  clears its loading counter after a failed asynchronous load). Asynchronous loads now time out after 30 s and FMOD's counter is
  ignored after the same delay, with a warning.
- `LoadBankAsync` callbacks still pending at `Sound.Shutdown()` are now invoked with `false` instead of never.
- `OnMusicChanged(null)` is now raised whenever the music slot ends (`StopChannel(Music)`, `MusicHandle.Stop()`, a non-looping
  track finishing, a queued track dropped), not only from `StopMusic` / `StopAll`.
- `SoundBankLoader` with *Unload On = OnDisable* now reloads its banks when the object is enabled again; `Load()` while the
  system is not initialized no longer marks the loader as loaded.
- Volumes and mutes set before `Initialize` (Auto Initialize off) are no longer overwritten when the config's channels load.
- Exceptions thrown by game code (policy, event handlers, bank provider, settings store, `LoadBankAsync` callbacks) are logged
  and no longer escape from `Sound` calls or stop the update.
- "Log once" paths no longer allocate when hit repeatedly (e.g. `SetParameter` with a missing parameter every frame).
- The listener fix only removes *enabled* Unity AudioListeners.
- The Runtime Debugger and the `SoundEmitter` inspector no longer repaint in a tight loop.
- The wizard reports an unreadable FMOD version as unknown instead of unsupported.
- Samples: `SceneBankExample` no longer unloads a bank it does not hold.

### Changed
- Window titles match their menu items (*Setup Wizard*, *Cue Library*, *Runtime Debugger*).
- The config inspector and the wizard validate channel paths (`bus:/...` or `vca:/...`) and mention cue libraries that are not
  registered in the config.

## [1.0.0] - 2026-10-02

### Added
- `Sound` static facade: cue-based and `EventReference` playback (2D, 3D at position, attached to a transform), single music
  slot with FMOD fade-out, global parameters, snapshots (`PushSnapshot` / `SnapshotHandle.Release`), `PauseAll` / `ResumeAll`
  (master bus and/or Pause snapshot), bus volumes and mutes (linear 0..1), `StopChannel`, `StopAll`.
- Recycle-safe `SoundHandle` (generation ids; stale handles are no-ops) with parameters by name or id, volume, pitch, pause.
- Bank loading: reference-counted `LoadBank` / `UnloadBank`, callback-based `LoadBankAsync` (optionally waiting for sample
  data), per-scene banks from the config or an `ISoundBankProvider`; plays queue while FMOD banks load.
- Data model: `SoundCue`, `SoundCueLibrary` (multiple libraries), `SoundChannel` (built-in and custom channels),
  `SoundSystemConfig` loaded from `Resources/kinatraaFmodAudioConfig`, with defaults and one warning when missing.
- Per-cue cooldown, max instances with Reject / StopOldest, default parameters, auto-release for one-shots.
- Game hooks: `ISoundSettingsStore` (PlayerPrefs default with configurable prefix), `BindSettings`, `ApplyVolumes`,
  `GetAllVolumes`, `ISoundPolicy`, `ISoundBankProvider`, and events for volume, mute, cue played, music, bank load/unload.
- Components: `SoundEmitter`, `SoundBankLoader`, `SoundListenerGuard`.
- Thin `FmodBackend` layer: the only code calling FMOD APIs (written against FMOD for Unity 2.03.15).
- Bootstrap editor assembly without FMOD references: detects FMOD, keeps the `KINATRAA_FMOD` define in sync for every build
  target, hosts the Setup Wizard and `SetupValidator` (CI entry point `RunAndExit`).
- Setup Wizard with 12 checks, Fix / Fix All, a one-time first-import console notice with a link to the wizard.
- Editor tools: Cue Library window, `[SoundCueId]` dropdown, `SoundChannel` drawer, inspectors with validation,
  `SoundIds.cs` generator (Project Settings > kinatraa FMOD Audio), Play Mode runtime debugger.
- Samples: Basic Usage, Minimal Studio Project (instructions plus an `fmodstudiocl` script and an audio generator script).

[2.0.0]: https://github.com/kinatraa/com.kinatraa.fmodaudio/releases/tag/2.0.0
[1.1.0]: https://github.com/kinatraa/com.kinatraa.fmodaudio/releases/tag/1.1.0
[1.0.0]: https://github.com/kinatraa/com.kinatraa.fmodaudio/releases/tag/1.0.0

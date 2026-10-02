# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses [Semantic Versioning](https://semver.org/).

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

[1.0.0]: https://github.com/kinatraa/com.kinatraa.fmodaudio/releases/tag/1.0.0

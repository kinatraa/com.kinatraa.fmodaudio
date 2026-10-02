# kinatraa FMOD Audio

A small, safe wrapper and tooling layer on top of **FMOD for Unity**, packaged as a UPM package.

- `Sound.Play("ui_click")`: cue ids instead of event paths, GUID-based event references, recycle-safe handles.
- A single music slot, bus volumes and mutes, snapshots, pause, global parameters, ref-counted bank loading (sync and async), per-scene banks.
- Your game owns its settings: plug any save system in through `ISoundSettingsStore` (PlayerPrefs by default).
- A **Setup Wizard** that checks and configures a Unity project for FMOD with a fixed convention: Fix / Fix All, idempotent, logged, Undo where possible, plus a CI validator.
- Editor tools: Cue Library window (search, filters, bulk edit, drag & drop FMOD events, preview), `[SoundCueId]` dropdown, `SoundIds.cs` generator, Play Mode runtime debugger.

It does not ship FMOD and has no other dependencies. Without FMOD installed the package compiles cleanly and only the wizard (explaining what is missing) is active.

## Requirements and tested versions

| Component | Version | Status |
|---|---|---|
| FMOD for Unity | **2.03.15** | Tested (runtime tests, wizard, player build) |
| FMOD Studio (bank authoring) | **2.03.15** | Tested. Banks must be built with FMOD Studio from the same 2.03 line as FMOD for Unity |
| Unity | 6000.3.13f1 (macOS) | Tested: compile matrix (without FMOD, FMOD added later, FMOD removed, FMOD re-imported, next to another kinatraa audio package), wizard end to end, 21 Play Mode tests + 1 Edit Mode test (domain reload disabled), macOS Mono player build |
| Unity | 6000.3.6f1 (macOS) | Tested: fresh project + FMOD + package compiles without errors or warnings, define added automatically |
| Unity | 2021.3 LTS | Declared minimum (APIs chosen for it) but **not tested** |
| FMOD for Unity 2.02 / 1.x | | **Not supported** |

FMOD for Unity is a prerequisite you install yourself (it is not a UPM package and cannot be redistributed):
download *FMOD for Unity 2.03.15* from [fmod.com/download](https://www.fmod.com/download) and import the `.unitypackage`.

## Install

UPM git URL (Package Manager > + > Add package from git URL):

```
https://github.com/kinatraa/com.kinatraa.fmodaudio.git#1.0.0
```

Or as a git submodule (embedded package):

```
git submodule add https://github.com/kinatraa/com.kinatraa.fmodaudio.git Packages/com.kinatraa.fmodaudio
```

## 60-second quick start

1. Install FMOD for Unity 2.03.15 (Assets > Import Package > Custom Package...). Open it once in the editor so FMOD finishes its own setup.
2. Add this package (above). The `KINATRAA_FMOD` define is added automatically.
3. Create your FMOD Studio 2.03 project in `FMODProject/` next to `Assets/`, build banks (or use the *Minimal Studio Project* sample).
4. **Tools > kinatraa > FMOD Audio > Setup Wizard > Fix All**.
5. **Tools > kinatraa > FMOD Audio > Cue Library > + From FMOD event**, then play:

```csharp
using kinatraa.fmodaudio;

Sound.Play("ui_click");                                   // 2D
Sound.Play("explosion", hitPoint);                        // 3D at a position
SoundHandle engine = Sound.Play("engine_loop", transform);// attached, follows the transform
engine.SetParameter("RPM", 0.7f);
engine.Stop(allowFadeOut: true);
Sound.PlayMusic("battle_theme");                          // single music slot, replaces with FMOD fade-out
Sound.SetBusVolume(SoundChannel.Music, 0.5f);             // linear 0..1, persisted
SnapshotHandle pause = Sound.PushSnapshot("Pause");       // ... later: pause.Release();
```

## API cheat sheet

| Area | Calls |
|---|---|
| Play | `Sound.Play(id)`, `Play(id, Vector3)`, `Play(id, Transform)`, `Play(EventReference[, Vector3/Transform])` |
| Handle | `IsValid`, `IsPlaying`, `IsPaused`, `State`, `CueId`, `Stop(allowFadeOut)`, `Pause()`, `Resume()`, `SetParameter(name/id, v)`, `GetParameterId`, `SetVolume`, `SetPitch` |
| Music | `PlayMusic(id)`, `StopMusic()`, `PauseMusic()`, `ResumeMusic()`, `SetMusicParameter(name, v)`, `CurrentMusic` |
| Mix | `SetBusVolume / GetBusVolume`, `SetBusMuted / IsBusMuted`, `ApplyVolumes`, `GetAllVolumes`, `StopChannel`, `StopAll` |
| State | `SetGlobalParameter / GetGlobalParameter`, `PushSnapshot(key) -> SnapshotHandle.Release()`, `PauseAll()`, `ResumeAll()` |
| Banks | `LoadBank`, `LoadBankAsync(name, ok => ...)`, `UnloadBank`, `IsBankLoaded`, per-scene banks via config or `ISoundBankProvider` |
| Hooks | `SetSettingsStore`, `BindSettings(load, save)`, `SetPolicy(ISoundPolicy)`, `SetBankProvider` |
| Events | `OnBusVolumeChanged`, `OnBusMuteChanged`, `OnCuePlayed`, `OnMusicChanged`, `OnBankLoaded`, `OnBankUnloaded` |
| Components | `SoundEmitter`, `SoundBankLoader`, `SoundListenerGuard` |

Full reference: [Documentation~/API.md](Documentation~/API.md). Everything is main-thread only and never throws: missing cues, events, buses or banks log once and return invalid handles.

## Conventions

| Kind | Convention |
|---|---|
| Buses | `bus:/Music`, `bus:/SFX`, `bus:/UI`, `bus:/Voice`, `bus:/Ambient` (group buses under the Master Bus) |
| Events | `event:/Music/...`, `event:/SFX/...`, `event:/UI/...`, `event:/Ambient/...`, `event:/Voice/...` |
| Snapshots | `snapshot:/Pause`, `snapshot:/Ducking` |
| Banks | `Master`, `Master.strings`, `Common`, `Music`, one bank per level/area |
| Cue ids | `snake_case`, derived from the event name (`event:/UI/Click` -> `click`) |
| Global parameters | PascalCase nouns: `TimeOfDay`, `MusicIntensity`, `Health` |

Details: [Documentation~/Conventions.md](Documentation~/Conventions.md). Channels and bus paths are configurable in the config asset.

## Settings integration (summary)

The package never owns your save format. Volumes are linear 0..1 per channel name (`"Music"`, `"SFX"`...):

```csharp
Sound.SetSettingsStore(new MySaveSystemStore());                       // ISoundSettingsStore; reloads volumes now
Sound.BindSettings(key => save.Get(key), (key, v) => save.Set(key, v)); // delegates
Sound.ApplyVolumes(mySettings.Volumes);                                 // you serialize your own object
```

Three complete examples (PlayerPrefs, JSON file, ScriptableObject): [Documentation~/SettingsIntegration.md](Documentation~/SettingsIntegration.md).

## Setup Wizard

12 checks: FMOD installed and version, scripting define, FMOD settings linked to the `.fspro`, Studio conventions (buses/snapshots, read-only),
Unity audio disabled, listener, banks built and fresh, config asset, folders, `.gitignore`, platform sanity, Play Mode smoke test.
CI: `Unity -batchmode -quit -projectPath . -executeMethod kinatraa.fmodaudio.Editor.SetupValidator.RunAndExit`.
See [Documentation~/Wizard.md](Documentation~/Wizard.md).

## Documentation

[Getting started](Documentation~/GettingStarted.md) · [Conventions](Documentation~/Conventions.md) · [Settings integration](Documentation~/SettingsIntegration.md) ·
[Wizard](Documentation~/Wizard.md) · [API](Documentation~/API.md) · [Troubleshooting](Documentation~/Troubleshooting.md) · [Changelog](CHANGELOG.md)

## License

The code in this package is MIT licensed (see [LICENSE](LICENSE)). FMOD and FMOD for Unity are products of Firelight Technologies
under their own license; **no FMOD files are included in this repository**, and using FMOD requires accepting its license.

## Roadmap

- Test on Unity 2021.3 / 2022.3 LTS and Windows; WebGL and Android OBB (asynchronous bank loading) on device.
- Optional Addressables bank loading through FMOD's `AssetReference` API.
- Programmer sounds / localized dialogue helpers for the Voice channel.
- VCA support alongside buses.

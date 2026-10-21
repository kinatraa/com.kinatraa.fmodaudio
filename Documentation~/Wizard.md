# Setup Wizard

**Tools > kinatraa > FMOD Audio > Setup Wizard.** Each item shows a status (OK / Info / Warning / Missing), why it matters,
and a **Fix** button when it can be fixed automatically. **Fix All** runs every fixable item in order. Every fix:
- is idempotent (running Fix All twice changes nothing the second time),
- logs exactly what it changed to the Console (`[kinatraa.fmodaudio] Setup: ...`),
- supports Undo for scene and settings changes (listener, FMOD settings, Project Settings > Audio).

The toolbar has **Re-scan**, **Fix All (n)** (n = items Fix All would fix; disabled when there is nothing to do),
**Only problems** (hides OK and Info items), shortcuts to the **Cue Library** and **Runtime Debugger**, **Log report**
(the CI report in the Console) and **Docs**. Under it a summary counts OK / warning / missing items and says
"Project is set up." when nothing is left. Items with extra controls (pickers, previews, options) have a **Details** foldout.
The wizard re-scans when it gets focus and when Play Mode starts or stops.

The wizard lives in the bootstrap editor assembly, so it opens even when FMOD is missing. In that case it shows
"FMOD for Unity not detected" with install instructions and hides the other items.

| # | Check | Fixes automatically | Only reports |
|---|---|---|---|
| 1 | FMOD for Unity installed, version (expects 2.03.15) | - | Missing FMOD, FMOD without asmdefs, other or unreadable versions (download link, no download automation) |
| 2 | `KINATRAA_FMOD` define for every build target | Adds (or removes when FMOD is gone) | - |
| 3 | FMOD settings asset exists, Source = Studio project, `.fspro` path | Creates the settings asset through FMOD, links the `.fspro` (auto-detected next to `Assets/`, or Browse) | No `.fspro` found |
| 4 | Studio conventions: required buses and VCAs (from the config's channels), optional snapshots, saved by Studio 2.03 | - (never modifies the Studio project) | Missing buses/VCAs/snapshots with exact FMOD Studio steps; editable channel/snapshot list under Details |
| 5 | Unity built-in audio disabled | Sets Project Settings > Audio > *Disable Unity Audio* (FMOD 2.03.15 has no own switch; it uses the same setting) | Skipped by Fix All when the project has AudioClips/AudioMixers (likely still uses Unity audio) |
| 6 | One StudioListener on the main camera of the open scenes, no enabled Unity AudioListener | Adds StudioListener, optionally removes enabled AudioListeners (Undo) | No MainCamera, several StudioListeners |
| 7 | Banks: Master + Master.strings built, platform folder valid, not older than the Studio project, FMOD cache fresh, FMOD post-import staging done | Refreshes FMOD's bank cache | Unbuilt/stale banks (build in FMOD Studio), unknown config banks, *Load Banks = All* with scene banks |
| 8 | `Resources/kinatraaFmodAudioConfig.asset` | Creates it (default channels -> buses, Pause/Ducking snapshots) plus an empty `Assets/Audio/SoundCues.asset` library | Config issues (empty channels, channel paths not starting with `bus:/` or `vca:/`, no libraries, no Master banks); notes cue libraries that are not in the config |
| 9 | Recommended folders | Creates `Assets/Audio` after confirmation (never moves files) | Suggests `FMODProject/` for the Studio project |
| 10 | `.gitignore` FMOD Studio entries | Appends `<Studio folder>/Build/`, `.cache/`, `.unsaved/`, `.user/` (preview under Details; user lines are never removed) | No `.gitignore` (nothing created) |
| 11 | Platform sanity | - | Speaker mode, sample rate, real/virtual voices per platform, live update in editor / release builds |
| 12 | Runtime smoke test (Play Mode) | - | Plays a chosen cue and reports success or the reason of failure |

## First import

On the first import into a project (once per user per project, stored in `UserSettings/`), a non-blocking Console
message summarizes the state and links to the wizard.

## CI / batch mode

```
Unity -batchmode -quit -projectPath <project> -executeMethod kinatraa.FmodAudio.Editor.SetupValidator.RunAndExit
```

Exit code 0 when nothing is *Missing* (warnings do not fail), 1 otherwise; the report is in the log. From your own
editor code use `SetupValidator.Validate()` / `Run()`, which return a `SetupValidator.Result`
(`Passed`, `MissingCount`, `WarningCount`, `Entries`, `ExitCode`). The menu item **Tools > kinatraa > FMOD Audio > Validate Setup**
does the same interactively.

Notes for batch mode:
- FMOD for Unity 2.03.15 skips its own post-import native library staging in batch mode. Open a freshly imported FMOD
  once in the editor (or run `FMODUnity.StagingSystem.Startup()` and execute the returned steps) before validating in CI;
  check 7 reports it otherwise.
- On a fresh clone where the define is not committed yet, the first batch run adds `KINATRAA_FMOD`; the FMOD checks
  appear on the next run (the define is normally committed in `ProjectSettings/ProjectSettings.asset`).
- If FMOD was removed from a project whose committed settings still contain `KINATRAA_FMOD`, add `-ignorecompilererrors`
  to the first batch run so the bootstrap can remove the stale define (see Troubleshooting).

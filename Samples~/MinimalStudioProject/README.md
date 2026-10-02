# Minimal FMOD Studio project

The BasicUsage sample, the wizard's smoke test and the package's own verification use a tiny FMOD Studio
project. This folder contains no FMOD files: only instructions, an FMOD Studio script and a stdlib-only Python script
that writes placeholder audio.

Use **FMOD Studio 2.03.x** (tested: 2.03.15). Banks must be built with the same 2.03 line as FMOD for Unity.

## What it contains

| Kind | Path | Notes |
|---|---|---|
| Buses | `bus:/Music`, `bus:/SFX`, `bus:/UI`, `bus:/Voice`, `bus:/Ambient` | Group buses routed to the Master Bus |
| Event (2D one-shot) | `event:/UI/Click` | UI bus, bank `Common` |
| Event (3D one-shot) | `event:/SFX/Explosion`, `event:/SFX/Footstep` | Spatializer on, SFX bus, bank `Common` |
| Event (3D looping + parameter) | `event:/SFX/EngineLoop` | Loop region, local parameter `RPM` (0..1), bank `Common` |
| Event (music + parameter) | `event:/Music/BattleTheme` | Loop region, local parameter `MusicIntensity` (0..1), bank `Music` |
| Event (music) | `event:/Music/MenuTheme` | Loop region, bank `Music` |
| Event (ambience) | `event:/Ambient/Wind` | Loop region, bank `Level1` (scene bank) |
| Global parameter | `TimeOfDay` (0..1) | |
| Snapshots | `snapshot:/Pause`, `snapshot:/Ducking` | Assigned to the Master bank |
| Banks | `Master`, `Master.strings`, `Common`, `Music`, `Level1` | |

## Option A: scripted (about 1 minute)

1. In FMOD Studio 2.03: **File > New**, then **File > Save As...** to `<UnityProject>/FMODProject/FMODProject.fspro`. Close FMOD Studio.
2. `python3 make_test_audio.py <UnityProject>/FMODProject` (writes `FMODProject/SourceAudio/*.wav`).
3. Run the command line tool shipped with FMOD Studio:
   - macOS: `"/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl" -script CreateMinimalProject.js <UnityProject>/FMODProject/FMODProject.fspro`
   - Windows: `"C:\Program Files\FMOD SoundSystem\FMOD Studio 2.03.xx\fmodstudiocl.exe" -script CreateMinimalProject.js <UnityProject>\FMODProject\FMODProject.fspro`

   The script is idempotent and ends with `kinatraa minimal project ready; build succeeded`.

## Option B: by hand in FMOD Studio

1. **File > New**, **File > Save As...** `<UnityProject>/FMODProject/FMODProject.fspro`.
2. **Buses**: Window > Mixer. Right-click *Master Bus* > *New Group*; create `Music`, `SFX`, `UI`, `Voice`, `Ambient`.
3. **Event folders**: in the Events browser right-click > *New Folder*: `UI`, `SFX`, `Music`, `Ambient`.
4. **UI click**: right-click `UI` > *New Event* > *2D Timeline*, name it `Click`. Drag a short sound onto the timeline.
   In the event's *Master* track, set the output (routing) to `UI`.
5. **3D one-shots**: in `SFX` create *3D Timeline* events `Explosion` and `Footstep` with short sounds; route them to `SFX`.
6. **Looping 3D event with a parameter**: in `SFX` create a *3D Timeline* event `EngineLoop`. Add a sound, then right-click the
   logic track above the timeline > *Add Loop Region* covering the sound (without a loop region the event ends with its sound).
   Click **+** next to *Timeline* in the parameter tabs > *Add Parameter* > *Create New Parameter*: name `RPM`, range 0..1
   (optionally automate the instrument pitch with it). Route to `SFX`.
7. **Music**: in `Music` create *2D Timeline* events `BattleTheme` (with a loop region and a parameter `MusicIntensity` 0..1)
   and `MenuTheme` (loop region). Route both to `Music`.
8. **Ambience**: in `Ambient` create a 2D event `Wind` with a loop region; route it to `Ambient`.
9. **Global parameter**: Window > Preset Browser > Parameters, *New Parameter* `TimeOfDay` (0..1) with **Global** ticked.
10. **Snapshots**: in the Mixer's *Snapshots* tab right-click > *New Overriding Snapshot*: `Pause` and `Ducking`
    (e.g. lower `SFX` in `Pause`, lower `Music` in `Ducking`). Assign them to the Master bank (right-click > Assign to Bank > Master).
11. **Banks**: Window > Banks. Create banks `Common`, `Music`, `Level1`. Assign `UI/Click`, `SFX/*` to `Common`,
    `Music/*` to `Music`, `Ambient/Wind` to `Level1`.
12. **File > Save**, then **File > Build** (F7).

## Hooking it up in Unity

1. Tools > kinatraa > FMOD Audio > Setup Wizard > **Fix All** (links `FMODProject/FMODProject.fspro`, creates the config, etc.).
2. Tools > kinatraa > FMOD Audio > Cue Library > **+ From FMOD event**: add all events. Ids become `click`, `explosion`,
   `footstep`, `engine_loop`, `battle_theme`, `menu_theme`, `wind`. Rename `click` to `ui_click` to match the samples.
3. In the config (`Assets/Resources/kinatraaFmodAudioConfig.asset`): add `Common` and `Music` to *Banks*, and a *Scene Banks*
   entry `Level1 -> Level1` if you want the ambience bank tied to a scene. Set FMOD Settings > *Load Banks* to `None`
   (or `Specified`) so scene banks really load and unload with their scene.

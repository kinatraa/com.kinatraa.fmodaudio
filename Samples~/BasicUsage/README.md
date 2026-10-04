# Basic Usage sample

Scripts showing the kinatraa FMOD Audio API. They use IMGUI so they need no UI or input package.

| Script | Shows |
|---|---|
| `SfxExample` | 2D, 3D-at-position and transform-attached SFX, moving a playing sound with `SetPosition`; a looping handle with `SetParameter`, pause/resume, stop with fade-out |
| `MusicExample` | `PlayMusic` / `StopMusic`, a music parameter, `PauseAll` / `ResumeAll`, pushing and releasing a snapshot |
| `VolumeSliders` | Sliders and mute toggles bound to every configured channel (bus or VCA), persisted through the settings store; *Reset to defaults* (`ResetVolumes`) and *Save* (`SaveSettings`) |
| `JsonFileSettingsStore` + `JsonFileSettingsInstaller` | A custom `ISoundSettingsStore` saving to a JSON file |
| `SceneBankExample` | `LoadBankAsync` with a callback and a custom `ISoundBankProvider` (scene name -> bank) |

## Setup

1. Build the FMOD Studio project described in the **Minimal Studio Project** sample (or use your own events and
   change the cue ids in the inspectors).
2. Run Tools > kinatraa > FMOD Audio > Setup Wizard > Fix All, and add cues `ui_click`, `explosion`, `footstep`,
   `engine_loop`, `battle_theme`, `menu_theme`, `wind` with the Cue Library window.
3. Create a scene with a camera (the wizard adds the FMOD StudioListener), add an empty GameObject with
   `SfxExample`, `MusicExample`, `VolumeSliders` and `SceneBankExample`. Optionally add `JsonFileSettingsInstaller`
   to store volumes in a JSON file instead of PlayerPrefs.
4. Press Play.

The sample assembly only compiles when FMOD for Unity is installed (`KINATRAA_FMOD` define), like the package itself.

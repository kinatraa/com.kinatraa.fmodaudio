# Getting started

## 1. Prerequisites

- Unity 2021.3 or newer (tested on 6000.3.13f1 and 6000.3.6f1).
- **FMOD for Unity 2.03.15** imported into the project (Assets > Import Package > Custom Package...). Get it from
  [fmod.com/download](https://www.fmod.com/download). Open the project once in the editor after importing so FMOD
  finishes its own post-import step (it moves its native libraries out of `Assets/Plugins/FMOD/staging`).
- **FMOD Studio 2.03.x** to author and build banks. Banks must come from the same 2.03 line as FMOD for Unity.

## 2. Install the package

- Package Manager > **+** > *Add package from git URL*: `https://github.com/kinatraa/com.kinatraa.fmodaudio.git#1.0.0`
- or `git submodule add https://github.com/kinatraa/com.kinatraa.fmodaudio.git Packages/com.kinatraa.fmodaudio`

On the first compile the bootstrap editor assembly detects FMOD and adds the `KINATRAA_FMOD` scripting define to every
build target, which enables the runtime and editor assemblies (one automatic recompile). Without FMOD nothing else compiles
and the project has no errors; the wizard explains what to install.

## 3. Run the Setup Wizard

**Tools > kinatraa > FMOD Audio > Setup Wizard**, then **Fix All**. It links the FMOD settings to your `.fspro`,
creates `Assets/Resources/kinatraaFmodAudioConfig.asset` plus an empty cue library, adds a StudioListener, disables Unity's
built-in audio, appends FMOD Studio entries to `.gitignore`, and reports anything it cannot fix (missing buses, unbuilt banks...).
See [Wizard.md](Wizard.md).

## 4. Create cues

**Tools > kinatraa > FMOD Audio > Cue Library**:
- **+ From FMOD event** or drag events from FMOD's Event Browser onto the drop area. Each event becomes a cue with a snake_case id,
  a channel guessed from its folder, and auto-release set for one-shots.
- Edit cooldowns, max instances, default parameters and tags; preview with the play button.
- Optionally **Generate SoundIds.cs** to get constants (`Sound.Play(SoundIds.UiClick)`).

## 5. Play

```csharp
using kinatraa.fmodaudio;
using UnityEngine;

public class Player : MonoBehaviour
{
    [SoundCueId] public string jumpCue = "jump";
    SoundHandle engine;

    void Start() => engine = Sound.Play("engine_loop", transform);   // follows this transform
    void Jump() => Sound.Play(jumpCue);
    void Update() => engine.SetParameter("RPM", Mathf.InverseLerp(0, 30, speed));
    void OnDestroy() => engine.Stop();
    float speed;
}
```

The system initializes itself before the first scene loads. To initialize manually, untick *Auto Initialize* in the
config and call `Sound.Initialize(config)`.

## 6. Settings menu

```csharp
musicSlider.value = Sound.GetBusVolume(SoundChannel.Music);
musicSlider.onValueChanged.AddListener(v => Sound.SetBusVolume(SoundChannel.Music, v)); // saved through the store
```

By default volumes persist with PlayerPrefs. To use your own save system see [SettingsIntegration.md](SettingsIntegration.md).

## 7. Banks

- Always-loaded banks: config > *Banks* (defaults: `Master.strings`, `Master`). Add `Common`, `Music`, etc.
- Per-scene banks: config > *Scene Banks*, a `SoundBankLoader` component, or your own `ISoundBankProvider`.
- If you use per-scene banks, set FMOD Settings > Initialization > *Load Banks* to **None** (or *Specified*);
  with *All*, FMOD keeps every bank loaded and scene banks never unload. The wizard warns about this.

## Threading

Everything in this package is **main thread only**. Do not call it from jobs, worker threads or FMOD callbacks.

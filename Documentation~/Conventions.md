# Conventions

The wizard and the defaults assume this layout. Everything is configurable (channels and bus paths in the config,
snapshot keys in the config), but following it means zero configuration.

## FMOD Studio project location

`<UnityProject>/FMODProject/FMODProject.fspro`, next to `Assets/`. Ignore `FMODProject/Build/`, `.cache/`, `.unsaved/`
and `.user/` in git (the wizard appends them). Note that ignoring `Build/` means every machine (and CI) must build banks
with FMOD Studio; commit `Build/` instead if that is not possible.

## Buses (mixer group buses under the Master Bus)

| Channel | Bus path | Use |
|---|---|---|
| Master | `bus:/` | Everything (FMOD's master bus) |
| Music | `bus:/Music` | Music events |
| SFX | `bus:/SFX` | Gameplay sound effects |
| UI | `bus:/UI` | Interface sounds |
| Voice | `bus:/Voice` | Dialogue / VO |
| Ambient | `bus:/Ambient` | Ambiences, room tones |

Route every event's master track output to one of these buses. Nested buses work too (e.g. `bus:/SFX/Weapons`); add a
channel with that bus path in the config to control it from code.

## VCAs (optional)

FMOD recommends VCAs for player-facing volume settings when one setting spans several buses. Point a channel at a VCA by
setting its *Bus Path* to `vca:/<Name>` (e.g. channel `Music` -> `vca:/Music`). Volume goes to the VCA and mute sets the VCA
to 0 (VCAs have no mute). `StopChannel` then stops only that channel's tracked sounds, because a VCA cannot stop events.
Assign VCAs to the Master bank so they are always available.

## Events

| Path | Notes |
|---|---|
| `event:/Music/...` | Looping music (loop region), parameters like `MusicIntensity` |
| `event:/SFX/...` | 3D unless clearly 2D |
| `event:/UI/...` | 2D one-shots |
| `event:/Ambient/...` | Loops and scatterers |
| `event:/Voice/...` | Dialogue |

Looping events need a **loop region** (or a sustain point); an instrument's own *Loop* flag alone ends with the timeline.

## Snapshots

| Key | Path | Used by |
|---|---|---|
| Pause | `snapshot:/Pause` | `Sound.PauseAll()` when *Pause Mode* includes the snapshot |
| Ducking | `snapshot:/Ducking` | Your code: `Sound.PushSnapshot("Ducking")` |

Assign snapshots to the Master bank so they are always available.

## Banks

| Bank | Content | Loaded |
|---|---|---|
| `Master`, `Master.strings` | Mixer, snapshots, path strings | Always (config *Banks*) |
| `Common` | Shared SFX and UI | Always |
| `Music` | Music | Always or per area |
| `Level1`, `Forest`, ... | One per level/area | With its scene (config *Scene Banks* / `SoundBankLoader`) |

## Global parameters

PascalCase nouns describing world state: `TimeOfDay`, `Weather`, `Health`, `Danger`. Local event parameters use the same
style (`RPM`, `MusicIntensity`, `Surface`).

## Cue ids

`snake_case`, unique across all registered libraries. The Cue Library derives them from the event name
(`event:/UI/Click` -> `click`, prefixed with the folder on collision: `ui_click`). `SoundIds.cs` turns them into
PascalCase constants (`ui_click` -> `SoundIds.UiClick`).

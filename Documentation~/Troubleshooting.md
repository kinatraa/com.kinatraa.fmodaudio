# Troubleshooting

**"FMOD for Unity not detected" but FMOD is installed.**
The package looks for the `FMODUnity` and `FMODUnityEditor` assembly definitions (FMOD for Unity 2.0+). FMOD versions
without asmdefs are not supported. Re-import FMOD for Unity 2.03.15.

**Compile errors mentioning FMODUnity after removing FMOD.**
The bootstrap removes `KINATRAA_FMOD` automatically after FMOD's asmdefs disappear (on import/delete and after every
compilation). If FMOD was deleted while the editor was closed, Unity may offer Safe Mode on the next open: choose
*Ignore*/*Exit Safe Mode*; the bootstrap compiles independently and removes the define, then everything recompiles. As a
manual fallback remove `KINATRAA_FMOD` from Project Settings > Player > Scripting Define Symbols.

**Nothing plays, "Unknown cue id".**
The cue's library is not registered: add it to the config's *Libraries* or call `Sound.RegisterLibrary(lib)`.

**"event ... not found. Is its bank loaded?"**
The event's bank is not loaded (add it to the config's *Banks* or *Scene Banks*), banks were not rebuilt after adding the
event, or the cue references an event deleted in FMOD Studio (the Cue Library marks it "event not found").

**"Bus 'bus:/X' not found".**
Create the group bus in FMOD Studio (Mixer > right-click Master Bus > New Group), route events to it and rebuild, or change
the channel's *Bus Path* in the config.

**Scene banks never unload.**
FMOD Settings > Initialization > *Load Banks* is *All*: FMOD loads every bank at startup. Set it to *None* or *Specified*.

**3D sounds are not positioned / FMOD warns about a listener.**
Add an FMOD StudioListener to the camera (wizard item 6) or a `SoundListenerGuard`.

**Sounds keep playing in the pause menu / UI sounds stop when paused.**
`PauseAll` with *Pause Mode = Master Bus* pauses everything, including UI. Use *Snapshot* mode (the snapshot lowers or
filters gameplay buses) and pause gameplay buses yourself, or stop/pause specific channels.

**A `SoundEmitter` with Play On = OnEnable is silent in a newly loaded scene.**
OnEnable runs before the scene's banks are loaded by the scene hook. Use *Start*, or load the bank earlier.

**Banks load slowly / first play stutters.**
Load sample data: `Sound.LoadBankAsync(name, onDone, loadSampleData: true)` or `SoundBankLoader.LoadSampleData`.

**WebGL / Android (OBB).**
FMOD loads banks asynchronously there. Plays made before loading finishes are queued (up to 128) and start
automatically; queued plays are dropped (with a warning) if banks have not finished after 10 seconds.
Not yet tested on device.

**Enter Play Mode Options (domain reload disabled).**
Supported: all static state and events are reset at the start of each play session. Subscribe to `Sound` events from
your own initialization code, not from static constructors.

**Logs.** Set the config's *Log Level* to *Info* to see initialization, bank and channel messages.
Every missing cue, event, bus, parameter or bank is logged once per play session and never throws.

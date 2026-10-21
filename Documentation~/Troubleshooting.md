# Troubleshooting

**"FMOD for Unity not detected" but FMOD is installed.**
The package looks for the `FMODUnity` and `FMODUnityEditor` assembly definitions (FMOD for Unity 2.0+). FMOD versions
without asmdefs are not supported. Re-import FMOD for Unity 2.03.15.

**Compile errors mentioning FMODUnity after removing FMOD.**
Removing FMOD inside the editor is handled automatically: the bootstrap sees FMOD's asmdefs disappear, removes
`KINATRAA_FMOD` from every build target, saves the project settings and Unity recompiles cleanly (verified).
If FMOD disappears while the editor is closed (e.g. checking out a branch without FMOD), the committed define makes the
package's FMOD assemblies fail to compile on the next open. As soon as Unity continues past those errors, the bootstrap
assembly (which never references FMOD) loads and removes the define, and the next compile is clean:
- Batch mode / CI: run once with `-ignorecompilererrors` (verified), or remove the define from `ProjectSettings/ProjectSettings.asset`.
- Interactive editor: choose *Ignore* in Unity's Safe Mode prompt (this path was not tested), or remove the define in
  Project Settings > Player > Scripting Define Symbols.

**CS0012: "The type 'EventReference' is defined in an assembly that is not referenced".**
Your script lives in an asmdef: add `FMODUnity` next to `kinatraa.FmodAudio` in its references.

**Nothing plays, "Unknown cue id".**
The cue's library is not registered: use **Add to config** in the Cue Library window (or the library's inspector), add it
to the config's *Libraries* by hand, or call `Sound.RegisterLibrary(lib)`.

**"event ... not found. Is its bank loaded?"**
The event's bank is not loaded (add it to the config's *Banks* or *Scene Banks*), banks were not rebuilt after adding the
event, or the cue references an event deleted in FMOD Studio (the Cue Library marks it "event not found").

**"Bus 'bus:/X' not found" / "VCA 'vca:/X' not found".**
Create the group bus in FMOD Studio (Mixer > right-click Master Bus > New Group), route events to it and rebuild, or change
the channel's *Bus Path* in the config. For a VCA: Mixer > VCAs > New VCA, assign buses to it, put it in the Master bank and
rebuild.

**`OnMusicChanged(null)` arrives although I did not call `StopMusic`.**
It is raised whenever the music slot ends: `StopChannel(Music)`, `StopAll`, `MusicHandle.Stop()`, a non-looping track
reaching its end, or a queued track being dropped. Give music events a loop region if they should keep playing.

**"... threw ..." errors from the sound system.**
Your own code called by `Sound` (an `ISoundPolicy`, an event handler, an `ISoundBankProvider`, a settings store or a
`LoadBankAsync` callback) threw. The error names the hook and includes the stack trace; playback continues.

**"SoundBankLoader on 'X': the sound system is not initialized".**
The loader ran before `Sound.Initialize` (Auto Initialize off). Initialize first, or set *Load On* to *Manual* and call
`Load()` afterwards.

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
automatically; queued plays are dropped (with a warning) if banks have not finished after 10 seconds. A bank load that is
still pending after 30 seconds is treated as failed (`LoadBankAsync` completes with `false`), and if FMOD itself keeps
reporting a load in progress (FMOD for Unity 2.03.15 never clears it after a failed asynchronous load, e.g. a misspelled bank
name), playback stops waiting for it with the warning "FMOD still reports a bank load in progress". Check the bank names in
the config. Not yet tested on device.

**Enter Play Mode Options (domain reload disabled).**
Supported: all static state and events are reset at the start of each play session. Subscribe to `Sound` events from
your own initialization code, not from static constructors.

**Logs.** Set the config's *Log Level* to *Info* to see initialization, bank and channel messages.
Every missing cue, event, bus, parameter or bank is logged once per play session and never throws.

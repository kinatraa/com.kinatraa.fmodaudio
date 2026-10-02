/* -----------------------------------------------------------------------------------------------
   kinatraa FMOD Audio - minimal FMOD Studio 2.03 project for the samples and the wizard smoke test.

   1. Create an empty project in FMOD Studio 2.03 (File > New, File > Save As... e.g. <Unity project>/FMODProject/FMODProject.fspro).
   2. python3 make_test_audio.py <Unity project>/FMODProject            (writes FMODProject/SourceAudio/*.wav)
   3. Close FMOD Studio, then run the command line tool that ships with it:
        macOS:   "/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl" -script CreateMinimalProject.js <path>/FMODProject.fspro
        Windows: "C:\Program Files\FMOD SoundSystem\FMOD Studio 2.03.xx\fmodstudiocl.exe" -script CreateMinimalProject.js <path>\FMODProject.fspro
   The script is idempotent: existing buses, events, banks and snapshots are reused. It saves and builds the project.
   ----------------------------------------------------------------------------------------------- */
var project = studio.project;
var ws = project.workspace;
var audioFolder = project.filePath.replace(/[^\/\\]+$/, "") + "SourceAudio/";

function bus(name) {
    var b = project.lookup("bus:/" + name);
    if (b) return b;
    b = project.create("MixerGroup");
    b.name = name;
    b.output = ws.mixer.masterBus;
    return b;
}

function eventFolder(name) {
    var f = project.lookup("event:/" + name);
    if (f) return f;
    f = project.create("EventFolder");
    f.name = name;
    f.folder = ws.masterEventFolder;
    return f;
}

function bank(name) {
    var b = project.lookup("bank:/" + name);
    if (b) return b;
    b = project.create("Bank");
    b.name = name;
    return b;
}

var masterBank = project.model.Bank.findInstances().filter(function (b) { return b.isMasterBank; })[0];

function addEvent(folderName, name, is3D, file, seconds, loop, busName, bankName) {
    var existing = project.lookup("event:/" + folderName + "/" + name);
    if (existing) return existing;
    var e = ws.addEvent(name, is3D);
    e.folder = eventFolder(folderName);
    var sound = e.addGroupTrack("Audio").addSound(e.timeline, "SingleSound", 0, seconds);
    sound.audioFile = project.importAudioFile(audioFolder + file);
    if (loop) {
        sound.looping = true;
        e.markerTracks[0].addRegion(0, seconds, "Loop", project.regionLoopMode.Looping);
    }
    e.mixerInput.output = bus(busName);
    e.relationships.banks.add(bank(bankName));
    return e;
}

["Music", "SFX", "UI", "Voice", "Ambient"].forEach(bus);

addEvent("UI", "Click", false, "click.wav", 0.08, false, "UI", "Common");
addEvent("SFX", "Explosion", true, "explosion.wav", 0.8, false, "SFX", "Common");
addEvent("SFX", "Footstep", true, "footstep.wav", 0.12, false, "SFX", "Common");
var engine = addEvent("SFX", "EngineLoop", true, "engine.wav", 1.0, true, "SFX", "Common");
if (engine.parameters.length === 0) engine.addGameParameter({ name: "RPM", type: project.parameterType.User, min: 0, max: 1 });
var battle = addEvent("Music", "BattleTheme", false, "music.wav", 4.0, true, "Music", "Music");
if (battle.parameters.length === 0) battle.addGameParameter({ name: "MusicIntensity", type: project.parameterType.User, min: 0, max: 1 });
addEvent("Music", "MenuTheme", false, "music.wav", 4.0, true, "Music", "Music");
addEvent("Ambient", "Wind", false, "wind.wav", 2.0, true, "Ambient", "Level1");

var timeOfDay = project.model.ParameterPreset.findInstances().filter(function (p) { return p.name === "TimeOfDay"; })[0];
if (!timeOfDay) timeOfDay = ws.addGameParameter({ name: "TimeOfDay", type: project.parameterType.User, min: 0, max: 1 });
(timeOfDay.parameter || timeOfDay).isGlobal = true;

["Pause", "Ducking"].forEach(function (name) {
    if (project.lookup("snapshot:/" + name)) return;
    var snapshot = project.create("Snapshot");
    snapshot.name = name;
    ws.mixer.snapshotList.relationships.items.add(snapshot);
    snapshot.relationships.banks.add(masterBank);
});

project.save();
console.log("kinatraa minimal project ready; build " + (project.build() ? "succeeded" : "FAILED"));

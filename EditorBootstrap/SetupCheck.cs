using System;
using System.Collections.Generic;

namespace kinatraa.FmodAudio.Editor
{
    /// <summary>Status of a setup check.</summary>
    public enum SetupStatus
    {
        /// <summary>Configured correctly.</summary>
        OK,
        /// <summary>Informational; never fails validation.</summary>
        Info,
        /// <summary>Works, but something is likely wrong or not following the convention.</summary>
        Warning,
        /// <summary>Required setup is missing. Fails validation.</summary>
        Missing,
    }

    /// <summary>Outcome of evaluating a <see cref="SetupCheck"/>.</summary>
    public readonly struct SetupCheckResult
    {
        /// <summary>Status.</summary>
        public readonly SetupStatus Status;
        /// <summary>Human-readable explanation, including how to fix it manually.</summary>
        public readonly string Message;

        /// <summary>Creates a result.</summary>
        public SetupCheckResult(SetupStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        /// <summary>OK result.</summary>
        public static SetupCheckResult Ok(string message) => new SetupCheckResult(SetupStatus.OK, message);
        /// <summary>Info result.</summary>
        public static SetupCheckResult Info(string message) => new SetupCheckResult(SetupStatus.Info, message);
        /// <summary>Warning result.</summary>
        public static SetupCheckResult Warn(string message) => new SetupCheckResult(SetupStatus.Warning, message);
        /// <summary>Missing result.</summary>
        public static SetupCheckResult Missing(string message) => new SetupCheckResult(SetupStatus.Missing, message);
    }

    /// <summary>
    /// One item of the Setup Wizard checklist. Checks that need FMOD types live in the FMOD-dependent editor assembly
    /// and register themselves through <see cref="SetupCheckRegistry"/>. Fixes must be idempotent and log what they change.
    /// </summary>
    public abstract class SetupCheck
    {
        /// <summary>Sort order in the checklist (the numbering of the wizard).</summary>
        public abstract int Order { get; }
        /// <summary>Short title.</summary>
        public abstract string Title { get; }
        /// <summary>Evaluates the project state. Must not modify anything.</summary>
        public abstract SetupCheckResult Evaluate();
        /// <summary>True when <see cref="Fix"/> can resolve the given result.</summary>
        public virtual bool CanFix(SetupCheckResult result) => false;
        /// <summary>Label of the fix button.</summary>
        public virtual string FixLabel => "Fix";
        /// <summary>Applies the fix. Must be idempotent and log every change.</summary>
        public virtual void Fix() { }
        /// <summary>When false, "Fix All" skips this check (its Fix button still works).</summary>
        public virtual bool IncludedInFixAll => true;
        /// <summary>True when <see cref="DrawGUI"/> draws something (the wizard then shows a "Details" foldout).</summary>
        public virtual bool HasDetails => false;
        /// <summary>Optional extra controls shown under the check in the wizard (IMGUI).</summary>
        public virtual void DrawGUI() { }

        /// <summary>Logs a change made by a fix, so every modification is visible in the Console.</summary>
        protected static void LogChange(string message) => UnityEngine.Debug.Log("[kinatraa.fmodaudio] Setup: " + message);
    }

    /// <summary>Registry of setup checks. FMOD-dependent checks register from the FMOD-dependent editor assembly.</summary>
    public static class SetupCheckRegistry
    {
        private static readonly List<SetupCheck> checks = new List<SetupCheck>();

        /// <summary>
        /// Returns the FMOD Studio project folder relative to the Unity project root (e.g. "FMODProject"), or null.
        /// Set by the FMOD-dependent editor assembly, which can read the FMOD settings.
        /// </summary>
        public static Func<string> StudioProjectFolderProvider;

        /// <summary>Registers a check (ignored if one of the same type is already registered).</summary>
        public static void Register(SetupCheck check)
        {
            if (check == null || checks.Exists(c => c.GetType() == check.GetType())) return;
            checks.Add(check);
            checks.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        /// <summary>True once the FMOD-dependent editor assembly registered its checks.</summary>
        public static bool FmodChecksRegistered { get; private set; }

        /// <summary>Registers the checks of the FMOD-dependent editor assembly.</summary>
        public static void RegisterFmodChecks(params SetupCheck[] fmodChecks)
        {
            foreach (SetupCheck check in fmodChecks) Register(check);
            FmodChecksRegistered = true;
        }

        /// <summary>All registered checks, ordered.</summary>
        public static IReadOnlyList<SetupCheck> All
        {
            get
            {
                EnsureBuiltIns();
                return checks;
            }
        }

        private static void EnsureBuiltIns()
        {
            Register(new FmodInstalledCheck());
            Register(new ScriptingDefineCheck());
            Register(new UnityAudioCheck());
            Register(new FoldersCheck());
            Register(new GitIgnoreCheck());
        }
    }
}

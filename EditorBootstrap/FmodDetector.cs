using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;

namespace kinatraa.fmodaudio.Editor
{
    /// <summary>Result of <see cref="FmodDetector.Detect"/>.</summary>
    public enum FmodInstallState
    {
        /// <summary>FMOD for Unity is not in the project.</summary>
        NotInstalled,
        /// <summary>FMOD for Unity is installed with its FMODUnity / FMODUnityEditor assembly definitions.</summary>
        Installed,
        /// <summary>FMOD types exist but not in the FMODUnity assembly (very old FMOD without asmdefs). Not supported.</summary>
        InstalledWithoutAsmdef,
    }

    /// <summary>
    /// Detects FMOD for Unity without referencing it. Lives in the bootstrap assembly, which compiles in every project.
    /// Detection is based on FMOD's assembly definition files (so it reflects the project on disk even before a domain
    /// reload) plus a type lookup for diagnostics and the version number.
    /// </summary>
    public static class FmodDetector
    {
        /// <summary>Scripting define that enables the package's FMOD-dependent assemblies.</summary>
        public const string Define = "KINATRAA_FMOD";
        /// <summary>Assembly name of FMOD for Unity's runtime asmdef (verified on 2.03.15).</summary>
        public const string RuntimeAssembly = "FMODUnity";
        /// <summary>Assembly name of FMOD for Unity's editor asmdef (verified on 2.03.15).</summary>
        public const string EditorAssembly = "FMODUnityEditor";
        /// <summary>FMOD version this package was built and tested against (FMOD.VERSION.number format).</summary>
        public const uint TestedVersion = 0x00020315;
        /// <summary>FMOD download page.</summary>
        public const string DownloadUrl = "https://www.fmod.com/download#fmodforunity";

        /// <summary>Current install state.</summary>
        public static FmodInstallState Detect()
        {
            if (!string.IsNullOrEmpty(RuntimeAsmdefPath) && !string.IsNullOrEmpty(EditorAsmdefPath)) return FmodInstallState.Installed;
            Type runtimeManager = FindType("FMODUnity.RuntimeManager");
            if (runtimeManager != null && runtimeManager.Assembly.GetName().Name != RuntimeAssembly) return FmodInstallState.InstalledWithoutAsmdef;
            return FmodInstallState.NotInstalled;
        }

        /// <summary>True when FMOD for Unity with its asmdefs is in the project.</summary>
        public static bool IsInstalled => Detect() == FmodInstallState.Installed;

        /// <summary>Path of FMODUnity.asmdef, or null.</summary>
        public static string RuntimeAsmdefPath => CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(RuntimeAssembly);

        /// <summary>Path of FMODUnityEditor.asmdef, or null.</summary>
        public static string EditorAsmdefPath => CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(EditorAssembly);

        /// <summary>Assembly that currently contains FMODUnity.RuntimeManager (diagnostics), or null.</summary>
        public static string RuntimeManagerAssemblyName => FindType("FMODUnity.RuntimeManager")?.Assembly.GetName().Name;

        /// <summary>
        /// Installed FMOD version (FMOD.VERSION.number format, e.g. 0x00020315), or 0 if unknown. Read by reflection, or
        /// from fmod.cs next to the asmdef when the FMOD assembly is not loaded yet.
        /// </summary>
        public static uint InstalledVersion
        {
            get
            {
                Type version = FindType("FMOD.VERSION");
                FieldInfo number = version?.GetField("number", BindingFlags.Public | BindingFlags.Static);
                if (number != null && number.IsLiteral) return Convert.ToUInt32(number.GetRawConstantValue());

                string asmdef = RuntimeAsmdefPath;
                if (string.IsNullOrEmpty(asmdef)) return 0;
                string source = Path.Combine(Path.GetDirectoryName(asmdef) ?? string.Empty, "src", "fmod.cs");
                if (!File.Exists(source)) return 0;
                Match m = Regex.Match(File.ReadAllText(source), @"const\s+int\s+number\s*=\s*0x([0-9A-Fa-f]+)");
                return m.Success ? Convert.ToUInt32(m.Groups[1].Value, 16) : 0;
            }
        }

        /// <summary>Formats an FMOD version number as "2.03.15".</summary>
        public static string FormatVersion(uint version)
        {
            if (version == 0) return "unknown";
            return $"{(version >> 16) & 0xFFFF:X}.{(version >> 8) & 0xFF:X2}.{version & 0xFF:X2}";
        }

        /// <summary>True when the version is on the tested 2.03 line.</summary>
        public static bool IsSupportedLine(uint version) => (version & 0xFFFFFF00) == (TestedVersion & 0xFFFFFF00);

        private static Type FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}

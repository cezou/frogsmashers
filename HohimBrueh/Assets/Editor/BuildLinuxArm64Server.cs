using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FrogSmashers.Editor
{
    public static class LinuxArm64ServerBuilder
    {
        const string BaseOutputDir = "Builds/LinuxArm64Server";
        const string FolderPrefix  = "FrogSmashersServerArm64V0.";
        const string ExeName       = "FrogSmashers";

        [MenuItem("Tools/FrogSmashers/Build Linux ARM64 Server")]
        public static void Build()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                Log("ERROR: Linux Build Support module is not installed. Add it via Unity Hub.");
                return;
            }

            string baseDir = GetCliArg("-buildOutput") ?? BaseOutputDir;
            Directory.CreateDirectory(baseDir);

            int nextVersion = ResolveNextVersion(baseDir);
            string versionFolder = $"{FolderPrefix}{nextVersion}";
            string outputDir = Path.Combine(baseDir, versionFolder);
            Directory.CreateDirectory(outputDir);

            int previousArch = PlayerSettings.GetArchitecture(NamedBuildTarget.Server);
            int arm64Value = (int)OSArchitecture.ARM64;
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
            Log($"Before: arch={previousArch}, scriptingBackend={previousBackend}");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);

            // Linux Server arch is controlled via SetPlatformSettings (PlayerSettings.SetArchitecture
            // only applies to iOS/tvOS/visionOS per Unity 6 docs).
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "Linux64", "Architecture", "ARM64");
            EditorUserBuildSettings.SetPlatformSettings("Server", "Linux64", "Architecture", "ARM64");

            var prevSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
            {
                Log($"Switching active build target → StandaloneLinux64");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            }

            Log($"GetPlatformSettings(Standalone,Linux64,Architecture)={EditorUserBuildSettings.GetPlatformSettings("Standalone", "Linux64", "Architecture")}");
            Log($"GetPlatformSettings(Server,Linux64,Architecture)={EditorUserBuildSettings.GetPlatformSettings("Server", "Linux64", "Architecture")}");
            Log($"After:  arch={PlayerSettings.GetArchitecture(NamedBuildTarget.Server)}, scriptingBackend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server)}");

            try
            {
                var scenes = new List<string>();
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled) scenes.Add(s.path);

                var options = new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = Path.Combine(outputDir, ExeName),
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Server,
                    options = BuildOptions.CleanBuildCache,
                };

                Log($"Building {scenes.Count} scene(s) → {options.locationPathName} (Linux ARM64 server)");
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                    Log($"Build {versionFolder} succeeded. Size: {summary.totalSize / (1024 * 1024)} MB. Time: {summary.totalTime}");
                else
                    Log($"Build {versionFolder} FAILED. Result: {summary.result}");
            }
            finally
            {
                PlayerSettings.SetArchitecture(NamedBuildTarget.Server, previousArch);
                EditorUserBuildSettings.standaloneBuildSubtarget = prevSubtarget;
                Log($"Architecture restored to {previousArch}, subtarget restored to {prevSubtarget}");
            }
        }

        static int ResolveNextVersion(string baseDir)
        {
            int max = 0;
            var regex = new Regex(@"^" + Regex.Escape(FolderPrefix) + @"(\d+)$");
            foreach (var dir in Directory.GetDirectories(baseDir))
            {
                var name = Path.GetFileName(dir);
                var m = regex.Match(name);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n > max)
                    max = n;
            }
            return max + 1;
        }

        static string GetCliArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static void Log(string msg)
        {
            Debug.Log($"[LinuxArm64ServerBuilder] {msg}");
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FrogSmashers.Editor
{
    public static class LinuxServerBuilder
    {
        const string BaseOutputDir = "Builds/LinuxServer";
        const string FolderPrefix  = "FrogSmashersServerV0.";
        const string ExeName       = "FrogSmashers.x86_64";

        [MenuItem("Tools/FrogSmashers/Build Linux Server")]
        public static void Build()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                Log("ERROR: Linux Build Support module is not installed. " +
                    "Open Unity Hub > Installs > 6000.4.7f1 > Add Modules > 'Linux Build Support (IL2CPP)'.");
                return;
            }

            string baseDir = GetCliArg("-buildOutput") ?? BaseOutputDir;
            Directory.CreateDirectory(baseDir);

            int nextVersion = ResolveNextVersion(baseDir);
            string versionFolder = $"{FolderPrefix}{nextVersion}";
            string outputDir = Path.Combine(baseDir, versionFolder);
            Directory.CreateDirectory(outputDir);

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
                options = BuildOptions.None,
            };

            Log($"Building {scenes.Count} scene(s) → {options.locationPathName} (Linux x86_64 server subtarget)");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
                Log($"Build {versionFolder} succeeded. Size: {summary.totalSize / (1024 * 1024)} MB. Time: {summary.totalTime}");
            else
                Log($"Build {versionFolder} FAILED. Result: {summary.result}");
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
            Debug.Log($"[LinuxServerBuilder] {msg}");
        }
    }
}

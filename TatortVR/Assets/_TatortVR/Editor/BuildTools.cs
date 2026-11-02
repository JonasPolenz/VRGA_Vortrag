using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TatortVR.EditorTools
{
    /// <summary>
    /// Builds the Quest APK without installing it, to check that the Android build works.
    /// The summary is also written to Logs/tatort_build_result.txt for tools that cannot read the console.
    /// </summary>
    public static class BuildTools
    {
        const string k_ApkPath = "Builds/TatortVR.apk";
        const string k_ResultPath = "Logs/tatort_build_result.txt";

        [MenuItem("Tools/TatortVR/Test-Build (APK)")]
        public static void BuildTestApk()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = k_ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            var text = $"result={summary.result}\nerrors={summary.totalErrors}\nwarnings={summary.totalWarnings}\n" +
                       $"sizeMB={summary.totalSize / (1024f * 1024f):0.0}\ntime={summary.totalTime}\noutput={summary.outputPath}\n";
            foreach (var message in report.steps.SelectMany(step => step.messages))
            {
                if (message.type == LogType.Error || message.type == LogType.Exception)
                    text += "ERR: " + message.content + "\n";
            }

            File.WriteAllText(k_ResultPath, text);
            Debug.Log("[TatortBuild] " + text.Replace("\n", " | "));
        }
    }
}

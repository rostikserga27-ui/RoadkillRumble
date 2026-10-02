using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// One click from project to a zip you can send to a friend: release Windows build,
    /// steam_appid.txt next to the exe (Steam needs it until the game has its own App ID),
    /// the player instructions, and none of Unity's do-not-ship folders.
    /// </summary>
    public static class BuildForFriend
    {
        const string BuildFolder = "Builds/RoadkillRumble";
        const string ZipPath = "Builds/RoadkillRumble.zip";
        const string Instructions = "Builds/ЧИТАЙ_МЕНЯ.txt";   // ЧИТАЙ_МЕНЯ.txt
        const string Checklist = "Builds/ЧЕК-ЛИСТ_ТЕСТА.txt";   // ЧЕК-ЛИСТ_ТЕСТА.txt

        [MenuItem("Roadkill/Build For Friend (zip)")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("Roadkill: no scenes in Build Settings. Add Assets/Scenes/Greybox.unity first.");
                return;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = $"{BuildFolder}/RoadkillRumble.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError($"Roadkill: build failed ({report.summary.totalErrors} errors).");
                return;
            }

            if (File.Exists("steam_appid.txt")) File.Copy("steam_appid.txt", $"{BuildFolder}/steam_appid.txt", true);
            if (File.Exists(Instructions)) File.Copy(Instructions, $"{BuildFolder}/{Path.GetFileName(Instructions)}", true);
            if (File.Exists(Checklist)) File.Copy(Checklist, $"{BuildFolder}/{Path.GetFileName(Checklist)}", true);

            if (File.Exists(ZipPath)) File.Delete(ZipPath);
            using (var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create))
            {
                string root = Path.GetFullPath(BuildFolder);
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length + 1);
                    if (relative.Contains("DontShip") || relative.Contains("BackUpThisFolder")) continue;
                    zip.CreateEntryFromFile(file, relative.Replace('\\', '/'), System.IO.Compression.CompressionLevel.Optimal);
                }
            }

            long megabytes = new FileInfo(ZipPath).Length / (1024 * 1024);
            Debug.Log($"Roadkill: {ZipPath} is ready ({megabytes} MB). Send it to your friend.");
            EditorUtility.RevealInFinder(ZipPath);
        }
    }
}

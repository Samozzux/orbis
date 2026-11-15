using System;
using System.IO;
using Orbis.Core.Versioning;
using Orbis.UI.App;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Orbis.EditorTools.Build
{
    /// <summary>
    /// Build da riga di comando (docs/07 §3), ad esempio:
    /// Unity -batchmode -nographics -quit -projectPath . -buildTarget WebGL
    ///       -executeMethod Orbis.EditorTools.Build.BuildScript.BuildWeb
    /// Variabili d'ambiente: ORBIS_VERSION (es. 0.1.0), ORBIS_BUILD_NUMBER, ORBIS_COMMIT,
    /// per Android ORBIS_KEYSTORE_PATH, ORBIS_KEYSTORE_PASS, ORBIS_KEY_ALIAS, ORBIS_KEY_PASS.
    /// </summary>
    public static class BuildScript
    {
        public const string IosOutput = "build/ios";
        public const string AndroidOutput = "build/android/Orbis.apk";
        public const string WebOutput = "build/web";

        public static void BuildIOS() => Build(BuildTarget.iOS, IosOutput);
        public static void BuildAndroid() => Build(BuildTarget.Android, AndroidOutput);
        public static void BuildWeb() => Build(BuildTarget.WebGL, WebOutput);

        private static void Build(BuildTarget target, string output)
        {
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != target)
                    throw new InvalidOperationException($"Avviare Unity con -buildTarget {target} (attivo: {EditorUserBuildSettings.activeBuildTarget}).");

                ProjectGenerator.GenerateAll();
                var info = ApplyVersion();
                WriteBuildInfo(info);
                if (target == BuildTarget.Android)
                    ConfigureAndroidSigning();

                var parent = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { OrbisPaths.BootScene },
                    locationPathName = output,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                    options = BuildOptions.None,
                });

                var s = report.summary;
                Debug.Log($"[Orbis] Build {target}: {s.result}, {s.totalErrors} errori, {s.totalWarnings} avvisi, " +
                          $"{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalMinutes:0.0} min → {output}");
                if (s.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[Orbis] Build {target} fallita.");
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        private static BuildInfo ApplyVersion()
        {
            var versionText = Environment.GetEnvironmentVariable("ORBIS_VERSION");
            var version = AppVersion.TryParse(versionText, out var v) ? v : AppVersion.Zero;
            int.TryParse(Environment.GetEnvironmentVariable("ORBIS_BUILD_NUMBER"), out var build);
            build = Math.Max(build, 0);

            PlayerSettings.bundleVersion = version.ToString();
            PlayerSettings.iOS.buildNumber = build.ToString();
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, build);

            return new BuildInfo
            {
                version = version.ToString(),
                build = build,
                commit = Environment.GetEnvironmentVariable("ORBIS_COMMIT") ?? string.Empty,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"),
            };
        }

        private static void WriteBuildInfo(BuildInfo info)
        {
            LocalizationGenerator.EnsureFolder(Path.GetDirectoryName(OrbisPaths.BuildInfoJson)?.Replace('\\', '/'));
            File.WriteAllText(OrbisPaths.BuildInfoJson, JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset(OrbisPaths.BuildInfoJson, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureAndroidSigning()
        {
            var keystore = Environment.GetEnvironmentVariable("ORBIS_KEYSTORE_PATH");
            if (!string.IsNullOrEmpty(keystore) && File.Exists(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(keystore);
                PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ORBIS_KEYSTORE_PASS");
                PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ORBIS_KEY_ALIAS");
                PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ORBIS_KEY_PASS");
                Debug.Log("[Orbis] APK firmato con il keystore di Orbis.");
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[Orbis] Keystore assente: APK firmato con la chiave di debug (non aggiornabile sopra la versione firmata).");
            }
        }
    }
}

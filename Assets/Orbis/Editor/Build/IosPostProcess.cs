#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Orbis.EditorTools.Build
{
    /// <summary>
    /// Completa il progetto Xcode (docs/01 §10): testi dei permessi dalla sorgente Localization nell'Info.plist;
    /// per il plugin Swift del Keychain e di Face ID (M3) la libreria LocalAuthentication e la versione di Swift.
    /// </summary>
    public static class IosPostProcess
    {
        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
                return;
            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;
            root.SetString("CFBundleDisplayName", OrbisPaths.ProductName);
            root.SetString("NSFaceIDUsageDescription", LocalizationGenerator.SourceText("ios.usage.faceid"));
            root.SetString("NSAppleMusicUsageDescription", LocalizationGenerator.SourceText("ios.usage.apple_music"));
            root.SetString("NSPhotoLibraryAddUsageDescription", LocalizationGenerator.SourceText("ios.usage.photos_add"));
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            var framework = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework, "LocalAuthentication.framework", false);
            project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
            project.WriteToFile(projectPath);
        }
    }
}
#endif

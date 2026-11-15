#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Orbis.EditorTools.Build
{
    /// <summary>Completa l'Info.plist del progetto Xcode (docs/01 §10): testi dei permessi dalla sorgente Localization.</summary>
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
        }
    }
}
#endif

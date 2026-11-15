using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.EditorTools
{
    /// <summary>Impostazioni del player per iOS, Android e Web (CLAUDE.md "Stack fissato", docs/07 §3 e §5).</summary>
    public static class PlayerSettingsConfigurator
    {
        public static void Apply()
        {
            PlayerSettings.companyName = OrbisPaths.ProductName;
            PlayerSettings.productName = OrbisPaths.ProductName;
            foreach (var target in new[] { NamedBuildTarget.iOS, NamedBuildTarget.Android, NamedBuildTarget.WebGL, NamedBuildTarget.Standalone })
                PlayerSettings.SetApplicationIdentifier(target, OrbisPaths.BundleId);

            // gioco in orizzontale (il telefono nel gioco gestirà il verticale a runtime)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.statusBarHidden = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;

            ApplyIos();
            ApplyAndroid();
            ApplyWeb();
        }

        private static void ApplyIos()
        {
            PlayerSettings.iOS.targetOSVersionString = "26.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.hideHomeButton = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
        }

        private static void ApplyAndroid()
        {
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
            PlayerSettings.Android.forceInternetPermission = true;
            EditorUserBuildSettings.buildAppBundle = false; // APK, niente Play Store
        }

        private static void ApplyWeb()
        {
            PlayerSettings.WebGL.template = "PROJECT:Orbis";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true; // GitHub Pages non imposta Content-Encoding
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.WebGPU, GraphicsDeviceType.OpenGLES3 });
        }
    }
}

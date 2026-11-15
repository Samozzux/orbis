using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Orbis.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

namespace Orbis.Tests.EditMode
{
    /// <summary>Controlla che gli asset generati da codice siano coerenti con la specifica.</summary>
    public class GeneratedProjectTests
    {
        private static readonly Regex UxmlKey = new Regex("text=\"#([a-z0-9_.]+)\"");
        private static readonly Regex CodeKey = new Regex("\"((?:app|loading|menu|web|ios)\\.[a-z0-9_.]+)\"");

        [Test]
        public void ScenaDiAvvio_NelleBuildSettings()
        {
            Assert.That(File.Exists(OrbisPaths.BootScene), "Scena Boot mancante: eseguire Orbis > Genera progetto");
            var scenes = EditorBuildSettings.scenes;
            Assert.That(scenes.Length, Is.GreaterThan(0));
            Assert.That(scenes[0].path, Is.EqualTo(OrbisPaths.BootScene));
            Assert.That(scenes[0].enabled);
        }

        [Test]
        public void PanelSettings_HaIlTema()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(OrbisPaths.PanelSettings);
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.themeStyleSheet, Is.Not.Null);
        }

        [Test]
        public void TestiUxml_EsistonoNellaSorgente()
        {
            var keys = LocalizationGenerator.LoadSource().entries.Select(e => e.key).ToHashSet();
            var used = UxmlKey.Matches(File.ReadAllText(OrbisPaths.AppUxml)).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.That(used, Is.Not.Empty);
            var missing = used.Where(k => !keys.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "Chiavi usate in App.uxml ma assenti in data/localization/ui.json");
        }

        [Test]
        public void TestiNelCodice_EsistonoNellaSorgente()
        {
            var keys = LocalizationGenerator.LoadSource().entries.Select(e => e.key).ToHashSet();
            var used = Directory.GetFiles("Assets/Orbis", "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Tests/"))
                .SelectMany(p => CodeKey.Matches(File.ReadAllText(p)).Cast<Match>().Select(m => m.Groups[1].Value))
                .Distinct()
                .ToList();
            var missing = used.Where(k => !keys.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "Chiavi usate nel codice ma assenti in data/localization/ui.json");
        }

        [Test]
        public void TabellaLocalization_UgualeAllaSorgente()
        {
            var source = LocalizationGenerator.LoadSource();
            var collection = LocalizationEditorSettings.GetStringTableCollection(source.table);
            Assert.That(collection, Is.Not.Null, "Tabella UI mancante");
            var table = collection.GetTable(LocalizationGenerator.LocaleCode) as StringTable;
            Assert.That(table, Is.Not.Null, "Tabella italiana mancante");
            foreach (var e in source.entries)
            {
                var entry = table.GetEntry(e.key);
                Assert.That(entry, Is.Not.Null, $"Manca {e.key}");
                Assert.That(entry.Value, Is.EqualTo(e.it), $"Testo diverso per {e.key}: rigenerare il progetto");
            }
        }

        [Test]
        public void ImpostazioniPlayer_ComeDaSpecifica()
        {
            Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS), Is.EqualTo(OrbisPaths.BundleId));
            Assert.That(PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android), Is.EqualTo(OrbisPaths.BundleId));
            Assert.That(PlayerSettings.productName, Is.EqualTo("Orbis"));
            Assert.That(PlayerSettings.iOS.targetOSVersionString, Is.EqualTo("26.0"));
            Assert.That(PlayerSettings.iOS.targetDevice, Is.EqualTo(iOSTargetDevice.iPhoneAndiPad));
            Assert.That(PlayerSettings.Android.minSdkVersion, Is.EqualTo(AndroidSdkVersions.AndroidApiLevel29));
            Assert.That(PlayerSettings.Android.targetArchitectures, Is.EqualTo(AndroidArchitecture.ARM64));
            Assert.That(PlayerSettings.WebGL.compressionFormat, Is.EqualTo(WebGLCompressionFormat.Brotli));
            Assert.That(PlayerSettings.WebGL.decompressionFallback, Is.True);
        }
    }
}

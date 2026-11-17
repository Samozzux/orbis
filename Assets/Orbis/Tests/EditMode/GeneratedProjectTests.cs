using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Orbis.EditorTools;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.UIElements;

namespace Orbis.Tests.EditMode
{
    /// <summary>Controlla che gli asset generati da codice siano coerenti con la specifica.</summary>
    public class GeneratedProjectTests
    {
        private static readonly Regex UxmlKey = new Regex("text=\"#([a-z0-9_.]+)\"");
        private static readonly Regex CodeKey = new Regex("\"((?:app|loading|menu|web|ios|settings|game|details|weather|osm|build|finance|panel|newgame|saves|rewind|mission|offline|time)\\.[a-z0-9_.]+)\"");

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
                .Where(k => !k.EndsWith(".")) // prefissi completati a runtime (controllati da TestiDegliEnum)
                .Distinct()
                .ToList();
            var missing = used.Where(k => !keys.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "Chiavi usate nel codice ma assenti in data/localization/ui.json");
        }

        /// <summary>Chiavi composte a runtime dai valori degli enum (motivi, categorie, voci del bilancio, aliquote, difficoltà).</summary>
        [Test]
        public void TestiDegliEnum_EsistonoNellaSorgente()
        {
            var keys = LocalizationGenerator.LoadSource().entries.Select(e => e.key).ToHashSet();
            var expected = new List<string>();
            void Add<T>(string prefix) where T : System.Enum
            {
                foreach (var v in System.Enum.GetNames(typeof(T)))
                    if (v != "Count") expected.Add(prefix + v);
            }
            Add<Orbis.Core.Game.BuildResult>("result.");
            Add<Orbis.Core.Game.CatalogCategory>("category.");
            Add<Orbis.Core.Game.LedgerCategory>("ledger.");
            Add<Orbis.Core.Game.TaxKind>("tax.");
            Add<Orbis.Core.Game.Difficulty>("newgame.difficulty.");
            var missing = expected.Where(k => !keys.Contains(k)).ToList();
            Assert.That(missing, Is.Empty, "Testi degli enum assenti in data/localization/ui.json");
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
        public void LinguaDiAvvio_SempreItaliano()
        {
            // su dispositivi in un'altra lingua il selettore "specifico" deve scegliere l'italiano
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            Assert.That(settings, Is.Not.Null);
            var specific = settings.GetStartupLocaleSelectors().OfType<SpecificLocaleSelector>().ToList();
            Assert.That(specific, Is.Not.Empty, "Manca il selettore di lingua specifico");
            Assert.That(specific.Select(s => s.LocaleId.Code), Is.All.EqualTo(LocalizationGenerator.LocaleCode));
        }

        [Test]
        public void CittaDelMenu_HaTuttiIMateriali()
        {
            var names = System.Enum.GetNames(typeof(Orbis.UI.Menu3D.MenuCityMaterial));
            Assert.That(names.Length, Is.EqualTo(Orbis.UI.Menu3D.MenuCity.MaterialCount));
            foreach (var n in names)
            {
                var mat = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{MenuAssetsGenerator.Folder}/Materials/City_{n}.mat");
                Assert.That(mat, Is.Not.Null, $"Materiale mancante: {n}");
                Assert.That(mat.shader.name, Is.EqualTo("Universal Render Pipeline/Simple Lit"));
            }
            var blur = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(MenuAssetsGenerator.BlurMaterial);
            Assert.That(blur, Is.Not.Null);
            Assert.That(blur.shader.name, Is.EqualTo(MenuAssetsGenerator.BlurShader));
            Assert.That(blur.passCount, Is.EqualTo(2));
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
            // telefono e tablet: verticale e orizzontale, rotazione automatica di default
            Assert.That(PlayerSettings.defaultInterfaceOrientation, Is.EqualTo(UIOrientation.AutoRotation));
            Assert.That(PlayerSettings.allowedAutorotateToPortrait, Is.True);
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeLeft, Is.True);
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeRight, Is.True);
        }
    }
}

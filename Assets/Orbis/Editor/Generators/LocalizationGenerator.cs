using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Crea/aggiorna le tabelle Localization a partire da data/localization/ui.json.
    /// Lingua unica per ora: italiano (CLAUDE.md, "Lingua").
    /// </summary>
    public static class LocalizationGenerator
    {
        public const string LocaleCode = "it";

        [Serializable]
        public sealed class SourceEntry
        {
            public string key;
            public string it;
        }

        [Serializable]
        public sealed class SourceFile
        {
            public string table;
            public string[] locales;
            public SourceEntry[] entries;
        }

        public static SourceFile LoadSource()
        {
            if (!File.Exists(OrbisPaths.LocalizationSource))
                throw new FileNotFoundException("Sorgente dei testi mancante", OrbisPaths.LocalizationSource);
            var source = JsonUtility.FromJson<SourceFile>(File.ReadAllText(OrbisPaths.LocalizationSource));
            if (source == null || string.IsNullOrEmpty(source.table) || source.entries == null)
                throw new InvalidDataException($"{OrbisPaths.LocalizationSource} non valido");
            var duplicates = source.entries.GroupBy(e => e.key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
                throw new InvalidDataException("Chiavi duplicate: " + string.Join(", ", duplicates));
            return source;
        }

        /// <summary>Testo della sorgente per una chiave (usato in build, es. Info.plist e pagina web).</summary>
        public static string SourceText(string key)
        {
            var entry = LoadSource().entries.FirstOrDefault(e => e.key == key);
            if (entry == null)
                throw new KeyNotFoundException($"Testo mancante in {OrbisPaths.LocalizationSource}: {key}");
            return entry.it;
        }

        public static void Generate()
        {
            var source = LoadSource();
            EnsureFolder(OrbisPaths.LocalizationDir);
            EnsureFolder(OrbisPaths.LocalizationDir + "/Locales");
            EnsureFolder(OrbisPaths.LocalizationDir + "/Tables");

            var addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
            addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(addressables);

            var settings = EnsureSettings();
            var locale = EnsureLocale(LocaleCode);
            EnsureStartupSelector(settings, locale);

            var collection = LocalizationEditorSettings.GetStringTableCollection(source.table)
                ?? LocalizationEditorSettings.CreateStringTableCollection(source.table, OrbisPaths.LocalizationDir + "/Tables");
            var table = collection.GetTable(locale.Identifier) as StringTable
                ?? collection.AddNewTable(locale.Identifier) as StringTable;
            if (table == null)
                throw new InvalidOperationException("Impossibile creare la tabella italiana");

            var keys = new HashSet<string>();
            foreach (var e in source.entries)
            {
                keys.Add(e.key);
                var entry = table.GetEntry(e.key) ?? table.AddEntry(e.key, e.it);
                entry.Value = e.it;
            }
            foreach (var shared in collection.SharedData.Entries.ToList())
            {
                if (!keys.Contains(shared.Key))
                    collection.RemoveEntry(shared.Key);
            }

            LocalizationEditorSettings.SetPreloadTableFlag(table, true);
            EditorUtility.SetDirty(table);
            EditorUtility.SetDirty(collection.SharedData);
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static LocalizationSettings EnsureSettings()
        {
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings != null)
                return settings;
            var path = OrbisPaths.LocalizationDir + "/LocalizationSettings.asset";
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "LocalizationSettings";
                AssetDatabase.CreateAsset(settings, path);
            }
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            return settings;
        }

        private static Locale EnsureLocale(string code)
        {
            var locale = LocalizationEditorSettings.GetLocale(code);
            if (locale != null)
                return locale;
            locale = Locale.CreateLocale(new LocaleIdentifier(code));
            locale.name = $"Locale ({code})";
            AssetDatabase.CreateAsset(locale, $"{OrbisPaths.LocalizationDir}/Locales/Locale_{code}.asset");
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        private static void EnsureStartupSelector(LocalizationSettings settings, Locale locale)
        {
            // con una sola lingua si parte sempre in italiano, qualunque sia la lingua del dispositivo
            var selectors = settings.GetStartupLocaleSelectors();
            if (!selectors.OfType<SpecificLocaleSelector>().Any())
                selectors.Insert(0, new SpecificLocaleSelector { LocaleId = locale.Identifier });
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

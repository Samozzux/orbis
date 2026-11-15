using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

namespace Orbis.UI.Localization
{
    /// <summary>
    /// Accesso ai testi della tabella Localization "UI" (sorgente: data/localization/ui.json).
    /// Nei file UXML i testi si scrivono come chiavi precedute da "#", es. text="#menu.new_game".
    /// </summary>
    public static class Loc
    {
        public const string TableName = "UI";
        private const string KeyPrefix = "#";

        private static StringTable _table;
        private static readonly HashSet<string> MissingReported = new HashSet<string>();

        public static bool IsReady => _table != null;

        /// <summary>Inizializza Localization e carica la tabella. Asincrono: funziona anche sul web.</summary>
        public static IEnumerator Initialize()
        {
            if (_table != null)
                yield break;

            var init = LocalizationSettings.InitializationOperation;
            yield return init;
            if (init.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError("[Orbis] Inizializzazione di Localization fallita.");
                yield break;
            }

            var op = LocalizationSettings.StringDatabase.GetTableAsync(TableName);
            yield return op;
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                _table = op.Result;
            else
                Debug.LogError($"[Orbis] Tabella di testi '{TableName}' non trovata.");
        }

        public static string Get(string key)
        {
            var entry = _table != null ? _table.GetEntry(key) : null;
            if (entry != null)
                return entry.GetLocalizedString();
            if (MissingReported.Add(key))
                Debug.LogWarning($"[Orbis] Testo mancante nella tabella '{TableName}': {key}");
            return key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        /// <summary>Sostituisce i testi "#chiave" di tutti gli elementi testuali sotto <paramref name="root"/>.</summary>
        public static void LocalizeTree(VisualElement root)
        {
            root.Query<TextElement>().ForEach(t =>
            {
                if (t.text != null && t.text.StartsWith(KeyPrefix))
                    t.text = Get(t.text.Substring(KeyPrefix.Length));
            });
        }
    }
}

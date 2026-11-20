using System.IO;
using UnityEditor;
using UnityEngine;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Copia i dati del gioco da /data (catalogo, bilanciamento, missioni, popolazione, eventi) in Resources, così finiscono in
    /// tutte le build (docs/01 §3: "dati prima di tutto"; i content pack scaricabili arrivano più avanti).
    /// </summary>
    public static class ContentGenerator
    {
        public const string Folder = "Assets/Orbis/Content/Resources/OrbisContent";

        public static void Generate()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Directory.CreateDirectory(Path.Combine(root, Folder));
            bool changed = false;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "data", "catalog"), "*.json"))
                changed |= Copy(file, Path.Combine(root, Folder, "catalog_" + Path.GetFileName(file)));
            changed |= Copy(Path.Combine(root, "data", "balance", "economy.json"), Path.Combine(root, Folder, "economy.json"));
            changed |= Copy(Path.Combine(root, "data", "scenarios", "missions.json"), Path.Combine(root, Folder, "missions.json"));
            // popolazione reale (M4): dati ISTAT/MEF per comune, nomi, parametri del generatore, calendario degli eventi
            foreach (var file in Directory.GetFiles(Path.Combine(root, "data", "population"), "*.json"))
                changed |= Copy(file, Path.Combine(root, Folder, "population_" + Path.GetFileName(file)));
            changed |= Copy(Path.Combine(root, "data", "balance", "population.json"), Path.Combine(root, Folder, "population_balance.json"));
            foreach (var file in Directory.GetFiles(Path.Combine(root, "data", "events"), "*.json"))
                changed |= Copy(file, Path.Combine(root, Folder, "events_" + Path.GetFileName(file)));
            if (changed) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool Copy(string from, string to)
        {
            var data = File.ReadAllText(from);
            if (File.Exists(to) && File.ReadAllText(to) == data) return false;
            File.WriteAllText(to, data);
            return true;
        }
    }
}

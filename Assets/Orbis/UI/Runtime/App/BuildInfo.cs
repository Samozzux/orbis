using System;
using UnityEngine;

namespace Orbis.UI.App
{
    /// <summary>Dati della build scritti dal BuildScript in Resources/OrbisBuildInfo.json (assenti in editor).</summary>
    [Serializable]
    public sealed class BuildInfo
    {
        public const string ResourceName = "OrbisBuildInfo";

        public string version = "0.0.0";
        public int build;
        public string commit = string.Empty;
        public string date = string.Empty;

        public static BuildInfo Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset != null)
            {
                try
                {
                    var info = JsonUtility.FromJson<BuildInfo>(asset.text);
                    if (info != null) return info;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Orbis] {ResourceName} non leggibile: {e.Message}");
                }
            }
            return new BuildInfo { version = Application.version };
        }

        public string ShortCommit => string.IsNullOrEmpty(commit) ? "dev" : commit.Length > 7 ? commit.Substring(0, 7) : commit;
    }
}

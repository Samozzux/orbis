using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Orbis.World
{
    /// <summary>
    /// Da dove arrivano i file del region pack: StreamingAssets/world/&lt;regione&gt;/ (dentro l'app su iOS e Android,
    /// sul sito per il web: stessa origine, niente CORS). Sempre con UnityWebRequest, che funziona ovunque.
    /// </summary>
    public sealed class TileSource
    {
        public readonly string BaseUrl;

        private TileSource(string url) => BaseUrl = url.EndsWith("/") ? url : url + "/";

        /// <summary>Pack incluso nella build (StreamingAssets).</summary>
        public static TileSource ForRegion(string region) =>
            new TileSource(Combine(Application.streamingAssetsPath, "world/" + region + "/"));

        /// <summary>Cartella o URL qualsiasi (test, pack scaricati).</summary>
        public static TileSource FromUrl(string url) => new TileSource(url);

        public static TileSource FromFolder(string folder) => new TileSource("file://" + Path.GetFullPath(folder).Replace('\\', '/'));

        private static string Combine(string root, string rel)
        {
            if (root.Contains("://")) return root.TrimEnd('/') + "/" + rel; // web, Android (jar:file://)
            return "file://" + Path.Combine(root, rel).Replace('\\', '/');
        }

        /// <summary>Scarica il file; restituisce null se manca (404 o file assente).</summary>
        public IEnumerator Fetch(string relativePath, Action<byte[]> done)
        {
            using (var req = UnityWebRequest.Get(BaseUrl + relativePath))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    done(null);
                    yield break;
                }
                done(req.downloadHandler.data);
            }
        }
    }
}

using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Orbis.Gameplay
{
    /// <summary>
    /// Segnala nella console i lavori del thread principale che superano 100 ms (sul web bloccano tutto: niente thread).
    /// </summary>
    public static class SlowWork
    {
        public const long ThresholdMs = 100;
        /// <summary>Lavoro massimo per fotogramma dei compiti divisi a pezzi (costruzione della rete, tempi tra le zone).</summary>
        public const long FrameBudgetMs = 8;

        public static void Log(string what, Stopwatch sw)
        {
            if (sw.ElapsedMilliseconds >= ThresholdMs) Debug.Log($"[Orbis] lento: {what} {sw.ElapsedMilliseconds} ms");
        }
    }
}

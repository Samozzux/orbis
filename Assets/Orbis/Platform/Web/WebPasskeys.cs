using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Orbis.Platform
{
    /// <summary>
    /// Passkey nel browser (WebAuthn) tramite OrbisWeb.jslib. Safari accetta la richiesta solo dentro un gesto
    /// del giocatore: <see cref="Arm"/> si chiama quando il dito (o il mouse) preme il pulsante, e la richiesta
    /// parte al rilascio; se il rilascio è arrivato prima, <see cref="RunNow"/> la fa partire subito.
    /// La risposta è il JSON {ok, credential} oppure {ok: false, error} con il nome dell'errore del browser.
    /// </summary>
    public static class WebPasskeys
    {
        private delegate void Callback(int requestId, IntPtr json);

        private static readonly Dictionary<int, TaskCompletionSource<string>> Pending = new Dictionary<int, TaskCompletionSource<string>>();
        private static int _next;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int OrbisWeb_PasskeySupported();
        [DllImport("__Internal")] private static extern void OrbisWeb_PasskeyArm(int create, string options, int requestId, Callback callback);
        [DllImport("__Internal")] private static extern void OrbisWeb_PasskeyRunNow(int requestId);
        [DllImport("__Internal")] private static extern void OrbisWeb_Free(IntPtr ptr);
#else
        private static int OrbisWeb_PasskeySupported() => 0;
        private static void OrbisWeb_PasskeyArm(int create, string options, int requestId, Callback callback) => callback(requestId, IntPtr.Zero);
        private static void OrbisWeb_PasskeyRunNow(int requestId) { }
        private static void OrbisWeb_Free(IntPtr ptr) { }
#endif

        /// <summary>Il browser sa usare le passkey (solo nella versione web).</summary>
        public static bool Supported => OrbisWeb_PasskeySupported() != 0;

        /// <summary>Prepara la richiesta (create = crea la passkey, altrimenti entra) con le opzioni del server.</summary>
        public static int Arm(bool create, string optionsJson, out Task<string> result)
        {
            var tcs = new TaskCompletionSource<string>();
            int id;
            lock (Pending)
            {
                id = ++_next;
                Pending[id] = tcs;
            }
            result = tcs.Task;
            OrbisWeb_PasskeyArm(create ? 1 : 0, optionsJson, id, OnResult);
            return id;
        }

        public static void RunNow(int requestId) => OrbisWeb_PasskeyRunNow(requestId);

        [AOT.MonoPInvokeCallback(typeof(Callback))]
        private static void OnResult(int requestId, IntPtr json)
        {
            TaskCompletionSource<string> tcs;
            lock (Pending)
            {
                if (!Pending.TryGetValue(requestId, out tcs)) return;
                Pending.Remove(requestId);
            }
            string text = null;
            if (json != IntPtr.Zero)
            {
                text = Marshal.PtrToStringUTF8(json);
                OrbisWeb_Free(json);
            }
            tcs.TrySetResult(text ?? "{\"ok\":false,\"error\":\"Unavailable\"}");
        }
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>
    /// Android Keystore e sblocco biometrico (plugin Kotlin <c>Assets/Plugins/Android/OrbisSecurity.kt</c>).
    /// I valori normali sono cifrati con una chiave AES del Keystore; quelli protetti con una chiave RSA la cui
    /// parte privata si usa solo dopo l'impronta o il volto (BiometricPrompt).
    /// </summary>
    internal static class AndroidSecurity
    {
        private const string PluginClass = "it.orbis.platform.OrbisSecurity";

        private sealed class Callback : AndroidJavaProxy
        {
            private readonly TaskCompletionSource<string> _tcs;

            public Callback(TaskCompletionSource<string> tcs) : base("it.orbis.platform.OrbisSecurityCallback") => _tcs = tcs;

            // chiamato da Java (thread dell'interfaccia): il seguito dell'await torna sul thread di Unity
            public void onResult(int requestId, string value)
            {
                lock (Alive) Alive.Remove(requestId);
                _tcs.TrySetResult(value);
            }
        }

        // i proxy restano vivi finché Java non risponde
        private static readonly Dictionary<int, Callback> Alive = new Dictionary<int, Callback>();
        private static int _next;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject Activity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private static T Call<T>(string method, params object[] args)
        {
            using (var plugin = new AndroidJavaClass(PluginClass))
            using (var activity = Activity())
            {
                var all = new object[args.Length + 1];
                all[0] = activity;
                args.CopyTo(all, 1);
                return plugin.CallStatic<T>(method, all);
            }
        }

        private static void Call(string method, params object[] args)
        {
            using (var plugin = new AndroidJavaClass(PluginClass))
            using (var activity = Activity())
            {
                var all = new object[args.Length + 1];
                all[0] = activity;
                args.CopyTo(all, 1);
                plugin.CallStatic(method, all);
            }
        }

        public const bool Native = true;
#else
        private static T Call<T>(string method, params object[] args) => default;
        private static void Call(string method, params object[] args) { }
        public const bool Native = false;
#endif

        public static bool BiometryAvailable
        {
            get
            {
                try
                {
                    return Call<bool>("biometryAvailable");
                }
                catch (AndroidJavaException e)
                {
                    Debug.LogWarning("[Orbis] biometria non disponibile: " + e.Message);
                    return false;
                }
            }
        }

        public static bool Set(string key, string value, bool biometric)
        {
            try
            {
                return Call<bool>("set", key, value ?? "", biometric);
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning("[Orbis] archivio protetto: " + e.Message);
                return false;
            }
        }

        public static void Delete(string key, bool biometric)
        {
            try
            {
                Call("delete", key, biometric);
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning("[Orbis] archivio protetto: " + e.Message);
            }
        }

        public static Task<string> GetAsync(string key, string reason, string cancel, bool biometric)
        {
            var tcs = new TaskCompletionSource<string>();
            if (!Native)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }
            int id;
            Callback cb;
            lock (Alive)
            {
                id = ++_next;
                cb = new Callback(tcs);
                Alive[id] = cb;
            }
            try
            {
                Call("get", key, reason ?? "", cancel ?? "", biometric, id, cb);
            }
            catch (AndroidJavaException e)
            {
                lock (Alive) Alive.Remove(id);
                Debug.LogWarning("[Orbis] archivio protetto: " + e.Message);
                tcs.TrySetResult(null);
            }
            return tcs.Task;
        }
    }

    /// <summary>Archivio protetto senza biometria (chiave AES del Keystore).</summary>
    public sealed class AndroidKeystoreStore : ISecureStore
    {
        public bool IsPersistent => AndroidSecurity.Native;
        public Task SetAsync(string key, string value) => Task.FromResult(AndroidSecurity.Set(key, value, false));
        public Task<string> GetAsync(string key) => AndroidSecurity.GetAsync(key, null, null, false);

        public Task DeleteAsync(string key)
        {
            AndroidSecurity.Delete(key, false);
            return Task.CompletedTask;
        }
    }

    /// <summary>Impronta o volto con BiometricPrompt e la chiave RSA del Keystore (docs/06 §4).</summary>
    public sealed class AndroidBiometricAuth : IBiometricAuth
    {
        /// <summary>Testo del pulsante per annullare (lo imposta l'interfaccia dalle tabelle dei testi; non può essere vuoto).</summary>
        public string CancelText = "×";

        public BiometricKind Kind => BiometricKind.AndroidBiometric;
        public bool IsAvailable => AndroidSecurity.BiometryAvailable;
        public Task<bool> StoreProtectedAsync(string key, string secret) => Task.FromResult(AndroidSecurity.Set(key, secret, true));
        public Task<string> ReadProtectedAsync(string key, string reason) => AndroidSecurity.GetAsync(key, reason, CancelText, true);

        public Task DeleteProtectedAsync(string key)
        {
            AndroidSecurity.Delete(key, true);
            return Task.CompletedTask;
        }
    }
}

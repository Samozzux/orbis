using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Orbis.Platform
{
    /// <summary>
    /// Keychain e Face ID su iOS (plugin Swift <c>Assets/Plugins/iOS/OrbisSecurity.swift</c>). Gli elementi
    /// normali sono accessibili dopo il primo sblocco del dispositivo; quelli protetti usano
    /// <c>.biometryCurrentSet</c>: leggerli mostra Face ID, e se cambiano i volti registrati non si leggono più.
    /// </summary>
    internal static class IosSecurity
    {
        private delegate void ResultCallback(int requestId, IntPtr value);

        private static readonly Dictionary<int, TaskCompletionSource<string>> Pending = new Dictionary<int, TaskCompletionSource<string>>();
        private static int _next;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int OrbisSecurity_BiometryAvailable();
        [DllImport("__Internal")] private static extern int OrbisSecurity_Set(string key, string value, int biometric);
        [DllImport("__Internal")] private static extern void OrbisSecurity_Get(string key, string reason, int biometric, int requestId, ResultCallback callback);
        [DllImport("__Internal")] private static extern void OrbisSecurity_Delete(string key, int biometric);
        public const bool Native = true;
#else
        private static int OrbisSecurity_BiometryAvailable() => 0;
        private static int OrbisSecurity_Set(string key, string value, int biometric) => 0;
        private static void OrbisSecurity_Get(string key, string reason, int biometric, int requestId, ResultCallback callback) => callback(requestId, IntPtr.Zero);
        private static void OrbisSecurity_Delete(string key, int biometric) { }
        public const bool Native = false;
#endif

        public static bool BiometryAvailable => OrbisSecurity_BiometryAvailable() != 0;

        public static bool Set(string key, string value, bool biometric) => OrbisSecurity_Set(key, value, biometric ? 1 : 0) != 0;

        public static void Delete(string key, bool biometric) => OrbisSecurity_Delete(key, biometric ? 1 : 0);

        public static Task<string> GetAsync(string key, string reason, bool biometric)
        {
            var tcs = new TaskCompletionSource<string>();
            int id;
            lock (Pending)
            {
                id = ++_next;
                Pending[id] = tcs;
            }
            OrbisSecurity_Get(key, reason ?? "", biometric ? 1 : 0, id, OnResult);
            return tcs.Task;
        }

        // richiamata dal plugin sul thread principale (DispatchQueue.main)
        [AOT.MonoPInvokeCallback(typeof(ResultCallback))]
        private static void OnResult(int requestId, IntPtr value)
        {
            TaskCompletionSource<string> tcs;
            lock (Pending)
            {
                if (!Pending.TryGetValue(requestId, out tcs)) return;
                Pending.Remove(requestId);
            }
            tcs.TrySetResult(value == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(value));
        }
    }

    /// <summary>Keychain senza biometria (refresh token quando Face ID non è attivo, impostazioni riservate).</summary>
    public sealed class IosKeychainStore : ISecureStore
    {
        public bool IsPersistent => IosSecurity.Native;
        public Task SetAsync(string key, string value) => Task.FromResult(IosSecurity.Set(key, value, false));
        public Task<string> GetAsync(string key) => IosSecurity.GetAsync(key, null, false);

        public Task DeleteAsync(string key)
        {
            IosSecurity.Delete(key, false);
            return Task.CompletedTask;
        }
    }

    /// <summary>Face ID / Touch ID con il Keychain protetto (docs/06 §4).</summary>
    public sealed class IosBiometricAuth : IBiometricAuth
    {
        public BiometricKind Kind => BiometricKind.FaceId;
        public bool IsAvailable => IosSecurity.BiometryAvailable;
        public Task<bool> StoreProtectedAsync(string key, string secret) => Task.FromResult(IosSecurity.Set(key, secret, true));
        public Task<string> ReadProtectedAsync(string key, string reason) => IosSecurity.GetAsync(key, reason, true);

        public Task DeleteProtectedAsync(string key)
        {
            IosSecurity.Delete(key, true);
            return Task.CompletedTask;
        }
    }
}

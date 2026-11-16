namespace Orbis.Platform
{
    /// <summary>Punto di accesso ai servizi della piattaforma corrente.</summary>
    public static class PlatformServices
    {
        private static IPlatformServices _current;

        public static IPlatformServices Current => _current ?? (_current = Create());

        /// <summary>Per i test: sostituisce i servizi (null ripristina quelli reali).</summary>
        public static void Override(IPlatformServices services) => _current = services;

        private static IPlatformServices Create()
        {
#if UNITY_EDITOR
            return new EditorPlatformServices();
#elif UNITY_IOS
            return new IosPlatformServices();
#elif UNITY_ANDROID
            return new AndroidPlatformServices();
#elif UNITY_WEBGL
            return new WebPlatformServices();
#else
            return new EditorPlatformServices(); // desktop: stessi ripieghi dell'editor
#endif
        }
    }
}

namespace Orbis.Core.Device
{
    public enum PlatformKind
    {
        Editor,
        Ios,
        Android,
        Web,
        Desktop,
    }

    /// <summary>Caratteristiche del dispositivo usate per scegliere qualità e budget (docs/08 §4).</summary>
    public sealed class DeviceInfo
    {
        public PlatformKind Platform { get; set; }
        public string Model { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public int SystemMemoryMb { get; set; }
        public int GraphicsMemoryMb { get; set; }
        public string GraphicsApi { get; set; } = string.Empty;
        public string GraphicsDevice { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }

        /// <summary>Pixel per punto logico (es. 3 su iPhone 15 Pro, devicePixelRatio sul web).</summary>
        public float PixelsPerPoint { get; set; } = 1f;

        /// <summary>Vero su dispositivi touch (telefono, tablet, browser mobile).</summary>
        public bool IsTouchPrimary { get; set; }
    }
}

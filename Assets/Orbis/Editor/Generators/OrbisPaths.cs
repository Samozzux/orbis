namespace Orbis.EditorTools
{
    /// <summary>Percorsi degli asset generati da codice.</summary>
    public static class OrbisPaths
    {
        public const string BundleId = "com.samuelecozza.orbis";
        public const string ProductName = "Orbis";

        public const string BootScene = "Assets/Orbis/Scenes/Boot.unity";
        public const string PanelSettings = "Assets/Orbis/UI/Settings/OrbisPanelSettings.asset";
        public const string Theme = "Assets/Orbis/UI/Settings/OrbisTheme.tss";
        public const string AppUxml = "Assets/Orbis/UI/Uxml/App.uxml";
        public const string LocalizationDir = "Assets/Orbis/Localization";
        public const string BrandingDir = "Assets/Orbis/Branding";
        public const string AppIcon = "Assets/Orbis/Branding/AppIcon.png";
        public const string WebTemplateData = "Assets/WebGLTemplates/Orbis/TemplateData";
        public const string BuildInfoJson = "Assets/Orbis/UI/Resources/OrbisBuildInfo.json";

        /// <summary>Sorgente dei testi, relativa alla radice del repo (= progetto Unity).</summary>
        public const string LocalizationSource = "data/localization/ui.json";
    }
}

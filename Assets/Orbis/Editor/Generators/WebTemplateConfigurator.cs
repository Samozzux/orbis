using UnityEditor;

namespace Orbis.EditorTools
{
    /// <summary>
    /// Passa alla pagina web (Assets/WebGLTemplates/Orbis/index.html) i testi della tabella
    /// Localization, così anche la pagina di caricamento non ha testi scritti a mano.
    /// </summary>
    public static class WebTemplateConfigurator
    {
        public static void Apply()
        {
            Set("ORBIS_LOADING", "web.loading");
            Set("ORBIS_UNSUPPORTED", "web.unsupported");
            Set("ORBIS_ERROR", "web.error");
            Set("ORBIS_DESCRIPTION", "web.description");
        }

        private static void Set(string variable, string key) =>
            PlayerSettings.SetTemplateCustomValue(variable, LocalizationGenerator.SourceText(key));
    }
}

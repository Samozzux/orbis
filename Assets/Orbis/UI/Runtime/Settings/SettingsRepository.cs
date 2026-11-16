using Orbis.Core.Settings;
using UnityEngine;

namespace Orbis.UI.Settings
{
    /// <summary>
    /// Impostazioni del dispositivo (non della partita): restano su questo telefono/tablet/browser.
    /// Usa PlayerPrefs (sul web finisce in IndexedDB).
    /// </summary>
    public static class SettingsRepository
    {
        public const string DisplayKey = "orbis.settings.display";

        public static DisplaySettings LoadDisplay() => DisplaySettings.Deserialize(PlayerPrefs.GetString(DisplayKey, string.Empty));

        public static void SaveDisplay(DisplaySettings settings)
        {
            PlayerPrefs.SetString(DisplayKey, settings.Serialize());
            PlayerPrefs.Save();
        }
    }
}

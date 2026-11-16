using System;

namespace Orbis.Core.Settings
{
    /// <summary>Orientamento scelto quando la rotazione automatica è spenta.</summary>
    public enum FixedOrientation
    {
        Portrait,
        Landscape,
    }

    /// <summary>Orientamenti che lo schermo può assumere.</summary>
    public readonly struct AllowedOrientations : IEquatable<AllowedOrientations>
    {
        public AllowedOrientations(bool portrait, bool landscape)
        {
            Portrait = portrait;
            Landscape = landscape;
        }

        public bool Portrait { get; }
        public bool Landscape { get; }

        public bool Equals(AllowedOrientations other) => Portrait == other.Portrait && Landscape == other.Landscape;
        public override bool Equals(object obj) => obj is AllowedOrientations o && Equals(o);
        public override int GetHashCode() => (Portrait ? 1 : 0) | (Landscape ? 2 : 0);
        public override string ToString() => $"verticale={Portrait}, orizzontale={Landscape}";
    }

    /// <summary>
    /// Impostazioni dello schermo su telefono e tablet (decisione di Samuele, 16/11/2026):
    /// di default rotazione automatica attiva e avvio in verticale; spegnendo la rotazione si sceglie
    /// se giocare sempre in verticale o sempre in orizzontale.
    /// </summary>
    public sealed class DisplaySettings
    {
        public const bool DefaultAutoRotate = true;
        public const FixedOrientation DefaultFixed = FixedOrientation.Portrait;

        public bool AutoRotate { get; private set; } = DefaultAutoRotate;
        public FixedOrientation Fixed { get; private set; } = DefaultFixed;

        /// <summary>Orientamento con cui il gioco parte (verticale, come richiesto).</summary>
        public FixedOrientation StartOrientation => AutoRotate ? FixedOrientation.Portrait : Fixed;

        public AllowedOrientations Allowed =>
            AutoRotate
                ? new AllowedOrientations(portrait: true, landscape: true)
                : new AllowedOrientations(portrait: Fixed == FixedOrientation.Portrait, landscape: Fixed == FixedOrientation.Landscape);

        /// <summary>
        /// Accende o spegne la rotazione. Spegnendola si blocca l'orientamento che si sta usando
        /// in quel momento (<paramref name="current"/>), così lo schermo non gira all'improvviso.
        /// </summary>
        public void SetAutoRotate(bool enabled, FixedOrientation current)
        {
            if (!enabled && AutoRotate)
                Fixed = current;
            AutoRotate = enabled;
        }

        /// <summary>Sceglie l'orientamento bloccato (ha effetto solo con la rotazione spenta).</summary>
        public void SetFixed(FixedOrientation orientation) => Fixed = orientation;

        // --- salvataggio come testo semplice (es. PlayerPrefs) ---

        public string Serialize() => $"{(AutoRotate ? "auto" : "fixed")}:{(Fixed == FixedOrientation.Portrait ? "portrait" : "landscape")}";

        /// <summary>Legge il testo salvato; se è vuoto o non valido restituisce i valori predefiniti.</summary>
        public static DisplaySettings Deserialize(string text)
        {
            var s = new DisplaySettings();
            if (string.IsNullOrEmpty(text))
                return s;
            var parts = text.Split(':');
            if (parts.Length != 2)
                return s;
            if (parts[1] == "landscape") s.Fixed = FixedOrientation.Landscape;
            else if (parts[1] == "portrait") s.Fixed = FixedOrientation.Portrait;
            if (parts[0] == "fixed") s.AutoRotate = false;
            else if (parts[0] == "auto") s.AutoRotate = true;
            return s;
        }
    }
}

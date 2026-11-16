using Orbis.Core.Settings;
using UnityEngine;

namespace Orbis.Platform
{
    /// <summary>App iOS e Android: rotazione automatica o blocco in verticale/orizzontale.</summary>
    public sealed class MobileScreenOrientation : IScreenOrientation
    {
        public bool CanControl => true;

        public FixedOrientation Current => CurrentFromScreen();

        public void Apply(DisplaySettings settings)
        {
            var allowed = settings.Allowed;
            Screen.autorotateToPortrait = allowed.Portrait;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = allowed.Landscape;
            Screen.autorotateToLandscapeRight = allowed.Landscape;
            if (settings.AutoRotate)
            {
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
            else if (settings.Fixed == FixedOrientation.Portrait)
            {
                Screen.orientation = ScreenOrientation.Portrait;
            }
            else
            {
                // si gira subito in orizzontale, poi si lascia libero il passaggio tra i due lati orizzontali
                if (Current != FixedOrientation.Landscape)
                    Screen.orientation = ScreenOrientation.LandscapeLeft;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
        }

        internal static FixedOrientation CurrentFromScreen() =>
            Screen.height > Screen.width ? FixedOrientation.Portrait : FixedOrientation.Landscape;
    }

    /// <summary>
    /// Web, desktop ed editor: l'orientamento non si può bloccare (i browser, Safari in particolare,
    /// non lo permettono); il gioco si adatta a come è tenuto il dispositivo.
    /// </summary>
    public sealed class FreeScreenOrientation : IScreenOrientation
    {
        public bool CanControl => false;
        public FixedOrientation Current => MobileScreenOrientation.CurrentFromScreen();
        public void Apply(DisplaySettings settings) { }
    }
}

using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Orbis.UI.App
{
    /// <summary>Mostra una schermata alla volta con dissolvenza (le transizioni sono nell'USS: .screen / .screen--visible).</summary>
    public sealed class ScreenSwitcher
    {
        private const string VisibleClass = "screen--visible";
        private const long HideDelayMs = 450;
        private readonly List<VisualElement> _screens = new List<VisualElement>();

        public VisualElement Current { get; private set; }

        public void Register(VisualElement screen)
        {
            _screens.Add(screen);
            screen.style.display = DisplayStyle.None;
            screen.RemoveFromClassList(VisibleClass);
        }

        public void Show(VisualElement screen)
        {
            if (Current == screen)
                return;
            var previous = Current;
            Current = screen;
            if (previous != null)
            {
                previous.RemoveFromClassList(VisibleClass);
                previous.pickingMode = PickingMode.Ignore;
                previous.schedule.Execute(() =>
                {
                    if (Current != previous) previous.style.display = DisplayStyle.None;
                }).StartingIn(HideDelayMs);
            }
            screen.style.display = DisplayStyle.Flex;
            screen.pickingMode = PickingMode.Position;
            // un frame dopo, così la transizione di opacità parte davvero
            screen.schedule.Execute(() => screen.AddToClassList(VisibleClass)).StartingIn(16);
        }
    }
}

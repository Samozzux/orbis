using Orbis.Core.Settings;
using Orbis.Platform;
using Orbis.UI.App;
using UnityEngine.UIElements;

namespace Orbis.UI.Settings
{
    /// <summary>
    /// Pannello Impostazioni. Per ora contiene la sezione "Schermo": rotazione automatica (predefinita)
    /// oppure blocco in verticale/orizzontale. Nelle app iOS/Android l'effetto è immediato; nel browser
    /// l'orientamento segue il dispositivo e si mostra una nota.
    /// </summary>
    public sealed class SettingsController
    {
        private const string VisibleClass = "sheet-layer--visible";
        private const long HideDelayMs = 320;

        private readonly VisualElement _layer;
        private readonly IScreenOrientation _orientation;
        private readonly VisualElement _switch;
        private readonly VisualElement _orientationRow;
        private readonly Button _portrait;
        private readonly Button _landscape;
        private readonly Button _close;
        private DisplaySettings _display;
        private bool _open;

        public SettingsController(VisualElement root, IScreenOrientation orientation, DisplaySettings display)
        {
            _orientation = orientation;
            _display = display;
            _layer = MainMenuController.Require<VisualElement>(root, "settings");
            _switch = MainMenuController.Require<VisualElement>(root, "autorotate-switch");
            _orientationRow = MainMenuController.Require<VisualElement>(root, "orientation-row");
            _portrait = MainMenuController.Require<Button>(root, "orientation-portrait");
            _landscape = MainMenuController.Require<Button>(root, "orientation-landscape");
            _close = MainMenuController.Require<Button>(root, "settings-close");

            _close.clicked += Close;
            MainMenuController.Require<VisualElement>(root, "settings-scrim").RegisterCallback<PointerDownEvent>(_ => Close());
            MainMenuController.Require<Button>(root, "toggle-autorotate").clicked += ToggleAutoRotate;
            _portrait.clicked += () => ChooseFixed(FixedOrientation.Portrait);
            _landscape.clicked += () => ChooseFixed(FixedOrientation.Landscape);
            // Esc, "indietro" di Android e tasto B del gamepad chiudono il pannello
            _layer.RegisterCallback<NavigationCancelEvent>(_ => Close());

            var canControl = orientation.CanControl;
            MainMenuController.Require<VisualElement>(root, "orientation-controls").style.display = canControl ? DisplayStyle.Flex : DisplayStyle.None;
            MainMenuController.Require<VisualElement>(root, "orientation-note").style.display = canControl ? DisplayStyle.None : DisplayStyle.Flex;

            _layer.style.display = DisplayStyle.None;
            Refresh();
        }

        public bool IsOpen => _open;
        public DisplaySettings Display => _display;

        public void Open()
        {
            _open = true;
            _layer.style.display = DisplayStyle.Flex;
            _layer.schedule.Execute(() =>
            {
                if (_open) _layer.AddToClassList(VisibleClass);
            }).StartingIn(16);
            _close.Focus();
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            _layer.RemoveFromClassList(VisibleClass);
            _layer.schedule.Execute(() =>
            {
                if (!_open) _layer.style.display = DisplayStyle.None;
            }).StartingIn(HideDelayMs);
        }

        private void ToggleAutoRotate()
        {
            _display.SetAutoRotate(!_display.AutoRotate, _orientation.Current);
            Commit();
        }

        private void ChooseFixed(FixedOrientation orientation)
        {
            if (_display.AutoRotate)
                return;
            _display.SetFixed(orientation);
            Commit();
        }

        private void Commit()
        {
            _orientation.Apply(_display);
            SettingsRepository.SaveDisplay(_display);
            Refresh();
        }

        private void Refresh()
        {
            _switch.EnableInClassList("switch--on", _display.AutoRotate);
            _orientationRow.SetEnabled(!_display.AutoRotate);
            _portrait.EnableInClassList("segmented__item--selected", !_display.AutoRotate && _display.Fixed == FixedOrientation.Portrait);
            _landscape.EnableInClassList("segmented__item--selected", !_display.AutoRotate && _display.Fixed == FixedOrientation.Landscape);
        }
    }
}

using System;
using System.Collections;
using System.Threading.Tasks;
using Orbis.Platform;
using Orbis.UI.Localization;
using Orbis.UI.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.App
{
    /// <summary>
    /// Menu principale (docs/04 §1): Continua, Nuova partita e navbar con Account, Impostazioni, Crediti.
    /// Nuova partita, Continua e Account aprono i loro pannelli, Impostazioni il suo; Crediti mostra "in arrivo".
    /// Gli amici sono spostati in M9: per ora al loro posto c'è l'account (M3).
    /// </summary>
    public sealed class MainMenuController
    {
        private const long ToastMs = 2600;

        private readonly VisualElement _root;
        private readonly IPlatformServices _services;
        private readonly Button _continue;
        private readonly Button _newGame;
        private readonly VisualElement _toast;
        private readonly Label _toastText;
        private IVisualElementScheduledItem _toastHide;
        private readonly Label _accountLabel;

        /// <summary>Il giocatore ha toccato Account nella barra.</summary>
        public event Action Account;

        /// <summary>Nome mostrato sotto l'icona dell'account (vuoto = "Account").</summary>
        public void SetAccountName(string name) => _accountLabel.text = string.IsNullOrWhiteSpace(name) ? Loc.Get("menu.account") : name;

        /// <param name="menu">schermata del menu</param>
        /// <param name="toast">avviso a capsula (sta fuori dal menu, sopra tutte le schermate)</param>
        public MainMenuController(VisualElement menu, VisualElement toast, IPlatformServices services, SettingsController settings)
        {
            _root = menu;
            _services = services;
            _continue = Require<Button>(menu, "btn-continue");
            _newGame = Require<Button>(menu, "btn-new-game");
            _toast = toast ?? throw new ArgumentNullException(nameof(toast));
            _toastText = Require<Label>(toast, "toast-text");

            _newGame.clicked += () =>
            {
                services.Haptics.Play(HapticKind.Selection);
                NewGame?.Invoke();
            };
            _continue.clicked += () =>
            {
                services.Haptics.Play(HapticKind.Selection);
                Continue?.Invoke();
            };
            Require<Button>(menu, "nav-credits").clicked += ComingSoon;
            Require<Button>(menu, "nav-account").clicked += () =>
            {
                services.Haptics.Play(HapticKind.Selection);
                Account?.Invoke();
            };
            _accountLabel = Require<Label>(menu, "nav-account-label");
            Require<Button>(menu, "nav-settings").clicked += settings.Open;

            var build = BuildInfo.Load();
            var device = services.DeviceProfile.Info;
            Require<Label>(menu, "version").text =
                $"{Loc.Format("menu.version", build.version, build.build)} · {device.Platform} · {device.GraphicsApi}";
        }

        /// <summary>Controlla se esistono partite salvate per abilitare "Continua".</summary>
        public IEnumerator Prepare()
        {
            Task<System.Collections.Generic.IReadOnlyList<string>> list = _services.SaveStore.ListAsync("saves/");
            while (!list.IsCompleted)
                yield return null;
            var hasSaves = list.Status == TaskStatus.RanToCompletion && list.Result.Count > 0;
            _continue.SetEnabled(hasSaves);
            Require<Label>(_root, "continue-detail").text = Loc.Get(hasSaves ? "menu.continue.detail" : "menu.continue.empty");
        }

        public void OnShown()
        {
            // con mouse e tastiera il fuoco va subito sull'azione principale (Invio per confermare)
            if (!_services.DeviceProfile.Info.IsTouchPrimary)
                _newGame.Focus();
        }

        /// <summary>Elemento obbligatorio: sul web i null non generano eccezioni, quindi si controlla qui.</summary>
        internal static T Require<T>(VisualElement parent, string name) where T : VisualElement =>
            parent.Q<T>(name) ?? throw new InvalidOperationException($"Elemento UI mancante: {typeof(T).Name} '{name}'");

        public bool IsToastVisible => _toast.ClassListContains("toast--visible");

        /// <summary>"Nuova partita" premuto.</summary>
        public event Action NewGame;

        /// <summary>"Continua" premuto (partite salvate).</summary>
        public event Action Continue;

        private void ComingSoon()
        {
            _services.Haptics.Play(HapticKind.Selection);
            ShowToast(Loc.Get("menu.coming_soon"));
        }

        /// <summary>Avviso a capsula sopra tutte le schermate.</summary>
        public void ShowToast(string text)
        {
            _toastText.text = text;
            _toast.AddToClassList("toast--visible");
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.RemoveFromClassList("toast--visible")).StartingIn(ToastMs);
        }
    }
}

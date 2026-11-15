using System.Collections;
using System.Threading.Tasks;
using Orbis.Platform;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.App
{
    /// <summary>
    /// Menu principale (docs/04 §1): Continua, Nuova partita e navbar con Amici, Impostazioni, Crediti.
    /// In M0 le voci non aprono ancora nulla: mostrano un avviso "in arrivo".
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

        public MainMenuController(VisualElement root, IPlatformServices services)
        {
            _root = root;
            _services = services;
            _continue = root.Q<Button>("btn-continue");
            _newGame = root.Q<Button>("btn-new-game");
            _toast = root.Q("toast");
            _toastText = root.Q<Label>("toast-text");

            _newGame.clicked += ComingSoon;
            _continue.clicked += ComingSoon;
            foreach (var id in new[] { "nav-friends", "nav-settings", "nav-credits" })
                root.Q<Button>(id).clicked += ComingSoon;

            var build = BuildInfo.Load();
            var device = services.DeviceProfile.Info;
            root.Q<Label>("version").text =
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
            _root.Q<Label>("continue-detail").text = Loc.Get(hasSaves ? "menu.continue.detail" : "menu.continue.empty");
        }

        public void OnShown()
        {
            // con mouse e tastiera il fuoco va subito sull'azione principale (Invio per confermare)
            if (!_services.DeviceProfile.Info.IsTouchPrimary)
                _newGame.Focus();
        }

        private void ComingSoon()
        {
            _services.Haptics.Play(HapticKind.Selection);
            _toastText.text = Loc.Get("menu.coming_soon");
            _toast.AddToClassList("toast--visible");
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.RemoveFromClassList("toast--visible"));
            _toastHide.ExecuteLater(ToastMs);
        }
    }
}

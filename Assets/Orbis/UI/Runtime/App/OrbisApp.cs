using System.Collections;
using Orbis.Core.Boot;
using Orbis.Platform;
using Orbis.UI.Layout;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.App
{
    /// <summary>
    /// Punto di ingresso dell'app (scena Boot): logo → caricamento → menu principale.
    /// La scena è generata da codice (Orbis > Genera progetto).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class OrbisApp : MonoBehaviour
    {
        public enum AppState
        {
            Starting,
            Splash,
            Loading,
            MainMenu,
        }

        private const float SplashSeconds = 1.6f;
        private const float MinLoadingSeconds = 1.2f;

        public static OrbisApp Instance { get; private set; }

        public AppState State { get; private set; } = AppState.Starting;

        /// <summary>Menu principale (disponibile quando <see cref="State"/> è MainMenu).</summary>
        public MainMenuController MainMenu => _mainMenu;

        private UIDocument _document;
        private AdaptiveLayout _layout;
        private readonly ScreenSwitcher _screens = new ScreenSwitcher();
        private VisualElement _splash;
        private VisualElement _loading;
        private VisualElement _menu;
        private VisualElement _progressFill;
        private Label _loadingStatus;
        private MainMenuController _mainMenu;

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            _document = GetComponent<UIDocument>();
            // copia delle impostazioni del pannello: la scala cambia a runtime senza toccare l'asset
            _document.panelSettings = Instantiate(_document.panelSettings);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private IEnumerator Start()
        {
            var root = _document.rootVisualElement;
            var services = PlatformServices.Current;
            _layout = new AdaptiveLayout(_document.panelSettings, services.DeviceProfile,
                MainMenuController.Require<VisualElement>(root, "app"), MainMenuController.Require<VisualElement>(root, "safe-area"));
            _layout.Update(force: true);

            _splash = MainMenuController.Require<VisualElement>(root, "splash");
            _loading = MainMenuController.Require<VisualElement>(root, "loading");
            _menu = MainMenuController.Require<VisualElement>(root, "menu");
            _progressFill = MainMenuController.Require<VisualElement>(root, "progress-fill");
            _loadingStatus = MainMenuController.Require<Label>(root, "loading-status");
            _screens.Register(_splash);
            _screens.Register(_loading);
            _screens.Register(_menu);

            var progress = new BootProgress(new[]
            {
                new BootStep("texts", 1f, "loading.texts"),
                new BootStep("platform", 1f, "loading.platform"),
                new BootStep("content", 2f, "loading.content"),
                new BootStep("world", 2f, "loading.world"),
            });

            // 1) testi: servono già al logo
            yield return Loc.Initialize();
            Loc.LocalizeTree(root);
            progress.Complete("texts");

            // 2) logo
            State = AppState.Splash;
            _screens.Show(_splash);
            yield return new WaitForSeconds(SplashSeconds);

            // 3) caricamento
            State = AppState.Loading;
            _screens.Show(_loading);
            var started = Time.realtimeSinceStartup;
            var shown = 0f;
            UpdateLoading(progress, ref shown, immediate: true);

            services.DeviceProfile.Refresh();
            var info = services.DeviceProfile.Info;
            Debug.Log($"[Orbis] Avvio su {info.Platform} · {info.Model} · {info.GraphicsApi} · {info.SystemMemoryMb} MB · {info.PixelsPerPoint:0.##} px/pt");
            progress.Complete("platform");

            // contenuti e mondo arrivano da M1/M2: per ora i passi completano in modo graduale
            foreach (var id in new[] { "content", "world" })
            {
                for (var f = 0.25f; f <= 1f; f += 0.25f)
                {
                    progress.Report(id, f);
                    yield return null;
                }
            }

            while (Time.realtimeSinceStartup - started < MinLoadingSeconds || shown < 0.999f)
            {
                UpdateLoading(progress, ref shown, immediate: false);
                yield return null;
            }
            _loadingStatus.text = Loc.Get("loading.ready");

            // 4) menu principale
            _mainMenu = new MainMenuController(_menu, MainMenuController.Require<VisualElement>(root, "toast"), services);
            yield return _mainMenu.Prepare();
            State = AppState.MainMenu;
            _screens.Show(_menu);
            _mainMenu.OnShown();
            Debug.Log("[Orbis] Menu principale pronto.");
        }

        private void Update()
        {
            _layout?.Update();
        }

        /// <summary>La barra avanza in modo morbido verso il valore reale.</summary>
        private void UpdateLoading(BootProgress progress, ref float shown, bool immediate)
        {
            var target = progress.Overall;
            shown = immediate ? target : Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.4f);
            _progressFill.style.width = Length.Percent(shown * 100f);
            var step = progress.StepAt(shown);
            _loadingStatus.text = Loc.Get(step != null ? step.LabelKey : "loading.ready");
        }
    }
}

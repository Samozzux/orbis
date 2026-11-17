using System;
using System.Collections;
using System.Threading.Tasks;
using Orbis.Core.Boot;
using Orbis.Core.Game;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.Game;
using Orbis.UI.Glass;
using Orbis.UI.Layout;
using Orbis.UI.Localization;
using Orbis.UI.Menu3D;
using Orbis.UI.Settings;
using UnityEngine;
using UnityEngine.Rendering;
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
            /// <summary>Nel mondo (M1: esplorazione di Chieti).</summary>
            Game,
        }

        private const float SplashSeconds = 1.8f;
        private const float MinLoadingSeconds = 1.6f;
        private const int TipCount = 6;

        [Tooltip("Città 3D dietro il menu (generata dalla scena).")]
        [SerializeField] private MenuCity menuCity;
        [Tooltip("Cattura sfocata della scena per il vetro.")]
        [SerializeField] private GlassBlurCapture glassBlur;

        /// <summary>Partita in corso (null nel menu).</summary>
        public WorldSession Session => _session;

        /// <summary>HUD della partita (null nel menu).</summary>
        public GameHud Hud => _hud;

        /// <summary>Economia e costruzione della partita in corso (null nel menu).</summary>
        public GameController Game => _session != null ? _session.Game : null;

        /// <summary>Pannello a scheda condiviso (nuova partita, salvataggi, catalogo, finanze…).</summary>
        public GamePanel Panel => _panel;

        public static OrbisApp Instance { get; private set; }

        public AppState State { get; private set; } = AppState.Starting;

        /// <summary>Menu principale (disponibile quando <see cref="State"/> è MainMenu).</summary>
        public MainMenuController MainMenu => _mainMenu;

        /// <summary>Pannello Impostazioni.</summary>
        public SettingsController Settings => _settings;

        private UIDocument _document;
        private AdaptiveLayout _layout;
        private readonly ScreenSwitcher _screens = new ScreenSwitcher();
        private VisualElement _splash;
        private VisualElement _loading;
        private VisualElement _menu;
        private VisualElement _progressFill;
        private Label _loadingStatus;
        private Label _loadingPercent;
        private VisualElement _backdrop;
        private MainMenuController _mainMenu;
        private SettingsController _settings;
        private VisualElement _game;
        private WorldSession _session;
        private GameHud _hud;
        private EconomyHud _economy;
        private LotsOverlay _lots;
        private GamePanel _panel;
        private GameMenus _menus;
        private bool _starting;

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            // orientamento scelto nelle impostazioni (predefinito: rotazione automatica, avvio in verticale)
            PlatformServices.Current.ScreenOrientation.Apply(SettingsRepository.LoadDisplay());
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
            _game = MainMenuController.Require<VisualElement>(root, "game");
            _progressFill = MainMenuController.Require<VisualElement>(root, "progress-fill");
            _loadingStatus = MainMenuController.Require<Label>(root, "loading-status");
            _loadingPercent = MainMenuController.Require<Label>(root, "loading-percent");
            _backdrop = MainMenuController.Require<VisualElement>(root, "backdrop");
            MainMenuController.Require<VisualElement>(root, "vignette").style.backgroundImage = new StyleBackground(Wallpaper.CreateVignette());
            _screens.Register(_splash);
            _screens.Register(_loading);
            _screens.Register(_menu);
            _screens.Register(_game);

            var progress = new BootProgress(new[]
            {
                new BootStep("texts", 1f, "loading.texts"),
                new BootStep("platform", 1f, "loading.platform"),
                new BootStep("content", 1f, "loading.content"),
                new BootStep("world", 4f, "loading.world"),
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
            MainMenuController.Require<Label>(root, "tip-text").text = Loc.Get($"tips.{UnityEngine.Random.Range(1, TipCount + 1)}");
            _screens.Show(_loading);
            var started = Time.realtimeSinceStartup;
            var shown = 0f;
            UpdateLoading(progress, ref shown, immediate: true);

            services.DeviceProfile.Refresh();
            var info = services.DeviceProfile.Info;
            Debug.Log($"[Orbis] Avvio su {info.Platform} · {info.Model} · {info.GraphicsApi} · {info.SystemMemoryMb} MB · {info.PixelsPerPoint:0.##} px/pt");
            progress.Complete("platform");

            // i contenuti arrivano con M2: per ora il passo completa in modo graduale
            for (var f = 0.25f; f <= 1f; f += 0.25f)
            {
                progress.Report("content", f);
                yield return null;
            }

            // il "mondo" del menu: la città 3D (non sui dispositivi senza GPU, es. i test in batchmode)
            var city3D = menuCity != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (city3D)
            {
                yield return menuCity.Build(f => progress.Report("world", f));
                if (glassBlur != null)
                    glassBlur.Activate();
                _backdrop.AddToClassList("backdrop--hidden"); // sfuma lo sfondo fisso: appare la città
            }
            progress.Complete("world");

            while (Time.realtimeSinceStartup - started < MinLoadingSeconds || shown < 0.999f)
            {
                UpdateLoading(progress, ref shown, immediate: false);
                yield return null;
            }
            _loadingStatus.text = Loc.Get("loading.ready");

            // 4) menu principale
            _settings = new SettingsController(root, services.ScreenOrientation, SettingsRepository.LoadDisplay());
            _mainMenu = new MainMenuController(_menu, MainMenuController.Require<VisualElement>(root, "toast"), services, _settings);
            _panel = new GamePanel(root);
            _panel.SetPointerLayout(!services.DeviceProfile.Info.IsTouchPrimary);
            _menus = new GameMenus(_panel, services);
            _menus.Start += StartGame;
            _menus.SavesChanged += () => StartCoroutine(_mainMenu.Prepare());
            _mainMenu.NewGame += _menus.OpenNewGame;
            _mainMenu.Continue += _menus.OpenSaves;
            yield return _mainMenu.Prepare();
            State = AppState.MainMenu;
            _screens.Show(_menu);
            _mainMenu.OnShown();
            Debug.Log("[Orbis] Menu principale pronto.");
        }

        private void Update()
        {
            _layout?.Update();
            _hud?.Update();
            _economy?.Update();
        }

        /// <summary>Nuova partita con le opzioni predefinite (test e fotografie).</summary>
        public void StartGame() => StartGame(new GameStart());

        /// <summary>Entra nel mondo: nuova partita o partita salvata (docs/03 §1, M2: solo Chieti).</summary>
        public void StartGame(GameStart start)
        {
            if (State == AppState.Game || _starting || start == null) return;
            if (start.SaveId != null) StartCoroutine(LoadAndEnter(start));
            else EnterWorld(start, null);
        }

        private IEnumerator LoadAndEnter(GameStart start)
        {
            _starting = true;
            var task = SaveManager.LoadAsync(PlatformServices.Current.SaveStore, start.SaveId);
            while (!task.IsCompleted) yield return null;
            _starting = false;
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null)
            {
                Debug.LogWarning("[Orbis] partita non trovata: " + start.SaveId);
                _mainMenu.ShowToast(Loc.Get("saves.error"));
                yield break;
            }
            EnterWorld(start, task.Result);
        }

        private void EnterWorld(GameStart start, byte[] save)
        {
            var services = PlatformServices.Current;
            var content = GameContent.Load();
            var camera = Camera.main;
            var sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            var device = services.DeviceProfile.Info;
            var session = WorldSession.Create(camera, sun, Orbis.World.WorldQuality.Auto(device.IsTouchPrimary, device.SystemMemoryMb));

            GameSession game;
            OfflineSummary offline = null;
            try
            {
                if (save != null)
                {
                    game = GameController.LoadSession(save, content, session);
                    offline = game.CatchUp(DateTime.UtcNow);
                }
                else
                {
                    game = GameController.NewSession(start.Name, start.Config, content, session);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] partita illeggibile: " + e.Message);
                Destroy(session.gameObject);
                _mainMenu.ShowToast(Loc.Get("saves.error"));
                return;
            }

            State = AppState.Game;
            _session = session;
            if (menuCity != null) menuCity.gameObject.SetActive(false);
            var rig = camera != null ? camera.GetComponent<MenuCameraRig>() : null;
            if (rig != null) rig.enabled = false;
            if (glassBlur != null) glassBlur.Interval = 3; // il vetro sfoca il mondo, ma un frame su tre
            _hud = new GameHud(_document.rootVisualElement, _game, _session, services);
            _hud.Home += EndGame;
            _lots = new LotsOverlay(_session);
            _hud.LotsToggled += _lots.SetVisible;

            var id = start.SaveId ?? SaveManager.NewId();
            var controller = GameController.Attach(_session, game, id, start.Transient ? null : services.SaveStore, offline);
            _economy = new EconomyHud(_game, _hud, controller, _panel, services);
            if (save == null && !start.Transient) controller.SaveNow(); // la nuova partita compare subito in "Continua"
            _screens.Show(_game);
            _menus.ShowOfflineSummary(offline);
            Debug.Log(save != null ? $"[Orbis] Partita caricata: {game.State.Name}." : $"[Orbis] Nuova partita: {game.State.Name}.");
        }

        /// <summary>Torna al menu principale salvando e chiudendo il mondo.</summary>
        public void EndGame()
        {
            if (State != AppState.Game) return;
            _panel.Close();
            var game = Game;
            if (game != null) game.SaveNowSync();
            _economy = null;
            _lots?.Dispose();
            _lots = null;
            if (_session != null) Destroy(_session.gameObject);
            _session = null;
            _hud = null;
            _game.RemoveFromClassList("game--building");
            _game.RemoveFromClassList("game--details");
            var camera = Camera.main;
            if (camera != null)
            {
                var rig = camera.GetComponent<MenuCameraRig>();
                if (rig != null) rig.enabled = true;
                camera.orthographic = false;
            }
            if (menuCity != null) menuCity.gameObject.SetActive(true);
            if (glassBlur != null) glassBlur.Interval = 1;
            State = AppState.MainMenu;
            _screens.Show(_menu);
            StartCoroutine(_mainMenu.Prepare());
            _mainMenu.OnShown();
        }

        /// <summary>La barra avanza in modo morbido verso il valore reale.</summary>
        private void UpdateLoading(BootProgress progress, ref float shown, bool immediate)
        {
            var target = progress.Overall;
            shown = immediate ? target : Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.4f);
            _progressFill.style.width = Length.Percent(shown * 100f);
            var step = progress.StepAt(shown);
            _loadingStatus.text = Loc.Get(step != null ? step.LabelKey : "loading.ready");
            _loadingPercent.text = Loc.Format("loading.percent", Mathf.RoundToInt(shown * 100f));
        }
    }
}

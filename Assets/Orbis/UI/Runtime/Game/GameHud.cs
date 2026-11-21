using System;
using System.Globalization;
using Orbis.Core.World.Fb;
using Orbis.Core.World.Geometry;
using Orbis.Core.World.Sky;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.App;
using Orbis.UI.Elements;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Game
{
    /// <summary>
    /// HUD del gioco (docs/04 §2-3): casa, orologio con meteo e velocità, 2D/3D, nord, lotti,
    /// pannello Dettagli dell'edificio selezionato con i dati reali OSM.
    /// </summary>
    public sealed class GameHud
    {
        private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

        private readonly VisualElement _root;
        private readonly VisualElement _screen;
        private readonly WorldSession _session;
        private readonly IPlatformServices _services;
        private readonly Label _time;
        private readonly Label _weather;
        private readonly OrbisIcon _weatherIcon;
        private readonly OrbisIcon _speedIcon;
        private readonly Label _speedLabel;
        private readonly OrbisIcon _viewIcon;
        private readonly OrbisIcon _northIcon;
        private readonly Button _layersButton;
        private readonly VisualElement _layers;
        private readonly VisualElement _streetsSwitch;
        private readonly VisualElement _lotsSwitch;
        private readonly VisualElement _loading;
        private readonly Label _loadingText;
        private readonly VisualElement _details;
        private readonly Label _detailsTitle;
        private readonly Label _detailsSubtitle;
        private readonly VisualElement _rows;
        private readonly VisualElement _actions;
        private readonly StreetLabels _labels;
        private WeatherKind _shownWeather = (WeatherKind)(-1);
        private int _shownTemp = int.MinValue;
        private string _shownTime;

        public event Action Home;
        public event Action<bool> LotsToggled;

        /// <summary>Dettagli personalizzati (elementi piazzati): true se gestiti.</summary>
        public Func<WorldSelection, bool> CustomDetails;

        /// <summary>Azioni aggiuntive per gli edifici reali (es. demolisci).</summary>
        public Action<WorldSelection, VisualElement> ExtraActions;

        /// <summary>Contenitore dei pulsanti d'azione del pannello Dettagli.</summary>
        public VisualElement DetailsActions => _actions;

        public bool LotsVisible { get; private set; }
        public bool StreetsVisible { get; private set; }
        private const string StreetsPref = "orbis.map.streets";

        public GameHud(VisualElement root, VisualElement screen, WorldSession session, IPlatformServices services)
        {
            _root = root;
            _screen = screen;
            _session = session;
            _services = services;
            _time = Req<Label>("hud-time");
            _weather = Req<Label>("hud-weather");
            _weatherIcon = Req<OrbisIcon>("hud-weather-icon");
            _speedIcon = Req<OrbisIcon>("hud-speed-icon");
            _speedLabel = Req<Label>("hud-speed-label");
            _viewIcon = Req<OrbisIcon>("hud-view-icon");
            _northIcon = Req<OrbisIcon>("hud-north-icon");
            _layersButton = Req<Button>("hud-layers");
            _layers = Req<VisualElement>("layers");
            _streetsSwitch = Req<VisualElement>("streets-switch");
            _lotsSwitch = Req<VisualElement>("lots-switch");
            _loading = Req<VisualElement>("world-loading");
            _loadingText = Req<Label>("world-loading-text");
            _details = Req<VisualElement>("details");
            _detailsTitle = Req<Label>("details-title");
            _detailsSubtitle = Req<Label>("details-subtitle");
            _rows = Req<VisualElement>("details-rows");
            _actions = Req<VisualElement>("details-actions");

            Req<Button>("hud-home").clicked += () => Home?.Invoke();
            Req<Button>("hud-speed").clicked += CycleSpeed;
            Req<Button>("hud-view").clicked += () => session.CameraRig.SetTopDown(!session.CameraRig.TopDown);
            Req<Button>("hud-north").clicked += () => session.CameraRig.ResetNorth();
            _layersButton.clicked += ToggleLayersPanel;
            Req<Button>("toggle-streets").clicked += ToggleStreets;
            Req<Button>("toggle-lots").clicked += ToggleLots;
            Req<Button>("details-close").clicked += () => session.Select(null);
            session.SelectionChanged += ShowDetails;
            session.CameraRig.ModeChanged += topDown => _viewIcon.icon = topDown ? OrbisIconKind.Cube : OrbisIconKind.Map;
            session.CameraRig.IsOverUi = IsOverUi;
            session.CameraRig.KeyboardBlocked = IsTyping;
            _labels = new StreetLabels(screen, session);
            StreetsVisible = PlayerPrefs.GetInt(StreetsPref, 1) == 1;
            _labels.Visible = StreetsVisible;
            _streetsSwitch.EnableInClassList("switch--on", StreetsVisible);
            _details.RemoveFromClassList("details--visible");
            _loading.AddToClassList("world-loading--visible");
            _loadingText.text = Loc.Format("game.loading", "Chieti");
            UpdateSpeed();
        }

        private T Req<T>(string name) where T : VisualElement => MainMenuController.Require<T>(_screen, name);

        public void Update()
        {
            switch (_session.State)
            {
                case WorldSession.Phase.Loading:
                    _loading.AddToClassList("world-loading--visible");
                    break;
                case WorldSession.Phase.Missing:
                    _loadingText.text = Loc.Get("game.missing");
                    _loading.AddToClassList("world-loading--visible");
                    break;
                case WorldSession.Phase.Failed:
                    _loadingText.text = Loc.Get("game.failed");
                    _loading.AddToClassList("world-loading--visible");
                    break;
                default:
                    _loading.RemoveFromClassList("world-loading--visible");
                    break;
            }

            var now = _session.Clock.Now;
            var time = now.ToString("HH:mm", Italian);
            if (time != _shownTime)
            {
                _shownTime = time;
                _time.text = time;
            }
            var w = _session.CurrentWeather;
            bool night = _session.Sky.Night > 0.5f;
            var temp = _session.Temperature;
            int shownTemp = temp.HasValue ? Mathf.RoundToInt(temp.Value) : int.MinValue;
            if (w != _shownWeather || shownTemp != _shownTemp)
            {
                _shownWeather = w;
                _shownTemp = shownTemp;
                // "Sereno · 12°" con il meteo della partita, altrimenti solo il tempo
                _weather.text = temp.HasValue ? Loc.Format("weather.withTemp", Loc.Get("weather." + w), shownTemp) : Loc.Get("weather." + w);
            }
            _weatherIcon.icon = WeatherIcon(w, night);
            _labels.Update();
            // bussola: l'ago indica il nord
            _northIcon.style.rotate = new Rotate(new Angle(-_session.CameraRig.Yaw, AngleUnit.Degree));
        }

        private static OrbisIconKind WeatherIcon(WeatherKind w, bool night)
        {
            switch (w)
            {
                case WeatherKind.Rain: return OrbisIconKind.Rain;
                case WeatherKind.Fog: return OrbisIconKind.Fog;
                case WeatherKind.Cloudy: return OrbisIconKind.Cloud;
                default: return night ? OrbisIconKind.Moon : OrbisIconKind.Sun;
            }
        }

        private void CycleSpeed()
        {
            _services.Haptics.Play(HapticKind.Selection);
            var clock = _session.Clock;
            clock.SetSpeed(clock.SpeedIndex + 1 >= GameClock.Speeds.Length ? 0 : clock.SpeedIndex + 1);
            UpdateSpeed();
        }

        private void UpdateSpeed()
        {
            var clock = _session.Clock;
            _speedIcon.icon = clock.Paused ? OrbisIconKind.Pause : (clock.Speed > 1 ? OrbisIconKind.Fast : OrbisIconKind.Play);
            _speedLabel.text = clock.Paused ? "‖" : clock.Speed.ToString("0", Italian) + "×";
        }

        private void ToggleLayersPanel()
        {
            _services.Haptics.Play(HapticKind.Selection);
            bool open = !_layers.ClassListContains("layers--visible");
            _layers.EnableInClassList("layers--visible", open);
            _layersButton.EnableInClassList("hud-button--on", open);
        }

        private void ToggleStreets()
        {
            _services.Haptics.Play(HapticKind.Selection);
            StreetsVisible = !StreetsVisible;
            _labels.Visible = StreetsVisible;
            _streetsSwitch.EnableInClassList("switch--on", StreetsVisible);
            PlayerPrefs.SetInt(StreetsPref, StreetsVisible ? 1 : 0);
            PlayerPrefs.Save();
        }

        private void ToggleLots()
        {
            _services.Haptics.Play(HapticKind.Selection);
            LotsVisible = !LotsVisible;
            _lotsSwitch.EnableInClassList("switch--on", LotsVisible);
            LotsToggled?.Invoke(LotsVisible);
        }

        /// <summary>Il fuoco è in un campo di testo (ricerca del catalogo, nome della partita)?</summary>
        public bool IsTyping()
        {
            var focused = _root.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
        }

        /// <summary>Il punto (pixel, y in basso) è sopra un controllo dell'interfaccia?</summary>
        public bool IsOverUi(Vector2 screen)
        {
            var panel = _root.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            for (var e = panel.Pick(p); e != null; e = e.parent)
            {
                if (e is Button || e.ClassListContains("details") || e.ClassListContains("hud-clock") || e.ClassListContains("layers--visible") ||
                    e.ClassListContains("sheet-layer--visible") || e.ClassListContains("build-bar") || e is ScrollView)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ dettagli

        public void ClearSelection() => _session.Select(null);

        /// <summary>Apre il pannello Dettagli vuoto con titolo e sottotitolo (le righe si aggiungono con <see cref="DetailsRow"/>).</summary>
        public void ShowDetailsPanel(string title, string subtitle, bool landmark)
        {
            _detailsTitle.text = title;
            _detailsSubtitle.text = subtitle ?? "";
            _details.EnableInClassList("details--landmark", landmark);
            _rows.Clear();
            _actions.Clear();
            _details.AddToClassList("details--visible");
            _screen.AddToClassList("game--details");
        }

        public void DetailsRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("details__row");
            var l = new Label(label);
            l.AddToClassList("details__label");
            var v = new Label(value);
            v.AddToClassList("details__value");
            row.Add(l);
            row.Add(v);
            _rows.Add(row);
        }

        private void ShowDetails(WorldSelection s)
        {
            if (s != null && s.PlacedObjectId != 0 && CustomDetails != null)
            {
                _services.Haptics.Play(HapticKind.Selection);
                if (CustomDetails(s)) return;
            }
            if (s == null || s.Building == null)
            {
                _details.RemoveFromClassList("details--visible");
                _screen.RemoveFromClassList("game--details");
                _actions.Clear();
                return;
            }
            _services.Haptics.Play(HapticKind.Selection);
            var b = s.Building;
            string use = Loc.Get("use." + b.Use);
            string title = !string.IsNullOrEmpty(b.Name) ? b.Name : FirstNamedPoi(s) ?? use;
            ShowDetailsPanel(title, title == use ? (b.Address ?? "") : use, !string.IsNullOrEmpty(b.Landmark));
            ExtraActions?.Invoke(s, _actions);
            if (!string.IsNullOrEmpty(b.Landmark)) Row("details.landmark", Loc.Get("use." + b.Use));
            if (!string.IsNullOrEmpty(b.Address)) Row("details.address", b.Address);
            Row("details.use", use);
            var height = Mathf.RoundToInt(b.Height).ToString(Italian);
            Row("details.height", Loc.Format(b.HasFlag(BuildingFlag.HeightEstimated) ? "details.height.estimated" : "details.height.value",
                height, b.Levels));
            Row("details.roof", Loc.Get("roof." + b.Roof));
            float area = Mathf.Abs(Polygon2D.SignedArea(b.Outer));
            foreach (var h in b.Holes) area -= Mathf.Abs(Polygon2D.SignedArea(h));
            Row("details.footprint", Loc.Format("details.footprint.value", Mathf.RoundToInt(area).ToString("N0", Italian)));
            if (b.Year > 0) Row("details.year", b.Year.ToString(Italian));
            foreach (var p in s.Pois)
            {
                var cat = PoiLabel(p.Category);
                Row("details.inside", string.IsNullOrEmpty(p.Name) ? cat : $"{p.Name} · {cat}");
            }
            Row("details.position", string.Format(Italian, "{0:0.00000}°N  {1:0.00000}°E", s.Lat, s.Lon));
            Row("details.source", Loc.Format("details.source.value", Loc.Get("osm." + OsmKind(b.OsmType)), b.OsmId));
        }

        private static string OsmKind(OsmType t) => t == OsmType.Relation ? "relation" : (t == OsmType.Node ? "node" : "way");

        private static string FirstNamedPoi(WorldSelection s)
        {
            foreach (var p in s.Pois)
                if (!string.IsNullOrEmpty(p.Name)) return p.Name;
            return null;
        }

        private static string PoiLabel(string category)
        {
            if (string.IsNullOrEmpty(category)) return "";
            if (Loc.TryGet("poi." + category.Replace('/', '.'), out var text)) return text;
            var slash = category.IndexOf('/');
            var v = slash >= 0 ? category.Substring(slash + 1) : category;
            v = v.Replace('_', ' ');
            return v.Length > 0 ? char.ToUpperInvariant(v[0]) + v.Substring(1) : v;
        }

        private void Row(string labelKey, string value) => DetailsRow(Loc.Get(labelKey), value);
    }
}

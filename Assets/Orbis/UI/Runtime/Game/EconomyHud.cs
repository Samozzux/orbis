using System;
using System.Collections.Generic;
using Orbis.Core.Game;
using Orbis.Core.World.Geometry;
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
    /// Interfaccia della partita (M2, docs/04 §3-4): cassa, livello, missione corrente, annulla/ripeti,
    /// Costruisci (catalogo a schede), Finanze (aliquote), Riavvolgi, barra della costruzione con anteprima
    /// verde/rossa e motivo, azioni nel pannello Dettagli (potenzia, colori, sposta, ruota, demolisci).
    /// </summary>
    public sealed class EconomyHud
    {
        private readonly VisualElement _screen;
        private readonly GameHud _hud;
        private readonly GameController _game;
        private readonly GamePanel _panel;
        private readonly IPlatformServices _services;
        private readonly Label _cash, _level, _missionTitle, _missionText, _buildName, _buildInfo, _toastText;
        private readonly VisualElement _xpFill, _missionFill, _missionCard, _buildBar, _toast;
        private readonly Button _undo, _redo, _confirm;
        private IVisualElementScheduledItem _toastHide;
        private long _shownCash = long.MinValue;
        private int _shownLevel = -1;
        private int _shownMission = -1;
        private string _catalogTab = nameof(CatalogCategory.Residential);
        private string _search = "";
        private VisualElement _catalogGrid;

        public EconomyHud(VisualElement screen, GameHud hud, GameController game, GamePanel panel, IPlatformServices services)
        {
            _screen = screen;
            _hud = hud;
            _game = game;
            _panel = panel;
            _services = services;
            _cash = Req<Label>("hud-cash-value");
            _level = Req<Label>("hud-level-value");
            _xpFill = Req<VisualElement>("hud-xp-fill");
            _missionCard = Req<VisualElement>("mission-card");
            _missionTitle = Req<Label>("mission-title");
            _missionText = Req<Label>("mission-text");
            _missionFill = Req<VisualElement>("mission-fill");
            _buildBar = Req<VisualElement>("build-bar");
            _buildName = Req<Label>("build-name");
            _buildInfo = Req<Label>("build-info");
            _toast = Req<VisualElement>("game-toast");
            _toastText = Req<Label>("game-toast-text");
            _undo = Req<Button>("hud-undo");
            _redo = Req<Button>("hud-redo");
            _confirm = Req<Button>("build-confirm");

            Req<Button>("mission-card").clicked += () => _missionCard.ToggleInClassList("mission-card--open");
            _undo.clicked += () =>
            {
                if (!_game.Undo()) return;
                Toast(Loc.Get("game.undone"));
                RefreshSelection();
            };
            _redo.clicked += () =>
            {
                if (!_game.Redo()) return;
                Toast(Loc.Get("game.redone"));
                RefreshSelection();
            };
            Req<Button>("hud-build").clicked += OpenCatalog;
            Req<Button>("hud-finance").clicked += OpenFinance;
            Req<Button>("hud-rewind").clicked += OpenRewind;
            Req<Button>("build-rotate").clicked += () => _game.Build.Rotate();
            Req<Button>("build-cancel").clicked += () => _game.Build.Cancel();
            _confirm.clicked += () => _game.Build.Confirm();

            var build = _game.Build;
            build.AimAtPointer = !services.DeviceProfile.Info.IsTouchPrimary;
            build.Changed += RefreshBuildBar;
            build.Confirmed += OnBuildConfirmed;
            _game.SimulationEvent += OnSimulationEvent;
            _game.Changed += () => _shownCash = long.MinValue;
            _game.PopulationStarted += name => Toast(Loc.Format("citizens.arriving", name));
            _game.PopulationReady += p => Toast(Loc.Format("citizens.ready", Fmt.Money(p.AliveCount), p.MunicipalityName));
            hud.CustomDetails = ShowPlacedDetails;
            hud.ExtraActions = AddOsmActions;
            RefreshBuildBar();
        }

        private T Req<T>(string name) where T : VisualElement => MainMenuController.Require<T>(_screen, name);

        /// <summary>Dopo annulla/ripeti/riavvolgi il pannello Dettagli si riallinea (o si chiude se l'elemento non c'è più).</summary>
        private void RefreshSelection()
        {
            var sel = _game.World.Selection;
            if (sel == null) return;
            if (sel.PlacedObjectId != 0)
            {
                if (_game.State.Objects.ContainsKey(sel.PlacedObjectId)) _game.World.SelectPlaced(sel.PlacedObjectId);
                else _hud.ClearSelection();
            }
            else if (sel.Building != null)
            {
                // edificio reale: demolito → si chiude; altrimenti si ridisegna (livello cambiato)
                if (_game.State.DemolishedOsm.ContainsKey(GameState.OsmKey(sel.Building.OsmType, sel.Building.OsmId))) _hud.ClearSelection();
                else _game.World.Select(sel);
            }
        }

        public void Update()
        {
            var s = _game.State;
            if (s.Cash != _shownCash)
            {
                _shownCash = s.Cash;
                _cash.text = Fmt.Money(s.Cash);
                _undo.SetEnabled(_game.Session.Commands.CanUndo);
                _redo.SetEnabled(_game.Session.Commands.CanRedo);
            }
            if (s.Level != _shownLevel || Time.frameCount % 30 == 0)
            {
                _shownLevel = s.Level;
                _level.text = Loc.Format("game.level", s.Level);
                _xpFill.style.width = Length.Percent(Progression.LevelProgress(s, _game.Content.Balance) * 100f);
                _undo.SetEnabled(_game.Session.Commands.CanUndo);
                _redo.SetEnabled(_game.Session.Commands.CanRedo);
            }
            var m = _game.Content.Missions.Current(s);
            if (s.MissionIndex != _shownMission || Time.frameCount % 30 == 0)
            {
                _shownMission = s.MissionIndex;
                _missionCard.EnableInClassList("mission-card--done", m == null);
                if (m != null)
                {
                    _missionTitle.text = m.Title;
                    var reward = m.RewardCash > 0 ? Loc.Format("mission.reward", Fmt.Money(m.RewardCash)) : "";
                    _missionText.text = m.Text + (reward.Length > 0 ? "\n" + reward : "");
                    float p = m.Count > 0 ? Mathf.Clamp01((float)Missions.Progress(s, m) / m.Count) : 0;
                    _missionFill.style.width = Length.Percent(p * 100f);
                }
            }
            _screen.EnableInClassList("game--building", _game.Build.Active);
        }

        // ------------------------------------------------------------------ avvisi

        public void Toast(string text)
        {
            _toastText.text = text;
            _toast.AddToClassList("game-toast--visible");
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => _toast.RemoveFromClassList("game-toast--visible")).StartingIn(2800);
        }

        private void OnSimulationEvent(SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.ConstructionDone:
                    var item = _game.Content.Catalog.Get(e.Key);
                    Toast(Loc.Format("game.event.built", item != null ? item.Name : e.Key));
                    if (_game.World.Selection != null && _game.World.Selection.PlacedObjectId == e.ObjectId) RefreshSelection();
                    break;
                case SimEventKind.MissionDone:
                    var m = _game.Content.Missions.All.Find(x => x.Key == e.Key);
                    Toast(Loc.Format("game.event.mission", m != null ? m.Title : e.Key, Fmt.Money(e.Amount)));
                    _services.Haptics.Play(HapticKind.Success);
                    break;
                case SimEventKind.LevelUp:
                    Toast(Loc.Format("game.event.level", e.Amount));
                    _services.Haptics.Play(HapticKind.Success);
                    break;
                case SimEventKind.Reaction when e.Reaction != null:
                    // petizioni, proteste e scioperi: "Petizione per tasse più basse: 1.250 firme"
                    Toast(Loc.Format("reaction." + e.Reaction.Kind, Fmt.Money(e.Amount), Loc.Get("demand." + e.Reaction.Factor)));
                    break;
            }
        }

        public static string Reason(BuildResult r) => Loc.Get("result." + r);

        // ------------------------------------------------------------------ costruzione

        private void RefreshBuildBar()
        {
            var b = _game.Build;
            if (!b.Active) return;
            _hud.ClearSelection();
            _buildName.text = b.Item.Name + "  ·  " + Fmt.Money(b.Cost);
            bool ok = b.Check == BuildResult.Ok;
            _buildBar.EnableInClassList("build-bar--bad", !ok);
            _confirm.SetEnabled(ok);
            _buildInfo.text = ok
                ? Loc.Get(b.AimAtPointer ? "build.hint.pointer" : "build.hint.touch")
                : Reason(b.Check);
        }

        private void OnBuildConfirmed(BuildResult r)
        {
            if (r == BuildResult.Ok)
            {
                _services.Haptics.Play(HapticKind.Success);
                Toast(Loc.Format("build.started", _game.Build.Item != null ? _game.Build.Item.Name : ""));
            }
            else
            {
                _services.Haptics.Play(HapticKind.Warning);
                Toast(Reason(r));
            }
        }

        private void OpenCatalog()
        {
            _services.Haptics.Play(HapticKind.Selection);
            _panel.Open(Loc.Get("build.title"));
            FillCatalog();
        }

        private void FillCatalog()
        {
            var tabs = new List<(string, string)>();
            foreach (CatalogCategory c in Enum.GetValues(typeof(CatalogCategory)))
                tabs.Add((c.ToString(), Loc.Get("category." + c)));
            _panel.Tabs(tabs, _search.Length > 0 ? null : _catalogTab, key =>
            {
                _catalogTab = key;
                _search = "";
                _panel.Clear();
                FillCatalog();
            });
            // ricerca per nome in tutto il catalogo (le schede si spengono mentre si cerca)
            var search = new TextField { value = _search, name = "catalog-search", maxLength = 40 };
            search.AddToClassList("panel-field");
            search.textEdition.placeholder = Loc.Get("build.search");
            search.RegisterValueChangedCallback(e =>
            {
                _search = (e.newValue ?? "").Trim();
                FillCatalogGrid();
            });
            _panel.Content.Add(search);
            _catalogGrid = new VisualElement();
            _catalogGrid.AddToClassList("panel-grid");
            _panel.Content.Add(_catalogGrid);
            FillCatalogGrid();
        }

        private void FillCatalogGrid()
        {
            _catalogGrid.Clear();
            var s = _game.State;
            IEnumerable<CatalogItem> items;
            if (_search.Length > 0)
            {
                var found = new List<CatalogItem>();
                foreach (var it in _game.Content.Catalog.Items)
                    if (it.Name.IndexOf(_search, StringComparison.CurrentCultureIgnoreCase) >= 0) found.Add(it);
                items = found;
                if (found.Count == 0) _panel.Text(Loc.Get("build.search.none"), _catalogGrid);
            }
            else
            {
                items = _game.Content.Catalog.InCategory((CatalogCategory)Enum.Parse(typeof(CatalogCategory), _catalogTab));
            }
            foreach (var item in items)
            {
                bool locked = s.Level < item.UnlockLevel && !s.Config.CreativeMode;
                var card = new Button();
                card.AddToClassList("catalog-card");
                card.EnableInClassList("catalog-card--locked", locked);
                var name = new Label(item.Name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("catalog-card__name");
                // anteprima 3D dell'elemento sotto il nome (arriva dopo pochi frame la prima volta)
                var preview = new VisualElement { pickingMode = PickingMode.Ignore };
                preview.AddToClassList("catalog-card__preview");
                var tex = _game.Previews != null ? _game.Previews.Get(item, t => SetPreview(preview, t)) : null;
                if (tex != null) SetPreview(preview, tex);
                var info = new Label(Describe(item)) { pickingMode = PickingMode.Ignore };
                info.AddToClassList("catalog-card__info");
                var cost = new Label(locked ? Loc.Format("build.locked", item.UnlockLevel) : Fmt.Money(Placement.CostOf(item, null))) { pickingMode = PickingMode.Ignore };
                cost.AddToClassList("catalog-card__cost");
                card.Add(name);
                card.Add(preview);
                card.Add(info);
                card.Add(cost);
                var it = item;
                card.clicked += () =>
                {
                    if (locked)
                    {
                        Toast(Loc.Format("build.locked", it.UnlockLevel));
                        return;
                    }
                    _panel.Close();
                    _game.Build.StartPlacing(it);
                };
                _catalogGrid.Add(card);
            }
        }

        private static void SetPreview(VisualElement e, Texture t)
        {
            if (t is RenderTexture rt) e.style.backgroundImage = Background.FromRenderTexture(rt);
            else if (t is Texture2D t2) e.style.backgroundImage = Background.FromTexture2D(t2);
            e.AddToClassList("catalog-card__preview--ready");
        }

        /// <summary>Sagoma e resa principale (abitanti, posti, energia, produzione…).</summary>
        private static string Describe(CatalogItem item)
        {
            var l = item.LevelSpec(1);
            var parts = new List<string> { Loc.Format("build.size", Num(item.Width), Num(item.Depth)) };
            if (item.Category == CatalogCategory.Residential && l.Capacity > 0) parts.Add(Loc.Format("build.residents", l.Capacity));
            else if (l.Jobs > 0) parts.Add(Loc.Format("build.jobs", l.Jobs));
            if (l.PowerKw > 1) parts.Add(Loc.Format("build.power", Fmt.Money((long)l.PowerKw)));
            if (l.RentPerMonth > 0) parts.Add(Loc.Format("build.rent", Fmt.Money(l.RentPerMonth)));
            if (l.ProductionPerMonth > 0) parts.Add(Loc.Format("build.production", Fmt.Money(l.ProductionPerMonth)));
            bool amenity = item.Category == CatalogCategory.Nature || item.Category == CatalogCategory.Culture || item.Category == CatalogCategory.Sports ||
                           item.Category == CatalogCategory.Services || item.Category == CatalogCategory.Decoration;
            if (amenity && item.Happiness > 0.4f) parts.Add(Loc.Get("build.happy"));
            return string.Join(" · ", parts);
        }

        private static string Num(float v) => v.ToString(v % 1 == 0 ? "0" : "0.0", Fmt.It);

        // ------------------------------------------------------------------ finanze

        private void OpenFinance()
        {
            _services.Haptics.Play(HapticKind.Selection);
            _panel.Open(Loc.Get("finance.title"));
            FillFinance();
        }

        private void FillFinance()
        {
            var s = _game.State;
            var b = _game.Content.Balance;
            var f = Economy.Compute(_game.Session.Context);
            _panel.Section(Loc.Get("finance.city"));
            _panel.Row(Loc.Get("finance.cash"), Fmt.Money(s.Cash));
            _panel.Row(Loc.Get("finance.population"), Fmt.Money(f.Population));
            _panel.Row(Loc.Get("finance.jobs"), Fmt.Money(f.CommercialJobs + f.IndustrialJobs));
            _panel.Row(Loc.Get("finance.happiness"), Mathf.RoundToInt(f.Happiness) + "/100");
            _panel.Row(Loc.Get("finance.energy"), Loc.Format("finance.kw", Fmt.Signed((long)f.PowerKw)), Math.Sign(f.PowerKw));
            _panel.Row(Loc.Get("finance.forecast"), Fmt.Signed((long)Math.Round(f.Net)), Math.Sign(f.Net));

            _panel.Section(Loc.Get("finance.taxes"));
            TaxRow(TaxKind.Residential, s.ResidentialTax, b.ResidentialTax);
            TaxRow(TaxKind.Commercial, s.CommercialTax, b.CommercialTax);
            TaxRow(TaxKind.Industrial, s.IndustrialTax, b.IndustrialTax);

            // ultimo giorno chiuso e totale degli ultimi 30 giorni, per categoria
            LedgerDay last = null;
            for (int i = s.Ledger.Count - 1; i >= 0; i--)
                if (s.Ledger[i].Date < s.Now.Date) { last = s.Ledger[i]; break; }
            var month = new long[(int)LedgerCategory.Count];
            foreach (var d in s.Ledger)
                if ((s.Now.Date - d.Date).TotalDays <= 30)
                    for (int k = 0; k < month.Length; k++) month[k] += d.Amounts[k];
            if (last != null)
            {
                _panel.Section(Loc.Format("finance.day", Fmt.Date(last.Date)));
                for (int k = 0; k < (int)LedgerCategory.Count; k++)
                    if (last.Amounts[k] != 0) _panel.Row(Loc.Get("ledger." + (LedgerCategory)k), Fmt.Signed(last.Amounts[k]), Math.Sign(last.Amounts[k]));
                _panel.Row(Loc.Get("finance.net"), Fmt.Signed(last.Net), Math.Sign(last.Net));
            }
            _panel.Section(Loc.Get("finance.month"));
            long net = 0;
            for (int k = 0; k < month.Length; k++)
            {
                net += month[k];
                if (month[k] != 0) _panel.Row(Loc.Get("ledger." + (LedgerCategory)k), Fmt.Signed(month[k]), Math.Sign(month[k]));
            }
            _panel.Row(Loc.Get("finance.net"), Fmt.Signed(net), Math.Sign(net));
        }

        private void TaxRow(TaxKind kind, float value, TaxRange range)
        {
            var row = _panel.Row(Loc.Get("tax." + kind), "");
            var valueLabel = row.Q<Label>(className: "panel-row__value");
            valueLabel.text = Fmt.Percent(value);
            var controls = new VisualElement();
            controls.AddToClassList("tax-controls");
            row.Remove(valueLabel);
            controls.Add(valueLabel);
            var minus = GamePanel.ActionButton(controls, "", OrbisIconKind.Minus, "", () => SetTax(kind, value - 0.5f));
            minus.name = "tax-" + kind + "-minus";
            minus.SetEnabled(value > range.Min + 1e-3f);
            var plus = GamePanel.ActionButton(controls, "", OrbisIconKind.Plus, "", () => SetTax(kind, value + 0.5f));
            plus.name = "tax-" + kind + "-plus";
            plus.SetEnabled(value < range.Max - 1e-3f);
            row.Add(controls);
        }

        private void SetTax(TaxKind kind, float rate)
        {
            _game.Do(new SetTaxCommand { Kind = kind, Rate = rate });
            _panel.Clear();
            FillFinance();
        }

        // ------------------------------------------------------------------ riavvolgi

        private void OpenRewind()
        {
            _services.Haptics.Play(HapticKind.Selection);
            _panel.Open(Loc.Get("rewind.title"));
            _panel.Text(Loc.Get("rewind.text"));
            var snaps = _game.Session.Snapshots.Items;
            for (int i = snaps.Count - 1; i >= 0; i--)
            {
                var sn = snaps[i];
                var row = _panel.Row(Fmt.DateTime(sn.GameTime), Loc.Format("rewind.row", Fmt.Money(sn.Cash), sn.Level));
                var b = GamePanel.ActionButton(row, Loc.Get("rewind.go"), OrbisIconKind.Rewind, "", () =>
                    _panel.Confirm(Loc.Get("rewind.confirm.title"), Loc.Format("rewind.confirm.text", Fmt.DateTime(sn.GameTime)), Loc.Get("rewind.go"), () =>
                    {
                        _game.Rewind(sn);
                        RefreshSelection();
                        Toast(Loc.Format("rewind.done", Fmt.DateTime(sn.GameTime)));
                    }));
                b.style.marginLeft = 8;
            }
        }

        // ------------------------------------------------------------------ dettagli

        private bool ShowPlacedDetails(WorldSelection sel)
        {
            if (sel == null || sel.PlacedObjectId == 0) return false;
            if (!_game.State.Objects.TryGetValue(sel.PlacedObjectId, out var o)) return false;
            var item = _game.Content.Catalog.Get(o.ItemKey);
            if (item == null) return false;
            var s = _game.State;
            var spec = item.LevelSpec(o.Level);
            _hud.ShowDetailsPanel(item.Name, Loc.Format("details.placed.subtitle", Loc.Get("category." + item.Category), o.Level, item.MaxLevel), false);
            _hud.DetailsRow(Loc.Get("details.placed.state"), o.IsReady(s.Now) ? Loc.Get("details.placed.ready") : Loc.Format("details.placed.building", Fmt.DateTime(o.ReadyAt)));
            if (item.Category == CatalogCategory.Residential && spec.Capacity > 0) _hud.DetailsRow(Loc.Get("details.placed.residents"), Fmt.Money(spec.Capacity));
            if (spec.Jobs > 0) _hud.DetailsRow(Loc.Get("details.placed.jobs"), Fmt.Money(spec.Jobs));
            if (spec.RentPerMonth > 0) _hud.DetailsRow(Loc.Get("details.placed.rent"), Fmt.Money(spec.RentPerMonth));
            if (spec.ProductionPerMonth > 0) _hud.DetailsRow(Loc.Get("details.placed.production"), Fmt.Money(spec.ProductionPerMonth));
            if (Math.Abs(spec.PowerKw) > 0.5f) _hud.DetailsRow(Loc.Get("details.placed.power"), Loc.Format("finance.kw", Fmt.Signed((long)spec.PowerKw)));
            if (spec.MaintenancePerMonth > 0) _hud.DetailsRow(Loc.Get("details.placed.maintenance"), Fmt.Money(spec.MaintenancePerMonth));
            Placement.Extent(item, o.Rotation, out var sx, out var sy);
            _hud.DetailsRow(Loc.Get("details.placed.footprint"), Loc.Format("build.size", Num(sx), Num(sy)));
            var variant = item.Variant(o.Variant);
            if (variant != null) _hud.DetailsRow(Loc.Get("details.placed.variant"), variant.Name);

            var actions = _hud.DetailsActions;
            if (o.Level < item.MaxLevel)
            {
                long cost = Placement.CostOf(item, o.Variant, o.Level + 1);
                var up = GamePanel.ActionButton(actions, Loc.Format("details.action.upgrade", Fmt.Money(cost)), OrbisIconKind.Upgrade, "primary",
                    () => Run(new UpgradeCommand { ObjectId = o.Id }, o.Id));
                up.SetEnabled(o.IsReady(s.Now));
            }
            if (item.Variants.Count > 1)
            {
                int i = Math.Max(0, item.Variants.FindIndex(v => v.Key == o.Variant));
                var next = item.Variants[(i + 1) % item.Variants.Count];
                GamePanel.ActionButton(actions, Loc.Format("details.action.variant", next.Name), OrbisIconKind.Palette, "",
                    () => Run(new VariantCommand { ObjectId = o.Id, Variant = next.Key }, o.Id));
            }
            GamePanel.ActionButton(actions, Loc.Get("details.action.move"), OrbisIconKind.Move, "", () =>
            {
                _hud.ClearSelection();
                _game.Build.StartMoving(o.Id);
            });
            GamePanel.ActionButton(actions, Loc.Get("details.action.rotate"), OrbisIconKind.Rotate, "",
                () => Run(new MoveCommand { ObjectId = o.Id, Lot = o.Lot, HalfX = o.HalfX, HalfY = o.HalfY, Rotation = (o.Rotation + 1) & 3 }, o.Id));
            GamePanel.ActionButton(actions, Loc.Get("details.action.demolish"), OrbisIconKind.Trash, "danger", () =>
                _panel.Confirm(Loc.Get("details.demolish.title"), Loc.Format("details.demolish.text", item.Name), Loc.Get("details.action.demolish"), () =>
                {
                    Run(new DemolishCommand { ObjectId = o.Id }, 0);
                    _hud.ClearSelection();
                }));
            return true;
        }

        /// <summary>Azioni sugli edifici reali OSM: potenziare (addobbi che cambiano a ogni livello) e demolire per liberare il lotto.</summary>
        private void AddOsmActions(WorldSelection sel, VisualElement actions)
        {
            if (sel?.Building == null || sel.Tile == null) return;
            var b = sel.Building;
            long key = GameState.OsmKey(b.OsmType, b.OsmId);
            float area = Mathf.Abs(Polygon2D.SignedArea(b.Outer));
            int floors = b.Levels > 0 ? b.Levels : Mathf.Max(1, Mathf.RoundToInt(b.Height / 3.2f));
            float floorArea = area * floors;
            var lot = LotOf(sel);
            int level = _game.State.OsmLevel(key);
            _hud.DetailsRow(Loc.Get("details.osm.level"), Loc.Format("details.osm.level.value", level, UpgradeOsmCommand.MaxLevel));
            if (level < UpgradeOsmCommand.MaxLevel)
            {
                long upCost = _game.State.Config.CreativeMode ? 0 : UpgradeOsmCommand.CostOf(_game.Content.Balance, level, floorArea);
                GamePanel.ActionButton(actions, Loc.Format("details.action.upgrade", Fmt.Money(upCost)), OrbisIconKind.Upgrade, "primary", () =>
                {
                    var r = _game.Do(new UpgradeOsmCommand { OsmKey = key, Lot = lot, FloorArea = floorArea });
                    if (r != BuildResult.Ok)
                    {
                        _services.Haptics.Play(HapticKind.Warning);
                        Toast(Reason(r));
                        return;
                    }
                    _services.Haptics.Play(HapticKind.Success);
                    Toast(Loc.Format("details.osm.upgraded", _game.State.OsmLevel(key)));
                    _game.World.Select(sel); // ridisegna i Dettagli con il nuovo livello
                }).name = "details-upgrade-osm";
            }
            long cost = (long)Math.Round(area * _game.Content.Balance.DemolishOsmCostPerM2);
            GamePanel.ActionButton(actions, Loc.Format("details.action.demolish_cost", Fmt.Money(cost)), OrbisIconKind.Trash, "danger", () =>
                _panel.Confirm(Loc.Get("details.demolish.title"), Loc.Get("details.demolish.osm"), Loc.Get("details.action.demolish"), () =>
                {
                    var r = _game.Do(new DemolishOsmCommand { OsmKey = key, Lot = lot, AreaM2 = area });
                    Toast(r == BuildResult.Ok ? Loc.Get("details.demolished") : Reason(r));
                    _hud.ClearSelection();
                }));
        }

        /// <summary>Lotto che contiene il centro dell'edificio (0 se nessuno: basta il tile per ridisegnarlo).</summary>
        private static LotKey LotOf(WorldSelection sel)
        {
            var b = sel.Building;
            float cx = (b.Bounds.MinX + b.Bounds.MaxX) / 2, cy = (b.Bounds.MinY + b.Bounds.MaxY) / 2;
            uint lotId = 0;
            foreach (var l in sel.Tile.Data.Lots)
                if (l.Bounds.Intersects(b.Bounds) && Polygon2D.Contains(l.Outer, l.Holes, cx, cy))
                {
                    lotId = l.Id;
                    break;
                }
            return new LotKey(sel.Tile.Id.X, sel.Tile.Id.Y, lotId);
        }

        private void Run(IGameCommand cmd, int reselect)
        {
            var r = _game.Do(cmd);
            if (r != BuildResult.Ok)
            {
                _services.Haptics.Play(HapticKind.Warning);
                Toast(Reason(r));
                return;
            }
            _services.Haptics.Play(HapticKind.Selection);
            if (reselect != 0) _game.World.SelectPlaced(reselect);
        }
    }
}

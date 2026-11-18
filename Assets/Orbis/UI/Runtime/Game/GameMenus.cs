using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orbis.Core.Game;
using Orbis.Gameplay;
using Orbis.Platform;
using Orbis.UI.Elements;
using Orbis.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI.Game
{
    /// <summary>Come entrare nel mondo: nuova partita (con la sua configurazione) o una partita salvata.</summary>
    public sealed class GameStart
    {
        /// <summary>Partita salvata da caricare; null = nuova partita.</summary>
        public string SaveId;
        public string Name = "Chieti";
        public WorldConfig Config = new WorldConfig();
        /// <summary>Senza salvataggi (fotografie del mondo).</summary>
        public bool Transient;
    }

    /// <summary>
    /// Pannelli del menu (docs/04 §1, docs/03 §1): Nuova partita con le opzioni del mondo, elenco delle partite
    /// salvate per "Continua" (carica, elimina) e riepilogo del tempo passato ad app chiusa.
    /// </summary>
    public sealed class GameMenus
    {
        private readonly GamePanel _panel;
        private readonly IPlatformServices _services;

        /// <summary>Il giocatore ha scelto di entrare nel mondo.</summary>
        public event Action<GameStart> Start;

        /// <summary>L'elenco delle partite è cambiato (es. eliminata l'ultima).</summary>
        public event Action SavesChanged;

        /// <summary>Stato nel cloud di una partita ("Nel cloud", "Solo su questo dispositivo"; vuoto senza account).</summary>
        public Func<string, Task<string>> CloudLabel;

        public GameMenus(GamePanel panel, IPlatformServices services)
        {
            _panel = panel;
            _services = services;
        }

        // ------------------------------------------------------------------ nuova partita

        public void OpenNewGame()
        {
            var start = new GameStart();
            _panel.Open(Loc.Get("newgame.title"));
            _panel.Section(Loc.Get("newgame.place"));
            _panel.Row(Loc.Get("newgame.city"), "Chieti");
            _panel.Text(Loc.Get("newgame.place.note"));

            _panel.Section(Loc.Get("newgame.name"));
            var name = new TextField { value = Loc.Get("newgame.default_name"), maxLength = 32, name = "newgame-name" };
            name.AddToClassList("panel-field");
            _panel.Content.Add(name);

            _panel.Section(Loc.Get("newgame.difficulty"));
            var difficulty = new VisualElement();
            difficulty.AddToClassList("segmented");
            difficulty.AddToClassList("panel-choice");
            var buttons = new List<Button>();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var b = new Button { name = "newgame-difficulty-" + d.ToString().ToLowerInvariant() };
                b.AddToClassList("segmented__item");
                var l = new Label(Loc.Get("newgame.difficulty." + d)) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("segmented__label");
                b.Add(l);
                var value = d;
                b.clicked += () =>
                {
                    _services.Haptics.Play(HapticKind.Selection);
                    start.Config.Difficulty = value;
                    foreach (var x in buttons) x.EnableInClassList("segmented__item--selected", x == b);
                };
                b.EnableInClassList("segmented__item--selected", d == start.Config.Difficulty);
                buttons.Add(b);
                difficulty.Add(b);
            }
            _panel.Content.Add(difficulty);
            _panel.Text(Loc.Get("newgame.difficulty.note"));

            _panel.Section(Loc.Get("newgame.options"));
            Switch("newgame.offline", "newgame.offline.note", start.Config.OfflineTime, v => start.Config.OfflineTime = v);
            Switch("newgame.creative", "newgame.creative.note", start.Config.CreativeMode, v => start.Config.CreativeMode = v);

            _panel.Footer(Loc.Get("newgame.start"), OrbisIconKind.Play, "primary", () =>
            {
                var n = (name.value ?? "").Trim();
                start.Name = n.Length > 0 ? n : Loc.Get("newgame.default_name");
                _panel.Close();
                Start?.Invoke(start);
            }).name = "newgame-start";
        }

        private void Switch(string labelKey, string noteKey, bool value, Action<bool> changed)
        {
            var row = new Button();
            row.AddToClassList("setting-row");
            row.AddToClassList("panel-switch");
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.AddToClassList("setting-row__texts");
            var label = new Label(Loc.Get(labelKey)) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("setting-row__label");
            var note = new Label(Loc.Get(noteKey)) { pickingMode = PickingMode.Ignore };
            note.AddToClassList("setting-row__detail");
            texts.Add(label);
            texts.Add(note);
            var sw = new VisualElement { pickingMode = PickingMode.Ignore };
            sw.AddToClassList("switch");
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("switch__knob");
            sw.Add(knob);
            sw.EnableInClassList("switch--on", value);
            row.Add(texts);
            row.Add(sw);
            row.clicked += () =>
            {
                _services.Haptics.Play(HapticKind.Selection);
                value = !value;
                sw.EnableInClassList("switch--on", value);
                changed(value);
            };
            _panel.Content.Add(row);
        }

        // ------------------------------------------------------------------ partite salvate

        public async void OpenSaves()
        {
            _panel.Open(Loc.Get("saves.title"));
            var loading = _panel.Text(Loc.Get("saves.loading"));
            List<SaveMeta> saves;
            try
            {
                saves = await SaveManager.ListAsync(_services.SaveStore);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] elenco salvataggi non disponibile: " + e.Message);
                saves = new List<SaveMeta>();
            }
            if (!_panel.IsOpen) return;
            loading.RemoveFromHierarchy();
            if (saves.Count == 0)
            {
                _panel.Text(Loc.Get("saves.empty"));
                return;
            }
            foreach (var s in saves)
            {
                var card = new VisualElement();
                card.AddToClassList("save-card");
                var title = new Label(s.Name);
                title.AddToClassList("save-card__name");
                var info = new Label(Loc.Format("saves.info", Fmt.DateTime(s.GameTime), Fmt.Money(s.Cash), s.Level,
                    Fmt.DateTime(s.SavedUtc.ToLocalTime())));
                info.AddToClassList("save-card__info");
                var cloud = new Label("");
                cloud.AddToClassList("save-card__cloud");
                if (CloudLabel != null) _ = FillAsync(cloud, CloudLabel(s.Id));
                var actions = new VisualElement();
                actions.AddToClassList("save-card__actions");
                var meta = s;
                GamePanel.ActionButton(actions, Loc.Get("saves.delete"), OrbisIconKind.Trash, "danger", () =>
                    _panel.Confirm(Loc.Get("saves.delete.title"), Loc.Format("saves.delete.text", meta.Name), Loc.Get("saves.delete"), async () =>
                    {
                        await SaveManager.DeleteAsync(_services.SaveStore, meta.Id);
                        SavesChanged?.Invoke();
                        OpenSaves();
                    }));
                GamePanel.ActionButton(actions, Loc.Get("saves.play"), OrbisIconKind.Play, "primary", () =>
                {
                    _panel.Close();
                    Start?.Invoke(new GameStart { SaveId = meta.Id, Name = meta.Name });
                }).name = "saves-play";
                card.Add(title);
                card.Add(info);
                card.Add(cloud);
                card.Add(actions);
                _panel.Content.Add(card);
            }
        }

        private static async Task FillAsync(Label label, Task<string> text)
        {
            try
            {
                label.text = await text;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Orbis] stato cloud non disponibile: " + e.Message);
            }
            label.style.display = string.IsNullOrEmpty(label.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>La partita più recente (per "Continua" con un solo salvataggio), null se non ce ne sono.</summary>
        public async Task<SaveMeta> LatestAsync()
        {
            var list = await SaveManager.ListAsync(_services.SaveStore);
            return list.Count > 0 ? list[0] : null;
        }

        // ------------------------------------------------------------------ tempo ad app chiusa

        public void ShowOfflineSummary(OfflineSummary s)
        {
            if (s == null) return;
            _panel.Open(Loc.Get("offline.title"));
            _panel.Text(Loc.Format("offline.text", Duration(s.RealElapsed), Duration(s.GameElapsed)));
            _panel.Row(Loc.Get("offline.income"), Fmt.Signed(s.Income), Math.Sign(s.Income));
            _panel.Row(Loc.Get("offline.expenses"), Fmt.Signed(s.Expenses), Math.Sign(s.Expenses));
            if (s.ConstructionsDone > 0) _panel.Row(Loc.Get("offline.built"), s.ConstructionsDone.ToString(Fmt.It));
            if (s.MissionsDone > 0) _panel.Row(Loc.Get("offline.missions"), s.MissionsDone.ToString(Fmt.It));
            if (s.LevelsGained > 0) _panel.Row(Loc.Get("offline.levels"), "+" + s.LevelsGained.ToString(Fmt.It));
            _panel.Footer(Loc.Get("offline.ok"), OrbisIconKind.Check, "primary", _panel.Close);
        }

        private static string Duration(TimeSpan t)
        {
            if (t.TotalDays >= 1) return Loc.Format("time.days", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return Loc.Format("time.hours", (int)t.TotalHours, t.Minutes);
            return Loc.Format("time.minutes", Math.Max(1, (int)t.TotalMinutes));
        }
    }
}

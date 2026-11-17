using System;
using System.Collections.Generic;
using System.Globalization;
using Orbis.UI.App;
using Orbis.UI.Elements;
using Orbis.UI.Localization;
using UnityEngine.UIElements;

namespace Orbis.UI.Game
{
    /// <summary>Formati dei numeri in italiano (monete, percentuali, date).</summary>
    public static class Fmt
    {
        public static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

        public static string Money(long v) => v.ToString("N0", It);
        public static string Signed(long v) => (v > 0 ? "+" : "") + v.ToString("N0", It);
        public static string Percent(float v) => v.ToString("0.0", It) + "%";
        public static string Date(DateTime d) => d.ToString("ddd d MMM yyyy", It);
        public static string DateTime(DateTime d) => d.ToString("ddd d MMM, HH:mm", It);
    }

    /// <summary>
    /// Pannello a scheda riutilizzabile (catalogo, finanze, riavvolgi, nuova partita, partite salvate):
    /// titolo, schede facoltative, contenuto scorrevole e pulsanti in fondo. Uno solo aperto alla volta.
    /// </summary>
    public sealed class GamePanel
    {
        private const string VisibleClass = "sheet-layer--visible";

        private readonly VisualElement _layer;
        private readonly Label _title;
        private readonly VisualElement _tabs;
        private readonly ScrollView _tabsScroll;
        private readonly ScrollView _scroll;
        private readonly VisualElement _footer;
        private IVisualElementScheduledItem _hide;

        public VisualElement Content { get; }
        public bool IsOpen { get; private set; }
        public event Action Closed;

        public GamePanel(VisualElement root)
        {
            _layer = MainMenuController.Require<VisualElement>(root, "panel");
            _title = MainMenuController.Require<Label>(root, "panel-title");
            _tabs = MainMenuController.Require<VisualElement>(root, "panel-tabs");
            _tabsScroll = MainMenuController.Require<ScrollView>(root, "panel-tabs-scroll");
            _scroll = MainMenuController.Require<ScrollView>(root, "panel-scroll");
            Content = MainMenuController.Require<VisualElement>(root, "panel-content");
            _footer = MainMenuController.Require<VisualElement>(root, "panel-footer");
            MainMenuController.Require<Button>(root, "panel-close").clicked += Close;
            MainMenuController.Require<VisualElement>(root, "panel-scrim").RegisterCallback<ClickEvent>(_ => Close());
            _layer.style.display = DisplayStyle.None;
            _layer.RegisterCallback<NavigationCancelEvent>(_ => Close());
            // schede: col tocco una riga che si trascina, senza barra; col mouse vanno a capo e si vedono tutte
            _tabsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabsScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        /// <summary>Con mouse e tastiera le schede vanno a capo invece di scorrere.</summary>
        public void SetPointerLayout(bool pointer)
        {
            _tabsScroll.mode = pointer ? ScrollViewMode.Vertical : ScrollViewMode.Horizontal;
            _tabsScroll.style.maxHeight = pointer ? new StyleLength(StyleKeyword.None) : new StyleLength(StyleKeyword.Null);
            _tabs.EnableInClassList("panel-tabs--wrap", pointer);
            _tabsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabsScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        public void Open(string title)
        {
            _hide?.Pause();
            Content.Clear();
            _tabs.Clear();
            _footer.Clear();
            _tabsScroll.style.display = DisplayStyle.None;
            _title.text = title;
            _scroll.scrollOffset = UnityEngine.Vector2.zero;
            _layer.style.display = DisplayStyle.Flex;
            _layer.schedule.Execute(() => _layer.AddToClassList(VisibleClass));
            IsOpen = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _layer.RemoveFromClassList(VisibleClass);
            _hide = _layer.schedule.Execute(() => { if (!IsOpen) _layer.style.display = DisplayStyle.None; }).StartingIn(260);
            Closed?.Invoke();
        }

        public void Clear()
        {
            Content.Clear();
            _footer.Clear();
        }

        /// <summary>Schede in alto (es. categorie del catalogo).</summary>
        public void Tabs(IList<(string key, string label)> tabs, string selected, Action<string> onSelect)
        {
            _tabs.Clear();
            _tabsScroll.style.display = DisplayStyle.Flex;
            foreach (var (key, label) in tabs)
            {
                var b = new Button { text = label };
                b.AddToClassList("panel-tab");
                b.EnableInClassList("panel-tab--selected", key == selected);
                string k = key;
                b.clicked += () => onSelect(k);
                _tabs.Add(b);
            }
        }

        public Label Section(string text, VisualElement parent = null)
        {
            var l = new Label(text);
            l.AddToClassList("panel-section");
            (parent ?? Content).Add(l);
            return l;
        }

        public Label Text(string text, VisualElement parent = null)
        {
            var l = new Label(text);
            l.AddToClassList("panel-text");
            (parent ?? Content).Add(l);
            return l;
        }

        public VisualElement Row(string label, string value, int sign = 0, VisualElement parent = null)
        {
            var row = new VisualElement();
            row.AddToClassList("panel-row");
            var l = new Label(label);
            l.AddToClassList("panel-row__label");
            var v = new Label(value);
            v.AddToClassList("panel-row__value");
            if (sign > 0) v.AddToClassList("panel-row__value--plus");
            if (sign < 0) v.AddToClassList("panel-row__value--minus");
            row.Add(l);
            row.Add(v);
            (parent ?? Content).Add(row);
            return row;
        }

        public static Button ActionButton(VisualElement parent, string label, OrbisIconKind icon, string style, Action onClick)
        {
            var b = new Button();
            b.AddToClassList("action-button");
            if (!string.IsNullOrEmpty(style)) b.AddToClassList("action-button--" + style);
            if (icon != OrbisIconKind.None)
            {
                var i = new OrbisIcon { icon = icon, pickingMode = PickingMode.Ignore };
                i.AddToClassList("action-button__icon");
                b.Add(i);
            }
            var l = new Label(label) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("action-button__label");
            b.Add(l);
            b.clicked += onClick;
            parent.Add(b);
            return b;
        }

        public Button Footer(string label, OrbisIconKind icon, string style, Action onClick) => ActionButton(_footer, label, icon, style, onClick);

        /// <summary>Chiede conferma (es. riavvolgi, elimina partita).</summary>
        public void Confirm(string title, string text, string confirmLabel, Action onConfirm)
        {
            Open(title);
            Text(text);
            Footer(Loc.Get("panel.cancel"), OrbisIconKind.Close, "", Close);
            Footer(confirmLabel, OrbisIconKind.Check, "danger", () =>
            {
                Close();
                onConfirm();
            });
        }
    }
}

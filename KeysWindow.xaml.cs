using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WTOPMBot.Models;

namespace WTOPMBot;

public partial class KeysWindow : Window
{
    private readonly KeyBindProfile _profile;
    private readonly List<(ShortcutPanels.KeyDef Key, ComboBox Combo)> _rows = [];
    private bool _suppress;
    private int _nextTargetAttempts = 3;
    private bool _approachEnabled;
    private int _approachIntervalMs = 1500;
    private int _lootAttempts = 3;
    private int _lootIntervalMs = 800;

    public event Action<KeyBindSaveData>? Saved;
    public event Action? ClosedByUser;
    public bool AllowClose { get; set; }

    public KeysWindow(
        KeyBindProfile profile,
        int nextTargetAttempts = 3,
        bool approachNextTargetEnabled = false,
        int approachNextTargetIntervalMs = 1500,
        int lootAttempts = 3,
        int lootIntervalMs = 800)
    {
        _profile = profile;
        _profile.EnsureComplete();
        _nextTargetAttempts = Math.Clamp(nextTargetAttempts, 0, 20);
        _approachEnabled = approachNextTargetEnabled;
        _approachIntervalMs = Math.Clamp(approachNextTargetIntervalMs, 300, 10000);
        _lootAttempts = Math.Clamp(lootAttempts, 1, 20);
        _lootIntervalMs = Math.Clamp(lootIntervalMs, 200, 10000);
        InitializeComponent();
        NextTargetAttemptsBox.Text = _nextTargetAttempts.ToString();
        ApproachNextCheck.IsChecked = _approachEnabled;
        ApproachIntervalBox.Text = _approachIntervalMs.ToString();
        LootAttemptsBox.Text = _lootAttempts.ToString();
        LootIntervalBox.Text = _lootIntervalMs.ToString();
        BuildPanels();
        Closing += OnClosing;
    }

    private void BuildPanels()
    {
        BuildPanel(Panel1Host, ShortcutPanels.Panel1);
        BuildPanel(Panel2Host, ShortcutPanels.Panel2);
        BuildPanel(Panel3Host, ShortcutPanels.Panel3);
    }

    private void BuildPanel(Panel host, IReadOnlyList<ShortcutPanels.KeyDef> keys)
    {
        host.Children.Clear();
        foreach (var key in keys)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyBadge = new Border
            {
                Background = (Brush)FindResource("FieldBgBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 0, 4, 0),
                Height = 30,
                Child = new TextBlock
                {
                    Text = key.Label,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = (Brush)FindResource("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(keyBadge, 0);

            var combo = new ComboBox
            {
                Height = 30,
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                Tag = key,
                ItemsSource = BotActionInfo.Assignable.Select(BotActionInfo.DisplayName).ToList()
            };
            var current = _profile.GetAction(key.Panel, key.Index);
            combo.SelectedItem = BotActionInfo.DisplayName(current);
            combo.SelectionChanged += Combo_OnSelectionChanged;
            Grid.SetColumn(combo, 1);

            row.Children.Add(keyBadge);
            row.Children.Add(combo);
            host.Children.Add(row);
            _rows.Add((key, combo));
        }
    }

    private void Combo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || sender is not ComboBox combo || combo.Tag is not ShortcutPanels.KeyDef key)
            return;

        var name = combo.SelectedItem as string ?? BotActionInfo.DisplayName(BotAction.None);
        var action = BotActionInfo.Assignable.FirstOrDefault(a => BotActionInfo.DisplayName(a) == name);
        _profile.SetAction(key.Panel, key.Index, action);

        // Next target can exist on only one slot — refresh other combos.
        if (action == BotAction.NextTarget)
            RefreshComboSelections();
    }

    private void RefreshComboSelections()
    {
        _suppress = true;
        foreach (var (key, combo) in _rows)
            combo.SelectedItem = BotActionInfo.DisplayName(_profile.GetAction(key.Panel, key.Index));
        _suppress = false;
    }

    private void ResetDefaultsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var defaults = KeyBindProfile.CreateDefault();
        _suppress = true;
        foreach (var (key, combo) in _rows)
        {
            var action = defaults.GetAction(key.Panel, key.Index);
            _profile.SetAction(key.Panel, key.Index, action);
            combo.SelectedItem = BotActionInfo.DisplayName(action);
        }
        _suppress = false;
        NextTargetAttemptsBox.Text = "3";
        ApproachNextCheck.IsChecked = false;
        ApproachIntervalBox.Text = "1500";
        LootAttemptsBox.Text = "3";
        LootIntervalBox.Text = "800";
        HintText.Text = "Сброшено: Таргет=3 · Атака=1 · Next×3 · Лут×3/800мс. Сохраните.";
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _profile.EnsureSingleNextTarget();
        RefreshComboSelections();

        if (!int.TryParse(NextTargetAttemptsBox.Text.Trim(), out var attempts))
            attempts = 3;
        attempts = Math.Clamp(attempts, 0, 20);
        NextTargetAttemptsBox.Text = attempts.ToString();
        _nextTargetAttempts = attempts;

        _approachEnabled = ApproachNextCheck.IsChecked == true;
        if (!int.TryParse(ApproachIntervalBox.Text.Trim(), out var interval))
            interval = 1500;
        interval = Math.Clamp(interval, 300, 10000);
        ApproachIntervalBox.Text = interval.ToString();
        _approachIntervalMs = interval;

        if (!int.TryParse(LootAttemptsBox.Text.Trim(), out var lootAttempts))
            lootAttempts = 3;
        lootAttempts = Math.Clamp(lootAttempts, 1, 20);
        LootAttemptsBox.Text = lootAttempts.ToString();
        _lootAttempts = lootAttempts;

        if (!int.TryParse(LootIntervalBox.Text.Trim(), out var lootInterval))
            lootInterval = 800;
        lootInterval = Math.Clamp(lootInterval, 200, 10000);
        LootIntervalBox.Text = lootInterval.ToString();
        _lootIntervalMs = lootInterval;

        var named = _profile.FindAllKeys(BotAction.SearchMob).Count;
        var next = _profile.FindKey(BotAction.NextTarget)?.Label ?? "—";
        var loot = _profile.FindKey(BotAction.Loot)?.Label ?? "—";
        var road = _approachEnabled ? $" · дорога Next/{_approachIntervalMs}мс" : "";
        HintText.Text =
            $"Сохранено · ников: {named} · Next: {next} × {attempts}{road} · Лут: {loot} × {_lootAttempts}/{_lootIntervalMs}мс";

        Saved?.Invoke(new KeyBindSaveData
        {
            Profile = _profile,
            NextTargetAttempts = _nextTargetAttempts,
            ApproachNextTargetEnabled = _approachEnabled,
            ApproachNextTargetIntervalMs = _approachIntervalMs,
            LootAttempts = _lootAttempts,
            LootIntervalMs = _lootIntervalMs
        });
        Hide();
        ClosedByUser?.Invoke();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AllowClose)
            return;

        e.Cancel = true;
        Hide();
        ClosedByUser?.Invoke();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        if (e.OriginalSource is DependencyObject src &&
            (FindVisualParent<Button>(src) is not null || FindVisualParent<CheckBox>(src) is not null))
            return;
        DragMove();
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) =>
        Close();
}

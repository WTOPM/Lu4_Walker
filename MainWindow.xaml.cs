using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WTOPMBot.Models;
using WTOPMBot.Services;
using WTOPMBot.Services.Input;

namespace WTOPMBot;

public partial class MainWindow : Window
{
    private static readonly Brush DangerBrush = (Brush)Application.Current.Resources["DangerBrush"];
    private static readonly Brush MutedBrush = (Brush)Application.Current.Resources["MutedBrush"];
    private static readonly Brush SuccessBrush = (Brush)Application.Current.Resources["SuccessBrush"];

    private GlobalHotkeys? _hotkeys;
    private AppSettings _settings = new();
    private StatusBarProfile _barProfile = StatusBarProfileStore.CreateDefault();
    private DispatcherTimer? _sampleTimer;
    private AttackLoop? _attackLoop;
    private IKeyInput? _keyInput;
    private MonitorWindow? _monitor;
    private KeysWindow? _keysWindow;
    private nint _sampleHwnd;
    private bool _isRunning;
    private int? _selectedPid;
    private bool _isCapturing;
    private bool _suppressInputUiEvents;
    private bool _playerBarsCalibrated;
    private bool _targetBarCalibrated;
    private string _calibrationHint = "калибровка…";

    public MainWindow()
    {
        _suppressInputUiEvents = true;
        InitializeComponent();
        StopButton.IsEnabled = false;
        Loaded += OnLoaded;
        Closed += OnClosed;
        RebuildKeyInput();
        _suppressInputUiEvents = false;
    }

    private string CapturesDirectory =>
        Path.Combine(AppContext.BaseDirectory, "captures");

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        _barProfile = StatusBarProfileStore.LoadOrCreateDefault();
        StatusBarProfileStore.EnsureDeathSkullProbe(_barProfile);
        _playerBarsCalibrated = HasBar(_barProfile, "cp") && HasBar(_barProfile, "hp") && HasBar(_barProfile, "mp");
        _targetBarCalibrated = HasBar(_barProfile, "target_hp");
        if (!_barProfile.AutoCalibrated)
        {
            _playerBarsCalibrated = false;
            _targetBarCalibrated = false;
        }

        _suppressInputUiEvents = true;
        TopmostCheck.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        AutoAttackCheck.IsChecked = _settings.AutoAttackEnabled;
        _suppressInputUiEvents = false;

        RefreshTeensyPorts(selectPreferred: true);
        AttachHotkeys();
        RefreshProcesses(keepSelection: false);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopSampling();
        PersistUiSettings();
        _hotkeys?.Dispose();
        _keyInput?.Dispose();
        if (_monitor is not null)
        {
            _monitor.AllowClose = true;
            _monitor.Close();
            _monitor = null;
        }

        if (_keysWindow is not null)
        {
            _keysWindow.AllowClose = true;
            _keysWindow.Close();
            _keysWindow = null;
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;
        // Only title bar is wired — never steal focus from inputs.
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

    private void AttachHotkeys()
    {
        _hotkeys?.Dispose();
        _hotkeys = new GlobalHotkeys(this);
        _hotkeys.StartPressed += () => Dispatcher.BeginInvoke(StartBot);
        _hotkeys.StopPressed += () => Dispatcher.BeginInvoke(StopBot);
        _hotkeys.ScreenshotPressed += () => Dispatcher.BeginInvoke(async () => await CaptureScreenshotAsync());
        _hotkeys.Attach();

        HotkeyHintText.Text = $"PgUp/PgDn · Ins=скрин · {_hotkeys.DescribeStatus()}";
    }

    private void TopmostCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheck.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
        SettingsStore.Save(_settings);
    }

    private void AutoAttackCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressInputUiEvents)
            return;

        _settings.AutoAttackEnabled = AutoAttackCheck.IsChecked == true;
        if (_attackLoop is not null)
            _attackLoop.Enabled = _settings.AutoAttackEnabled;
        SettingsStore.Save(_settings);
    }

    private void RebuildKeyInput()
    {
        _keyInput?.Dispose();

        _settings.InputBackend = "Teensy";
        var port = TeensyPortCombo?.SelectedItem as string
                   ?? _settings.TeensyComPort
                   ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(port))
            _settings.TeensyComPort = port;

        _keyInput = new TeensyKeyInput(_settings.TeensyComPort);
        UpdateTeensyStatusLabel();

        _settings.KeyBinds ??= KeyBindProfile.CreateDefault();
        _settings.KeyBinds.EnsureComplete();
        _attackLoop = new AttackLoop(_keyInput)
        {
            Enabled = AutoAttackCheck?.IsChecked == true,
            TargetHwnd = _sampleHwnd,
            Binds = _settings.KeyBinds,
            NextTargetAttempts = Math.Clamp(_settings.NextTargetAttempts, 0, 20),
            ApproachNextTargetEnabled = _settings.ApproachNextTargetEnabled,
            ApproachNextTargetIntervalMs = Math.Clamp(_settings.ApproachNextTargetIntervalMs, 300, 10000),
            LootAttempts = Math.Clamp(_settings.LootAttempts, 1, 20),
            LootIntervalMs = Math.Clamp(_settings.LootIntervalMs, 200, 10000)
        };
    }

    private void RefreshTeensyButton_OnClick(object sender, RoutedEventArgs e) =>
        RefreshTeensyPorts(selectPreferred: true);

    private void TeensyPortCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInputUiEvents)
            return;

        if (TeensyPortCombo.SelectedItem is string port && !string.IsNullOrWhiteSpace(port))
        {
            _settings.TeensyComPort = port;
            _settings.InputBackend = "Teensy";
            SettingsStore.Save(_settings);
            RebuildKeyInput();
        }
    }

    private void RefreshTeensyPorts(bool selectPreferred)
    {
        _suppressInputUiEvents = true;
        try
        {
            var ports = System.IO.Ports.SerialPort.GetPortNames()
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TeensyPortCombo.ItemsSource = ports;

            // Prefer a port that actually answers PING→PONG (real Teensy).
            var live = TeensyKeyInput.FindLivePort(ports);
            string? selected = null;

            if (live is not null)
                selected = live;
            else if (selectPreferred && !string.IsNullOrWhiteSpace(_settings.TeensyComPort))
                selected = ports.FirstOrDefault(p =>
                    string.Equals(p, _settings.TeensyComPort, StringComparison.OrdinalIgnoreCase));

            selected ??= ports.FirstOrDefault(p =>
                !string.Equals(p, "COM1", StringComparison.OrdinalIgnoreCase));
            selected ??= ports.FirstOrDefault();

            TeensyPortCombo.SelectedItem = selected;
            if (!string.IsNullOrWhiteSpace(selected))
                _settings.TeensyComPort = selected;
        }
        finally
        {
            _suppressInputUiEvents = false;
        }

        RebuildKeyInput();
    }

    private void UpdateTeensyStatusLabel()
    {
        if (TeensyStatusText is null)
            return;

        if (_keyInput is TeensyKeyInput teensy)
        {
            TeensyStatusText.Text = teensy.IsReady
                ? $"Teensy готов · {teensy.Name} (PING OK)"
                : $"Teensy не готов · {teensy.LastError ?? "прошейте Serial+Keyboard+Mouse"}";
            TeensyStatusText.Foreground = teensy.IsReady ? SuccessBrush : DangerBrush;
            return;
        }

        TeensyStatusText.Text = "Ожидание Teensy…";
        TeensyStatusText.Foreground = MutedBrush;
    }

    private void MonitorButton_OnClick(object sender, RoutedEventArgs e) => OpenMonitor();

    private void KeysButton_OnClick(object sender, RoutedEventArgs e) => OpenKeys();

    private void OpenKeys()
    {
        _settings.KeyBinds ??= KeyBindProfile.CreateDefault();
        _settings.KeyBinds.EnsureComplete();

        if (_keysWindow is not null)
        {
            _keysWindow.Saved -= OnKeyBindsSaved;
            _keysWindow.AllowClose = true;
            _keysWindow.Close();
            _keysWindow = null;
        }

        _keysWindow = new KeysWindow(
            _settings.KeyBinds,
            _settings.NextTargetAttempts,
            _settings.ApproachNextTargetEnabled,
            _settings.ApproachNextTargetIntervalMs,
            _settings.LootAttempts,
            _settings.LootIntervalMs) { Owner = this };
        _keysWindow.Saved += OnKeyBindsSaved;
        _keysWindow.Show();
        _keysWindow.Activate();
    }

    private void OnKeyBindsSaved(KeyBindSaveData data)
    {
        _settings.KeyBinds = data.Profile;
        _settings.NextTargetAttempts = Math.Clamp(data.NextTargetAttempts, 0, 20);
        _settings.ApproachNextTargetEnabled = data.ApproachNextTargetEnabled;
        _settings.ApproachNextTargetIntervalMs = Math.Clamp(data.ApproachNextTargetIntervalMs, 300, 10000);
        _settings.LootAttempts = Math.Clamp(data.LootAttempts, 1, 20);
        _settings.LootIntervalMs = Math.Clamp(data.LootIntervalMs, 200, 10000);
        if (_attackLoop is not null)
        {
            _attackLoop.Binds = data.Profile;
            _attackLoop.NextTargetAttempts = _settings.NextTargetAttempts;
            _attackLoop.ApproachNextTargetEnabled = _settings.ApproachNextTargetEnabled;
            _attackLoop.ApproachNextTargetIntervalMs = _settings.ApproachNextTargetIntervalMs;
            _attackLoop.LootAttempts = _settings.LootAttempts;
            _attackLoop.LootIntervalMs = _settings.LootIntervalMs;
            _attackLoop.Reset();
        }

        SettingsStore.Save(_settings);
    }

    private void OpenMonitor()
    {
        if (_monitor is null)
        {
            _monitor = new MonitorWindow
            {
                Owner = this,
                SampleX = _settings.SampleX,
                SampleY = _settings.SampleY
            };
        }

        if (!_monitor.IsVisible)
            _monitor.Show();
        _monitor.Activate();
    }

    private void RefreshButton_OnClick(object sender, RoutedEventArgs e) =>
        RefreshProcesses(keepSelection: true);

    private void SaveProcessButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProcessCombo.SelectedItem is not ProcessEntry selected)
        {
            SetStatus("Сначала выберите процесс.", DangerBrush);
            return;
        }

        _settings.PreferredProcessName = selected.Name;
        _settings.PreferredWindowTitle = selected.Title;
        PersistUiSettings();
        SetStatus($"По умолчанию: {selected.Name}", SuccessBrush);
        UpdateSaveButtonVisual(true);
    }

    private void ProcessCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessCombo.SelectedItem is ProcessEntry entry)
        {
            _selectedPid = entry.Id;
            UpdateSaveButtonVisual(IsPreferred(entry));
            if (!_isRunning)
                SetStatus($"Выбран: {entry.Name} (PID {entry.Id})", MutedBrush);
        }
        else
        {
            UpdateSaveButtonVisual(false);
        }
    }

    private void StartButton_OnClick(object sender, RoutedEventArgs e) => StartBot();
    private void StopButton_OnClick(object sender, RoutedEventArgs e) => StopBot();
    private async void CaptureButton_OnClick(object sender, RoutedEventArgs e) =>
        await CaptureScreenshotAsync();

    private void StartBot()
    {
        if (_isCapturing || _isRunning)
            return;

        if (ProcessCombo.SelectedItem is not ProcessEntry selected)
        {
            SetStatus("Выберите процесс.", DangerBrush);
            return;
        }

        var live = ResolveLiveEntry(selected);
        if (live is null)
        {
            SetStatus("Процесс недоступен. Обновите список.", DangerBrush);
            RefreshProcesses(keepSelection: false);
            return;
        }

        _sampleHwnd = live.MainWindowHandle;
        PersistUiSettings();
        RefreshTeensyPorts(selectPreferred: true);

        if (_keyInput is not { IsReady: true })
        {
            SetStatus(
                $"Teensy не готов: {_keyInput?.LastError ?? "выберите COM3 (не COM1)"}",
                DangerBrush);
            return;
        }

        _attackLoop?.Reset();

        // Always re-find bars on start — display settings may have shifted.
        _playerBarsCalibrated = false;
        _targetBarCalibrated = false;
        _calibrationHint = "калибровка…";
        _barProfile.AutoCalibrated = false;
        _barProfile.Bars = [];

        SetRunning(true);
        StartSampling();
        SampleOnce();
        SetStatus("Калибровка полосок…", MutedBrush);
    }

    private void StopBot()
    {
        if (_isCapturing)
            return;

        if (!_isRunning)
        {
            SetStatus("Уже остановлено.", MutedBrush);
            return;
        }

        StopSampling();
        SetRunning(false);
        SetStatus("Остановлено.", MutedBrush);
    }

    private void StartSampling()
    {
        StopSampling();
        _sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _sampleTimer.Tick += SampleTimer_OnTick;
        _sampleTimer.Start();
    }

    private void StopSampling()
    {
        if (_sampleTimer is null)
            return;
        _sampleTimer.Stop();
        _sampleTimer.Tick -= SampleTimer_OnTick;
        _sampleTimer = null;
    }

    private void SampleTimer_OnTick(object? sender, EventArgs e) => SampleOnce();

    private void SampleOnce()
    {
        if (!_isRunning)
            return;

        if (_selectedPid is int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.MainWindowHandle != nint.Zero)
                    _sampleHwnd = process.MainWindowHandle;
            }
            catch
            {
                StopBot();
                SetStatus("Процесс закрыт.", DangerBrush);
                return;
            }
        }

        if (!_playerBarsCalibrated || !_targetBarCalibrated)
            TryAutoCalibrateBars();

        if (_playerBarsCalibrated || _targetBarCalibrated)
            UpdateCombatAndBars();
        else
            SetStatus($"Калибровка… {_calibrationHint}", MutedBrush);

        UpdatePixelProbe();
    }

    private void TryAutoCalibrateBars()
    {
        if (!StatusBarAutoCalibrator.TryCalibrate(_sampleHwnd, out var found, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                SetStatus(error, DangerBrush);
            else
                _calibrationHint = found.Summary;

            if (_monitor is { IsVisible: true })
                _monitor.SetHint($"Калибровка: {_calibrationHint}");
            return;
        }

        var changed = false;

        if (!_playerBarsCalibrated && found.PlayerOk)
        {
            StatusBarAutoCalibrator.ApplyToProfile(_barProfile, new StatusBarAutoCalibrator.CalibrationResult
            {
                PlayerOk = true,
                Cp = found.Cp,
                Hp = found.Hp,
                Mp = found.Mp
            });
            _playerBarsCalibrated = true;
            changed = true;
        }

        if (!_targetBarCalibrated && found.TargetOk)
        {
            StatusBarAutoCalibrator.ApplyToProfile(_barProfile, new StatusBarAutoCalibrator.CalibrationResult
            {
                TargetOk = true,
                TargetHp = found.TargetHp
            });
            _targetBarCalibrated = true;
            changed = true;
        }

        _calibrationHint = found.Summary;

        if (_playerBarsCalibrated && _targetBarCalibrated)
            _barProfile.AutoCalibrated = true;

        if (changed)
            StatusBarProfileStore.Save(_barProfile);

        if (_monitor is { IsVisible: true })
        {
            _monitor.SetHint(
                _playerBarsCalibrated && _targetBarCalibrated
                    ? $"Автокалибровка OK · {_calibrationHint}"
                    : $"Калибровка: {_calibrationHint}" +
                      (!_playerBarsCalibrated ? " · ждут CP/HP/MP" : "") +
                      (!_targetBarCalibrated ? " · ждут TGT (возьми моба)" : ""));
        }
    }

    private void UpdateCombatAndBars()
    {
        if (!StatusBarReader.TryReadAll(_sampleHwnd, _barProfile, out var readings, out var error))
        {
            if (!string.IsNullOrWhiteSpace(error))
                SetStatus(error, DangerBrush);
            return;
        }

        var cp = readings.FirstOrDefault(r => r.Id.Equals("cp", StringComparison.OrdinalIgnoreCase));
        var hp = readings.FirstOrDefault(r => r.Id.Equals("hp", StringComparison.OrdinalIgnoreCase));
        var mp = readings.FirstOrDefault(r => r.Id.Equals("mp", StringComparison.OrdinalIgnoreCase));
        var tgt = readings.FirstOrDefault(r => r.Id.Equals("target_hp", StringComparison.OrdinalIgnoreCase));

        if (!PixelProbeReader.TryReadAll(_sampleHwnd, _barProfile, out var probes, out var probeError))
        {
            if (!string.IsNullOrWhiteSpace(probeError))
                SetStatus(probeError, DangerBrush);
            return;
        }

        var combat = PixelProbeReader.BuildCombatState(probes, tgt);

        if (_attackLoop is not null)
        {
            _attackLoop.TargetHwnd = _sampleHwnd;
            _attackLoop.Tick(combat);
        }

        if (_monitor is { IsVisible: true })
        {
            _monitor.SetBars(
                cp?.FillPercent ?? 0,
                hp?.FillPercent ?? 0,
                mp?.FillPercent ?? 0,
                FormatBarPercent(cp),
                FormatBarPercent(hp),
                FormatBarPercent(mp),
                FormatTargetPercent(tgt),
                tgt is { Detected: true } ? tgt.FillPercent : 0);
            _monitor.SetCombat(combat);
            _monitor.SetHint(
                $"Окно: {StatusBarReader.LastClientWidth}×{StatusBarReader.LastClientHeight} · " +
                $"авто · {_calibrationHint}");
        }

        UpdateLiveStatus(combat, cp, hp, mp, tgt);
    }

    private void UpdateLiveStatus(
        CombatState combat,
        StatusBarReading? cp,
        StatusBarReading? hp,
        StatusBarReading? mp,
        StatusBarReading? tgt)
    {
        var situation = combat.TargetDead
            ? "Цель: труп (череп)"
            : combat.TargetAlive
                ? $"Цель: есть · HP {FormatTargetPercent(tgt)}"
                : "Цель: нет";

        string actionLine;
        string phaseLine;
        if (_attackLoop is null || !_attackLoop.Enabled)
        {
            actionLine = "Автодействия выключены";
            phaseLine = "Включите галочку «Автодействия»";
        }
        else
        {
            var actionName = BotActionInfo.DisplayName(_attackLoop.PlannedAction);
            var namedCount = _settings.KeyBinds.FindAllKeys(BotAction.SearchMob).Count;
            var cycle = namedCount > 0
                ? $" · ник {_attackLoop.NamedIndex + 1}/{namedCount} · next {_attackLoop.NextTargetAttempt}/{_attackLoop.NextTargetAttempts}"
                : "";
            if (_attackLoop.IsLooting)
                cycle += $" · лут {_attackLoop.LootAttempt}/{_attackLoop.LootAttempts}";
            actionLine =
                $"Сейчас: {actionName} · [{_attackLoop.LastKeyLabel}] · нажатий {_attackLoop.PressCount}{cycle}";
            phaseLine = _attackLoop.Phase;
        }

        var barsLine =
            $"Свои CP {FormatBarPercent(cp)} · HP {FormatBarPercent(hp)} · MP {FormatBarPercent(mp)}";

        SetStatus(situation, actionLine, phaseLine, barsLine, SuccessBrush);
    }

    private void UpdatePixelProbe()
    {
        if (_monitor is not { IsVisible: true })
            return;

        if (!_monitor.TryGetCoordinates(out var x, out var y))
        {
            _monitor.ClearPixel();
            return;
        }

        if (!WindowCaptureService.TryReadClientPixel(_sampleHwnd, x, y, out var r, out var g, out var b, out _))
        {
            _monitor.ClearPixel();
            return;
        }

        _monitor.SetPixel(r, g, b);
    }

    private async Task CaptureScreenshotAsync()
    {
        if (_isCapturing)
            return;

        if (ProcessCombo.SelectedItem is not ProcessEntry selected)
        {
            SetStatus("Для скрина выберите процесс.", DangerBrush);
            return;
        }

        var live = ResolveLiveEntry(selected);
        if (live is null)
        {
            SetStatus("Процесс недоступен.", DangerBrush);
            RefreshProcesses(keepSelection: false);
            return;
        }

        _isCapturing = true;
        CaptureButton.IsEnabled = false;
        SetStatus("Скриншот…", MutedBrush);

        var previousOpacity = Opacity;
        var wasTopmost = Topmost;
        var monitorWasVisible = _monitor?.IsVisible == true;
        Topmost = false;
        Hide();
        if (monitorWasVisible)
            _monitor!.Hide();

        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            var path = await Task.Run(() => WindowCaptureService.CaptureToBmp(live, CapturesDirectory));
            SetStatus($"BMP: {Path.GetFileName(path)}", SuccessBrush);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, DangerBrush);
        }
        finally
        {
            Opacity = previousOpacity;
            Show();
            if (monitorWasVisible)
                _monitor!.Show();
            Topmost = wasTopmost || TopmostCheck.IsChecked == true;
            _isCapturing = false;
            CaptureButton.IsEnabled = true;
        }
    }

    private void RefreshProcesses(bool keepSelection)
    {
        var previousPid = keepSelection ? _selectedPid : null;
        var processes = ProcessEnumerator.GetWindowedProcesses();
        ProcessCombo.ItemsSource = processes;

        ProcessEntry? selected = null;
        if (previousPid is int pid)
            selected = processes.FirstOrDefault(p => p.Id == pid);
        selected ??= FindPreferred(processes);

        if (selected is not null)
        {
            ProcessCombo.SelectedItem = selected;
            _selectedPid = selected.Id;
            UpdateSaveButtonVisual(IsPreferred(selected));
            if (!_isRunning)
            {
                SetStatus(
                    IsPreferred(selected)
                        ? $"По умолчанию: {selected.Name}"
                        : $"Выбран: {selected.Name}",
                    IsPreferred(selected) ? SuccessBrush : MutedBrush);
            }
            return;
        }

        ProcessCombo.SelectedItem = null;
        _selectedPid = null;
        UpdateSaveButtonVisual(false);
        if (!_isRunning)
            SetStatus("Процесс не найден. Выберите из списка.", DangerBrush);
    }

    private ProcessEntry? FindPreferred(IReadOnlyList<ProcessEntry> processes)
    {
        if (string.IsNullOrWhiteSpace(_settings.PreferredProcessName))
            return null;

        var byName = processes
            .Where(p => string.Equals(p.Name, _settings.PreferredProcessName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(_settings.PreferredWindowTitle))
        {
            var byTitle = byName.FirstOrDefault(p =>
                string.Equals(p.Title, _settings.PreferredWindowTitle, StringComparison.OrdinalIgnoreCase));
            if (byTitle is not null)
                return byTitle;
        }

        return byName[0];
    }

    private bool IsPreferred(ProcessEntry entry) =>
        !string.IsNullOrWhiteSpace(_settings.PreferredProcessName) &&
        string.Equals(entry.Name, _settings.PreferredProcessName, StringComparison.OrdinalIgnoreCase);

    private void UpdateSaveButtonVisual(bool isSavedSelection)
    {
        SaveProcessButton.Foreground = isSavedSelection
            ? (Brush)Application.Current.Resources["AccentBrush"]
            : (Brush)Application.Current.Resources["TextBrush"];
    }

    private static ProcessEntry? ResolveLiveEntry(ProcessEntry selected)
    {
        try
        {
            using var process = Process.GetProcessById(selected.Id);
            if (process.MainWindowHandle == nint.Zero)
                return null;

            return new ProcessEntry
            {
                Id = process.Id,
                Name = process.ProcessName,
                Title = process.MainWindowTitle?.Trim() ?? selected.Title,
                MainWindowHandle = process.MainWindowHandle
            };
        }
        catch
        {
            return null;
        }
    }

    private void PersistUiSettings()
    {
        if (_monitor is not null)
        {
            _settings.SampleX = _monitor.SampleX;
            _settings.SampleY = _monitor.SampleY;
        }

        _settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        _settings.AutoAttackEnabled = AutoAttackCheck.IsChecked == true;
        _settings.InputBackend = "Teensy";
        if (TeensyPortCombo?.SelectedItem is string port && !string.IsNullOrWhiteSpace(port))
            _settings.TeensyComPort = port;
        SettingsStore.Save(_settings);
    }

    private static bool HasBar(StatusBarProfile profile, string id) =>
        profile.Bars.Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && (b.HasPercents || b.X2 > b.X1));

    private static string FormatBarPercent(StatusBarReading? reading)
    {
        if (reading is null) return "—";
        if (!reading.Detected) return "0%";
        return $"{reading.FillPercent:0}%";
    }

    private static string FormatTargetPercent(StatusBarReading? reading)
    {
        if (reading is null) return "—";
        if (!reading.Detected) return "нет";
        return $"{reading.FillPercent:0}%";
    }

    private void SetRunning(bool running)
    {
        _isRunning = running;
        ProcessCombo.IsEnabled = !running;
        RefreshButton.IsEnabled = !running;
        SaveProcessButton.IsEnabled = !running;
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        AutoAttackCheck.IsEnabled = !running;
        StatusDot.Fill = running ? SuccessBrush : MutedBrush;
    }

    private void SetStatus(string message, Brush dotBrush) =>
        SetStatus(message, "", "", "", dotBrush);

    private void SetStatus(string situation, string action, string phase, string bars, Brush dotBrush)
    {
        StatusSituationText.Text = situation;
        StatusActionText.Text = action;
        StatusPhaseText.Text = phase;
        StatusBarsText.Text = bars;
        StatusDot.Fill = _isRunning ? SuccessBrush : dotBrush;
    }
}

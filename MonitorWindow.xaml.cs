using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WTOPMBot.Models;

namespace WTOPMBot;

public partial class MonitorWindow : Window
{
    private static readonly Brush DangerBrush = (Brush)Application.Current.Resources["DangerBrush"];
    private static readonly Brush MutedBrush = (Brush)Application.Current.Resources["MutedBrush"];
    private static readonly Brush SuccessBrush = (Brush)Application.Current.Resources["SuccessBrush"];

    private double _cpFill;
    private double _hpFill;
    private double _mpFill;
    private double _targetHpFill;

    public event Action? ClosedByUser;
    public bool AllowClose { get; set; }

    public MonitorWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public int SampleX
    {
        get => int.TryParse(CoordXBox.Text.Trim(), out var x) ? x : 0;
        set => CoordXBox.Text = value.ToString();
    }

    public int SampleY
    {
        get => int.TryParse(CoordYBox.Text.Trim(), out var y) ? y : 0;
        set => CoordYBox.Text = value.ToString();
    }

    public bool TryGetCoordinates(out int x, out int y)
    {
        var xOk = int.TryParse(CoordXBox.Text.Trim(), out x);
        var yOk = int.TryParse(CoordYBox.Text.Trim(), out y);
        return xOk && yOk;
    }

    public void SetPixel(byte r, byte g, byte b)
    {
        ColorRBox.Text = r.ToString();
        ColorGBox.Text = g.ToString();
        ColorBBox.Text = b.ToString();
        ColorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    public void ClearPixel()
    {
        ColorRBox.Text = ColorGBox.Text = ColorBBox.Text = "—";
    }

    public void SetBars(double cp, double hp, double mp, string cpText, string hpText, string mpText, string tgtText, double tgtFill)
    {
        _cpFill = cp;
        _hpFill = hp;
        _mpFill = mp;
        _targetHpFill = tgtFill;
        CpPercentText.Text = cpText;
        HpPercentText.Text = hpText;
        MpPercentText.Text = mpText;
        TargetHpPercentText.Text = tgtText;
        ApplyBarVisuals();
    }

    public void SetCombat(CombatState combat)
    {
        TargetSelectedText.Text = combat.TargetSelected ? "Цель: да" : "Цель: нет";
        TargetSelectedDot.Fill = combat.TargetSelected ? SuccessBrush : MutedBrush;

        TargetDeadText.Text = !combat.TargetSelected
            ? "Мёртв: —"
            : combat.TargetDead ? "Мёртв: да" : "Мёртв: нет";
        TargetDeadDot.Fill = combat.TargetDead ? DangerBrush : MutedBrush;
    }

    public void SetHint(string text) => BarsHintText.Text = text;

    private void MonitorTopmostCheck_OnChanged(object sender, RoutedEventArgs e)
    {
        Topmost = MonitorTopmostCheck.IsChecked == true;
    }

    private void BarTrack_OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyBarVisuals();

    private void ApplyBarVisuals()
    {
        SetFillWidth(CpFill, CpTrack, _cpFill);
        SetFillWidth(HpFill, HpTrack, _hpFill);
        SetFillWidth(MpFill, MpTrack, _mpFill);
        SetFillWidth(TargetHpFill, TargetHpTrack, _targetHpFill);
    }

    private static void SetFillWidth(System.Windows.Controls.Border fill, System.Windows.Controls.Border track, double percent)
    {
        var width = track.ActualWidth;
        if (width <= 0 || double.IsNaN(width))
            return;
        fill.Width = Math.Clamp(width * (percent / 100.0), 0, width);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AllowClose)
            return;

        // Hide instead of destroy so MainWindow can reopen quickly.
        e.Cancel = true;
        Hide();
        ClosedByUser?.Invoke();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) =>
        Close();
}


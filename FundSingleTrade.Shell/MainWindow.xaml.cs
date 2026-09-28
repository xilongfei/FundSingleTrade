using FundSingleTrade.Shell.Data;
using FundSingleTrade.Shell.Services;
using FundSingleTrade.Shell.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FundSingleTrade.Shell;

/// <summary>主窗口负责绑定视图模型及呈现 ScottPlot 净值图表。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private FundQuote[] _visibleQuotes = [];
    private double[] _chartValues = [];

    /// <summary>初始化页面、数据绑定及图表交互事件。</summary>
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new ConfirmationDialogService());
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.SelectedFund) or
                nameof(MainViewModel.PeriodStart) or nameof(MainViewModel.PeriodEnd) or
                nameof(MainViewModel.ThemeMode))
                DrawChart();
        };
        _viewModel.SelectedQuotes.CollectionChanged += (_, _) => DrawChart();
        QuotePlot.SizeChanged += (_, _) => DrawChart();
        QuotePlot.AddHandler(UIElement.MouseMoveEvent,
            new MouseEventHandler(QuotePlot_MouseMove), true);
        QuotePlot.MouseLeave += (_, _) => ChartHoverCard.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => DrawChart();
    }

    /// <summary>按所选日期筛选每日净值并绘制区间累计涨跌幅。</summary>
    private void DrawChart()
    {
        if (!IsLoaded)
            return;

        _visibleQuotes = _viewModel.SelectedQuotes
            .Where(q => q.QuoteDate.Date >= _viewModel.PeriodStart.Date &&
                        q.QuoteDate.Date <= _viewModel.PeriodEnd.Date)
            .GroupBy(q => q.QuoteDate.Date)
            .Select(group => group.OrderByDescending(q => q.Id).First())
            .OrderBy(q => q.QuoteDate)
            .ToArray();
        _chartValues = [];
        ChartHoverCard.Visibility = Visibility.Collapsed;
        QuotePlot.Plot.Clear();

        if (_visibleQuotes.Length == 0)
        {
            QuotePlot.Refresh();
            return;
        }

        _chartValues = CalculateCumulativeChanges(_visibleQuotes);
        var xs = _visibleQuotes.Select(q => q.QuoteDate.ToOADate()).ToArray();
        var color = ScottPlot.Color.FromHex(_chartValues[^1] >= 0 ? "#DC2626" : "#16A34A");
        var series = QuotePlot.Plot.Add.Scatter(xs, _chartValues);
        series.Color = color;
        series.LineWidth = 2;
        series.MarkerSize = 0;
        // 每条净值单独添加标记，避免 ScottPlot 大数据优化跳过部分点。
        for (var i = 0; i < xs.Length; i++)
        {
            var marker = QuotePlot.Plot.Add.Marker(xs[i], _chartValues[i]);
            marker.Shape = ScottPlot.MarkerShape.FilledCircle;
            marker.Size = 8;
            marker.Color = color;
        }

        var zeroLine = QuotePlot.Plot.Add.HorizontalLine(0);
        zeroLine.Color = ScottPlot.Color.FromHex("#98A2B3");
        zeroLine.LineWidth = 1;
        zeroLine.LinePattern = ScottPlot.LinePattern.Dashed;

        QuotePlot.Plot.Axes.DateTimeTicksBottom();
        QuotePlot.Plot.Axes.Left.Label.Text = "涨跌幅（%）";
        QuotePlot.Plot.Axes.Left.Label.FontSize = 14;
        QuotePlot.Plot.Axes.Left.Label.FontName = "微软雅黑";
        QuotePlot.Plot.Axes.Left.TickLabelStyle.FontName = "微软雅黑";
        QuotePlot.Plot.Axes.AutoScale();
        ApplyChartPalette();
        QuotePlot.Refresh();
    }

    /// <summary>计算以区间首个有效净值为基准的累计涨跌幅序列。</summary>
    private static double[] CalculateCumulativeChanges(IReadOnlyList<FundQuote> quotes)
    {
        var firstValue = quotes[0].NetValue;
        return quotes.Select(q => firstValue == 0
            ? 0
            : (double)((q.NetValue / firstValue - 1) * 100)).ToArray();
    }

    /// <summary>使 ScottPlot 画布背景与当前明暗主题保持一致。</summary>
    private void ApplyChartPalette()
    {
        var dark = _viewModel.ThemeMode == "深色" ||
                   (_viewModel.ThemeMode == "跟随系统" && ThemeManager.IsSystemDarkMode());
        var background = ScottPlot.Color.FromHex(dark ? "#1F2937" : "#FFFFFF");
        QuotePlot.Plot.FigureBackground.Color = background;
        QuotePlot.Plot.DataBackground.Color = background;
        var foreground = ScottPlot.Color.FromHex(dark ? "#D1D5DB" : "#344054");
        QuotePlot.Plot.Axes.Color(foreground);
        QuotePlot.Plot.Axes.FrameColor(foreground);
    }

    /// <summary>鼠标靠近某个净值点时显示该日涨幅和区间累计涨幅。</summary>
    private void QuotePlot_MouseMove(object sender, MouseEventArgs args)
    {
        if (_visibleQuotes.Length == 0)
        {
            ChartHoverCard.Visibility = Visibility.Collapsed;
            return;
        }

        var cursor = QuotePlot.GetPlotPixelPosition(args);
        var nearest = Enumerable.Range(0, _visibleQuotes.Length)
            .Select(i =>
            {
                var pixel = QuotePlot.Plot.GetPixel(new ScottPlot.Coordinates(
                    _visibleQuotes[i].QuoteDate.ToOADate(), _chartValues[i]));
                var dx = pixel.X - cursor.X;
                var dy = pixel.Y - cursor.Y;
                return (Index: i, Distance: Math.Sqrt(dx * dx + dy * dy));
            })
            .MinBy(point => point.Distance);
        if (nearest.Distance > 26)
        {
            ChartHoverCard.Visibility = Visibility.Collapsed;
            return;
        }

        var quote = _visibleQuotes[nearest.Index];
        var previous = _viewModel.SelectedQuotes
            .Where(q => q.QuoteDate.Date < quote.QuoteDate.Date)
            .OrderByDescending(q => q.QuoteDate)
            .FirstOrDefault();
        var dailyChange = previous is null || previous.NetValue == 0
            ? "暂无前一交易日净值"
            : $"{(quote.NetValue / previous.NetValue - 1) * 100:+0.00;-0.00;0.00}%";
        ChartHoverText.Text =
            $"{quote.QuoteDate:yyyy-MM-dd}\n当日涨幅：{dailyChange}\n区间累计涨幅：{_chartValues[nearest.Index]:+0.00;-0.00;0.00}%\n单位净值：{quote.NetValue:0.0000}";
        ChartHoverCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardWidth = ChartHoverCard.DesiredSize.Width;
        var cardHeight = ChartHoverCard.DesiredSize.Height;
        var pointer = args.GetPosition(ChartOverlay);
        var left = Math.Clamp(pointer.X + 14, 4,
            Math.Max(4, ChartOverlay.ActualWidth - cardWidth - 4));
        var top = Math.Clamp(pointer.Y + 14, 4,
            Math.Max(4, ChartOverlay.ActualHeight - cardHeight - 4));
        Canvas.SetLeft(ChartHoverCard, left);
        Canvas.SetTop(ChartHoverCard, top);
        ChartHoverCard.Visibility = Visibility.Visible;
    }
}

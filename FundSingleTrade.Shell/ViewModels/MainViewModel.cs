using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FundSingleTrade.Shell.Data;
using FundSingleTrade.Shell.Services;
using Microsoft.EntityFrameworkCore;

namespace FundSingleTrade.Shell.ViewModels;

/// <summary>主题强调色候选项。</summary>
public sealed record AccentChoice(string Name, string Hex);

/// <summary>显示单只基金及其所选日期区间的涨跌幅。</summary>
public partial class FundListItem : ObservableObject
{
    [ObservableProperty] private Fund fund = null!;
    [ObservableProperty] private decimal? periodChange;
    [ObservableProperty] private DateTime periodStart;
    [ObservableProperty] private DateTime periodEnd;
    public string DisplayName => $"{Fund.Name}  ({Fund.Code})";
    public string ChangeText => PeriodChange is null ? "暂无区间数据" : $"{PeriodChange:+0.00;-0.00;0.00}%";
    public Brush ChangeBrush => Application.Current?.Resources[
        PeriodChange is < 0 ? "NegativeChangeBrush" : "PositiveChangeBrush"] as Brush
        ?? (PeriodChange is < 0 ? Brushes.ForestGreen : Brushes.Firebrick);
    public string PeriodText => $"{PeriodStart:MM-dd} 至 {PeriodEnd:MM-dd}";
    partial void OnPeriodChangeChanged(decimal? value)
    {
        OnPropertyChanged(nameof(ChangeText));
        OnPropertyChanged(nameof(ChangeBrush));
    }
    public void RefreshAppearance() => OnPropertyChanged(nameof(ChangeBrush));
    partial void OnPeriodStartChanged(DateTime value) => OnPropertyChanged(nameof(PeriodText));
    partial void OnPeriodEndChanged(DateTime value) => OnPropertyChanged(nameof(PeriodText));
}

/// <summary>将交易本金、估值日净值和持有收益组织为表格行。</summary>
public sealed class TradeSummary
{
    public int TradeId { get; init; }
    public DateTime TradeDate { get; init; }
    public decimal Amount { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? LatestReturnPercent { get; init; }
    public decimal? LatestProfit { get; init; }
    public DateTime? ValuationDate { get; init; }
    public string ReturnText => LatestReturnPercent is null ? "缺少买入净值/最新净值" : $"{LatestReturnPercent:+0.00;-0.00;0.00}%";
    public string ProfitText => LatestProfit is null ? "—" : $"{LatestProfit:+#,##0.00;-#,##0.00;0.00}";
    public string ValuationText => ValuationDate?.ToString("yyyy-MM-dd") ?? "—";
}

/// <summary>主窗口视图模型，管理基金、交易、图表区间和用户偏好。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FundDataService _dataService = new();
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly UserPreferences _preferences;
    private readonly SemaphoreSlim _preferencesSaveLock = new(1, 1);
    private bool _isReloadingFunds;
    private bool _suppressSelectionSync;

    public ObservableCollection<FundListItem> Funds { get; } = new();
    public ObservableCollection<FundQuote> SelectedQuotes { get; } = new();
    public ObservableCollection<TradeSummary> SelectedTrades { get; } = new();
    public IReadOnlyList<string> ThemeModes { get; } = ["跟随系统", "浅色", "深色"];
    public IReadOnlyList<AccentChoice> AccentChoices { get; } =
    [
        new("蓝色", "#2563EB"),
        new("靛蓝", "#4F46E5"),
        new("紫色", "#7C3AED"),
        new("青色", "#0891B2"),
        new("绿色", "#16A34A"),
        new("橙色", "#EA580C"),
        new("玫红", "#DB2777")
    ];

    [ObservableProperty] private FundListItem? selectedFund;
    [ObservableProperty] private DateTime periodStart = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime periodEnd = DateTime.Today.AddDays(-1);
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "准备就绪";
    [ObservableProperty] private string newFundCode = string.Empty;
    [ObservableProperty] private DateTime newTradeDate = DateTime.Today;
    [ObservableProperty] private decimal newTradeAmount;
    [ObservableProperty] private string themeMode;
    [ObservableProperty] private string accentColor;

    public string ValuationDateText => SelectedQuotes.Where(x => x.QuoteDate.Date <= DateTime.Today.AddDays(-1))
        .OrderByDescending(x => x.QuoteDate).Select(x => x.QuoteDate.ToString("yyyy-MM-dd")).FirstOrDefault() ?? "暂无净值";

    public MainViewModel(IConfirmationDialogService confirmationDialogService)
    {
        _confirmationDialogService = confirmationDialogService;
        // 启动时读回与数据库同目录的主题记忆，首次运行则采用默认值。
        _preferences = UserPreferencesStore.Load();
        themeMode = _preferences.ThemeMode;
        accentColor = _preferences.AccentColor;
        SelectedQuotes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ValuationDateText));
        _ = InitializeAsync();
    }

    /// <summary>主题选择变化时立即更新界面并保存偏好。</summary>
    partial void OnThemeModeChanged(string value)
    {
        ApplyAppearance();
        foreach (var fund in Funds)
            fund.RefreshAppearance();
    }

    /// <summary>强调色选择变化时立即更新界面并保存偏好。</summary>
    partial void OnAccentColorChanged(string value) => ApplyAppearance();

    /// <summary>立即应用当前外观设置，并串行保存到本地。</summary>
    private void ApplyAppearance()
    {
        _preferences.ThemeMode = ThemeMode;
        _preferences.AccentColor = AccentColor;
        ThemeManager.Apply(_preferences);
        _ = SaveAppearancePreferencesAsync();
    }

    /// <summary>串行保存偏好，避免快速切换时多个文件写入互相覆盖。</summary>
    private async Task SaveAppearancePreferencesAsync()
    {
        await _preferencesSaveLock.WaitAsync();
        try
        {
            await UserPreferencesStore.SaveAsync(_preferences);
            StatusMessage = "界面外观偏好已保存。";
        }
        catch (IOException ex)
        {
            StatusMessage = $"界面外观已应用，但设置文件保存失败：{ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"界面外观已应用，但没有权限保存设置：{ex.Message}";
        }
        finally
        {
            _preferencesSaveLock.Release();
        }
    }

    /// <summary>切换基金时同步更新图表净值和交易汇总。</summary>
    partial void OnSelectedFundChanged(FundListItem? value)
    {
        SelectedQuotes.Clear();
        SelectedTrades.Clear();
        OnPropertyChanged(nameof(ValuationDateText));
        if (value is null) return;
        PeriodStart = value.PeriodStart;
        PeriodEnd = value.PeriodEnd;
        foreach (var quote in value.Fund.Quotes.OrderBy(x => x.QuoteDate))
            SelectedQuotes.Add(quote);
        UpdateTradeSummaries(value.Fund);
        if (!_isReloadingFunds && !_suppressSelectionSync)
            _ = EnsureSelectedFundCurrentAsync(value);
    }

    /// <summary>启动时只检查并同步当前选中的基金，不批量请求其他基金。</summary>
    private async Task InitializeAsync()
    {
        await LoadAsync();
        if (SelectedFund is not null)
            await EnsureSelectedFundCurrentAsync(SelectedFund);
    }

    /// <summary>验证基金代码并创建记录，随后自动同步基金名称和行情。</summary>
    [RelayCommand]
    private async Task AddFundAsync()
    {
        var code = NewFundCode.Trim();
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            StatusMessage = "请输入 6 位基金代码。";
            return;
        }
        await using var db = new FundDbContext();
        if (await db.Funds.AnyAsync(x => x.Code == code))
        {
            StatusMessage = "该基金已经在列表中。";
            return;
        }
        var fund = new Fund
        {
            Code = code,
            Name = code
        };
        db.Funds.Add(fund);
        await db.SaveChangesAsync();
        NewFundCode = string.Empty;
        await LoadAsync();
        _suppressSelectionSync = true;
        try
        {
            SelectedFund = Funds.FirstOrDefault(x => x.Fund.Id == fund.Id);
        }
        finally
        {
            _suppressSelectionSync = false;
        }
        if (SelectedFund is not null)
            await SyncFundAsync(SelectedFund);
    }

    /// <summary>仅当净值落后于昨天且今天尚未同步时自动更新当前基金。</summary>
    private async Task EnsureSelectedFundCurrentAsync(FundListItem fund)
    {
        var targetDate = DateTime.Today.AddDays(-1);
        var latestQuoteDate = fund.Fund.Quotes
            .Where(quote => quote.QuoteDate.Date <= targetDate)
            .Select(quote => (DateTime?)quote.QuoteDate.Date)
            .Max();
        if (latestQuoteDate >= targetDate ||
            fund.Fund.LastSyncedAt?.Date == DateTime.Today)
            return;

        await SyncFundAsync(fund);
    }

    /// <summary>同步指定基金截至昨天的净值，不触发其他基金的数据请求。</summary>
    private async Task SyncFundAsync(FundListItem selectedFund)
    {
        var selectedId = selectedFund.Fund.Id;
        var targetDate = DateTime.Today.AddDays(-1);
        IsBusy = true;
        try
        {
            var fundName = await _dataService.GetFundNameAsync(selectedFund.Fund.Code, CancellationToken.None);
            // 多取区间开始日前的净值，便于计算区间首日涨幅和非交易日边界。
            var fetchStart = selectedFund.PeriodStart.Date.AddDays(-15);
            var earliestTrade = selectedFund.Fund.Trades.Select(x => x.TradeDate.Date).DefaultIfEmpty(fetchStart).Min();
            fetchStart = earliestTrade < fetchStart ? earliestTrade.AddDays(-15) : fetchStart;
            var remote = await _dataService.GetQuotesAsync(selectedFund.Fund.Code, fetchStart, targetDate, CancellationToken.None);
            await using var db = new FundDbContext();
            var fund = await db.Funds.Include(x => x.Quotes).Include(x => x.Trades)
                .SingleAsync(x => x.Id == selectedId);
            if (!string.IsNullOrWhiteSpace(fundName))
                fund.Name = fundName;
            foreach (var item in remote)
            {
                var quote = fund.Quotes.FirstOrDefault(x => x.QuoteDate == item.Date);
                if (quote is null)
                    fund.Quotes.Add(new FundQuote { QuoteDate = item.Date, NetValue = item.NetValue, AccumulatedValue = item.AccumulatedValue });
                else
                {
                    quote.NetValue = item.NetValue;
                    quote.AccumulatedValue = item.AccumulatedValue;
                }
            }
            fund.LastSyncedAt = DateTime.Now;
            await db.SaveChangesAsync();
            await LoadAsync();
            OnPropertyChanged(nameof(ValuationDateText));
            StatusMessage = $"已同步“{fund.Name}”的名称和 {remote.Count} 条净值记录。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>二次确认后删除基金及其关联交易、净值数据。</summary>
    [RelayCommand]
    private async Task DeleteSelectedFundAsync()
    {
        if (SelectedFund is null)
        {
            StatusMessage = "请先选择要删除的基金。";
            return;
        }
        var fund = SelectedFund.Fund;
        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "确认删除基金",
            $"确定删除“{fund.Name}（{fund.Code}）”及其所有净值和交易记录吗？此操作无法撤销。");
        if (!confirmed)
            return;

        await using var db = new FundDbContext();
        db.Funds.Remove(await db.Funds.SingleAsync(x => x.Id == fund.Id));
        await db.SaveChangesAsync();
        SelectedFund = null;
        await LoadAsync();
        StatusMessage = $"已删除基金“{fund.Name}”及其相关记录。";
    }

    /// <summary>根据交易日期净值计算份额并保存单笔买入记录。</summary>
    [RelayCommand]
    private async Task AddTradeAsync()
    {
        if (SelectedFund is null || NewTradeAmount <= 0)
        {
            StatusMessage = "请选择基金并输入大于 0 的交易金额。";
            return;
        }
        var selectedId = SelectedFund.Fund.Id;
        var selectedFund = SelectedFund.Fund;
        decimal? unitPrice = selectedFund.Quotes
            .Where(x => x.QuoteDate.Date <= NewTradeDate.Date &&
                        x.QuoteDate.Date >= NewTradeDate.Date.AddDays(-15))
            .OrderByDescending(x => x.QuoteDate)
            .Select(x => (decimal?)x.NetValue)
            .FirstOrDefault();
        if (unitPrice is null)
        {
            var earliestQuoteDate = NewTradeDate.Date.AddDays(-15);
            try
            {
                var purchaseQuotes = await _dataService.GetQuotesAsync(
                    selectedFund.Code, earliestQuoteDate, NewTradeDate.Date, CancellationToken.None);
                unitPrice = purchaseQuotes.OrderByDescending(x => x.Date)
                    .Select(x => (decimal?)x.NetValue).FirstOrDefault();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or
                                       System.Text.Json.JsonException or TaskCanceledException)
            {
                StatusMessage = $"获取交易日净值失败：{ex.Message}";
                return;
            }
        }
        if (unitPrice is null or <= 0)
        {
            StatusMessage = "无法取得交易日净值，请先检查交易日期或同步基金净值。";
            return;
        }
        await using var db = new FundDbContext();
        db.Trades.Add(new Trade
        {
            FundId = selectedId,
            TradeDate = NewTradeDate.Date,
            Amount = NewTradeAmount,
            UnitPrice = unitPrice,
            Shares = NewTradeAmount / unitPrice.Value
        });
        await db.SaveChangesAsync();
        NewTradeAmount = 0;
        await LoadAsync();
        SelectedFund = Funds.First(x => x.Fund.Id == selectedId);
        StatusMessage = "交易记录已保存，正在更新最新估值。";
        if (SelectedFund is not null)
            await SyncFundAsync(SelectedFund);
    }

    /// <summary>删除选中的单笔交易并刷新基金详情。</summary>
    [RelayCommand]
    private async Task DeleteTradeAsync(TradeSummary? trade)
    {
        if (trade is null || SelectedFund is null)
        {
            StatusMessage = "请选择要删除的交易记录。";
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "确认删除交易",
            $"确定删除 {trade.TradeDate:yyyy-MM-dd} 的交易记录（金额 {trade.Amount:N2}）吗？");
        if (!confirmed)
            return;

        var fundId = SelectedFund.Fund.Id;
        await using var db = new FundDbContext();
        var record = await db.Trades.SingleOrDefaultAsync(x => x.Id == trade.TradeId && x.FundId == fundId);
        if (record is null)
        {
            StatusMessage = "该交易记录已不存在，请刷新后重试。";
            return;
        }

        db.Trades.Remove(record);
        await db.SaveChangesAsync();
        await LoadAsync();
        SelectedFund = Funds.FirstOrDefault(x => x.Fund.Id == fundId);
        StatusMessage = "交易记录已删除。";
    }

    /// <summary>从本地数据库载入基金、分类关联、净值和交易记录。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var previousRanges = Funds.ToDictionary(
            item => item.Fund.Id,
            item => (item.PeriodStart, item.PeriodEnd));
        var selectedFundId = SelectedFund?.Fund.Id;
        _isReloadingFunds = true;
        try
        {
            await using var db = new FundDbContext();
            await db.Database.EnsureCreatedAsync();
            var funds = await db.Funds.Include(x => x.Quotes).Include(x => x.Trades).Include(x => x.Category)
                .OrderBy(x => x.Name).ToListAsync();
            SelectedFund = null;
            Funds.Clear();
            foreach (var fund in funds)
            {
                var range = previousRanges.TryGetValue(fund.Id, out var previousRange)
                    ? previousRange
                    : (DateTime.Today.AddMonths(-1), DateTime.Today.AddDays(-1));
                var item = new FundListItem
                {
                    Fund = fund,
                    PeriodStart = range.Item1,
                    PeriodEnd = range.Item2
                };
                item.PeriodChange = CalculateChange(fund.Quotes, item.PeriodStart, item.PeriodEnd);
                Funds.Add(item);
            }
            SelectedFund = Funds.FirstOrDefault(item => item.Fund.Id == selectedFundId)
                           ?? Funds.FirstOrDefault();
        }
        finally
        {
            _isReloadingFunds = false;
        }
    }

    /// <summary>仅更新当前基金的起始日期和区间涨幅。</summary>
    partial void OnPeriodStartChanged(DateTime value) => RecalculateSelectedFundChange();
    /// <summary>仅更新当前基金的结束日期和区间涨幅。</summary>
    partial void OnPeriodEndChanged(DateTime value) => RecalculateSelectedFundChange();

    private void RecalculateSelectedFundChange()
    {
        if (SelectedFund is null)
            return;

        SelectedFund.PeriodStart = PeriodStart;
        SelectedFund.PeriodEnd = PeriodEnd;
        SelectedFund.PeriodChange = CalculateChange(
            SelectedFund.Fund.Quotes, SelectedFund.PeriodStart, SelectedFund.PeriodEnd);
    }

    private void UpdateTradeSummaries(Fund fund)
    {
        // 使用最近已同步的净值估算每笔买入的当前收益，不将估算当作正式结算净值。
        SelectedTrades.Clear();
        var latest = fund.Quotes.Where(x => x.QuoteDate.Date <= DateTime.Today)
            .OrderByDescending(x => x.QuoteDate).FirstOrDefault();
        foreach (var trade in fund.Trades.OrderByDescending(x => x.TradeDate))
        {
            var purchasePrice = trade.UnitPrice ?? fund.Quotes
                .Where(x => x.QuoteDate.Date <= trade.TradeDate.Date &&
                            x.QuoteDate.Date >= trade.TradeDate.Date.AddDays(-15))
                .OrderByDescending(x => x.QuoteDate)
                .Select(x => (decimal?)x.NetValue)
                .FirstOrDefault();
            var shares = trade.Shares ?? (purchasePrice is > 0 ? trade.Amount / purchasePrice.Value : 0);
            decimal? change = purchasePrice is > 0 && latest is not null
                ? (latest.NetValue / purchasePrice.Value - 1) * 100
                : null;
            decimal? profit = purchasePrice is > 0 && latest is not null
                ? shares * latest.NetValue - trade.Amount
                : null;
            SelectedTrades.Add(new TradeSummary
            {
                TradeId = trade.Id,
                TradeDate = trade.TradeDate,
                Amount = trade.Amount,
                UnitPrice = purchasePrice,
                LatestReturnPercent = change,
                LatestProfit = profit,
                ValuationDate = latest?.QuoteDate
            });
        }
    }

    private static decimal? CalculateChange(IEnumerable<FundQuote> quotes, DateTime start, DateTime end)
    {
        var range = quotes.Where(x => x.QuoteDate.Date >= start.Date && x.QuoteDate.Date <= end.Date)
            .OrderBy(x => x.QuoteDate).ToList();
        if (range.Count < 2 || range[0].NetValue == 0) return null;
        return (range[^1].NetValue / range[0].NetValue - 1) * 100;
    }
}

namespace FundSingleTrade.Shell.Data;

/// <summary>基金分类数据及其基金集合。</summary>
public sealed class FundCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Fund> Funds { get; set; } = new List<Fund>();
}

/// <summary>单只基金的代码、分类、行情及交易数据。</summary>
public sealed class Fund
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public FundCategory? Category { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<FundQuote> Quotes { get; set; } = new List<FundQuote>();
}

/// <summary>基金的一笔买入交易及计算后的份额信息。</summary>
public sealed class Trade
{
    public int Id { get; set; }
    public int FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public DateTime TradeDate { get; set; }
    public decimal Amount { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Shares { get; set; }
    public string Note { get; set; } = string.Empty;
}

/// <summary>基金在某个交易日发布的单位净值。</summary>
public sealed class FundQuote
{
    public int Id { get; set; }
    public int FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public DateTime QuoteDate { get; set; }
    public decimal NetValue { get; set; }
    public decimal? AccumulatedValue { get; set; }
}

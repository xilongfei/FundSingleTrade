using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace FundSingleTrade.Shell.Services;

/// <summary>远程基金净值经过解析后的单日数据。</summary>
public sealed record RemoteQuote(DateTime Date, decimal NetValue, decimal? AccumulatedValue);

/// <summary>调用东方财富公开接口查询基金资料和历史净值。</summary>
public sealed class FundDataService
{
    private readonly HttpClient _httpClient = new();
    private const int PageSize = 20;

    /// <summary>按基金代码查询基金中文名称。</summary>
    public async Task<string?> GetFundNameAsync(string code, CancellationToken cancellationToken)
    {
        var url = $"https://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx?m=1&key={Uri.EscapeDataString(code)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri("https://fund.eastmoney.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"基金名称查询接口返回 HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。",
                null, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("Datas", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("基金名称查询接口返回格式无法识别。");

        foreach (var result in results.EnumerateArray())
        {
            if (GetString(result, "CODE") == code)
                return GetString(result, "NAME");
        }
        return null;
    }

    /// <summary>按页下载指定日期范围的历史净值。</summary>
    public async Task<IReadOnlyList<RemoteQuote>> GetQuotesAsync(
        string code, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        if (startDate.Date > endDate.Date)
            throw new ArgumentException("开始日期不能晚于结束日期。");

        var result = new List<RemoteQuote>();
        var pageCount = 1;
        for (var page = 1; page <= pageCount; page++)
        {
            var url = $"https://api.fund.eastmoney.com/f10/lsjz?fundCode={Uri.EscapeDataString(code)}" +
                      $"&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}" +
                      $"&pageIndex={page}&pageSize={PageSize}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://fund.eastmoney.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"基金接口返回 HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。",
                    null, response.StatusCode);

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(ExtractJson(text));
            var root = document.RootElement;
            if (root.TryGetProperty("ErrMsg", out var errorMessage) &&
                errorMessage.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(errorMessage.GetString()))
                throw new InvalidOperationException($"基金接口错误：{errorMessage.GetString()}");
            if (!root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"基金接口未返回数据（基金代码 {code}，第 {page} 页）。请检查基金代码或稍后重试。");
            if (!data.TryGetProperty("LSJZList", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("基金接口响应中缺少历史净值列表。");

            if (page == 1 && root.TryGetProperty("TotalCount", out var totalCount) &&
                totalCount.TryGetInt32(out var count))
                pageCount = (int)Math.Ceiling(count / (double)PageSize);

            foreach (var row in rows.EnumerateArray())
            {
                var dateText = GetString(row, "FSRQ");
                var netValueText = GetString(row, "DWJZ");
                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    !decimal.TryParse(netValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var netValue))
                    continue;
                decimal? accumulated = decimal.TryParse(GetString(row, "LJJZ"), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var value) ? value : null;
                result.Add(new RemoteQuote(date.Date, netValue, accumulated));
            }
        }

        if (result.Count == 0)
            throw new InvalidOperationException(
                $"基金接口没有返回有效净值数据（基金代码 {code}）。请确认代码正确，或稍后重试。");

        return result;
    }

    /// <summary>从响应内容中提取 JSON 对象，兼容接口的 JSONP 包装。</summary>
    private static string ExtractJson(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("基金接口返回内容不是有效 JSON。");
        return response[start..(end + 1)];
    }

    /// <summary>安全读取指定 JSON 字符串属性。</summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

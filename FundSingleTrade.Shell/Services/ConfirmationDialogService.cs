using MaterialDesignThemes.Wpf;

namespace FundSingleTrade.Shell.Services;

/// <summary>确认弹窗中显示的标题和说明。</summary>
public sealed record ConfirmationDialogModel(string Title, string Message);

/// <summary>通过 Material Design DialogHost 显示主题化的确认弹窗。</summary>
public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    /// <summary>显示确认/取消弹窗，只有明确确认才返回 true。</summary>
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var result = await DialogHost.Show(new ConfirmationDialogModel(title, message), "RootDialog");
        return result is true || result is string value && bool.TryParse(value, out var confirmed) && confirmed;
    }
}

/// <summary>向视图模型提供异步确认操作。</summary>
public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
}

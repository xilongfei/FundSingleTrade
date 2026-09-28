using FundSingleTrade.Shell.Data;
using System.Windows;

namespace FundSingleTrade.Shell;

/// <summary>应用入口；启动时读取用户设置并应用主题。</summary>
public partial class App : Application
{
    /// <summary>初始化持久化主题后启动主窗口。</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Apply(UserPreferencesStore.Load());
        base.OnStartup(e);
    }
}
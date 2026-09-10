using System.Windows;
using System.Windows.Threading;

namespace DLSSGManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureCreated();
        AppPaths.Log($"===== 启动 DLSSG 30 系管理器 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

        // A stray exception in a click handler should surface, not silently kill the window.
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppPaths.Log("未处理异常: " + args.ExceptionObject);

        base.OnStartup(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppPaths.Log("界面异常: " + e.Exception);
        MessageBox.Show(
            "操作过程中出现未处理的错误：\n\n" + e.Exception.Message + "\n\n详细信息已写入日志。",
            "DLSSG 30 系管理器", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

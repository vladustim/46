using System.Diagnostics;
using System.Reflection;

namespace PostService.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        Application.Run(new PostingList());
    }

    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        object error = e.ExceptionObject;
        while (error is TargetInvocationException { InnerException: { } inner })
        {
            error = inner;
        }

        Debug.WriteLine($"[{DateTimeOffset.Now:O}] {error}");
        MessageBox.Show(
            "Сталася непередбачена помилка. Застосунок буде закрито.",
            "Помилка PostService", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

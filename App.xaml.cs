using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LogViewer2;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = ApplyUpdateAsync(e.Args[1], e.Args[2]);
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task ApplyUpdateAsync(string targetPath, string processIdText)
    {
        try
        {
            if (int.TryParse(processIdText, out var processId))
            {
                try
                {
                    using var oldProcess = Process.GetProcessById(processId);
                    await oldProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (ArgumentException) { }
                catch (TimeoutException) { throw new IOException("The previous LogViewer instance did not close in time."); }
            }

            var sourcePath = Environment.ProcessPath ?? throw new IOException("Unable to locate the downloaded update.");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? throw new IOException("Invalid application path."));
            Exception? lastError = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try { File.Copy(sourcePath, targetPath, true); lastError = null; break; }
                catch (IOException ex) { lastError = ex; await Task.Delay(250); }
                catch (UnauthorizedAccessException ex) { lastError = ex; await Task.Delay(250); }
            }
            if (lastError is not null) throw lastError;
            Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LogViewer update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Shutdown(); }
    }
}

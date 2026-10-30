using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using System.IO;

namespace Samy;

public partial class App : Application
{
    private Window? _window;
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAMY");

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Startup failed", ex);
            ShowRecoveryWindow(ex);
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteDiagnostic("Unhandled WinUI exception", e.Exception);
        // Keep WinUI's normal crash semantics. The diagnostic file survives the process exit.
    }

    private void ShowRecoveryWindow(Exception error)
    {
        try
        {
            var message = new TextBlock
            {
                Text = "SAMY couldn't finish starting. Your local data has not been deleted.\n\n" +
                       error.GetType().Name + ": " + error.Message +
                       "\n\nA diagnostic log was saved in %LOCALAPPDATA%\\SAMY\\startup-error.log.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 241, 250)),
                Margin = new Thickness(24)
            };
            var panel = new StackPanel { Spacing = 12, MaxWidth = 620 };
            panel.Children.Add(new TextBlock
            {
                Text = "SAMY startup recovery",
                FontSize = 25,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 215, 165, 244))
            });
            panel.Children.Add(message);
            var openFolder = new Button
            {
                Content = "Open diagnostic folder",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            openFolder.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DataDirectory) { UseShellExecute = true }); }
                catch { }
            };
            panel.Children.Add(openFolder);
            var root = new Grid
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 11, 13, 18)),
                Padding = new Thickness(24)
            };
            root.Children.Add(panel);
            _window = new Window { Title = "SAMY — startup recovery", Content = root };
            _window.Activate();
        }
        catch
        {
            // Last-resort logging still leaves a useful file for support.
        }
    }

    private static void WriteDiagnostic(string heading, Exception error)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.AppendAllText(
                Path.Combine(DataDirectory, "startup-error.log"),
                $"[{DateTimeOffset.Now:O}] {heading}{Environment.NewLine}{error}{Environment.NewLine}{new string('-', 72)}{Environment.NewLine}");
        }
        catch { /* Startup diagnostics must never prevent the recovery window. */ }
    }
}

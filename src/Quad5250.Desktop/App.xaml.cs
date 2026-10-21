using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace Quad5250.Desktop;

public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must be first: during install, update and uninstall Velopack starts
        // the exe with hook arguments, and Run() handles them and exits.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the terminal alive: a failed send or render shouldn't take the
        // whole session down with it.
        MessageBox.Show(e.Exception.Message, "Quad 5250", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

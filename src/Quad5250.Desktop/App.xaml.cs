using System.Windows;
using System.Windows.Threading;

namespace Quad5250.Desktop;

public partial class App : Application
{
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the terminal alive: a failed send or render shouldn't take the
        // whole session down with it.
        MessageBox.Show(e.Exception.Message, "Quad 5250", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

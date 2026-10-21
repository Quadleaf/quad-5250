using System.ComponentModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Quad5250.Protocol.DataStream;
using Quad5250.Protocol.Input;
using Quad5250.Protocol.Screen;
using Quad5250.Protocol.Session;
using Quad5250.Rendering;

namespace Quad5250.Desktop;

public partial class MainWindow : Window
{
    private Tn5250Session? _session;
    private (string Host, int Port)? _activeHost;
    // Credentials to auto-fill on the first sign-on screen after connecting.
    private SavedCredentials? _pendingFill;
    private AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();

        HostBox.Text = _settings.Host;
        PortBox.Text = _settings.Port.ToString();
        ThemeBox.ItemsSource = TerminalTheme.All.Select(t => t.Name).ToList();
        ThemeBox.SelectedItem = TerminalTheme.FromName(_settings.Theme).Name;

        Terminal.PreviewKeyDown += OnTerminalKeyDown;
        Terminal.TextInput += OnTerminalTextInput;
        Terminal.CellClicked += (row, col) => Edit(e => e.SetCursor(row, col));
        Terminal.FunctionKeyClicked += aid => _ = PressAidAsync(aid);

        Loaded += (_, _) => (string.IsNullOrEmpty(HostBox.Text) ? (UIElement)HostBox : Terminal).Focus();
        Loaded += async (_, _) => ShowUpdate(await _updates.DownloadAsync());

        var version = typeof(MainWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;
        // Drop the "+commit" suffix the SDK appends.
        if (version is not null) Title = $"Quad 5250 {version.Split('+')[0]}";
    }

    // ---- Updates ---------------------------------------------------------

    private readonly AppUpdates _updates = new();

    private void ShowUpdate(string? version)
    {
        if (version is null) return;
        UpdateButton.Content = $"Restart to update ({version})";
        UpdateButton.Visibility = Visibility.Visible;
    }

    private void OnApplyUpdate(object sender, RoutedEventArgs e)
    {
        // Velopack stops every process running from the install folder while
        // it swaps versions, including an MCP server Claude has open.
        var mcpRunning = System.Diagnostics.Process.GetProcessesByName("Quad5250.Mcp").Length > 0;
        var warnings = new List<string>();
        if (_session is { IsConnected: true })
            warnings.Add("Restarting ends the current 5250 session. Sign off first if you're in the middle of something.");
        if (mcpRunning)
            warnings.Add("Claude's 5250 connection (the MCP server) will also stop; reconnect it in Claude afterwards (/mcp).");

        if (warnings.Count > 0 &&
            MessageBox.Show(this, string.Join("\n\n", warnings) + "\n\nRestart now?",
                "Quad 5250", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _updates.ApplyAndRestart();
    }

    // ---- Connection ------------------------------------------------------

    private void OnHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnConnect(sender, e);
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0) { ShowMessage("Enter a host."); HostBox.Focus(); return; }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            ShowMessage("Port must be 1-65535."); PortBox.Focus(); return;
        }

        _settings = _settings with { Host = host, Port = port };
        _settings.Save();

        await CloseSessionAsync();

        var session = new Tn5250Session(host, port);
        // Events arrive on the session's read thread; ignore any from a
        // session that has since been replaced.
        session.ScreenChanged += () => Dispatcher.BeginInvoke(() => { if (_session == session) OnHostScreen(); });
        session.Disconnected += () => Dispatcher.BeginInvoke(() => { if (_session == session) OnHostDisconnected(); });

        _session = session;
        _activeHost = (host, port);
        _pendingFill = CredentialStore.Get(host, port);
        ShowMessage(null);
        ConnectionText.Text = $"Connecting to {host}:{port}…";
        ConnectButton.IsEnabled = false;

        try
        {
            await session.StartAsync();
            ConnectionText.Text = $"Connected to {host}:{port}";
            DisconnectButton.IsEnabled = true;
            Terminal.Focus();
        }
        catch (Exception ex)
        {
            if (_session == session) _session = null;
            await session.DisposeAsync();
            ConnectionText.Text = "Disconnected";
            ShowMessage($"Connect failed: {ex.Message}");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        await CloseSessionAsync();
        ConnectionText.Text = "Disconnected";
        Refresh();
    }

    private async Task CloseSessionAsync()
    {
        var session = _session;
        _session = null;
        _activeHost = null;
        _pendingFill = null;
        DisconnectButton.IsEnabled = false;
        if (session is not null) await session.DisposeAsync();
    }

    private void OnHostDisconnected()
    {
        ConnectionText.Text = "Disconnected";
        DisconnectButton.IsEnabled = false;
        ShowMessage("Host closed the connection. Connect to start a new session.");
        Refresh();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _ = CloseSessionAsync();

    // ---- Screen updates --------------------------------------------------

    private void OnHostScreen()
    {
        TryAutoFill();
        Refresh();
    }

    private void Refresh()
    {
        var snap = _session?.GetSnapshot() ?? Terminal.Snapshot;
        Terminal.Snapshot = snap;
        LockText.Text = _session is not null && snap.KeyboardLocked ? "X SYSTEM" : "";
        CursorText.Text = $"{snap.CursorRow + 1:00}/{snap.CursorCol + 1:000}";
    }

    /// <summary>
    /// Fill saved credentials into the first screen that has a visible input
    /// field followed by a non-display one (the sign-on layout). One shot per
    /// connect, so clearing the fields by hand isn't undone.
    /// </summary>
    private void TryAutoFill()
    {
        if (_session is null || _pendingFill is not { } creds) return;

        // Find and fill in one edit so a host record can't move the fields in between.
        var found = false;
        _session.Edit(e =>
        {
            var (user, password) = SignOnFields(e.Fields);
            if (user is null || password is null) return EditResult.Ok;
            found = true;
            var result = e.FillField(user, creds.Username);
            return result == EditResult.Ok ? e.FillField(password, creds.Password) : result;
        });
        if (found) _pendingFill = null;
    }

    private static (FieldDefinition? User, FieldDefinition? Password) SignOnFields(IReadOnlyList<FieldDefinition> fields)
        => (fields.FirstOrDefault(f => f.IsEnterable && !f.IsNonDisplay),
            fields.FirstOrDefault(f => f.IsEnterable && f.IsNonDisplay));

    // ---- Keyboard --------------------------------------------------------

    private void OnTerminalKeyDown(object sender, KeyEventArgs e)
    {
        // F10 arrives as a system key; anything else with Alt (Alt+F4, menu
        // accelerators) belongs to Windows.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Alt)) return;
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var ctrl = mods.HasFlag(ModifierKeys.Control);

        if (key is >= Key.F1 and <= Key.F12 && !ctrl)
        {
            var n = key - Key.F1 + 1 + (shift ? 12 : 0);
            Handle(e, () => _ = PressAidAsync(Enum.Parse<Aid>($"F{n}")));
            return;
        }

        switch (key)
        {
            case Key.Enter: Handle(e, () => _ = PressAidAsync(Aid.Enter)); break;
            case Key.Escape: Handle(e, () => _ = PressAidAsync(Aid.Clear)); break;
            case Key.PageUp: Handle(e, () => _ = PressAidAsync(Aid.RollDown)); break;
            case Key.PageDown: Handle(e, () => _ = PressAidAsync(Aid.RollUp)); break;
            case Key.Tab: Handle(e, () => Edit(ed => { if (shift) ed.TabBackward(); else ed.TabForward(); })); break;
            case Key.Back: Handle(e, () => Edit(ed => ed.Backspace())); break;
            case Key.Delete: Handle(e, () => Edit(ed => ed.Delete())); break;
            case Key.End when ctrl: Handle(e, () => Edit(ed => ed.EraseToEndOfField())); break;
            case Key.Home: Handle(e, () => Edit(ed => ed.Home())); break;
            case Key.Left: Handle(e, () => Edit(ed => ed.MoveCursor(0, -1))); break;
            case Key.Right: Handle(e, () => Edit(ed => ed.MoveCursor(0, 1))); break;
            case Key.Up: Handle(e, () => Edit(ed => ed.MoveCursor(-1, 0))); break;
            case Key.Down: Handle(e, () => Edit(ed => ed.MoveCursor(1, 0))); break;
            case Key.V when ctrl: Handle(e, () => TypeText(Clipboard.ContainsText() ? Clipboard.GetText() : "")); break;
            case Key.C when ctrl && shift: Handle(e, () => OnCopyScreenshot(this, e)); break;
            case Key.S when ctrl && shift: Handle(e, () => OnSaveScreenshot(this, e)); break;
        }
    }

    private static void Handle(KeyEventArgs e, Action action)
    {
        e.Handled = true;
        action();
    }

    private void OnTerminalTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = true;
        TypeText(e.Text);
    }

    private void TypeText(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsControl(ch)) continue;
            if (!Edit(e => e.TypeChar(ch))) break;
        }
    }

    /// <summary>Apply an edit; returns false (after beeping) when the terminal refused it.</summary>
    private bool Edit(Func<FieldEditor, EditResult> edit)
    {
        if (_session is null) return false;
        var result = _session.Edit(edit);
        Refresh();

        switch (result)
        {
            case EditResult.Ok:
                ShowMessage(null);
                return true;
            case EditResult.AutoEnter:
                _ = PressAidAsync(Aid.Enter);
                return false;
            default:
                SystemSounds.Beep.Play();
                ShowMessage(result switch
                {
                    EditResult.ProtectedPosition => "Cursor is in a protected area of the display.",
                    EditResult.InvalidCharacter => "Character not allowed in this field.",
                    _ => _session.IsConnected ? "Keyboard locked: waiting for the host." : null,
                });
                return false;
        }
    }

    private void Edit(Action<FieldEditor> edit) => Edit(e => { edit(e); return EditResult.Ok; });

    private async Task PressAidAsync(Aid aid)
    {
        if (_session is not { IsConnected: true } session) return;
        RememberSignOn(session);

        try
        {
            if (!await session.SendAidAsync(aid))
            {
                SystemSounds.Beep.Play();
                ShowMessage("Keyboard locked: waiting for the host.");
            }
            else ShowMessage(null);
        }
        catch (Exception ex)
        {
            ShowMessage($"Send failed: {ex.Message}");
        }
        Refresh();
    }

    /// <summary>
    /// With "Remember credentials" on, save what's typed on the sign-on screen
    /// just before it's sent. Only when both fields have content, so pressing
    /// Enter on a half-filled form doesn't overwrite a working entry.
    /// </summary>
    private void RememberSignOn(Tn5250Session session)
    {
        if (RememberBox.IsChecked != true || _activeHost is not { } active) return;
        var creds = session.Read((editor, screen) =>
        {
            var (user, password) = SignOnFields(screen.Fields);
            if (user is null || password is null) return null;
            var u = editor.ReadField(user);
            var p = editor.ReadField(password);
            return u.Length > 0 && p.Length > 0 ? new SavedCredentials(u, p) : null;
        });
        if (creds is not null) CredentialStore.Save(active.Host, active.Port, creds);
    }

    private void OnForget(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0 || !int.TryParse(PortBox.Text, out var port)) return;
        CredentialStore.Forget(host, port);
        ShowMessage($"Forgot saved credentials for {host}:{port}.");
    }

    // ---- Screenshots and theme ------------------------------------------

    private ScreenshotOptions ScreenshotOptions => new() { Theme = Terminal.Theme };

    private void OnCopyScreenshot(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(TerminalScreenshot.RenderBitmap(Terminal.Snapshot, ScreenshotOptions));
        Terminal.Focus();
    }

    private void OnSaveScreenshot(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"5250-{DateTime.Now:yyyyMMdd-HHmmss}.png",
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllBytes(dialog.FileName, TerminalScreenshot.RenderPng(Terminal.Snapshot, ScreenshotOptions));
        Terminal.Focus();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = TerminalTheme.FromName(ThemeBox.SelectedItem as string);
        Terminal.Theme = theme;
        TerminalHost.Background = new SolidColorBrush(theme.Background);
        _settings = _settings with { Theme = theme.Name };
        _settings.Save();
    }

    private void ShowMessage(string? message) => MessageText.Text = message ?? "";
}

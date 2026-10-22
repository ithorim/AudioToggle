using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace AudioToggle;

internal static class Program
{
    internal const string DeviceAMatch = "HyperX Cloud Flight";
    internal const string DeviceBMatch = "Speakers (Realtek USB Audio";
    internal const uint HotkeyModifiers = 0x0002 | 0x0001; // MOD_CONTROL | MOD_ALT
    internal const Keys HotkeyKey = Keys.F9;
    internal const string HotkeyName = "Ctrl+Alt+F9";
    internal const int PopupDurationMs = 1300;
    internal const string Author = "ithorim";
    internal const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string StartupValueName = "AudioToggle";
    private const string MutexName = @"Local\AudioToggle.89057D5E-34F0-4241-9469-841584EF4C4E";
    private static readonly Lazy<Icon> appIcon = new(() =>
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AudioToggle.audiotoggle.ico")
            ?? throw new InvalidOperationException("The embedded app icon is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    });
    internal static Icon AppIcon => appIcon.Value;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out bool firstInstance);
        if (!firstInstance)
            return;

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetColorMode(SystemColorMode.Dark);
            using var context = new TrayContext();
            try
            {
                using var startupKey = Registry.CurrentUser.CreateSubKey(StartupRegistryPath)
                    ?? throw new IOException("Could not open the Windows startup registry key.");
                EnsureStartupRegistration(startupKey, Environment.ProcessPath
                    ?? throw new IOException("Could not determine the app's executable path."));
            }
            catch (Exception ex)
            {
                context.Notify($"Could not enable startup: {ex.Message}", warning: true);
            }
            Application.Run(context);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AudioToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (appIcon.IsValueCreated)
                appIcon.Value.Dispose();
            mutex.ReleaseMutex();
        }
    }

    internal static bool EnsureStartupRegistration(RegistryKey startupKey, string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("The startup executable must have an absolute path.", nameof(executablePath));
        string command = $"\"{Path.GetFullPath(executablePath)}\"";
        if (command.Length > 260)
            throw new PathTooLongException("The executable path is too long for Windows startup.");
        if (startupKey.GetValue(StartupValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string current &&
            string.Equals(current, command, StringComparison.OrdinalIgnoreCase) &&
            startupKey.GetValueKind(StartupValueName) == RegistryValueKind.String)
            return false;

        startupKey.SetValue(StartupValueName, command, RegistryValueKind.String);
        return true;
    }

    internal static bool IsDeviceA(string name) =>
        name.Contains(DeviceAMatch, StringComparison.OrdinalIgnoreCase);

    internal static bool IsDeviceB(string name) =>
        name.StartsWith(DeviceBMatch, StringComparison.OrdinalIgnoreCase);
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly ContextMenuStrip menu = new() { ShowImageMargin = false, ShowCheckMargin = false };
    private readonly NotifyIcon tray = new();
    private readonly SwitchPopup popup = new();
    private AppSettings settings = null!;
    private Form? dialog;
    private HotkeyWindow? hotkey;

    internal TrayContext()
    {
        try
        {
            try { settings = SettingsStore.Load(); }
            catch (Exception ex)
            {
                settings = AudioDevices.DefaultSettings();
                popup.ShowMessage($"Could not load settings; using the default pair. {ex.Message}", warning: true);
            }
            menu.Items.Add("Settings", null, (_, _) => OpenSettings());
            menu.Items.Add("About", null, (_, _) => OpenAbout());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitThread());
            tray.Icon = Program.AppIcon;
            tray.Text = "AudioToggle";
            tray.ContextMenuStrip = menu;
            hotkey = new HotkeyWindow(Toggle);
            tray.Visible = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OpenSettings()
    {
        if (dialog is not null) { dialog.Activate(); return; }
        try
        {
            using var form = new SettingsForm(settings);
            dialog = form;
            if (form.ShowDialog() == DialogResult.OK)
                settings = form.Result!;
        }
        catch (Exception ex) { Notify($"Could not open settings: {ex.Message}", warning: true); }
        finally { dialog = null; }
    }

    private void OpenAbout()
    {
        if (dialog is not null) { dialog.Activate(); return; }
        using var form = new AboutForm();
        dialog = form;
        try { form.ShowDialog(); }
        finally { dialog = null; }
    }

    private void Toggle()
    {
        string? targetName = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var active = AudioDevices.GetActive(enumerator);

            string? currentId = null;
            try
            {
                using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                currentId = current.ID;
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490))
            {
                // No default endpoint: start at the first configured device.
            }

            var next = AudioDevices.Next(settings.Devices, active, currentId);
            targetName = next.Name;
            var target = active.Find(device => device.Id == next.Id);

            if (target is null)
            {
                Notify($"{targetName} not available", warning: true);
                return;
            }

            // Recheck immediately before changing roles in case it was unplugged.
            using var endpoint = enumerator.GetDevice(target.Id);
            if (endpoint.State != DeviceState.Active)
            {
                Notify($"{targetName} not available", warning: true);
                return;
            }

            PolicyConfig.SetDefaultForAllRoles(target.Id, enumerator);
            Notify($"Switched to {target.Name}");
        }
        catch (COMException ex) when (targetName is not null &&
            (ex.HResult == unchecked((int)0x80070490) || // E_NOTFOUND
             ex.HResult == unchecked((int)0x88890004))) // AUDCLNT_E_DEVICE_INVALIDATED
        {
            Notify($"{targetName} not available", warning: true);
        }
        catch (Exception ex)
        {
            // A failed COM call must never escape the WM_HOTKEY handler.
            Notify($"Could not switch audio: {ex.Message}", warning: true);
        }
    }

    internal void Notify(string message, bool warning = false) =>
        popup.ShowMessage(message, warning);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hotkey?.Dispose();
            hotkey = null;
            dialog?.Close();
            popup.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SwitchPopup : Form
{
    private readonly Label heading = new() { Text = "AudioToggle", AutoSize = false };
    private readonly Label messageLabel = new() { AutoSize = false, AutoEllipsis = true, UseMnemonic = false };
    private readonly System.Windows.Forms.Timer dismissTimer = new() { Interval = Program.PopupDurationMs };

    internal SwitchPopup()
    {
        Text = "AudioToggle.Notification";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None; // Bounds below are scaled explicitly for the display DPI.
        BackColor = Color.FromArgb(30, 32, 36);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10f);
        Controls.AddRange([heading, messageLabel]);
        dismissTimer.Tick += (_, _) => Dismiss();
        Click += (_, _) => Dismiss();
        heading.Click += (_, _) => Dismiss();
        messageLabel.Click += (_, _) => Dismiss();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    internal void ShowMessage(string message, bool warning = false)
    {
        // Reuse one window and restart one timer; rapid switches never queue.
        dismissTimer.Stop();
        _ = Handle;
        int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);
        var area = (Screen.PrimaryScreen ?? Screen.FromPoint(Point.Empty)).WorkingArea;
        int padding = Scale(16);
        int width = Math.Min(Scale(440), area.Width - 2 * padding);
        int contentWidth = width - 2 * padding;
        int headingHeight = Scale(24);
        int textHeight = TextRenderer.MeasureText(message, Font,
            new Size(contentWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        int bodyHeight = Math.Clamp(textHeight, Scale(24), Scale(80));
        ClientSize = new Size(width, 2 * padding + headingHeight + bodyHeight);
        heading.SetBounds(padding, padding, contentWidth, headingHeight);
        heading.ForeColor = warning ? Color.FromArgb(255, 191, 105) : Color.FromArgb(153, 191, 214);
        messageLabel.SetBounds(padding, padding + headingHeight, contentWidth, bodyHeight);
        messageLabel.Text = message;
        Location = new Point(area.Right - Width - padding, area.Bottom - Height - padding);
        if (!Visible)
            Show();
        dismissTimer.Start();
    }

    private void Dismiss()
    {
        dismissTimer.Stop();
        Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            dismissTimer.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint ModNoRepeat = 0x4000;
    private readonly Action onHotkey;

    internal HotkeyWindow(Action onHotkey)
    {
        this.onHotkey = onHotkey;
        CreateHandle(new CreateParams
        {
            Caption = "AudioToggle.Hotkey",
            Parent = new IntPtr(-3) // HWND_MESSAGE: no visible window/taskbar entry.
        });

        if (!RegisterHotKey(Handle, HotkeyId,
                Program.HotkeyModifiers | ModNoRepeat, (uint)Program.HotkeyKey))
        {
            int error = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new Win32Exception(error,
                $"Could not register {Program.HotkeyName}. It may already be in use. AudioToggle will exit.");
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam == (IntPtr)HotkeyId)
        {
            onHotkey();
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

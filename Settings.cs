using System.Text.Json;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioToggle;

internal sealed record DeviceSelection(string Id, string Name);

internal sealed class AppSettings
{
    public List<DeviceSelection> Devices { get; set; } = [];

    internal void Validate()
    {
        if (Devices is null || Devices.Count < 2 ||
            Devices.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)) ||
            Devices.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Devices.Count)
            throw new InvalidDataException("Choose at least two different playback devices.");
    }
}

internal static class AudioDevices
{
    private const string DefaultA = "default:hyperx";
    private const string DefaultB = "default:realtek-speakers";

    internal static List<DeviceSelection> GetActive()
    {
        using var enumerator = new MMDeviceEnumerator();
        return GetActive(enumerator);
    }

    internal static List<DeviceSelection> GetActive(MMDeviceEnumerator enumerator)
    {
        var devices = new List<DeviceSelection>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
                devices.Add(new DeviceSelection(device.ID, device.FriendlyName));
        }
        return devices;
    }

    internal static AppSettings DefaultSettings() => new()
    {
        Devices = [new(DefaultA, "HyperX Cloud Flight"), new(DefaultB, "Speakers (Realtek USB Audio)")]
    };

    internal static DeviceSelection Resolve(DeviceSelection device, IReadOnlyList<DeviceSelection> active) =>
        active.FirstOrDefault(candidate => device.Id switch
        {
            DefaultA => Program.IsDeviceA(candidate.Name),
            DefaultB => Program.IsDeviceB(candidate.Name),
            _ => string.Equals(candidate.Id, device.Id, StringComparison.OrdinalIgnoreCase)
        }) ?? device;

    internal static DeviceSelection Next(IReadOnlyList<DeviceSelection> devices,
        IReadOnlyList<DeviceSelection> active, string? currentId)
    {
        var resolved = devices.Select(device => Resolve(device, active)).ToList();
        int currentIndex = resolved.FindIndex(device => string.Equals(device.Id, currentId, StringComparison.OrdinalIgnoreCase));
        return resolved[(currentIndex + 1) % resolved.Count];
    }
}

internal static class SettingsStore
{
    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioToggle", "settings.json");

    internal static AppSettings Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path))
            return AudioDevices.DefaultSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Settings are empty.");
        settings.Validate();
        return settings;
    }

    internal static void Save(AppSettings settings, string? path = null)
    {
        settings.Validate();
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

internal static class DialogStyle
{
    internal static void Apply(Form form, string title, Size size)
    {
        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.None;
        form.ClientSize = size;
        form.MinimumSize = new Size(size.Width, size.Height);
        form.StartPosition = FormStartPosition.CenterScreen;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.BackColor = Color.FromArgb(30, 32, 36);
        form.ForeColor = Color.WhiteSmoke;
        form.Font = new Font("Segoe UI", 10f);
        form.Icon = Program.AppIcon;
    }

    internal static void AddContent(Form form, Control content)
    {
        // Draw our own header so Windows cannot add a mismatched light title bar.
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = Color.FromArgb(65, 69, 77) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = form.BackColor };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(25, 27, 31) };
        var title = new Label { Text = form.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0), ForeColor = Color.Silver };
        var close = new StyledButton(quiet: true) { Text = "×", AccessibleName = "Close window", Dock = DockStyle.Right, Width = 44,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.Silver, BackColor = header.BackColor,
            UseVisualStyleBackColor = false, TabStop = false, Font = new Font("Segoe UI", 14f) };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(161, 47, 56);
        close.FlatAppearance.MouseDownBackColor = Color.FromArgb(128, 36, 44);
        close.Click += (_, _) => form.Close();
        MouseEventHandler drag = (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            var point = Control.MousePosition;
            int coordinates = (point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16);
            SendMessage(form.Handle, 0x00A1, (IntPtr)2, (IntPtr)coordinates); // WM_NCLBUTTONDOWN, HTCAPTION
        };
        header.MouseDown += drag;
        title.MouseDown += drag;
        header.Controls.Add(title);
        header.Controls.Add(close);
        content.Margin = Padding.Empty;
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        frame.Controls.Add(root);
        form.ClientSize = new Size(form.ClientSize.Width + 2, form.ClientSize.Height + 38);
        form.Controls.Add(frame);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    internal static Button Button(string text, EventHandler? action = null, bool primary = false)
    {
        var button = new StyledButton
        {
            Text = text, AutoSize = true, MinimumSize = new Size(70, 32), Primary = primary,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 51, 57),
            ForeColor = Color.WhiteSmoke, UseVisualStyleBackColor = false,
            Margin = new Padding(4), Padding = new Padding(6, 0, 6, 0)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(76, 80, 88);
        if (action is not null) button.Click += action;
        return button;
    }
}

internal sealed class SettingsForm : Form
{
    private List<DeviceSelection> active;
    private readonly List<DeviceSelection> selected;
    private readonly TableLayoutPanel rows = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Color.Silver, AutoSize = true,
        Font = new Font("Segoe UI", 9f), Margin = new Padding(4, 8, 0, 2) };
    private readonly Button add;
    private readonly Button save;
    private readonly Action<AppSettings> persist;
    internal AppSettings? Result { get; private set; }

    internal SettingsForm(AppSettings settings, List<DeviceSelection>? available = null, Action<AppSettings>? persist = null)
    {
        this.persist = persist ?? (value => SettingsStore.Save(value));
        active = available ?? AudioDevices.GetActive();
        selected = settings.Devices.Select(device => AudioDevices.Resolve(device, active)).ToList();
        DialogStyle.Apply(this, "AudioToggle - Settings", new Size(720, 440));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Playback devices", AutoSize = true, Font = new Font(Font.FontFamily, 15f), Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        layout.Controls.Add(new Label { Text = $"{Program.HotkeyName}: two devices toggle; three or more cycle from top to bottom.", AutoSize = true,
            ForeColor = Color.Silver, Margin = new Padding(0, 0, 0, 14) }, 0, 1);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0) };
        scroll.Controls.Add(rows);
        layout.Controls.Add(scroll, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        add = DialogStyle.Button("+ Add device", (_, _) => AddDevice());
        var refresh = DialogStyle.Button("Refresh devices", (_, _) =>
        {
            try
            {
                active = AudioDevices.GetActive();
                for (int i = 0; i < selected.Count; i++) selected[i] = AudioDevices.Resolve(selected[i], active);
                RebuildRows();
            }
            catch (Exception ex)
            {
                status.ForeColor = Color.FromArgb(255, 191, 105);
                status.Text = $"Could not refresh devices: {ex.Message}";
            }
        });
        actions.Controls.AddRange([add, refresh]);
        layout.Controls.Add(status, 0, 3);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1,
            Margin = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        save = DialogStyle.Button("Save", (_, _) => SaveSettings(), primary: true);
        var cancel = DialogStyle.Button("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([save, cancel]);
        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(buttons, 2, 0);
        layout.Controls.Add(footer, 0, 4);
        DialogStyle.AddContent(this, layout);
        AcceptButton = save;
        CancelButton = cancel;
        RebuildRows();
    }

    private void AddDevice()
    {
        var next = active.FirstOrDefault(candidate => selected.All(device => device.Id != candidate.Id));
        if (next is null) return;
        selected.Add(next);
        RebuildRows();
    }

    private void RebuildRows()
    {
        rows.SuspendLayout();
        foreach (Control control in rows.Controls.Cast<Control>().ToArray()) control.Dispose();
        rows.Controls.Clear();
        rows.RowStyles.Clear();
        rows.RowCount = selected.Count;
        for (int index = 0; index < selected.Count; index++)
        {
            int position = index;
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Margin = new Padding(0, 0, 0, 8) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(new Label { Text = $"{index + 1}.", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            var picker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = $"Playback device {index + 1}", Margin = new Padding(0, 4, 6, 4),
                DisplayMember = nameof(DeviceOption.Label) };
            var options = active.ToList();
            if (options.All(device => device.Id != selected[index].Id)) options.Insert(0, selected[index]);
            foreach (var device in options)
                picker.Items.Add(new DeviceOption(device, device.Name + (active.Any(candidate => candidate.Id == device.Id) ? "" : " (unavailable)")));
            picker.SelectedIndex = options.FindIndex(device => device.Id == selected[index].Id);
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (picker.SelectedItem is DeviceOption option) selected[position] = option.Device;
                UpdateValidation();
            };
            row.Controls.Add(picker, 1, 0);
            var up = new ChevronButton(up: true);
            up.Click += (_, _) => MoveDevice(position, -1);
            up.Enabled = index > 0;
            var down = new ChevronButton(up: false);
            down.Click += (_, _) => MoveDevice(position, 1);
            down.Enabled = index < selected.Count - 1;
            var remove = DialogStyle.Button("Remove", (_, _) => { selected.RemoveAt(position); RebuildRows(); });
            remove.Enabled = selected.Count > 2;
            foreach (var button in new Button[] { up, down, remove })
            {
                button.AutoSize = false;
                button.MinimumSize = Size.Empty;
                button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                button.Margin = new Padding(0, 4, button == remove ? 0 : 6, 4);
            }
            void SizeRowButtons()
            {
                int height = picker.Height;
                up.Size = down.Size = new Size(height, height);
                int padding = (int)Math.Round(20 * picker.DeviceDpi / 96f);
                remove.Size = new Size(TextRenderer.MeasureText(remove.Text, picker.Font).Width + padding, height);
            }
            picker.SizeChanged += (_, _) => SizeRowButtons();
            picker.FontChanged += (_, _) => SizeRowButtons();
            SizeRowButtons();
            row.Controls.Add(up, 2, 0);
            row.Controls.Add(down, 3, 0);
            row.Controls.Add(remove, 4, 0);
            rows.Controls.Add(row, 0, index);
        }
        rows.ResumeLayout(true);
        UpdateValidation();
    }

    private void MoveDevice(int index, int offset)
    {
        (selected[index], selected[index + offset]) = (selected[index + offset], selected[index]);
        RebuildRows();
    }

    private void UpdateValidation()
    {
        bool duplicate = selected.Select(device => device.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count;
        save.Enabled = !duplicate;
        add.Enabled = active.Any(candidate => selected.All(device => device.Id != candidate.Id));
        bool unavailable = selected.Any(device => active.All(candidate => candidate.Id != device.Id));
        status.ForeColor = duplicate || unavailable ? Color.FromArgb(255, 191, 105) : Color.Silver;
        status.Text = duplicate ? "Choose a different device in each row." :
            unavailable
                ? "Unavailable devices stay in your list. Reconnect them before switching to them."
                : "Changes take effect after Save. Use the arrows to change the cycle order.";
    }

    private void SaveSettings()
    {
        try
        {
            var updated = new AppSettings { Devices = selected.ToList() };
            persist(updated);
            Result = updated;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            status.ForeColor = Color.FromArgb(255, 191, 105);
            status.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private sealed record DeviceOption(DeviceSelection Device, string Label);
}

internal sealed class AboutForm : Form
{
    internal AboutForm()
    {
        DialogStyle.Apply(this, "About AudioToggle", new Size(360, 210));
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = "AudioToggle", AutoSize = true, Font = new Font(Font.FontFamily, 18f), Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { Text = $"Version {Application.ProductVersion.Split('+')[0]}", AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        layout.Controls.Add(new Label { Text = $"Author: {Program.Author}", AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        var close = DialogStyle.Button("Close");
        close.DialogResult = DialogResult.OK;
        layout.Controls.Add(close);
        DialogStyle.AddContent(this, layout);
        AcceptButton = close;
        CancelButton = close;
    }
}

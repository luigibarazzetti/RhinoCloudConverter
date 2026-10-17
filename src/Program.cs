using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;

namespace RhinoCloudConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ConverterForm());
    }
}

public sealed class ConverterForm : Form
{
    private readonly ListBox files = new() { SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
    private readonly TextBox pdalPath = new() { Text = FindPdal() };
    private readonly TextBox output = new() { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "survey.copc.laz") };
    private readonly ComboBox precision = new() { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
    private readonly CheckBox customPrecision = new() { Text = "Override coordinate precision (advanced)", Checked = false, AutoSize = true };
    private readonly CheckBox merge = new() { Text = "Merge all inputs into one COPC", Checked = true, AutoSize = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button convert = new() { Text = "Convert to COPC", Height = 40 };
    private readonly Button cancel = new() { Text = "Cancel", Enabled = false };
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Blocks };
    private CancellationTokenSource? cts;
    private Process? currentProcess;

    private static string FindPdal()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "engine", "Library", "bin", "pdal.exe");
        if (File.Exists(bundled)) return bundled;
        bundled = Path.Combine(AppContext.BaseDirectory, "engine", "Scripts", "pdal.exe");
        if (File.Exists(bundled)) return bundled;
        return "pdal.exe";
    }

    public ConverterForm()
    {
        Text = "RhinoCloud Converter | E57 / LAS / LAZ to COPC";
        Width = 850; Height = 720; MinimumSize = new System.Drawing.Size(680, 580);
        StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 10 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "E57 / LAS / LAZ input files", AutoSize = true, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold) }, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var add = new Button { Text = "Add files...", AutoSize = true };
        add.Click += (_, _) => AddFiles();
        var folder = new Button { Text = "Add folder...", AutoSize = true };
        folder.Click += (_, _) => AddFolder();
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        remove.Click += (_, _) => { foreach (var x in files.SelectedItems.Cast<string>().ToArray()) files.Items.Remove(x); };
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) => files.Items.Clear();
        actions.Controls.AddRange(new Control[] { add, folder, remove, clear });
        root.Controls.Add(actions, 0, 1);
        files.Dock = DockStyle.Fill;
        root.Controls.Add(files, 0, 2);
        root.Controls.Add(Labeled("PDAL executable", pdalPath, "Browse...", () => BrowsePdal()), 0, 3);
        root.Controls.Add(Labeled("Output COPC file / folder", output, "Browse...", () => BrowseOutput()), 0, 4);
        precision.Items.AddRange(new object[] { "0.001 (1 mm)", "0.0001 (0.1 mm)", "0.01 (1 cm)" });
        precision.SelectedIndex = 0;
        customPrecision.CheckedChanged += (_, _) => precision.Enabled = customPrecision.Checked;
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill };
        options.Controls.Add(merge);
        options.Controls.Add(customPrecision);
        options.Controls.Add(new Label { Text = "Scale (input units):", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        precision.Width = 150;
        options.Controls.Add(precision);
        merge.CheckedChanged += (_, _) => UpdateOutputHint();
        root.Controls.Add(options, 0, 5);
        var run = new FlowLayoutPanel { Dock = DockStyle.Fill };
        convert.Width = 160;
        convert.Click += async (_, _) => await ConvertAsync();
        cancel.Width = 95; cancel.Height = 40;
        cancel.Click += (_, _) => { cts?.Cancel(); TryKill(); };
        run.Controls.Add(convert); run.Controls.Add(cancel);
        root.Controls.Add(run, 0, 6);
        progress.Dock = DockStyle.Fill;
        root.Controls.Add(progress, 0, 7);
        log.Dock = DockStyle.Fill;
        root.Controls.Add(log, 0, 8);
        root.Controls.Add(new Label { Text = "Default keeps source coordinate units; COPC quantization is determined by PDAL (not lossless).", AutoSize = true }, 0, 9);
    }

    private static Control Labeled(string label, TextBox box, string buttonText, Action browse)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        box.Dock = DockStyle.Fill;
        panel.Controls.Add(box, 1, 0);
        var button = new Button { Text = buttonText, Dock = DockStyle.Fill };
        button.Click += (_, _) => browse();
        panel.Controls.Add(button, 2, 0);
        return panel;
    }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Point clouds (*.e57;*.las;*.laz)|*.e57;*.las;*.laz|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == DialogResult.OK) AddPaths(dialog.FileNames);
    }
    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
            AddPaths(Directory.EnumerateFiles(dialog.SelectedPath).Where(p => Supported(p)));
    }
    private static bool Supported(string p) => new[] { ".e57", ".las", ".laz" }.Contains(Path.GetExtension(p).ToLowerInvariant());
    private void AddPaths(IEnumerable<string> paths)
    {
        var existing = files.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths.Where(Supported).Select(Path.GetFullPath)) if (existing.Add(p)) files.Items.Add(p);
    }
    private void BrowsePdal()
    {
        using var d = new OpenFileDialog { Filter = "PDAL executable (pdal.exe)|pdal.exe|Executables (*.exe)|*.exe" };
        if (d.ShowDialog() == DialogResult.OK) pdalPath.Text = d.FileName;
    }
    private void BrowseOutput()
    {
        if (merge.Checked)
        {
            using var d = new SaveFileDialog { Filter = "COPC (*.copc.laz)|*.copc.laz", FileName = Path.GetFileName(output.Text) };
            if (d.ShowDialog() == DialogResult.OK) output.Text = d.FileName;
        }
        else
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog() == DialogResult.OK) output.Text = d.SelectedPath;
        }
    }
    private void UpdateOutputHint()
    {
        if (!merge.Checked && output.Text.EndsWith(".laz", StringComparison.OrdinalIgnoreCase))
            output.Text = Path.GetDirectoryName(output.Text) ?? output.Text;
        else if (merge.Checked && !output.Text.EndsWith(".copc.laz", StringComparison.OrdinalIgnoreCase))
            output.Text = Path.Combine(output.Text, "survey.copc.laz");
    }
    private void Append(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(message)); return; }
        log.AppendText(message + Environment.NewLine);
    }
    private void TryKill()
    {
        try { if (currentProcess is { HasExited: false }) currentProcess.Kill(entireProcessTree: true); } catch { }
    }
    private async Task ConvertAsync()
    {
        if (files.Items.Count == 0) { MessageBox.Show("Add at least one input file."); return; }
        var inputs = files.Items.Cast<string>().ToArray();
        if (inputs.Any(p => !File.Exists(p))) { MessageBox.Show("One or more input files no longer exist."); return; }
        var exe = pdalPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(exe)) { MessageBox.Show("Choose pdal.exe."); return; }
        var destination = output.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination)) { MessageBox.Show("Choose an output path."); return; }
        if (merge.Checked && !destination.EndsWith(".copc.laz", StringComparison.OrdinalIgnoreCase))
        { MessageBox.Show("Merged output must end with .copc.laz"); return; }
        cts = new CancellationTokenSource();
        convert.Enabled = false; cancel.Enabled = true; progress.Style = ProgressBarStyle.Marquee;
        try
        {
            Append("Checking PDAL drivers...");
            var drivers = await RunPdal(exe, new[] { "--drivers" }, cts.Token);
            if (drivers.ExitCode != 0) throw new Exception("Cannot run PDAL: " + drivers.Output);
            if (!drivers.Output.Contains("writers.copc", StringComparison.OrdinalIgnoreCase)) throw new Exception("PDAL build does not include writers.copc.");
            if (inputs.Any(p => Path.GetExtension(p).Equals(".e57", StringComparison.OrdinalIgnoreCase)) &&
                !drivers.Output.Contains("readers.e57", StringComparison.OrdinalIgnoreCase))
                throw new Exception("PDAL build does not include readers.e57. Install a build with the optional E57 plugin.");
            double? scale = customPrecision.Checked ? precision.SelectedIndex switch { 1 => 0.0001, 2 => 0.01, _ => 0.001 } : null;
            Append(scale.HasValue ? $"Custom coordinate scale: {scale.Value} (in source units)" : "Default: use PDAL COPC writer coordinate scale/offset defaults; no unit conversion or precision override.");
            if (merge.Checked) await ConvertOne(exe, inputs, destination, scale, cts.Token);
            else
            {
                Directory.CreateDirectory(destination);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var input in inputs)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(input);
                    var path = Path.Combine(destination, name + ".copc.laz");
                    int i = 2;
                    while (!used.Add(path) || File.Exists(path)) path = Path.Combine(destination, name + "_" + i++ + ".copc.laz");
                    await ConvertOne(exe, new[] { input }, path, scale, cts.Token);
                }
            }
            Append("Completed successfully.");
        }
        catch (OperationCanceledException) { Append("Cancelled."); }
        catch (Exception e) { Append("ERROR: " + e.Message); MessageBox.Show(e.Message, "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { progress.Style = ProgressBarStyle.Blocks; convert.Enabled = true; cancel.Enabled = false; cts.Dispose(); cts = null; }
    }
    private async Task ConvertOne(string exe, string[] inputs, string target, double? scale, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        if (File.Exists(target)) throw new IOException("Output already exists: " + target);
        // Explicit tagged readers and merge stage, to avoid ambiguity with multiple input views.
        var pipeline = new List<object>();
        var tags = new List<string>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var tag = "source" + i;
            tags.Add(tag);
            pipeline.Add(new Dictionary<string, object> { ["type"] = Path.GetExtension(inputs[i]).Equals(".e57", StringComparison.OrdinalIgnoreCase) ? "readers.e57" : "readers.las", ["filename"] = inputs[i], ["tag"] = tag });
        }
        string writerInput;
        if (tags.Count > 1)
        {
            pipeline.Add(new Dictionary<string, object> { ["type"] = "filters.merge", ["inputs"] = tags, ["tag"] = "merged" });
            writerInput = "merged";
        }
        else writerInput = tags[0];
        var writer = new Dictionary<string, object> { ["type"] = "writers.copc", ["inputs"] = new[] { writerInput }, ["filename"] = target };
        // Default: let PDAL choose its native writer defaults; never infer meters or rescale XYZ.
        // Advanced: explicit quantization step in the source coordinate units.
        if (scale.HasValue)
        {
            writer["scale_x"] = scale.Value;
            writer["scale_y"] = scale.Value;
            writer["scale_z"] = scale.Value;
            writer["offset_x"] = "auto";
            writer["offset_y"] = "auto";
            writer["offset_z"] = "auto";
        }
        pipeline.Add(writer);
        var tmp = Path.Combine(Path.GetTempPath(), "rhinocloud_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { pipeline }, new JsonSerializerOptions { WriteIndented = true }), token);
            Append("Converting " + inputs.Length + " file(s) -> " + target);
            var result = await RunPdal(exe, new[] { "pipeline", tmp }, token);
            Append(result.Output);
            if (result.ExitCode != 0) throw new Exception("PDAL exited with code " + result.ExitCode + ". See log.");
            if (!File.Exists(target)) throw new Exception("PDAL completed but output was not found.");
            Append("Created: " + target);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
    private async Task<(int ExitCode, string Output)> RunPdal(string exe, string[] args, CancellationToken token)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo { FileName = exe, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) p.StartInfo.ArgumentList.Add(a);
        var engine = Path.Combine(AppContext.BaseDirectory, "engine");
        if (Directory.Exists(engine))
        {
            var extra = new[] { Path.Combine(engine, "Library", "bin"), Path.Combine(engine, "Scripts"), engine };
            p.StartInfo.Environment["PATH"] = string.Join(Path.PathSeparator, extra) + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            p.StartInfo.Environment["PDAL_DRIVER_PATH"] = Path.Combine(engine, "Library", "bin");
        }
        p.Start(); currentProcess = p;
        try
        {
            var stdout = p.StandardOutput.ReadToEndAsync(token);
            var stderr = p.StandardError.ReadToEndAsync(token);
            await p.WaitForExitAsync(token);
            return (p.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
        }
        catch (OperationCanceledException) { TryKill(); throw; }
        finally { currentProcess = null; }
    }
}

namespace ecomode;

using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using ecomode;           // or ecomode
using ecomode.Interop;   // or ecomode.Interop

public partial class Form1 : Form
{
    private readonly bool _isAdmin;
    private readonly AppState _state;

    private bool _ecoHookActive = false;

    public Form1(bool isAdmin)
    {
        // InitializeComponent();
        try
        {
            _isAdmin = isAdmin;
            // InitializeComponent();
            _InitializeComponent();
            _state = AppState.Load();
            ApplyStateToUi();
            UpdateAdminBanner();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"InitializeComponent/MainForm error:\r\n\r\n{ex}",
                "EcoToggleGUI — Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw; // let Program.cs handler log it too
        }
    }

    // Optional: ensure hook gets cleaned up on exit
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_ecoHookActive)
        {
            try { HookManager.UnsubscribeWindowEvents(); } catch { }
            _ecoHookActive = false;
        }
        base.OnFormClosed(e);
    }

    public static readonly HashSet<string> BypassProcessList = new(StringComparer.OrdinalIgnoreCase)
    {
        // Not ourselves
        // "energystar.exe",
        "ecomode.exe",

        // Edge has energy awareness
        "msedge.exe",
        "webviewhost.exe",

        // UWP Frame has special handling
        "applicationframehost.exe",

        // Fire extinguisher should not catch fire
        "taskmgr.exe",
        "procmon.exe",
        "procmon64.exe",

        // Widgets
        "widgets.exe",

        // System shell
        "dwm.exe",
        "explorer.exe",
        "shellexperiencehost.exe",
        "startmenuexperiencehost.exe",
        "searchhost.exe",
        "sihost.exe",
        "fontdrvhost.exe",

        // IME
        "chsime.exe",
        "ctfmon.exe",

    #if DEBUG
        // Visual Studio
        "devenv.exe",
    #endif

        // System Services
        "csrss.exe",
        "smss.exe",
        "svchost.exe",

        // WUDF
        "wudfrd.exe",
    };


    private void UpdateAdminBanner()
    {
        lblAdmin.Text = _isAdmin
            ? "Running as Administrator: full features enabled."
            : "Not elevated: system power plan + HKLM policies will be skipped.";
    }

    #region UI wiring
    private Button btnEcoOn = new();
    private Button btnEcoOff = new();
    private CheckBox chkPowerPlan = new();
    private CheckBox chkEdge = new();
    private CheckBox chkChrome = new();
    private CheckBox chkGpuPref = new();
    private CheckBox chkEcoQos = new();
    private TextBox txtGpuApps = new();
    private TextBox txtEcoQosProcs = new();
    private Label lblAdmin = new();
    private Button btnSave = new();
    private TextBox txtLog = new();
    private SplitContainer splitMain;
    private SplitContainer splitTop;
    private SplitContainer splitRight;

    private void _InitializeComponent()
    {
        Text = "Eco Toggle GUI";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(1100, 700);

        // ===== Main vertical split: Top (controls) / Bottom (log) =====
        splitMain = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        Controls.Add(splitMain);

        // ===== Top horizontal split: Left (buttons+checkboxes) / Right (text areas) =====
        splitTop = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        splitMain.Panel1.Controls.Add(splitTop);

        // ---------- Left Panel ----------
        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        splitTop.Panel1.Controls.Add(leftPanel);

        lblAdmin.AutoSize = false;
        lblAdmin.Height = 24;
        lblAdmin.Dock = DockStyle.Top;
        leftPanel.Controls.Add(lblAdmin);

        btnEcoOn.Text = "Eco ON";
        btnEcoOn.SetBounds(0, 36, 140, 40);
        btnEcoOn.Click += (_, __) => ToggleEco(true);

        btnEcoOff.Text = "Eco OFF";
        btnEcoOff.SetBounds(150, 36, 140, 40);
        btnEcoOff.Click += (_, __) => ToggleEco(false);

        btnSave.Text = "Save Settings";
        btnSave.SetBounds(300, 36, 160, 40);
        btnSave.Click += (_, __) => SaveFromUi();

        chkPowerPlan.Text = "Windows power plan (EPP/Boost via powercfg)";
        chkPowerPlan.SetBounds(0, 88, 520, 26);

        chkEdge.Text = "Edge policy (Hardware Accel OFF / Efficiency Mode ON)";
        chkEdge.SetBounds(0, 120, 540, 26);

        chkChrome.Text = "Chrome policy (Hardware Accel OFF / Battery Saver)";
        chkChrome.SetBounds(0, 152, 520, 26);

        chkGpuPref.Text = "Per-app GPU preference (HKCU)";
        chkGpuPref.SetBounds(0, 184, 320, 26);

        chkEcoQos.Text = "EcoQoS for running processes";
        chkEcoQos.SetBounds(0, 216, 320, 26);

        leftPanel.Controls.AddRange(new Control[]
        {
            btnEcoOn, btnEcoOff, btnSave,
            chkPowerPlan, chkEdge, chkChrome, chkGpuPref, chkEcoQos
        });

        // ---------- Right Panel (two stacked text areas) ----------
        splitRight = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        splitTop.Panel2.Controls.Add(splitRight);

        // GPU Pref Apps (top)
        var panelGpu = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var lblGpu = new Label { Text = "GPU Pref Apps (EXE paths, one per line):", Dock = DockStyle.Top, Height = 20 };
        txtGpuApps.Multiline = true;
        txtGpuApps.ScrollBars = ScrollBars.Vertical;
        txtGpuApps.Dock = DockStyle.Fill;
        panelGpu.Controls.Add(txtGpuApps);
        panelGpu.Controls.Add(lblGpu);
        splitRight.Panel1.Controls.Add(panelGpu);

        // EcoQoS Processes (bottom)
        var panelQos = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var lblQos = new Label { Text = "EcoQoS Processes (names, one per line):", Dock = DockStyle.Top, Height = 20 };
        txtEcoQosProcs.Multiline = true;
        txtEcoQosProcs.ScrollBars = ScrollBars.Vertical;
        txtEcoQosProcs.Dock = DockStyle.Fill;
        panelQos.Controls.Add(txtEcoQosProcs);
        panelQos.Controls.Add(lblQos);
        splitRight.Panel2.Controls.Add(panelQos);

        // ===== Bottom log =====
        txtLog.Multiline = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.ReadOnly = true;
        txtLog.Dock = DockStyle.Fill;
        splitMain.Panel2.Controls.Add(txtLog);

        // Defer splitter setup until form is actually shown
        this.Shown += OnFormShown;
    }


    private void OnFormShown(object? sender, EventArgs e)
    {
        // Now the SplitContainers have real sizes. Set min sizes first, then distances.
        TrySetMinSizes(splitMain,  240, 120);
        TrySetMinSizes(splitTop,   360, 280);
        TrySetMinSizes(splitRight, 140, 140);

        SafeSetSplitter(splitMain,  (int)(splitMain.Height * 0.65));  // ~65% to top controls
        SafeSetSplitter(splitTop,   (int)(splitTop.Width  * 0.55));   // ~55% to left panel
        SafeSetSplitter(splitRight, (int)(splitRight.Height * 0.50)); // half top/bottom
    }

    private static void TrySetMinSizes(SplitContainer sc, int min1, int min2)
    {
        // Guard: if not sized yet, skip quietly
        int length = sc.Orientation == Orientation.Horizontal ? sc.Height : sc.Width;
        if (length <= 0) return;

        // Make sure the combined mins fit; if not, shrink proportionally
        int maxTotal = Math.Max(0, length - sc.SplitterWidth - 1); // leave 1px headroom
        int totalMin = Math.Max(0, min1) + Math.Max(0, min2);

        if (totalMin > maxTotal && totalMin > 0)
        {
            // Scale them down while preserving the ratio
            double scale = (double)maxTotal / totalMin;
            min1 = (int)Math.Floor(min1 * scale);
            min2 = Math.Max(0, maxTotal - min1);
        }

        // Apply safely
        try { sc.Panel1MinSize = Math.Max(0, min1); } catch { /* ignore */ }
        try { sc.Panel2MinSize = Math.Max(0, min2); } catch { /* ignore */ }
    }

    private static void SafeSetSplitter(SplitContainer sc, int proposed)
    {
        int length = sc.Orientation == Orientation.Horizontal ? sc.Height : sc.Width;
        if (length <= 0) return;

        int min = Math.Max(0, sc.Panel1MinSize);
        int max = Math.Max(0, length - sc.Panel2MinSize - sc.SplitterWidth);

        if (min > max)
        {
            // If mins still don't fit, collapse to a workable point
            min = Math.Max(0, Math.Min(min, length - sc.SplitterWidth));
            max = min;
        }

        int clamped = Math.Max(min, Math.Min(proposed, max));

        try { sc.SplitterDistance = clamped; } catch { /* ignore if container is mid-layout */ }
    }

    private void ApplyStateToUi()
    {
        chkPowerPlan.Checked = _state.IncludePowerPlan;
        chkEdge.Checked = _state.IncludeEdge;
        chkChrome.Checked = _state.IncludeChrome;
        chkGpuPref.Checked = _state.IncludeGpuPref;
        chkEcoQos.Checked = _state.IncludeEcoQos;
        txtGpuApps.Text = string.Join(Environment.NewLine, _state.GpuPrefApps);
        txtEcoQosProcs.Text = string.Join(Environment.NewLine, _state.EcoQosProcesses);
    }

    private void SaveFromUi()
    {
        _state.IncludePowerPlan = chkPowerPlan.Checked;
        _state.IncludeEdge = chkEdge.Checked;
        _state.IncludeChrome = chkChrome.Checked;
        _state.IncludeGpuPref = chkGpuPref.Checked;
        _state.IncludeEcoQos = chkEcoQos.Checked;
        _state.GpuPrefApps = txtGpuApps.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();
        _state.EcoQosProcesses = txtEcoQosProcs.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();
        _state.Save();
        Log("Settings saved.");
    }
    #endregion

    // private void ToggleEco(bool eco)
    // {
    //     Log($"=== {(eco ? "ECO ON" : "ECO OFF")} ===");

    //     if (_state.IncludePowerPlan)
    //     {
    //         if (_isAdmin) ApplyPowerPlan(eco);
    //         else Log("Skipped power plan (needs elevation).");
    //     }

    //     if (_state.IncludeEdge)
    //     {
    //         if (_isAdmin) ApplyEdgePolicy(eco);
    //         else Log("Skipped Edge policy (needs elevation).");
    //     }

    //     if (_state.IncludeChrome)
    //     {
    //         if (_isAdmin) ApplyChromePolicy(eco);
    //         else Log("Skipped Chrome policy (needs elevation).");
    //     }

    //     if (_state.IncludeGpuPref) ApplyGpuPreferences(eco);

    //     if (_state.IncludeEcoQos) ApplyEcoQos(eco);

    //     Log("Done. Restart browsers to apply policy changes.");
    // }
    private void ToggleEco(bool eco)
    {
        Log($"=== {(eco ? "ECO ON" : "ECO OFF")} ===");

        if (_state.IncludePowerPlan)
        {
            if (_isAdmin) ApplyPowerPlan(eco);
            else Log("Skipped power plan (needs elevation).");
        }

        if (_state.IncludeEdge)
        {
            if (_isAdmin) ApplyEdgePolicy(eco);
            else Log("Skipped Edge policy (needs elevation).");
        }

        if (_state.IncludeChrome)
        {
            if (_isAdmin) ApplyChromePolicy(eco);
            else Log("Skipped Chrome policy (needs elevation).");
        }

        if (_state.IncludeGpuPref) ApplyGpuPreferences(eco);

        if (_state.IncludeEcoQos)
        {
            if (eco)
            {
                // Start foreground hook and throttle background immediately
                if (!_ecoHookActive)
                {
                    HookManager.SubscribeToWindowEvents();
                    _ecoHookActive = true;
                }
                EnergyManager.ThrottleAllUserBackgroundProcesses();
                Log("EcoQoS: throttled background processes; foreground will auto-boost.");
            }
            else
            {
                // Stop hook and unthrottle everything
                if (_ecoHookActive)
                {
                    HookManager.UnsubscribeWindowEvents();
                    _ecoHookActive = false;
                }
                EnergyManager.UnthrottleAllUserProcesses();
                Log("EcoQoS: restored normal priority to user processes.");
            }
        }

        Log("Done. Restart browsers to apply policy changes.");
    }

    #region Actions

    private void ApplyPowerPlan(bool eco)
    {
        // EPP + Boost (AC/DC); adapt values as desired
        // Eco: EPP high (85/90), Boost off (0)
        // Perf: EPP low (10/25), Boost aggressive/default (2/1)
        var cmds = eco
            ? new[]
            {
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 85",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 90",
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0",
                "powercfg -setactive SCHEME_CURRENT"
            }
            : new[]
            {
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 10",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 25",
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 1",
                "powercfg -setactive SCHEME_CURRENT"
            };

        foreach (var c in cmds) RunCmd(c);
    }

    private void ApplyEdgePolicy(bool eco)
    {
        // HKLM\SOFTWARE\Policies\Microsoft\Edge
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true);
        if (key is null) { Log("Edge policy key unavailable."); return; }

        key.SetValue("HardwareAccelerationModeEnabled", eco ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("EfficiencyModeEnabled", eco ? 1 : 0, RegistryValueKind.DWord);
        Log("Edge policy updated.");
    }

    private void ApplyChromePolicy(bool eco)
    {
        // HKLM\SOFTWARE\Policies\Google\Chrome
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome", true);
        if (key is null) { Log("Chrome policy key unavailable."); return; }

        key.SetValue("HardwareAccelerationModeEnabled", eco ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("BatterySaverModeAvailability", eco ? 1 : 0, RegistryValueKind.DWord);
        Log("Chrome policy updated.");
    }

    private void ApplyGpuPreferences(bool eco)
    {
        // HKCU\Software\Microsoft\DirectX\UserGpuPreferences
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
        if (key is null) { Log("GPU preference key unavailable."); return; }

        foreach (var exe in _state.GpuPrefApps)
        {
            try
            {
                var path = exe.Trim('"');
                if (!File.Exists(path))
                {
                    Log($"GPU Pref: file not found: {path}");
                    continue;
                }
                var val = eco ? "GpuPreference=1;" : "GpuPreference=2;"; // 1=power-saving, 2=high-perf
                key.SetValue(path, val, RegistryValueKind.String);
                Log($"GPU Pref set: {(eco ? "power-saving" : "high-perf")} -> {path}");
            }
            catch (Exception ex)
            {
                Log($"GPU Pref error for {exe}: {ex.Message}");
            }
        }
    }

    // private void ApplyEcoQos(bool eco)
    // {
    //     foreach (var proc in Process.GetProcesses())
    //     {
    //         string exeName = "";

    //         try
    //         {
    //             // Get process name safely
    //             exeName = proc.ProcessName + ".exe";

    //             // Skip bypass list
    //             if (BypassProcessList.Contains(exeName))
    //             {
    //                 Log($"EcoQoS: skipping bypassed process {exeName}:{proc.Id}");
    //                 continue;
    //             }

    //             if (proc.SessionId == 0)
    //             {
    //                 Log($"EcoQoS: skipping system service {exeName}:{proc.Id}");
    //                 continue; // skip system services
    //             }

    //             // Try to set EcoQoS
    //             SetEcoQos(proc.Id, eco);
    //             Log($"EcoQoS {(eco ? "ENABLED" : "DISABLED")} -> {exeName}:{proc.Id}");
    //         }
    //         catch (Win32Exception)
    //         {
    //             // Access denied (system process, etc.)
    //             Log($"EcoQoS: access denied {exeName}:{proc.Id}");
    //             continue;
    //         }
    //         catch (Exception ex)
    //         {
    //             Log($"EcoQoS error {proc.ProcessName}:{proc.Id} -> {ex.Message}");
    //         }
    //     }
    // }

    // private void ApplyEcoQos(bool eco)
    // {
    //     foreach (var name in _state.EcoQosProcesses)
    //     {
    //         var list = Process.GetProcessesByName(name);
    //         if (list.Length == 0) { Log($"EcoQoS: no running process named '{name}'"); continue; }

    //         foreach (var p in list)
    //         {
    //             try
    //             {
    //                 SetEcoQos(p.Id, eco);
    //                 Log($"EcoQoS {(eco ? "ENABLED" : "DISABLED")} -> {name}:{p.Id}");
    //             }
    //             catch (Exception ex)
    //             {
    //                 Log($"EcoQoS error {name}:{p.Id} -> {ex.Message}");
    //             }
    //         }
    //     }
    // }

    #endregion

    #region Helpers

    private void RunCmd(string cmdLine)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + cmdLine)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Log($"> {cmdLine}");
            if (!string.IsNullOrWhiteSpace(stdout)) Log(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) Log(stderr.Trim());
        }
        catch (Exception ex)
        {
            Log($"> {cmdLine} -> ERROR: {ex.Message}");
        }
    }

    private void Log(string msg)
    {
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }

    // ==== EcoQoS P/Invoke ====
    // const int ProcessPowerThrottling = 3; // PROCESS_POWER_THROTTLING
    // const int PROCESS_SET_INFORMATION = 0x0200;

    // [StructLayout(LayoutKind.Sequential)]
    // struct PROCESS_POWER_THROTTLING_STATE
    // {
    //     public uint Version;
    //     public uint ControlMask;
    //     public uint StateMask;
    // }

    // const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    // const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;

    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    // [DllImport("kernel32.dll", SetLastError = true)]
    // static extern bool SetProcessInformation(IntPtr hProcess, int infoClass,
    //     ref PROCESS_POWER_THROTTLING_STATE info, int infoLen);

    // [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    // private static void SetEcoQos(int pid, bool enable)
    // {
    //     var h = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
    //     if (h == IntPtr.Zero)
    //         throw new Win32Exception(Marshal.GetLastWin32Error());

    //     try
    //     {
    //         var st = new PROCESS_POWER_THROTTLING_STATE
    //         {
    //             Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
    //             ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
    //             StateMask = enable ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0
    //         };
    //         if (!SetProcessInformation(h, ProcessPowerThrottling, ref st,
    //                 Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
    //         {
    //             throw new Win32Exception(Marshal.GetLastWin32Error());
    //         }
    //     }
    //     finally { CloseHandle(h); }
    // }

    #endregion
}

public class AppState
{
    public bool IncludePowerPlan { get; set; } = true;
    public bool IncludeEdge { get; set; } = true;
    public bool IncludeChrome { get; set; } = true;
    public bool IncludeGpuPref { get; set; } = true;
    public bool IncludeEcoQos { get; set; } = true;

    public string[] GpuPrefApps { get; set; } = new[]
    {
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    };

    public string[] EcoQosProcesses { get; set; } = new[] { "chrome", "msedge", "Code", "Discord" };

    public static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "EcoToggleGUI", "settings.json");

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AppState Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var s = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppState>(s) ?? new AppState();
            }
        }
        catch { /* ignore and fallback */ }
        return new AppState();
    }
}
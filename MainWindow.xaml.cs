using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace UE58Launcher;

public partial class MainWindow : Window
{
    private string? _engineExePath;
    private string _engineVersion = "5";

    public MainWindow()
    {
        InitializeComponent();
        _engineExePath = ResolveEnginePath();
        UpdateVersionLabels();
    }

    private void UpdateVersionLabels()
    {
        if (!string.IsNullOrWhiteSpace(_engineExePath) && File.Exists(_engineExePath))
        {
            _engineVersion = ReadEngineVersion(_engineExePath);
            Title = $"UE {_engineVersion} Launcher";
            LaunchButtonText.Text = $"Launch UE {_engineVersion}";
            LaunchEditorDirectButton.ToolTip = _engineExePath;
        }
        else
        {
            Title = "UE5 Launcher";
            LaunchButtonText.Text = "Launch UE5";
            LaunchEditorDirectButton.ToolTip = "Click to locate and launch UnrealEditor.exe";
        }
    }

    private static string ReadEngineVersion(string exePath)
    {
        try
        {
            string? win64Dir = Path.GetDirectoryName(exePath);
            string? binariesDir = win64Dir != null ? Path.GetDirectoryName(win64Dir) : null;
            string? engineDir = binariesDir != null ? Path.GetDirectoryName(binariesDir) : null;
            string? buildVersionPath = engineDir != null ? Path.Combine(engineDir, "Build", "Build.version") : null;

            if (buildVersionPath != null && File.Exists(buildVersionPath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(buildVersionPath));
                var root = doc.RootElement;
                int major = root.TryGetProperty("MajorVersion", out var maj) ? maj.GetInt32() : 5;
                int minor = root.TryGetProperty("MinorVersion", out var min) ? min.GetInt32() : 0;
                return $"{major}.{minor}";
            }
        }
        catch
        {
            // Ignore JSON parse errors and fallback
        }

        return "5";
    }

    private static Version ParseSortableVersion(string exePath)
    {
        try
        {
            string? win64Dir = Path.GetDirectoryName(exePath);
            string? binariesDir = win64Dir != null ? Path.GetDirectoryName(win64Dir) : null;
            string? engineDir = binariesDir != null ? Path.GetDirectoryName(binariesDir) : null;
            string? buildVersionPath = engineDir != null ? Path.Combine(engineDir, "Build", "Build.version") : null;

            if (buildVersionPath != null && File.Exists(buildVersionPath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(buildVersionPath));
                var root = doc.RootElement;
                int major = root.TryGetProperty("MajorVersion", out var maj) ? maj.GetInt32() : 5;
                int minor = root.TryGetProperty("MinorVersion", out var min) ? min.GetInt32() : 0;
                int patch = root.TryGetProperty("PatchVersion", out var pat) ? pat.GetInt32() : 0;
                return new Version(major, minor, patch);
            }
        }
        catch
        {
            // Ignore
        }

        return new Version(5, 0, 0);
    }

    private static string? ResolveEnginePath()
    {
        // 1. Walk up from the launcher's own directory (if placed inside a UE installation folder)
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        var discovered = new List<string>();

        void TryAddRoot(string? installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return;
            try
            {
                string exe = Path.Combine(installRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
                if (File.Exists(exe))
                {
                    discovered.Add(exe);
                }
            }
            catch
            {
                // Ignore invalid paths
            }
        }

        // 2. Check Epic Games Launcher's LauncherInstalled.dat
        try
        {
            string launcherDat = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");

            if (File.Exists(launcherDat))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(launcherDat));
                if (doc.RootElement.TryGetProperty("InstallationList", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        if (item.TryGetProperty("InstallLocation", out var loc))
                        {
                            TryAddRoot(loc.GetString());
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors reading LauncherInstalled.dat
        }

        // 3. Check Windows Registry (EpicGames Unreal Engine installations & custom builds)
        try
        {
            using var hklmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine");
            if (hklmKey != null)
            {
                foreach (string subName in hklmKey.GetSubKeyNames())
                {
                    using var subKey = hklmKey.OpenSubKey(subName);
                    TryAddRoot(subKey?.GetValue("InstalledDirectory") as string);
                }
            }
        }
        catch
        {
            // Ignore registry errors
        }

        try
        {
            using var hkcuKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine\Builds");
            if (hkcuKey != null)
            {
                foreach (string valName in hkcuKey.GetValueNames())
                {
                    TryAddRoot(hkcuKey.GetValue(valName) as string);
                }
            }
        }
        catch
        {
            // Ignore registry errors
        }

        // 4. Scan common installation directories (Desktop, OneDrive\Desktop, Program Files\Epic Games, drive roots)
        var searchParents = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop")
        };

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            searchParents.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "Epic Games"));
            searchParents.Add(Path.Combine(drive.RootDirectory.FullName, "Epic Games"));
            searchParents.Add(drive.RootDirectory.FullName);
        }

        foreach (string parent in searchParents.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(parent))
                    continue;

                foreach (string subDir in Directory.EnumerateDirectories(parent, "UE*"))
                {
                    TryAddRoot(subDir);
                }
            }
            catch
            {
                // Ignore permission errors
            }
        }

        return discovered
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseSortableVersion)
            .FirstOrDefault();
    }

    private void LaunchEditorDirectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_engineExePath) || !File.Exists(_engineExePath))
        {
            var dialog = new OpenFileDialog
            {
                Title = "Locate UnrealEditor.exe",
                Filter = "UnrealEditor Executable (UnrealEditor.exe)|UnrealEditor.exe|Executables (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                _engineExePath = dialog.FileName;
                UpdateVersionLabels();
            }
            else
            {
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _engineExePath,
                WorkingDirectory = Path.GetDirectoryName(_engineExePath) ?? "",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start UnrealEditor.exe:\n{ex.Message}",
                "Launch Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

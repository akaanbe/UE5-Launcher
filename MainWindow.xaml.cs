using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace UE5Launcher;

public class EngineInstallation
{
    public string ExecutablePath { get; set; } = "";
    public string RootPath { get; set; } = "";
    public Version ParsedVersion { get; set; } = new(5, 0, 0);
    public string DisplayName { get; set; } = "Unreal Engine 5";
}

public partial class MainWindow : Window
{
    private readonly List<EngineInstallation> _installations;

    public MainWindow()
    {
        InitializeComponent();
        _installations = DiscoverInstallations();
    }

    private static EngineInstallation CreateInstallationEntry(string exePath)
    {
        string fullExe = Path.GetFullPath(exePath);
        string? win64Dir = Path.GetDirectoryName(fullExe);
        string? binariesDir = win64Dir != null ? Path.GetDirectoryName(win64Dir) : null;
        string? engineDir = binariesDir != null ? Path.GetDirectoryName(binariesDir) : null;
        string rootDir = (engineDir != null ? Path.GetDirectoryName(engineDir) : null) ?? (win64Dir ?? "");

        Version parsedVer = new(5, 0, 0);
        string displayName = "Unreal Engine 5";

        try
        {
            string? buildVersionPath = engineDir != null ? Path.Combine(engineDir, "Build", "Build.version") : null;
            if (buildVersionPath != null && File.Exists(buildVersionPath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(buildVersionPath));
                var root = doc.RootElement;
                int major = root.TryGetProperty("MajorVersion", out var maj) ? maj.GetInt32() : 5;
                int minor = root.TryGetProperty("MinorVersion", out var min) ? min.GetInt32() : 0;
                int patch = root.TryGetProperty("PatchVersion", out var pat) ? pat.GetInt32() : 0;
                parsedVer = new Version(major, minor, patch);
                displayName = $"Unreal Engine {major}.{minor} ({major}.{minor}.{patch})";
            }
        }
        catch
        {
            // Keep default version display if Build.version cannot be parsed
        }

        return new EngineInstallation
        {
            ExecutablePath = fullExe,
            RootPath = rootDir,
            ParsedVersion = parsedVer,
            DisplayName = displayName
        };
    }

    private static List<EngineInstallation> DiscoverInstallations()
    {
        var discoveredExePaths = new List<string>();

        void TryAddRoot(string? installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return;
            try
            {
                string exe = Path.Combine(installRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
                if (File.Exists(exe))
                {
                    discoveredExePaths.Add(Path.GetFullPath(exe));
                }
            }
            catch
            {
                // Ignore invalid paths
            }
        }

        // 1. Walk up from the launcher's own directory
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            TryAddRoot(dir.FullName);
            dir = dir.Parent;
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

        return discoveredExePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateInstallationEntry)
            .Where(inst => inst.ParsedVersion.Major == 5)
            .OrderByDescending(inst => inst.ParsedVersion)
            .ToList();
    }

    private void LaunchExecutable(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
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

    private void LaunchEditorDirectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installations.Count == 0)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Locate UnrealEditor.exe",
                Filter = "UnrealEditor Executable (UnrealEditor.exe)|UnrealEditor.exe|Executables (*.exe)|*.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                var entry = CreateInstallationEntry(dialog.FileName);
                _installations.Add(entry);
                LaunchExecutable(entry.ExecutablePath);
            }
            return;
        }

        if (_installations.Count == 1)
        {
            LaunchExecutable(_installations[0].ExecutablePath);
            return;
        }

        // Multiple installations found: show version picker
        VersionsListBox.ItemsSource = _installations;
        LaunchEditorDirectButton.Visibility = Visibility.Collapsed;
        Width = 380;
        Height = 250;
        VersionPickerPanel.Visibility = Visibility.Visible;
    }

    private void VersionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionsListBox.SelectedItem is EngineInstallation selected)
        {
            LaunchExecutable(selected.ExecutablePath);
            VersionsListBox.SelectedItem = null;
            VersionPickerPanel.Visibility = Visibility.Collapsed;
            Width = 300;
            Height = 160;
            LaunchEditorDirectButton.Visibility = Visibility.Visible;
        }
    }
}

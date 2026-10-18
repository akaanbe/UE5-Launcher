using System.Diagnostics;
using System.IO;
using System.Windows;

namespace UE58Launcher;

public partial class MainWindow : Window
{
    private readonly string _engineExePath;

    public MainWindow()
    {
        InitializeComponent();
        _engineExePath = ResolveEnginePath();
    }

    private static string ResolveEnginePath()
    {
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

        return @"c:\Users\asdma\OneDrive\Desktop\UE_5.8\Engine\Binaries\Win64\UnrealEditor.exe";
    }

    private void LaunchEditorDirectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_engineExePath))
        {
            MessageBox.Show(
                $"UnrealEditor.exe could not be found at:\n{_engineExePath}",
                "Engine Executable Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
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

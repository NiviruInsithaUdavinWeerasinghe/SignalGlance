using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SignalGlance.Installer
{
    public partial class MainWindow : Window
    {
        private int _currentPage = 1;

        public MainWindow()
        {
            InitializeComponent();
            UpdateStepIndicator(1);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void AgreeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentPage == 2)
            {
                NextButton.IsEnabled = AgreeCheckBox.IsChecked == true;
            }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage == 1)
            {
                // Transition to License page
                IntroPage.Visibility = Visibility.Collapsed;
                LicensePage.Visibility = Visibility.Visible;
                NextButton.IsEnabled = AgreeCheckBox.IsChecked == true;
                _currentPage = 2;
                UpdateStepIndicator(2);
            }
            else if (_currentPage == 2)
            {
                // Transition to Progress page and run install
                LicensePage.Visibility = Visibility.Collapsed;
                ProgressPage.Visibility = Visibility.Visible;
                CancelButton.IsEnabled = false;
                NextButton.IsEnabled = false;
                _currentPage = 3;
                UpdateStepIndicator(3);

                await RunInstallationAsync();
            }
            else if (_currentPage == 4)
            {
                // Launch app if checked and exit setup
                if (LaunchCheckBox.IsChecked == true)
                {
                    try
                    {
                        string exePath = @"C:\SignalGlance\SignalGlance\bin\Debug\net9.0-windows10.0.19041.0\SignalGlance.exe";
                        if (!File.Exists(exePath))
                        {
                            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                            if (baseDir.Contains("SignalGlance.Installer"))
                            {
                                exePath = Path.GetFullPath(Path.Combine(baseDir, "..\\..\\..\\..\\SignalGlance\\bin\\Debug\\net9.0-windows10.0.19041.0\\SignalGlance.exe"));
                            }
                            else
                            {
                                exePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SignalGlance", "SignalGlance.exe");
                            }
                        }
                        
                        if (File.Exists(exePath))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                            {
                                UseShellExecute = true
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not launch SignalGlance: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                Application.Current.Shutdown();
            }
        }

        private async Task RunInstallationAsync()
        {
            try
            {
                // Read configurations on the UI thread
                bool createDesktopShortcut = false;
                bool launchOnStartup = false;

                Dispatcher.Invoke(() =>
                {
                    createDesktopShortcut = DesktopShortcutCheckBox.IsChecked == true;
                    launchOnStartup = StartupCheckBox.IsChecked == true;
                });

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installDir = Path.Combine(localAppData, "SignalGlance");

                // Get source path of binaries
                string sourceDir = "";
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                
                if (baseDir.Contains("SignalGlance.Installer"))
                {
                    sourceDir = Path.GetFullPath(Path.Combine(baseDir, "..\\..\\..\\..\\SignalGlance\\bin\\Debug\\net9.0-windows10.0.19041.0"));
                }
                else
                {
                    sourceDir = baseDir;
                }

                // Smooth progress bar animation helper
                Func<int, int, string, Task> animateProgress = async (fromVal, toVal, status) =>
                {
                    UpdateProgressText(status);
                    for (int val = fromVal; val <= toVal; val += 2)
                    {
                        UpdateProgressValue(val);
                        await Task.Delay(15);
                    }
                };

                // STEP 1: Creating directory
                UpdateStepUI(1, "▶", "#00E676", FontWeights.SemiBold);
                await animateProgress(0, 15, "Creating install directory...");
                Directory.CreateDirectory(installDir);
                await animateProgress(15, 25, "Install directory created.");
                UpdateStepUI(1, "✔", "#00E676", FontWeights.Normal);

                // STEP 2: Copy files
                UpdateStepUI(2, "▶", "#00E676", FontWeights.SemiBold);
                await animateProgress(25, 30, "Preparing file copy...");

                string[] filesToCopy = {
                    "SignalGlance.exe",
                    "SignalGlance.dll",
                    "SignalGlance.deps.json",
                    "SignalGlance.runtimeconfig.json"
                };

                for (int i = 0; i < filesToCopy.Length; i++)
                {
                    string file = filesToCopy[i];
                    string srcPath = Path.Combine(sourceDir, file);
                    string destPath = Path.Combine(installDir, file);

                    int stepStart = 30 + (i * 10);
                    int stepEnd = stepStart + 10;

                    await animateProgress(stepStart, stepEnd, $"Copying {file}...");
                    if (!File.Exists(srcPath))
                    {
                        throw new FileNotFoundException($"Setup files are missing! Could not locate: {srcPath}");
                    }
                    File.Copy(srcPath, destPath, true);
                }
                UpdateStepUI(2, "✔", "#00E676", FontWeights.Normal);

                // STEP 3: Registry startup configuration
                UpdateStepUI(3, "▶", "#00E676", FontWeights.SemiBold);
                if (launchOnStartup)
                {
                    await animateProgress(70, 80, "Writing Windows Startup registry keys...");
                    try
                    {
                        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null)
                            {
                                string exePath = Path.Combine(installDir, "SignalGlance.exe");
                                key.SetValue("SignalGlance", $"\"{exePath}\" --startup");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Registry write warning: {ex.Message}");
                    }
                }
                else
                {
                    await animateProgress(70, 80, "Skipping Windows Startup registration...");
                }
                UpdateStepUI(3, "✔", "#00E676", FontWeights.Normal);

                // STEP 4: Creating shortcuts (Desktop and/or Start Menu)
                UpdateStepUI(4, "▶", "#00E676", FontWeights.SemiBold);
                await animateProgress(80, 90, "Creating shortcut links...");
                
                // Desktop Shortcut
                if (createDesktopShortcut)
                {
                    try
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string shortcutPath = Path.Combine(desktopPath, "SignalGlance.lnk");
                        string exePath = Path.Combine(installDir, "SignalGlance.exe");
                        
                        string vbsScript = $@"
Set sh = CreateObject(""WScript.Shell"")
Set shortcut = sh.CreateShortcut(""{shortcutPath}"")
shortcut.TargetPath = ""{exePath}""
shortcut.WorkingDirectory = ""{installDir}""
shortcut.Description = ""SignalGlance Connection Monitor""
shortcut.Save()";
                        string tempVbs = Path.Combine(Path.GetTempPath(), "shortcut_desktop.vbs");
                        File.WriteAllText(tempVbs, vbsScript);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wscript.exe", $"\"{tempVbs}\"") { CreateNoWindow = true })?.WaitForExit();
                        File.Delete(tempVbs);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Desktop shortcut warning: {ex.Message}");
                    }
                }

                // Start Menu Shortcut (Always Created)
                try
                {
                    string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SignalGlance.lnk");
                    string exePath = Path.Combine(installDir, "SignalGlance.exe");
                    
                    string vbsScript = $@"
Set sh = CreateObject(""WScript.Shell"")
Set shortcut = sh.CreateShortcut(""{startMenuPath}"")
shortcut.TargetPath = ""{exePath}""
shortcut.WorkingDirectory = ""{installDir}""
shortcut.Description = ""SignalGlance Connection Monitor""
shortcut.Save()";
                    string tempVbs = Path.Combine(Path.GetTempPath(), "shortcut_startmenu.vbs");
                    File.WriteAllText(tempVbs, vbsScript);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wscript.exe", $"\"{tempVbs}\"") { CreateNoWindow = true })?.WaitForExit();
                    File.Delete(tempVbs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Start Menu shortcut warning: {ex.Message}");
                }

                UpdateStepUI(4, "✔", "#00E676", FontWeights.Normal);

                // Finish animation
                await animateProgress(90, 100, "Installation complete!");
                await Task.Delay(500);

                // Show Finish Page
                ProgressPage.Visibility = Visibility.Collapsed;
                FinishPage.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "Finish";
                NextButton.IsEnabled = true;
                _currentPage = 4;
                UpdateStepIndicator(4);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void UpdateStepIndicator(int step)
        {
            Dispatcher.Invoke(() =>
            {
                StepNumText.Text = step.ToString();
                
                var greenBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                var emptyBrush = Brushes.Transparent;
                var emptyBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF"));

                Func<System.Windows.Controls.Border, bool, bool> setPill = (pill, isFilled) =>
                {
                    if (isFilled)
                    {
                        pill.Background = greenBrush;
                        pill.BorderThickness = new Thickness(0);
                    }
                    else
                    {
                        pill.Background = emptyBrush;
                        pill.BorderBrush = emptyBorder;
                        pill.BorderThickness = new Thickness(1);
                    }
                    return true;
                };

                setPill(Pill1, step >= 1);
                setPill(Pill2, step >= 2);
                setPill(Pill3, step >= 3);
                setPill(Pill4, step >= 4);
            });
        }

        private void UpdateProgressValue(int percentage)
        {
            Dispatcher.Invoke(() =>
            {
                InstallationProgressBar.Value = percentage;
            });
        }

        private void UpdateProgressText(string status)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressStatusText.Text = status;
            });
        }

        private void UpdateStepUI(int step, string bullet, string hexColor, FontWeight fontWeight)
        {
            Dispatcher.Invoke(() =>
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
                
                if (step == 1)
                {
                    Step1Bullet.Text = bullet;
                    Step1Bullet.Foreground = brush;
                    Step1Text.Foreground = brush;
                    Step1Text.FontWeight = fontWeight;
                }
                else if (step == 2)
                {
                    Step2Bullet.Text = bullet;
                    Step2Bullet.Foreground = brush;
                    Step2Text.Foreground = brush;
                    Step2Text.FontWeight = fontWeight;
                }
                else if (step == 3)
                {
                    Step3Bullet.Text = bullet;
                    Step3Bullet.Foreground = brush;
                    Step3Text.Foreground = brush;
                    Step3Text.FontWeight = fontWeight;
                }
                else if (step == 4)
                {
                    Step4Bullet.Text = bullet;
                    Step4Bullet.Foreground = brush;
                    Step4Text.Foreground = brush;
                    Step4Text.FontWeight = fontWeight;
                }
            });
        }
    }
}
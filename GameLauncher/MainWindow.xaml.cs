using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Sentry;
using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using GameLauncher.Services.Concrete;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string APP_NAME = "avio-test";
        private const string MAIN_EXE = "AvioTest.exe";
        private const string UPDATER_NAME = "Updater";
        private const string UPDATER_EXE = "Updater.exe";
        private const string LAUNCHER_NAME = "GameLauncher";
        private const string LAUNCHER_EXE = "GameLauncher.exe";

        private readonly string _localAppData;
        private readonly string _appDirectory;
        private readonly string _gameDirectory;
        private readonly string _backupDirectory;
        private readonly string _localVersionFile;
        private readonly string _updaterDirectory;

        private readonly VersionService _versionService;

        private ServerManifest _localManifest;
        private AppVersionDto _launcherVersion;
        private AppVersionDto _updaterVersion;
        private AppVersionDto _gameVersion;

        private readonly INetworkService _networkService;
        private readonly IFileService _fileService;

        public MainWindow()
        {
            InitializeComponent();
            // Local App Klasör Yolunu getirir.
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _appDirectory = Path.Combine(_localAppData, APP_NAME);
            _updaterDirectory = Path.Combine(_appDirectory, "System");
            _localVersionFile = Path.Combine(_appDirectory, "version.json");
            _gameDirectory = Path.Combine(_appDirectory, "Game");
            _backupDirectory = Path.Combine(_appDirectory, "Backups");

            _versionService = new VersionService();

            _networkService = new NetworkService();
            _fileService = new FileService();

            Loaded += async (s, e) => await InitializeAsync();

        }

        private async Task InitializeAsync()
        {
            try
            {
                await LoadLocalManifestAsync();
                UpdateStatus("Başlatılıyor...");

                Directory.CreateDirectory(_appDirectory);

                var hasInternet = await CheckInternetConnectionAsync();

                if (!hasInternet)
                {
                    HandleNoInternetConnection();
                    return;
                }

                UpdateStatus("Versiyonlar kontrol ediliyor...");

                // Verison manifestlarını yükle
                _launcherVersion = await _versionService.GetLatestVersion("Launcher", "Windows");
                _updaterVersion = await _versionService.GetLatestVersion("Updater", "Windows");
                _gameVersion = await _versionService.GetLatestVersion("AvioTest", "Windows");

                if (_launcherVersion == null || _updaterVersion == null || _gameVersion == null)
                {
                    UpdateStatus("Sunucuya bağlanılamadı", isOffline: true);
                    HandleNoInternetConnection();
                    return;
                }

                await CheckPhysicalIntegrityAsync();

                string[] args = Environment.GetCommandLineArgs();
                bool justUpdated = Array.Exists(args, arg => arg == "/post_update");

                if (justUpdated)
                {
                    UpdateStatus("Güncelleme tamamlandı, yapılandırılıyor...");

                    _localManifest.launcher.Version = _launcherVersion.Version;
                    _localManifest.launcher.DownloadUrl = _launcherVersion.DownloadUrl;
                    _localManifest.launcher.ChecksumSha256 = _launcherVersion.ChecksumSha256;
                    _localManifest.launcher.Size = _launcherVersion.Size;
                    await SaveLocalManifestAsync();
                }

                // Updater güncellemesi kontrolü
                if (_updaterVersion.Version > _localManifest.updater.Version)
                {
                    UpdateStatus("Sistem bileşenleri güncelleniyor...");
                    var tempFileName = $"{UPDATER_NAME}_{Guid.NewGuid()}_update.zip";
                    var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

                    try
                    {
                        await DownloadFileAsync(_updaterVersion.DownloadUrl, tempZipPath);


                        if (Directory.Exists(_updaterDirectory))
                        {
                            Directory.Delete(_updaterDirectory, true);
                        }

                        await _fileService.ExtractZipAsync(tempZipPath, _updaterDirectory);

                        await CleanupOldUpdaterFiles();

                        _localManifest.updater.Version = _updaterVersion.Version;
                        _localManifest.updater.DownloadUrl = _updaterVersion.DownloadUrl;
                        _localManifest.updater.ChecksumSha256 = _updaterVersion.ChecksumSha256;
                        _localManifest.updater.Size = _updaterVersion.Size;
                        await SaveLocalManifestAsync();
                    }
                    catch (IOException ex)
                    {
                        UpdateStatus("Dosyalar kilitli!", isOffline: true);
                        MessageBox.Show("Oyun dosyaları şu anda başka bir program tarafından kullanılıyor.\n\nLütfen oyunun kapalı olduğundan emin olun ve tekrar deneyin.",
                                        "Dosya Kilitli", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        UpdateStatus("Erişim engellendi!", isOffline: true);
                        MessageBox.Show("Dosyalara erişim izni yok. Antivirüs programınız kurulumu engelliyor olabilir veya Launcher'ı Yönetici olarak çalıştırmanız gerekebilir.",
                                        "Erişim Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.CaptureException(ex);
                        MessageBox.Show($"Updater güncellenemedi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    finally
                    {
                        if (File.Exists(tempZipPath))
                        {
                            try { File.Delete(tempZipPath); } catch { /* Silinemezse, Windows halleder */ }
                        }
                    }
                }

                // Laucnher güncelleme kontrolü
                if (_launcherVersion.Version > _localManifest.launcher.Version)
                {
                    UpdateStatus("Sistem bileşenleri güncelleniyor...");
                    var tempFileName = $"{LAUNCHER_NAME}_${Guid.NewGuid()}_launcher.zip";
                    var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

                    try
                    {
                        await DownloadFileAsync(_launcherVersion.DownloadUrl, tempZipPath);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.CaptureException(ex);
                        MessageBox.Show($"Güncelleme dosyası indirilemedi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string updaterExe = Path.Combine(_updaterDirectory, UPDATER_EXE);
                    
                    if (!File.Exists(updaterExe))
                    {
                        SentrySdk.CaptureMessage("Updater.exe bulunamadı: " + updaterExe, SentryLevel.Error);
                        MessageBox.Show("Kritik sistem dosyası eksik. Lütfen uygulamayı yeniden kurun.",
                            "Dosya Eksik", MessageBoxButton.OK, MessageBoxImage.Error);
                        Application.Current.Shutdown();
                        return;
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = updaterExe,
                        // ARGÜMANLARIN ANLAMI:
                        // /mode:launcher       -> "Ben Launcher'ı güncelleyeceğim" modu.
                        // /source:"..."        -> "Zip dosyası burada, bunu al."
                        // /target:"..."        -> "Launcher'ın olduğu klasör burası."
                        // /pid:1234            -> "Bu ID'li program (Launcher) kapanana kadar bekle."
                        Arguments = $"/mode:launcher /source:\"{tempZipPath}\" /target:\"{_appDirectory}\" /pid:{Process.GetCurrentProcess().Id}",
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(updaterExe)
                    };

                    try
                    {
                        Process.Start(startInfo);
                    }
                    catch (Exception ex)
                    {
                        SentrySdk.CaptureException(ex);
                        MessageBox.Show($"Updater başlatılamadı:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 4. UYGULAMAYI KAPAT (Sahne artık Updater'ın)
                    Application.Current.Shutdown();
                    return;
                }

                // Game update kontrolü
                UpdateStatus("Uygulama versiyonu kontrol ediliyor...");
                bool isGameNew = _gameVersion.Version > _localManifest.game.Version;
                bool isMandotary = _gameVersion.IsMandatory;

                if (_gameVersion.MinVersion != null)
                {
                    if (_gameVersion.MinVersion > _localManifest.game.Version)
                        isMandotary = true;
                }

                if (isGameNew)
                {
                    if (isMandotary)
                    {
                        await DownloadAndInstallUpdateAsync();
                    }
                    else
                    {
                        var result = MessageBox.Show(
                         $"Yeni bir güncelleme mevcut (v{_gameVersion.Version}).\n" +
                         "Şimdi güncellemek ister misiniz?\n\n" +
                         "(Hayır derseniz mevcut sürümle uygulamaya girebilirsiniz.)",
                         "Güncelleme Mevcut",
                         MessageBoxButton.YesNo,
                         MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            await DownloadAndInstallUpdateAsync();
                        }
                        else
                        {
                            UpdateStatus("Güncelleme ertelendi.");
                        }
                    }
                }
                else
                {
                    UpdateStatus("Uygulama güncel!");
                }

                ShowPlayButton();
            }
            catch (IOException ex)
            {
                UpdateStatus("Dosyalar kilitli!", isOffline: true);
                MessageBox.Show("Oyun dosyaları şu anda başka bir program tarafından kullanılıyor.\n\nLütfen oyunun kapalı olduğundan emin olun ve tekrar deneyin.",
                                "Dosya Kilitli", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                UpdateStatus("Erişim engellendi!", isOffline: true);
                MessageBox.Show("Dosyalara erişim izni yok. Antivirüs programınız kurulumu engelliyor olabilir veya Launcher'ı Yönetici olarak çalıştırmanız gerekebilir.",
                                "Erişim Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("3 kez"))
                {
                    UpdateStatus("Bağlantı veya disk sorunu", isOffline: true);
                    MessageBox.Show(ex.Message, "İndirme Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                    // KAPANMIYOR, kullanıcı Retry butonuna basabilir.
                }
                else
                {
                    SentrySdk.CaptureException(ex);
                    UpdateStatus("Kritik Hata Oluştu!");
                    MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                }
            }
        }

        private async Task CleanupOldUpdaterFiles()
        {
            try
            {
                string[] oldFiles = {
                    UPDATER_EXE,
                    "Updater.pdb",
                    "Updater.deps.json",
                    "Updater.runtimeconfig.json"
                };

                foreach (var fileName in oldFiles)
                {
                    string filePath = Path.Combine(_appDirectory, fileName);
                    await _fileService.DeleteFileAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Eski dosyalar silinemedi");
            }
        }

        /// <summary>
        /// Downloads the latest update package, verifies its integrity, creates a backup of the current installation,
        /// and installs the update asynchronously.
        /// </summary>
        /// <remarks>This method updates the application by downloading the update package from the URL
        /// specified in the current manifest, verifying its SHA-256 hash if provided, backing up the existing
        /// installation, and extracting the new files. The method also updates the local manifest to reflect the new
        /// version. Progress and status updates are reported during the operation. Temporary files created during the
        /// process are deleted upon completion.</remarks>
        /// <returns>A task that represents the asynchronous download and installation operation.</returns>
        /// <exception cref="Exception">Thrown if the downloaded file fails integrity verification or if an error occurs during the download or
        /// installation process.</exception>
        private async Task DownloadAndInstallUpdateAsync()
        {
            var tempFileName = $"{APP_NAME}_${Guid.NewGuid()}_update.zip";
            var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

            int maxRetryCount = 3;
            bool isVerified = false;

            try
            {
                var progressHandler = new Progress<DownloadState>(state =>
                {
                    DownloadProgress.Value = state.Percentage;
                    PercentageText.Text = $"%{state.Percentage:F0}";
                    SpeedText.Text = $"{state.SpeedMBps:F2}MB/s";
                });

                for (int attempt = 1; attempt <= maxRetryCount; attempt++)
                {
                    try
                    {
                        UpdateStatus(attempt > 1 ? $"Yeniden indiriliyor... (Deneme {attempt}/{maxRetryCount})" : $"İndiriliyor... v{_gameVersion.Version}");

                        await _networkService.DownloadFilesAsync(_gameVersion.DownloadUrl, tempZipPath, progressHandler);

                        UpdateStatus("Dosya doğrulanıyor...");
                        isVerified = await _fileService.VerifyFileHashAsync(tempZipPath, _gameVersion.ChecksumSha256);

                        if (isVerified)
                        {
                            break;
                       
                        } 
                        else
                        {
                            UpdateStatus("Bozuk dosya tespit edildi, temizleniyor...");
                            await _fileService.DeleteFileAsync(tempZipPath);
                        }

                    }
                    catch (HttpRequestException) 
                    {
                        if (attempt == maxRetryCount) throw;
                        await Task.Delay(1000);
                    }
                }

                if (!isVerified)
                {
                    throw new Exception("Güncelleme dosyası 3 kez indirilmesine rağmen doğrulanamadı. İnternet bağlantınızda veya diskte bir sorun olabilir.");
                }

                // Güncelleme yapmadan önce yedekleme işlemini başlatıyoruz.
                UpdateStatus("Yedekleme yapılıyor...");
                await _fileService.CreateBackupAsync(_gameDirectory, _backupDirectory);

                // Eski dosyalar siliniyor...
                UpdateStatus("Temizleniyor...");
                await _fileService.CleanDirectoryAsync(_gameDirectory);

                UpdateStatus("Kuruluyor...");
                await _fileService.ExtractZipAsync(tempZipPath, _gameDirectory, overwrite: true);

                _localManifest.game = _gameVersion;
                await SaveLocalManifestAsync();

                UpdateStatus("Güncelleme tamamlandı!");
                await Task.Delay(1000);

            }
            finally
            {
                // En son indirilen geçici dosyayı sil!
                await _fileService.DeleteFileAsync(tempZipPath);
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            var dir = new DirectoryInfo(sourceDir);

            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestDir = Path.Combine(destDir, subDir.Name);
                CopyDirectoryRecursive(subDir.FullName, newDestDir);
            }
        }

        private async Task<bool> CheckInternetConnectionAsync()
        {
            UpdateStatus("İnternet bağlantısı kontrol ediliyor...");
            return await _networkService.CheckInternetConnectionAsync();
        }

        private void HandleNoInternetConnection()
        {
            UpdateStatus("İnternet bağlantısı yok", isOffline: true);

            var exePath = Path.Combine(_gameDirectory, MAIN_EXE);
            bool appExists = File.Exists(exePath);

            if (appExists)
            {
                try
                {
                    LaunchMainApplication();
                }
                catch (Exception ex)
                {
                    SentrySdk.CaptureException(ex);
                    MessageBox.Show($"Uygulama başlatılamadı: {ex.Message}", "Hata",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(
                    "İnternet bağlantısı bulunamadı.\n\n" +
                    "Uygulamanın çalışabilmesi için ilk kurulumun yapılması gerekmektedir.\n" +
                    "Lütfen internet bağlantınızı kontrol edip tekrar deneyin.",
                    "Bağlantı Gerekli",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Application.Current.Shutdown();
            }
        }

        private void LaunchMainApplication()
        {
            var exePath = Path.Combine(_gameDirectory, MAIN_EXE);

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Ana uygulama bulunamadı!", exePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _gameDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            Process.Start(startInfo);

            Task.Delay(500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => Application.Current.Shutdown());
            });
        }

        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            var progressHandler = new Progress<DownloadState>(state =>
            {
                DownloadProgress.Value = state.Percentage;
                PercentageText.Text = $"%{state.Percentage:F0}";
                SpeedText.Text = $"{state.SpeedMBps:F2} MB/s";
            });
            
            await _networkService.DownloadFilesAsync(url, destinationPath, progressHandler);
        }

        private async Task LoadLocalManifestAsync()
        {
            if (File.Exists(_localVersionFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_localVersionFile);
                    _localManifest = JsonSerializer.Deserialize<ServerManifest>(json);

                    _localManifest.launcher ??= new AppVersionDto { Version = new Version("0.0.0") };
                    _localManifest.updater ??= new AppVersionDto { Version = new Version("0.0.0") };
                    _localManifest.game ??= new AppVersionDto { Version = new Version("0.0.0") };
                }
                catch
                {
                    _localManifest = _localManifest = new ServerManifest
                    {
                        launcher = new AppVersionDto { Version = new Version("0.0.0") },
                        updater = new AppVersionDto { Version = new Version("0.0.0") },
                        game = new AppVersionDto { Version = new Version("0.0.0") }
                    };
                }
            }
            else
            {
                _localManifest = new ServerManifest
                {
                    launcher = new AppVersionDto { Version = new Version("0.0.0") },
                    updater = new AppVersionDto { Version = new Version("0.0.0") },
                    game = new AppVersionDto { Version = new Version("0.0.0") }
                };
            }

            Dispatcher.Invoke(() =>
            {
                VersionText.Text = $"v{_localManifest.game.Version}";
            });
        }
        private async Task SaveLocalManifestAsync()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_localManifest, options);
            await File.WriteAllTextAsync(_localVersionFile, json);
        }

        private Task CheckPhysicalIntegrityAsync()
        {
            bool manifestDirty = false;

            string updaterExePath = Path.Combine(_updaterDirectory, UPDATER_EXE);
            if (!Directory.Exists(_updaterDirectory) || !File.Exists(updaterExePath))
            {
                UpdateStatus("Sistem bileşenleri eksik veya bozuk, Onarılıyor...");
                _localManifest.updater.Version = new Version("0.0.0");
                manifestDirty = true;
            }

            string launcherExePath = Path.Combine(_appDirectory, LAUNCHER_EXE);
            if (!Directory.Exists(_appDirectory) || !File.Exists(launcherExePath))
            {
                UpdateStatus("Sistem bileşenleri eksik veya bozuk, Onarılıyor...");
                _localManifest.launcher.Version = new Version("0.0.0");
                manifestDirty = true;
            }

            string gameExePath = Path.Combine(_gameDirectory, MAIN_EXE);
            if (!Directory.Exists(_gameDirectory) || !File.Exists(gameExePath))
            {
                UpdateStatus("Oyun dosyaları eksik veya bozuk, Onarılıyor...");
                _localManifest.game.Version = new Version("0.0.0");
                manifestDirty = true;
            }

            if (manifestDirty)
            {
                return SaveLocalManifestAsync();
            }
          
            return Task.CompletedTask;
        }

        private void ShowPlayButton()
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgress.Visibility = Visibility.Collapsed;
                PercentageText.Visibility = Visibility.Collapsed;
                SpeedText.Visibility = Visibility.Collapsed;
                PlayButton.Visibility = Visibility.Visible;
            });
        }

        private void UpdateStatus(string message, bool isOffline = false)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;

                if (isOffline)
                {
                    RetryButton.Visibility = Visibility.Visible;

                }
                else
                {
                    RetryButton.Visibility = Visibility.Collapsed;

                }
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LaunchMainApplication();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uygulama başlatılamadı: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            RetryButton.IsEnabled = false;
            RetryButton.Visibility = Visibility.Collapsed;

            UpdateStatus("Yeniden bağlanmaya çalışılıyor...");
            DownloadProgress.Value = 0;
            PercentageText.Text = "%0";
            SpeedText.Text = "0 MB/s";

            try
            {
                await InitializeAsync();
            }
            finally
            {
                // 4. İşlem bittiğinde (başarılı ya da başarısız) butonu tekrar aktif et.
                // (Eğer başarılı olursa zaten InitializeAsync içindeki ShowPlayButton() butonu gizleyecektir)
                RetryButton.IsEnabled = true;
            }
        }

        private void BottomButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

    }
    public class ServerManifest
    {
        public AppVersionDto launcher { get; set; }
        public AppVersionDto updater { get; set; }
        public AppVersionDto game { get; set; }
        public DateTime lastUpdated { get; set; }
        public bool initalRelease { get; set; }
    }
}
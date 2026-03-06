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
using GameLauncher.Entites;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string VERSION_URL = "https://files.aviotest.org/version.json"; // R2 linki!
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

        private readonly HttpClient _httpClient;
        private readonly ApiClient _apiClient;

        private ServerManifest _localManifest;
        private AppVersionDto _launcherVersion;
        private AppVersionDto _updaterVersion;
        private AppVersionDto _gameVersion;

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

            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _apiClient = new ApiClient();

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
                _launcherVersion = await _apiClient.GetLatestVersion("Launcher", "Windows");
                _updaterVersion = await _apiClient.GetLatestVersion("Updater", "Windows");
                _gameVersion = await _apiClient.GetLatestVersion("AvioTest", "Windows");

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
                    var tempFileName = $"{UPDATER_NAME}_${Guid.NewGuid()}_update.zip";
                    var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);

                    try
                    {
                        await DownloadFileAsync(_updaterVersion.DownloadUrl, tempZipPath);

                        if (Directory.Exists(_updaterDirectory))
                        {
                            Directory.Delete(_updaterDirectory, true);
                        }

                        Directory.CreateDirectory(_updaterDirectory);
                        ZipFile.ExtractToDirectory(tempZipPath, _updaterDirectory);

                        CleanupOldUpdaterFiles();

                        _localManifest.updater.Version = _updaterVersion.Version;
                        _localManifest.updater.DownloadUrl = _updaterVersion.DownloadUrl;
                        _localManifest.updater.ChecksumSha256 = _updaterVersion.ChecksumSha256;
                        _localManifest.updater.Size = _updaterVersion.Size;
                        await SaveLocalManifestAsync();
                    }
                    catch (Exception ex)
                    {
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
                        MessageBox.Show($"Güncelleme dosyası indirilemedi:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string updaterExe = Path.Combine(_updaterDirectory, UPDATER_EXE);
                    if (!File.Exists(updaterExe))
                    {
                        MessageBox.Show("Kritik Hata: Updater.exe bulunamadı! Lütfen uygulamayı web sitesinden tekrar indirip kurun.", "Dosya Eksik", MessageBoxButton.OK, MessageBoxImage.Error);
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
            catch (Exception ex)
            {
                UpdateStatus("Hata oluştu!");
                MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{ex.Message}",
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void CleanupOldUpdaterFiles()
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
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
            }
            catch (Exception)
            {
                // Debug.WriteLine("Eski dosyalar silinemedi: " + ex.Message);
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

            try
            {
                UpdateStatus($"İndiriliyor... v{_gameVersion.Version}");

                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // ResponseHeadersRead fonksiyonu tüm dosyayı RAM'e indireye çalışmaz. Dosyayı indirirken işlemeye başlar. Bir akış sağlar.
                    var downloadResponse = await _httpClient.GetAsync(_gameVersion.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    downloadResponse.EnsureSuccessStatusCode();

                    // Gönderilen dosya boyutu biliniyorsa ilerleme çubuğunu güncelleyebiliriz.
                    var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;

                    using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192 * 4];
                        long totalRead = 0;
                        int bytesRead;
                        var stopwatch = Stopwatch.StartNew();

                        DateTime lastUpdate = DateTime.MinValue;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress && (DateTime.Now - lastUpdate).TotalMicroseconds > 300)
                            {
                                lastUpdate = DateTime.Now;
                                var progress = (double)totalRead / totalBytes * 100;
                                var speed = totalRead / stopwatch.Elapsed.TotalSeconds / 1024 / 1024; // MB/s

                                Dispatcher.Invoke(() =>
                                {
                                    DownloadProgress.Value = progress;
                                    PercentageText.Text = $"{progress:F0}%";
                                    SpeedText.Text = $"{speed:F2} MB/s";
                                });
                            }
                        }
                    }
                }

                UpdateStatus("Dosya doğrulanıyor...");
                DownloadProgress.Value = 100;
                PercentageText.Text = "100%";

                if (!string.IsNullOrEmpty(_gameVersion.ChecksumSha256))
                {
                    if (!VerifyFileHash(tempZipPath, _gameVersion.ChecksumSha256))
                    {
                        throw new Exception("Dosya bütünlüğü doğrulanamadı!");
                    }
                }

                // Güncelleme yapmadan önce yedekleme işlemini başlatıyoruz.
                if (Directory.Exists(_gameDirectory))
                {
                    UpdateStatus("Yedekleme yapılıyor...");
                    CreateBackup(_gameDirectory, _backupDirectory);
                }

                // Eski dosyalar siliniyor...
                UpdateStatus("Temizleniyor...");
                CleanOldVersion();

                UpdateStatus("Kuruluyor...");
                ZipFile.ExtractToDirectory(tempZipPath, _gameDirectory, overwriteFiles: true);

                _localManifest.game = _gameVersion;
                await SaveLocalManifestAsync();

                UpdateStatus("Güncelleme tamamlandı!");
                await Task.Delay(1000);

            }
            finally
            {
                // En son indirilen geçici dosyayı sil!
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
        }

        private bool VerifyFileHash(string filePath, string expectedHash)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
            return hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        private void CleanOldVersion()
        {
            if (!Directory.Exists(_gameDirectory))
            {
                Directory.CreateDirectory(_gameDirectory);
                return;
            }

            try
            {
                DirectoryInfo gameDir = new DirectoryInfo(_gameDirectory);

                var protectedExtensions = new[] { ".sav", ".save", ".dat", ".user" };
                var protectedFiles = new[] { "settings.ini", "config.cfg", "userdata.db", "config.json" };
                var protectedFolders = new[] { "Screenshots", "Saves", "Logs", "cache" };

                foreach (FileInfo file in gameDir.GetFiles())
                {
                    var ext = file.Extension.ToLower(); // .exe, .dll vb.
                    var name = file.Name;

                    bool isProtected = protectedExtensions.Contains(ext) || protectedFiles.Contains(name);

                    if (!isProtected)
                    {
                        if (file.IsReadOnly) file.IsReadOnly = false;
                        file.Delete();
                    }
                }

                foreach (DirectoryInfo dir in gameDir.GetDirectories())
                {
                    if (!protectedFolders.Contains(dir.Name))
                    {
                        dir.Delete(true);
                    }
                }

            }
            catch (IOException ex)
            {
                MessageBox.Show($"Temizlik yapılırken hata oluştu. Lütfen uygulamanın kapalı olduğundan emin olun.\n\nHata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Temizlik hatası: " + ex.Message);
            }
        }

        private void CreateBackup(string sourceDir, string backupDir)
        {
            if (!(Directory.Exists(sourceDir))) return;

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Directory.Delete(backupDir, true);
                }
                Directory.CreateDirectory(backupDir);

                CopyDirectoryRecursive(sourceDir, backupDir);

                UpdateStatus("Yedekleme Tamamlandı");
            }
            catch (Exception e)
            {
                MessageBox.Show($"Yedekleme hatası: {e.Message}");
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
            try
            {
                UpdateStatus("İnternet bağlantısı kontrol ediliyor...");

                var request = new HttpRequestMessage(HttpMethod.Head, VERSION_URL);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                // Hata ayrıntısını görmek için developer modda açılabilir.
                //var errorDetails = $"Hata: {ex.Message}\n\n";
                //if (ex.InnerException != null)
                //{
                //    errorDetails += $"Inner Exception: {ex.InnerException.Message}\n\n";
                //    if (ex.InnerException.InnerException != null)
                //    {
                //        errorDetails += $"Inner Inner: {ex.InnerException.InnerException.Message}";
                //    }
                //}

                //MessageBox.Show(errorDetails, "Bağlantı Hatası",
                //    MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }
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


        /// <summary>
        /// Asynchronously downloads a file from the specified URL and saves it to the given destination path.
        /// </summary>
        /// <remarks>Progress updates may be reported during the download if the total file size is known.
        /// The method overwrites the destination file if it already exists.</remarks>
        /// <param name="url">The URL of the file to download. Must be a valid, absolute URI.</param>
        /// <param name="destinationPath">The full file system path where the downloaded file will be saved. If the file exists, it will be
        /// overwritten.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        private async Task DownloadFileAsync(string url, string destinationPath)
        {
            using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReport = totalBytes != -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                {
                    var buffer = new byte[8192 * 4];
                    long totalRead = 0;
                    int bytesRead;

                    var stopwatch = Stopwatch.StartNew();

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        var speed = totalRead / stopwatch.Elapsed.TotalSeconds / 1024 / 1024; // MB/s

                        if (canReport)
                        {
                            var progress = (double)totalRead / totalBytes * 100;
                            Dispatcher.Invoke(() =>
                            {
                                DownloadProgress.Value = progress;
                                PercentageText.Text = $"%{progress:F0}";
                                SpeedText.Text = $"{speed:F2} MB/s";
                            });
                        }
                    }
                }
            }
        }

        private async Task LoadLocalManifestAsync()
        {
            if (File.Exists(_localVersionFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_localVersionFile);
                    _localManifest = JsonSerializer.Deserialize<ServerManifest>(json);
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

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {

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
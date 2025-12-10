using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace GameLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string VERSION_URL = "https://pub-dd7bff491d404728990afee779fd43a1.r2.dev/version.json"; // R2 linki!
        private const string APP_NAME = "avio-test";
        private const string MAIN_EXE = "AvioTest.exe";

        private readonly string _localAppData;
        private readonly string _appDirectory;
        private readonly string _localVersionFile;
        private readonly HttpClient _httpClient;
        private Storyboard _spinnerAnimation;

        public MainWindow()
        {
            InitializeComponent();
            // Local App Klasör Yolunu getirir.
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _appDirectory = Path.Combine(_localAppData, APP_NAME);
            _localVersionFile = Path.Combine(_appDirectory, "version.json");

            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            InitializeSpinnerAnimation();
            Loaded += async (s, e) => await InitializeAsync();

        }

        private void InitializeSpinnerAnimation()
        {
            var rotation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _spinnerAnimation = new Storyboard();
            _spinnerAnimation.Children.Add(rotation);
            Storyboard.SetTarget(rotation, SpinnerRotation);
            Storyboard.SetTargetProperty(rotation, new PropertyPath("Angle"));
        }

        private async Task InitializeAsync()
        {
            try
            {
                UpdateStatus("Başlatılıyor...", showSpinner: true);

                Directory.CreateDirectory(_appDirectory);

                var hasInternet = await CheckInternetConnectionAsync();

                if (!hasInternet)
                {
                    HandleNoInternetConnection();
                    return;
                }

                var updateNeeded = await CheckForUpdateAsync();

                if (updateNeeded)
                {
                    await DownloadAndInstallUpdateAsync();
                }
                else
                {
                    UpdateStatus("Hazır!", showSpinner: false);
                    await Task.Delay(500);
                }

                ShowPlayButton();

            }
            catch (Exception ex)
            {
                UpdateStatus("Hata oluştu!", showSpinner: false);
                MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{ex.Message}",
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private async Task DownloadAndInstallUpdateAsync()
        {
            var serverJson = await _httpClient.GetStringAsync(VERSION_URL);
            var versionInfo = JsonSerializer.Deserialize<VersionInfo>(serverJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });


            var tempZipPath = Path.Combine(Path.GetTempPath(), $"{APP_NAME}_update.zip");

            try
            {
                UpdateStatus($"İndiriliyor... v{versionInfo.Version}", showSpinner: true);

                using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    // ResponseHeadersRead fonksiyonu tüm dosyayı RAM'e indireye çalışmaz. Dosyayı indirriken işlemey başlar. Bir akış sağlar.
                    var downloadResponse = await _httpClient.GetAsync(versionInfo.Url, HttpCompletionOption.ResponseHeadersRead);
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

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (canReportProgress)
                            {
                                var progress = (double)totalRead / totalBytes * 100;
                                var speed = totalRead / stopwatch.Elapsed.TotalSeconds / 1024 / 1024; // MB/s

                                Dispatcher.Invoke(() =>
                                {
                                    DownloadProgress.Value = progress;
                                    PercentageText.Text = $"{progress:F1}%";
                                    SpeedText.Text = $"{speed:F2} MB/s";
                                });
                            }
                        }
                    }
                }

                UpdateStatus("Dosya doğrulanıyor...", showSpinner: true);
                DownloadProgress.Value = 100;
                PercentageText.Text = "100%";

                if (!string.IsNullOrEmpty(versionInfo.Sha256))
                {
                    if (!VerifyFileHash(tempZipPath, versionInfo.Sha256))
                    {
                        throw new Exception("Dosya bütünlüğü doğrulanamadı!");
                    }
                }

                // Güncelleme yapmadan önce yedekleme işlemini başlatıyoruz.
                var backupDir = Path.Combine(_appDirectory, "backup");
                if (Directory.Exists(_appDirectory))
                {
                    UpdateStatus("Yedekleme yapılıyor...", showSpinner: true);
                    CreateBackup(backupDir);
                }

                // Eski dosyalar siliniyor...
                UpdateStatus("Temizleniyor...", showSpinner: true);
                CleanOldVersion();

                UpdateStatus("Kuruluyor...", showSpinner: true);
                ZipFile.ExtractToDirectory(tempZipPath, _appDirectory, overwriteFiles: true);

                var localVersionInfo = new VersionInfo
                {
                    Version = versionInfo.Version,
                    Url = versionInfo.Url
                };

                var localJson = JsonSerializer.Serialize(localVersionInfo, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_localVersionFile, localJson);

                UpdateStatus("Güncelleme tamamlandı!", showSpinner: false);
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
            var protectedFiles = new[] { "config.json", "userdata.db", "version.json" };
            var protectedDirs = new[] { "backup", "logs", "cache" };

            foreach (var file in Directory.GetFiles(_appDirectory))
            {
                var fileName = Path.GetFileName(file);
                if (!Array.Exists(protectedFiles, pf => pf.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            foreach (var dir in Directory.GetDirectories(_appDirectory))
            {
                var dirName = Path.GetFileName(dir);
                if (!Array.Exists(protectedDirs, pd => pd.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
        }

        private void CreateBackup(string backupDir)
        {
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }

            Directory.CreateDirectory(backupDir);

            foreach (var file in Directory.GetFiles(_appDirectory, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).ToLower();
                if (ext == ".exe" || ext == ".dll" || ext == ".config")
                {
                    var fileName = Path.GetFileName(file);
                    // app Directory dosyalarını yedekliyoruz.
                    File.Copy(file, Path.Combine(backupDir, fileName), true);
                }
            }
        }

        private async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                UpdateStatus("İnternet bağlantısı kontrol ediliyor...", showSpinner: true);
                // Çok hızlı bir istek atılır ve yanıt dönüp dönmediği kontrol edilir.
                // Tam bir GET gibi çalışıp tüm içeriği indirmez!
                var request = new HttpRequestMessage(HttpMethod.Head, VERSION_URL);
                // Header'ı aldığın ana işlemi tamamla bitir!
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private void HandleNoInternetConnection()
        {
            UpdateStatus("İnternet bağlantısı yok", showSpinner: false);

            var exePath = Path.Combine(_appDirectory, MAIN_EXE);
            bool appExists = File.Exists(exePath);

            if (appExists)
            {
                // SENARYO 1: Oyun yüklü ama internet yok -> UYARI verip oyunu açıyoruz.
                MessageBox.Show(
                    "İnternet bağlantısı bulunamadı.\n\n" +
                    "Güncellemeler kontrol edilemedi ancak uygulamanın yüklü bir sürümü bulundu.\n" +
                    "Uygulama Çevrimdışı (Offline) modda başlatılıyor.",
                    "Çevrimdışı Mod", // Başlık düzeltildi
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning // Hata değil, Uyarı ikonu daha doğru
                );

                try
                {
                    LaunchMainApplication();
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Uygulama başlatılamadı: {ex.Message}", "Hata",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // SENARYO 2: Oyun hiç yüklü değil ve internet yok -> HATA verip kapatıyoruz.
                MessageBox.Show(
                    "İnternet bağlantısı bulunamadı.\n\n" +
                    "Uygulamanın çalışabilmesi için ilk kurulumun yapılması gerekmektedir.\n" +
                    "Lütfen internet bağlantınızı kontrol edip tekrar deneyin.",
                    "Bağlantı Gerekli", // Başlık düzeltildi
                    MessageBoxButton.OK,
                    MessageBoxImage.Error // Bu bir hata, çünkü oyun açılamıyor.
                );

                Application.Current.Shutdown();
            }
        }

        private async Task<bool> CheckForUpdateAsync()
        {

            UpdateStatus("Güncellemeler kontrol ediliyor...", showSpinner: true);

            var serverJson = await _httpClient.GetStringAsync(VERSION_URL);
            var serverVersion = JsonSerializer.Deserialize<VersionInfo>(serverJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            VersionInfo localVersion = null;
            if (File.Exists(_localVersionFile))
            {
                var localJson = await File.ReadAllTextAsync(_localVersionFile);
                localVersion = JsonSerializer.Deserialize<VersionInfo>(localJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            var mainExePath = Path.Combine(_appDirectory, MAIN_EXE);
            // İlk kurulum...
            if (!File.Exists(mainExePath))
            {
                return true;
            }
            // version.json dosyamız yoksa'da güncelleme gerekli.
            if (localVersion == null)
            {
                return true;
            }

            // Version sınıfı karşılaştırmanın doğru olması için gereklidir.
            var local = new Version(localVersion.Version);
            var server = new Version(serverVersion.Version);

            Dispatcher.Invoke(() =>
            {
                if (server > local)
                {
                    VersionText.Text = $"v{localVersion.Version} → v{serverVersion.Version}";

                }
                else
                {
                    VersionText.Text = $"v{localVersion.Version}";
                }
            });

            return server > local;
        }

        private void LaunchMainApplication()
        {
            var exePath = Path.Combine(_appDirectory, MAIN_EXE);

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Ana uygulama bulunamadı!", exePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _appDirectory,
                UseShellExecute = false
            };

            Process.Start(startInfo);
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

        private void UpdateStatus(string message, bool showSpinner)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                SpinnerRing.Opacity = showSpinner ? 1 : 0;

                if (showSpinner)
                {
                    _spinnerAnimation.Begin();
                }
                else
                {
                    _spinnerAnimation.Stop();
                }
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double click - do nothing (no maximize)
            }
            else
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
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uygulama başlatılamadı: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    public class VersionInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public bool Mandatory { get; set; }
    }
}
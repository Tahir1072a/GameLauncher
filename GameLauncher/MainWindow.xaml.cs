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
using System.Windows.Media.Animation;

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

                UpdateCheckResult updateResult = await CheckForUpdatesAsync();

                if (updateResult.IsMandatory)
                {
                    await DownloadAndInstallUpdateAsync();
                }
                else if (updateResult.UpdateNeeded)
                {
                    var result = MessageBox.Show(
                        "Yeni bir güncelleme mevcut. Güncelleme yapmak ister misiniz?",
                        "Güncelleme Mevcut",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        await DownloadAndInstallUpdateAsync();
                    }
                    else
                    {
                        UpdateStatus("Güncelleme atlandı, hazır!", showSpinner: false);
                        await Task.Delay(500);
                    }
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

                var request = new HttpRequestMessage(HttpMethod.Head, VERSION_URL);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                var errorDetails = $"Hata: {ex.Message}\n\n";
                if (ex.InnerException != null)
                {
                    errorDetails += $"Inner Exception: {ex.InnerException.Message}\n\n";
                    if (ex.InnerException.InnerException != null)
                    {
                        errorDetails += $"Inner Inner: {ex.InnerException.InnerException.Message}";
                    }
                }

                MessageBox.Show(errorDetails, "Bağlantı Hatası",
                    MessageBoxButton.OK, MessageBoxImage.Error);

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
                MessageBox.Show(
                    "İnternet bağlantısı bulunamadı.\n\n" +
                    "Güncellemeler kontrol edilemedi ancak uygulamanın yüklü bir sürümü bulundu.\n" +
                    "Uygulama Çevrimdışı (Offline) modda başlatılıyor.",
                    "Çevrimdışı Mod", // Başlık düzeltildi
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
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

        private async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            UpdateStatus("Güncelleme kontrol ediliyor...", showSpinner: true);

            // Sunucudan version.json çek
            var serverJson = await _httpClient.GetStringAsync(VERSION_URL);
            var serverVersion = JsonSerializer.Deserialize<VersionInfo>(serverJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Yerel version.json'ı oku
            VersionInfo localVersion = null;
            if (File.Exists(_localVersionFile))
            {
                var localJson = await File.ReadAllTextAsync(_localVersionFile);
                localVersion = JsonSerializer.Deserialize<VersionInfo>(localJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            // Ana exe var mı kontrol et
            var mainExePath = Path.Combine(_appDirectory, MAIN_EXE);
            if (!File.Exists(mainExePath))
            {
                // İlk kurulum - güncelleme gerek
                return new UpdateCheckResult { UpdateNeeded = true, IsMandatory = true };
            }

            if (localVersion == null)
            {
                return new UpdateCheckResult { UpdateNeeded = true, IsMandatory = true };
            }

            var local = new Version(localVersion.Version);
            var server = new Version(serverVersion.Version);

            bool belowMinimum = false;
            if (!string.IsNullOrEmpty(serverVersion.MinimumVersion))
            {
                var minimum = new Version(serverVersion.MinimumVersion);
                belowMinimum = local < minimum;
            }

            bool updateAvailable = server > local;
            bool isMandatory = serverVersion.Mandatory || belowMinimum;

            Dispatcher.Invoke(() =>
            {
                if (updateAvailable)
                {
                    VersionText.Text = $"v{localVersion.Version} → v{serverVersion.Version}";
                }
                else
                {
                    VersionText.Text = $"v{localVersion.Version}";
                }
            });

            if (updateAvailable)
            {
                if (isMandatory)
                {
                    string message;
                    if (belowMinimum)
                    {
                        message = $"Mevcut sürümünüz (v{localVersion.Version}) artık desteklenmiyor.\n\n" +
                                $"Minimum desteklenen sürüm: v{serverVersion.MinimumVersion}\n" +
                                $"Yeni sürüm: v{serverVersion.Version}\n\n" +
                                "Uygulamayı kullanmak için güncelleme zorunludur.";
                    }
                    else
                    {
                        message = $"Kritik bir güncelleme yayınlandı (v{serverVersion.Version}).\n\n" +
                                "Bu güncelleme uygulamayı kullanmak için zorunludur.\n" +
                                "Güncelleme otomatik olarak başlatılacak.";
                    }

                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(message, "Zorunlu Güncelleme",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
                else
                {
                    UpdateStatus($"Yeni sürüm bulundu: v{serverVersion.Version}", showSpinner: true);
                }
            }

            return new UpdateCheckResult
            {
                UpdateNeeded = updateAvailable,
                IsMandatory = isMandatory
            };
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
        public string MinimumVersion { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool UpdateNeeded { get; set; }
        public bool IsMandatory { get; set; }
    }
}
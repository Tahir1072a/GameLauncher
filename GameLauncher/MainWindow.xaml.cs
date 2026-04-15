using System;
using System.Diagnostics;
using GameLauncher.Exceptions;
using GameLauncher.Entites;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Net.Http;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;

namespace GameLauncher
{
    public partial class MainWindow : Window
    {
        private const string APP_NAME = "avio-test";
        private const string MAIN_EXE = "AvioTest.exe";
        private const string UPDATER_EXE = "Updater.exe";
        private const string LAUNCHER_EXE = "GameLauncher.exe";

        private readonly string _localAppData;
        private readonly string _appDirectory;
        private readonly string _gameDirectory;
        private readonly string _backupDirectory;
        private readonly string _localVersionFile;
        private readonly string _updaterDirectory;

        private ServerManifest _localManifest = null!;
        private AppVersionDto? _launcherVersion;
        private AppVersionDto? _updaterVersion;
        private AppVersionDto? _gameVersion;

        private readonly INetworkService _networkService;
        private readonly IVersionService _versionService;
        private readonly IAppLauncherService _appLauncherService;
        private readonly ISystemUpdateService _systemUpdateService;
        private readonly IAppService _appService;
        private readonly IShutdownService _shutdownService;

        private readonly ILogger<MainWindow> _logger;
        private readonly IProgress<DownloadState> _progressReporter;

        private const string GAME_FALLBACK_URL = "https://files.aviotest.org/FallbackVersionFiles/game_fallback.json";
        private const string LAUNCHER_FALLBACK_URL = "https://files.aviotest.org/FallbackVersionFiles/launcher_fallback.json";
        private const string UPDATER_FALLBACK_URL = "https://files.aviotest.org/FallbackVersionFiles/updater_fallback.json";

        public MainWindow(
            ILogger<MainWindow> logger,
            IVersionService versionService,
            INetworkService networkService,
            IAppLauncherService appLauncherService,
            ISystemUpdateService systemUpdateService,
            IAppService appService,
            IShutdownService shutdownService)
        {
            InitializeComponent();
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _appDirectory = Path.Combine(_localAppData, APP_NAME);
            _updaterDirectory = Path.Combine(_appDirectory, "System");
            _localVersionFile = Path.Combine(_appDirectory, "version.json");
            _gameDirectory = Path.Combine(_appDirectory, "Game");
            _backupDirectory = Path.Combine(_appDirectory, "Backups");

            _versionService = versionService;
            _networkService = networkService;
            _appLauncherService = appLauncherService;
            _systemUpdateService = systemUpdateService;
            _appService = appService;
            _shutdownService = shutdownService;
            _logger = logger;

            _progressReporter = new Progress<DownloadState>(state =>
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadProgress.Value = state.Percentage;
                    PercentageText.Text = $"%{state.Percentage:F0}";
                    SpeedText.Text = $"{state.SpeedMBps:F2} MB/s";
                });
            });

            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major < 10)
                {
                    _logger.LogWarning("Desteklenmeyen işletim sistemi tespit edildi: {Version}", osVersion);
                    MessageBox.Show(
                        "Bu uygulama Windows 10 ve üzeri işletim sistemlerini desteklemektedir.\n\n" +
                        $"Mevcut sisteminiz: Windows {osVersion}\n\n" +
                        "Uygulamayı kullanmaya devam edebilirsiniz ancak güvenli bağlantı, güncelleme ve şifreleme hataları yaşayabilirsiniz.",
                        "Desteklenmeyen İşletim Sistemi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await LoadLocalManifestAsync();
                UpdateStatus("Başlatılıyor...");

                Directory.CreateDirectory(_appDirectory);

                var hasInternet = await CheckInternetConnectionAsync();

                if (!hasInternet)
                {
                    HandleNoInternetConnection();
                    return;
                }

                string[] args = Environment.GetCommandLineArgs();
                bool justUpdated = Array.Exists(args, arg => arg == "/post_update");
                string cachedVersionsFile = Path.Combine(_appDirectory, "cached_versions.json");

                UpdateStatus("Versiyonlar kontrol ediliyor...");

                if (justUpdated && File.Exists(cachedVersionsFile))
                {
                    _logger.LogInformation("Post-update tetiklendi, versiyonlar yerel cache üzerinden yükleniyor...");
                    try
                    {
                        var cacheJson = await File.ReadAllTextAsync(cachedVersionsFile);
                        var cachedData = JsonSerializer.Deserialize<ServerManifest>(cacheJson);
                        if (cachedData != null)
                        {
                            _launcherVersion = cachedData.launcher;
                            _updaterVersion = cachedData.updater;
                            _gameVersion = cachedData.game;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Yerel versiyon cache'i okunamadı. Hatadan kaçınmak için API'ye dönülecek.");
                    }
                    finally
                    {
                        try { File.Delete(cachedVersionsFile); } catch { }
                    }
                }

                if (_launcherVersion == null && _updaterVersion == null && _gameVersion == null)
                {
                    // Fallback Destekli Versiyon Çekimi
                    _launcherVersion = await _versionService.GetLatestVersionWithFallbackAsync("Launcher", "Windows", LAUNCHER_FALLBACK_URL);
                    _updaterVersion = await _versionService.GetLatestVersionWithFallbackAsync("Updater", "Windows", UPDATER_FALLBACK_URL);
                    _gameVersion = await _versionService.GetLatestVersionWithFallbackAsync("AvioTest", "Windows", GAME_FALLBACK_URL);

                    if (_launcherVersion != null || _updaterVersion != null || _gameVersion != null)
                    {
                        var cacheObj = new ServerManifest { launcher = _launcherVersion!, updater = _updaterVersion!, game = _gameVersion! };
                        await File.WriteAllTextAsync(cachedVersionsFile, JsonSerializer.Serialize(cacheObj));
                    }
                }

                if (_launcherVersion == null && _updaterVersion == null && _gameVersion == null)
                {
                    _logger.LogWarning("Hiçbir versiyon bilgisi alınamadı, offline moda geçiliyor.");
                    UpdateStatus("Sunucuya bağlanılamadı", isOffline: true);
                    HandleNoInternetConnection();
                    return;
                }

                await CheckPhysicalIntegrityAsync();

                if (justUpdated)
                {
                    UpdateStatus("Güncelleme tamamlandı, yapılandırılıyor...");
                    if (_launcherVersion != null)
                    {
                        _localManifest.launcher.Version = _launcherVersion.Version;
                        _localManifest.launcher.DownloadUrl = _launcherVersion.DownloadUrl;
                        _localManifest.launcher.ChecksumSha256 = _launcherVersion.ChecksumSha256;
                        _localManifest.launcher.Size = _launcherVersion.Size;
                        await SaveLocalManifestAsync();
                    }
                }

                // Sistem Güncellemesi (Launcher / Updater)
                UpdateStatus("Sistem bileşenleri güncelleniyor...");
                bool systemUpdatedAndNeedsRestart = await _systemUpdateService.CheckAndUpdateSystemComponentsAsync(
                    _launcherVersion, _updaterVersion, _localManifest, _appDirectory, _updaterDirectory, _progressReporter);

                if (systemUpdatedAndNeedsRestart)
                {
                    // Uygulamayı kapat, Updater işini yapacak
                    _shutdownService.Shutdown();
                    return;
                }

                await SaveLocalManifestAsync(); // Updater vs güncellendiyse listeyi kaydet

                // Oyun (App) Güncelleme Kontrolü
                UpdateStatus("Uygulama versiyonu kontrol ediliyor...");
                _logger.LogInformation("Uygulama sürümü kontrol ediliyor");

                if (_appService.RequiresUpdate(_gameVersion, _localManifest.game))
                {
                    bool isMandatory = _appService.IsMandatoryUpdate(_gameVersion, _localManifest.game);

                    if (isMandatory)
                    {
                        _logger.LogInformation("Zorunlu oyun güncellemesi yapılıyor.");
                        await UpdateGameAsync();
                    }
                    else
                    {
                        var result = MessageBox.Show(
                         $"Yeni bir güncelleme mevcut (v{_gameVersion?.Version}).\n" +
                         "Şimdi güncellemek ister misiniz?\n\n" +
                         "(Hayır derseniz mevcut sürümle uygulamaya girebilirsiniz.)",
                         "Güncelleme Mevcut",
                         MessageBoxButton.YesNo,
                         MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            await UpdateGameAsync();
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
            catch (FileServiceException ex)
            {
                _logger.LogError(ex, "Dosya servisi hatası: {Operation} - {FilePath}", ex.Operation, ex.FilePath);
                UpdateStatus("Dosya hatası!", isOffline: true);
                MessageBox.Show(
                    $"{ex.Message}\n\nLütfen oyunun tamamen kapalı olduğundan ve antivirüs programınızın dosyaları engellemediğinden emin olun.",
                    "Dosya İşlem Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (IOException)
            {
                UpdateStatus("Dosyalar kilitli!", isOffline: true);
                MessageBox.Show("Oyun dosyaları şu anda başka bir program tarafından kullanılıyor.\n\nLütfen oyunun kapalı olduğundan emin olun ve tekrar deneyin.",
                                "Dosya Kilitli", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException)
            {
                UpdateStatus("Erişim engellendi!", isOffline: true);
                MessageBox.Show("Dosyalara erişim izni yok. Antivirüs programınız kurulumu engelliyor olabilir veya Launcher'ı Yönetici olarak çalıştırmanız gerekebilir.",
                                "Erişim Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UpdateRolledBackException ex)
            {
                _logger.LogInformation("Yedeklemeden rollback yapıldı.");
                UpdateStatus("Eski sürüme dönüldü!", isOffline: true);
                MessageBox.Show(ex.Message, "Güncelleme İptal Edildi", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowPlayButton();
            }
            catch (UpdateFailedException ex)
            {
                UpdateStatus("Güncelleme Hatası!", isOffline: true);
                MessageBox.Show(ex.Message, "Güncelleme Başarısız", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (HttpRequestException)
            {
                // Bilgi: Ağ kesintileri doğrudan bu bloğa düşer. Uygulama çökmez, kullanıcı uyarılır.
                UpdateStatus("Ağ bağlantısı koptu!", isOffline: true);
                MessageBox.Show("İndirme veya sistem güncelleme esnasında internet bağlantınız kesildi.\nLütfen internetinizi kontrol edip Yeniden Dene butonuna basınız.", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (TaskCanceledException)
            {
                // Bilgi: Sunucu aniden yanıt vermeyi keserse ve timeout olursa buraya düşer. Uygulama çökmez.
                UpdateStatus("Sunucu zaman aşımı!", isOffline: true);
                MessageBox.Show("Sunucu ile bağlantı sağlanırken zaman aşımına uğradı.\nLütfen daha sonra tekrar deneyin.", "Timeout Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Uygulama başlatılırken öngörülemeyen kritik bir hata oluştu.");
                UpdateStatus("Kritik Hata Oluştu!");
                MessageBox.Show($"Beklenmeyen bir hata oluştu:\n\n{ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                _shutdownService.Shutdown();
            }
        }

        private async Task UpdateGameAsync()
        {
            // Oyun sürecinin kapalı olduğundan emin ol
            string processName = System.IO.Path.GetFileNameWithoutExtension(MAIN_EXE);
            var gameProcesses = Process.GetProcessesByName(processName);

            if (gameProcesses.Length > 0)
            {
                MessageBox.Show(
                    "Oyun şu anda çalışıyor. Güncelleme yapabilmek için önce oyunu kapatmanız gerekmektedir.\n\n" +
                    "Lütfen oyunu kapatıp tekrar deneyin.",
                    "Oyun Çalışıyor", MessageBoxButton.OK, MessageBoxImage.Warning);

                throw new UpdateFailedException("Güncelleme başlatılamadı: Oyun hâlâ çalışıyor.");
            }

            UpdateStatus($"Güncelleniyor... v{_gameVersion?.Version}");
            await _appService.DownloadAndInstallUpdateAsync(_gameVersion!, _localManifest, _gameDirectory, _backupDirectory, _progressReporter);
            await SaveLocalManifestAsync();
            UpdateStatus("Güncelleme tamamlandı!");
            await Task.Delay(1000);
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

            if (appExists) TryLaunchGame();
            else
            {
                MessageBox.Show(
                    "İnternet bağlantısı bulunamadı.\n\n" +
                    "Uygulamanın çalışabilmesi için ilk kurulumun yapılması gerekmektedir.\n" +
                    "Lütfen internet bağlantınızı kontrol edip tekrar deneyin.",
                    "Bağlantı Gerekli", MessageBoxButton.OK, MessageBoxImage.Error);
                _shutdownService.Shutdown();
            }
        }

        private async void TryLaunchGame()
        {
            var result = _appLauncherService.LaunchApplication(_gameDirectory, MAIN_EXE);
            if (result.IsSuccess)
            {
                _logger.LogWarning("Uygulama başarıyla başlatıldı.");
                await Task.Delay(500);
                _shutdownService.Shutdown();
            }
            else if (result.RequiresAutoRepair)
            {
                _logger.LogError("Uygulama başlatılamadı. Otomatik onarım döngüsü tetiklendi.");
                try
                {
                    UpdateStatus("Uygulama başlatılamıyor, otomatik onarım süreci başladı...");
                    if (_gameVersion != null)
                    {
                        await UpdateGameAsync();
                        _appLauncherService.MarkRepairCompleted(_gameDirectory);
                        TryLaunchGame();
                    }
                    else
                    {
                        _logger.LogError("Onarım için sürüm bilgisi alınamadı (İnternet bağlantısı yok veya API çökmüş).");
                        UpdateStatus("Onarım için sürüm bilgisi alınamadı", isOffline: true);
                        MessageBox.Show("Internet bağlantınız olmadığı için onarım başlatılamıyor.", "Bağlantı Gerekli", MessageBoxButton.OK, MessageBoxImage.Error);
                        _shutdownService.Shutdown();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Otomatik onarım süreci sırasında beklenmeyen bir hata oluştu.");
                    MessageBox.Show($"Onarım başarısız oldu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus("Onarım Başarısız!", isOffline: true);
                    _shutdownService.Shutdown();
                }
            }
            else if (result.IsUnrepairableSystemError)
            {
                _logger.LogError("Kritik Sistem Uyumsuzluğu: Oyun dosyaları sağlam/onarılmış olmasına rağmen başlatılamıyor.");
                MessageBox.Show(result.ErrorMessage, result.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                _shutdownService.Shutdown();
            }
            else
            {
                _logger.LogError("Uygulama başlatılamadı: {ErrorMessage}", result.ErrorMessage);
                MessageBox.Show(result.ErrorMessage, result.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                _shutdownService.Shutdown();
            }
        }

        private async Task LoadLocalManifestAsync()
        {
            if (File.Exists(_localVersionFile))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_localVersionFile);
                    _localManifest = JsonSerializer.Deserialize<ServerManifest>(json) ?? new ServerManifest();

                    _localManifest.launcher ??= new AppVersionDto { Version = new Version("0.0.0") };
                    _localManifest.updater ??= new AppVersionDto { Version = new Version("0.0.0") };
                    _localManifest.game ??= new AppVersionDto { Version = new Version("0.0.0") };
                }
                catch
                {
                    _localManifest = new ServerManifest();
                }
            }
            else
            {
                _localManifest = new ServerManifest();
            }

            Dispatcher.Invoke(() => VersionText.Text = $"v{_localManifest.game.Version}");
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

            if (manifestDirty) return SaveLocalManifestAsync();
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
                RetryButton.Visibility = isOffline ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => _shutdownService.Shutdown();

        private void PlayButton_Click(object sender, RoutedEventArgs e) => TryLaunchGame();

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            RetryButton.IsEnabled = false;
            RetryButton.Visibility = Visibility.Collapsed;
            UpdateStatus("Yeniden bağlanmaya çalışılıyor...");
            DownloadProgress.Value = 0;
            PercentageText.Text = "%0";
            SpeedText.Text = "0 MB/s";

            try { await InitializeAsync(); }
            finally { RetryButton.IsEnabled = true; }
        }

        private void BottomButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
    }
}
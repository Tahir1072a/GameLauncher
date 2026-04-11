using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using GameLauncher.Services.Concrete;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace GameLauncher.Tests
{
    public class AppServiceTests
    {
        private readonly Mock<ILogger<AppService>> _mockLogger = new();
        private readonly Mock<INetworkService> _mockNetworkService = new();
        private readonly Mock<IFileService> _mockFileService = new();
        private readonly AppService _appService;

        public AppServiceTests()
        {
            _appService = new AppService(_mockLogger.Object, _mockNetworkService.Object, _mockFileService.Object);
        }

        [Fact]
        public void RequiresUpdate_ShouldReturnTrue_WhenCriticalIsTrue_AndLocalIsNewer()
        {
            // Arrange
            // Amaç: Fallback Downgrade (Kurtarma) Senaryosunda gönderilen sürüm düşük olsa bile Critical = true olduğunda güncellemenin tetiklenmesinin (zorla downgrade) doğrulanması.
            var newVersion = new AppVersionDto { Version = new Version("1.0.0"), Critical = true };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.5") };

            // Act
            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            // Assert
            Assert.True(result, "Critical = true olduğu için lokal sürüm yeni olsa dahi güncelleme True dönmelidir.");
        }

        [Fact]
        public void RequiresUpdate_ShouldReturnTrue_WhenApiVersionIsHigher()
        {
            // Arrange
            // Amaç: Normal Güncelleme İşlemi (Zorunlu veya Opsiyonel) Local<API test edilmesi
            var newVersion = new AppVersionDto { Version = new Version("1.0.5"), Critical = false };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.0") };

            // Act
            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void RequiresUpdate_ShouldReturnFalse_WhenUpToDate()
        {
            // Arrange
            // Amaç: Hiçbir şey yapma, en günceli sende testinin doğrulanması (Local >= API)
            var newVersion = new AppVersionDto { Version = new Version("1.0.5"), Critical = false };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.5") };

            // Act
            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallUpdateAsync_ShouldRetryThreeTimes_WhenHashCheckFails()
        {
            // Arrange
            // Amaç: DownloadAndInstallUpdateAsync çağrıldığında hash doğrulaması false dönerse tam 3 kez retry atmasının test edilmesi.
            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            
            // Hash doğrulaması hep false dönsün
            _mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            
            // Act & Assert
            var ex = await Assert.ThrowsAsync<UpdateFailedException>(() => 
                _appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, "dummy/game", "dummy/backup", new Progress<DownloadState>()));

            Assert.Contains("3 kez", ex.Message);
            
            // 1. DownloadFilesAsync 3 kere çağrılmış olmalı
            _mockNetworkService.Verify(n => n.DownloadFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadState>>()), Times.Exactly(3));
            
            // 2. Yedekleme aşamasına (CreateBackupAsync) HİÇ GEÇMEMİŞ olmalı
            _mockFileService.Verify(f => f.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DownloadAndInstallUpdateAsync_ShouldProceedWithInstallation_WhenHashCheckSucceeds()
        {
            // Arrange
            // Amaç: Başarılı indirme ve doğrulamadan sonra sırasıyla Backup, Clean ve Extract adımlarının Verify ile test edilmesi.
            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            
            _mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            
            // Act
            var result = await _appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, "dummy/game", "dummy/backup", new Progress<DownloadState>());

            // Assert
            Assert.True(result);
            _mockFileService.Verify(f => f.CreateBackupAsync("dummy/game", "dummy/backup"), Times.Once);
            _mockFileService.Verify(f => f.CleanDirectoryAsync("dummy/game"), Times.Once);
            _mockFileService.Verify(f => f.ExtractZipAsync(It.IsAny<string>(), "dummy/game", true), Times.Once);
            _mockFileService.Verify(f => f.DeleteFileAsync(It.IsAny<string>()), Times.Once); // finally bloğu içindeki temp silinmesi
        }

        [Fact]
        public async Task DownloadAndInstall_IfSetupFails_ShouldRollback() 
        {
            // 1. Arrange (Hazırlık)
            var mockLogger = new Mock<ILogger<AppService>>();
            var mockFileService = new Mock<IFileService>();
            var mockNetworkService = new Mock<INetworkService>();

            // Sahte (Dummy) Parametreler
            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            string gameDirectory = "dummy/game";
            string backupDirectory = "dummy/backup";
            var progress = new Progress<DownloadState>();

            // Hash doğrulaması başarılı geçsin ki Backup ve Kurulum aşamasına atlayabilelim
            mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            // Yedekleme DOĞRU çalışsın
            mockFileService.Setup(x => x.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            // Ancak Çıkarma işleminde hata patlasın (Simülasyon)
            mockFileService.Setup(x => x.ExtractZipAsync(It.IsAny<string>(), It.IsAny<string>(), true)).ThrowsAsync(new System.IO.IOException("Simüle Edilmiş Disk Hatası"));
            
            // Geri Yükleme başarılı olsun
            mockFileService.Setup(x => x.RestoreBackupAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

            // Eksik parametreleri de ekleyerek Servisimizi ayağa kaldırıyoruz
            var appService = new AppService(mockLogger.Object, mockNetworkService.Object, mockFileService.Object);

            // 2. Act & Assert (Uygula & Bekle)
            await Assert.ThrowsAsync<UpdateRolledBackException>(async () => 
            {
                await appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, gameDirectory, backupDirectory, progress);
            });

            // 3. Verify (Doğrulama) - Geri yükleme fonksiyonunun gerçekten 1 kez tetikledi mi?
            mockFileService.Verify(x => x.RestoreBackupAsync(backupDirectory, gameDirectory), Times.Once);
        }
        
    }
}

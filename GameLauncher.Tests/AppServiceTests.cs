using GameLauncher.Entites;
using GameLauncher.Exceptions;
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
            var newVersion = new AppVersionDto { Version = new Version("1.0.0"), Critical = true };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.5") };

            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            Assert.True(result, "Critical = true olduğu için lokal sürüm yeni olsa dahi güncelleme True dönmelidir.");
        }

        [Fact]
        public void RequiresUpdate_ShouldReturnTrue_WhenApiVersionIsHigher()
        {
            var newVersion = new AppVersionDto { Version = new Version("1.0.5"), Critical = false };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.0") };

            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            Assert.True(result);
        }

        [Fact]
        public void RequiresUpdate_ShouldReturnFalse_WhenUpToDate()
        {
            var newVersion = new AppVersionDto { Version = new Version("1.0.5"), Critical = false };
            var currentVersion = new AppVersionDto { Version = new Version("1.0.5") };

            var result = _appService.RequiresUpdate(newVersion, currentVersion);

            Assert.False(result);
        }

        [Fact]
        public async Task DownloadAndInstallUpdateAsync_ShouldRetryThreeTimes_WhenHashCheckFails()
        {
            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            
            _mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);
            
            var ex = await Assert.ThrowsAsync<UpdateFailedException>(() => 
                _appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, "dummy/game", new Progress<DownloadState>()));

            Assert.Contains("3 kez", ex.Message);
            
            _mockNetworkService.Verify(n => n.DownloadFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadState>>()), Times.Exactly(3));
            
            // Çıkarma veya temizleme aşamasına GEÇİLMEMİŞ olmalı
            _mockFileService.Verify(f => f.ExtractZipAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            _mockFileService.Verify(f => f.CleanDirectoryAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DownloadAndInstallUpdateAsync_ShouldProceedWithInstallation_WhenHashCheckSucceeds()
        {
            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            
            _mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            
            var result = await _appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, "dummy/game", new Progress<DownloadState>());

            Assert.True(result);
            _mockFileService.Verify(f => f.ExtractZipAsync(It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);
            _mockFileService.Verify(f => f.CleanDirectoryAsync("dummy/game"), Times.Once);
            _mockFileService.Verify(f => f.MoveExtractedFilesAsync(It.IsAny<string>(), "dummy/game"), Times.Once);
            _mockFileService.Verify(f => f.DeleteFileAsync(It.IsAny<string>()), Times.Once); // finally bloğu içindeki zip silinmesi
        }

        [Fact]
        public async Task DownloadAndInstall_IfExtractionFails_ShouldThrowUpdateFailed_AndNotDeleteGame() 
        {
            var mockLogger = new Mock<ILogger<AppService>>();
            var mockFileService = new Mock<IFileService>();
            var mockNetworkService = new Mock<INetworkService>();

            var gameVersion = new AppVersionDto { Version = new Version("1.0.5"), DownloadUrl = "http://dummy", ChecksumSha256 = "dummy-hash" };
            var localManifest = new ServerManifest { game = new AppVersionDto { Version = new Version("1.0.0") } };
            string gameDirectory = "dummy/game";
            var progress = new Progress<DownloadState>();

            mockFileService.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            // Çıkarma işleminde disk doldu hatası simülasyonu
            mockFileService.Setup(x => x.ExtractZipAsync(It.IsAny<string>(), It.IsAny<string>(), true)).ThrowsAsync(new System.IO.IOException("Simüle Edilmiş Disk Hatası"));
            
            var appService = new AppService(mockLogger.Object, mockNetworkService.Object, mockFileService.Object);

            await Assert.ThrowsAsync<UpdateFailedException>(async () => 
            {
                await appService.DownloadAndInstallUpdateAsync(gameVersion, localManifest, gameDirectory, progress);
            });

            // DOĞRULAMA: Çıkarma işlemi çöktüğü için oyun klasörü ASLA temizlenmemeli ve taşımaya geçilmemelidir.
            mockFileService.Verify(x => x.CleanDirectoryAsync(gameDirectory), Times.Never);
            mockFileService.Verify(x => x.MoveExtractedFilesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }
        
    }
}

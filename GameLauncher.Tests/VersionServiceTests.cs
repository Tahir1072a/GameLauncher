using GameLauncher.Entites;
using GameLauncher.Services.Concrete;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace GameLauncher.Tests
{
    public class VersionServiceTests
    {
        private Mock<HttpMessageHandler> GetMockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
            return mockHandler;
        }

        [Fact]
        public async Task GetLatestVersionWithFallbackAsync_ShouldReturnFallback_WhenMainApiReturnsBadRequest()
        {
            // Arrange: 
            // Amaç: Ana API 400 verirse Fallback'e gidilip gidilmediğinin test edilmesi.
            // HttpClient'ı mockla, ilk çağrıda (Main) 400 Bad Request dönsün.
            // İkinci çağrıda (Fallback) başarılı 200 dönsün.
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.BadRequest, Content = new StringContent("Bad Request") })
                .ReturnsAsync(new HttpResponseMessage 
                { 
                    StatusCode = HttpStatusCode.OK, 
                    Content = new StringContent(JsonSerializer.Serialize(new AppVersionDto { Version = new Version("1.0.0"), Critical = true })) 
                });

            var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://dummy.url") };
            var mockLogger = new Mock<ILogger<VersionService>>();
            var versionService = new VersionService(mockLogger.Object, httpClient);

            // Act
            var result = await versionService.GetLatestVersionWithFallbackAsync("AvioTest", "Windows", "http://fallback.url");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Critical);
            Assert.Equal(new Version("1.0.0"), result.Version);
        }

        [Fact]
        public async Task GetLatestVersionWithFallbackAsync_ShouldReturnFallback_WhenMainApiFailsWith500()
        {
            // Arrange:
            // Amaç: Ana API 500 dönerse (sunucu hatası) Fallback'ten başarılı (Critical = true) JSON çekilip çekilmeyeceğinin test edilmesi.
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .SetupSequence<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }) // 1. Deneme (GetLatestVersion retries 3 times)
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }) // 2. Deneme
                .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError }) // 3. Deneme
                .ReturnsAsync(new HttpResponseMessage // Fallback (GetAlternativeVersionAsync)
                { 
                    StatusCode = HttpStatusCode.OK, 
                    Content = new StringContent(JsonSerializer.Serialize(new AppVersionDto { Version = new Version("0.9.9"), Critical = true })) 
                });

            var httpClient = new HttpClient(mockHandler.Object) { BaseAddress = new Uri("http://dummy.url") };
            var mockLogger = new Mock<ILogger<VersionService>>();
            var versionService = new VersionService(mockLogger.Object, httpClient);

            // Act
            var result = await versionService.GetLatestVersionWithFallbackAsync("AvioTest", "Windows", "http://fallback.url");

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Critical);
            Assert.Equal(new Version("0.9.9"), result.Version);
            
            // Log'un (critical ile fallback alındı) kontrolü eklenebilir.
        }
    }
}

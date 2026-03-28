using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace GameLauncher.Services.Concrete
{
    public class VersionService : IVersionService
    {
        private const string apiKey = "DesktopLauncher";
        private const string apiSecretKey = "f23d050c3b76a8b24ff5ca581abb60d05e2e305a4ec1759d7a8aa9e5a140ebc0";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public VersionService(ILogger<IVersionService> logger, HttpClient httpClient) 
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<AppVersionDto?> GetLatestVersion(string appName, string platform)
        {
            _logger.LogInformation($"Versiyon sorgulanıyor: {appName}, platform: {platform}");

            try
            {
                string method = "GET";
                string path = "/api/updates/latest";
                string query = $"?appName={appName}&platform={platform}";

                string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                string nonce = Guid.NewGuid().ToString();

                string canonicalString = $"{method}\n{path}\n{query}\n{timestamp}\n{nonce}";
                string signature = ComputeHmacSha256(apiSecretKey, canonicalString);

                var request = new HttpRequestMessage(HttpMethod.Get, path + query);
                request.Headers.Add("X-Api-Key", apiKey);
                request.Headers.Add("X-Timestamp", timestamp);
                request.Headers.Add("X-Nonce", nonce);
                request.Headers.Add("X-Signature", signature);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Versiyon başarıyla alındı: {AppName}", appName);
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return System.Text.Json.JsonSerializer.Deserialize<AppVersionDto>(content, options);
                }
                else if (response.StatusCode == HttpStatusCode.BadRequest) {
                    _logger.LogWarning("400 Bad Request alındı. Detay: {Content}", content);
                    var internalEx = new HttpRequestException($"400 Bad Request: {content}");
                    _logger.LogError(internalEx, "Kullanıcı 400 hatası aldı, payload veya imza hatalı olabilir.");
                    return null;              
                }
                else
                {
                    _logger.LogWarning("Versiyon alınamadı: {StatusCode}, Response: {Content}", response.StatusCode, content);
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP isteği başarısız: {AppName}, Platform: {Platform}", appName, platform);
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "İstek zaman aşımına uğradı: {AppName}", appName);
                return null;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialize hatası: {AppName}", appName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Beklenmeyen hata (GetLatestVersion): {AppName}", appName);
                return null;
            }
        }

        private string ComputeHmacSha256(string secretKey, string message)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

    }
}
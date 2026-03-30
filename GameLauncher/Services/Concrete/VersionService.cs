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
            const int maxRetries = 3;
            _logger.LogInformation("Versiyon sorgulanıyor: {AppName}, platform: {Platform}", appName, platform);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
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
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        _logger.LogWarning("400 Bad Request alındı. Detay: {Content}", content);
                        var internalEx = new HttpRequestException($"400 Bad Request: {content}");
                        _logger.LogError(internalEx, "Kullanıcı 400 hatası aldı, payload veya imza hatalı olabilir.");
                        return null; // 400 kalıcı hata — retry anlamsız
                    }
                    else if ((int)response.StatusCode >= 500)
                    {
                        // 5xx sunucu hatası — geçici olabilir, retry yap
                        _logger.LogWarning("Sunucu hatası {StatusCode}, deneme {Attempt}/{MaxRetries}: {AppName}", 
                            (int)response.StatusCode, attempt, maxRetries, appName);

                        if (attempt == maxRetries)
                        {
                            _logger.LogError("Sunucu hatası {MaxRetries} denemeden sonra devam ediyor: {AppName}", maxRetries, appName);
                            return null;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))); // 1s, 2s, 4s
                        continue;
                    }
                    else
                    {
                        _logger.LogWarning("Versiyon alınamadı: {StatusCode}, Response: {Content}", response.StatusCode, content);
                        return null; // 3xx, 4xx — kalıcı hata
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "HTTP isteği başarısız (deneme {Attempt}/{MaxRetries}): {AppName}, Platform: {Platform}", 
                        attempt, maxRetries, appName, platform);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "HTTP isteği {MaxRetries} denemeden sonra hâlâ başarısız: {AppName}", maxRetries, appName);
                        return null;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "İstek zaman aşımına uğradı (deneme {Attempt}/{MaxRetries}): {AppName}", 
                        attempt, maxRetries, appName);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Zaman aşımı {MaxRetries} denemeden sonra devam ediyor: {AppName}", maxRetries, appName);
                        return null;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogError(ex, "JSON deserialize hatası: {AppName}", appName);
                    return null; // JSON hatası kalıcı — retry anlamsız
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Beklenmeyen hata (GetLatestVersion): {AppName}", appName);
                    return null; // Bilinmeyen hata — güvenli tarafta kal
                }
            }

            return null;
        }

        public async Task<AppVersionDto?> GetAlternativeVersionAsync(string alternativeUrl)
        {
            _logger.LogInformation("Alternatif (Fallback) URL üzerinden versiyon sorgulanıyor: {Url}", alternativeUrl);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, alternativeUrl);
                // HMAC Authorization vb. güvenlik olmayacak
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var dto = System.Text.Json.JsonSerializer.Deserialize<AppVersionDto>(content, options);
                    
                    if (dto != null)
                    {
                        _logger.LogWarning("Alternatif sunucudan başarılı dönüş alındı. Critical: {Critical}, Version: {Version}", dto.Critical, dto.Version);
                    }
                    return dto;
                }
                else
                {
                    _logger.LogWarning("Alternatif sunucuya ulaşıldı ancak hata döndü. StatusCode: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alternatif sunucu JSON verisi çekilirken hata oluştu.");
            }
            return null;
        }

        private string ComputeHmacSha256(string secretKey, string message)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public async Task<AppVersionDto?> GetLatestVersionWithFallbackAsync(string appName, string platform, string fallbackUrl)
        {
            // Main Api
            var mainVersion = await GetLatestVersion(appName, platform);
            
            if (mainVersion != null)
            {
                return mainVersion;
            }

            _logger.LogWarning("Ana API'den versiyon alınamadı, Fallback sunucuya ({FallbackUrl}) gidiliyor: {AppName}", fallbackUrl, appName);
            
            var fallbackVersion = await GetAlternativeVersionAsync(fallbackUrl);
            
            if (fallbackVersion != null)
            {
                // Fallback tetiklendiğinde ve başarılı olduğunda log bas (Critical mı değil mi?)
                _logger.LogInformation("Fallback sunucusundan versiyon başarıyla çekildi: {AppName}. Critical: {Critical}, Sürüm: {Version}", 
                    appName, fallbackVersion.Critical, fallbackVersion.Version);
                return fallbackVersion;
            }
            
            _logger.LogError("Fallback sunucusundan da versiyon bilgisi okunamadı. Sistem tamamen offline: {AppName}", appName);
            return null;
        }

    }
}
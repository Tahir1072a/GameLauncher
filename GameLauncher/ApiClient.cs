using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Net.Http;

namespace GameLauncher
{
    public class ApiClient
    {
        private const string backendUrl = "https://versioncontrolmanagement-production.up.railway.app";
        private const string apiKey = "DesktopLauncher";
        private const string apiSecretKey = "f23d050c3b76a8b24ff5ca581abb60d05e2e305a4ec1759d7a8aa9e5a140ebc0";


        private readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri(backendUrl) };

        public async Task<Entites.AppVersionDto?> GetLatestVersion(string appName, string platform)
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
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return System.Text.Json.JsonSerializer.Deserialize<Entites.AppVersionDto>(content, options);
            }
            else
            {
                Console.WriteLine("Hata: " + response.StatusCode);
                Console.WriteLine(content);
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
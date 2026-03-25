using GameLauncher.Services.Abstract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace GameLauncher.Services.Concrete
{
    public class NetworkService : INetworkService
    {
        private readonly HttpClient _httpClient;

        public NetworkService()
        {
            // 5 dakika bekleme olursa bağlantıyı sıfırla
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<bool> CheckInternetConnectionAsync(string url)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                // Sadece başlıkları oku, içeriği indirme
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public async Task DownloadFilesAsync(string url, string destinationPath, IProgress<DownloadState> progress = null)
        {
            // Url'in varlığını kontrol et
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReport = totalBytes != -1 && progress != null;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // 32kb'lık bir buffer kullanarak dosyayı parça parça indir
            var buffer = new byte[8192 * 4];
            long totalRead = 0;
            int bytesRead;

            var stopwatch = Stopwatch.StartNew();
            DateTime lastUpdate = DateTime.MinValue;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;
                double SpeedMBps = totalRead / stopwatch.Elapsed.TotalSeconds / 1024 / 1024;

                if (canReport && (DateTime.Now - lastUpdate).TotalMicroseconds > 500)
                {
                    lastUpdate = DateTime.Now;
                    var percentage = (double)totalRead / totalBytes * 100;


                    progress.Report(new DownloadState(percentage, SpeedMBps));
                }
            }

            if (canReport)
            {
                progress.Report(new DownloadState(100, 0));
            }

        }
    }

    public record DownloadState(double Percentage, double SpeedMBps);
}

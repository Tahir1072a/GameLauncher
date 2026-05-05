using GameLauncher.Entites;
using GameLauncher.Services.Abstract;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GameLauncher.Services.Concrete
{
    public class NetworkService : INetworkService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NetworkService> _logger;

        public NetworkService(ILogger<NetworkService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        ///   İnternet bağlantısı kontrolü yapar.
        ///   HEAD isteği gönderir, body indirmez.
        /// </summary>
        /// <remarks>
        ///   <b>Olası hatalar ve nedenleri:</b>
        ///   <list type="table">
        ///     <listheader><term>Exception</term><description>Ne zaman fırlar?</description></listheader>
        ///     <item><term>HttpRequestException (SocketError)</term>
        ///       <description>DNS çözümlenemez, sunucu kapalı, port kapalı, bağlantı reddedildi.</description></item>
        ///     <item><term>HttpRequestException (SSL/TLS)</term>
        ///       <description>Sertifika geçersiz, süresi dolmuş veya zincir doğrulanamadı.</description></item>
        ///     <item><term>TaskCanceledException / OperationCanceledException</term>
        ///       <description>İstek 15 sn CancellationToken süresini aştı (timeout).</description></item>
        ///     <item><term>InvalidOperationException</term>
        ///       <description>BaseAddress null ise veya URI formatı geçersizse.</description></item>
        ///   </list>
        ///   <para>Sunucu 4xx/5xx döndüğünde exception fırlamaz ama <c>IsSuccessStatusCode</c> false olur.</para>
        /// </remarks>
        public async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, _httpClient.BaseAddress + "version.json");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Bağlantı kontrolü yanıtı başarısız: {StatusCode}", response.StatusCode);
                }

                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException socketEx)
            {
                // DNS çözümlenemedi veya sunucuya fiziksel erişim yok
                _logger.LogWarning("Ağ erişimi yok (Socket: {SocketError}): {Message}", socketEx.SocketErrorCode, socketEx.Message);
                return false;
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
            {
                _logger.LogWarning(ex, "SSL/TLS sertifika doğrulaması başarısız");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTP isteği başarısız");
                return false;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Bağlantı kontrolü zaman aşımına uğradı (15sn)");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bağlantı kontrolünde beklenmeyen hata");
                return false;
            }
        }

        /// <summary>
        ///   Verilen URL'den dosya indirir ve <paramref name="destinationPath"/>'e kaydeder.
        /// </summary>
        /// <remarks>
        ///   <b>Olası hatalar ve nedenleri:</b>
        ///   <list type="table">
        ///     <listheader><term>Aşama</term><description>Exception — Açıklama</description></listheader>
        ///     <item><term>HTTP isteği</term>
        ///       <description>
        ///         <c>HttpRequestException</c> — DNS hatası, bağlantı reddedildi, SSL hatası.<br/>
        ///         <c>TaskCanceledException</c> — HttpClient.Timeout (5dk) aşıldı.
        ///       </description>
        ///     </item>
        ///     <item><term>HTTP yanıt kodu</term>
        ///       <description>
        ///         <c>404 NotFound</c> — Dosya sunucuda yok, URL yanlış.<br/>
        ///         <c>403 Forbidden</c> — CDN/bucket erişim izni yok, token süresi dolmuş.<br/>
        ///         <c>401 Unauthorized</c> — Kimlik doğrulama gerekli.<br/>
        ///         <c>429 TooManyRequests</c> — Rate-limit aşıldı.<br/>
        ///         <c>500/502/503</c> — Sunucu tarafı hatalar.
        ///       </description>
        ///     </item>
        ///     <item><term>İndirme sırasında</term>
        ///       <description>
        ///         <c>IOException</c> — Bağlantı ortasında koptu (stream okuma hatası).<br/>
        ///         <c>IOException</c> — Diske yazılamadı (disk dolu, dosya kilitli).<br/>
        ///         <c>UnauthorizedAccessException</c> — Hedef dizine yazma izni yok.
        ///       </description>
        ///     </item>
        ///   </list>
        /// </remarks>
        public async Task DownloadFilesAsync(string url, string destinationPath, IProgress<DownloadState>? progress = null)
        {
            _logger.LogInformation("İndirme başlatılıyor: {Url} → {Destination}", url, destinationPath);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "İndirme isteği başarısız (ağ hatası): {Url}", url);
                throw;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "İndirme isteği zaman aşımına uğradı: {Url}", url);
                throw;
            }

            // HTTP durum kodu kontrolü — hata varsa anlamlı mesajla logla
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var statusCode = response.StatusCode;

                switch (statusCode)
                {
                    case HttpStatusCode.NotFound:
                        _logger.LogError("404 — Dosya bulunamadı: {Url}", url);
                        break;
                    case HttpStatusCode.Forbidden:
                        _logger.LogError("403 — Erişim engellendi (CDN/bucket izni): {Url}", url);
                        break;
                    case HttpStatusCode.Unauthorized:
                        _logger.LogError("401 — Kimlik doğrulama gerekli: {Url}", url);
                        break;
                    case HttpStatusCode.TooManyRequests:
                        _logger.LogWarning("429 — Rate limit aşıldı: {Url}", url);
                        break;
                    default:
                        _logger.LogError("HTTP {StatusCode} hatası: {Url}, Body: {Body}", (int)statusCode, url, errorBody);
                        break;
                }

                response.EnsureSuccessStatusCode(); // HttpRequestException fırlatır
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReport = totalBytes != -1 && progress != null;

            _logger.LogInformation("İndirme boyutu: {Size} bytes", totalBytes);

            try
            {
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192 * 4]; // 32KB buffer
                long totalRead = 0;
                int bytesRead;

                var stopwatch = Stopwatch.StartNew();
                var updateStopwatch = Stopwatch.StartNew();

                long lastSnapshotBytes = 0;
                double lastSnapshotSeconds = 0;

                using var cts = new CancellationTokenSource();

                while (true)
                {
                    // Veri geldikçe bu sayaç yeniden 45 saniyeye kurulacak. (TaskCanceledException)
                    cts.CancelAfter(TimeSpan.FromSeconds(45));

                    bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    totalRead += bytesRead;

                    if (canReport && updateStopwatch.ElapsedMilliseconds > 32)
                    {
                        double nowSeconds = stopwatch.Elapsed.TotalSeconds;

                        double intervalSeconds = nowSeconds - lastSnapshotSeconds;
                        double intervalBytes = totalRead - lastSnapshotBytes;
                        double speedMBps = intervalSeconds > 0
                            ? intervalBytes / intervalSeconds / 1024 / 1024
                            : 0;

                        lastSnapshotBytes = totalRead;
                        lastSnapshotSeconds = nowSeconds;
                        updateStopwatch.Restart();

                        var percentage = (double)totalRead / totalBytes * 100;
                        progress!.Report(new DownloadState(percentage, speedMBps));
                    }
                }

                if (canReport)
                {
                    progress!.Report(new DownloadState(100, 0));
                }

                _logger.LogInformation("İndirme tamamlandı: {TotalRead} bytes, {Destination}", totalRead, destinationPath);
            }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                // İndirme sırasında bağlantı koptu
                _logger.LogError(ex, "İndirme sırasında bağlantı kesildi: {Url}", url);
                throw;
            }
            catch (IOException ex)
            {
                if (ex.InnerException is System.ComponentModel.Win32Exception win32Ex)
                {
                    // Win32 hatası tespit edildi, teşhis için OS bilgisini loga ekle
                    var osVersion = Environment.OSVersion;
                    _logger.LogError(ex,
                        "Win32 kaynaklı IO hatası. NativeError: {NativeError}, OS: {OS} {Version}",
                        win32Ex.NativeErrorCode, osVersion.Platform, osVersion.Version);
                }
                else
                {
                    _logger.LogError(ex, "Ağ bağlantısı veya disk yazma hatası: {Destination}", destinationPath);
                }

                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Dosya yazma izni yok: {Destination}", destinationPath);
                throw;
            }
            finally
            {
                response.Dispose();
            }
        }
    }
}

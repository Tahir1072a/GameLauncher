# AvioTest - Game Launcher Dokümantasyonu

## 1. Genel Bilgi
AvioTest Game Launcher, "AvioTest.exe" isimli ana uygulamanın ve bağlantılı bileşenlerinin son kullanıcıya sorunsuz, güncel ve hatasız bir şekilde ulaştırılmasını sağlayan WPF (Windows Presentation Foundation) tabanlı bir başlatıcı (launcher) yazılımıdır. 
.NET 10 altyapısı kullanılarak geliştirilmiş olup, Dependency Injection (DI), asenkron yapı ve Sentry loglama gibi modern yazılım standartlarına sahiptir.

## 2. Mimari ve Bileşenler
Uygulama kod mimarisi `MainWindow.xaml` üzerinden bir UI döngüsüne sahip olup tüm iş ve hata yönetimlerini **Services** katmanındaki servis sınıfları üzerinden yönetmektedir.

### 2.1 Servisler (Services Katmanı)
* **IVersionService:** Uygulama sürüm bilgilerini ana sunucudan (veya yedek Fallback sunucularından) çeker. İnternet erişimi kopmalarına veya sunucu çökmelerine karşı Fallback (yedek) mekanizması içerir.
* **INetworkService:** İndirme işlemlerini ve internet bağlantısı kontrollerini asenkron olarak gerçekleştirir.
* **IFileService:** Dosya sistemi üzerindeki indirme, kopyalama, taşıma ve "atomic" (kayıpsız) geçiş işlemlerini yürütür. Ayrıca diskteki yer analizlerini de yapar.
* **ISystemUpdateService:** Başlatıcının (`GameLauncher.exe`) veya Güncelleyicinin (`Updater.exe`) yeni sürümlerinin inmesi ve yönetilmesinden sorumludur. 
* **IAppService:** Oyun sürüm doğrulama (Zorunlu vs İsteğe Bağlı) karar mekanizmalarını barındırır ve yeni oyun dosyalarını sisteme entegre eder.
* **IAppLauncherService:** `AvioTest.exe` uygulamasının ayağa kaldırılması ve eksik/bozuk dosya çıkması durumunda "Otomatik Onarım" (Auto-Repair) sürecini tetikleyen merkezi yapıdır.
* **IAppLifecycleService / SentryLifecycleService:** Uygulamadaki hataların, çökmelerin (crash) ve uyarıların `Sentry` üzerinden bulut log sistemine aktarılmasını sağlar.
* **IShutdownService:** Uygulamanın, loglar flush edildikten sonra (Sentry ile en fazla 3 saniye bekleme zaman aşımı yapılarak) güvenli ve sorunsuz sistem çıkışını (Environment.Exit) yönetir.

### 2.2 Modeller (Entites)
* **AppVersionDto:** Versiyon numarası, indirme linkleri, dosya boyutları (Byte) ve SHA256 korumalarını barındıran veri yapısıdır.
* **ServerManifest:** Launcher, Updater ve Game için ayrı `AppVersionDto` nesnelerini gruplayan ana kontrol verisidir.
* **DownloadState:** Dosyalar indirilirken arayüzde gösterilen ilerleme çubuğu yüzdesi ve indirme hızı (MB/s) bilgilerini raporlar.

## 3. Çalışma Döngüsü (Lifecycle)
Uygulamanın çalıştırıldığı ilk andan itibaren aşağıdaki akış gerçekleşir:
1. **İşletim Sistemi Desteği:** İşletim sisteminin Windows 10 (veya daha yeni) olup olmadığı denetlenir; eskiyse güvenlik uyarısı verilir.
2. **Bağlantı Kontrolü:** Network altyapısı kontrol edilir, erişim yoksa `Offline Mod` sekansına geçilerek oyuncunun internet olmadan var olan sürüm üzerinden oynamasına imkan tanınır.
3. **Versiyon Kontrolü:** Eğer internet varsa, merkezi API üzerinden (`versioncontrolmanagement-production.up.railway.app`) en yeni sürümler teyit edilir.
4. **Fiziksel Bütünlük Doğrulaması:** Dosyaların `App Data` tarafındaki varlıkları kontrol edilir. Eksik anahtar bir dosya tespit edilirse (örneğin ana .exe dosyasının antivirüs tarafından silinmesi), lokal sürüm `0.0.0` kabul edilerek zorunlu indirme tetiklenir.
5. **Launcher Sistemi Güncellemesi:** Gerekliyse `Updater.exe` devreye sokulup Launcher kapatılır ve o güncellenir.
6. **Oyun Güncellemesi:** Oyuna ait önemli ya da isteğe bağlı güncellemeler indirilir. Veri, geçici Temp klasöründe hazırlanıp bozulmalara (corruption) karşı atomic olarak ana dosyaya taşınır.
7. **Başlatma ve Onarım:** Oyuna giriş tetiklendiğinde exe process bazında çalıştırılır. Beklenmedik şekilde uygulamadan "Crash" veya bozuk DLL dönerse, Launcher sistemi kendini toparlayıp dosya onarım moduna girer.

## 4. Hata ve Kilitlenme Yönetimi (Error Handling)
UI Thread hataları global düzeyde yakalanır ve `App_DispatcherUnhandledException` metodundan Sentry platformuna iletilir.
"Zaman Aşımı", "İnternet Bağlantısı Koptu" ve "Dosyalar Paylaşılamadı/Başka Uygulama Tarafından Kullanılıyor" senaryolarında uygulamanın çökmesi yerine kullanıcıya durum anı mesajlarıyla geri bildirilir. Olası deadlock (kilitlenme) durumlarında `IShutdownService` devreye girerek süreci kapatır.

## 5. Dış Bağımlılıklar (Dependencies)
- **Newtonsoft.Json**: Formatlama, kaydetme ve API'den gelen mesajların okunması.
- **Sentry / Sentry.Extensions.Logging**: Gelişmiş hata yönetimi ve metrik izleme platformu integrasyonu.
- **Microsoft.Extensions.DependencyInjection**: Uygulama mimarisi tasarımının çekirdeği.
- **Microsoft.Extensions.Logging**: Tüm log mesajlarının konsolda, debug işlemlerinde ve Sentry'e iletilmesinde kullanılan köprü sistem.

---
*Uygulamanın Sürümü ve İsterleri:* .NET 10 (Masaüstü WPF)

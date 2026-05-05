# 🎮 AvioTest Game Launcher

<div align="center">

**AvioTest ekosisteminin masaüstü başlatıcı uygulaması**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Windows-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-All%20Rights%20Reserved-red.svg)](LICENSE.txt)
[![Sentry](https://img.shields.io/badge/Sentry-Enabled-362D59?logo=sentry&logoColor=white)](https://sentry.io)

</div>

---

## 📋 Genel Bakış

AvioTest Game Launcher, **AvioTest.exe** ana uygulamasının ve bağlantılı bileşenlerinin son kullanıcıya sorunsuz, güncel ve hatasız bir şekilde ulaştırılmasını sağlayan **WPF (Windows Presentation Foundation)** tabanlı bir başlatıcı (launcher) yazılımıdır.

### ✨ Temel Özellikler

- 🔄 **Otomatik Güncelleme** — Launcher, Updater ve oyun dosyalarının sürüm kontrolü ve güncellenmesi
- 🛡️ **SHA-256 Bütünlük Doğrulaması** — İndirilen dosyaların hash kontrolü ile veri güvenliği
- 🔧 **Otomatik Onarım (Auto-Repair)** — Bozuk veya eksik dosyaların tespit edilip yeniden indirilmesi
- 🌐 **Çevrimdışı Mod** — İnternet bağlantısı olmadan mevcut sürüm ile oynama desteği
- 📡 **Fallback Sunucu Desteği** — Ana sunucu erişilemez olduğunda yedek sunuculara otomatik geçiş
- 💾 **Atomic Dosya İşlemleri** — Güncelleme sırasında dosya bozulmasını önleyen güvenli geçiş mekanizması
- 📊 **Sentry Entegrasyonu** — Gerçek zamanlı hata izleme ve metrik toplama

---

## 🏗️ Mimari

Proje, **Dependency Injection (DI)** tabanlı temiz bir mimari ile yapılandırılmıştır.

```
GameLauncher/
├── App.xaml / App.xaml.cs          # Uygulama giriş noktası & DI konfigürasyonu
├── MainWindow.xaml / .xaml.cs      # Ana arayüz & uygulama yaşam döngüsü
├── Services/
│   ├── Abstract/                   # Servis arayüzleri (Interface'ler)
│   │   ├── IVersionService         # Sürüm kontrol servisi
│   │   ├── INetworkService         # İndirme & bağlantı yönetimi
│   │   ├── IFileService            # Dosya sistemi operasyonları
│   │   ├── ISystemUpdateService    # Launcher/Updater güncelleme
│   │   ├── IAppService             # Oyun sürüm yönetimi
│   │   ├── IAppLauncherService     # Uygulama başlatma & onarım
│   │   ├── IAppLifecycleService    # Yaşam döngüsü yönetimi
│   │   └── IShutdownService        # Güvenli kapanış yönetimi
│   └── Concrete/                   # Servis implementasyonları
│       ├── VersionService
│       ├── NetworkService
│       ├── FileService
│       ├── SystemUpdateService
│       ├── AppService
│       ├── AppLauncherService
│       ├── SentryLifecycleService
│       └── ShutdownService
├── Entites/                        # Veri modelleri
│   ├── AppVersionDto               # Sürüm bilgileri DTO
│   ├── ServerManifest              # Sunucu manifest yapısı
│   ├── DownloadState               # İndirme durumu
│   ├── UpdateFailedException       # Güncelleme hata modeli
│   └── UpdateRolledBackException   # Geri alma hata modeli
├── Exceptions/
│   └── FileServiceException        # Dosya servisi hataları
├── Assets/                         # Görseller, fontlar, ikonlar
└── GameLauncher.Tests/             # Birim testleri
    ├── AppServiceTests
    ├── SystemUpdateServiceTests
    └── VersionServiceTests
```

---

## 🔄 Çalışma Döngüsü

Uygulama başlatıldığında aşağıdaki akış sırasıyla çalışır:

```
┌─────────────────────┐
│  Uygulama Başlatma  │
└─────────┬───────────┘
          ▼
┌─────────────────────┐     Hayır    ┌──────────────────┐
│  OS Uyumluluk       ├────────────►│  Güvenlik Uyarısı │
│  Kontrolü (Win10+)  │             └──────────────────┘
└─────────┬───────────┘
          ▼ Evet
┌─────────────────────┐     Hayır    ┌──────────────────┐
│  İnternet Bağlantısı├────────────►│  Çevrimdışı Mod   │
│  Kontrolü           │             └──────────────────┘
└─────────┬───────────┘
          ▼ Evet
┌─────────────────────┐
│  Sürüm Kontrolü     │
│  (Merkezi API)       │
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  Dosya Bütünlük     │
│  Doğrulaması (SHA)   │
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  Launcher/Updater   │
│  Güncellemesi        │
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  Oyun Güncellemesi   │
│  (Atomic İşlemler)   │
└─────────┬───────────┘
          ▼
┌─────────────────────┐
│  AvioTest.exe        │
│  Başlatma & İzleme   │
└─────────────────────┘
```

---

## ⚙️ Gereksinimler

| Gereksinim | Minimum |
|---|---|
| **İşletim Sistemi** | Windows 10 veya üzeri |
| **Runtime** | .NET 10 Desktop Runtime |
| **Bellek** | 512 MB RAM |
| **Disk Alanı** | Uygulama boyutuna göre değişir |

---

## 🚀 Kurulum & Çalıştırma

### Geliştirme Ortamı

1. **Ön Koşullar**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) yüklü olmalıdır
   - Visual Studio 2022+ veya Rider (WPF desteği ile)

2. **Repoyu klonlayın**
   ```bash
   git clone https://github.com/Tahir1072a/GameLauncher.git
   cd GameLauncher
   ```

3. **Projeyi derleyin**
   ```bash
   dotnet build
   ```

4. **Uygulamayı çalıştırın**
   ```bash
   dotnet run --project GameLauncher
   ```

### Testleri Çalıştırma

```bash
dotnet test
```

---

## 📦 Bağımlılıklar

| Paket | Sürüm | Açıklama |
|---|---|---|
| **Newtonsoft.Json** | 13.0.4 | JSON serileştirme ve API yanıt işleme |
| **Sentry** | 6.2.0 | Hata izleme ve performans metrikleri |
| **Sentry.Extensions.Logging** | 6.2.0 | ILogger → Sentry köprüsü |
| **Microsoft.Extensions.DependencyInjection** | 10.0.0 | IoC konteyner altyapısı |
| **Microsoft.Extensions.Logging** | 10.0.0 | Loglama soyutlama katmanı |
| **Microsoft.Extensions.Logging.Console** | 10.0.0 | Konsol log sağlayıcısı |
| **Microsoft.Extensions.Logging.Debug** | 10.0.0 | Debug çıkış log sağlayıcısı |
| **Microsoft.Extensions.Http** | 10.0.0 | HttpClient fabrika yönetimi |

---

## 🛡️ Hata Yönetimi

Uygulama, çoklu katmanlı bir hata yönetim stratejisi kullanır:

- **UI Thread Hataları** → `DispatcherUnhandledException` ile yakalanır, Sentry'e iletilir
- **Ağ Hataları** → Timeout ve bağlantı kopma durumlarında kullanıcıya geri bildirim verilir
- **Dosya Hataları** → Kilitli dosya ve erişim sorunlarında açıklayıcı mesajlar gösterilir
- **Deadlock Koruması** → `IShutdownService` ile güvenli kapanış (max 3 saniye Sentry flush)

---

## 📄 Lisans

Bu proje **tescilli yazılım** olarak yayınlanmıştır. Tüm hakları saklıdır. Kaynak kodun görüntülenmesi, kopyalama, değiştirme veya dağıtma hakkı vermez. Detaylar için [LICENSE.txt](LICENSE.txt) dosyasına bakınız.

# Halı ve Koltuk Yıkama Yönetim Sistemi

Halı ve koltuk yıkama işletmeleri için geliştirilmiş modern, ölçeklenebilir ve tam otomatik sipariş takip ve yönetim platformudur.

## 🚀 Özellikler

- **ASP.NET Core 9 Web API:** Güçlü, asenkron ve RESTful mimari.
- **WhatsApp Cloud API Entegrasyonu:** Müşterilerin doğrudan WhatsApp üzerinden sipariş oluşturabilmesi ve durum takibi yapabilmesi.
- **Ofis Yönetim Paneli (`/index.html`):**
  - Gelen yeni talepleri ve alınacak siparişleri anlık takip etme.
  - Fiş numarası, tutar, POS durumu ve sipariş durumu güncelleme.
  - Müşteri adı ve telefon ile hızlı arama.
- **Sürücü Teslimat Paneli (`/driver.html`):**
  - Sahadaki sürücüler için mobil uyumlu görev listesi.
  - Adresleri tek dokunuşla Google Haritalar'da açma.
  - Halı için "Fiş Kesildi & Teslim Alındı", yerinde koltuk yıkama için "Yıkandı & Ödeme Alındı" durum yönetimi.
- **Otomatik WhatsApp Bildirimleri:**
  - Sipariş hazırlandığında, dağıtıma çıktığında veya tamamlandığında müşteriye otomatik WhatsApp durum mesajı.
- **Asenkron Kuyruk Mimarisi:**
  - `System.Threading.Channels` tabanlı yüksek performanslı arka plan mesaj işleme.
- **Entity Framework Core & SQL Server:**
  - Kod odaklı (Code-First) migration ve veri bütünlüğü.

## 🛠️ Kurulum ve Çalıştırma

### Gereksinimler
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server LocalDB veya SQL Server

### Adımlar

1. Depoyu klonlayın:
```bash
git clone https://github.com/hasanaliyucel-06/HaliYikamaAPI.git
cd HaliYikamaAPI
```

2. Bağımlılıkları yükleyin:
```bash
dotnet restore
```

3. Veritabanını güncelleyin:
```bash
dotnet dotnet-ef database update
```

4. Uygulamayı başlatın:
```bash
dotnet run --launch-profile http
```

5. Tarayıcınızdan erişin:
- **Ofis Paneli:** `http://localhost:5194/index.html`
- **Sürücü Paneli:** `http://localhost:5194/driver.html`
- **Swagger API Dokümantasyonu:** `http://localhost:5194/swagger`

## 📄 Lisans

Bu proje MIT lisansı ile korunmaktadır. Detaylar için LICENSE dosyasına bakabilirsiniz.

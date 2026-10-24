# İlerleme

Plan: [PLAN.md](PLAN.md). Her oturum sonunda güncellenir: ne bitti, ne kaldı, sıradaki adım.

## Durum

| Adım | Durum | Not |
|---|---|---|
| 1.1 Solution iskeleti | Tamam | Build 0 uyarı/0 hata, 3/3 test yeşil, AppHost'ta Web ve Qdrant sağlıklı |
| 1.2 NIM kablolaması + model spike | Tamam | ADR-001; çevrimdışı 12/12 ve Live kabul testleri 4/4 yeşil |
| 1.3 Privacy | Tamam | Çevrimdışı 57/57 (Privacy 38), Live 4/4 maskeleme katmanı arkasında |
| 1.4 Dokümanlar + PdfPig + chunking | Başlamadı | |
| 1.5 Ingestion + hybrid search | Başlamadı | |
| 1.6 Blazor sohbet + kaynaklar | Başlamadı | |

## Günlük

### 2026-10-24 (oturum 3): 1.3
- **Tasarım düzeltmesi (PLAN Bölüm 5 güncellendi):** Maskeleme katmanı en dışta değil, ham istemcinin hemen üstünde. Zincir: `FunctionInvoking (agent ekler) → PiiMaskingChatClient → raw`. En dışta olsaydı, function invocation döngüsünde eklenen araç sonuçları maskelenmeden modele giderdi.
- Privacy (bağımlılıksız):
  - `Checksums`: TCKN, Luhn ve IBAN mod-97 doğrulamaları.
  - `PiiDetector`: regex yalnızca aday üretir, sayısal kimlikler ayrıca checksum'dan geçmek zorunda. Çakışmalarda uzun eşleşme kazanır. Kart için yalnızca bitişik 13–19 hane ya da 4-4-4-4 ve 4-6-5 baskı biçimleri kabul ediliyor; serbest rakam dizileri komşu sayıları birleştirebileceği için bilerek dışarıda.
  - `PiiMasker`: tipli Türkçe yer tutucular (`[TCKN]`, `[KART_NO]`, `[IBAN]`, `[TELEFON]`, `[EPOSTA]`).
- Core/Safety:
  - `PiiMaskingChatClient`: metni, reasoning'i, araç argümanlarını, araç sonuçlarını ve `Instructions`'ı maskeler. Çağıranın geçmişini değiştirmez; `RawRepresentation`'ı kopyalamaz.
  - `PiiMaskingEmbeddingGenerator`.
  - `AddNimModels` artık yalnızca bu sarmalayıcıları kaydediyor; `TODO(1.3)` kapandı.
- Testler:
  - Privacy.Tests (38): seed'li Bogus ve algoritmayla üretilmiş 100–200'er örnek; checksum'dan geçmeyen numaralar maskelenmiyor; tutar, tarih ve sipariş numarası gibi olağan bankacılık metinlerine dokunulmuyor; telefon biçimleri; maskeleme idempotent.
  - Core.Tests: dekoratör testleri (kayıt tutan sahte iç istemciye hiç ham PII ulaşmıyor), `Di_exposes_only_pii_masking_wrappers`.
  - Privacy.Tests'teki geçici `--ignore-exit-code 8` kaldırıldı (Rag.Tests'te 1.4'e kadar duruyor).
- Sınırlar (bilinçli):
  - Yalnızca mobil telefon tanınıyor; sabit hatlar kapsam dışı.
  - Önek olmadan yazılan 10 haneli ve 5 ile başlayan sayılar telefon olarak maskeleniyor. Bu olası bir false positive, ama gizlilik açısından güvenli tarafta.
  - Bilinmeyen içerik tipleri (örneğin onay içerikleri) olduğu gibi geçiyor. Bu tipler serbest metin taşımıyor; 2.5'teki sızıntı testi bu varsayımı doğrulayacak.

### 2026-10-24 (oturum 2): 1.2
- Seçilen modeller ([ADR-001](decisions/ADR-001-nim-model-selection.md)): ana `nemotron-3-ultra-550b-a55b`, küçük `nemotron-3.5-bolt-e4b`, embedding `nemotron-3.5-embed-1b`. Model kimlikleri `src/Assistant.Web/appsettings.json` içinde.
- Bulgular:
  - `llama-nemotron-embed-1b-v2` NIM'de yok.
  - nemotron-3.5 embedding modelleri `input_type` olmadan 400 dönüyor.
  - GLM-5.3, DeepSeek V4.1 Flash ve Gemma 4 ücretsiz katmanda cevap vermedi.
  - embed-8b modeli 3 denemenin 2'sinde takıldı.
  - Ana model, kaynak yokken ayrıntı uyduruyor (telefon numarası, EFT saatleri).
- Kod:
  - Rag: `EmbeddingInputType` (query/passage sözleşmesi).
  - Core/Models: `NimOptions` (ApiKey `Nim:ApiKey`, yoksa `NVIDIA_API_KEY`; `MaxRetries` = 2), `NimClientFactory` (internal; chat için MEAI.OpenAI, endpoint sonunda `/`), `NimEmbeddingGenerator` (doğrudan HTTP, `input_type` zorunlu, hata mesajında anahtar yok), `AddNimModels` (ana `IChatClient`, keyed `"small"` ve `IEmbeddingGenerator`).
  - AppHost: `nim-api-key` gizli parametresi `Nim__ApiKey` olarak Web'e geçiyor.
- Testler:
  - Çevrimdışı: `NimEmbeddingGeneratorTests` (5), `ModelServiceCollectionExtensionsTests` (3).
  - Live: `NimLiveTests` (4 kabul testi, Web konfigürasyonu ve gerçek DI üzerinden), `ModelSpikeTests` (aday karşılaştırması, sonuçlar `artifacts/spike/` altına yazılıyor, git'e girmez).
- Bilinen borçlar:
  - ~~**1.3:** DI şu an ham istemcileri kaydediyor.~~ 1.3'te kapandı.
  - **1.6:** ServiceDefaults'taki standart HTTP resilience handler'ın deneme başına zaman aşımı (varsayılan 10 sn) ana modelin 15 saniyeye varan cevaplarını kesecek. `nim` HttpClient'ı için zaman aşımları ayrıca ayarlanmalı.

### 2026-10-24 (oturum 1): 1.1
- Plan onaylandı, docs/PLAN.md olarak kaydedildi.
- `git init` (main), `.gitignore`.
- Ortam: .NET SDK 10.0.402, Docker 29.7.2, Aspire şablonları 13.6.3.
- 1.1 tamamlandı:
  - `SelimBank.slnx`, `Directory.Build.props` (net10.0, Nullable, TreatWarningsAsErrors, latest-recommended analizör), `Directory.Packages.props` (CPM), `global.json`.
  - AppHost (Aspire SDK 13.6.4): Qdrant (kalıcı container + data volume) ve Web; Web'de `AddServiceDefaults` ve `MapDefaultEndpoints`.
  - Boş Blazor Web (Interactive Server), Privacy/Rag/Core kütüphaneleri; bağımlılıklar plandaki grafiğe göre (Core → Privacy, Rag; Web → Core, ServiceDefaults).
  - 3 test projesi (xunit.v3 4.0.2, Microsoft Testing Platform). `ArchitectureTests`: Privacy ve Rag sağlayıcı SDK'larına referans vermiyor, Privacy'nin proje bağımlılığı yok.
- Plandan sapmalar:
  - `.sln` yerine `.slnx`; .NET 10'un varsayılanı.
  - Testler VSTest yerine MTP ile çalışıyor; .NET 10 SDK, xunit.v3 4.x'i VSTest ile çalıştırmıyor. Filtre: `dotnet test --filter-not-trait "Category=Live"`. Coverlet şimdilik yok.
  - FakeBank.Api ve testi 2.1'e bırakıldı; boş proje taşımaya gerek yok.
  - Privacy.Tests ve Rag.Tests'te geçici `--ignore-exit-code 8` (sıfır test). İlk testler gelince kaldırılacak (TODO işaretli).

## Sıradaki adım
**1.4: Kaynak dokümanlar, PdfPig okuyucu ve chunking.**
- Kurgusal Selim Bank dokümanları (ücret tarifesi, kart sözleşmesi, SSS) betikle PDF olarak üretilecek; kamuya açık KVKK metni eklenecek. Telif ve kaynak notu `docs/sources/README.md`'ye yazılacak.
- Rag'de PdfPig okuyucu: sayfa, başlık ve "Madde N" tespiti.
- Yapı-farkında chunker: yaklaşık 350–500 token, yaklaşık %10 örtüşme, "Doküman > Bölüm > Madde" öneki; Türkçe cümle ayırıcı kısaltma ve sayı biçimlerine dikkat edecek.
- Testler: sabit bir PDF'ten beklenen chunk sınırları çıkmalı, madde ortasından bölme olmamalı. Rag.Tests'teki geçici `--ignore-exit-code 8` kaldırılacak.
- Açık soru: PDF'leri hangi araçla üreteceğiz? Seçenekler .NET'te QuestPDF ya da Markdown'dan dönüştürme; lisans durumu 1.4'ün başında kontrol edilecek.

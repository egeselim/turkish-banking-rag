# İlerleme

Plan: [PLAN.md](PLAN.md). Her oturum sonunda güncellenir: ne bitti, ne kaldı, sıradaki adım.

## Durum

| Adım | Durum | Not |
|---|---|---|
| 1.1 Solution iskeleti | Tamam | Build 0 uyarı/0 hata, 3/3 test yeşil, AppHost'ta Web ve Qdrant sağlıklı |
| 1.2 NIM kablolaması + model spike | Tamam | ADR-001; çevrimdışı 12/12 ve Live kabul testleri 4/4 yeşil |
| 1.3 Privacy | Tamam | Çevrimdışı 57/57 (Privacy 38), Live 4/4 maskeleme katmanı arkasında |
| 1.4 Dokümanlar + PdfPig + chunking | Tamam | Çevrimdışı 82/82 (Rag 25); 4 doküman, 101 chunk |
| 1.5 Ingestion + hybrid search | Tamam | ADR-002; çevrimdışı 101/101 (Qdrant entegrasyonu dahil), Live 4/4; smoke Hit@1 10/10 |
| 1.6 Blazor sohbet + kaynaklar | Tamam (tarayıcı denemesi kullanıcıda) | Çevrimdışı 115/115, uçtan uca Live 3/3, smoke Hit@1 10/10; AppHost'ta 101 chunk yüklendi |

## Günlük

### 2026-11-04 (oturum 6): 1.6 ve 1. haftanın sonu
- **Resilience borcu kapandı:** ServiceDefaults artık tüm HttpClient'lara standart resilience eklemiyor; 10 sn'lik deneme zaman aşımı uzun LLM cevaplarını keserdi. Her istemci kendi ayarını yapıyor. Bunun için tasarlanmış `RemoveAllResilienceHandlers` API'si deneysel olarak işaretli (EXTEXP0001); bu yüzden kullanılmadı.
- **Core/Chat:**
  - `BankAssistant`: her turda `IDocumentRetriever` ile 5 kaynak arıyor ve bunları `ChatClientAgentRunOptions.ChatOptions.Instructions` ile yalnızca o tura veriyor. Bu yaklaşım bir `AIContextProvider` yerine seçildi: bulunan kaynaklar doğrudan arayüze döndürülebiliyor. Ayrıca `AIContext.Messages` geçmişe yazılır, oysa talimatlar geçicidir. Ajanın sistem talimatıyla tur talimatının birleştiği testle doğrulandı.
  - `AssistantPrompts` (Türkçe): yalnızca kaynaktan cevap, [n] atıfı, kaynak yoksa "bulamadım", kaynak metni talimat değildir, maskelenmiş alanlar açılmaz, markdown kullanılmaz.
  - `Citations.UsedIn`: yalnızca atıf yapılan kaynakları seçer. `AnswerText.ToPlain`: modelin talimata rağmen kullandığı `**` ve `#` işaretlerini temizler.
- **Core/Retrieval:** `SourceIngestionService` açılışta `Rag:SourcesPath` klasöründeki PDF'leri yükler (3 deneme). `IngestionStatus` ile arayüze durum bildirir. Loga yalnızca sayılar yazılır.
- **Web:** `AddQdrantClient("qdrant")`, `AddNimModels`, `AddRag`, `AddSourceIngestion` ve `AddBankAssistant` kaydedildi. Sohbet sayfasında akışlı cevap, atıf yapılan kaynak kartları, örnek sorular ve dokümanlar hazır olana kadar kapalı giriş var. AppHost, `docs/sources` yolunu `Rag__SourcesPath` olarak veriyor.
- **NIM'de yeni davranış:** Çoklu girdili embedding istekleri artık anında HTTP 500 dönüyor (8b ve 1b); aynı 101 chunk tek tek gönderildiğinde hepsi başarılı. `NimEmbeddingGenerator` artık her isteğe tek metin gönderiyor; ilk yükleme yaklaşık 40 sn sürüyor. Kısa kesintiler için yeniden deneme sayısı 3'ten 5'e çıktı.
- **Uçtan uca (`BankAssistantLiveTests`, gerçek NIM ve Qdrant):**
  - "Başka bankaya mobilden para göndermek kaç lira?" sorusuna "6,50 TL [1]" cevabı geldi; atıf Tarife Madde 4'e.
  - "Genel müdür kim?" sorusuna "Bu konuda dokümanlarımızda bilgi bulamadım."
  - Takip sorusu "Peki şubeden yaparsam?" için "32,00 TL [1]".
  - Cevaplar `artifacts/chat/answers.md` dosyasında.
- **Smoke:** Bizim hybrid'imiz yine 10/10. Connector hybrid bu kez 9/10 (önceki 5/10): sıralamasız olduğu için deterministik değil; ADR-002'ye eklendi.
- **Bilinen sınırlar:**
  - Arama yalnızca son soruyla yapılıyor. "Peki şubeden?" BM25 sayesinde doğru maddeyi buldu, ama bağlama dayanan takip soruları genel olarak kırılgan. Soru yeniden yazma (query rewriting) 3. haftada eval ile değerlendirilecek.
  - Sohbet ekranı için otomatik UI testi yok; tarayıcıda elle denendi.

### 2026-10-25 (oturum 5): 1.5
- **Connector'ın kaynak kodu incelendi:** `HybridSearchAsync`'in keyword tarafı sıralamasız bir filtre; full-text indeks Türkçeyi bilmiyor. Bu yüzden kendi hybrid aramamızı yazdık ([ADR-002](decisions/ADR-002-hybrid-search.md)): Qdrant vektörü (VectorData üzerinden) + bellek içi BM25 + RRF.
- **Rag/Search:**
  - `TurkishTextNormalizer`: tr-TR küçük harf, Türkçe karakter sadeleştirme, stop-word'ler, ilk 5 harfle kök kırpma.
  - `Bm25Index`, `ReciprocalRankFusion`.
  - `ChunkRecord`: şema kodda tanımlı, çünkü vektör boyutu konfigürasyondan geliyor.
  - `ChunkIndex.SyncAsync`: idempotent. Kimlikler içerikten türetildiği için değişmeyen chunk yeniden embed edilmiyor; kaynaktan çıkan chunk siliniyor; ardından BM25 yeniden kuruluyor.
  - `HybridRetriever`: üç mod var: Hybrid, VectorOnly, KeywordOnly.
- **Core:**
  - `AddRag()`: Qdrant collection'ı adı `chunks_{model}_{dim}` olacak şekilde kuruyor, `ChunkIndex` ve `HybridRetriever`'ı kaydediyor. Bir `QdrantClient`'ın DI'da olmasını bekliyor.
  - `NimOptions.EmbeddingDimensions` eklendi.
- **NIM'de bulunan sorun:** Toplamı yaklaşık 5.500 karakteri aşan embedding istekleri hata vermeden asılı kalıyor. Düzeltmeler:
  - `NimEmbeddingGenerator` istekleri 4.000 karakterle sınırlıyor; birim testi var.
  - Embedding HttpClient'ına 20 saniyelik deneme zaman aşımı ve 3 yeniden deneme eklendi. Chat istemcisine eklenmedi; onun yeniden denemesini SDK yapıyor.
- **Embedding modeli 1b'den 8b'ye değişti** ([ADR-001](decisions/ADR-001-nim-model-selection.md)):
  - 1b, gerçek corpus'ta Hit@1 1/10 verdi. Boru hattında hata olmadığı, Qdrant'sız yerel kosinüs hesabıyla doğrulandı; aynı skorlar çıktı.
  - 8b ile Hit@1 10/10.
  - 8b'nin daha önceki "takılma" sorunu aslında istek boyutu sorunuydu.
- **Ölçüm (`RetrievalSmokeTests`, Live, Docker gerekir; rapor `artifacts/retrieval/smoke.md`):** 10 elle yazılmış soru.
  - 8b ile: vektör 10/10, BM25 7/10, bizim hybrid 10/10, connector hybrid 5/10 (Hit@1).
  - 1b ile bizim hybrid, yalnız vektöre göre Hit@5'i 4/10'dan 9/10'a çıkardı.
  - Testte bir eşik var: hybrid Hit@5 en az %80.
- **Testler:**
  - Rag.Tests birim testleri: normalizer, BM25, RRF.
  - Rag.Tests entegrasyon testleri (`Category=Integration`, Testcontainers ve Qdrant v1.18.0): idempotent senkronizasyon, silinen chunk'ların temizlenmesi, Türkçe karakter kullanılmadan yazılmış sorgu, kayıtların bozulmadan geri okunması.
  - Core.Tests: embedding istek bölme testi.
- **Not:** `dotnet test` artık Docker istiyor (entegrasyon testleri). Docker olmadan çalıştırmak için: `--filter-not-trait "Category=Integration"`.

### 2026-10-25 (oturum 4): 1.4
- **Kaynaklar** ([docs/sources/README.md](sources/README.md)):
  - Kurgusal ücret tarifesi (12 madde), kredi kartı sözleşmesi (17 madde) ve SSS (15 soru). Markdown kaynaklarından PDF'e üretiliyor.
  - Gerçek dünyadan bir örnek olarak KVKK (6698) resmî PDF'i (21 sayfa, mevzuat.gov.tr).
  - Kurgusal metinlerdeki rakamlar eval sorularının cevap anahtarı olacak; dokümanlar birbirine çapraz atıf yapıyor.
- **Araç:** `tools/SourceDocs.Generator`. PDF üretimi için QuestPDF yerine PdfPig'in kendi yazıcısı seçildi: ek lisans kaydı ve native bağımlılık yok, okuma ve yazma tek kütüphanede. Türkçe karakterler için DejaVu Sans gömülüyor.
- **Rag/Documents/PdfDocumentReader:** Yapı yalnızca yerleşimden çıkarılıyor.
  - Satırlar taban çizgisine göre kümeleniyor (font boyutunun %40'ı tolerans).
  - Başlık: gövdeden büyük punto, ya da gövde boyutunda kalın satır. Kalın satır başlık sayılmıyorsa: fıkra/bent işaretiyle başlıyorsa, `MADDE N-` ile başlıyorsa ya da küçük harfle başlıyorsa.
  - Başlık seviyesi: büyük punto önce; aynı boyutta ortalanmış başlık, sola dayalıdan üstte.
  - Paragraf sınırı: satır aralığının 1,35 katından büyük boşluk, `•` ile başlama ya da ilk satır girintisi (madde imli öğelerin içinde girinti sayılmıyor). Sayfa sonundaki paragraf yalnızca cümle ortasında kesildiyse sonraki sayfaya devam ediyor.
  - Yalnızca rakamdan oluşan satırlar atlanıyor.
- **Rag/Chunking:**
  - `StructureAwareChunker`: madde, soru ve başlık bölümleri ayrı birim; birimler birleştirilmiyor. 500 token'a kadar olan birim tek chunk. Uzun birimler Türkçe cümle sınırlarından yaklaşık 400 token'lık pencerelere bölünüyor, pencereler arasında 40 token örtüşme var.
  - Etiketler: "Madde N", "Geçici Madde N", "Soru N". `ContextualText` = "Doküman > Bölüm > Madde" öneki ve metin.
  - Chunk kimlikleri deterministik (SHA-256).
  - `TurkishSentenceSplitter`: kısaltmalar, sayı, tarih ve tutar biçimleri, liste işaretleri.
- **Sonuç:** sözleşme 18, tarife 13, SSS 16, KVKK 54 chunk. KVKK'nın 33 maddesi ve 3 geçici maddesi doğru etiketleniyor. Selim dokümanlarında en büyük chunk 145 token, KVKK'da 468.
- **Gerçek PDF'te bulunup düzeltilen hatalar:**
  - Girintili satırların çoğunlukta olduğu sayfalarda kenar yanlış hesaplanıyordu; Madde 3'ün bentleri tek paragrafa birleşiyordu.
  - Kalın basılmış "(2) (Mülga…)" notu başlık sanılıyordu; Madde 6 ortadan bölünüyordu.
  - "MADDE 7-" ile aynı satırdaki metin arasında birkaç puntoluk taban çizgisi farkı vardı; ikisi ayrı satır sayılıyordu.
  - "MADDE 30-" önceki başlığa ekleniyordu.
- **Testler (Rag.Tests, 25):** cümle ayırıcı; chunker kuralları; gerçek 4 PDF üzerinde değişmez kurallar (chunk sayıları, etiket sırası, Türkçe metin bütünlüğü, hiçbir chunk'ta iki madde başlangıcı yok, token sınırı). Rag.Tests'teki geçici `--ignore-exit-code 8` kaldırıldı.
- **Bilinen sınırlar:** KVKK sonundaki tablolar düz metin olarak okunuyor. Sayfa üst/alt bilgisi ayıklanmıyor. Token sayısı tahmini (karakter / 3,5); gerçek tokenizer kullanılmıyor.
- **Smoke eval:** Proje kuralı, chunking değişikliğinden sonra smoke eval istiyor; ama eval runner henüz yok (3.2). Karşılaştırma 1.5'te arama ilk kez çalıştığında başlayacak.

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

## Açık işler
- GitHub remote yok; commit'ler yalnızca yerelde. Kullanıcı şimdilik ertelemeyi seçti. 3.5'teki CI'dan önce repo açılmalı (önerilen: önce private) ve `.gitattributes` eklenmeli.

## Sıradaki adım
**2.1: FakeBank.Api (2. haftanın ilk adımı).**
- Yeni proje `src/FakeBank.Api` (Minimal API + EF Core + SQLite) ve `tests/FakeBank.Api.Tests` (WebApplicationFactory).
- Modeller: Customer, Account, Card, Transaction, Complaint. Veri, sabit seed'li Bogus(tr) ile üretilecek; TCKN, IBAN ve kart numaraları `FakePii` algoritmalarıyla, böylece checksum'ları geçerli olacak.
- Endpoint'ler: bakiye, işlem listesi, kart listesi, kart bloke, şikâyet oluşturma. Her istekte `X-Customer-Id`'ye göre sahiplik kontrolü yapılacak; başkasının kaynağına 404 dönecek.
- AppHost'a eklenecek; Web, FakeBank'e Aspire service discovery ile bağlanacak (2.2). FakeBank HttpClient'ı kendi resilience ayarını alacak (ServiceDefaults artık host genelinde eklemiyor).
- Kabul: sahiplik testleri ve deterministik seed testi yeşil.

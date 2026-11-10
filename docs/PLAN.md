# Selim Bank Asistanı: 3 Haftalık Uygulama Planı

## Bağlam
Bu proje, proje önerisindeki Türkçe bankacılık asistanının portföy için hazırlanan uygulaması.

Kullanıcının verdiği cevaplar dört kararı belirledi:
- Model sağlayıcısı **NVIDIA NIM** (build.nvidia.com).
- Kaynak dokümanlar **kurgusal ve kamuya açık** metinlerden oluşacak.
- Çalışma süresi **günde yaklaşık 4 saat**. Hesap haftada 6 oturum ve 1 tampon oturum üzerinden yapıldı.
- CI'da **retrieval offline** çalışacak, LLM eval'i isteğe bağlı olacak.

Plan onaylandıktan sonra bu dosya `docs/PLAN.md` olarak kaydedilecek. Yanına `docs/PROGRESS.md` açılacak ve iş 1.1 adımıyla başlayacak.

### NIM kararının sonuçları (itiraz ve düzeltmeler)
- **Agent katmanı Microsoft Agent Framework olacak.** Proje önerisi ve CV satırında "Semantic Kernel" yerine "Microsoft Agent Framework + NVIDIA NIM" yazılmalı. Nedenleri:
  - Agent katmanı için Semantic Kernel değil **Microsoft Agent Framework** (`Microsoft.Agents.AI`) seçildi. Agent Framework, Microsoft'un Semantic Kernel'in agent kısmının ve AutoGen'in yerine çıkardığı halef. Kodda Semantic Kernel paketi hiç olmayacak.
  - Semantic Kernel'in vektör connector'ları da artık deprecated; Qdrant connector'ı CommunityToolkit'e taşındı. Yani Semantic Kernel'e bağımlılık için kalan son gerekçe de ortadan kalktı.
  - CV, kodda gerçekten kullanılanı söylemeli. Mülakatta "Semantic Kernel" yazıp repoda Agent Framework görünmesi tutarsız durur.
  - Önerinin asıl argümanı, yani .NET ile Microsoft ekosisteminde ayrışmak, aynen geçerli. Hatta daha da güçlü, çünkü Agent Framework daha güncel.
- **Bağlantı biçimi.** NIM'in chat ve embedding uçları OpenAI uyumlu (`https://integrate.api.nvidia.com/v1`). Bu yüzden `Microsoft.Extensions.AI.OpenAI` paketi, endpoint değiştirilerek kullanılacak. Re-ranker ucu OpenAI uyumlu değil; onun için ince bir HttpClient yazılacak.
- **Model kimlikleri yalnızca konfigürasyonda tutulacak.** NIM modelleri hızla deprecate ediliyor; örneğin `llama-3.2-nv-rerankqa-1b-v2` 18.05.2026'da kaldırıldı. Kodda hiçbir model adı sabit yazılmayacak.
- **Kapsam itirazı.** İstediğin dağılımda 2. hafta en riskli hafta: API, 4 araç, onay UI'ı, PII ve audit aynı haftada. **PII maskeleme çekirdeğini 1. haftaya aldım.** İki nedeni var. Birincisi, proje kuralına göre LLM'e giden her metin ilk günden Privacy'den geçmeli; 1. haftanın sohbet ekranı zaten kullanıcı metnini LLM'e gönderiyor. İkincisi, bu kod saf ve kolay test edilir, tek oturumda biter. **Re-ranker'ı 1. haftadan çıkarıp 3. haftanın optimizasyonu yaptım.** Böylece öncesi/sonrası ölçümü temiz olur. Konuşma özetleme ve "küçük modelle yönlendirme" stretch hedefi oldu; ITSM bonusu kapsam dışı.

---

## 1. Solution ve proje yapısı

```
SelimBank.slnx                (.NET 10 varsayılan XML solution biçimi)
Directory.Build.props        (net10.0, Nullable, TreatWarningsAsErrors, LangVersion latest)
Directory.Packages.props     (Central Package Management: tüm sürümler burada sabit)
global.json                  (SDK 10.0.402, test runner: Microsoft.Testing.Platform)
src/
  SelimBank.AppHost          Aspire: Qdrant container, SelimBank.Api, Assistant.Web; NIM anahtarını parametre olarak geçirir
  SelimBank.ServiceDefaults  OTel (traces/metrics/logs), health checks, service discovery, HTTP resilience, PII scrub processor kaydı
  Assistant.Privacy          Saf kütüphane: dedektörler (TCKN, Luhn kart, IBAN mod-97, TR telefon, e-posta), IPiiMasker
  Assistant.Rag              PdfPig okuma, Türkçe normalizasyon, chunking, ingestion, hybrid search, IReranker arayüzü
  Assistant.Core             NIM sağlayıcı kablolaması, PII dekoratörleri, agent, araçlar, middleware'ler, audit, SelimBank istemcisi
  Assistant.Web              Blazor (Interactive Server) sohbet, streaming, onay butonları, demo müşteri seçimi, açılışta ingestion
  SelimBank.Api              Minimal API + EF Core SQLite + Bogus(tr) seed, sahiplik kontrolü
tests/
  Assistant.Privacy.Tests    Birim (algoritma, false-positive)
  Assistant.Rag.Tests        Birim (chunking, normalizasyon) + entegrasyon (Testcontainers Qdrant)
  Assistant.Core.Tests       Sahte IChatClient ile agent, onay, audit, sızıntı (leak) testleri, mimari testleri
  SelimBank.Api.Tests        WebApplicationFactory ile endpoint ve sahiplik testleri
eval/
  questions.jsonl, injection.jsonl, cache/ (offline embedding önbelleği)
  Eval.Runner                Konsol: --subset smoke|full, --mode retrieval|answer, JSON + Markdown rapor
docs/
  sources/ (kaynak PDF'ler + src/*.md kaynak metinler), decisions/ (ADR), PLAN.md, PROGRESS.md
tools/
  SourceDocs.Generator       Kurgusal kaynak PDF'leri Markdown'dan üretir (PdfPig yazıcısı + DejaVu Sans), 1.4'te eklendi
.github/workflows/ci.yml
```

**Bağımlılık grafiği** (ok, "referans verir" anlamında):

```
Privacy      → (hiçbiri)
Rag          → MEAI.Abstractions, VectorData.Abstractions, PdfPig      (sağlayıcı SDK'sı YOK, Privacy YOK)
Core         → Privacy, Rag, Microsoft.Agents.AI, MEAI, MEAI.OpenAI, CommunityToolkit.VectorData.Qdrant
Web          → Core, ServiceDefaults
SelimBank.Api → ServiceDefaults                                          (Core'u bilmez; Core onu HTTP ile çağırır)
AppHost      → Web, SelimBank.Api (proje referansı olarak), Aspire.Hosting.Qdrant
Eval.Runner  → Core (aynı DI kompozisyonu), MEAI.Evaluation.*
```

Neden böyle:
- Rag yalnızca `IEmbeddingGenerator` ve `IReranker` soyutlamalarını görür.
- Gerçek istemcileri ve PII dekoratörlerini Core üretir.
- Böylece "sağlayıcı SDK tipi Core dışına sızmaz" kuralı ve "her şey Privacy'den geçer" kuralı **tek bir kompozisyon noktasında** uygulanır.
- Bu kural bir **mimari testiyle** korunur: Core dışındaki hiçbir assembly `OpenAI*` assembly'sine referans vermez. Test reflection ile çalışır, ek paket gerektirmez.

---

## 2. NuGet paketleri (23.10.2026'da nuget.org'da kontrol edildi)

| Paket | Sürüm | Durum | Neden |
|---|---|---|---|
| Microsoft.Agents.AI | 1.26.0 | stabil | `ChatClientAgent`, `AgentSession`, `ApprovalRequiredAIFunction`, agent-run ve function-calling middleware (`AsBuilder().Use(...)`) |
| Microsoft.Extensions.AI | 10.11.0 | stabil | `IChatClient`/`IEmbeddingGenerator` pipeline'ı: `UseFunctionInvocation`, `UseOpenTelemetry`, caching, `DelegatingChatClient` |
| Microsoft.Extensions.AI.OpenAI | 10.11.0 | stabil | NIM'in OpenAI uyumlu ucuna `AsIChatClient()` / `AsIEmbeddingGenerator()` ile bağlanmak (yalnızca Core'da) |
| Microsoft.Extensions.VectorData.Abstractions | 10.11.0 | stabil | `VectorStoreCollection`, `[VectorStoreKey/Data/Vector]`, `IKeywordHybridSearchable<T>.HybridSearchAsync` |
| CommunityToolkit.VectorData.Qdrant | 1.0.1 | stabil (yeni) | Qdrant connector'ı. **`Microsoft.SemanticKernel.Connectors.Qdrant` deprecated** (son sürümü 1.74.0-preview); internetteki örneklerin çoğu hâlâ eski paketi gösteriyor |
| Aspire.Hosting.Qdrant | 13.6.3 | stabil | AppHost'ta Qdrant container'ı |
| Aspire.Qdrant.Client | 13.6.3 | stabil | Web ve Eval tarafında `QdrantClient`'ı DI'a, health check'e ve OTel'e kaydetmek |
| Microsoft.Extensions.AI.Evaluation(.Quality, .Reporting) | 10.11.0 | stabil | Groundedness, Relevance, Retrieval, Completeness gibi evaluator'lar; rapor üretimi |
| PdfPig | 0.1.16 | stabil | PDF metni, harf konumu ve font boyutu (başlık ve madde tespiti için). 0.1.17 alpha **kullanılmayacak** |
| Bogus | kurulumda doğrulanacak | — | `tr` locale ile sahte müşteri ve işlem verisi; sabit seed |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.x, kurulumda doğrulanacak | — | SelimBank veri katmanı |
| xunit.v3 | 4.0.2 | stabil | Testler; Microsoft Testing Platform modunda (.NET 10 SDK VSTest ile çalıştırmıyor), filtre: `--filter-not-trait` |
| Testcontainers.Qdrant | kurulumda doğrulanacak | — | Gerçek Qdrant'a karşı hybrid search entegrasyon testi |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.x | — | SelimBank.Api testleri |

**Bilerek eklenmeyen paketler:**
- `Microsoft.Agents.AI.OpenAI` / `.Hosting` / `.AGUI`: Hosting ve AGUI paketleri preview; `ChatClientAgent` zaten çekirdek pakette var.
- `Microsoft.Agents.AI.Harness`: onay için gerekli değil; standart `ApprovalRequiredAIFunction` yetiyor.

**API notları (ezbere yazılmayacak, 1.1 adımında kurulu sürümün dokümanıyla doğrulanacak):**
- Onay isteği `ToolApprovalRequestContent` tipiyle gelir; yanıt `.CreateResponse(bool)` ile üretilir. Eski dokümanlarda tip adı `FunctionApprovalRequestContent` olarak geçiyor, yani ad sürümden sürüme değişmiş.
- Approval-response binding varsayılan olarak açık: sahte ya da tekrar oynatılmış (replay) bir onay geçmez.
- Function-calling middleware yalnızca `FunctionInvokingChatClient` kullanan agent'larda çalışır (`ChatClientAgent` bunlardan biri).
- VectorData Qdrant hybrid search, vektör sorgusu ile keyword sorgusunu paralel çalıştırıp sonuçları birleştirir. Bunun için `IsFullTextIndexed = true` alanı gerekir. Sparse-vector hybrid desteği yok.
- NIM embedding ucu `input_type` (`query` | `passage`) parametresini ister. Bu parametre `EmbeddingGenerationOptions.AdditionalProperties` ile gönderilecek; 1.2'de doğrulanacak.

---

## 3–4. Haftalık yol haritası (her adım yaklaşık bir oturum, yani 3–4 saat)

Her adımın sonunda `dotnet build` ve `dotnet test` yeşil olmalı. Adım bitince `docs/PROGRESS.md` güncellenir.

### 1. hafta: RAG ve kaynak gösteren sohbet
**Hafta sonu sürümü:** `dotnet run --project src/SelimBank.AppHost` komutu Qdrant'ı, Web'i ve dashboard'u kaldırır. Kullanıcı ücret ya da sözleşme sorusu sorduğunda Türkçe, streaming ve kaynaklı bir cevap alır.

| # | Görev | Kabul kriteri | Doğrulama |
|---|---|---|---|
| 1.1 | Solution iskeleti, `Directory.*.props`, AppHost (Qdrant + boş Web), ServiceDefaults, 4 test projesi; `.gitignore`, `git init` | AppHost çalışıyor; dashboard'da Qdrant ve Web sağlıklı görünüyor | `dotnet build`, `dotnet test` (boş ama yeşil); dashboard'u elle kontrol |
| 1.2 | Core'da NIM kablolaması: `IChatClient` (ana model ve küçük model) ile `IEmbeddingGenerator` konfigürasyondan oluşur; anahtar user-secrets'tan ya da `NVIDIA_API_KEY`'den okunur. **Model spike:** 2–3 aday model, 10 Türkçe prompt ve 2 tool-call senaryosuyla denenir; seçim ADR-001'e yazılır | Seçilen model Türkçe cevap veriyor ve araç çağrısını doğru JSON ile yapıyor | `[Trait("Category","Live")]` testleri (CI'da atlanır); ADR-001 |
| 1.3 | Privacy: TCKN (algoritmik), kart (Luhn, 13–19 hane, boşluk ve tire varyantlarıyla), IBAN TR (mod-97), telefon (+90/0 5xx), e-posta dedektörleri; tipli yer tutucularla (`[TCKN]`, `[KART]`...) maskeleme. Core'da `PiiMaskingChatClient` ve `PiiMaskingEmbeddingGenerator` dekoratörleri yazılır ve DI'a **yalnızca sarılı hâlleri** kaydedilir | Geçerli PII maskeleniyor; geçersiz 11 haneli sayı ve Luhn'dan geçmeyen 16 haneli sayı maskelenmiyor | Bogus ve algoritmayla üretilmiş verilerle teori (theory) testleri; dekoratör testi: kayıt tutan sahte bir inner client'a hiç ham PII ulaşmıyor |
| 1.4 | Kaynak dokümanlar: kurgusal Selim Bank ücret tarifesi, kart sözleşmesi ve SSS (betikle üretilmiş PDF'ler) + kamuya açık KVKK metni. PdfPig okuyucu (sayfa, başlık ve "Madde N" tespiti) ve yapı-farkında chunker | Her chunk'ta doküman, sayfa, bölüm/madde ve metin var; chunk boyutu hedef aralıkta | Birim testleri: sabit PDF'ten beklenen chunk sayısı ve sınırları çıkıyor; madde ortasından bölme yok |
| 1.5 | Ingestion ve hybrid search: `ChunkRecord` (vektör + `IsFullTextIndexed` olan normalize `SearchText` + filtrelenebilir `DocId`); Türkçe normalizer (tr-TR küçük harf, basit ek kırpma); idempotent ingestion (içerik hash'i ile) | Aynı corpus iki kez yüklendiğinde kayıt sayısı değişmiyor; anahtar kelimeyle ve anlamsal sorguyla doğru chunk ilk 5'te | Testcontainers Qdrant entegrasyon testi, deterministik sahte embedding'le; normalizer birim testleri |
| 1.6 | Blazor sohbeti: streaming; RAG bağlamı bir araç (`SearchDocuments`) ya da context provider ile verilir; cevap altında kaynak kartları (doküman, sayfa, madde); Türkçe sistem prompt'u | "EFT ücreti ne kadar?" sorusunun cevabı doğru maddeyi gösteriyor; kaynak bulunamazsa "bilmiyorum" diyor | Core testi: sahte chat client ve sahte retriever ile atıf formatı doğru; elle demo; ilk smoke eval kaydı (3. haftadaki runner gelene kadar 10 soruluk elle yazılmış test) |
| (tampon) | Taşan işler | | |

### 2. hafta: sahte banka, agent araçları ve güvenlik
**Hafta sonu sürümü:** Kullanıcı demo müşteri seçer. Bakiye ve işlemleri sorabilir; "ikincisine itiraz et" dediğinde şikâyet açılır. Kart bloke etmek için ekranda onay butonu çıkar. Audit log ve trace'lerde PII yoktur.

| # | Görev | Kabul kriteri | Doğrulama |
|---|---|---|---|
| 2.1 | SelimBank.Api: Customer, Account, Card, Transaction, Complaint modelleri; sabit seed ile Bogus(tr); endpoint'ler; `X-Customer-Id`'ye göre **sahiplik kontrolü**; AppHost'a eklenmesi | Başkasının kartına ya da hesabına erişimde 404 dönüyor; seed deterministik | WebApplicationFactory testleri |
| 2.2 | Core: `ChatClientAgent`, `GetBalance` ve `ListTransactions` araçları, typed HttpClient (Aspire service discovery). **Müşteri kimliği araç parametresi değil:** sunucu tarafındaki oturumdan enjekte edilir | "Son 3 harcamam" sonrası "ikincisi" doğru işlemi buluyor (aynı `AgentSession`) | Sahte chat client'ın script'lediği tool-call'larla çok turlu test; canlı test (Live) |
| 2.3 | `BlockCard` ve `CreateComplaint` araçları `ApprovalRequiredAIFunction` ile sarılır; salt okunur `ListCards` da burada eklenir (bloke için kart kimliği gerekiyor; 2026-11-06'da 2.6'dan taşındı); Blazor'da `ToolApprovalRequestContent` gelince Onayla/Reddet kartı gösterilir | Onay olmadan API çağrısı **hiç** yapılmıyor; Reddet sonrasında da yapılmıyor; Onayla sonrasında tam bir kez yapılıyor | Core testi: SelimBank HttpHandler çağrı sayacı 0, 0 ve 1; sahte ya da tekrar oynatılmış onay mesajı reddediliyor |
| 2.4 | Function-calling middleware: **audit** (zaman, müşteri takma kimliği, araç, maskelenmiş argüman, sonuç durumu, süre) ayrı bir SQLite tablosuna yazılır. **Yetki savunması:** onay gerektiren araç onaysız gelirse middleware bunu da reddeder (derinlemesine savunma). Agent-run middleware: çıktı guard'ı (cevapta PII varsa maskeleme). Girdi guard'ı (injection sezgileri, uzunluk) `AskAsync`'in başında çalışır (2026-11-08): retrieval soruyu agent'tan önce embedding modeline gönderiyor | Her araç çağrısı tam bir audit satırı üretiyor, satırda ham PII yok | Audit testleri; guard birim testleri |
| 2.5 | Telemetri ve log garantisi: `UseOpenTelemetry` sensitive-data kapalı; ServiceDefaults'a `PiiScrubbingActivityProcessor` (tag'lerde maskeleme; event'leri değiştirilemediği için PII'li event taşıyan span gönderilmez, 2026-11-10 kararı) ve `PiiScrubbingLogProcessor` eklenir; kullanıcı metni hiçbir log şablonuna girmez | Kanarya PII'li uçtan uca bir konuşmada hiçbir log satırında, span tag'inde ya da event'te ve hiçbir LLM/embedding girdisinde kanarya geçmiyor | **Sızıntı testi** (Bölüm 5) |
| 2.6 | injection.jsonl'ın ilk 20 örneği (`ListCards` 2.3'e taşındı) | Injection örneklerinde araç kötüye kullanılmıyor | Canlı test (Live) |
| (tampon) | | | |

### 3. hafta: değerlendirme, optimizasyon, CI ve README
**Hafta sonu sürümü:** Öncesi/sonrası eval tablosu, yeşil CI ve README hazır.

| # | Görev | Kabul kriteri | Doğrulama |
|---|---|---|---|
| 3.1 | `questions.jsonl` (yaklaşık 100 soru; her biri `expected_doc`, `expected_section`, `reference_answer` ve `smoke` bayrağı taşır) ve `injection.jsonl` (yaklaşık 30); şema doğrulama | Her soru gerçek bir chunk'a eşleniyor; smoke alt kümesi yaklaşık 20 soru | Şema ve eşleşme testi |
| 3.2 | Eval.Runner, retrieval modu: Hit@1/5, MRR, p50/p95 gecikme. Diskte embedding önbelleği (model + metin hash'i, `eval/cache` altında, commit edilir) sayesinde **offline** çalışır. JSON + Markdown rapor; önceki raporla fark | `--subset smoke --mode retrieval` anahtarsız çalışıyor | Runner birim testleri; baseline raporu commit edilir |
| 3.3 | Answer modu: MEAI.Evaluation ile Groundedness, Relevance ve Completeness (hakem: NIM modeli); token ve maliyet; injection geçme oranı | Tam baseline raporu var | Raporun kendisi; `docs/eval/baseline.md` |
| 3.4 | **Optimizasyon: re-ranking.** `NimReranker : IReranker`; hybrid top-20 → rerank → top-5. Konfigürasyonla açılıp kapatılabilir. Öncesi/sonrası tablosu ve ADR | Hit@1 ve MRR'deki değişim ile gecikme maliyeti ölçülmüş | Aynı soru setinde iki rapor ve fark tablosu |
| 3.5 | GitHub Actions: build, test (Live hariç; Qdrant service container ile), smoke retrieval eval (offline). `workflow_dispatch` ya da `main` için `NVIDIA_API_KEY` secret'ı varsa answer eval çalışır. Anahtar asla echo edilmez | PR'da yeşil; secret olmayan fork PR'ında da yeşil | Actions çalıştırması |
| 3.6 | README: mermaid mimari diyagramı, demo GIF'i, eval tablosu, "neden bu tasarım", güvenlik bölümü, kurulum | README'deki komutlar sıfırdan çalışıyor | Temiz klonla elle deneme |
| stretch | Küçük modelle niyet yönlendirme (SSS mi, işlem mi, kapsam dışı mı), maliyet ölçümü; konuşma özetleme; embedding ve cevap caching | | |

---

## 5. Güvenlik tasarımı

**PII'nin LLM'e, embedding'e, loga ve OTel'e hiç ulaşmaması: dört katman ve onu kanıtlayan bir test**

1. **Tek kapı (kompozisyon).** Ham NIM istemcileri yalnızca Core'daki fabrika metodu içinde oluşturulur ve DI'a **hiç kaydedilmez**. DI'da yalnızca şu zincirler bulunur:
   - `FunctionInvoking (agent ekler) → PiiMaskingChatClient → OpenTelemetry(sensitive=false) → raw`
   - `PiiMaskingEmbeddingGenerator → raw`

   **1.3'te düzeltildi:** Maskeleme katmanı en dışta değil, ham istemcinin hemen üstünde, yani function invocation'ın *içinde* durur. Nedeni şu: `FunctionInvokingChatClient`, araç sonuçlarını kendi döngüsü içinde konuşmaya ekleyip içerideki istemciyi yeniden çağırır. Maskeleme dışarıda kalsaydı bu sonuçlar maskelenmeden modele giderdi. Bu sırayla modele giden **her** istek maskelenir: kullanıcı mesajı, araç çağrısı argümanları, araç sonuçları (`FunctionResultContent`), reasoning içeriği ve `Instructions`. Mesajlar kopyalanır; `RawRepresentation` kopyaya taşınmaz, çünkü sağlayıcı bu alanı doğrudan gönderip maskeyi atlayabilir.
2. **PII gerektirmeyen araç sözleşmeleri.** Müşteri kimliği LLM'den alınmaz; oturumdan gelir. Kartlar `cardId` ve son 4 haneyle tanımlanır. Araçlar tam kart numarası, TCKN ya da IBAN döndürmez; döndürse bile 1. katman bunları maskeler. Bu sayede geri çözülebilir bir "vault"a ihtiyaç kalmaz (YAGNI).
3. **Log.** Kullanıcı ve model metni log şablonlarına hiç girmez; yalnızca kimlikler, sayılar ve durum loglanır. Audit kaydı maskelenmiş argümanla yazılır.
4. **OTel.** MEAI ve Agent Framework telemetrisinde sensitive data kapalıdır. Ek güvenlik ağı olarak ServiceDefaults'taki `PiiScrubbingActivityProcessor` tüm string tag ve event'leri export'tan önce Privacy'den geçirir. Qdrant ve HttpClient span'larında URL ve query dışında içerik yoktur.
5. **Kanıt: sızıntı testi** (`Assistant.Core.Tests`, 2.5). Bu test:
   - Bogus ve algoritmayla geçerli bir TCKN, kart, IBAN ve telefon (kanarya) üretir.
   - Bunları içeren çok turlu bir konuşmayı gerçek DI kompozisyonu üzerinden çalıştırır. Yalnızca en içteki ham istemci kayıt tutan bir sahteyle değiştirilir.
   - Üç yeri kontrol eder: (a) sahte LLM/embedding'in aldığı tüm metin, (b) in-memory `ILoggerProvider` çıktısı, (c) in-memory OTel exporter'daki tüm span'lar.
   - Kanaryalardan **hiçbiri** bu üç yerden birinde bulunamaz.

   Mimari testleri de buna eşlik eder: Core dışında OpenAI assembly referansı yoktur, DI'dan çözülen bütün `IChatClient` ve `IEmbeddingGenerator`'lar PII sarmalayıcısıdır (`Di_exposes_only_pii_masking_wrappers`).

**Onay ve yetki kontrolünün yeri**
- **Onay:** Agent Framework katmanında. `BlockCard` ve `CreateComplaint` araçları `ApprovalRequiredAIFunction` ile sarılır; `FunctionInvokingChatClient` bunları onay gelmeden çalıştırmaz ve approval-response binding açıktır. İkinci kontrol 2.4'teki function middleware'dedir. Prompt'ta onay kuralı **yoktur**.
- **Yetki:** Üç yerde.
  1. Web: demo müşteri seçimi sunucu tarafındaki oturumda tutulur.
  2. Core: `CustomerContext` araçlara DI ile enjekte edilir; LLM'den gelen kimlik parametresi yoktur.
  3. SelimBank.Api: her endpoint sahiplik kontrolü yapar, başka müşterinin kaynağı için 404 döner.

  LLM ikna edilse bile başka bir müşterinin verisine erişemez.
- **Injection:** Doküman içeriği ve araç sonuçları prompt'a "veri" olarak işaretlenmiş bloklarla eklenir. Girdi guard'ı kalır, ama asıl güvence yukarıdaki yapısal kontrollerdir. Sonuç injection eval'iyle ölçülür.

---

## 6. Riskler ve açık kararlar

| Konu | Seçenekler | Öneri |
|---|---|---|
| **Ana LLM (NIM)** | **Karar verildi (ADR-001):** `nemotron-3-ultra-550b-a55b`; yedek `nemotron-3.5-lightning-30b-a3b` | GLM-5.3, DeepSeek V4.1 Flash ve Gemma 4 ücretsiz katmanda cevap vermedi. Kalan riskler: kaynak yokken uydurma (önlem: 1.6'daki sistem prompt'u, ölçüm: 3.3'teki Groundedness) ve erişilebilirlik (yedek model konfigürasyonda hazır) |
| **Küçük model** | **Karar verildi (ADR-001):** `nemotron-3.5-bolt-e4b` (9/10 doğru, 0,2 sn) | Yalnızca yönlendirme ve sınıflandırma için kullanılır, güvenlik kararı vermez. Eval hakemi ana modeldir |
| **Embedding** | **Karar verildi, 1.5'te revize edildi (ADR-001):** `nemotron-3.5-embed-8b` (asimetrik, 4096 boyut). 1b, gerçek corpus'ta Hit@1 1/10 verdi. 8b'nin "güvenilmezliği" aslında istek boyutu sorunuydu (5.500 karakterin üstündeki istekler asılı kalıyor) | 3.2'de 8b ile Ollama bge-m3 karşılaştırılacak. Offline CI disk önbelleğiyle çözülür. Model ve boyut Qdrant collection adına yazılır (`chunks_{model}_{dim}`) |
| **Re-ranker** | (a) NIM `llama-nemotron-rerank-1b-v2` (Türkçe değerlendirilmiş, 8K token), (b) LLM ile rerank (pahalı, yavaş), (c) yerel bge-reranker-v2-m3 (ONNX ya da TEI container; kurulum yükü var) | **(a)**. Ucu farklı ve OpenAI uyumlu değil; 3.4'te request/response şeması kurulu dokümana bakılarak yazılır |
| **Türkçe chunking** | (a) sabit token penceresi, (b) cümle tabanlı, (c) yapı-farkında (başlık, "Madde N", numaralı fıkra) ve uzun maddede cümle tabanlı yedek | **(c)**: hedef yaklaşık 350–500 token, yaklaşık %10 örtüşme, chunk başına "Doküman > Bölüm > Madde" başlık önekiyle (bağlamsal başlık). Türkçe cümle ayırıcı kısaltmalara ("vb.", "Md.", "T.C.") ve sayılara ("1.000,50 TL") dikkat eder. Alternatif yöntem eval ile karşılaştırılabilir |
| **Türkçe keyword araması** | Qdrant full-text tokenizer'ı Türkçe ekleri ("kartımı", "kartın") eşlemez | **Uygulandı (1.5):** `TurkishTextNormalizer`: tr-TR küçük harf, Türkçe karakter sadeleştirme (ı→i, ş→s…), stop-word'ler, ilk 5 harfle kök kırpma; bellek içi BM25 |
| **Hybrid füzyonu** | Connector'ın varsayılan birleştirmesi ya da iki ayrı sorgu ve kendi RRF'imiz | **Karar verildi (ADR-002):** kendi RRF'imiz. Connector'ın keyword tarafı sıralamasız bir filtre; ölçümde connector hybrid Hit@1 5/10, bizim hybrid 10/10 |
| **NIM rate limit ve trial şartları** | Free tier limitleri belirsiz; trial ToS üretim kullanımı için değil | Eval runner'da eşzamanlılık sınırı ve retry (ServiceDefaults resilience); embedding önbelleği. Portföy için uygun |
| **Paket değişkenliği** | AF ve MEAI neredeyse haftalık sürüm çıkarıyor | Sürümler `Directory.Packages.props`'ta sabit; 3 hafta boyunca yükseltme yok (güvenlik düzeltmesi hariç) |
| **Kapsam** | Haftada yaklaşık 25 saat | Her haftada 1 tampon oturum var. Taşma olursa ilk kesilecekler: 2.6'daki injection genişletmesi, 3.3'teki Completeness metriği, GIF |

---

## Doğrulama (uçtan uca)
- `dotnet build`, `dotnet test`: Live dışındaki tüm testler yeşil (kırmızıyla iş bitmiş sayılmaz).
- `dotnet run --project src/SelimBank.AppHost`: dashboard'da tüm kaynaklar sağlıklı. Elle senaryolar:
  - SSS sorusu sorulur, cevap kaynaklı gelir.
  - "Son 3 harcamam" sorulur, ardından "ikincisine itiraz" denir, şikâyet açılır.
  - "Kartımı bloke et" denir, onay kartı çıkar; Reddet ile işlem yapılmaz, Onayla ile yapılır.
  - Dashboard trace'lerinde girilen TCKN görünmez.
- `dotnet run --project eval/Eval.Runner -- --subset smoke`: rapor oluşur, önceki raporla fark gösterilir.
- CI: PR'da yeşil.

## Onaydan sonraki ilk adım
1. Bu planı `docs/PLAN.md` olarak kaydetmek; `docs/PROGRESS.md` dosyasını başlatmak.
2. 1.1'e başlamak: `git init`, solution iskeleti, `Directory.Packages.props`, Aspire şablonu.

## Kaynaklar (23.10.2026)
- NuGet: [Microsoft.Agents.AI](https://www.nuget.org/packages/Microsoft.Agents.AI), [Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI), [VectorData.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.VectorData.Abstractions), [CommunityToolkit.VectorData.Qdrant](https://www.nuget.org/packages/CommunityToolkit.VectorData.Qdrant), [SK Qdrant (deprecated)](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.Qdrant), [Aspire.Hosting.Qdrant](https://www.nuget.org/packages/Aspire.Hosting.Qdrant), [PdfPig](https://www.nuget.org/packages/PdfPig), [MEAI.Evaluation.Quality](https://www.nuget.org/packages/Microsoft.Extensions.AI.Evaluation.Quality)
- Dokümanlar: [AF tool approval](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval), [AF middleware](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/), [VectorData hybrid search](https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/hybrid-search), [NIM rerank 1b v2](https://docs.api.nvidia.com/nim/reference/nvidia-llama-nemotron-rerank-1b-v2), [build.nvidia.com modeller](https://build.nvidia.com/models)

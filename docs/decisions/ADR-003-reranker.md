# ADR-003: Re-ranker

- **Durum:** Kabul edildi. Re-ranker kodu ve ölçümü var, ama **uygulamada kapalı** (`Rag:RerankModel` boş). Seçim kuralı için [ADR-002](ADR-002-hybrid-search.md#seçim-kuralı-2026-11-12-34-sonuçlar-alınmadan-önce-yazıldı).
- **Tarih:** 2026-11-12
- **Adım:** 3.4

## Bağlam

- PLAN'da seçilen `llama-nemotron-rerank-1b-v2` (ve eski adı `llama-3.2-nv-rerankqa-1b-v2`) NIM'de kullanımdan kalkmış. Ham sonda: `ai.api.nvidia.com/v1/retrieval/nvidia/llama-nemotron-rerank-1b-v2/reranking` → **HTTP 410 Gone**, "end of life on 2026-08-25".
- Aynı uç biçimiyle erişilebilen iki model bulundu: `nvidia/nemotron-3.5-rerank-8b` ve `nvidia/llama-nemotron-rerank-vl-1b-v2`. `llama-nemotron-rerank-500m-v2` ve `nemotron-3.5-rerank-1b` bu hesapta 404.
- Rerank modelleri `/v1/models` listesinde görünmüyor; yalnızca `retrieval` uçlarında.
- **Şema** (sondayla doğrulandı): istek `{model, query: {text}, passages: [{text}], truncate: "END"}`, cevap `{rankings: [{index, logit}], usage}`.

## Tasarım

- `Assistant.Rag`: `IReranker` (pasaj başına skor) ve `RerankingRetriever`. Seçilen yöntemle (vektör ya da Hybrid) 20 aday alıp sıralıyor ve ilk 5'i döndürüyor. Eşit skorda iç sıra korunuyor.
- `Assistant.Core`:
  - `NimReranker`: doğrudan HTTP, kendi HttpClient'ı (`nim-rerank`; deneme 15 sn, 3 tekrar). Hata mesajı durum kodunu ve kısaltılmış gövdeyi içeriyor, anahtarı değil.
  - `PiiMaskingReranker`: soru kullanıcı girdisi olduğu için soru ve pasajlar maskeleniyor. `RerankerFactory` yalnızca bu sarmalayıcıyı döndürüyor (testli).
  - `DiskRerankCache`: eval ve CI için (`eval/cache/rerank/`, commit edilir). Embedding önbelleğiyle aynı kurallar: maskelemenin altında, metin değil karma saklıyor, offline modda eksik skor hata veriyor. Cross-encoder her (soru, pasaj) çiftini ayrı puanladığı için yalnızca eksik pasajlar soruluyor.
  - Konfigürasyon: `Rag:Mode` (`Hybrid` | `VectorOnly`), `Rag:RerankModel` (boşsa kapalı), `Rag:RerankCandidates` (20). `BankAssistant` arama yöntemini artık konfigürasyondan alıyor.

## Ölçüm (2026-11-12, 92 cevaplı soru, embed-8b)

| Yöntem | Hit@1 | Hit@5 | Hit@20 | MRR |
|---|---|---|---|---|
| Yalnız vektör | 88 | 92 | 92 | 0,9746 |
| Yalnız BM25 | 71 | 84 | 92 | 0,8268 |
| Hybrid | 79 | 90 | 92 | 0,9103 |
| Vektör + rerank 8b | **91** | 92 | – | **0,9946** |
| Hybrid + rerank 8b | **91** | 92 | – | **0,9946** |
| Vektör + rerank vl-1b | 87 | 92 | – | 0,9674 |
| Hybrid + rerank vl-1b | 87 | 92 | – | 0,9674 |

- Hit@20 her temel yöntemde 92/92: doğru kaynak her zaman aday havuzunda. Bu yüzden 8b, vektör ve Hybrid adaylarıyla soru soru aynı sırayı veriyor.
- 8b, KVKK (MRR 0,82 → 1,00, Hybrid'e göre) ve dokümandaki kelimeleri kullanmayan sorularda (0,85 → 1,00) belirgin. Tek kalan hata q-039 (2. sırada).
- vl-1b, vektörden biraz kötü (0,9674'e karşı 0,9746).
- **Gerçek gecikme (önbellek boşken, ilk çalışan yöntem):** 8b p50 368 ms, p95 619 ms; vl-1b p50 270 ms, p95 342 ms. Yalnız vektör p95 yaklaşık 7 ms. Önbellekten okunan sonraki yöntemlerin gecikmesi gerçek maliyeti göstermiyor.

## Karar (kural uygulandı)

- **Kural 2:** Vektör + 8b, yalnız vektöre göre MRR'ı **+0,01993** artırıyor; eşik 0,02. Koşul sağlanmıyor. Hybrid + 8b, Hybrid'e göre +0,084 artırıyor ve p95 1,5 sn'nin altında; koşul sağlanıyor.
- **Kural 1 ve 3:** Uygun adaylar arasında en yüksek MRR Hybrid + 8b (0,9946). Ancak yalnız vektörle farkı (0,0199) 0,02'nin altında. Kural 3 gereği daha basit ve hızlı olan seçiliyor: **yalnız vektör, re-ranker yok.**
- **Sınır çok dar:** Fark, 92 sorunun 3'ünde doğru kaynağın 2. ya da 3. sıradan 1. sıraya çıkması demek, ve her soruya yaklaşık 0,6 sn ile bir NIM çağrısı ekliyor. Uygulama zaten 5 kaynağı birden modele verdiği için Hit@5 (her ikisinde 92/92) cevap kalitesi açısından daha belirleyici olabilir. Bu, answer eval'iyle kontrol ediliyor (ADR-002, kural 4).
- Re-ranker kodu kalıyor. `Rag:RerankModel=nvidia/nemotron-3.5-rerank-8b` ile açılabilir; soru seti büyür ya da embedding modeli zayıflarsa yeniden ölçülmeli.

## Sınırlar

- Ölçüm tek bir soru setiyle yapıldı ve soruları dokümanları bilen kişi yazdı. Re-ranker'ın asıl değeri, daha zor ve gerçek kullanıcı sorularında daha büyük olabilir.
- Rerank gecikmesi ücretsiz katmanda ölçüldü ve günden güne değişebilir.

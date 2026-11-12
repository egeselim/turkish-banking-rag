# ADR-002: Hybrid search, Qdrant vektörü + kendi BM25'imiz + RRF

- **Durum:** Kabul edildi; **2026-11-12'de (3.4) uygulamanın varsayılanı yalnız vektör oldu**, aşağıdaki "Karar" bölümü
- **Tarih:** 2026-10-25
- **Adım:** 1.5

## Karar

- **Vektör araması:** Qdrant'ta, `Microsoft.Extensions.VectorData` üzerinden (`CommunityToolkit.VectorData.Qdrant`), kosinüs benzerliğiyle.
- **Keyword araması:** Bellek içi BM25 (`Bm25Index`). Metinler Türkçe normalize ediliyor (`TurkishTextNormalizer`): tr-TR küçük harf, Türkçe karakterlerin sadeleştirilmesi (ı→i, ş→s…), stop-word'lerin atılması ve ilk 5 harfle kök kırpma.
- **Birleştirme:** Kendi Reciprocal Rank Fusion kodumuz (k = 60, her listeden 20 aday).
- Connector'ın kendi `HybridSearchAsync`'i **kullanılmıyor**.

## Neden connector'ın hybrid'i değil

Kaynak kodu (`CommunityToolkit/AI`, `MEVD/src/Qdrant/QdrantCollection.cs`) incelendi:
- Keyword tarafı bir Qdrant **filtresi**: anahtar kelimelerden biri geçiyorsa eşleşme sayılıyor (`Should` + `Match.Text`). Sorgu ya da skor yok. Eşleşen noktalar ilgiden bağımsız sırayla dönüyor ve bu sıra RRF'e giriyor. "kart" gibi her yerde geçen bir kelime, sıralamaya fiilen rastgele chunk'lar sokuyor.
- Full-text indeks Qdrant'ın varsayılan tokenizer'ıyla kuruluyor. Türkçe ekleri ("kartımı"/"kart") ve Türkçe karakter kullanılmadan yazılmış sorguları ("caldirdim") eşleştirmiyor.
- Sparse-vector (BM25) hybrid, connector tarafından desteklenmiyor. Qdrant istemcisini doğrudan kullanmak ise projenin VectorData soyutlama kuralını deler.

## Ölçüm (`RetrievalSmokeTests`, gerçek NIM ve Docker'da Qdrant, 101 chunk, 10 elle yazılmış soru)

Sorular dokümandaki kelimeler kullanılmadan, müşteri ağzından yazıldı ("Asgariyi ödeyemezsem başıma ne gelir?").

| Yöntem | embed-1b Hit@1 / Hit@5 / MRR | embed-8b Hit@1 / Hit@5 / MRR |
|---|---|---|
| Yalnız vektör | 1/10 · 4/10 · 0,20 | **10/10 · 10/10 · 1,00** |
| Yalnız BM25 | 7/10 · 10/10 · 0,83 | 7/10 · 10/10 · 0,83 |
| **Bizim hybrid (RRF)** | 5/10 · 9/10 · 0,64 | **10/10 · 10/10 · 1,00** |
| Connector hybrid | 1/10 · 5/10 · 0,24 | 5/10 · 10/10 · 0,75 |

- **Connector hybrid deterministik değil:** 2026-11-04'teki tekrar ölçümde aynı sorular, aynı model ve aynı chunk'larla Hit@1 9/10 çıktı (önceki ölçümde 5/10). Keyword tarafı sıralamasız olduğu için sonuç, noktaların Qdrant'a yazılma sırasına göre değişiyor. Bizim hybrid'imiz iki ölçümde de 10/10. Ölçülebilir olmayan bir arama, eval'e dayalı geliştirme için uygun değil.
- Aynı vektörlerle connector hybrid, yalnız vektörden **kötü**. Sıralamasız keyword listesi doğru cevabı ikinci sıraya itiyor (8b'de 5 soruda).
- Zayıf embedding ile (1b), bizim hybrid'imiz yalnız vektöre göre çok daha dayanıklı: Hit@5 4/10'dan 9/10'a çıkıyor. BM25 bir güvenlik ağı görevi görüyor.
- 8b ile yalnız vektör ve hybrid ikisi de bu küçük sette tavanda. Hybrid'in katkısı 3. haftadaki yaklaşık 100 soruluk eval setinde, özellikle Türkçe karakter kullanılmadan yazılmış ve kelimesi kelimesine ifadeli sorularda ölçülecek.

## Güncelleme, 2026-11-11 (3.2): 100 soruluk eval hybrid'in aleyhine

Eval.Runner, `eval/questions.jsonl` (92 cevaplı soru), embed-8b, aynı 101 chunk. Rapor: `eval/reports/baseline-retrieval.md`.

| Yöntem | Hit@1 | Hit@5 | MRR |
|---|---|---|---|
| Yalnız vektör | **88/92** | **92/92** | **0,97** |
| Yalnız BM25 | 71/92 | 84/92 | 0,83 |
| Bizim hybrid (RRF) | 79/92 | 90/92 | 0,91 |

- 1.5'teki 10 soru (q-001..q-010) birebir aynı sonucu verdi (vektör ve hybrid 10/10, BM25 7/10). Fark, yeni sorulardan geliyor.
- Hybrid 11 soruda vektörden farklı sıra veriyor. Bunlardan **10'unda kötüleştiriyor**, 1'inde (q-027) iyileştiriyor. İki soruyu (q-034, q-078) ilk 5'ten tamamen düşürüyor.
- **Mekanizma:** Bu soruların çoğunda BM25 doğru chunk'ı ilk 5'te bulamıyor, ama 20 yanlış adayı eşit ağırlıkla RRF'e giriyor. Vektörün 1. sıradaki doğru cevabı 2.–4. sıraya itiliyor. En çok KVKK (Hit@1 17/24) ve dokümandaki kelimeleri kullanmayan sorular (paraphrase 26/33) etkileniyor.
- 1.5'teki "BM25 güvenlik ağı" gerekçesi zayıf embedding (1b) içindi. 8b ile bu sette ağ işe yaramıyor, zarar veriyor.
- **Karar henüz değişmedi.** Seçenekler: (a) yalnız vektör, (b) vektöre daha çok ağırlık veren RRF, (c) hybrid adayları + re-ranker (3.4). Arama değişikliği olduğu için kullanıcı kararı ve aynı baseline'la öncesi/sonrası ölçümü gerekiyor.

## Seçim kuralı (2026-11-12, 3.4; sonuçlar alınmadan önce yazıldı)

Adaylar: yalnız vektör, Hybrid, yalnız vektör + re-ranker, Hybrid + re-ranker. Re-ranker iki modelle ölçülür (`nemotron-3.5-rerank-8b`, `llama-nemotron-rerank-vl-1b-v2`); her ikisi de seçilen yöntemin ilk 20 adayını sıralar.

1. Tam sette (92 cevaplı soru) en yüksek MRR'ye sahip yapılandırma seçilir.
2. Re-ranker'lı bir yapılandırma ancak şu iki koşulu sağlarsa seçilebilir: aynı aday yönteminin re-ranker'sız hâline göre MRR'yi en az 0,02 artırır (3.3'te görülen ölçüm gürültüsünün üstü) ve p95 gecikmeye en fazla 1,5 sn ekler (gerçek NIM çağrısıyla ölçülen değer).
3. MRR farkı 0,02'den küçükse daha basit ya da daha hızlı olan seçilir.
4. Seçilen yapılandırma answer modunda bir kez çalıştırılır ve 3.3 baseline'ıyla (Hybrid) aynı koşullarda karşılaştırılır. Cevap kalitesi belirgin biçimde düşerse karar kullanıcıya geri getirilir.

## Karar (2026-11-12, 3.4): uygulama yalnız vektörle arıyor

Seçim kuralı uygulandı; ayrıntı ve tablo [ADR-003](ADR-003-reranker.md)'te.
- Uygulamanın araması `Rag:Mode=VectorOnly`, re-ranker kapalı. Hybrid (MRR 0,9103) yalnız vektörün (0,9746) belirgin biçimde gerisinde. En iyi re-ranker'lı yapılandırma (0,9946) ise yalnız vektörden 0,0199 önde, eşiğin altında.
- BM25 ve RRF kodu kalıyor; `Rag:Mode=Hybrid` ile açılabilir. Zayıf bir embedding modeline (örneğin 1b) dönülürse Hybrid'in güvenlik ağı değeri yeniden ölçülmeli (1.5'te 1b ile Hit@5'i 4/10'dan 9/10'a çıkarmıştı).
- Answer eval sonucu (kural 4): PROGRESS ve [docs/eval/baseline.md](../eval/baseline.md).

## Sınırlar ve riskler

- **10 soru küçük bir örnek** ve soruları dokümanları bilen kişi yazdı. Bu sonuçlar bir duman testi; asıl karar verici 3.2'deki eval.
- **BM25 indeksi bellek içinde** ve `ChunkIndex.SyncAsync` çalışınca kuruluyor. Senkronizasyon yapılmamış bir süreçte arama yalnız vektörle yapılıyor. Web uygulaması açılışta senkronizasyonu çalıştıracak (1.6).
- **5 harfle kök kırpma**, 4 harflik kökü olan kelimeleri ayırabiliyor ("kart" ile "kartı" → "karti"). Vektör tarafı bu durumu telafi ediyor; normalizer eval'e göre ayarlanabilir.
- **İndeks bellekte tutuluyor;** büyük corpus'lar için uygun değil. Bu proje yaklaşık 100 chunk ile çalışıyor; binlerce dokümana çıkılırsa Qdrant'ın native BM25/sparse desteğine geçilmeli.

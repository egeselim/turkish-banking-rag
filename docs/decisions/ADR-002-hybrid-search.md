# ADR-002: Hybrid search, Qdrant vektörü + kendi BM25'imiz + RRF

- **Durum:** Kabul edildi
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

## Sınırlar ve riskler

- **10 soru küçük bir örnek** ve soruları dokümanları bilen kişi yazdı. Bu sonuçlar bir duman testi; asıl karar verici 3.2'deki eval.
- **BM25 indeksi bellek içinde** ve `ChunkIndex.SyncAsync` çalışınca kuruluyor. Senkronizasyon yapılmamış bir süreçte arama yalnız vektörle yapılıyor. Web uygulaması açılışta senkronizasyonu çalıştıracak (1.6).
- **5 harfle kök kırpma**, 4 harflik kökü olan kelimeleri ayırabiliyor ("kart" ile "kartı" → "karti"). Vektör tarafı bu durumu telafi ediyor; normalizer eval'e göre ayarlanabilir.
- **İndeks bellekte tutuluyor;** büyük corpus'lar için uygun değil. Bu proje yaklaşık 100 chunk ile çalışıyor; binlerce dokümana çıkılırsa Qdrant'ın native BM25/sparse desteğine geçilmeli.

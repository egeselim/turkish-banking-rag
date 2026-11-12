# Retrieval eval (full)

- Tarih (UTC): 2026-11-12 20:28
- Embedding: nvidia/nemotron-3.5-embed-8b (4096 boyut), 101 chunk
- Soru: 100 (92 cevaplı, 8 cevapsız; cevapsızlar isabet metriklerine girmez)
- Embedding önbelleği: 707 isabet, 0 yeni (NIM'den)
- Karşılaştırma: baseline-retrieval-3.2.json (2026-11-10), ortak 100 soru üzerinden; parantez içi fark

> Gecikme, `SearchAsync` çağrısının duvar saati süresidir. Embedding ve rerank skorları önbellekten geldiğinde NIM süresi dahil değildir; yalnızca yerel iş ölçülür.
> Hit@20: doğru kaynağın ilk 20 aday içinde olması, yani bir re-ranker'ın en fazla ulaşabileceği oran.

| Yöntem | Hit@1 | Hit@5 | Hit@20 | MRR | p50 ms | p95 ms |
|---|---|---|---|---|---|---|
| Vektör | 88/92 (=) | 92/92 (=) | 92/92 | 0.97 (=) | 3.7 (+0.8) | 5.0 (+1.1) |
| BM25 | 71/92 (=) | 84/92 (=) | 92/92 | 0.83 (=) | 0.1 (=) | 0.1 (=) |
| Hybrid | 79/92 (=) | 90/92 (=) | 92/92 | 0.91 (=) | 3.6 (+0.7) | 5.1 (+1.1) |
| Vektör + rerank (nemotron-3.5-rerank-8b) | 91/92 | 92/92 | - | 0.99 | 4.8 | 7.0 |
| Hybrid + rerank (nemotron-3.5-rerank-8b) | 91/92 | 92/92 | - | 0.99 | 5.0 | 7.1 |
| Vektör + rerank (llama-nemotron-rerank-vl-1b-v2) | 87/92 | 92/92 | - | 0.97 | 5.0 | 6.9 |
| Hybrid + rerank (llama-nemotron-rerank-vl-1b-v2) | 87/92 | 92/92 | - | 0.97 | 5.0 | 6.9 |

## Dokümana göre MRR

| Grup | Soru | Vektör | BM25 | Hybrid | Vektör + rerank (nemotron-3.5-rerank-8b) | Hybrid + rerank (nemotron-3.5-rerank-8b) | Vektör + rerank (llama-nemotron-rerank-vl-1b-v2) | Hybrid + rerank (llama-nemotron-rerank-vl-1b-v2) |
|---|---|---|---|---|---|---|---|---|
| kredi-karti-sozlesmesi | 26 | 0.98 | 0.87 | 0.94 | 0.98 | 0.98 | 0.96 | 0.96 |
| kvkk-6698 | 24 | 0.95 | 0.65 | 0.82 | 1.00 | 1.00 | 0.94 | 0.94 |
| sss | 18 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| ucret-tarifesi | 24 | 0.97 | 0.83 | 0.90 | 1.00 | 1.00 | 0.98 | 0.98 |

## Etikete göre MRR

| Grup | Soru | Vektör | BM25 | Hybrid | Vektör + rerank (nemotron-3.5-rerank-8b) | Hybrid + rerank (nemotron-3.5-rerank-8b) | Vektör + rerank (llama-nemotron-rerank-vl-1b-v2) | Hybrid + rerank (llama-nemotron-rerank-vl-1b-v2) |
|---|---|---|---|---|---|---|---|---|
| cross_doc | 4 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| definition | 5 | 1.00 | 0.57 | 0.87 | 1.00 | 1.00 | 1.00 | 1.00 |
| fact | 71 | 0.97 | 0.84 | 0.89 | 0.99 | 0.99 | 0.96 | 0.96 |
| no_diacritics | 3 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| paraphrase | 33 | 0.96 | 0.75 | 0.85 | 1.00 | 1.00 | 0.94 | 0.94 |
| procedure | 17 | 1.00 | 0.86 | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |

## Sırası değişen sorular (Hybrid, ilk 5)

Yok.

## Hybrid'in ilk 5'te bulamadığı sorular

- q-034: Yeni gelen kartımı aktif etmezsem ne olur? → sss:Soru 8; sss:Soru 1; kredi-karti-sozlesmesi:Madde 7
- q-078: Banka bilgilerimi başka bir şirkete aktarabilir mi? → kvkk-6698:Madde 9; sss:Soru 14; sss:Soru 13

## Cevabı olmayan sorular: Hybrid'in ilk 3 sonucu

- q-093: Selim Bank'ın genel müdürü kim? → ucret-tarifesi:Madde 1; sss:-; sss:Soru 13
- q-094: Konut kredisi faiz oranınız kaç? → kredi-karti-sozlesmesi:Madde 12; kredi-karti-sozlesmesi:Madde 14; kredi-karti-sozlesmesi:Madde 13
- q-095: Vadeli mevduata yıllık ne kadar faiz veriyorsunuz? → kredi-karti-sozlesmesi:Madde 12; kredi-karti-sozlesmesi:Madde 15; sss:Soru 4
- q-096: En yakın şubeniz nerede, kaçta kapanıyor? → kredi-karti-sozlesmesi:Madde 16; sss:Soru 11; sss:Soru 1
- q-097: Bugün dolar kuru kaç? → sss:Soru 12; ucret-tarifesi:-; ucret-tarifesi:Madde 9
- q-098: Kredi kartı başvurusu için hangi belgeleri getirmem gerekiyor? → kredi-karti-sozlesmesi:Madde 1; kredi-karti-sozlesmesi:Madde 3; kredi-karti-sozlesmesi:Madde 17
- q-099: Müşteri İletişim Merkezi'nin telefon numarası nedir? → sss:Soru 13; sss:Soru 15; kredi-karti-sozlesmesi:Madde 7
- q-100: Selim Bank'ın hisseleri borsada işlem görüyor mu? → ucret-tarifesi:Madde 9; ucret-tarifesi:Madde 1; sss:Soru 12

## Soru bazında sıra (- = aranan derinlikte yok)

| Soru | Vektör | BM25 | Hybrid | Vektör + rerank (nemotron-3.5-rerank-8b) | Hybrid + rerank (nemotron-3.5-rerank-8b) | Vektör + rerank (llama-nemotron-rerank-vl-1b-v2) | Hybrid + rerank (llama-nemotron-rerank-vl-1b-v2) |
|---|---|---|---|---|---|---|---|
| q-001: Başka bankaya mobilden para göndermek kaç lira? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-002: Gece yarısı acil para yollamam lazım, anında gitmesi için ne kullanmalıyım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-003: Kartım çalındı, ben fark edene kadar yapılan harcamalardan ne kadar sorumluyum? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-004: Ekstremde tanımadığım bir harcama var, itiraz için ne kadar vaktim var? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-005: Platinum kartın senelik ücreti ne kadar? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-006: Asgariyi ödeyemezsem başıma ne gelir? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-007: Başka bankanın bankamatiğinden para çekersem kesinti olur mu? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-008: Bilgilerim yurt dışındaki bir şirkete gönderilebilir mi? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-009: Açık rıza ne anlama geliyor? | 1 | 3 | 1 | 1 | 1 | 1 | 1 |
| q-010: Tatile gittim, kartım dışarıda çalışmıyor | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-011: Tarifede yazmayan bir hizmet için benden ücret alınabilir mi? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-012: Banka ücretlere zam yapacaksa beni ne kadar önceden haberdar etmek zorunda? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-013: Ücret artışını kabul etmezsem ürünümü ücretsiz kapatabilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-014: Hesabımda para yoksa işlem ücreti nereden kesilir? | 1 | 6 | 2 | 1 | 1 | 1 | 1 |
| q-015: Şubeden EFT yaparsam ne kadar öderim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-016: ATM'den başka bankaya EFT ücreti ne kadar? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-017: Diğer bankadaki kendi hesabıma mobilden para atarsam ücret öder miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-018: eft saat kaca kadar yapilabiliyor | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-019: FAST ile en fazla ne kadar para gönderebilirim? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-020: Selim Bank içindeki başka bir hesaba havale yapmak ücretli mi? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-021: Yurt dışına para gönderirsem komisyon ne olur? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-022: Yurt dışından bana gelen paraya kesinti yapılıyor mu? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-023: Gold kartın yıllık ücreti ne kadar? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-024: Kartımla yılda ne kadar harcarsam aidat ödemem? | 1 | 3 | 1 | 1 | 1 | 1 | 1 |
| q-025: Kredi kartıyla nakit avans çekersem ne kadar masraf çıkar? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-026: Kartım kırıldı, yenisini istersem ücret var mı? | 1 | 6 | 3 | 1 | 1 | 2 | 2 |
| q-027: Banka kartı için yıllık ücret alıyor musunuz? | 3 | 1 | 2 | 1 | 1 | 1 | 1 |
| q-028: Uzun süredir kullanmadığım hesabımdan ücret kesilir mi? | 1 | 16 | 4 | 1 | 1 | 1 | 1 |
| q-029: hesap ozetini postayla istersem ucret var mi | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-030: SMS bildirim paketi aylık kaç lira? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-031: Kredi kartı sözleşmesinin tarafları kimler? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-032: Son ödeme tarihi hesap kesiminden kaç gün sonra? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-033: Banka benim haberim olmadan limitimi yükseltebilir mi? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-034: Yeni gelen kartımı aktif etmezsem ne olur? | 1 | 14 | 6 | 1 | 1 | 1 | 1 |
| q-035: Kartımı nasıl aktifleştiririm? | 1 | 5 | 1 | 1 | 1 | 1 | 1 |
| q-036: Kart şifremi bir yere not alabilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-037: Yeni kartımla internetten alışveriş yapabilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-038: Kartım çalındı, kartı nasıl kapatırım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-039: Kalıcı olarak kapattığım kartı tekrar açtırabilir miyim? | 2 | 3 | 2 | 2 | 2 | 2 | 2 |
| q-040: Kayıp bildirimi yaptıktan sonra kartımla yapılan harcamalardan sorumlu muyum? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-041: Harcama itirazım kaç günde sonuçlanır? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-042: Aynı alışveriş kartımdan iki kere çekildi, paramı ne zaman geri alırım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-043: Limitim 80.000 TL, asgari ödeme oranım ne olur? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-044: Borcumun tamamını ödemezsem hangi faiz uygulanır? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-045: Kredi kartı faiz oranlarının bir üst sınırı var mı? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-046: Asgari ödemeyi üç ay üst üste yapmazsam kartıma ne olur? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-047: Limitimin ne kadarını nakit avans olarak çekebilirim? | 1 | 1 | 1 | 1 | 1 | 2 | 2 |
| q-048: Aidat ekstreme yansıdıktan iki ay sonra kartı kapatırsam ücreti geri alır mıyım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-049: Kredi kartı sözleşmemi nasıl feshedebilirim? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-050: Kartımı iptal ettirme bildirimim ne zaman geçerli olur? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-051: Banka hangi durumlarda kartımı kapatıp sözleşmeyi feshedebilir? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-052: Banka sözleşmeyi feshederse bana nasıl haber verir? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-053: Kartımı kaybettim, ilk ne yapmalıyım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-054: Çalıntı yüzünden yenilenen kart için para ödeyecek miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-055: Kartımı bulana kadar sadece dondurmak istiyorum, mümkün mü? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-056: Geçici kapattığım kartın numarası değişir mi? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-057: Limit artışını uygulamanın hangi menüsünden isterim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-058: Yıllık kart aidatımı geri alabilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-059: Yurt dışı kullanımını uygulamada nereden açarım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-060: Tanımadığım bir harcama görünce ilk olarak ne yapmalıyım? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-061: Mükerrer çekim için nereye başvururum? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-062: asgari odemeyi geciktirirsem gecikme faizi yuzde kac | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-063: Asgari ödeme tutarım nasıl hesaplanıyor? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-064: EFT ile FAST arasındaki fark nedir? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-065: Cuma akşamı gönderdiğim EFT ne zaman hesaba geçer? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-066: Günlük para çekme limitimi yükseltebilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-067: Bankadan aradıklarını söyleyen biri kart şifremi istedi, vermeli miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-068: Bankanın hakkımda tuttuğu bilgileri nasıl öğrenirim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-069: Sohbette yazdığım kimlik numaram yapay zekâya gidiyor mu? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-070: Asistan benim onayım olmadan kartımı bloke edebilir mi? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-071: Kişisel Verilerin Korunması Kanunu'nun amacı nedir? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-072: KVKK kimlere uygulanır? | 1 | 10 | 3 | 1 | 1 | 1 | 1 |
| q-073: Veri sorumlusu kime denir? | 1 | 6 | 3 | 1 | 1 | 1 | 1 |
| q-074: Kişisel veriler işlenirken hangi ilkelere uyulmalı? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-075: Açık rızam olmadan bilgilerim işlenebilir mi? | 1 | 7 | 2 | 1 | 1 | 1 | 1 |
| q-076: Sağlık bilgilerim özel nitelikli veri sayılır mı? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-077: Verilerimin silinmesini isteyebilir miyim? | 1 | 2 | 1 | 1 | 1 | 1 | 1 |
| q-078: Banka bilgilerimi başka bir şirkete aktarabilir mi? | 3 | 16 | 6 | 1 | 1 | 4 | 4 |
| q-079: Yeterlilik kararı yoksa verilerim yurt dışına hangi güvencelerle aktarılabilir? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-080: Veri sorumlusu bilgilerimi toplarken bana neyi bildirmek zorunda? | 1 | 3 | 2 | 1 | 1 | 1 | 1 |
| q-081: KVKK'ya göre kişisel verilerimle ilgili hangi haklarım var? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-082: Verilerim çalınırsa şirket ne yapmak zorunda? | 1 | 1 | 1 | 1 | 1 | 4 | 4 |
| q-083: Veri sorumlusuna yaptığım başvuru ne kadar sürede cevaplanmalı? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-084: Başvurum reddedilirse Kurula ne zamana kadar şikâyet edebilirim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-085: Bankaya başvurmadan doğrudan Kurula şikâyette bulunabilir miyim? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-086: Kurul şikâyetime iki ay içinde cevap vermezse ne olur? | 2 | 1 | 2 | 1 | 1 | 1 | 1 |
| q-087: Veri Sorumluları Siciline kimler kaydolmak zorunda? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-088: Kişisel verileri silmemek suç mu? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-089: Aydınlatma yükümlülüğüne uymayan şirkete ne ceza verilir? | 1 | 5 | 2 | 1 | 1 | 1 | 1 |
| q-090: Kişisel Verileri Koruma Kurumu'nun merkezi nerede? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-091: Kişisel Verileri Koruma Kurulu kaç üyeden oluşuyor? | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| q-092: Evde ailemle ilgili tuttuğum notlar KVKK kapsamında mı? | 1 | 3 | 1 | 1 | 1 | 1 | 1 |

# Retrieval eval (full)

- Tarih (UTC): 2026-11-10 21:41
- Embedding: nvidia/nemotron-3.5-embed-8b (4096 boyut), 101 chunk
- Soru: 100 (92 cevaplı, 8 cevapsız; cevapsızlar isabet metriklerine girmez)
- Embedding önbelleği: 303 isabet, 0 yeni (NIM'den)
- Karşılaştırma: yok

> Gecikme, `SearchAsync` çağrısının duvar saati süresidir. Embedding önbellekten geldiğinde NIM süresi dahil değildir; yalnızca yerel arama (Qdrant + BM25) ölçülür.

| Yöntem | Hit@1 | Hit@5 | MRR | p50 ms | p95 ms |
|---|---|---|---|---|---|
| Vektör | 88/92 | 92/92 | 0.97 | 2.9 | 3.9 |
| BM25 | 71/92 | 84/92 | 0.83 | 0.1 | 0.2 |
| Hybrid | 79/92 | 90/92 | 0.91 | 2.9 | 4.0 |

## Hybrid: dokümana göre

| Grup | Soru | Hit@1 | Hit@5 | MRR |
|---|---|---|---|---|
| kredi-karti-sozlesmesi | 26 | 24 | 25 | 0.94 |
| kvkk-6698 | 24 | 17 | 23 | 0.82 |
| sss | 18 | 18 | 18 | 1.00 |
| ucret-tarifesi | 24 | 20 | 24 | 0.90 |

## Hybrid: etikete göre

| Grup | Soru | Hit@1 | Hit@5 | MRR |
|---|---|---|---|---|
| cross_doc | 4 | 4 | 4 | 1.00 |
| definition | 5 | 4 | 5 | 0.87 |
| fact | 71 | 59 | 69 | 0.89 |
| no_diacritics | 3 | 3 | 3 | 1.00 |
| paraphrase | 33 | 26 | 31 | 0.85 |
| procedure | 17 | 17 | 17 | 1.00 |

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

## Soru bazında sıra (- = ilk 5'te yok)

| Soru | Vektör | BM25 | Hybrid |
|---|---|---|---|
| q-001: Başka bankaya mobilden para göndermek kaç lira? | 1 | 1 | 1 |
| q-002: Gece yarısı acil para yollamam lazım, anında gitmesi için ne kullanmalıyım? | 1 | 1 | 1 |
| q-003: Kartım çalındı, ben fark edene kadar yapılan harcamalardan ne kadar sorumluyum? | 1 | 1 | 1 |
| q-004: Ekstremde tanımadığım bir harcama var, itiraz için ne kadar vaktim var? | 1 | 1 | 1 |
| q-005: Platinum kartın senelik ücreti ne kadar? | 1 | 1 | 1 |
| q-006: Asgariyi ödeyemezsem başıma ne gelir? | 1 | 2 | 1 |
| q-007: Başka bankanın bankamatiğinden para çekersem kesinti olur mu? | 1 | 1 | 1 |
| q-008: Bilgilerim yurt dışındaki bir şirkete gönderilebilir mi? | 1 | 2 | 1 |
| q-009: Açık rıza ne anlama geliyor? | 1 | 3 | 1 |
| q-010: Tatile gittim, kartım dışarıda çalışmıyor | 1 | 1 | 1 |
| q-011: Tarifede yazmayan bir hizmet için benden ücret alınabilir mi? | 1 | 1 | 1 |
| q-012: Banka ücretlere zam yapacaksa beni ne kadar önceden haberdar etmek zorunda? | 1 | 1 | 1 |
| q-013: Ücret artışını kabul etmezsem ürünümü ücretsiz kapatabilir miyim? | 1 | 1 | 1 |
| q-014: Hesabımda para yoksa işlem ücreti nereden kesilir? | 1 | - | 2 |
| q-015: Şubeden EFT yaparsam ne kadar öderim? | 1 | 1 | 1 |
| q-016: ATM'den başka bankaya EFT ücreti ne kadar? | 1 | 1 | 1 |
| q-017: Diğer bankadaki kendi hesabıma mobilden para atarsam ücret öder miyim? | 1 | 1 | 1 |
| q-018: eft saat kaca kadar yapilabiliyor | 1 | 1 | 1 |
| q-019: FAST ile en fazla ne kadar para gönderebilirim? | 1 | 2 | 1 |
| q-020: Selim Bank içindeki başka bir hesaba havale yapmak ücretli mi? | 1 | 1 | 1 |
| q-021: Yurt dışına para gönderirsem komisyon ne olur? | 1 | 1 | 1 |
| q-022: Yurt dışından bana gelen paraya kesinti yapılıyor mu? | 1 | 1 | 1 |
| q-023: Gold kartın yıllık ücreti ne kadar? | 1 | 1 | 1 |
| q-024: Kartımla yılda ne kadar harcarsam aidat ödemem? | 1 | 3 | 1 |
| q-025: Kredi kartıyla nakit avans çekersem ne kadar masraf çıkar? | 1 | 1 | 1 |
| q-026: Kartım kırıldı, yenisini istersem ücret var mı? | 1 | - | 3 |
| q-027: Banka kartı için yıllık ücret alıyor musunuz? | 3 | 1 | 2 |
| q-028: Uzun süredir kullanmadığım hesabımdan ücret kesilir mi? | 1 | - | 4 |
| q-029: hesap ozetini postayla istersem ucret var mi | 1 | 1 | 1 |
| q-030: SMS bildirim paketi aylık kaç lira? | 1 | 1 | 1 |
| q-031: Kredi kartı sözleşmesinin tarafları kimler? | 1 | 1 | 1 |
| q-032: Son ödeme tarihi hesap kesiminden kaç gün sonra? | 1 | 1 | 1 |
| q-033: Banka benim haberim olmadan limitimi yükseltebilir mi? | 1 | 1 | 1 |
| q-034: Yeni gelen kartımı aktif etmezsem ne olur? | 1 | - | - |
| q-035: Kartımı nasıl aktifleştiririm? | 1 | 5 | 1 |
| q-036: Kart şifremi bir yere not alabilir miyim? | 1 | 1 | 1 |
| q-037: Yeni kartımla internetten alışveriş yapabilir miyim? | 1 | 1 | 1 |
| q-038: Kartım çalındı, kartı nasıl kapatırım? | 1 | 1 | 1 |
| q-039: Kalıcı olarak kapattığım kartı tekrar açtırabilir miyim? | 2 | 3 | 2 |
| q-040: Kayıp bildirimi yaptıktan sonra kartımla yapılan harcamalardan sorumlu muyum? | 1 | 1 | 1 |
| q-041: Harcama itirazım kaç günde sonuçlanır? | 1 | 1 | 1 |
| q-042: Aynı alışveriş kartımdan iki kere çekildi, paramı ne zaman geri alırım? | 1 | 1 | 1 |
| q-043: Limitim 80.000 TL, asgari ödeme oranım ne olur? | 1 | 1 | 1 |
| q-044: Borcumun tamamını ödemezsem hangi faiz uygulanır? | 1 | 1 | 1 |
| q-045: Kredi kartı faiz oranlarının bir üst sınırı var mı? | 1 | 1 | 1 |
| q-046: Asgari ödemeyi üç ay üst üste yapmazsam kartıma ne olur? | 1 | 1 | 1 |
| q-047: Limitimin ne kadarını nakit avans olarak çekebilirim? | 1 | 1 | 1 |
| q-048: Aidat ekstreme yansıdıktan iki ay sonra kartı kapatırsam ücreti geri alır mıyım? | 1 | 1 | 1 |
| q-049: Kredi kartı sözleşmemi nasıl feshedebilirim? | 1 | 2 | 1 |
| q-050: Kartımı iptal ettirme bildirimim ne zaman geçerli olur? | 1 | 1 | 1 |
| q-051: Banka hangi durumlarda kartımı kapatıp sözleşmeyi feshedebilir? | 1 | 1 | 1 |
| q-052: Banka sözleşmeyi feshederse bana nasıl haber verir? | 1 | 1 | 1 |
| q-053: Kartımı kaybettim, ilk ne yapmalıyım? | 1 | 1 | 1 |
| q-054: Çalıntı yüzünden yenilenen kart için para ödeyecek miyim? | 1 | 1 | 1 |
| q-055: Kartımı bulana kadar sadece dondurmak istiyorum, mümkün mü? | 1 | 1 | 1 |
| q-056: Geçici kapattığım kartın numarası değişir mi? | 1 | 1 | 1 |
| q-057: Limit artışını uygulamanın hangi menüsünden isterim? | 1 | 1 | 1 |
| q-058: Yıllık kart aidatımı geri alabilir miyim? | 1 | 1 | 1 |
| q-059: Yurt dışı kullanımını uygulamada nereden açarım? | 1 | 1 | 1 |
| q-060: Tanımadığım bir harcama görünce ilk olarak ne yapmalıyım? | 1 | 1 | 1 |
| q-061: Mükerrer çekim için nereye başvururum? | 1 | 1 | 1 |
| q-062: asgari odemeyi geciktirirsem gecikme faizi yuzde kac | 1 | 1 | 1 |
| q-063: Asgari ödeme tutarım nasıl hesaplanıyor? | 1 | 1 | 1 |
| q-064: EFT ile FAST arasındaki fark nedir? | 1 | 1 | 1 |
| q-065: Cuma akşamı gönderdiğim EFT ne zaman hesaba geçer? | 1 | 1 | 1 |
| q-066: Günlük para çekme limitimi yükseltebilir miyim? | 1 | 1 | 1 |
| q-067: Bankadan aradıklarını söyleyen biri kart şifremi istedi, vermeli miyim? | 1 | 1 | 1 |
| q-068: Bankanın hakkımda tuttuğu bilgileri nasıl öğrenirim? | 1 | 1 | 1 |
| q-069: Sohbette yazdığım kimlik numaram yapay zekâya gidiyor mu? | 1 | 1 | 1 |
| q-070: Asistan benim onayım olmadan kartımı bloke edebilir mi? | 1 | 1 | 1 |
| q-071: Kişisel Verilerin Korunması Kanunu'nun amacı nedir? | 1 | 2 | 1 |
| q-072: KVKK kimlere uygulanır? | 1 | - | 3 |
| q-073: Veri sorumlusu kime denir? | 1 | - | 3 |
| q-074: Kişisel veriler işlenirken hangi ilkelere uyulmalı? | 1 | 1 | 1 |
| q-075: Açık rızam olmadan bilgilerim işlenebilir mi? | 1 | - | 2 |
| q-076: Sağlık bilgilerim özel nitelikli veri sayılır mı? | 1 | 1 | 1 |
| q-077: Verilerimin silinmesini isteyebilir miyim? | 1 | 2 | 1 |
| q-078: Banka bilgilerimi başka bir şirkete aktarabilir mi? | 3 | - | - |
| q-079: Yeterlilik kararı yoksa verilerim yurt dışına hangi güvencelerle aktarılabilir? | 1 | 1 | 1 |
| q-080: Veri sorumlusu bilgilerimi toplarken bana neyi bildirmek zorunda? | 1 | 3 | 2 |
| q-081: KVKK'ya göre kişisel verilerimle ilgili hangi haklarım var? | 1 | 1 | 1 |
| q-082: Verilerim çalınırsa şirket ne yapmak zorunda? | 1 | 1 | 1 |
| q-083: Veri sorumlusuna yaptığım başvuru ne kadar sürede cevaplanmalı? | 1 | 1 | 1 |
| q-084: Başvurum reddedilirse Kurula ne zamana kadar şikâyet edebilirim? | 1 | 1 | 1 |
| q-085: Bankaya başvurmadan doğrudan Kurula şikâyette bulunabilir miyim? | 1 | 1 | 1 |
| q-086: Kurul şikâyetime iki ay içinde cevap vermezse ne olur? | 2 | 1 | 2 |
| q-087: Veri Sorumluları Siciline kimler kaydolmak zorunda? | 1 | 1 | 1 |
| q-088: Kişisel verileri silmemek suç mu? | 1 | 1 | 1 |
| q-089: Aydınlatma yükümlülüğüne uymayan şirkete ne ceza verilir? | 1 | 5 | 2 |
| q-090: Kişisel Verileri Koruma Kurumu'nun merkezi nerede? | 1 | 1 | 1 |
| q-091: Kişisel Verileri Koruma Kurulu kaç üyeden oluşuyor? | 1 | 1 | 1 |
| q-092: Evde ailemle ilgili tuttuğum notlar KVKK kapsamında mı? | 1 | 3 | 1 |

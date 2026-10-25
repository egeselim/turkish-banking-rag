# Kaynak dokümanlar

Asistanın RAG ile cevap verdiği dokümanlar. Gerçek bir bankaya ait hiçbir içerik yoktur.

| Dosya | Kaynak | Lisans / hukuki durum |
|---|---|---|
| `ucret-tarifesi.pdf` | Kurgusal; `src/ucret-tarifesi.md`'den üretildi | Proje için yazıldı |
| `kredi-karti-sozlesmesi.pdf` | Kurgusal; `src/kredi-karti-sozlesmesi.md`'den üretildi | Proje için yazıldı, hukuki bağlayıcılığı yok |
| `sss.pdf` | Kurgusal; `src/sss.md`'den üretildi | Proje için yazıldı |
| `kvkk-6698.pdf` | 6698 sayılı Kişisel Verilerin Korunması Kanunu, [mevzuat.gov.tr](https://www.mevzuat.gov.tr/MevzuatMetin/1.5.6698.pdf), 24.10.2026'da indirildi | Kanun metni; FSEK m.31 gereği serbestçe çoğaltılabilir |

Kurgusal dokümanlardaki rakamlar (ücretler, oranlar, süreler) uydurmadır ve eval sorularının cevap anahtarıdır. Bir rakamı değiştirirsen `eval/questions.jsonl`'daki ilgili soruyu da güncelle.

## PDF'leri yeniden üretmek

```
dotnet run --project tools/SourceDocs.Generator -- docs/sources/src docs/sources
```

Üretici, PdfPig ile DejaVu Sans fontunu gömer (`tools/SourceDocs.Generator/Fonts`, Bitstream Vera/DejaVu lisansı). Başlıklar kalın ve büyük puntoyla basılır. Böylece okuyucu, belgenin yapısını gerçek banka PDF'lerinde olduğu gibi yalnızca yerleşimden çıkarmak zorunda kalır.

Desteklenen Markdown alt kümesi: `#` başlık, `##` bölüm, `###` madde/soru başlığı (`Madde N – …`, `Soru N – …`), `- ` madde imi, boş satırla ayrılmış paragraflar.

## Bilinen okuma sınırları

- KVKK PDF'inin sonundaki kadro cetveli ve değişiklik listesi tablo olarak okunamıyor; düz satırlar hâlinde, etiketsiz chunk'lara giriyor.
- Tekrarlanan sayfa üst ve alt bilgileri ayıklanmıyor (bu dokümanlarda bulunmuyor). Yalnızca rakamdan oluşan satırlar (sayfa numarası, dipnot işareti) atlanıyor.

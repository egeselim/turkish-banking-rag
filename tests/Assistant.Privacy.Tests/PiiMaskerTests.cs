using Assistant.Privacy.Tests.TestData;
using Bogus;

namespace Assistant.Privacy.Tests;

public class PiiMaskerTests
{
    private readonly PiiMasker _masker = new();

    [Fact]
    public void Masks_every_pii_type_in_a_turkish_sentence()
    {
        var f = new Faker("tr") { Random = FakePii.Seeded() };
        for (int i = 0; i < 100; i++)
        {
            string tckn = FakePii.Tckn(f.Random);
            string card = FakePii.CardNumber(f.Random);
            string iban = FakePii.TrIban(f.Random);
            string phone = "0" + FakePii.Mobile(f.Random);
            string email = FakePii.Email(f);
            string text = $"TC kimlik numaram {tckn}, kartım {card}, IBAN'ım {iban}, telefonum {phone}, e-postam {email}. Lütfen kontrol eder misiniz?";

            string masked = _masker.Mask(text);

            Assert.Equal(
                "TC kimlik numaram [TCKN], kartım [KART_NO], IBAN'ım [IBAN], telefonum [TELEFON], e-postam [EPOSTA]. Lütfen kontrol eder misiniz?",
                masked);
        }
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('-')]
    public void Masks_card_numbers_printed_in_groups(char separator)
    {
        var r = FakePii.Seeded();
        string card16 = FakePii.Group(FakePii.CardNumber(r), 4, separator);
        string card19 = FakePii.Group(FakePii.CardNumber(r, "9792", 19), 4, separator);

        Assert.Equal("Kart: [KART_NO]", _masker.Mask($"Kart: {card16}"));
        Assert.Equal("Kart: [KART_NO]", _masker.Mask($"Kart: {card19}"));
    }

    [Fact]
    public void Masks_iban_written_in_groups_and_lower_case_as_one_iban()
    {
        string iban = FakePii.TrIban(FakePii.Seeded());

        Assert.Equal("[IBAN]", _masker.Mask(FakePii.Group(iban, 4, ' ')));
        Assert.Equal("[IBAN]", _masker.Mask(iban.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("+90 532 123 45 67")]
    [InlineData("+905321234567")]
    [InlineData("0090 532 123 45 67")]
    [InlineData("0532 123 45 67")]
    [InlineData("05321234567")]
    [InlineData("0 (532) 123-45-67")]
    [InlineData("532 123 45 67")]
    [InlineData("5321234567")]
    public void Masks_turkish_mobile_formats(string phone) =>
        Assert.Equal("Ara: [TELEFON].", _masker.Mask($"Ara: {phone}."));

    [Fact]
    public void Does_not_mask_numbers_that_fail_their_checksum()
    {
        var r = FakePii.Seeded();
        for (int i = 0; i < 100; i++)
        {
            string badTckn = FakePii.WithWrongLastDigit(FakePii.Tckn(r));
            string badCard = FakePii.WithWrongLastDigit(FakePii.CardNumber(r));
            string badIban = FakePii.WithWrongLastDigit(FakePii.TrIban(r));
            string text = $"Sipariş {badTckn}, referans {badCard}, hesap {badIban}";

            Assert.Equal(text, _masker.Mask(text));
        }
    }

    [Theory]
    [InlineData("Tutar 1.250,75 TL olarak tahsil edildi.")]
    [InlineData("İşlem tarihi 24.10.2026 saat 14:35.")]
    [InlineData("Sipariş no SP-2026-000123, müşteri no 123456.")]
    [InlineData("Faiz oranı %4,25; vade 36 ay.")]
    [InlineData("Kartın son 4 hanesi 4821.")]
    [InlineData("Bu metinde hiç kişisel veri yok.")]
    public void Leaves_ordinary_banking_text_untouched(string text) =>
        Assert.Same(text, _masker.Mask(text));

    [Fact]
    public void Masks_adjacent_identifiers_separately()
    {
        var r = FakePii.Seeded();
        string text = $"{FakePii.Tckn(r)} {FakePii.CardNumber(r)} 0{FakePii.Mobile(r)}";

        Assert.Equal("[TCKN] [KART_NO] [TELEFON]", _masker.Mask(text));
    }

    [Fact]
    public void Masking_is_idempotent()
    {
        var f = new Faker("tr") { Random = FakePii.Seeded() };
        string once = _masker.Mask($"{FakePii.Tckn(f.Random)} / {FakePii.Email(f)} / {FakePii.TrIban(f.Random)}");

        Assert.Equal(once, _masker.Mask(once));
    }

    [Fact]
    public void Detect_reports_types_and_positions()
    {
        string tckn = FakePii.Tckn(FakePii.Seeded());
        string text = $"No: {tckn}";

        var match = Assert.Single(PiiDetector.Detect(text));

        Assert.Equal(new PiiMatch(PiiType.NationalId, 4, 11), match);
    }
}

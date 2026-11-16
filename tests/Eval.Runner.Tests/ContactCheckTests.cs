namespace Eval.Runner.Tests;

public sealed class ContactCheckTests
{
    private static readonly IReadOnlySet<string> NoKnownPhones = new HashSet<string>();

    // Answers seen in the 3.3/3.4 evals and common Turkish formats.
    [Theory]
    [InlineData("Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 444 0 444 numaralı telefonla ulaşabilirsiniz.", "4440444")]
    [InlineData("Lütfen 444 0 888'i arayın.", "4440888")]
    [InlineData("Şubemiz: 0212 555 12 34", "2125551234")]
    [InlineData("Bize +90 850 123 45 67 numarasından ulaşın.", "8501234567")]
    [InlineData("Cep: 0532 123 45 67", "5321234567")]
    public void Phone_numbers_not_in_the_sources_are_reported(string answer, string digits)
    {
        Assert.Equal([digits], ContactCheck.InventedPhones(answer, NoKnownPhones));
    }

    // Banking answers are full of numbers that must not be mistaken for phone numbers.
    [Theory]
    [InlineData("Selim Platinum Kart yıllık üyelik ücreti 1.650 TL'dir. [1]")]
    [InlineData("FAST ile tutarı 50.000 TL'yi aşmayan transferler 7 gün 24 saat yapılabilir.")]
    [InlineData("EFT iş günlerinde 08:30 ile 17:00 arasında yapılır.")]
    [InlineData("1. Papatya Market – 176,11 TL (23.10.2026, kart son 4 hane: 9280)")]
    [InlineData("Kişisel verileri silmeyenler 5237 sayılı Kanunun 138 inci maddesine göre cezalandırılır.")]
    [InlineData("Bakiyeniz 41.373,70 TL.")]
    public void Amounts_dates_and_times_are_not_phone_numbers(string answer)
    {
        Assert.Empty(ContactCheck.InventedPhones(answer, NoKnownPhones));
    }

    [Fact]
    public void A_number_written_in_the_sources_is_not_invented_in_any_format()
    {
        var known = ContactCheck.PhonesIn(["Müşteri İletişim Merkezi: 0850 123 45 67"]);

        Assert.Empty(ContactCheck.InventedPhones("+90 850 123 45 67 numarasını arayın.", known));
        Assert.Empty(ContactCheck.InventedPhones("850 123 45 67", known));
    }

    [Fact]
    public void Old_reports_without_source_phones_count_every_number_as_invented()
    {
        var q = new AnswerResult(
            "q-1", "soru", null, ["unanswerable"], "Bulamadım. 444 0 444'ü arayın.", [], false, true,
            new Dictionary<string, double?>(), new Dictionary<string, string>(), 1, TokenUsage.None, TokenUsage.None, null);
        var report = new AnswerReport(AnswerReport.CurrentSchemaVersion, DateTimeOffset.UnixEpoch, "t", "c", "j", "VectorOnly", null, [q]);

        Assert.Equal(["q-1"], AnswerReport.FromJson(report.ToJson()).Summarize().InventedPhoneIds);
    }
}

using Assistant.Privacy.Tests.TestData;
using Assistant.Privacy.Validation;

namespace Assistant.Privacy.Tests;

public class ChecksumsTests
{
    private const int Samples = 200;

    [Fact]
    public void Generated_tckns_are_valid_and_a_changed_last_digit_is_not()
    {
        var r = FakePii.Seeded();
        for (int i = 0; i < Samples; i++)
        {
            string tckn = FakePii.Tckn(r);
            Assert.True(Checksums.IsValidTckn(tckn), tckn);
            Assert.False(Checksums.IsValidTckn(FakePii.WithWrongLastDigit(tckn)), tckn);
        }
    }

    [Theory]
    [InlineData("12345678901")] // checksum fails
    [InlineData("11111111111")] // d10 ok, d11 fails
    [InlineData("01234567890")] // leading zero
    [InlineData("1234567890")]  // 10 digits
    [InlineData("1234567890a")]
    public void Invalid_tckns_are_rejected(string value) =>
        Assert.False(Checksums.IsValidTckn(value));

    [Fact]
    public void Generated_card_numbers_pass_luhn_and_a_changed_last_digit_does_not()
    {
        var r = FakePii.Seeded();
        for (int i = 0; i < Samples; i++)
        {
            string card = FakePii.CardNumber(r, prefix: r.ArrayElement(["4", "51", "9792"]), length: r.ArrayElement([15, 16, 19]));
            Assert.True(Checksums.IsValidLuhn(card), card);
            Assert.False(Checksums.IsValidLuhn(FakePii.WithWrongLastDigit(card)), card);
        }
    }

    [Theory]
    [InlineData("4111111111111111", true)] // well-known test number
    [InlineData("4111111111111112", false)]
    [InlineData("", false)]
    [InlineData("4111 1111 1111 1111", false)] // validator expects digits only
    public void Luhn_known_values(string value, bool expected) =>
        Assert.Equal(expected, Checksums.IsValidLuhn(value));

    [Fact]
    public void Generated_tr_ibans_are_valid_and_a_changed_last_digit_is_not()
    {
        var r = FakePii.Seeded();
        for (int i = 0; i < Samples; i++)
        {
            string iban = FakePii.TrIban(r);
            Assert.Equal(26, iban.Length);
            Assert.True(Checksums.IsValidIban(iban), iban);
            Assert.False(Checksums.IsValidIban(FakePii.WithWrongLastDigit(iban)), iban);
        }
    }

    [Theory]
    [InlineData("TR330006100519786457841326", true)] // ISO 13616 documentation example
    [InlineData("TR330006100519786457841327", false)]
    [InlineData("tr330006100519786457841326", false)] // validator expects upper case
    [InlineData("TR33", false)]
    public void Iban_known_values(string value, bool expected) =>
        Assert.Equal(expected, Checksums.IsValidIban(value));
}

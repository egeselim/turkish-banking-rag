using System.Globalization;

namespace Eval.Runner;

/// <summary>Command line of the eval runner. Messages are Turkish: they are shown to the person running it.</summary>
internal sealed record RunnerOptions
{
    public const string Usage = """
        Kullanım: dotnet run --project eval/Eval.Runner -- [seçenekler]
          --subset smoke|full     Soru alt kümesi (varsayılan: smoke)
          --mode retrieval|answer Ölçüm modu (varsayılan: retrieval). answer NIM anahtarı ister, --offline ile çalışmaz
          --offline               NIM'i hiç çağırma; embedding'ler yalnızca eval/cache'ten gelir
          --qdrant <url>          Var olan Qdrant (gRPC, örn. http://localhost:6334); verilmezse geçici container açılır
          --out <klasör>          Rapor klasörü (varsayılan: eval/reports/local)
          --compare <json>        Karşılaştırılacak rapor (varsayılan: eval/reports/baseline-<mod>.json)
          --no-compare            Karşılaştırma yapma
          --baseline              Raporu eval/reports/baseline-<mod>.{json,md} olarak yaz (yalnızca --subset full)
          --limit <n>             Yalnızca ilk n soru (deneme için; --baseline ile kullanılamaz)
          --fail-under-hit5 <0-1> retrieval: Hybrid Hit@5 bu oranın altındaysa çıkış kodu 1
          --price-in <usd>        answer: milyon giriş token'ı başına varsayımsal fiyat
          --price-out <usd>       answer: milyon çıkış token'ı başına varsayımsal fiyat
        """;

    public string Subset { get; init; } = "smoke";

    public string Mode { get; init; } = "retrieval";

    public bool Offline { get; init; }

    public Uri? Qdrant { get; init; }

    public string? OutDir { get; init; }

    public string? Compare { get; init; }

    public bool NoCompare { get; init; }

    public bool WriteBaseline { get; init; }

    public double? FailUnderHit5 { get; init; }

    public int? Limit { get; init; }

    public double? PriceIn { get; init; }

    public double? PriceOut { get; init; }

    public string BaselineName => $"baseline-{Mode}";

    public static RunnerOptions Parse(IReadOnlyList<string> args)
    {
        var options = new RunnerOptions();
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            options = arg switch
            {
                "--subset" => options with { Subset = OneOf(Value(args, ref i, arg), arg, "smoke", "full") },
                "--mode" => options with { Mode = Value(args, ref i, arg) },
                "--offline" => options with { Offline = true },
                "--qdrant" => options with { Qdrant = ParseUri(Value(args, ref i, arg)) },
                "--out" => options with { OutDir = Value(args, ref i, arg) },
                "--compare" => options with { Compare = Value(args, ref i, arg) },
                "--no-compare" => options with { NoCompare = true },
                "--baseline" => options with { WriteBaseline = true },
                "--fail-under-hit5" => options with { FailUnderHit5 = ParseRatio(Value(args, ref i, arg)) },
                "--limit" => options with { Limit = ParseCount(Value(args, ref i, arg)) },
                "--price-in" => options with { PriceIn = ParsePrice(Value(args, ref i, arg), arg) },
                "--price-out" => options with { PriceOut = ParsePrice(Value(args, ref i, arg), arg) },
                _ => throw new ArgumentException($"Bilinmeyen seçenek: {arg}"),
            };
        }

        if (options.Mode is not ("retrieval" or "answer"))
        {
            throw new ArgumentException($"Bilinmeyen mod: {options.Mode} (retrieval ya da answer)");
        }

        if (options.Mode == "answer" && options.Offline)
        {
            throw new ArgumentException("answer modu modeli çağırır; --offline ile çalışmaz.");
        }

        if (options.Mode == "answer" && options.FailUnderHit5 is not null)
        {
            throw new ArgumentException("--fail-under-hit5 yalnızca retrieval modunda geçerli.");
        }

        if (options.Mode == "retrieval" && (options.PriceIn is not null || options.PriceOut is not null))
        {
            throw new ArgumentException("--price-in/--price-out yalnızca answer modunda geçerli.");
        }

        if ((options.PriceIn is null) != (options.PriceOut is null))
        {
            throw new ArgumentException("--price-in ve --price-out birlikte verilmeli.");
        }

        if (options.WriteBaseline && options.Subset != "full")
        {
            throw new ArgumentException("--baseline yalnızca --subset full ile yazılır; smoke çalıştırmaları bu baseline'la karşılaştırılır.");
        }

        if (options.WriteBaseline && options.Limit is not null)
        {
            throw new ArgumentException("--baseline ile --limit kullanılamaz.");
        }

        if (options.Compare is not null && options.NoCompare)
        {
            throw new ArgumentException("--compare ve --no-compare birlikte kullanılamaz.");
        }

        return options;
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name) =>
        ++i < args.Count && !args[i].StartsWith("--", StringComparison.Ordinal)
            ? args[i]
            : throw new ArgumentException($"{name} bir değer bekliyor.");

    private static string OneOf(string value, string name, params string[] allowed) =>
        allowed.Contains(value) ? value : throw new ArgumentException($"{name} şunlardan biri olmalı: {string.Join(", ", allowed)}");

    private static Uri ParseUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new ArgumentException($"--qdrant geçerli bir http(s) adresi olmalı: {value}");

    private static int ParseCount(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : throw new ArgumentException($"--limit pozitif bir tam sayı olmalı: {value}");

    private static double ParsePrice(string value, string name) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double price) && price >= 0
            ? price
            : throw new ArgumentException($"{name} sıfır ya da pozitif bir sayı olmalı (örn. 0.5): {value}");

    private static double ParseRatio(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio is >= 0 and <= 1
            ? ratio
            : throw new ArgumentException($"--fail-under-hit5 0 ile 1 arasında olmalı (örn. 0.8): {value}");
}

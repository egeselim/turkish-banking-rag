using System.Globalization;

namespace Eval.Runner;

/// <summary>Command line of the eval runner. Messages are Turkish: they are shown to the person running it.</summary>
internal sealed record RunnerOptions
{
    public const string Usage = """
        Kullanım: dotnet run --project eval/Eval.Runner -- [seçenekler]
          --subset smoke|full     Soru alt kümesi (varsayılan: smoke)
          --mode retrieval        Ölçüm modu (answer modu 3.3'te gelecek)
          --offline               NIM'i hiç çağırma; embedding'ler yalnızca eval/cache'ten gelir
          --qdrant <url>          Var olan Qdrant (gRPC, örn. http://localhost:6334); verilmezse geçici container açılır
          --out <klasör>          Rapor klasörü (varsayılan: eval/reports/local)
          --compare <json>        Karşılaştırılacak rapor (varsayılan: eval/reports/baseline-retrieval.json)
          --no-compare            Karşılaştırma yapma
          --baseline              Raporu eval/reports/baseline-retrieval.{json,md} olarak yaz (yalnızca --subset full)
          --fail-under-hit5 <0-1> Hybrid Hit@5 bu oranın altındaysa çıkış kodu 1
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
                _ => throw new ArgumentException($"Bilinmeyen seçenek: {arg}"),
            };
        }

        if (options.Mode == "answer")
        {
            throw new ArgumentException("answer modu henüz yok (adım 3.3). Şimdilik yalnızca --mode retrieval.");
        }

        if (options.Mode != "retrieval")
        {
            throw new ArgumentException($"Bilinmeyen mod: {options.Mode}");
        }

        if (options.WriteBaseline && options.Subset != "full")
        {
            throw new ArgumentException("--baseline yalnızca --subset full ile yazılır; smoke çalıştırmaları bu baseline'la karşılaştırılır.");
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

    private static double ParseRatio(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio is >= 0 and <= 1
            ? ratio
            : throw new ArgumentException($"--fail-under-hit5 0 ile 1 arasında olmalı (örn. 0.8): {value}");
}

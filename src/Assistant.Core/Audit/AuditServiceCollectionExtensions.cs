using Assistant.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Assistant.Core.Audit;

public static class AuditServiceCollectionExtensions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// Registers the audit trail. <c>Audit:PseudonymKey</c> comes from user-secrets (see README);
    /// <c>Audit:ConnectionString</c> defaults to an <c>audit.db</c> file in the working directory (git-ignored).
    /// </summary>
    public static IServiceCollection AddAuditLog(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        services.TryAddSingleton<IPiiMasker, PiiMasker>();
        services.TryAddSingleton<IAuditLog>(_ => new SqliteAuditLog(section["ConnectionString"] ?? "Data Source=audit.db"));
        services.TryAddSingleton(sp => new AuditPseudonymizer(section["PseudonymKey"], Logger(sp)));
        services.TryAddSingleton(sp => new AuditTrail(
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<IPiiMasker>(),
            sp.GetRequiredService<AuditPseudonymizer>(),
            logger: Logger(sp)));
        return services;
    }

    private static ILogger? Logger(IServiceProvider sp) => sp.GetService<ILoggerFactory>()?.CreateLogger<AuditTrail>();
}

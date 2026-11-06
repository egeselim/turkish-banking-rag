using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SelimBank.Api.Data;

namespace SelimBank.Api.Customers;

/// <summary>The authenticated demo customer of the current request (scoped). Set only by <see cref="CustomerHeader"/>.</summary>
public sealed class CurrentCustomer
{
    private int? id;

    public int Id
    {
        // Fails loudly if an endpoint forgot RequireCustomer(), instead of silently querying customer 0.
        get => id ?? throw new InvalidOperationException("RequireCustomer() is missing on this endpoint.");
        internal set => id = value;
    }
}

/// <summary>
/// Demo authentication: the customer id comes from the <c>X-Customer-Id</c> header, which the assistant fills
/// from its server-side session. Missing, malformed or unknown ids get 401.
/// </summary>
public static class CustomerHeader
{
    public const string Name = "X-Customer-Id";

    public static TBuilder RequireCustomer<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!int.TryParse(http.Request.Headers[Name], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                return Unauthorized();
            }

            var db = http.RequestServices.GetRequiredService<SelimBankDbContext>();
            if (!await db.Customers.AnyAsync(c => c.Id == id, http.RequestAborted))
            {
                return Unauthorized();
            }

            // Handler parameters are bound before filters run; they receive this same scoped instance.
            http.RequestServices.GetRequiredService<CurrentCustomer>().Id = id;
            return await next(context);
        });

    private static ProblemHttpResult Unauthorized() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Müşteri kimliği gerekli",
        detail: $"{Name} başlığı eksik ya da geçersiz.");
}

using System.Text.Json.Serialization;
using SelimBank.Api.Customers;
using SelimBank.Api.Data;
using SelimBank.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSelimBankDatabase();
builder.Services.AddScoped<CurrentCustomer>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await app.InitializeSelimBankDatabaseAsync();

app.MapDefaultEndpoints();
app.MapBankEndpoints();

await app.RunAsync();

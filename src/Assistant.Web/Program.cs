using Assistant.Core.Chat;
using Assistant.Core.Models;
using Assistant.Core.Retrieval;
using Assistant.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Qdrant connection comes from the AppHost ("qdrant" resource).
builder.AddQdrantClient("qdrant");

builder.Services
    .AddNimModels(builder.Configuration)
    .AddRag()
    .AddSourceIngestion(builder.Configuration)
    .AddBankAssistant();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

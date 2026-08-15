using System.Diagnostics;
using Alexandreia;
using Alexandreia.Components;

// Import da Excel: comando a sé, non avvia l'applicativo.
if (args.Contains("--import")) return Cli.RunImport(args);
if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Cli.Usage); return 0; }

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = Db.DefaultPath();
builder.Services.AddSingleton(new Db(dbPath));

// Solo loopback: l'applicativo non è raggiungibile dalla rete. Porta 0 = la sceglie il sistema,
// così due avvii o un'altra app sulla stessa porta non fanno fallire il lancio.
builder.WebHost.UseUrls("http://127.0.0.1:0");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = app.Urls.First();
    Console.WriteLine($"Alexandreia: {url}   (dati in {dbPath})");
    if (Environment.GetEnvironmentVariable("ALEXANDREIA_NO_BROWSER") == "1") return;
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
});

app.Run();
return 0;

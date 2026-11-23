using System.Diagnostics;
using Airport.Core.Simulation;
using Airport.Data;
using Airport.Desktop;
using Airport.Desktop.Components;

// AirportOps.exe: hosts the shared Blazor UI as a local web app with SQL persistence,
// then opens it in the default browser.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAirportDatabase(builder.Configuration);
builder.Services.AddSingleton<AirportEngine>();
builder.Services.AddHostedService<EngineHostedService>();

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Airport.UI.Routes).Assembly);

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
    Console.WriteLine();
    Console.WriteLine("  Airport Operations Center is running.");
    Console.WriteLine($"  Open {url} in your browser. Close this window to stop.");
    Console.WriteLine();

    if (app.Configuration.GetValue("OpenBrowser", true) && !app.Environment.IsDevelopment())
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser available: the console message is enough */ }
    }
});

app.Run();

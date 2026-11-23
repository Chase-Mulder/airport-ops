using Airport.Core.Persistence;
using Airport.Core.Simulation;
using Airport.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

// GitHub Pages demo: everything runs in the browser tab. Nothing is saved.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<IAirportStore, InMemoryAirportStore>();
builder.Services.AddSingleton<AirportEngine>();

var host = builder.Build();

var engine = host.Services.GetRequiredService<AirportEngine>();
await engine.InitializeAsync();
engine.Start();

await host.RunAsync();

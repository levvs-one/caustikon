using Caustikon.Site;
using Caustikon.Site.Localization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddSingleton<Locale>();

WebAssemblyHost host = builder.Build();

// The language a visitor chose last time, else the browser's; set before the first render so nothing flashes in the wrong one.
Locale locale = host.Services.GetRequiredService<Locale>();
IJSRuntime js = host.Services.GetRequiredService<IJSRuntime>();
locale.Code = await js.InvokeAsync<string>("caustikonLang.get");

await host.RunAsync();

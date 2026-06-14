using CnuFacebookBlazor.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("BackendAPI", client =>
{
    string baseUrl = builder.Configuration["BackendApi:BaseUrl"] ?? "http://localhost:5099/";
    if (!baseUrl.EndsWith('/')) baseUrl += '/';
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient("CnuConnectAPI", client =>
{
    string baseUrl = builder.Configuration["CnuConnectApi:BaseUrl"] ?? "https://localhost:7112/";
    if (!baseUrl.EndsWith('/')) baseUrl += '/';
    client.BaseAddress = new Uri(baseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

// Proxy the Facebook OAuth callback to the backend API.
// Conveyor tunnels only this frontend port, so the RedirectUri registered
// with Facebook must point here, and we forward the code/state params onward.
app.MapGet("/api/CnuFacebook/FacebookCallback", async (HttpContext ctx, IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("CnuConnectAPI");
    var qs = ctx.Request.QueryString.Value ?? "";
    var res = await http.GetAsync("api/CnuFacebook/FacebookCallback" + qs);
    var html = await res.Content.ReadAsStringAsync();
    return Results.Content(html, "text/html");
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

using MCPWebApp.Models;
using MCPWebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MCPOptions>(builder.Configuration.GetSection("MCP"));
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddRazorPages();

builder.Services.AddSingleton<MCPClientService>();
builder.Services.AddSingleton<IMCPClientService>(sp => sp.GetRequiredService<MCPClientService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MCPClientService>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/Chat"));

app.Run();

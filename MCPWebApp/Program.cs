using MCPWebApp.Models;
using MCPWebApp.Data;
using MCPWebApp.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MCPOptions>(builder.Configuration.GetSection("MCP"));
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddScoped<IConfigStoreService, ConfigStoreService>();
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddHostedService<DatabaseInitializerHostedService>();

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

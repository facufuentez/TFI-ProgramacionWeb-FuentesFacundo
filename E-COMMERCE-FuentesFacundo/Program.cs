using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using E_COMMERCE_FuentesFacundo.Data;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Services;
using Microsoft.AspNetCore.RateLimiting;
using QuestPDF.Infrastructure;
using System.Threading.RateLimiting;

LoadDotEnv(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
LoadDotEnv(Path.Combine(AppContext.BaseDirectory, ".env"));

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    try
    {
        var keysPath = Path.Combine(builder.Environment.ContentRootPath, "..", "private", "keys");
        Directory.CreateDirectory(keysPath);

        builder.Services.AddDataProtection()
            .SetApplicationName("E-COMMERCE-FuentesFacundo")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"No se pudo configurar DataProtection persistente: {ex.Message}");
    }
}

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(GetRequiredConnectionString(builder.Configuration)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<PedidoReporteService>();
builder.Services.AddHttpClient<GeminiChatService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<GeminiChatService>();

builder.Services.AddHttpClient<MercadoPagoService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<MercadoPagoService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("chat", limiterOptions =>
    {
        limiterOptions.PermitLimit = 15;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
});

var app = builder.Build();

// Seed roles and catalog data
await SeedData.InitializeAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(app.Environment.WebRootPath, "images", "productos")),
    RequestPath = "/images/productos"
});

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static string GetRequiredConnectionString(IConfiguration configuration)
{
    var rawConnectionString = configuration.GetConnectionString("DefaultConnection");

    if (string.IsNullOrWhiteSpace(rawConnectionString))
    {
        throw new InvalidOperationException(
            "Falta la cadena de conexión 'ConnectionStrings:DefaultConnection'. " +
            "Configúrala en MonsterASP como variable de entorno 'ConnectionStrings__DefaultConnection'.");
    }

    var connectionString = rawConnectionString
        .Trim()
        .Replace("\r", string.Empty)
        .Replace("\n", string.Empty);

    if (connectionString.Length >= 2 &&
        ((connectionString.StartsWith('"') && connectionString.EndsWith('"')) ||
         (connectionString.StartsWith('\'') && connectionString.EndsWith('\''))))
    {
        connectionString = connectionString[1..^1].Trim();
    }

    try
    {
        _ = new SqlConnectionStringBuilder(connectionString);
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException)
    {
        var detail = ex.Message;
        var indexMatch = System.Text.RegularExpressions.Regex.Match(detail, @"index (\d+)");
        if (indexMatch.Success &&
            int.TryParse(indexMatch.Groups[1].Value, out var errorIndex))
        {
            detail += DescribeConnectionStringPosition(connectionString, errorIndex);
        }

        throw new InvalidOperationException(
            "La cadena de conexión 'ConnectionStrings:DefaultConnection' no es válida. " +
            "Revisa comillas, saltos de línea y caracteres especiales (por ejemplo ';' dentro de Password). " +
            $"Detalle: {detail}", ex);
    }

    return connectionString;
}

static string DescribeConnectionStringPosition(string connectionString, int index)
{
    if (index < 0 || index >= connectionString.Length)
    {
        return string.Empty;
    }

    var character = connectionString[index];
    var start = connectionString.LastIndexOf(';', Math.Max(0, index - 1)) + 1;
    var end = connectionString.IndexOf(';', index);
    if (end < 0)
    {
        end = connectionString.Length;
    }

    var segment = connectionString[start..end];
    var equalsIndex = segment.IndexOf('=');
    var key = equalsIndex > 0 ? segment[..equalsIndex].Trim() : string.Empty;

    return key.Length > 0
        ? $" Carácter en la posición {index}: '{character}' (clave '{key}')."
        : $" Carácter en la posición {index}: '{character}'.";
}

static void LoadDotEnv(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();

        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        var environmentKey = key switch
        {
            "GEMINI_API_KEY" => "Gemini__ApiKey",
            "GEMINI_MODEL" => "Gemini__Model",
            "GEMINI_BASE_URL" => "Gemini__BaseUrl",
            "GEMINI_MAX_OUTPUT_TOKENS" => "Gemini__MaxOutputTokens",
            "GEMINI_TEMPERATURE" => "Gemini__Temperature",
            "GEMINI_TIMEOUT_SECONDS" => "Gemini__TimeoutSeconds",
            "GEMINI_MAX_INPUT_CHARS" => "Gemini__MaxInputChars",
            "GEMINI_MAX_HISTORY_MESSAGES" => "Gemini__MaxHistoryMessages",
            "GEMINI_MAX_CATALOG_PRODUCTS" => "Gemini__MaxCatalogProducts",
            "MP_ACCESS_TOKEN" => "MercadoPago__AccessToken",
            "MP_WEBHOOK_SECRET" => "MercadoPago__WebhookSecret",
            "MP_BASE_URL" => "MercadoPago__BaseUrl",
            "MP_ENVIRONMENT" => "MercadoPago__Environment",
            _ => key
        };

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(environmentKey)))
        {
            Environment.SetEnvironmentVariable(environmentKey, value, EnvironmentVariableTarget.Process);
        }
    }
}

public partial class Program { }

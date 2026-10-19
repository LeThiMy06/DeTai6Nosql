using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OnlineSurvey.Infrastructure;
using OnlineSurvey.Models;
using OnlineSurvey.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<RedisSettings>(
    builder.Configuration.GetSection("Redis"));
builder.Services.Configure<AuthDatabaseSettings>(
    builder.Configuration.GetSection("AuthDatabase"));

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<MongoDbSettings>>().Value;

    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(serviceProvider =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = serviceProvider.GetRequiredService<IMongoClient>();

    return client.GetDatabase(settings.DatabaseName);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    var settings = serviceProvider
        .GetRequiredService<IOptions<RedisSettings>>().Value;
    var options = ConfigurationOptions.Parse(settings.ConnectionString);

    // Allow the website to start even when Redis is temporarily unavailable.
    // SurveyService already falls back to MongoDB and retries Redis on later calls.
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 1;
    options.ConnectTimeout = 1500;
    options.SyncTimeout = 1500;

    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddSingleton<ISurveyRepository, MongoSurveyRepository>();
builder.Services.AddSingleton<SurveyService>();
builder.Services.AddSingleton<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddSingleton<IAdminRepository, MongoAdminRepository>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "OnlineSurvey.Admin";
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

var adminRepository = app.Services.GetRequiredService<IAdminRepository>();
await adminRepository.InitializeAsync();

await adminRepository.ResetAdminPasswordAsync("admin", "Admin@12345");
app.Logger.LogInformation("Đã đồng bộ tài khoản quản trị mặc định 'admin' với mật khẩu 'Admin@12345'.");

if (args.Contains("--seed-large", StringComparer.OrdinalIgnoreCase))
{
    var mongoDb = app.Services.GetRequiredService<IMongoDatabase>();
    var surveyService = app.Services.GetRequiredService<SurveyService>();
    var (surveyCount, responseCount) = await LargeDataSeeder.SeedAllLargeDataAsync(mongoDb, surveyService, "6aab4ec590062125b3635833");
    Console.WriteLine($"[SEED SUCCESS] Đã nạp thành công {surveyCount} khảo sát lớn (mỗi khảo sát đúng 100 câu hỏi, tổng 500 câu hỏi) và {responseCount} phản hồi mẫu vào MongoDB!");
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();

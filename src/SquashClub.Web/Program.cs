using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SquashClub.Web.Api;
using SquashClub.Web.Data;
using SquashClub.Web.Domain;
using SquashClub.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ClubDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Club") ??
    "Host=localhost;Database=squashclub;Username=postgres;Password=postgres"));
builder.Services.AddIdentity<Member, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true;
    options.Password.RequiredLength = 10;
}).AddEntityFrameworkStores<ClubDbContext>().AddDefaultTokenProviders();
builder.Services.AddAuthorization(options => options.AddPolicy("Administrator",
    policy => policy.RequireRole("Administrator")));
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ClubOptions());
builder.Services.AddScoped<ICreditService, CreditService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ICourtAvailabilityService, CourtAvailabilityService>();
builder.Services.AddScoped<INotificationService, DatabaseNotificationService>();
builder.Services.AddScoped<ICancellationNotificationService, CancellationNotificationService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddSingleton<IPaymentGateway, DevelopmentPaymentGateway>();
builder.Services.AddSingleton<IEmailNotificationSender, DevelopmentEmailNotificationSender>();
builder.Services.AddSingleton<ILightingProvider, MockLightingProvider>();
builder.Services.AddScoped<ICourtLightingService, CourtLightingService>();
builder.Services.AddScoped<CompetitionService>();
builder.Services.AddHostedService<LightingReconciliationWorker>();
builder.Services.AddHostedService<NotificationDeliveryWorker>();
builder.Services.AddHostedService<SplitExpirationWorker>();
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("external", limiter =>
{
    limiter.PermitLimit = 60;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapGet("/api/account/csrf", (HttpContext context,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
    Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
app.MapGet("/api/public/products", async (ClubDbContext db, CancellationToken ct) => Results.Ok(new
{
    memberships = await db.MembershipProducts.Where(x => x.Active).ToListAsync(ct),
    credits = await db.CreditPackages.Where(x => x.Active).ToListAsync(ct)
}));
app.MapAccountEndpoints();
app.MapApplicationEndpoints();

app.MapPost("/api/webhooks/credits/debit", async (HttpContext http, DebitRequest request,
    ICreditService service, ClubDbContext db) =>
{
    if (!ExternalAuthentication.Authenticate(http, builder.Configuration,
        request.ExternalTransactionId)) return Results.Unauthorized();
    if (!TryConvertCredits(request.Credits, out var units))
        return Results.BadRequest(new { success = false, error = "INVALID_AMOUNT" });
    try
    {
        var duplicate = await db.CreditTransactions.AnyAsync(x =>
            x.ExternalReference == request.ExternalTransactionId, http.RequestAborted);
        var transaction = await service.ExternalDebitAsync(request.MemberId, units,
            request.ExternalTransactionId, request.Description, http.RequestAborted);
        return Results.Ok(new { success = true, duplicate, transactionId = transaction.Id,
            creditsDeducted = -transaction.Units / 100m,
            remainingCredits = transaction.ResultingBalanceUnits / 100m });
    }
    catch (ClubRuleException ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Code });
    }
}).RequireRateLimiting("external");

app.MapGet("/api/members/{id:guid}/credits", async (Guid id, HttpContext http, ClubDbContext db) =>
{
    if (!ExternalAuthentication.Authenticate(http, builder.Configuration, id.ToString()))
        return Results.Unauthorized();
    var member = await db.Users.FindAsync([id], http.RequestAborted);
    if (member is null) return Results.NotFound();
    var now = DateTime.UtcNow;
    return Results.Ok(new { memberId = id, credits = member.CreditBalanceUnits / 100m,
        membershipActive = await db.Memberships.AnyAsync(x => x.MemberId == id && !x.Cancelled &&
            x.StartsAtUtc <= now && x.EndsAtUtc > now, http.RequestAborted) });
}).RequireRateLimiting("external");

app.MapPost("/api/webhooks/payments/stripe", async (HttpContext http, PaymentWebhookDto request,
    PaymentService payments) =>
{
    if (!ExternalAuthentication.Authenticate(http, builder.Configuration,
        request.ProviderEventId, "Payments:HmacSecret")) return Results.Unauthorized();
    await payments.ConfirmAsync(request.PaymentId, request.ProviderEventId, http.RequestAborted);
    return Results.Ok();
}).RequireRateLimiting("external");

app.MapRazorPages();
await DevelopmentSeed.SeedAsync(app.Services, app.Environment);
await ClubSettingsLoader.LoadAsync(app.Services);
app.Run();

static bool TryConvertCredits(decimal credits, out int units)
{
    units = 0;
    if (credits <= 0 || decimal.Round(credits, 2) != credits) return false;
    try { units = checked((int)(credits * 100)); return units > 0; }
    catch (OverflowException) { return false; }
}

public static class ExternalAuthentication
{
    public static bool Authenticate(HttpContext http, IConfiguration configuration, string payload,
        string secretKey = "ExternalApi:HmacSecret")
    {
        var key = configuration["ExternalApi:Key"];
        var secret = configuration[secretKey];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret) ||
            http.Request.Headers["X-Api-Key"] != key ||
            !long.TryParse(http.Request.Headers["X-Timestamp"], out var timestamp) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp) > 300) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));
        var supplied = http.Request.Headers["X-Signature"].ToString().ToUpperInvariant();
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied));
    }
}

public sealed record DebitRequest(Guid MemberId, decimal Credits, string ExternalTransactionId,
    string Description);
public sealed record PaymentWebhookDto(Guid PaymentId, string ProviderEventId);
public partial class Program { }

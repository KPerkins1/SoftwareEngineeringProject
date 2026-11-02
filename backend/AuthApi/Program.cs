using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthApi.Models;
using AuthApi.Services;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

// This is the main entry point for the backend API.
// It sets up the app, loads important config values, and wires in CORS, rate limiting,
// and all of the routes that the front-end app will call.
var builder = WebApplication.CreateBuilder(args);
var signingKeyText = builder.Configuration["CTF_SESSION_KEY"];
var adminEmail = builder.Configuration["CTF_BOOTSTRAP_ADMIN_EMAIL"];
var adminPassword = builder.Configuration["CTF_BOOTSTRAP_ADMIN_PASSWORD"];
if (string.IsNullOrWhiteSpace(signingKeyText) || Encoding.UTF8.GetByteCount(signingKeyText) < 32)
{
    throw new InvalidOperationException("CTF_SESSION_KEY must be configured with at least 32 bytes.");
}
if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12)
{
    throw new InvalidOperationException("Set CTF_BOOTSTRAP_ADMIN_EMAIL and a CTF_BOOTSTRAP_ADMIN_PASSWORD of at least 12 characters.");
}

var signingKey = Encoding.UTF8.GetBytes(signingKeyText);
var isProduction = builder.Environment.IsProduction();
var webOrigin = builder.Configuration["CTF_WEB_ORIGIN"] ?? "http://localhost:4200";

// These services are shared app-wide, so they can be reused across requests.
// JsonFileStore handles the local JSON data files, and SessionTokens creates/validates login cookies.
builder.Services.AddSingleton<JsonFileStore>();
builder.Services.AddSingleton(new SessionTokens(signingKey));
builder.Services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

// CORS lets the Angular app talk to this API while still keeping a secure origin policy.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// This rate limiter makes brute-force login attempts harder by limiting each IP to a few tries per minute.
builder.Services.AddRateLimiter(options => options.AddPolicy("login", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

var app = builder.Build();
var store = app.Services.GetRequiredService<JsonFileStore>();

// On app startup, we make sure the bootstrap admin account exists before anyone tries to log in.
await store.EnsureBootstrapAdminAsync(adminEmail, adminPassword);

if (isProduction)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors();
app.UseRateLimiter();

// This middleware runs for every request and tries to recover the signed session cookie.
// If the cookie is valid, the user's identity is attached to the request so later endpoints can check roles.
app.Use(async (context, next) =>
{
    var token = context.Request.Cookies["ctf_session"];
    var principal = token is null ? null : app.Services.GetRequiredService<SessionTokens>().Validate(token);
    if (principal is not null)
    {
        context.User = principal;
    }

    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// This is the actual login endpoint.
// It validates input, checks the hashed password against the stored user, and then sends back a secure session cookie.
app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context) =>
{
    var email = request.Email.Trim();
    if (email.Length > 254 || request.Password.Length is 0 or > 1024)
    {
        return Results.BadRequest(new { message = "Invalid email or password." });
    }

    var account = await store.FindUserAsync(email);
    if (account is null || !PasswordSecurity.VerifyPassword(account, request.Password))
    {
        return Results.Unauthorized();
    }

    var tokens = context.RequestServices.GetRequiredService<SessionTokens>();
    context.Response.Cookies.Append("ctf_session", tokens.Create(account), CookieOptions(isProduction, httpOnly: true));
    context.Response.Cookies.Append("ctf_csrf", tokens.CreateCsrfToken(), CookieOptions(isProduction, httpOnly: false));
    return Results.Ok(ToPublicUser(account));
}).RequireRateLimiting("login");

// This endpoint lets the front-end ask "who am I right now?" without re-login.
app.MapGet("/api/auth/session", (HttpContext context) =>
{
    var account = context.User.Identity?.IsAuthenticated == true ? ToPublicUserFromPrincipal(context.User) : null;
    return account is null ? Results.Unauthorized() : Results.Ok(account);
});

// Logout is simple but protected: the browser must send the matching CSRF token before the server deletes the cookies.
app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    if (!HasValidCsrf(context)) return Results.BadRequest(new { message = "Invalid CSRF token." });
    context.Response.Cookies.Delete("ctf_session", CookieOptions(isProduction, httpOnly: true));
    context.Response.Cookies.Delete("ctf_csrf", CookieOptions(isProduction, httpOnly: false));
    return Results.NoContent();
});

// Only an admin can create other instructor accounts.
app.MapPost("/api/admin/instructors", async (CreateInstructorRequest request, HttpContext context) =>
{
    if (!HasValidCsrf(context)) return Results.BadRequest(new { message = "Invalid CSRF token." });
    if (!IsRole(context.User, "Admin")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!IsValidEmail(request.Email) || request.Password.Length < 12 || request.Password.Length > 1024)
    {
        return Results.BadRequest(new { message = "Enter a valid email and a password of at least 12 characters." });
    }

    try
    {
        var instructor = await store.CreateInstructorAsync(request.Email, request.Password);
        return Results.Created("/api/admin/instructors", ToPublicUser(instructor));
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { message = exception.Message });
    }
});

app.MapGet("/api/ctfs", async (HttpContext context) =>
{
    if (!IsRole(context.User, "Admin", "Instructor")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    return Results.Ok(await store.GetCtfsAsync());
});

app.MapPost("/api/ctfs", async (CtfInput request, HttpContext context) =>
{
    if (!HasValidCsrf(context)) return Results.BadRequest(new { message = "Invalid CSRF token." });
    if (!IsRole(context.User, "Admin", "Instructor")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var validation = ValidateCtf(request);
    if (validation is not null) return Results.BadRequest(new { message = validation });

    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var ctf = CreateCtf(request, userId);
    await store.CreateCtfAsync(ctf);
    return Results.Created($"/api/ctfs/{ctf.Id}", ctf);
});

app.MapPost("/api/ctfs/import", async (CtfImportRequest request, HttpContext context) =>
{
    if (!HasValidCsrf(context)) return Results.BadRequest(new { message = "Invalid CSRF token." });
    if (!IsRole(context.User, "Admin", "Instructor")) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (request.Ctfs is null || request.Ctfs.Count is < 1 or > 50)
    {
        return Results.BadRequest(new { message = "Import between 1 and 50 CTFs per file." });
    }

    foreach (var ctf in request.Ctfs)
    {
        var validation = ValidateCtf(ctf);
        if (validation is not null) return Results.BadRequest(new { message = validation });
    }

    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    var created = new List<CtfRecord>(request.Ctfs.Count);
    foreach (var input in request.Ctfs)
    {
        var ctf = CreateCtf(input, userId);
        await store.CreateCtfAsync(ctf);
        created.Add(ctf);
    }

    return Results.Created("/api/ctfs", created);
}).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1_048_576));

app.Run();

static CookieOptions CookieOptions(bool secure, bool httpOnly) => new()
{
    HttpOnly = httpOnly,
    Secure = secure,
    SameSite = SameSiteMode.Strict,
    Path = "/",
    IsEssential = true
};

static bool HasValidCsrf(HttpContext context)
{
    var cookie = context.Request.Cookies["ctf_csrf"];
    var header = context.Request.Headers["X-CSRF-Token"].ToString();
    if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header)) return false;
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header));
}

static bool IsRole(ClaimsPrincipal user, params string[] roles) =>
    user.Identity?.IsAuthenticated == true && roles.Any(user.IsInRole);

static PublicUser ToPublicUser(UserAccount account) => new(account.Id, account.Email, account.Role);

static PublicUser ToPublicUserFromPrincipal(ClaimsPrincipal user) => new(
    user.FindFirstValue(ClaimTypes.NameIdentifier)!,
    user.FindFirstValue(ClaimTypes.Email)!,
    user.FindFirstValue(ClaimTypes.Role)!);

static bool IsValidEmail(string email) =>
    email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(email, out var address) &&
    string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);

static string? ValidateCtf(CtfInput request)
{
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 120)
        return "Title is required and must be 120 characters or fewer.";
    if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 4000)
        return "Description is required and must be 4000 characters or fewer.";
    if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 60)
        return "Category is required and must be 60 characters or fewer.";
    if (request.StartsAt is null || request.EndsAt is null || request.EndsAt <= request.StartsAt)
        return "Choose a valid start and end time; the end must be after the start.";
    return null;
}

static CtfRecord CreateCtf(CtfInput input, string userId) => new(
    Guid.NewGuid().ToString("N"),
    input.Title.Trim(),
    input.Description.Trim(),
    input.Category.Trim(),
    input.StartsAt!.Value,
    input.EndsAt!.Value,
    DateTimeOffset.UtcNow,
    userId);

public sealed class CtfImportRequest
{
    public List<CtfInput>? Ctfs { get; set; }
}

public sealed class SessionTokens(byte[] key)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Create(UserAccount account)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new SessionPayload(
            account.Id, account.Email, account.Role, DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds()), JsonOptions);
        var encoded = WebEncoders.Base64UrlEncode(payload);
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(encoded));
        return $"{encoded}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public ClaimsPrincipal? Validate(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2) return null;
        try
        {
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(signature, expected)) return null;
            var payload = JsonSerializer.Deserialize<SessionPayload>(WebEncoders.Base64UrlDecode(parts[0]), JsonOptions);
            if (payload is null || payload.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, payload.Id),
                new Claim(ClaimTypes.Email, payload.Email),
                new Claim(ClaimTypes.Role, payload.Role)
            };
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "CtfSession"));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public string CreateCsrfToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed record SessionPayload(string Id, string Email, string Role, long Expires);
}

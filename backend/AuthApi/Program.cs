using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

const string angularPolicy = "AngularDevelopmentClient";
const string cookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
const string demoPassword = "Password123!";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors(options => options.AddPolicy(angularPolicy, policy =>
    policy.WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
builder.Services
    .AddAuthentication(cookieScheme)
    .AddCookie(cookieScheme, options =>
    {
        options.Cookie.Name = "Northstar.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();
var demoUser = new DemoUser("1", "demo@example.com", "Demo User");
demoUser.PasswordHash = new PasswordHasher<DemoUser>()
    .HashPassword(demoUser, demoPassword);

app.UseCors(angularPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
    {
        return Results.BadRequest(new { message = "Email and password are required." });
    }

    var passwordMatches = string.Equals(
        request.Email.Trim(), demoUser.Email, StringComparison.OrdinalIgnoreCase)
        && new PasswordHasher<DemoUser>().VerifyHashedPassword(
            demoUser, demoUser.PasswordHash, request.Password)
            != PasswordVerificationResult.Failed;

    if (!passwordMatches)
    {
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, demoUser.Id),
        new Claim(ClaimTypes.Name, demoUser.Name),
        new Claim(ClaimTypes.Email, demoUser.Email)
    };
    var identity = new ClaimsIdentity(claims, cookieScheme);
    await context.SignInAsync(cookieScheme, new ClaimsPrincipal(identity));

    return Results.Ok(new UserResponse(demoUser.Id, demoUser.Email, demoUser.Name));
});

app.MapGet("/api/auth/me", (ClaimsPrincipal principal) =>
{
    var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    var email = principal.FindFirstValue(ClaimTypes.Email);
    var name = principal.FindFirstValue(ClaimTypes.Name);

    return id is null || email is null || name is null
        ? Results.Unauthorized()
        : Results.Ok(new UserResponse(id, email, name));
}).RequireAuthorization();

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(cookieScheme);
    return Results.Ok();
}).RequireAuthorization();

app.Run();

sealed class DemoUser(string id, string email, string name)
{
    public string Id { get; } = id;
    public string Email { get; } = email;
    public string Name { get; } = name;
    public string PasswordHash { get; set; } = string.Empty;
}

sealed record LoginRequest(string? Email, string? Password);
sealed record UserResponse(string Id, string Email, string Name);
using AuthApi.Endpoints;
using AuthApi.Models;
using AuthApi.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

// These settings let the Angular dev server make cookie-based requests to this API.
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
var demoUser = new DemoUser("1", "demo@example.com", "Demo User");
demoUser.PasswordHash = new PasswordHasher<DemoUser>()
    .HashPassword(demoUser, demoPassword);
builder.Services.AddSingleton(demoUser);
builder.Services.AddSingleton<CtfStore>();

var app = builder.Build();
app.UseCors(angularPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCtfEndpoints();

app.Run();

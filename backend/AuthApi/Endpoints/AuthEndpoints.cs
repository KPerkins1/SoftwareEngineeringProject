using System.Security.Claims;
using AuthApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace AuthApi.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        const string cookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        // Check the demo credentials and create a cookie for later requests.
        app.MapPost("/api/auth/login", async (LoginRequest request, HttpContext context, DemoUser demoUser) =>
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
    }
}
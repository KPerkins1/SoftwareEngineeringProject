namespace AuthApi.Models;

// This is the stored user record.
// It keeps the hashed password data and role so the API can check authentication and authorization.
public sealed record UserAccount(
    string Id,
    string Email,
    string Role,
    string PasswordSalt,
    string PasswordHash);

public sealed record PublicUser(string Id, string Email, string Role);

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class CreateInstructorRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

namespace AuthApi.Models;

public class DemoUser(string id, string email, string name)
{
    public string Id { get; } = id;
    public string Email { get; } = email;
    public string Name { get; } = name;
    public string PasswordHash { get; set; } = string.Empty;
}

public record LoginRequest(string? Email, string? Password);
public record UserResponse(string Id, string Email, string Name);
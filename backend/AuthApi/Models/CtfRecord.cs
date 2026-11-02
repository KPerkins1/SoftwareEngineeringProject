namespace AuthApi.Models;

// This is the incoming data the front-end sends when creating or importing a CTF.
public sealed class CtfInput
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}

public sealed record CtfRecord(
    string Id,
    string Title,
    string Description,
    string Category,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset CreatedAt,
    string CreatedBy);

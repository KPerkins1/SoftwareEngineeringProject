using System.Collections.Concurrent;

namespace AuthApi.Models;

public record CreateCtfRequest(string? Name, string? Description);
public record SubmitFlagRequest(string? Flag);
public record FlagSubmissionResponse(bool Correct, string Message, int Points);
public record PuzzleFile(string FileName, long Size, string StoredName);
public record CtfResponse(Guid Id, string Name, string Description, DateTimeOffset CreatedAt, IReadOnlyCollection<ChallengeResponse> Challenges);
public record ChallengeResponse(Guid Id, string Name, string Description, int Points, string? FileName, long? FileSize, bool IsSolved);

public class CtfEvent(Guid id, string name, string description, DateTimeOffset createdAt)
{
    public Guid Id { get; } = id;
    // These can change when an organizer edits the event.
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public ConcurrentDictionary<Guid, CtfChallenge> Challenges { get; } = new();
}

public class CtfChallenge(Guid id, string name, string description, string flag, int points, PuzzleFile? file, DateTimeOffset createdAt)
{
    public Guid Id { get; } = id;
    // Organizers can update these puzzle details after publishing.
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;
    public string Flag { get; set; } = flag;
    public int Points { get; set; } = points;
    public PuzzleFile? File { get; set; } = file;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public ConcurrentDictionary<string, byte> SolvedBy { get; } = new();
}
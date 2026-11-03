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
    public string Name { get; } = name;
    public string Description { get; } = description;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public ConcurrentDictionary<Guid, CtfChallenge> Challenges { get; } = new();
}

public class CtfChallenge(Guid id, string name, string description, string flag, int points, PuzzleFile? file, DateTimeOffset createdAt)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string Flag { get; } = flag;
    public int Points { get; } = points;
    public PuzzleFile? File { get; } = file;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public ConcurrentDictionary<string, byte> SolvedBy { get; } = new();
}
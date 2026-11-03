using System.Collections.Concurrent;
using AuthApi.Models;

namespace AuthApi.Services;

// Keeps the demo's CTF data in memory and builds the private upload folders.
public class CtfStore(IWebHostEnvironment environment)
{
    public const long MaxUploadSize = 25 * 1024 * 1024;

    private readonly ConcurrentDictionary<Guid, CtfEvent> ctfs = new();

    public IEnumerable<CtfEvent> GetAll()
    {
        return ctfs.Values;
    }

    public bool TryGetCtf(Guid id, out CtfEvent? ctf)
    {
        return ctfs.TryGetValue(id, out ctf);
    }

    public void Add(CtfEvent ctf)
    {
        ctfs[ctf.Id] = ctf;
    }

    // Remove the event from memory and clean up its whole folder of puzzle files.
    public bool DeleteCtf(Guid id)
    {
        if (!ctfs.TryRemove(id, out _))
        {
            return false;
        }

        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "uploads", id.ToString("N"));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return true;
    }

    // Return false when the puzzle ID does not belong to this event.
    public bool DeleteChallenge(CtfEvent ctf, Guid challengeId)
    {
        if (!ctf.Challenges.TryRemove(challengeId, out _))
        {
            return false;
        }

        var directory = GetChallengeDirectory(ctf.Id, challengeId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return true;
    }

    public string GetChallengeDirectory(Guid ctfId, Guid challengeId)
    {
        // Keep each puzzle's uploads in its own folder so they can be removed together.
        return Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "uploads",
            ctfId.ToString("N"),
            challengeId.ToString("N"));
    }
}
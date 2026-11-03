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

    public string GetChallengeDirectory(Guid ctfId, Guid challengeId)
    {
        return Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "uploads",
            ctfId.ToString("N"),
            challengeId.ToString("N"));
    }
}
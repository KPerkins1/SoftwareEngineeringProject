using System.Security.Claims;
using AuthApi.Models;
using AuthApi.Services;

namespace AuthApi.Endpoints;

public static class CtfEndpoints
{
    public static void MapCtfEndpoints(this WebApplication app)
    {
        app.MapGet("/api/ctfs", (ClaimsPrincipal principal, CtfStore store) => Results.Ok(store.GetAll()
            .OrderByDescending(ctf => ctf.CreatedAt)
            .Select(ctf => ToResponse(ctf, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty))))
            .RequireAuthorization();

        app.MapPost("/api/ctfs", (CreateCtfRequest request, ClaimsPrincipal principal, CtfStore store) =>
        {
            var name = request.Name?.Trim();
            var description = request.Description?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || description.Length > 1000)
            {
                return Results.BadRequest(new { message = "A name up to 100 characters is required; descriptions may be up to 1,000 characters." });
            }

            var ctf = new CtfEvent(Guid.NewGuid(), name, description, DateTimeOffset.UtcNow);
            store.Add(ctf);
            return Results.Created($"/api/ctfs/{ctf.Id}", ToResponse(ctf, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty));
        }).RequireAuthorization();

        app.MapPost("/api/ctfs/{id:guid}/challenges", async (Guid id, HttpRequest request, ClaimsPrincipal principal, CtfStore store) =>
        {
            if (!store.TryGetCtf(id, out var ctf) || ctf is null)
            {
                return Results.NotFound(new { message = "CTF not found." });
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Submit a multipart form with a file field." });
            }

            var form = await request.ReadFormAsync();
            var name = form["name"].ToString().Trim();
            var description = form["description"].ToString().Trim();
            var flag = form["flag"].ToString().Trim();
            var pointsValue = form["points"].ToString();
            var file = form.Files.GetFile("file");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100
                || string.IsNullOrWhiteSpace(description) || description.Length > 4000
                || string.IsNullOrWhiteSpace(flag) || flag.Length > 200
                || !int.TryParse(pointsValue, out var points) || points is < 1 or > 10000)
            {
                return Results.BadRequest(new { message = "Provide a puzzle name, prompt, flag, and points from 1 to 10,000." });
            }
            if (file is not null && (file.Length == 0 || file.Length > CtfStore.MaxUploadSize))
            {
                return Results.BadRequest(new { message = "Puzzle files must be non-empty and 25 MB or smaller." });
            }

            var challengeId = Guid.NewGuid();
            PuzzleFile? puzzleFile = null;
            if (file is not null)
            {
                var fileName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
                {
                    return Results.BadRequest(new { message = "The file name must be between 1 and 255 characters." });
                }

                var extension = Path.GetExtension(fileName);
                if (extension.Length > 12 || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
                {
                    extension = string.Empty;
                }

                var storedFileName = $"{Guid.NewGuid():N}{extension}";
                var directory = store.GetChallengeDirectory(id, challengeId);
                Directory.CreateDirectory(directory);
                await using (var stream = File.Create(Path.Combine(directory, storedFileName)))
                {
                    await file.CopyToAsync(stream);
                }
                puzzleFile = new PuzzleFile(fileName, file.Length, storedFileName);
            }

            var challenge = new CtfChallenge(challengeId, name, description, flag, points, puzzleFile, DateTimeOffset.UtcNow);
            ctf.Challenges[challenge.Id] = challenge;
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            return Results.Created($"/api/ctfs/{id}/challenges/{challenge.Id}", ToChallengeResponse(challenge, userId));
        }).RequireAuthorization();

        app.MapGet("/api/ctfs/{ctfId:guid}/challenges/{challengeId:guid}/file", (Guid ctfId, Guid challengeId, CtfStore store) =>
        {
            if (!store.TryGetCtf(ctfId, out var ctf)
                || ctf is null
                || !ctf.Challenges.TryGetValue(challengeId, out var challenge)
                || challenge.File is null)
            {
                return Results.NotFound();
            }

            var path = Path.Combine(store.GetChallengeDirectory(ctfId, challengeId), challenge.File.StoredName);
            return File.Exists(path)
                ? Results.File(path, "application/octet-stream", challenge.File.FileName)
                : Results.NotFound();
        }).RequireAuthorization();

        app.MapPost("/api/ctfs/{ctfId:guid}/challenges/{challengeId:guid}/submit", (Guid ctfId, Guid challengeId, SubmitFlagRequest request, ClaimsPrincipal principal, CtfStore store) =>
        {
            if (!store.TryGetCtf(ctfId, out var ctf)
                || ctf is null
                || !ctf.Challenges.TryGetValue(challengeId, out var challenge))
            {
                return Results.NotFound(new { message = "Puzzle not found." });
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var correct = string.Equals(request.Flag?.Trim(), challenge.Flag, StringComparison.Ordinal);
            if (!correct)
            {
                return Results.Ok(new FlagSubmissionResponse(false, "Incorrect flag. Try again.", 0));
            }

            var alreadySolved = !challenge.SolvedBy.TryAdd(userId, 0);
            return Results.Ok(new FlagSubmissionResponse(true, alreadySolved ? "Already solved." : "Correct flag!", alreadySolved ? 0 : challenge.Points));
        }).RequireAuthorization();
    }

    // Keep the expected flag out of every response sent to the browser.
    private static CtfResponse ToResponse(CtfEvent ctf, string userId) => new(
        ctf.Id,
        ctf.Name,
        ctf.Description,
        ctf.CreatedAt,
        ctf.Challenges.Values
            .OrderByDescending(challenge => challenge.CreatedAt)
            .Select(challenge => ToChallengeResponse(challenge, userId))
            .ToArray());

    private static ChallengeResponse ToChallengeResponse(CtfChallenge challenge, string userId) => new(
        challenge.Id,
        challenge.Name,
        challenge.Description,
        challenge.Points,
        challenge.File?.FileName,
        challenge.File?.Size,
        challenge.SolvedBy.ContainsKey(userId));
}
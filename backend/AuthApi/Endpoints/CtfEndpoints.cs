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

        // Update the event details but leave its puzzles and files alone.
        app.MapPut("/api/ctfs/{id:guid}", (Guid id, CreateCtfRequest request, ClaimsPrincipal principal, CtfStore store) =>
        {
            if (!store.TryGetCtf(id, out var ctf) || ctf is null)
            {
                return Results.NotFound(new { message = "CTF not found." });
            }

            var name = request.Name?.Trim();
            var description = request.Description?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || description.Length > 1000)
            {
                return Results.BadRequest(new { message = "A name up to 100 characters is required; descriptions may be up to 1,000 characters." });
            }

            ctf.Name = name;
            ctf.Description = description;
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            return Results.Ok(ToResponse(ctf, userId));
        }).RequireAuthorization();

        // Deleting an event also deletes its child puzzles and uploaded files.
        app.MapDelete("/api/ctfs/{id:guid}", (Guid id, CtfStore store) =>
            store.DeleteCtf(id)
                ? Results.NoContent()
                : Results.NotFound(new { message = "CTF not found." }))
            .RequireAuthorization();

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

        // Update puzzle details and optionally replace or remove its attached file.
        app.MapPut("/api/ctfs/{ctfId:guid}/challenges/{challengeId:guid}", async (Guid ctfId, Guid challengeId, HttpRequest request, ClaimsPrincipal principal, CtfStore store) =>
        {
            if (!store.TryGetCtf(ctfId, out var ctf)
                || ctf is null
                || !ctf.Challenges.TryGetValue(challengeId, out var challenge))
            {
                return Results.NotFound(new { message = "Puzzle not found." });
            }
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Submit puzzle changes as multipart form data." });
            }

            var form = await request.ReadFormAsync();
            var name = form["name"].ToString().Trim();
            var description = form["description"].ToString().Trim();
            var newFlag = form["flag"].ToString().Trim();
            var pointsValue = form["points"].ToString();
            var removeFile = string.Equals(form["removeFile"], "true", StringComparison.OrdinalIgnoreCase);
            var file = form.Files.GetFile("file");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100
                || string.IsNullOrWhiteSpace(description) || description.Length > 4000
                || newFlag.Length > 200
                || !int.TryParse(pointsValue, out var points) || points is < 1 or > 10000)
            {
                return Results.BadRequest(new { message = "Provide a puzzle name, prompt, and points from 1 to 10,000." });
            }
            if (file is not null && (file.Length == 0 || file.Length > CtfStore.MaxUploadSize))
            {
                return Results.BadRequest(new { message = "Puzzle files must be non-empty and 25 MB or smaller." });
            }

            // Keep the old file unless the organizer uploads a replacement or checks removeFile.
            PuzzleFile? replacementFile = challenge.File;
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
                var directory = store.GetChallengeDirectory(ctfId, challengeId);
                Directory.CreateDirectory(directory);
                await using (var stream = File.Create(Path.Combine(directory, storedFileName)))
                {
                    await file.CopyToAsync(stream);
                }
                if (challenge.File is not null)
                {
                    var oldPath = Path.Combine(directory, challenge.File.StoredName);
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                }
                replacementFile = new PuzzleFile(fileName, file.Length, storedFileName);
            }
            else if (removeFile && challenge.File is not null)
            {
                var oldPath = Path.Combine(store.GetChallengeDirectory(ctfId, challengeId), challenge.File.StoredName);
                if (File.Exists(oldPath)) File.Delete(oldPath);
                replacementFile = null;
            }

            challenge.Name = name;
            challenge.Description = description;
            challenge.Points = points;
            challenge.File = replacementFile;
            // A blank flag means no change; changing it clears old solves so players can try again.
            if (!string.IsNullOrWhiteSpace(newFlag) && !string.Equals(newFlag, challenge.Flag, StringComparison.Ordinal))
            {
                challenge.Flag = newFlag;
                challenge.SolvedBy.Clear();
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            return Results.Ok(ToChallengeResponse(challenge, userId));
        }).RequireAuthorization();

        // Removing a puzzle also removes its uploaded file from disk.
        app.MapDelete("/api/ctfs/{ctfId:guid}/challenges/{challengeId:guid}", (Guid ctfId, Guid challengeId, CtfStore store) =>
        {
            if (!store.TryGetCtf(ctfId, out var ctf) || ctf is null)
            {
                return Results.NotFound(new { message = "CTF not found." });
            }

            return store.DeleteChallenge(ctf, challengeId)
                ? Results.NoContent()
                : Results.NotFound(new { message = "Puzzle not found." });
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
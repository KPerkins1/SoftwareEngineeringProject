using System.Text.Json;
using AuthApi.Models;

namespace AuthApi.Services;

// This class acts like a very small database.
// Instead of SQL, it stores user and CTF records as JSON files in a local app-data folder.
public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // The lock prevents two requests from reading/writing the same JSON file at the exact same time.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _usersPath;
    private readonly string _ctfsPath;

    public JsonFileStore(IConfiguration configuration)
    {
        var directory = configuration["CTF_DATA_DIRECTORY"];
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CtfPortal");
        }

        Directory.CreateDirectory(directory);
        _usersPath = Path.Combine(directory, "users.json");
        _ctfsPath = Path.Combine(directory, "ctfs.json");
    }

    // This runs on startup and makes sure the configured admin exists.
    // If the password is set in config, we create the account once and store the hash in the JSON file.
    public async Task EnsureBootstrapAdminAsync(string email, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUnlockedAsync<UserAccount>(_usersPath);
            var existing = users.FirstOrDefault(user =>
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (!string.Equals(existing.Role, "Admin", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The configured bootstrap account is not an admin.");
                }

                return;
            }

            users.Add(PasswordSecurity.CreateAccount(email, "Admin", password));
            await WriteUnlockedAsync(_usersPath, users);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccount?> FindUserAsync(string email)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUnlockedAsync<UserAccount>(_usersPath);
            return users.FirstOrDefault(user =>
                string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserAccount> CreateInstructorAsync(string email, string password)
    {
        await _gate.WaitAsync();
        try
        {
            var users = await ReadUnlockedAsync<UserAccount>(_usersPath);
            if (users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("An account with that email already exists.");
            }

            var account = PasswordSecurity.CreateAccount(email, "Instructor", password);
            users.Add(account);
            await WriteUnlockedAsync(_usersPath, users);
            return account;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CtfRecord>> GetCtfsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return (await ReadUnlockedAsync<CtfRecord>(_ctfsPath))
                .OrderByDescending(ctf => ctf.StartsAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CtfRecord> CreateCtfAsync(CtfRecord ctf)
    {
        await _gate.WaitAsync();
        try
        {
            var ctfs = await ReadUnlockedAsync<CtfRecord>(_ctfsPath);
            ctfs.Add(ctf);
            await WriteUnlockedAsync(_ctfsPath, ctfs);
            return ctf;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<T>> ReadUnlockedAsync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions) ?? [];
    }

    private static async Task WriteUnlockedAsync<T>(string path, List<T> records)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, records, JsonOptions);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

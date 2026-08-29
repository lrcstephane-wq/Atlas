using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atlas.Core.Models;

namespace Atlas.Core.Services;

public sealed class UserAccountStore(string sharedRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private string UsersPath => Path.Combine(sharedRoot, "Configuration", "users.atlas.json");

    public async Task<IReadOnlyList<UserAccount>> LoadAsync()
    {
        if (!File.Exists(UsersPath)) return [];
        await using var stream = File.Open(UsersPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<List<UserAccount>>(stream, JsonOptions) ?? [];
    }

    public async Task<UserAccount> CreateFirstAdministratorAsync(string login, string displayName, string password)
    {
        var users = (await LoadAsync()).ToList();
        if (users.Count != 0) throw new InvalidOperationException("Un administrateur existe déjà.");
        var account = CreateAccount(login, displayName, password,
            UserPermissions.Read | UserPermissions.Edit | UserPermissions.Validate | UserPermissions.Administer);
        users.Add(account);
        await SaveAsync(users);
        return account;
    }

    public async Task<UserAccount> AddAsync(string login, string displayName, string password, UserPermissions permissions)
    {
        var users = (await LoadAsync()).ToList();
        if (users.Any(user => user.Login.Equals(login.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ce nom de connexion existe déjà.");
        var account = CreateAccount(login, displayName, password, permissions | UserPermissions.Read);
        users.Add(account);
        await SaveAsync(users);
        return account;
    }

    public async Task<UserAccount?> AuthenticateAsync(string login, string password)
    {
        var users = (await LoadAsync()).ToList();
        var account = users.FirstOrDefault(user => user.IsActive && user.Login.Equals(login.Trim(), StringComparison.OrdinalIgnoreCase));
        if (account is null || !VerifyPassword(account, password)) return null;
        account.LastLoginUtc = DateTimeOffset.UtcNow;
        await SaveAsync(users);
        return account;
    }

    public async Task SaveAccountsAsync(IEnumerable<UserAccount> accounts) => await SaveAsync(accounts.ToList());

    public static UserAccount CreateAccount(string login, string displayName, string password, UserPermissions permissions)
    {
        if (string.IsNullOrWhiteSpace(login)) throw new ArgumentException("Le nom de connexion est obligatoire.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Le nom affiché est obligatoire.");
        if (password.Length < 8) throw new ArgumentException("Le mot de passe doit contenir au moins 8 caractères.");
        var salt = RandomNumberGenerator.GetBytes(24);
        const int iterations = 180_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return new UserAccount
        {
            Login = login.Trim(), DisplayName = displayName.Trim(), Permissions = permissions,
            PasswordSalt = Convert.ToBase64String(salt), PasswordHash = Convert.ToBase64String(hash), PasswordIterations = iterations
        };
    }

    private static bool VerifyPassword(UserAccount account, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(account.PasswordSalt);
            var expected = Convert.FromBase64String(account.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, account.PasswordIterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }

    private async Task SaveAsync(IReadOnlyList<UserAccount> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UsersPath)!);
        var temporary = UsersPath + $".{Environment.MachineName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, users, JsonOptions);
        File.Move(temporary, UsersPath, true);
    }
}

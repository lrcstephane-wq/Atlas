using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Atlas.Core.Services;

public enum AtlasLicenseState
{
    Missing,
    Valid,
    Expired,
    Invalid
}

public sealed record AtlasLicenseInfo(
    AtlasLicenseState State,
    string Customer,
    DateOnly? ValidUntil,
    string LicenseId,
    string Message)
{
    public bool IsValid => State == AtlasLicenseState.Valid;
}

public sealed class AtlasLicenseService
{
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEDpV+t9vu4eMTZL7zR9dCWuAXDNbZ
        QqhUAjnRYh2sf+RzWiITQinOmMWI9zk76jAEAzxxwKomdt01RDWnXjkXIg==
        -----END PUBLIC KEY-----
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _publicKeyPem;
    private readonly string _licenseFilePath;

    public AtlasLicenseService(string? publicKeyPem = null, string? licenseFilePath = null)
    {
        _publicKeyPem = string.IsNullOrWhiteSpace(publicKeyPem) ? PublicKeyPem : publicKeyPem;
        _licenseFilePath = string.IsNullOrWhiteSpace(licenseFilePath) ? LicenseFilePath : licenseFilePath;
    }

    public static string LicenseFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions", "Atlas", "Licence.fautpastoucher");

    public static string SigningPrivateKeyPath(string sharedRoot) =>
        Path.Combine(sharedRoot, "Configuration", "Atlas-Licence-ClePrivee.pem");

    public AtlasLicenseInfo Read(DateOnly? today = null)
    {
        if (!File.Exists(_licenseFilePath))
            return new(AtlasLicenseState.Missing, string.Empty, null, string.Empty, "Aucune licence Atlas n’est installée.");
        try { return Validate(File.ReadAllText(_licenseFilePath), today); }
        catch { return new(AtlasLicenseState.Invalid, string.Empty, null, string.Empty, "Le fichier de licence est illisible."); }
    }

    public AtlasLicenseInfo Install(string code, DateOnly? today = null)
    {
        var result = Validate(code, today);
        if (result.State is not (AtlasLicenseState.Valid or AtlasLicenseState.Expired)) return result;
        Directory.CreateDirectory(Path.GetDirectoryName(_licenseFilePath)!);
        var temporary = _licenseFilePath + ".tmp";
        File.WriteAllText(temporary, code.Trim(), Encoding.UTF8);
        File.Move(temporary, _licenseFilePath, true);
        return result;
    }

    public AtlasLicenseInfo Validate(string code, DateOnly? today = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new(AtlasLicenseState.Missing, string.Empty, null, string.Empty, "Saisissez le code de licence reçu par e-mail.");
        try
        {
            var parts = code.Trim().Split('.');
            if (parts.Length != 3 || parts[0] != "ATL1") throw new InvalidDataException();
            var payloadBytes = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(_publicKeyPem);
            if (!verifier.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256)) throw new CryptographicException();

            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions) ?? throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(payload.Customer) || string.IsNullOrWhiteSpace(payload.LicenseId) || !DateOnly.TryParseExact(payload.ValidUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validUntil))
                throw new InvalidDataException();
            var currentDate = today ?? DateOnly.FromDateTime(DateTime.Today);
            return currentDate > validUntil
                ? new(AtlasLicenseState.Expired, payload.Customer.Trim(), validUntil, payload.LicenseId, $"La licence de {payload.Customer.Trim()} a expiré le {validUntil:dd/MM/yyyy}.")
                : new(AtlasLicenseState.Valid, payload.Customer.Trim(), validUntil, payload.LicenseId, $"Licence valide jusqu’au {validUntil:dd/MM/yyyy} inclus.");
        }
        catch
        {
            return new(AtlasLicenseState.Invalid, string.Empty, null, string.Empty, "Ce code de licence est invalide ou a été modifié.");
        }
    }

    public static string Generate(string customer, DateOnly validUntil, string privateKeyPem, string? licenseId = null)
    {
        if (string.IsNullOrWhiteSpace(customer)) throw new ArgumentException("Le nom du client ou du site est obligatoire.", nameof(customer));
        if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("La clé privée de signature est absente.", nameof(privateKeyPem));
        var payload = new LicensePayload
        {
            Customer = customer.Trim(),
            ValidUntil = validUntil.ToString("yyyy-MM-dd"),
            LicenseId = string.IsNullOrWhiteSpace(licenseId) ? Guid.NewGuid().ToString("N") : licenseId.Trim()
        };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var signature = signer.SignData(payloadBytes, HashAlgorithmName.SHA256);
        return $"ATL1.{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}";
    }

    private static string ToBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += normalized.Length % 4 switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    private sealed class LicensePayload
    {
        public string Customer { get; set; } = string.Empty;
        public string ValidUntil { get; set; } = string.Empty;
        public string LicenseId { get; set; } = string.Empty;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Atlas.Core.Services;

public sealed record IdeoAdminAccessGrant(string OperatorName, string TokenId, DateTimeOffset ExpiresUtc);

public sealed class IdeoAdminChallenge
{
    private readonly string _deviceId;
    private readonly string _nonce;
    private bool _consumed;

    internal IdeoAdminChallenge(string deviceId, string nonce, string requestCode)
    {
        _deviceId = deviceId;
        _nonce = nonce;
        RequestCode = requestCode;
    }

    public string RequestCode { get; }
    public bool IsConsumed => _consumed;

    public IdeoAdminAccessGrant ValidateAndConsume(string responseCode, string? publicKeyPem = null, DateTimeOffset? now = null)
    {
        if (_consumed) throw new InvalidOperationException("Cette demande d’accès Idéo a déjà été utilisée.");
        var grant = IdeoAdminAccessService.ValidateResponse(responseCode, _deviceId, _nonce, publicKeyPem, now);
        _consumed = true;
        return grant;
    }
}

public static class IdeoAdminAccessService
{
    private const string RequestPrefix = "ATLREQ1";
    private const string ResponsePrefix = "ATLADM1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IdeoAdminChallenge CreateChallenge(string? deviceId = null, DateTimeOffset? now = null)
    {
        deviceId = string.IsNullOrWhiteSpace(deviceId) ? MachineIdentityService.CurrentDeviceId : deviceId.Trim();
        var nonce = ToBase64Url(RandomNumberGenerator.GetBytes(24));
        var payload = new AccessRequestPayload
        {
            DeviceId = deviceId,
            Nonce = nonce,
            CreatedUtc = (now ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
        var requestCode = $"{RequestPrefix}.{ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))}";
        return new IdeoAdminChallenge(deviceId, nonce, requestCode);
    }

    public static string GenerateResponse(string requestCode, string operatorName, string privateKeyPem,
        DateTimeOffset? now = null, TimeSpan? responseLifetime = null)
    {
        if (string.IsNullOrWhiteSpace(operatorName)) throw new ArgumentException("Le nom de l’opérateur Idéo est obligatoire.", nameof(operatorName));
        if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("La clé privée Idéo est absente.", nameof(privateKeyPem));
        var request = ParseRequest(requestCode);
        var current = (now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (request.CreatedUtc > current.AddMinutes(1) || current - request.CreatedUtc > TimeSpan.FromMinutes(15))
            throw new InvalidOperationException("La demande du poste client a expiré. Générez une nouvelle demande.");
        var lifetime = responseLifetime ?? TimeSpan.FromMinutes(5);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(responseLifetime));
        var response = new AccessResponsePayload
        {
            Purpose = "ideo-admin-access",
            DeviceId = request.DeviceId,
            Nonce = request.Nonce,
            OperatorName = operatorName.Trim(),
            TokenId = Guid.NewGuid().ToString("N"),
            ExpiresUtc = current.Add(lifetime)
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var signature = signer.SignData(payload, HashAlgorithmName.SHA256);
        return $"{ResponsePrefix}.{ToBase64Url(payload)}.{ToBase64Url(signature)}";
    }

    public static IdeoAdminAccessGrant ValidateResponse(string responseCode, string expectedDeviceId, string expectedNonce,
        string? publicKeyPem = null, DateTimeOffset? now = null)
    {
        try
        {
            var parts = responseCode.Trim().Split('.');
            if (parts.Length != 3 || parts[0] != ResponsePrefix) throw new InvalidDataException();
            var payload = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(string.IsNullOrWhiteSpace(publicKeyPem) ? AtlasLicenseService.DefaultPublicKeyPem : publicKeyPem);
            if (!verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256)) throw new CryptographicException();
            var access = JsonSerializer.Deserialize<AccessResponsePayload>(payload, JsonOptions) ?? throw new InvalidDataException();
            if (access.Purpose != "ideo-admin-access" || string.IsNullOrWhiteSpace(access.OperatorName) || string.IsNullOrWhiteSpace(access.TokenId)) throw new InvalidDataException();
            if (!MachineIdentityService.Normalize(access.DeviceId).Equals(MachineIdentityService.Normalize(expectedDeviceId), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Ce code Administrateur Idéo a été généré pour un autre poste.");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(access.Nonce), Encoding.UTF8.GetBytes(expectedNonce)))
                throw new UnauthorizedAccessException("Ce code ne correspond pas à la demande actuellement affichée.");
            if ((now ?? DateTimeOffset.UtcNow).ToUniversalTime() > access.ExpiresUtc)
                throw new UnauthorizedAccessException("Ce code Administrateur Idéo a expiré.");
            return new IdeoAdminAccessGrant(access.OperatorName.Trim(), access.TokenId, access.ExpiresUtc);
        }
        catch (UnauthorizedAccessException) { throw; }
        catch
        {
            throw new UnauthorizedAccessException("Le code Administrateur Idéo est invalide ou a été modifié.");
        }
    }

    private static AccessRequestPayload ParseRequest(string requestCode)
    {
        try
        {
            var parts = requestCode.Trim().Split('.');
            if (parts.Length != 2 || parts[0] != RequestPrefix) throw new InvalidDataException();
            var request = JsonSerializer.Deserialize<AccessRequestPayload>(FromBase64Url(parts[1]), JsonOptions) ?? throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.Nonce)) throw new InvalidDataException();
            return request;
        }
        catch { throw new InvalidDataException("La demande d’accès Idéo est invalide."); }
    }

    private static string ToBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }

    private sealed class AccessRequestPayload
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
    }

    private sealed class AccessResponsePayload
    {
        public string Purpose { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string TokenId { get; set; } = string.Empty;
        public DateTimeOffset ExpiresUtc { get; set; }
    }
}

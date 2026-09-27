namespace Hba.Identity.Api.Controllers.Requests;

// Corps de requête des routes REST d'Identity.
//
// CES ENREGISTREMENTS NE SONT PAS LES COMMANDES, ET C'EST DELIBERE. Une
// commande porte un Guid déjà analysé et un enum du domaine ; un corps JSON
// porte des chaînes venues de l'extérieur. Les confondre ferait entrer une
// valeur non validée directement dans la couche application, et rendrait tout
// renommage de champ interne visible sur le fil.
//
// Les identifiants portés par l'URL n'apparaissent pas ici : ils viennent de la
// route.

public sealed record OtpRequestBody(string Phone, string? Intent, string? DeviceId);

public sealed record OtpVerifyBody(string ChallengeId, string Code, string? DeviceId, string? DisplayName);

public sealed record LoginBody(string Login, string Password, string? DeviceId);

public sealed record RefreshBody(string RefreshToken, string? DeviceId);

public sealed record RevokeBody(string RefreshToken);

public sealed record ChangePasswordBody(string? AccountId, string? CurrentPassword, string NewPassword);

public sealed record RevokeAllBody(string? AccountId);

public sealed record CreateBackOfficeAccountBody(
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string InitialPassword);

public sealed record CreateMerchantAccountBody(
    string MerchantId,
    string? Email,
    string? Phone,
    string DisplayName,
    string Role,
    string InitialPassword);

public sealed record SetAccountRolesBody(IReadOnlyList<string> Roles, string Reason);

public sealed record ReasonBody(string Reason);

public sealed record LinkDriverProfileBody(string DriverId);

public sealed record CreatePartnerClientBody(
    string PartnerName,
    string Source,
    string? WebhookUrl,
    IReadOnlyList<string> Scopes,
    int RateLimitPerMinute);

public sealed record IssuePartnerTokenBody(
    string ClientId,
    string ClientSecret,
    IReadOnlyList<string>? Scopes);

using Hba.BuildingBlocks.Application.Messaging;
using Hba.Identity.Application.Views;

namespace Hba.Identity.Application.Accounts;

public sealed record CreateBackOfficeAccountCommand(
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string InitialPassword) : ICommand<AccountView>;

public sealed record CreateMerchantAccountCommand(
    string MerchantId,
    string? Email,
    string? Phone,
    string DisplayName,
    string Role,
    string InitialPassword) : ICommand<AccountView>;

public sealed record SetAccountRolesCommand(
    Guid AccountId,
    IReadOnlyList<string> Roles,
    string Reason) : ICommand<AccountView>;

public sealed record SuspendAccountCommand(Guid AccountId, string Reason) : ICommand<AccountView>;

public sealed record ReactivateAccountCommand(Guid AccountId, string Reason) : ICommand<AccountView>;

/// <summary>
/// Changement de mot de passe. Par soi-même avec l'ancien, ou par un
/// administrateur sans l'ancien — et dans ce cas toutes les sessions tombent.
/// </summary>
public sealed record SetPasswordCommand(
    Guid AccountId,
    string? CurrentPassword,
    string NewPassword) : ICommand<Unit>;

/// <summary>Rattache le profil livreur créé par le service Driver.</summary>
public sealed record LinkDriverProfileCommand(Guid AccountId, string DriverId) : ICommand<AccountView>;

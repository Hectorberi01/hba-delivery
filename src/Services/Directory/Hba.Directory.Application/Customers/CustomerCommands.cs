using Hba.BuildingBlocks.Application.Messaging;
using Hba.Directory.Application.Views;

namespace Hba.Directory.Application.Customers;

public sealed record AddressInput(
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes);

public sealed record GetCustomerQuery(string? CustomerId) : IQuery<CustomerView>;

public sealed record UpdateCustomerCommand(string? DisplayName, string? Email) : ICommand<CustomerView>;

public sealed record AddFavoriteAddressCommand(string Label, AddressInput Address, bool SetAsDefault)
    : ICommand<CustomerView>;

public sealed record UpdateFavoriteAddressCommand(
    Guid AddressId,
    string Label,
    AddressInput Address,
    bool SetAsDefault) : ICommand<CustomerView>;

public sealed record RemoveFavoriteAddressCommand(Guid AddressId) : ICommand<CustomerView>;

/// <summary>
/// Attache au profil une photo déjà déposée dans Media.
///
/// ELLE NE PORTE PAS D'IDENTIFIANT DE CLIENT, ET C'EST VOLONTAIRE : on ne pose
/// que sa propre photo. Le back-office qui aurait à en retirer une passe par
/// une autre porte le jour où elle existera — lui en donner une ici ferait
/// d'une commande de profil une commande d'administration.
/// </summary>
public sealed record SetCustomerPhotoCommand(Guid MediaId) : ICommand<CustomerView>;

public sealed record RemoveCustomerPhotoCommand : ICommand<CustomerView>;

/// <summary>
/// Le lien signé pour afficher la photo d'un client.
///
/// UNE REQUETE, PAS UN CHAMP DE LA FICHE. Une URL signée expire en quelques
/// minutes : la placer dans CustomerView obligerait à appeler Media à chaque
/// lecture de profil — y compris pour les clients qui n'ont pas de photo — et
/// ferait d'une réponse mise en cache une réponse fausse.
/// </summary>
public sealed record GetCustomerPhotoLinkQuery(string? CustomerId) : IQuery<PhotoLinkView>;

/// <summary>
/// Déclenchée par l'événement AccountRegistered d'Identity. C'est le chemin
/// NORMAL de création d'un profil client — le seul qui parte d'une inscription.
/// </summary>
public sealed record CreateCustomerProfileCommand(
    Guid AccountId,
    string DisplayName,
    string Phone,
    string? Email,
    DateTimeOffset RegisteredAt) : ICommand<Unit>;

/// <summary>
/// Rattrapage : crée le profil du client authentifié s'il manque, et rend le
/// profil dans tous les cas.
///
/// POURQUOI UNE SECONDE VOIE EXISTE, ALORS QUE LE COMMENTAIRE D'A COTE DIT
/// QU'IL N'Y EN A QU'UNE. Parce que la première peut se perdre : un événement
/// publié pendant que Directory était arrêté n'est pas rejoué par Kafka pour un
/// groupe de consommateurs qui n'existait pas encore. Le client se retrouve
/// alors avec un compte valide, des livraisons qui marchent, et aucun profil —
/// donc aucune adresse favorite, et un écran qui ne sait que s'excuser.
///
/// CE QUI LA REND ACCEPTABLE, ET QU'IL NE FAUT PAS DEFAIRE :
///   - elle ne prend AUCUN champ : nom, téléphone et courriel viennent du
///     jeton que Directory a validé lui-même, donc rien n'est forgeable ;
///   - elle est explicite : c'est un geste du client, pas un effet de bord
///     d'une lecture ni d'une demande de livraison ;
///   - elle est idempotente : un profil déjà là est rendu tel quel ;
///   - elle est journalisée à part, pour que la chaîne cassée reste visible.
///
/// Décidé le 28 septembre 2026. À reporter dans points-a-trancher.md.
/// </summary>
public sealed record EnsureCustomerProfileCommand : ICommand<CustomerView>;

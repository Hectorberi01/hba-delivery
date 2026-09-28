using Hba.BuildingBlocks.Application.Messaging;
using Hba.Media.Domain.Assets;

namespace Hba.Media.Application.Assets;

/// <summary>Ce que Media rend d'un fichier. Jamais la clé de stockage.</summary>
public sealed record MediaView(
    Guid Id,
    MediaOwnerType OwnerType,
    string OwnerId,
    MediaKind Kind,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string UploadedBy);

public sealed record LienDeLecture(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Dépôt d'un fichier.
///
/// LE FLUX N'EST PAS UN CHAMP COMME LES AUTRES : il se lit une fois, et il est
/// ouvert par la route HTTP qui le referme derrière elle. Le handler ne doit ni
/// le stocker, ni le rejouer.
/// </summary>
public sealed record StoreMediaCommand(
    MediaOwnerType OwnerType,
    string OwnerId,
    MediaKind Kind,
    Stream Contenu,
    long SizeBytes,
    string ContentType,
    string Extension) : ICommand<MediaView>;

public sealed record GetMediaQuery(Guid MediaId) : IQuery<MediaView>;

/// <summary>
/// URL de lecture signée.
///
/// L'APPELANT A DÉJÀ VÉRIFIÉ LE DROIT — Media ne saurait pas le faire : dire si
/// un livreur peut voir la photo d'un client suppose de savoir s'il est en
/// mission sur SA course, ce que seul Delivery connaît. Ce que Media fait, en
/// revanche, c'est consigner la demande.
/// </summary>
public sealed record GetReadUrlQuery(Guid MediaId, string? Motif) : IQuery<LienDeLecture>;

public sealed record ListMediaQuery(
    MediaOwnerType OwnerType,
    string OwnerId,
    MediaKind? Kind) : IQuery<IReadOnlyList<MediaView>>;

public sealed record DeleteMediaCommand(Guid MediaId) : ICommand<bool>;

/// <summary>
/// Supprime tout ce qui appartient à un propriétaire.
///
/// C'EST LA MOITIÉ MANQUANTE DE LA SUPPRESSION DE COMPTE. Le point 22 laissait
/// la question ouverte faute de pouvoir énumérer les objets : une clé oubliée
/// est un fichier qui survit à son propriétaire. L'inventaire la rend possible.
/// </summary>
public sealed record DeleteOwnerMediaCommand(
    MediaOwnerType OwnerType,
    string OwnerId) : ICommand<int>;

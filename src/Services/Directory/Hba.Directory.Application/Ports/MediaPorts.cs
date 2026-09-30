namespace Hba.Directory.Application.Ports;

/// <summary>
/// Ce que Directory sait d'un média, et c'est peu.
/// </summary>
///
/// <remarks>
/// DES CHAINES, ET NON LES ENUMERATIONS DE MEDIA. La couche applicative de
/// Directory ne connaît pas le contrat de Media : l'y faire entrer donnerait à
/// une règle de Directory — « on n'accepte comme portrait qu'une photo de
/// profil appartenant à ce client » — la forme du vocabulaire d'un autre
/// service, et la ferait bouger chaque fois que ce vocabulaire bouge. La
/// conversion est le travail de l'adaptateur, dans Infrastructure, qui est
/// justement l'endroit où les deux mondes ont le droit de se toucher.
/// </remarks>
public sealed record MediaDecrit(Guid Id, string OwnerType, string OwnerId, string Kind);

public sealed record LienDeLecture(Uri Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Le peu que Directory demande au service Media.
/// </summary>
///
/// <remarks>
/// TROIS METHODES, ET AUCUNE NE TRANSPORTE D'OCTETS. Directory n'écrit jamais
/// de fichier : le dépôt se fait en HTTP, directement sur Media, et ce que
/// Directory reçoit ensuite n'est qu'un identifiant. Ce port sert à VERIFIER
/// cet identifiant, à obtenir un lien pour l'afficher, et à faire effacer une
/// photo remplacée.
/// </remarks>
public interface IMediaCatalogue
{
    /// <summary>Décrit un média, ou rend null s'il n'existe pas.</summary>
    Task<MediaDecrit?> DecrireAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>
    /// URL de lecture signée.
    /// </summary>
    ///
    /// <remarks>
    /// LE MOTIF EST OBLIGATOIRE, ET IL PART DANS LE JOURNAL DE MEDIA. « Le
    /// client consulte son profil » et « le support ouvre une fiche » ne se
    /// jugent pas de la même façon, et c'est la seule question qu'on posera
    /// jamais à ce journal.
    /// </remarks>
    Task<LienDeLecture> LienDeLectureAsync(Guid mediaId, string motif, CancellationToken cancellationToken);

    /// <summary>
    /// Demande l'effacement d'un média.
    /// </summary>
    ///
    /// <remarks>
    /// NE DOIT PAS FAIRE ECHOUER L'APPELANT. Elle est appelée après qu'une
    /// nouvelle photo a été enregistrée : si Media est injoignable à cet
    /// instant, le client a bien sa nouvelle photo et c'est ce qui compte. Ce
    /// qui reste derrière est un fichier orphelin — que l'inventaire de Media
    /// permet précisément de retrouver, contrairement à l'ancien rangement par
    /// clés. L'implémentation journalise et ne propage pas.
    /// </remarks>
    Task SupprimerAsync(Guid mediaId, CancellationToken cancellationToken);

    /// <summary>
    /// Fait effacer TOUT ce que Media détient pour ce client.
    /// </summary>
    ///
    /// <remarks>
    /// PAS « LA PHOTO », MAIS « TOUT CE QU'IL POSSEDE ». Aujourd'hui il n'y a
    /// qu'un portrait ; demain il y aura des factures, peut-être des preuves.
    /// Une méthode qui n'effacerait que la photo laisserait le reste derrière
    /// elle sans que personne ne s'en aperçoive — c'est exactement le trou que
    /// l'inventaire de Media a été créé pour boucher.
    ///
    /// ELLE NE FAIT PAS ECHOUER L'APPELANT, pour la même raison que
    /// <see cref="SupprimerAsync" /> : mieux vaut une fiche effacée et un
    /// fichier orphelin — que l'inventaire sait retrouver — qu'une suppression
    /// de compte qui échoue et laisse tout en place.
    /// </remarks>
    Task SupprimerParProprietaireAsync(Guid customerId, CancellationToken cancellationToken);
}

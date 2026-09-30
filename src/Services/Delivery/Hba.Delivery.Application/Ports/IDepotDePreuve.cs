namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Pousse une photo de course vers le service Media, et rend son identifiant.
/// </summary>
///
/// <remarks>
/// « DELIVERY PORTE LES OCTETS » — point 7, question 4, tranchée le 30 septembre
/// 2026. Le livreur envoie sa photo à Delivery, qui vérifie sur SON agrégat
/// qu'il est bien le livreur affecté, puis dépose ici avec un JETON DE SERVICE.
/// C'est la seule lecture du point 27 qui ne demande à personne de faire le
/// métier d'un autre : Media ne sait pas qui est affecté à quelle course, et
/// Delivery ne stocke pas de fichiers.
///
/// CE QUI REVIENT EST UN IDENTIFIANT DE MEDIA, PAS UNE CLE DE STOCKAGE. Le point
/// 27 l'impose : la clé ne sort pas de Media, sous peine de voir les services
/// parler au stockage en direct et l'inventaire devenir faux — cet inventaire
/// étant précisément ce qui rend possibles la purge à trente jours et la
/// suppression de compte.
///
/// LE FLUX N'EST PAS UN PARAMETRE COMME LES AUTRES : il se lit une fois, et il
/// est ouvert puis refermé par la route HTTP qui l'a reçu. L'implémentation le
/// relaie sans le recopier en mémoire.
/// </remarks>
public interface IDepotDePreuve
{
    Task<Guid> DeposerAsync(
        Guid deliveryId,
        Stream contenu,
        long tailleOctets,
        string typeDeContenu,
        CancellationToken cancellationToken);
}

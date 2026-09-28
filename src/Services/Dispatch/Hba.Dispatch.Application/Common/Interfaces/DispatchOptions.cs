namespace Hba.Dispatch.Application.Common.Interfaces;

/// <summary>
/// Politique de recherche d'un livreur.
///
/// TROIS ANNEAUX — 2, 4 ET 6 KILOMETRES — ET UN LIVREUR A LA FOIS, decide le
/// 28 septembre 2026 (point 23). La course part au plus proche ; s'il ne
/// repond pas dans le delai, elle passe au suivant, et l'anneau ne s'elargit
/// que lorsqu'il ne reste plus personne a qui demander dedans.
///
/// CE N'EST PLUS « UNE VAGUE PAR RAYON », et c'est le changement de fond. Le
/// nombre de tentatives etait borne par le nombre de rayons : trois vagues,
/// puis NO_DRIVER_FOUND, quel que soit le nombre de livreurs autour. La
/// recherche s'arrete desormais quand la LISTE est epuisee, pas quand le
/// compteur de vagues l'est.
///
/// ELLES SONT EN CONFIGURATION, PAS EN DUR, et ce n'est pas de la prudence
/// gratuite : ce sont des parametres d'exploitation. Le pilote dira si deux
/// kilometres suffisent aux heures creuses, et l'ajustement ne doit pas
/// demander une compilation.
/// </summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>
    /// Les anneaux, du plus etroit au plus large.
    ///
    /// CE TABLEAU NE DIT PLUS COMBIEN DE TENTATIVES AURONT LIEU, seulement
    /// jusqu'ou chercher. On reste sur un anneau tant qu'il rend un livreur
    /// pas encore sollicite ; on passe au suivant quand il n'en rend plus.
    /// </summary>
    public int[] WaveRadiiMeters { get; set; } = [2000, 4000, 6000];

    /// <summary>Delai de reponse. Le referentiel fixe 30 s par defaut.</summary>
    public int OfferSeconds { get; set; } = 30;

    /// <summary>
    /// Nombre de livreurs sollicites a la fois. UN, depuis le 28 septembre.
    ///
    /// CINQ ETAIT UN NOMBRE QUE PERSONNE N'AVAIT DECIDE. Le point 5 avait
    /// tranche le nombre de vagues, les rayons et le delai ; la largeur de la
    /// diffusion, elle, etait arrivee dans le code sans discussion.
    ///
    /// DEUX RAISONS DE LA RAMENER A UN, et la seconde est la plus forte.
    /// D'abord chaque offre immobilise un livreur pendant trente secondes :
    /// en solliciter cinq pour une course en gele quatre pour rien, et les
    /// courses voisines n'ont plus personne. Ensuite, et surtout, la course
    /// revenait a CELUI QUI TAPE LE PLUS VITE, pas au plus proche : le client
    /// attendait un livreur plus loin que necessaire, et celui qui etait a
    /// cote perdait une course qui lui revenait.
    ///
    /// Le prix est le temps : les candidats sont sollicites l'un apres
    /// l'autre, donc une zone ou personne ne repond se parcourt en trente
    /// secondes par livreur.
    /// </summary>
    public int DriversPerWave { get; set; } = 1;

    /// <summary>
    /// Plafond de livreurs sollicites pour une meme course, tous anneaux
    /// confondus. Au-dela : NO_DRIVER_FOUND, meme s'il reste du monde.
    ///
    /// UN PLAFOND EN NOMBRE, PAS EN TEMPS, ET C'EST UN CHOIX QUI SE RAISONNE.
    /// Si dix livreurs ont laisse passer la course, le probleme n'est pas le
    /// onzieme : c'est la course elle-meme — trop loin, mal payee, un quartier
    /// qu'on evite. Continuer a descendre la liste ne ferait que faire perdre
    /// du temps au client pour un refus de plus.
    ///
    /// LA DUREE RESTE BORNEE SANS QU'ON AIT A LA REGLER : dix livreurs a
    /// trente secondes font cinq minutes au pire, et ce produit se relit d'un
    /// coup d'oeil. Deux plafonds — un temps ET un nombre — auraient pu se
    /// contredire, et c'est toujours celui qu'on a oublie qui se declenche.
    /// </summary>
    public int MaxDriversSolicited { get; set; } = 10;

    /// <summary>Duree du verrou d'acceptation. Courte : il ne couvre qu'une transaction.</summary>
    public int AcceptanceLockSeconds { get; set; } = 10;

    /// <summary>Periode de balayage du planificateur.</summary>
    public int SweepSeconds { get; set; } = 5;

    public int WaveCount => WaveRadiiMeters.Length;

    public TimeSpan OfferLifetime => TimeSpan.FromSeconds(OfferSeconds);
}

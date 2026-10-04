namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Ce qui passe dans les sockets.
/// </summary>
///
/// <remarks>
/// DEUX TRAMES SUR TROIS SONT DES SIGNAUX, PAS DES DONNEES, et c'est la decision la
/// plus importante de ce service.
///
/// Un changement d'etat et une offre pourraient voyager avec tout leur contenu : le
/// nom du chauffeur, son telephone, le prix, l'adresse. Ils ne le font pas. Ils
/// disent « ceci a change, va le lire » — et l'application rappelle la route REST
/// qu'elle appelait deja.
///
/// POURQUOI. Parce que la matrice de visibilite est appliquee dans les services et a
/// la passerelle, a des endroits precis et eprouves. Mettre le contenu dans la trame
/// obligerait a l'appliquer ICI une deuxieme fois : qui voit le telephone du
/// chauffeur, a partir de quand, jusqu'a quand. Deux implementations d'une regle de
/// confidentialite finissent par diverger, et celle qui diverge ici enverrait le
/// telephone de quelqu'un un peu trop tot — sans que rien ne le signale.
///
/// LE COUT EST UN ALLER-RETOUR REST PAR CHANGEMENT D'ETAT. Une mission en compte cinq
/// a huit, de bout en bout. Ce n'est rien, et on l'echange contre la certitude que la
/// matrice n'est appliquee qu'a un seul endroit.
///
/// LA POSITION, ELLE, EST UNE DONNEE, parce qu'aucune route REST ne la sert au client
/// — c'est tout l'objet de la phase 8 — et parce que sa borne est simple : la
/// position, rien de plus (point 37). Elle est donc la seule trame qui porte du
/// contenu, et c'est aussi celle dont le contenu est le plus etroit.
/// </remarks>
internal static class Trames
{
    // LES NOMS DE CHAMPS ARRIVENT EN MINUSCULE INITIALE, ET LES CLIENTS LES ECRIVENT
    // EN DUR.
    //
    // Le protocole JSON de SignalR serialise avec JsonNamingPolicy.CamelCase : la
    // propriete « MissionId » d'un enregistrement ci-dessous devient « missionId » dans
    // la trame, et c'est cette forme-la que lisent suivi_temps_reel.dart et la console.
    // Renommer une propriete ici casse donc un client sans qu'aucun compilateur ne s'en
    // apercoive — comme renommer un champ de reponse REST.
    //
    // LES DATES SORTENT EN ISO 8601, ce que « DateTime.tryParse » lit cote Dart. Les
    // rendre en secondes Unix aurait economise quelques octets sur un flux qui en
    // compte peu, et aurait demande une conversion a chaque lecteur.

    /// <summary>Noms des methodes appelees chez le client. Ils font partie du contrat.</summary>
    public static class Methodes
    {
        /// <summary>« L'etat de ta mission a change, relis-la. »</summary>
        public const string Etat = "etat";

        /// <summary>« Voici ou se trouve ton chauffeur. »</summary>
        public const string Position = "position";

        /// <summary>« Une offre t'attend, va la chercher. »</summary>
        public const string Offre = "offre";
    }
}

/// <summary>
/// Un changement d'etat. UN SIGNAL : il ne porte ni nom, ni telephone, ni montant.
/// </summary>
///
/// <remarks>
/// L'ETAT EST RENDU EN TEXTE, TEL QUE LE CONTRAT L'ECRIT — « DRIVER_ASSIGNED »,
/// « COMPLETED ». Pas un entier : la valeur numerique d'un enum protobuf obligerait
/// l'application a connaitre la numerotation du .proto pour lire une trame, et un
/// renumerotage silencieux changerait le sens de tous les messages deja en vol.
/// </remarks>
internal sealed record EtatDeMission(
    string Nature,
    string MissionId,
    string Etat,
    DateTimeOffset? Horodatage);

/// <summary>
/// La position du chauffeur affecte, pour le CLIENT de la mission.
/// </summary>
///
/// <remarks>
/// NI CAP, NI VITESSE, NI PRECISION, ET C'EST LE POINT 37 QUI LE DIT. « Ou est-il »
/// suffit a decider d'attendre ; la vitesse d'un homme au travail est une donnee de
/// surveillance, et la precision ne repond a aucune question du client. Cette trame
/// est volontairement plus pauvre que celle de l'exploitation, et toute tentative de
/// les unifier rouvrirait le point 37.
///
/// ELLE PORTE LA MISSION, PAS LE CHAUFFEUR. L'application sait quelle mission elle
/// affiche ; l'identifiant du chauffeur ne lui apprendrait rien et lui permettrait de
/// reconnaitre le meme homme d'une course a l'autre — ce que la matrice de visibilite
/// refuse deja pour les chauffeurs non affectes.
/// </remarks>
internal sealed record PositionDeMission(
    string Nature,
    string MissionId,
    double Latitude,
    double Longitude,
    DateTimeOffset VuA);

/// <summary>
/// La position d'un chauffeur, pour l'EXPLOITATION.
/// </summary>
///
/// <remarks>
/// LA SEULE TRAME QUI PORTE LE CAP, LA VITESSE ET LA PRECISION. L'exploitation doit
/// comprendre pourquoi une course traine : un chauffeur immobile depuis dix minutes
/// et un chauffeur qui roule n'appellent pas le meme coup de telephone.
///
/// ELLE NE NOMME PERSONNE. La console garde les noms qu'elle a charges par la route
/// REST ; les renvoyer a chaque battement ferait passer un nom et un telephone
/// plusieurs fois par seconde pour une information qui ne change jamais.
/// </remarks>
internal sealed record PositionDExploitation(
    string DriverId,
    double Latitude,
    double Longitude,
    double? CapDegres,
    double? VitesseMps,
    double? PrecisionMetres,
    DateTimeOffset VuA);

/// <summary>
/// Une offre posee. UN SIGNAL : le chauffeur va la lire par la route REST.
/// </summary>
///
/// <remarks>
/// L'ECHEANCE EST DANS LA TRAME, ET ELLE SEULE JUSTIFIE QU'IL Y AIT PLUS QUE
/// L'IDENTIFIANT. Une offre dure quelques dizaines de secondes ; l'application doit
/// pouvoir lancer son decompte a la milliseconde ou la trame arrive, sans attendre
/// l'aller-retour REST. Tout le reste — l'adresse, le prix, la distance — passe par
/// la route.
/// </remarks>
internal sealed record OffreSignalee(
    string OfferId,
    string Nature,
    string ReferenceId,
    DateTimeOffset? ExpireA);

using Hba.BuildingBlocks.Domain;
using Hba.Driver.Domain.Drivers.Events;
using Hba.Driver.Domain.Exceptions;

namespace Hba.Driver.Domain.Drivers;

/// <summary>
/// Livreur : son dossier, son vehicule, et son etat de travail.
///
/// SA POSITION N'EST PAS ICI, et c'est une regle du referentiel, pas une
/// commodite : « position courante (Redis GEO, jamais chaque point en base
/// relationnelle) ». Un livreur envoie un point toutes les quelques secondes ;
/// les ecrire durablement coute une transaction par battement, fait grossir la
/// table sans fin, et ne sert a rien passe la minute suivante.
///
/// L'IDENTIFIANT EST CELUI DU COMPTE. Le profil nait de l'evenement
/// AccountRegistered d'Identity, et reprend son identifiant : un seul
/// identifiant traverse tous les services, sans table de correspondance.
/// </summary>
public sealed class DriverAggregate : AggregateRoot
{
    private readonly List<DriverDocument> _documents = [];

    // UNE LISTE, ET C'EST UNE CONTRAINTE D'EF, PAS UN CHOIX DE MODELISATION.
    // Les collections primitives d'EF Core exigent un type qui implemente
    // IList<T> : leur materialisation remplit la collection par cette
    // interface. Un HashSet se serialisait bien a l'ecriture et faisait echouer
    // CHAQUE lecture sur InvalidCastException — « Unable to cast HashSet<T> to
    // IList<T> » —, ce qui condamnait tout le service Driver.
    //
    // L'UNICITE RESTE GARANTIE, AILLEURS. Le doublon n'a aucun sens ici :
    // « habilite deux fois au colis » ne veut rien dire. DeclareCapabilities est
    // le seul point de mutation : il deduplique par ToHashSet, vide puis
    // reecrit. Aucun autre code ne touche ce champ.
    private readonly List<DriverCapability> _capabilities = [];

    private DriverAggregate()
    {
    }

    private DriverAggregate(
        Guid id,
        string displayName,
        string phone,
        Vehicle vehicle,
        DateTimeOffset registeredAt)
        : base(id)
    {
        DisplayName = displayName;
        Phone = phone;
        Vehicle = vehicle;
        VerificationStatus = VerificationStatus.PendingVerification;
        OperationalStatus = OperationalStatus.Offline;
        RegisteredAt = registeredAt;
    }

    public string DisplayName { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public Vehicle Vehicle { get; private set; } = null!;

    /// <summary>Le vehicule a-t-il ete declare ? Voir <see cref="Drivers.Vehicle.EstDeclare"/>.</summary>
    public bool VehiculeDeclare => Vehicle.EstDeclare;

    public VerificationStatus VerificationStatus { get; private set; }

    public OperationalStatus OperationalStatus { get; private set; }

    /// <summary>Motif d'un REJECTED ou d'un SUSPENDED. Nul autrement.</summary>
    public string? StatusReason { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>
    /// Photo de profil. NON SOUMISE A L'EXAMEN (ADR 0021) : elle sert au
    /// client a reconnaitre qui arrive, pas a prouver une identite. C'est
    /// IdentityPhoto, piece du dossier, qui joue ce role.
    /// </summary>
    public string? ProfilePhotoKey { get; private set; }

    /// <summary>Dernier depart a l'examen. Nul tant que rien n'a ete soumis.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    public IReadOnlyCollection<DriverDocument> Documents => _documents.AsReadOnly();

    /// <summary>Ce que ce livreur est habilite a faire.</summary>
    ///
    /// <remarks>
    /// VIDE VEUT DIRE « LE COLIS, ET RIEN D'AUTRE ». C'est la lecture de
    /// <see cref="Habilite"/> : les milliers de livreurs deja inscrits n'ont
    /// declare aucune capacite, et ils font tous du colis. Leur en attribuer une
    /// a la migration aurait ecrit une declaration que personne n'a faite ;
    /// exiger qu'ils la fassent les aurait tous arretes du jour au lendemain.
    /// </remarks>
    public IReadOnlyCollection<DriverCapability> Capabilities => _capabilities;

    /// <summary>Ce livreur peut-il faire cela, aujourd'hui, avec ce vehicule ?</summary>
    ///
    /// <remarks>
    /// LES DEUX CONDITIONS SONT NECESSAIRES, ET C'EST TOUT LE PROPOS DE CETTE
    /// METHODE. L'habilitation appartient a la PERSONNE — ops l'accorde apres
    /// examen — et la possibilite appartient au MATERIEL. Un chauffeur habilite
    /// au transport de personnes qui roule aujourd'hui en velo ne peut pas
    /// prendre de passager ; c'est le vehicule declare qui tranche, pas le
    /// dossier.
    ///
    /// LE COLIS EST LE SOCLE, ET IL N'A PAS BESOIN D'ETRE DECLARE. Un dossier
    /// vide n'est pas un livreur sans metier : c'est le metier d'origine.
    /// </remarks>
    public bool Habilite(DriverCapability capacite)
    {
        if (!Vehicle.Autorise(capacite))
        {
            return false;
        }

        return capacite == DriverCapability.PackageDelivery
            || _capabilities.Contains(capacite);
    }

    public bool CanWork => VerificationStatus == VerificationStatus.Verified;

    /// <summary>
    /// Pieces exigees d'un vehicule a moteur, avant l'examen.
    ///
    /// LA CARTE GRISE ET LA PHOTO DU VEHICULE SONT DANS LA LISTE parce que le
    /// referentiel place la carte grise parmi les pieces KYC, et qu'ops ne
    /// peut verifier une plaque qu'en la voyant.
    /// </summary>
    public static readonly IReadOnlySet<DocumentType> PiecesDUnVehiculeAMoteur = new HashSet<DocumentType>
    {
        DocumentType.NationalId,
        DocumentType.DrivingLicence,
        DocumentType.VehicleRegistration,
        DocumentType.IdentityPhoto,
        DocumentType.VehiclePhoto,
    };

    /// <summary>
    /// Pieces exigees d'un velo.
    /// </summary>
    ///
    /// <remarks>
    /// NI PERMIS NI CARTE GRISE, ET CE N'EST PAS UNE FACILITE : un velo n'en a
    /// pas. Les exiger fermait ce type de livreur avant meme de l'ouvrir.
    /// Tranche le 30 septembre 2026 (point 28).
    ///
    /// LA PHOTO DU VEHICULE RESTE, et c'est ce qui distingue cette liste du
    /// minimum absolu : sans elle, ops ne voit jamais l'engin, ne peut pas
    /// ecarter un velo hors d'etat, et n'a rien pour reconnaitre le vehicule le
    /// jour d'une reclamation.
    /// </remarks>
    public static readonly IReadOnlySet<DocumentType> PiecesDUnVelo = new HashSet<DocumentType>
    {
        DocumentType.NationalId,
        DocumentType.IdentityPhoto,
        DocumentType.VehiclePhoto,
    };

    /// <summary>
    /// Ce qu'on exige de CE vehicule-la.
    /// </summary>
    ///
    /// <remarks>
    /// LA LISTE DEPEND DU VEHICULE DEPUIS LE 30 SEPTEMBRE 2026. Elle etait
    /// unique, et ce n'etait pas un oubli : tant que tous les vehicules avaient
    /// un moteur, une plaque et des papiers, une seule liste disait la verite.
    /// Le velo est le premier type dont ce n'est pas vrai.
    ///
    /// LE TRICYCLE N'EST PAS UNE EXCEPTION : c'est un trois-roues motorise,
    /// immatricule comme tel. Meme liste que la moto.
    ///
    /// LE DEFAUT EST LA LISTE COMPLETE, et c'est le bon sens du refus : un type
    /// de vehicule qu'on n'aurait pas pense a traiter ici se verrait demander
    /// TROP de pieces, pas trop peu. L'erreur se voit alors au guichet, pas sur
    /// la route.
    /// </remarks>
    public static IReadOnlySet<DocumentType> PiecesRequisesPour(VehicleType vehicule) =>
        vehicule == VehicleType.Bicycle ? PiecesDUnVelo : PiecesDUnVehiculeAMoteur;

    /// <summary>Pieces encore attendues. Vide, le dossier est complet.</summary>
    public IReadOnlyList<DocumentType> PiecesManquantes =>
        PiecesRequisesPour(Vehicle.Type)
            .Where(type => !_documents.Exists(d => d.Type == type))
            .ToList();

    /// <summary>
    /// Le dossier accepte-t-il encore des modifications ?
    ///
    /// UN DOSSIER VALIDE SE FIGE. Laisser remplacer une CNI apres validation
    /// reviendrait a valider une personne et a en laisser travailler une
    /// autre. La correction d'un dossier valide passe par ops, qui suspend
    /// d'abord.
    /// </summary>
    public bool DossierModifiable =>
        VerificationStatus is VerificationStatus.PendingVerification or VerificationStatus.Rejected;

    public static DriverAggregate Register(
        Guid id,
        string displayName,
        string phone,
        Vehicle vehicle,
        Actor actor,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new DomainException(DriverErrorCodes.MissingPhone, "Un livreur est joignable : le telephone est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException(DriverErrorCodes.MissingDisplayName, "Un livreur porte un nom : le client le verra.");
        }

        var driver = new DriverAggregate(id, displayName.Trim(), phone.Trim(), vehicle, registeredAt);
        driver.Raise(new DriverRegistered(id, driver.Phone, actor, registeredAt));

        return driver;
    }

    // ------------------------------------------------- Constitution du dossier --

    /// <summary>
    /// Depose ou remplace une piece.
    ///
    /// LE REMPLACEMENT REND L'ANCIENNE CLE, il ne l'efface pas : effacer un
    /// objet depuis le domaine reviendrait a lui donner un effet de bord hors
    /// de la transaction. L'appelant supprime apres que la base a valide — et
    /// s'il echoue, il reste un objet orphelin, ce qui est preferable a une
    /// ligne qui pointe vers un objet disparu.
    /// </summary>
    /// <returns>La cle de la piece remplacee, ou null si c'est un premier depot.</returns>
    public string? AttachDocument(DriverDocument document, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(actor);

        ExigerDossierModifiable();

        var precedente = _documents.Find(d => d.Type == document.Type);
        string? cleRemplacee = null;

        // REDEPOSER, C'EST MODIFIER LA PIECE, PAS EN CREER UNE AUTRE. Il y a
        // une piece par nature et par livreur : la ligne existe deja, elle
        // change de contenu. Voir DriverDocument.ReplaceWith pour ce que
        // coutait le retrait suivi d'un ajout.
        if (precedente is not null)
        {
            cleRemplacee = precedente.ReplaceWith(document);
        }
        else
        {
            _documents.Add(document);
        }

        Raise(new DriverDocumentAttached(Id, document.Type, document.ObjectKey, actor, now));

        return cleRemplacee;
    }

    /// <summary>
    /// Declaration ou changement du vehicule par le livreur.
    ///
    /// L'IMMATRICULATION EST OBLIGATOIRE ICI, alors que Vehicle.Create
    /// l'accepte vide. Ce n'est pas une contradiction : Vehicle.Unknown nait
    /// sans plaque a l'inscription, parce que le compte precede le dossier.
    /// Mais un livreur qui DECLARE son vehicule et laisse la plaque vide ne
    /// declare rien qu'ops puisse verifier.
    /// </summary>
    ///
    /// <remarks>
    /// UN DOSSIER VALIDE ACCEPTE DESORMAIS CE CHANGEMENT, ET LE PAIE. Jusqu'au
    /// 3 octobre 2026, ExigerDossierModifiable le refusait net : un livreur qui
    /// changeait de vehicule — il en achete un, le sien tombe en panne, il
    /// emprunte la voiture d'un frere — devait appeler HBA pour faire rouvrir
    /// son dossier. Le refus avait une bonne raison et un mauvais effet : la
    /// bonne raison, c'est que la carte grise au dossier atteste CE vehicule-la
    /// et aucun autre ; le mauvais effet, c'est qu'un livreur bloque continue de
    /// rouler avec un vehicule qui ne correspond plus a rien.
    ///
    /// LE CHANGEMENT RENVOIE DONC LE DOSSIER A L'EXAMEN. Le livreur modifie
    /// librement, et perd sa validation dans le meme geste : il ne recevra plus
    /// d'offre (CanWork exige Verified) tant qu'ops n'aura pas revu les pieces
    /// du nouveau vehicule. Rien n'est contourne — la verification est
    /// REPORTEE, pas supprimee — et c'est le livreur, pas HBA, qui decide quand
    /// la payer.
    ///
    /// CE QUI RESTE INTERDIT : changer pendant une mission. Le client attend le
    /// vehicule qu'on lui a annonce, et son ecran de suivi le nomme.
    ///
    /// LES PIECES NE SONT PAS EFFACEES, et c'est volontaire. Un livreur qui
    /// corrige une couleur ne doit pas redeposer sa CNI. PiecesManquantes
    /// recalcule ce qu'il faut pour le NOUVEAU type — passer du velo a la moto
    /// reclame d'un coup permis et carte grise — et SubmitForReview refusera
    /// tant qu'elles manquent.
    /// </remarks>
    public void DeclareVehicle(Vehicle vehicle, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(actor);

        // LA SUSPENSION RESTE UN MUR. Un compte suspendu ne se repare pas en
        // changeant de vehicule : c'est la personne qui est ecartee, pas son
        // materiel.
        if (VerificationStatus == VerificationStatus.Suspended)
        {
            throw new DomainException(
                DriverErrorCodes.ApplicationNotEditable,
                "Ce compte est suspendu : contactez HBA.");
        }

        if (OperationalStatus is OperationalStatus.Reserved or OperationalStatus.OnMission)
        {
            throw new DomainException(
                DriverErrorCodes.VehicleLockedDuringMission,
                "Terminez votre mission avant de changer de vehicule : le client "
                + "attend celui qui lui a ete annonce.");
        }

        // ELLE RESTE OBLIGATOIRE POUR TOUT CE QUI A UN MOTEUR. Ce qui a change
        // le 30 septembre 2026, c'est qu'un velo n'en a pas : exiger sa plaque
        // fermait ce type de livreur avant meme de l'ouvrir.
        if (vehicle.ExigeUnePlaque && string.IsNullOrWhiteSpace(vehicle.Plate))
        {
            throw new DomainException(
                DriverErrorCodes.MissingVehiclePlate,
                "L'immatriculation est obligatoire : ops la compare a la carte grise.");
        }

        var etaitValide = VerificationStatus == VerificationStatus.Verified;

        Vehicle = vehicle;

        if (etaitValide)
        {
            // IL REDEVIENT EN ATTENTE, ET IL PASSE HORS LIGNE. Le laisser
            // « disponible » avec un dossier non valide afficherait un livreur
            // de plus sur la carte des clients pour quelqu'un qui ne peut rien
            // recevoir.
            VerificationStatus = VerificationStatus.PendingVerification;
            StatusReason = null;
            SubmittedAt = null;

            if (OperationalStatus == OperationalStatus.Available)
            {
                OperationalStatus = OperationalStatus.Offline;
            }
        }

        // LES HABILITATIONS NE SONT PAS TOUCHEES, ET C'EST UNE REGLE DEJA ECRITE
        // AILLEURS : voir « Changer_de_vehicule_retire_ce_que_le_nouveau_ne_permet_pas »
        // dans CapacitesTests. Elles restent au dossier et cessent simplement
        // d'etre exercables — c'est Habilite() qui croise le declare et le
        // materiel au moment du dispatch. Le livreur qui reprend sa moto demain
        // n'a donc rien a redemander.
        Raise(new DriverVehicleDeclared(Id, vehicle.Type, vehicle.Plate, etaitValide, actor, now));
    }

    /// <summary>
    /// Photo de profil.
    ///
    /// ELLE SE CHANGE A TOUT MOMENT, meme dossier valide, et NE ROUVRE PAS
    /// L'EXAMEN (ADR 0021). Elle n'est pas une piece : elle sert au client a
    /// reconnaitre qui arrive. L'identite, elle, est prouvee par la CNI et le
    /// permis, qui ne bougent plus une fois le dossier valide.
    /// </summary>
    /// <returns>La cle de la photo remplacee, ou null s'il n'y en avait pas.</returns>
    public string? SetProfilePhoto(string objectKey, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new DomainException(
                DriverErrorCodes.MissingObjectKey,
                "Une photo sans cle de stockage serait introuvable.");
        }

        var precedente = ProfilePhotoKey;
        ProfilePhotoKey = objectKey.Trim();

        Raise(new DriverProfilePhotoChanged(Id, ProfilePhotoKey, actor, now));

        return precedente;
    }

    /// <summary>
    /// Le livreur envoie son dossier a l'examen.
    ///
    /// COMPLET, OU RIEN (ADR 0021). Accepter un dossier partiel ferait
    /// travailler ops pour le rejeter, et le livreur pour recommencer. Le
    /// refus nomme ce qui manque, sinon il est inutilisable.
    ///
    /// APRES UN REJET, ON REPART : le statut retombe en attente et le motif
    /// precedent s'efface. Sans cela, un rejet serait definitif et la seule
    /// issue serait un second compte pour la meme personne, avec la meme CNI.
    ///
    /// UN COMPTE SUSPENDU NE SE DEBLOQUE PAS AINSI. La suspension est une
    /// decision d'ops sur la personne, pas un verdict sur des papiers :
    /// resoumettre ne doit pas la lever. Seul ops la leve.
    /// </summary>
    /// <summary>
    /// Declare ce que ce livreur veut pouvoir faire.
    /// </summary>
    ///
    /// <remarks>
    /// REMPLACE, N'AJOUTE PAS. Une declaration dit l'ensemble de ce que le
    /// livreur exerce, pas un ajout a une liste dont il aurait perdu le compte.
    /// Retirer une habilitation doit etre aussi simple que l'accorder — sinon
    /// elle ne se retire jamais.
    ///
    /// CHAQUE CAPACITE EST CONFRONTEE AU VEHICULE DECLARE, et le refus nomme
    /// celle qui bloque. Sans cela, un cycliste cochant « transport de
    /// personnes » verrait sa declaration acceptee et n'en comprendrait les
    /// consequences qu'en ne recevant jamais la moindre course de personnes —
    /// un silence impossible a diagnostiquer depuis un telephone.
    ///
    /// ON NE VERIFIE PAS LA CAPACITE EN PLACES ICI, seulement le TYPE. Un
    /// livreur peut s'habiliter au transport de personnes avant d'avoir declare
    /// combien sa voiture en porte ; c'est le dispatch qui exigera les deux, par
    /// Vehicle.PeutTransporterDesPersonnes. Refuser des maintenant obligerait a
    /// remplir les deux ecrans dans un ordre precis, sans le dire.
    ///
    /// UN DOSSIER VALIDE SE DECLARE LIBREMENT, ET SANS REPASSER A L'EXAMEN.
    /// Tranche le 3 octobre 2026. La difference avec le vehicule est entiere :
    /// changer de vehicule produit un objet que PERSONNE n'a vu, alors que
    /// cocher « transport de personnes » ne fait qu'exercer ce que le vehicule
    /// DEJA VERIFIE autorise — la table de Vehicle.Autorise s'en porte garante,
    /// et elle est verifiee ci-dessous. Un livreur en voiture validee qui veut
    /// prendre des passagers n'apporte aucune piece nouvelle a examiner.
    ///
    /// LA SUSPENSION RESTE UN MUR, comme pour le vehicule : c'est la personne
    /// qui est ecartee.
    /// </remarks>
    public void DeclareCapabilities(
        IReadOnlyCollection<DriverCapability> capabilities,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(actor);

        if (VerificationStatus == VerificationStatus.Suspended)
        {
            throw new DomainException(
                DriverErrorCodes.ApplicationNotEditable,
                "Ce compte est suspendu : contactez HBA.");
        }

        var demandees = capabilities
            .Where(c => c != DriverCapability.Unspecified)
            .ToHashSet();

        foreach (var capacite in demandees)
        {
            if (!Vehicle.Autorise(capacite))
            {
                throw new DomainException(
                    DriverErrorCodes.CapabilityNotAllowedByVehicle,
                    $"Un vehicule de type {Vehicle.Type} ne permet pas : {capacite}.");
            }
        }

        _capabilities.Clear();

        foreach (var capacite in demandees)
        {
            _capabilities.Add(capacite);
        }

        Raise(new DriverCapabilitiesDeclared(Id, [.. demandees], actor, now));
    }

    public void SubmitForReview(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (VerificationStatus == VerificationStatus.Suspended)
        {
            throw new DomainException(
                DriverErrorCodes.DriverSuspended,
                "Ce compte est suspendu. Contactez HBA : resoumettre un dossier ne le debloque pas.");
        }

        if (VerificationStatus == VerificationStatus.Verified)
        {
            throw new DomainException(
                DriverErrorCodes.ApplicationNotEditable,
                "Ce dossier est deja valide.");
        }

        var manquantes = PiecesManquantes;

        if (manquantes.Count > 0)
        {
            throw new DomainException(
                DriverErrorCodes.IncompleteApplication,
                "Dossier incomplet. Pieces manquantes : "
                + string.Join(", ", manquantes.Select(t => t.ToString())) + ".");
        }

        // « DECLARE » ET NON « A UNE PLAQUE ». Un velo n'en a pas, et la
        // condition d'origine l'aurait retenu au guichet pour toujours. Voir
        // Vehicle.EstDeclare : ce qui rend la regle sure, c'est qu'aucun
        // vehicule ne NAIT sans plaque — le defaut est une moto.
        if (!VehiculeDeclare)
        {
            throw new DomainException(
                DriverErrorCodes.MissingVehiclePlate,
                "Le vehicule doit etre declare avant l'examen.");
        }

        VerificationStatus = VerificationStatus.PendingVerification;
        StatusReason = null;
        SubmittedAt = now;

        Raise(new DriverApplicationSubmitted(Id, actor, now));
    }

    private void ExigerDossierModifiable()
    {
        if (DossierModifiable)
        {
            return;
        }

        var raison = VerificationStatus == VerificationStatus.Suspended
            ? "Ce compte est suspendu : contactez HBA."
            : "Ce dossier est valide et ne se modifie plus. Contactez HBA pour le rouvrir.";

        throw new DomainException(DriverErrorCodes.ApplicationNotEditable, raison);
    }

    /// <summary>
    /// Decision KYC d'un administrateur. Auditee : l'evenement porte qui a
    /// decide et pourquoi.
    /// </summary>
    public void ReviewKyc(bool approved, string? reason, string reviewedBy, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!approved && string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                DriverErrorCodes.MissingRejectionReason,
                "Un refus de dossier doit etre motive : le livreur doit savoir quoi corriger.");
        }

        if (VerificationStatus == VerificationStatus.Suspended)
        {
            throw new InvalidStateTransitionException(
                nameof(DriverAggregate),
                VerificationStatus.ToString(),
                approved ? nameof(VerificationStatus.Verified) : nameof(VerificationStatus.Rejected),
                actor.Kind);
        }

        VerificationStatus = approved ? VerificationStatus.Verified : VerificationStatus.Rejected;
        StatusReason = approved ? null : reason;
        VerifiedAt = approved ? now : null;

        // UN DOSSIER REFUSE NE LAISSE PERSONNE EN LIGNE. Sans cette ligne, un
        // livreur deja connecte continuerait a recevoir des offres apres le
        // refus, jusqu'a ce qu'il ferme son application de lui-meme.
        if (!approved)
        {
            ForceOffline(actor, now);
        }

        Raise(new DriverKycReviewed(Id, VerificationStatus, reason ?? string.Empty, reviewedBy, actor, now));
    }

    /// <summary>
    /// Ops leve une suspension.
    /// </summary>
    ///
    /// <remarks>
    /// SANS CETTE METHODE, UNE SUSPENSION ETAIT DEFINITIVE. Le commentaire de
    /// SubmitForReview promet pourtant « Seul ops la leve » : personne ne la
    /// levait. Une fois suspendu, ReviewKyc levait une transition invalide,
    /// SubmitForReview refusait, AttachDocument et DeclareVehicle refusaient,
    /// GoOnline refusait. Un livreur suspendu par erreur etait exclu a vie, et
    /// la seule reparation passait par la base.
    ///
    /// LE STATUT RETROUVE SE DEDUIT, IL NE S'INVENTE PAS. VerifiedAt n'est pose
    /// qu'a une validation et efface a un refus : s'il est la, le dossier etait
    /// valide avant la suspension et le redevient ; sinon le livreur repasse en
    /// attente d'examen et resoumet. Retenir le statut d'avant aurait demande
    /// une colonne de plus pour la meme information.
    ///
    /// LE LIVREUR RESTE HORS LIGNE. La suspension l'avait force hors ligne ;
    /// c'est a lui de revenir, pas au back-office de le remettre au travail.
    /// </remarks>
    public void Reinstate(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                DriverErrorCodes.MissingRejectionReason,
                "Une levee de suspension doit etre motivee : l'audit doit dire pourquoi.");
        }

        if (VerificationStatus != VerificationStatus.Suspended)
        {
            throw new DomainException(
                DriverErrorCodes.ApplicationNotEditable,
                "Ce compte n'est pas suspendu.");
        }

        VerificationStatus = VerifiedAt is null
            ? VerificationStatus.PendingVerification
            : VerificationStatus.Verified;

        StatusReason = reason;

        Raise(new DriverKycReviewed(Id, VerificationStatus, reason, actor.Id, actor, now));
    }

    public void Suspend(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                DriverErrorCodes.MissingRejectionReason,
                "Une suspension doit etre motivee.");
        }

        VerificationStatus = VerificationStatus.Suspended;
        StatusReason = reason;

        ForceOffline(actor, now);

        Raise(new DriverKycReviewed(Id, VerificationStatus.Suspended, reason, actor.Id, actor, now));
    }

    /// <summary>
    /// Passage en ligne.
    ///
    /// LE DOSSIER DOIT ETRE VERIFIE — ET CETTE REGLE N'EST PAS ECRITE DANS LE
    /// REFERENTIEL. Elle s'en deduit : les statuts de verification existent
    /// pour decider qui peut travailler, et proposer une course a un livreur
    /// dont les papiers n'ont pas ete vus reviendrait a confier un colis a un
    /// inconnu. Le refus est le defaut sur : a confirmer explicitement, et a
    /// retirer d'une ligne si la decision est autre.
    /// </summary>
    public void GoOnline(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (VerificationStatus == VerificationStatus.Suspended)
        {
            throw new DomainException(DriverErrorCodes.DriverSuspended, "Ce compte livreur est suspendu.");
        }

        if (!CanWork)
        {
            throw new DomainException(
                DriverErrorCodes.DriverNotVerified,
                "Le dossier n'est pas encore verifie : impossible de passer en ligne.");
        }

        if (OperationalStatus is OperationalStatus.Available or OperationalStatus.Reserved or OperationalStatus.OnMission)
        {
            return;
        }

        Transition(OperationalStatus.Available, actor, now);
    }

    /// <summary>
    /// Passage hors ligne.
    ///
    /// PAS PENDANT UNE MISSION. Un colis est entre ses mains : disparaitre
    /// laisserait une course sans livreur et sans trace. La sortie passe par
    /// la fin de mission, ou par une reaffectation forcee de ops.
    /// </summary>
    public void GoOffline(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (OperationalStatus == OperationalStatus.OnMission)
        {
            throw new InvalidStateTransitionException(
                nameof(DriverAggregate),
                OperationalStatus.ToString(),
                nameof(OperationalStatus.Offline),
                actor.Kind);
        }

        if (OperationalStatus == OperationalStatus.Offline)
        {
            return;
        }

        Transition(OperationalStatus.Offline, actor, now);
    }

    /// <summary>
    /// Reserve le livreur pour une offre. UNE SEULE A LA FOIS : c'est la regle
    /// du referentiel, et elle se verifie ici en plus du verrou Redis du
    /// dispatch — la base est la seule a pouvoir trancher si le verrou expire
    /// au mauvais moment.
    /// </summary>
    public void Reserve(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (OperationalStatus == OperationalStatus.Reserved)
        {
            throw new DomainException(DriverErrorCodes.DriverAlreadyReserved, "Ce livreur porte deja une offre.");
        }

        if (OperationalStatus != OperationalStatus.Available)
        {
            throw new DomainException(DriverErrorCodes.DriverNotAvailable, "Ce livreur n'est pas disponible.");
        }

        Transition(OperationalStatus.Reserved, actor, now);
    }

    /// <summary>Offre refusee ou expiree : le livreur redevient disponible.</summary>
    public void ReleaseReservation(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (OperationalStatus != OperationalStatus.Reserved)
        {
            return;
        }

        Transition(OperationalStatus.Available, actor, now);
    }

    public void StartMission(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (OperationalStatus is not (OperationalStatus.Reserved or OperationalStatus.Available))
        {
            throw new InvalidStateTransitionException(
                nameof(DriverAggregate),
                OperationalStatus.ToString(),
                nameof(OperationalStatus.OnMission),
                actor.Kind);
        }

        Transition(OperationalStatus.OnMission, actor, now);
    }

    public void CompleteMission(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (OperationalStatus != OperationalStatus.OnMission)
        {
            return;
        }

        Transition(OperationalStatus.Available, actor, now);
    }

    private void ForceOffline(Actor actor, DateTimeOffset now)
    {
        if (OperationalStatus != OperationalStatus.Offline)
        {
            Transition(OperationalStatus.Offline, actor, now);
        }
    }

    private void Transition(OperationalStatus next, Actor actor, DateTimeOffset now)
    {
        var previous = OperationalStatus;
        OperationalStatus = next;

        Raise(new DriverStatusChanged(Id, previous, next, actor, now));
    }
}

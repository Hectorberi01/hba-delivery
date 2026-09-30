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
    /// Declaration du vehicule par le livreur.
    ///
    /// L'IMMATRICULATION DEVIENT OBLIGATOIRE ICI, alors que Vehicle.Create
    /// l'accepte vide. Ce n'est pas une contradiction : Vehicle.Unknown nait
    /// sans plaque a l'inscription, parce que le compte precede le dossier.
    /// Mais un livreur qui DECLARE son vehicule et laisse la plaque vide ne
    /// declare rien qu'ops puisse verifier.
    /// </summary>
    public void DeclareVehicle(Vehicle vehicle, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(actor);

        ExigerDossierModifiable();

        // ELLE RESTE OBLIGATOIRE POUR TOUT CE QUI A UN MOTEUR. Ce qui a change
        // le 30 septembre 2026, c'est qu'un velo n'en a pas : exiger sa plaque
        // fermait ce type de livreur avant meme de l'ouvrir.
        if (vehicle.ExigeUnePlaque && string.IsNullOrWhiteSpace(vehicle.Plate))
        {
            throw new DomainException(
                DriverErrorCodes.MissingVehiclePlate,
                "L'immatriculation est obligatoire : ops la compare a la carte grise.");
        }

        Vehicle = vehicle;

        Raise(new DriverVehicleDeclared(Id, vehicle.Type, vehicle.Plate, actor, now));
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

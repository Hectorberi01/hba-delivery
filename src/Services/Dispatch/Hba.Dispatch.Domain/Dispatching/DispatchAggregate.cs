using Hba.BuildingBlocks.Domain;
using Hba.Dispatch.Domain.Dispatching.Events;
using Hba.Dispatch.Domain.Exceptions;
using Hba.Dispatch.Domain.ValueObjects;

namespace Hba.Dispatch.Domain.Dispatching;

/// <summary>
/// La recherche d'un livreur pour UNE livraison.
///
/// POURQUOI UN AGREGAT PAR LIVRAISON, et non un agregat par offre : les
/// invariants qui comptent portent sur l'ensemble. « Au plus un gagnant »,
/// « ne pas reproposer a quelqu'un qui a deja refuse », « la troisieme vague
/// est la derniere » sont des regles sur la collection, pas sur une offre
/// isolee. Les disperser sur des agregats separes obligerait a les faire
/// respecter par une transaction distribuee ou, plus vraisemblablement, a ne
/// pas les faire respecter du tout.
/// </summary>
public sealed class DispatchAggregate : AggregateRoot
{
    /// <summary>
    /// Numero de vague des offres posees a la main. Zero n'est jamais rendu
    /// par le moteur, dont les vagues sont numerotees a partir de un.
    /// </summary>
    public const int ManualWave = 0;

    private readonly List<Offer> _offers = [];

    private DispatchAggregate()
    {
    }

    private DispatchAggregate(
        Guid id,
        Guid deliveryId,
        GeoPoint pickup,
        string pickupLandmark,
        int tripDistanceMeters,
        long driverEarningXof,
        DateTimeOffset createdAt)
        : base(id)
    {
        DeliveryId = deliveryId;
        Pickup = pickup;
        PickupLandmark = pickupLandmark;
        TripDistanceMeters = tripDistanceMeters;
        DriverEarningXof = driverEarningXof;
        Status = DispatchStatus.Searching;
        CurrentWave = 0;
        CreatedAt = createdAt;
    }

    public Guid DeliveryId { get; private set; }

    public GeoPoint Pickup { get; private set; } = null!;

    /// <summary>Repere de collecte, seul element d'adresse visible avant acceptation.</summary>
    public string PickupLandmark { get; private set; } = string.Empty;

    public int TripDistanceMeters { get; private set; }

    /// <summary>Part du livreur, figee par Pricing. Dispatch ne la calcule pas.</summary>
    public long DriverEarningXof { get; private set; }

    public DispatchStatus Status { get; private set; }

    /// <summary>Numero de la derniere vague ouverte. Zero avant la premiere.</summary>
    public int CurrentWave { get; private set; }

    /// <summary>
    /// Instant d'ouverture de la vague en cours. Nul avant la premiere.
    ///
    /// IL SERT A TENIR LA PROMESSE DE DUREE. Une vague sans candidat est
    /// close a l'instant meme ou elle s'ouvre : sans cette date, la suivante
    /// partirait aussitot, et les trois vagues s'epuiseraient en quelques
    /// secondes au lieu des quatre-vingt-dix annonces. Or ces secondes ont
    /// une valeur : un livreur peut passer en ligne pendant ce temps.
    /// </summary>
    public DateTimeOffset? CurrentWaveOpenedAt { get; private set; }

    public string? AssignedDriverId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyCollection<Offer> Offers => _offers.AsReadOnly();

    public bool IsOpen => Status == DispatchStatus.Searching;

    /// <summary>
    /// Livreurs deja sollicites, toutes vagues confondues. Sert de liste
    /// d'exclusion : reproposer la meme course a quelqu'un qui vient de la
    /// laisser passer gaspille une place dans la vague suivante.
    /// </summary>
    public IReadOnlySet<string> SolicitedDriverIds
        => _offers.Select(o => o.DriverId).ToHashSet(StringComparer.Ordinal);

    public static DispatchAggregate Start(
        Guid id,
        Guid deliveryId,
        GeoPoint pickup,
        string? pickupLandmark,
        int tripDistanceMeters,
        long driverEarningXof,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(pickup);

        return new DispatchAggregate(
            id,
            deliveryId,
            pickup,
            pickupLandmark?.Trim() ?? string.Empty,
            tripDistanceMeters,
            driverEarningXof,
            createdAt);
    }

    /// <summary>
    /// Ouvre une vague et emet une offre par livreur retenu.
    ///
    /// UNE VAGUE VIDE N'EST PAS UNE ERREUR : personne autour, a ce rayon, a cet
    /// instant. Elle compte quand meme comme tentative — sans quoi une zone
    /// deserte ferait tourner le moteur indefiniment.
    /// </summary>
    public IReadOnlyList<Offer> OpenWave(
        IReadOnlyCollection<CandidateDriver> candidates,
        TimeSpan offerLifetime,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(actor);

        EnsureOpen();

        CurrentWave++;
        CurrentWaveOpenedAt = now;

        var expiresAt = now.Add(offerLifetime);
        var nouvelles = new List<Offer>(candidates.Count);

        foreach (var candidate in candidates)
        {
            // Deuxieme filet apres la liste d'exclusion envoyee a Driver : la
            // recherche est asynchrone, et deux vagues rapprochees peuvent se
            // croiser.
            if (SolicitedDriverIds.Contains(candidate.DriverId))
            {
                continue;
            }

            var offer = new Offer(
                candidate.DriverId,
                CurrentWave,
                candidate.DistanceToPickupMeters,
                now,
                expiresAt);

            _offers.Add(offer);
            nouvelles.Add(offer);

            Raise(new OfferSent(DeliveryId, offer.Id, offer.DriverId, CurrentWave, expiresAt, actor, now));
        }

        return nouvelles;
    }

    /// <summary>
    /// L'exploitation propose la course a UN livreur choisi a la main.
    ///
    /// ELLE N'OUVRE PAS DE VAGUE, ET CE N'EST PAS UN DETAIL DE COMPTABILITE.
    /// Une vague est un tour du moteur : un rayon, un budget de livreurs, un
    /// delai au terme duquel on passe au rayon suivant. Un humain qui designe
    /// une personne ne fait rien de tout cela. Incrementer CurrentWave
    /// consommerait un des trois tours pour un geste qui n'en est pas un, et
    /// la course perdrait un rayon de recherche pour rien.
    ///
    /// L'OFFRE PORTE DONC LA VAGUE ZERO, qui se lit « a la main » partout
    /// ailleurs — dans le contrat, dans l'audit, et dans la statistique par
    /// vague, ou leur nombre dit exactement ce que le moteur n'a pas su faire
    /// seul.
    ///
    /// LA LISTE D'EXCLUSION NE S'APPLIQUE PAS ICI. Elle existe parce que
    /// reproposer une course a quelqu'un qui vient de la laisser passer
    /// gaspille une place dans la vague suivante ; une offre manuelle n'a pas
    /// de place a gaspiller. Seule une offre ENCORE EN ATTENTE fait obstacle,
    /// et pour une raison differente : deux offres vivantes pour la meme
    /// course chez la meme personne lui donneraient deux boutons pour un seul
    /// travail.
    ///
    /// LA DISPONIBILITE N'EST PAS VERIFIEE ICI. Dispatch ne sait ni ou sont
    /// les livreurs ni qui est libre — c'est la raison d'etre de IDriverFinder.
    /// La distance arrive donc mesuree du dehors, et l'appelant a deja
    /// demande a Driver si ce livreur pouvait recevoir une offre.
    /// </summary>
    public Offer OfferTo(
        string driverId,
        int distanceToPickupMeters,
        TimeSpan offerLifetime,
        Actor actor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(driverId))
        {
            throw new ArgumentException("Une offre manuelle vise un livreur.", nameof(driverId));
        }

        EnsureOpen();

        if (_offers.Any(o => o.IsPending && string.Equals(o.DriverId, driverId, StringComparison.Ordinal)))
        {
            throw new DomainException(
                DispatchErrorCodes.AlreadyOffered,
                "Ce livreur a deja une offre en attente sur cette course.");
        }

        var offer = new Offer(driverId, ManualWave, distanceToPickupMeters, now, now.Add(offerLifetime));

        _offers.Add(offer);

        Raise(new OfferSent(DeliveryId, offer.Id, offer.DriverId, ManualWave, offer.ExpiresAt, actor, now));

        return offer;
    }

    /// <summary>
    /// Un livreur accepte.
    ///
    /// LE VERROU REDIS N'EST PAS SUFFISANT A LUI SEUL, et cet agregat non plus.
    /// Le verrou empeche deux acceptations simultanees de meme se presenter
    /// ici ; le jeton de concurrence optimiste de la base tranche si le verrou
    /// a expire entre-temps ; et ce test-ci refuse le cas ou les deux auraient
    /// laisse passer. Trois barrieres pour une regle dont la violation
    /// donnerait une course a deux livreurs.
    /// </summary>
    public Offer Accept(Guid offerId, string driverId, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status == DispatchStatus.Assigned)
        {
            throw new DomainException(DispatchErrorCodes.AlreadyTaken, "Cette course a deja trouve son livreur.");
        }

        EnsureOpen();

        var offer = _offers.SingleOrDefault(o => o.Id == offerId)
            ?? throw new DomainException(DispatchErrorCodes.OfferNotFound, "Offre introuvable.");

        if (!string.Equals(offer.DriverId, driverId, StringComparison.Ordinal))
        {
            // NI « DEJA PRISE », NI « EXPIREE » : un livreur ne doit pas
            // apprendre, par le code d'erreur, qu'une offre existe pour
            // quelqu'un d'autre.
            throw new DomainException(DispatchErrorCodes.NotYourOffer, "Cette offre n'est pas la votre.");
        }

        offer.Accept(now);

        foreach (var autre in _offers.Where(o => o.Id != offerId && o.IsPending))
        {
            autre.Supersede(now);
            Raise(new OfferSuperseded(DeliveryId, autre.Id, autre.DriverId, actor, now));
        }

        Status = DispatchStatus.Assigned;
        AssignedDriverId = driverId;
        ClosedAt = now;

        Raise(new OfferAccepted(DeliveryId, offer.Id, driverId, actor, now));

        return offer;
    }

    public void Decline(Guid offerId, string driverId, string? reason, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var offer = _offers.SingleOrDefault(o => o.Id == offerId)
            ?? throw new DomainException(DispatchErrorCodes.OfferNotFound, "Offre introuvable.");

        if (!string.Equals(offer.DriverId, driverId, StringComparison.Ordinal))
        {
            throw new DomainException(DispatchErrorCodes.NotYourOffer, "Cette offre n'est pas la votre.");
        }

        if (!offer.IsPending)
        {
            return;
        }

        offer.Decline(reason, now);
        Raise(new OfferDeclined(DeliveryId, offer.Id, driverId, reason, actor, now));
    }

    /// <summary>
    /// Eteint les offres echues. Appele par le planificateur, que le
    /// referentiel designe pour les « timeouts d'offre ».
    /// </summary>
    public int ExpireDueOffers(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var echues = _offers.Where(o => o.IsPending && o.HasExpiredAt(now)).ToList();

        foreach (var offer in echues)
        {
            offer.Expire(now);
            Raise(new OfferExpired(DeliveryId, offer.Id, offer.DriverId, actor, now));
        }

        return echues.Count;
    }

    /// <summary>
    /// La vague en cours est-elle close, donc la suivante ouvrable ?
    ///
    /// DEUX CONDITIONS, PAS UNE. Plus aucune offre en attente — refusees,
    /// expirees — ET le delai de la vague ecoule. La seconde est ce qui
    /// distingue « personne n'a repondu en trente secondes » de « personne
    /// n'etait la a la seconde ou on a regarde ».
    ///
    /// TOUTE OFFRE EN ATTENTE RETIENT LE MOTEUR, PAS SEULEMENT CELLES DE LA
    /// VAGUE COURANTE. Une offre posee a la main porte la vague zero : la
    /// filtrer sur CurrentWave la rendrait invisible ici, et le moteur
    /// ouvrirait une vague par-dessus le choix de l'exploitation — la
    /// personne designee se ferait doubler par la machine qu'on venait de
    /// court-circuiter. Elle retient donc le moteur le temps de son delai,
    /// pas davantage : ExpireDueOffers l'eteint comme les autres, et la
    /// recherche reprend son cours.
    /// </summary>
    public bool WaveIsSettled(TimeSpan offerLifetime, DateTimeOffset now)
    {
        if (_offers.Any(o => o.IsPending))
        {
            return false;
        }

        if (CurrentWave == 0)
        {
            return true;
        }

        return CurrentWaveOpenedAt is null || now >= CurrentWaveOpenedAt.Value.Add(offerLifetime);
    }

    public void Exhaust(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status != DispatchStatus.Searching)
        {
            return;
        }

        Status = DispatchStatus.Exhausted;
        ClosedAt = now;

        Raise(new DispatchExhausted(DeliveryId, CurrentWave, actor, now));
    }

    /// <summary>
    /// La course est annulee ailleurs. Le moteur cesse de chercher, et REND LES
    /// LIVREURS QU'IL AVAIT RESERVES.
    /// </summary>
    ///
    /// <remarks>
    /// CETTE METHODE NE LEVAIT AUCUN EVENEMENT, ET ELLE IMMOBILISAIT UN LIVREUR
    /// POUR TOUJOURS.
    ///
    /// Un livreur qui recoit une offre passe RESERVED — c'est Driver qui le
    /// note, sur « OfferSent ». Il n'en ressort que sur un evenement de fin
    /// d'offre : le consommateur de Driver ne libere que sur « OfferExpired ».
    /// Ici, les offres etaient bien perimees en memoire, mais SANS RIEN DIRE a
    /// personne : le livreur restait RESERVED, et FindAvailableAsync n'accepte
    /// que « Available ». Il ne recevait plus jamais d'offre.
    ///
    /// AUCUN RATTRAPAGE N'EXISTAIT NON PLUS : le balayage des offres echues ne
    /// repasse que sur les recherches encore « Searching », et celle-ci vient
    /// de passer « Cancelled ». Refuser l'offre ne changeait rien — Decline
    /// sort en silence sur une offre qui n'est plus en attente. La seule issue
    /// etait de basculer hors ligne puis en ligne, ce que rien a l'ecran ne
    /// disait, et que l'accueil ne suggerait pas puisqu'il s'affichait
    /// toujours « en ligne ».
    ///
    /// « OfferSuperseded » EST EXACTEMENT LE FAIT QU'IL FALLAIT LEVER, et il
    /// existait deja : Accept s'en sert pour les offres des AUTRES livreurs, et
    /// le publieur le traduit sur le fil en « OfferExpired », que Driver sait
    /// consommer. Il n'y avait rien a inventer, seulement a le dire.
    ///
    /// L'ACTEUR EST CELUI QUI A ANNULE, ET IL VIENT DE L'APPELANT. Dispatch ne
    /// sait pas qui annule — client, exploitation, planificateur — et l'ecrire
    /// au hasard mentirait a l'audit.
    /// </remarks>
    public void Cancel(Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (Status is DispatchStatus.Assigned or DispatchStatus.Cancelled)
        {
            return;
        }

        foreach (var offer in _offers.Where(o => o.IsPending))
        {
            offer.Supersede(now);
            Raise(new OfferSuperseded(DeliveryId, offer.Id, offer.DriverId, actor, now));
        }

        Status = DispatchStatus.Cancelled;
        ClosedAt = now;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException(
                DispatchErrorCodes.DispatchClosed,
                $"La recherche est close ({Status}).");
        }
    }
}

/// <summary>Livreur retenu par la recherche de proximite, avec sa distance.</summary>
public sealed record CandidateDriver(string DriverId, int DistanceToPickupMeters);

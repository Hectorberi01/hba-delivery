using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Authorization;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hba.Delivery.Application.Commands.CreateDelivery;

public sealed class CreateDeliveryHandler(
    IDeliveryRepository repository,
    IPricingClient pricing,
    IPaymentClient payments,
    IBillingClient billing,
    IReferenceGenerator references,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock,
    ILogger<CreateDeliveryHandler> journal) : ICommandHandler<CreateDeliveryCommand, CreateDeliveryResult>
{
    private const string IdempotencyScope = "delivery:create";

    public async Task<CreateDeliveryResult> HandleAsync(
        CreateDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var orderer = ResolveOrderer(command);

        // Rejeu : la même clé renvoie la même livraison, sans rien recréer.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingId = await idempotency
                .TryGetResultAsync(IdempotencyScope, BuildIdempotencyKey(orderer, command.IdempotencyKey), cancellationToken)
                .ConfigureAwait(false);

            if (existingId is not null && Guid.TryParse(existingId, out var knownId))
            {
                var known = await repository.GetByIdAsync(knownId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException("Livraison", existingId);

                // CEINTURE ET BRETELLES : LA CLÉ CLOISONNE DÉJÀ, ET ON VÉRIFIE
                // QUAND MÊME. Ce chemin était le SEUL du service à rendre un
                // agrégat sans passer par DeliveryAccess ; une clé mal
                // cloisonnée — c'était le cas jusqu'à aujourd'hui — y devenait
                // directement une fuite. Le garde coûte une comparaison et
                // rend la faute impossible, quoi qu'il advienne de la clé.
                DeliveryAccess.EnsureCanRead(known, caller);

                // LE REJEU REND AUSSI L'ADRESSE DE PAIEMENT, ET SANS ELLE IL NE
                // SERVAIT A RIEN.
                //
                // Ce chemin rendait « null » : un client qui avait fermé la page
                // de paiement et qui réessayait recevait sa livraison sans
                // aucune adresse où payer, donc aucun moyen de la faire partir.
                // Elle restait en attente jusqu'à son abandon automatique, et
                // reprendre était impossible.
                //
                // REDEMANDER L'INTENTION NE FACTURE RIEN DEUX FOIS : la même clé
                // d'idempotence rend chez Payment la MEME intention et la MEME
                // adresse (CreatePaymentIntentHandler, rejeu explicite). On ne
                // rouvre donc pas une transaction, on retrouve la sienne.
                var reprise = await ReprendreIntentionAsync(known, command.IdempotencyKey!, cancellationToken)
                    .ConfigureAwait(false);

                return new CreateDeliveryResult(
                    DeliveryViewMapper.ToView(known, caller),
                    known.PaymentIntentId,
                    reprise);
            }
        }

        // Un partenaire ne crée jamais deux fois la même commande externe.
        if (!string.IsNullOrWhiteSpace(command.ExternalOrderId))
        {
            var duplicate = await repository
                .GetByExternalOrderIdAsync(orderer.PartnerId, command.ExternalOrderId, cancellationToken)
                .ConfigureAwait(false);

            if (duplicate is not null)
            {
                return new CreateDeliveryResult(
                    DeliveryViewMapper.ToView(duplicate, caller),
                    duplicate.PaymentIntentId,
                    null);
            }
        }

        // LE COMPTE EST VERIFIE AVANT QUE LE DEVIS NE SOIT CONSOMME.
        //
        // Sans cette ligne, la première course d'un donneur d'ordre sans compte
        // échouait en « compte de facturation introuvable » APRÈS avoir brûlé son
        // devis. Il en redemandait un, le brûlait aussi, et rien dans le message
        // ne disait que c'était à `finance` d'ouvrir le compte.
        //
        // ON NE PEUT PAS SIMPLEMENT INVERSER L'ORDRE : débiter avant de consommer
        // supposerait de lire le prix sans consommer le devis, ce que le contrat
        // de Pricing ne propose pas. Une vérification d'existence, elle, ne coûte
        // qu'un aller-retour vers un service qu'on appelle de toute façon juste
        // après — et seulement sur le chemin B2B.
        //
        // CE N'EST PAS UNE GARANTIE, C'EST UNE COURTOISIE. Le compte peut être
        // suspendu ou à sec entre cette vérification et le débit : c'est le débit
        // qui décide, et lui seul. Ce qu'on évite ici est le cas où l'on SAIT
        // déjà que l'appel échouera.
        await EnsureAccountExistsAsync(orderer, cancellationToken).ConfigureAwait(false);

        var deliveryId = Guid.CreateVersion7();

        // LE TRAJET PART AVEC LE DEVIS, et c'est Pricing qui refuse s'il ne
        // correspond pas au trajet chiffré. Les points sont ceux de la commande —
        // les mêmes que ceux passés à DeliveryAggregate.Create plus bas —, donc
        // ce qui est vérifié est bien ce qui sera livré.
        //
        // Le devis est consommé : le prix ne pourra plus changer pour cette course.
        var snapshot = await pricing
            .ConsumeQuoteAsync(
                command.QuoteId,
                deliveryId,
                GeoPoint.Create(command.Pickup.Latitude, command.Pickup.Longitude),
                GeoPoint.Create(command.Dropoff.Latitude, command.Dropoff.Longitude),
                cancellationToken)
            .ConfigureAwait(false);

        // LE COMPTE EST DEBITE AVANT QUE LA COURSE N'EXISTE.
        //
        // Un donneur d'ordre au plafond doit l'apprendre maintenant. Créer la
        // course puis laisser un événement la refuser lui donnerait une course
        // qui ne partira jamais, et personne pour le lui dire.
        //
        // LE DEVIS EST DEJA CONSOMME A CE STADE, et un débit refusé le brûle.
        // C'est exactement ce qui arrive déjà au client particulier dont
        // l'ouverture de paiement échoue : il en redemande un. Inverser l'ordre
        // supposerait de lire le prix sans consommer le devis, ce que le
        // contrat de Pricing ne propose pas.
        var reglement = await ReglerSurCompteAsync(orderer, deliveryId, snapshot, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await CreerAsync(command, orderer, deliveryId, snapshot, reglement, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (reglement is not null)
        {
            // LE DEBIT EST RENDU AVANT QUE L'ECHEC NE REMONTE. Sans ce
            // rattrapage, un donneur d'ordre payait une course qui n'existe
            // pas, et rien nulle part ne le signalait : le trou etait ecrit
            // noir sur blanc dans ce fichier depuis le premier jour.
            //
            // LE FILTRE « when » PORTE LA CONDITION, et non un « if » dans le
            // bloc : un client particulier n'a rien a compenser, et son echec
            // ne doit pas traverser un catch pour rien.
            await AnnulerLeReglementAsync(orderer, deliveryId).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Ecrit la course, ouvre le paiement du particulier, valide la transaction.
    /// </summary>
    ///
    /// <remarks>
    /// SEPAREE DU DEBIT A DESSEIN : tout ce qui est ici doit pouvoir echouer
    /// sous un rattrapage qui rend l'argent. Melanger les deux dans une seule
    /// methode obligeait a envelopper le debit lui-meme, dont l'echec n'a rien
    /// a compenser.
    /// </remarks>
    private async Task<CreateDeliveryResult> CreerAsync(
        CreateDeliveryCommand command,
        Orderer orderer,
        Guid deliveryId,
        PricingSnapshot snapshot,
        DeliveryAggregate.AccountSettlement? reglement,
        CancellationToken cancellationToken)
    {
        var delivery = DeliveryAggregate.Create(
            deliveryId,
            references.NextDeliveryReference(),
            command.Source,
            orderer.PartnerId,
            command.ExternalOrderId,
            orderer.CustomerId,
            orderer.MerchantId,
            PointDeCollecte(orderer, command),
            ToLocation(command.Pickup),
            ToLocation(command.Dropoff),
            Recipient.Create(command.RecipientName, command.RecipientPhone),
            snapshot,
            command.PackageDescription,
            command.PackageWeightGrams,
            caller.ToActor(),
            clock.UtcNow,
            reglement);

        PaymentIntentResult? intent = null;

        if (orderer.CustomerId is not null)
        {
            // LE PAYEUR EST CELUI QUI COMMANDE, PAS CELUI QUI REMET LE COLIS.
            //
            // Ce parametre recevait « command.Pickup.Phone » : le numero du
            // contact AU POINT DE COLLECTE. Or c'est ce numero que FedaPay
            // sollicite pour le mobile money — la demande de paiement partait
            // donc chez l'expediteur, la boutique ou le voisin qui remet le
            // colis, et le client qui commandait ne voyait jamais rien arriver.
            //
            // LE NUMERO VIENT DU JETON, ET IL EST BIEN LE SIEN : on n'entre ici
            // que par la branche « customer » de ResolveOrderer, ou
            // CustomerId EST le sujet du jeton. Un partenaire ou un commercant
            // n'y passe pas.
            var payeur = caller.Phone;

            if (string.IsNullOrWhiteSpace(payeur))
            {
                // UN REFUS QUI NOMME LA CAUSE, PLUTOT QU'UN PAIEMENT ENVOYE AU
                // HASARD. Retomber sur un autre numero ferait payer quelqu'un
                // d'autre, ce qui est exactement le defaut qu'on corrige ici.
                throw new DomainException(
                    "MISSING_PAYER_PHONE",
                    "Le jeton ne porte pas de téléphone : reconnectez-vous pour payer.");
            }

            intent = await payments
                .CreateIntentAsync(
                    command.IdempotencyKey ?? deliveryId.ToString(),
                    deliveryId,
                    orderer.CustomerId,
                    payeur,
                    snapshot.Total,
                    cancellationToken)
                .ConfigureAwait(false);

            delivery.AttachPaymentIntent(intent.PaymentIntentId);
        }

        repository.Add(delivery);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            await idempotency
                .RememberAsync(
                    IdempotencyScope,
                    BuildIdempotencyKey(orderer, command.IdempotencyKey),
                    deliveryId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // Une seule transaction : la livraison, la clé d'idempotence et les
        // messages d'Outbox sont validés ensemble.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CreateDeliveryResult(
            DeliveryViewMapper.ToView(delivery, caller),
            intent?.PaymentIntentId,
            intent?.RedirectUrl);
    }

    /// <summary>
    /// Débite le compte du donneur d'ordre professionnel, ou rend null quand
    /// la course est payée autrement.
    /// </summary>
    ///
    /// <remarks>
    /// UN CLIENT PARTICULIER NE PASSE PAS PAR ICI : il paie sa course par
    /// mobile money, course par course. Ce chemin est celui des COMMERÇANTS et
    /// des PARTENAIRES, qui en font trente par jour et pour qui un code PIN à
    /// chaque fois n'a aucun sens — et qui, pour un partenaire, est de toute
    /// façon impossible : c'est un système, il n'a personne devant un téléphone.
    ///
    /// LE TITULAIRE EST CELUI QUI COMMANDE, ET C'EST LE PARTENAIRE QUAND UN
    /// PARTENAIRE COMMANDE — même lorsqu'il désigne un commerçant. La règle
    /// était déjà écrite dans ResolveOrderer : « le partenaire agit pour un
    /// commerçant qu'il désigne, ou pour lui-même ; LE PAIEMENT EST CELUI DU
    /// PARTENAIRE ». Un commerçant désigné n'est qu'un point de collecte ; lui
    /// débiter une course qu'il n'a pas commandée serait prendre l'argent de
    /// quelqu'un qui n'a rien demandé.
    ///
    /// LE COMPTE VISE EST CELUI QUE DONNE <see cref="TitulaireDuCompte"/>, et la
    /// distinction Partenaire / Commerçant y est expliquée.
    ///
    /// SI LE DEBIT REUSSIT ET QUE LA SUITE ECHOUE, IL EST RENDU. C'est
    /// <see cref="AnnulerLeReglementAsync"/>, appelée par le rattrapage de
    /// HandleAsync. Ce n'est pas une transaction distribuée et ça n'a pas à
    /// l'être : ce qui reste découvert est l'arrêt BRUTAL du processus entre les
    /// deux, que seul un rapprochement périodique peut retrouver — il n'existe
    /// pas encore, et c'est noté dans points-a-trancher.
    /// </remarks>
    private async Task<DeliveryAggregate.AccountSettlement?> ReglerSurCompteAsync(
        Orderer orderer,
        Guid deliveryId,
        PricingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (TitulaireDuCompte(orderer) is not { } titulaire)
        {
            return null;
        }

        var mouvement = await billing
            .DebitAsync(
                titulaire.Type,
                titulaire.Identifiant,
                snapshot.Total,
                deliveryId.ToString(),
                deliveryId.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return new DeliveryAggregate.AccountSettlement(mouvement);
    }

    /// <summary>
    /// Refuse tout de suite si le donneur d'ordre professionnel n'a pas de compte.
    /// </summary>
    ///
    /// <remarks>
    /// LE MESSAGE DIT QUOI FAIRE, ET C'EST TOUT L'INTÉRÊT. « Compte de facturation
    /// introuvable » envoie celui qui le lit chercher une panne ; ici, il apprend
    /// qu'il faut demander l'ouverture du compte, ce qui est un acte commercial et
    /// non technique.
    /// </remarks>
    private async Task EnsureAccountExistsAsync(Orderer orderer, CancellationToken cancellationToken)
    {
        if (TitulaireDuCompte(orderer) is not { } titulaire)
        {
            return;
        }

        var existe = await billing
            .AccountExistsAsync(titulaire.Type, titulaire.Identifiant, cancellationToken)
            .ConfigureAwait(false);

        if (existe)
        {
            return;
        }

        throw new DomainException(
            "BILLING_ACCOUNT_MISSING",
            "Aucun compte de facturation n'est ouvert pour ce donneur d'ordre. "
            + "Demandez son ouverture au service financier de HBA avant de créer des courses.");
    }

    /// <summary>
    /// Le compte à débiter, ou null quand la course est payée autrement.
    /// </summary>
    ///
    /// <remarks>
    /// ON DISTINGUE LES DEUX PAR LE PARTENAIRE, PAS PAR LE COMMERÇANT. Un
    /// commerçant donneur d'ordre porte le partenaire INTERNE ; un partenaire
    /// porte le sien. Regarder d'abord MerchantId confondrait Partenaire et
    /// Commerçant, exactement ce que le référentiel acteurs interdit.
    ///
    /// EXTRAITE POUR QUE LE DEBIT ET SON ANNULATION VISENT LE MEME COMPTE. La
    /// règle était écrite en ligne dans le débit ; la recopier dans la
    /// compensation aurait fini par rembourser un compte et en débiter un autre.
    /// </remarks>
    private static (string Type, string Identifiant)? TitulaireDuCompte(Orderer orderer)
    {
        if (orderer.CustomerId is not null)
        {
            return null;
        }

        return string.Equals(orderer.PartnerId, PartnerIds.Internal, StringComparison.Ordinal)
            ? ("merchant", orderer.MerchantId!)
            : ("partner", orderer.PartnerId);
    }

    /// <summary>
    /// Rend le débit d'une course qui n'a pas pu être créée.
    /// </summary>
    ///
    /// <remarks>
    /// AUCUN JETON D'ANNULATION, ET C'EST VOLONTAIRE. L'échec qu'on compense est
    /// très souvent une annulation — client déconnecté, délai dépassé — et le
    /// jeton d'origine est alors DÉJÀ annulé : le passer ici ferait échouer la
    /// compensation dans le cas précis où elle est le plus nécessaire.
    ///
    /// L'ECHEC DE LA COMPENSATION NE REMPLACE PAS L'ECHEC D'ORIGINE. On
    /// journalise en erreur — avec de quoi retrouver le débit à la main — et on
    /// laisse remonter la faute initiale, qui est celle que le donneur d'ordre
    /// doit lire.
    /// </remarks>
    private async Task AnnulerLeReglementAsync(Orderer orderer, Guid deliveryId)
    {
        if (TitulaireDuCompte(orderer) is not { } titulaire)
        {
            return;
        }

        try
        {
            await billing
                .ReverseDebitAsync(
                    titulaire.Type,
                    titulaire.Identifiant,
                    deliveryId.ToString(),
                    CancellationToken.None)
                .ConfigureAwait(false);

            journal.LogWarning(
                "Débit annulé pour la course {DeliveryId} : sa création a échoué ({Type} {Identifiant}).",
                deliveryId,
                titulaire.Type,
                titulaire.Identifiant);
        }
        catch (Exception exception)
        {
            // LE SEUL CAS OU DE L'ARGENT RESTE PRIS SANS COURSE EN FACE. Il doit
            // etre trouvable dans les journaux avec tout ce qu'il faut pour le
            // rendre a la main : le compte, et la cle du debit.
            journal.LogError(
                exception,
                "ANNULATION DU DEBIT IMPOSSIBLE. Course {DeliveryId}, compte {Type} {Identifiant} : "
                + "le debit reste et doit etre rendu a la main.",
                deliveryId,
                titulaire.Type,
                titulaire.Identifiant);
        }
    }

    /// <summary>
    /// Retrouve l'adresse de paiement d'une livraison déjà créée, ou null s'il
    /// n'y a rien à reprendre.
    /// </summary>
    ///
    /// <remarks>
    /// TROIS CONDITIONS, ET CHACUNE ECARTE UN CAS REEL. Une livraison de
    /// PARTENAIRE n'a pas de client et ne passe pas par FedaPay. Une livraison
    /// DEJA PAYEE n'a plus besoin d'adresse — la redonner enverrait le client
    /// vers une page qu'il a déjà réglée. Et une livraison sans intention
    /// rattachée n'en a jamais eu.
    ///
    /// UN ECHEC NE FAIT PAS ECHOUER LA REPRISE : si Payment est injoignable, le
    /// client récupère quand même sa livraison, sans adresse. L'écran le lui
    /// dira ; refuser tout serait pire.
    /// </remarks>
    private async Task<string?> ReprendreIntentionAsync(
        DeliveryAggregate delivery,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (delivery.CustomerId is null
            || delivery.PaymentIntentId is null
            || delivery.Status != DeliveryStatus.PendingPayment)
        {
            return null;
        }

        var payeur = caller.Phone;

        if (string.IsNullOrWhiteSpace(payeur))
        {
            return null;
        }

        try
        {
            var intent = await payments
                .CreateIntentAsync(
                    idempotencyKey,
                    delivery.Id,
                    delivery.CustomerId,
                    payeur,
                    delivery.Pricing.Total,
                    cancellationToken)
                .ConfigureAwait(false);

            return intent.RedirectUrl;
        }
        catch (Exception exception)
        {
            // ON JOURNALISE, ON N'AVALE PAS EN SILENCE. Sans cette ligne, un
            // Payment injoignable se traduirait pour le client par un écran qui
            // dit « impossible d'ouvrir la page », et par rien du tout côté
            // serveur : personne ne saurait jamais pourquoi.
            journal.LogWarning(
                exception,
                "Reprise de l'intention de paiement impossible pour la livraison {DeliveryId}.",
                delivery.Id);

            return null;
        }
    }

    /// <summary>
    /// Détermine le donneur d'ordre à partir du jeton, jamais à partir du corps
    /// de la requête : un appelant ne choisit pas pour le compte de qui il crée.
    /// </summary>
    private Orderer ResolveOrderer(CreateDeliveryCommand command)
    {
        if (caller.IsInRole(HbaRoles.Partner))
        {
            var partnerId = caller.PartnerId
                ?? throw new ForbiddenException("Le jeton partenaire ne porte pas de partner_id.");

            if (string.IsNullOrWhiteSpace(command.ExternalOrderId))
            {
                throw new DomainException(
                    "MISSING_EXTERNAL_ORDER_ID",
                    "Un partenaire doit fournir externalOrderId.");
            }

            if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                throw new DomainException(
                    "MISSING_IDEMPOTENCY_KEY",
                    "Idempotency-Key est obligatoire pour les créations partenaire.");
            }

            // Le partenaire agit pour un commerçant qu'il désigne, ou pour
            // lui-même. Le paiement est celui du partenaire, hors de ce service.
            return new Orderer(partnerId, CustomerId: null, MerchantId: command.MerchantId);
        }

        if (caller.IsInRole(HbaRoles.MerchantOwner))
        {
            var merchantId = caller.MerchantId
                ?? throw new ForbiddenException("Le jeton commerçant ne porte pas de merchant_id.");

            // TRANCHE LE 29 SEPTEMBRE 2026 : COMPTE PREPAYE CHEZ BILLING.
            //
            // Ce chemin refusait toute création avec MERCHANT_SETTLEMENT_UNDECIDED,
            // parce que deux règlements étaient possibles et qu'aucun n'avait été
            // choisi. Le choix est fait : un compte par donneur d'ordre, débité
            // avant la création de la course. Le postpayé n'ajoutera pas un
            // second chemin — seulement un plafond accordé par `finance`.
            return new Orderer(PartnerIds.Internal, CustomerId: null, MerchantId: merchantId);
        }

        if (caller.IsInRole(HbaRoles.MerchantStaff))
        {
            throw new ForbiddenException("Un employé de commerçant ne crée pas de livraison.");
        }

        if (caller.IsInRole(HbaRoles.Customer))
        {
            // UN CLIENT NE DÉSIGNE PLUS DE COMMERÇANT, ET CE CHAMP ÉTAIT UNE
            // PORTE OUVERTE.
            //
            // Il était repris du CORPS de la requête sans aucune vérification :
            // ni l'existence du commerçant, ni qu'il ait quoi que ce soit à voir
            // avec cette course. Or MerchantId n'est pas un simple libellé —
            // DeliveryAccess s'en sert comme PÉRIMÈTRE. Y écrire l'identifiant
            // d'un commerçant quelconque lui donnait la lecture de la course,
            // donc le nom et le téléphone du destinataire, l'adresse de
            // livraison et le prix ; et EnsureCanCancel le laissait l'ANNULER.
            //
            // L'APPLICATION CLIENTE NE L'A JAMAIS ENVOYÉ : le champ n'était
            // atteignable qu'en forgeant la requête à la main.
            //
            // CE N'EST PAS LA FERMETURE DÉFINITIVE DE CE CAS. Un client qui
            // commande depuis la boutique d'un commerçant est un vrai besoin,
            // et le domaine l'assume ; il faudra alors vérifier auprès de
            // Directory que le point de collecte existe et appartient bien à ce
            // commerçant. Tant que le parcours commerçant n'est pas repris, on
            // ferme.
            return new Orderer(PartnerIds.Internal, caller.SubjectId, MerchantId: null);
        }

        throw new ForbiddenException("Ce rôle ne peut pas créer de livraison.");
    }

    /// <summary>
    /// La clé d'idempotence, cloisonnée par DONNEUR D'ORDRE.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE NE L'ÉTAIT QUE PAR PARTENAIRE, ET CELA NE CLOISONNAIT RIEN. Tous les
    /// clients particuliers et tous les commerçants portent le MÊME partenaire —
    /// la constante interne — et le magasin ne compare que la portée et la clé.
    /// Deux appelants différents qui envoyaient la même Idempotency-Key
    /// tombaient donc sur la même ligne, et le second recevait la livraison du
    /// premier : adresses, destinataire, téléphones et tarif.
    ///
    /// L'IDENTIFIANT DU DONNEUR D'ORDRE VIENT DU JETON, jamais du corps : c'est
    /// ResolveOrderer qui l'a posé. Un appelant ne peut donc pas se placer dans
    /// le cloisonnement d'un autre en choisissant sa clé.
    ///
    /// CONSÉQUENCE ASSUMÉE : les clés déjà enregistrées ne se retrouvent plus.
    /// Une création en cours de rejeu au moment du déploiement recréera une
    /// course au lieu de rendre la sienne. Avant mise en service, c'est sans
    /// effet ; après, il faudrait purger la table.
    /// </remarks>
    private static string BuildIdempotencyKey(Orderer orderer, string key)
        => $"{orderer.PartnerId}:{orderer.CustomerId ?? orderer.MerchantId ?? string.Empty}:{key}";

    /// <summary>
    /// Le point de collecte ne vaut que rattaché à un commerçant.
    /// </summary>
    ///
    /// <remarks>
    /// IL VIENT DU CORPS, COMME MerchantId, ET IL LE SUIT. Un identifiant de
    /// point de collecte sans commerçant en face ne désigne rien : le garder
    /// n'écrirait qu'une donnée forgée dans l'agrégat, que la vue rend ensuite
    /// telle quelle à tout le monde.
    /// </remarks>
    private static string? PointDeCollecte(Orderer orderer, CreateDeliveryCommand command)
        => orderer.MerchantId is null ? null : command.PickupPointId;

    private static Location ToLocation(LocationInput input)
        => Location.Create(
            GeoPoint.Create(input.Latitude, input.Longitude),
            input.Landmark,
            input.Phone,
            input.ContactName,
            input.Notes);

    private sealed record Orderer(string PartnerId, string? CustomerId, string? MerchantId);
}

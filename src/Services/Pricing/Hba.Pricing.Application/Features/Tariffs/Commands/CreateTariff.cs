using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Authorization;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Exceptions;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;

namespace Hba.Pricing.Application.Features.Tariffs.Commands;

/// <summary>
/// Cree une grille tarifaire, et clot celle qu'elle remplace.
/// </summary>
///
/// <remarks>
/// POURQUOI CETTE COMMANDE MANQUAIT, ET CE QUE COUTAIT SON ABSENCE. La seule
/// grille du systeme etait semee au demarrage par InitialTariffSeeder, depuis
/// la configuration, pour UN couple (zone, vehicule). Les quatre autres
/// vehicules — voiture, camionnette, velo, tricycle — n'avaient aucun prix :
/// toute course les concernant echouait sur NO_TARIFF, et la seule facon d'en
/// ajouter un etait de changer une variable d'environnement et de RECREER le
/// conteneur. Un changement de prix demandait donc un redemarrage de service.
///
/// UNE GRILLE NE SE MODIFIE JAMAIS, ON LA REMPLACE. C'est la regle que porte
/// deja ITariffRepository.FindInForceAsync, et elle vient de l'ADR 0004 : un
/// devis emis hier se reclame de la grille d'hier. Modifier une grille en place
/// changerait retroactivement des prix deja annonces, voire deja factures.
/// Cette commande CREE donc une version et CLOT la precedente a la seconde ou
/// la nouvelle prend effet — jamais de recouvrement, jamais de trou.
///
/// L'ORDRE DES DEUX ECRITURES N'A PAS D'IMPORTANCE ICI, et c'est voulu : les
/// deux partent dans la MEME transaction (IUnitOfWork). Une cloture sans
/// creation laisserait une zone sans prix ; une creation sans cloture laisserait
/// deux grilles en vigueur, et FindInForceAsync trancherait en silence par la
/// date la plus recente. Aucun des deux etats ne doit pouvoir exister.
/// </remarks>
public sealed record CreateTariffCommand(
    string TariffVersion,
    string ZoneCode,
    ServiceType ServiceType,
    VehicleType VehicleType,

    /// <summary>
    /// Debut de la plage horaire, en minutes depuis minuit, heure locale.
    /// </summary>
    ///
    /// <remarks>
    /// ZERO ET 1440 COUVRENT LA JOURNEE : c'est la valeur par defaut, et celle de
    /// toutes les grilles anterieures. UNE PLAGE PEUT ENJAMBER MINUIT — 1320 → 360
    /// vaut 22 h → 6 h — et s'ecrit alors avec un debut superieur a sa fin.
    /// </remarks>
    int BandFromMinute,
    int BandToMinute,
    long BaseFare,
    long PerKilometer,
    long PerMinute,
    long MinimumFare,
    int SurgeBasisPoints,
    int DriverShareBasisPoints,
    DateTimeOffset? ValidFrom,
    string IdempotencyKey,

    /// <summary>
    /// Tarif au kilogramme au-dela du poids inclus. Refuse sur une course de
    /// personnes : un passager n'a pas de poids declare.
    /// </summary>
    long PerKilogram = 0,

    int IncludedWeightGrams = 0,

    /// <summary>
    /// Retenue d'annulation apres affectation, en points de base du devis. Zero
    /// vaut gratuit, et c'est le defaut : une grille saisie sans ce champ ne
    /// preleve rien, ce qui n'enleve d'argent a personne.
    /// </summary>
    int CancellationFeeBasisPoints = 0) : ICommand<TariffView>;

public sealed class CreateTariffHandler(
    ITariffRepository tariffs,
    ICallerContext caller,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateTariffCommand, TariffView>
{
    private const string IdempotencyScope = "pricing.tariff.create";

    public async Task<TariffView> HandleAsync(
        CreateTariffCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // L'AUTORISATION D'ABORD, AVANT MEME DE LIRE QUOI QUE CE SOIT. Un
        // appelant sans droit ne doit pas pouvoir deduire, du temps de reponse
        // ou d'un refus different, quelles grilles existent.
        PricingAccess.EnsureCanAdministerTariffs(caller);

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new DomainException(
                "VALIDATION_FAILED",
                "Une cle d'idempotence est exigee pour creer une grille.");
        }

        // LE REJEU REND LA MEME GRILLE, IL NE REFUSE PAS. C'est la convention du
        // depot (voir CreatePaymentIntent) : un appelant qui reessaie apres un
        // reseau coupe doit obtenir ce qu'il a deja cree, pas une erreur qui
        // l'inviterait a recommencer autrement.
        //
        // SANS CELA, DEUX APPELS IDENTIQUES FERAIENT DEUX VERSIONS dont la
        // seconde clorait la premiere a la meme seconde : la premiere n'aurait
        // jamais servi, et l'historique porterait une grille fantome de duree
        // nulle, impossible a expliquer six mois plus tard.
        var connue = await idempotency
            .TryGetResultAsync(IdempotencyScope, command.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (connue is not null && Guid.TryParse(connue, out var dejaCreee))
        {
            var existante = await tariffs
                .GetByIdAsync(dejaCreee, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException("Grille tarifaire", connue);

            return TariffView.From(existante);
        }

        var prisEffet = command.ValidFrom ?? clock.UtcNow;
        var plage = TimeBand.Create(command.BandFromMinute, command.BandToMinute);
        var zone = command.ZoneCode.Trim().ToLowerInvariant();

        // AUCUNE PLAGE NE PEUT RECOUVRIR UNE AUTRE SUR LA MEME CLE, ET C'EST CE
        // CONTROLE QUI DONNE UN SENS AUX PLAGES.
        //
        // Sans lui, une grille 0–1440 et une grille 22 h–6 h cohabiteraient sur la
        // meme zone, la meme nature et le meme vehicule : le prix d'une course a 23 h
        // dependrait de l'ordre de lecture en base, donc changerait d'un appel a
        // l'autre sans que rien ne soit en panne. C'est exactement le genre d'ecart
        // qu'un client remarque et que personne ne reproduit.
        //
        // IL EST APPLICATIF, FAUTE DE POUVOIR L'EXPRIMER EN BASE. « Ces deux plages ne
        // se recouvrent pas » n'est pas une contrainte SQL sans une extension que ce
        // depot n'installe pas. Deux creations concurrentes sur la meme cle peuvent
        // donc passer — c'est un geste d'exploitation, rare et attribue.
        var surLaMemeCle = await tariffs
            .FindInForceOnKeyAsync(zone, command.ServiceType, command.VehicleType, prisEffet, cancellationToken)
            .ConfigureAwait(false);

        var conflit = surLaMemeCle.FirstOrDefault(t => t.Band.Chevauche(plage) && !t.Band.CouvreToutLeJour);

        if (conflit is not null)
        {
            throw new DomainException(
                PricingErrorCodes.OverlappingTimeBand,
                $"Une grille couvre deja {conflit.Band} sur cette zone, cette nature et ce vehicule. "
                + "Close-la avant d'en creer une qui recouvre ses heures.");
        }

        var nouvelle = Tariff.Create(
            Guid.CreateVersion7(),
            command.TariffVersion,
            command.ZoneCode,
            command.ServiceType,
            command.VehicleType,
            plage,
            MoneyXof.From(command.BaseFare),
            MoneyXof.From(command.PerKilometer),
            MoneyXof.From(command.PerMinute),
            MoneyXof.From(command.MinimumFare),
            command.SurgeBasisPoints,
            command.DriverShareBasisPoints,
            prisEffet,
            MoneyXof.From(command.PerKilogram),
            command.IncludedWeightGrams,
            command.CancellationFeeBasisPoints);

        // CELLE QU'ON REMPLACE EST CELLE QUI COUVRE LES MEMES HEURES, pas « la
        // derniere creee » ni « celle qui valait a cet instant ».
        //
        // LA NUANCE EST NEE DES PLAGES. Avant elles, une cle n'avait qu'une grille en
        // vigueur et la remplacer etait sans ambiguite. Desormais une zone peut porter
        // une grille de jour et une grille de nuit : creer une nouvelle grille de nuit
        // ne doit pas clore celle du jour. On ne clot donc que celle dont la plage est
        // la MEME — le chevauchement partiel, lui, a deja ete refuse plus haut.
        var remplacee = surLaMemeCle.FirstOrDefault(t => t.Band == plage);

        remplacee?.Close(prisEffet);

        tariffs.Add(nouvelle);

        // LA CLE EST RETENUE AVANT LE SaveChanges, DANS LA MEME TRANSACTION.
        // L'enregistrer apres laisserait une fenetre ou la grille existe et la
        // cle non : un rejeu pendant cette fenetre creerait la seconde version
        // que toute cette mecanique sert a eviter.
        await idempotency
            .RememberAsync(
                IdempotencyScope,
                command.IdempotencyKey,
                nouvelle.Id.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TariffView.From(nouvelle);
    }
}

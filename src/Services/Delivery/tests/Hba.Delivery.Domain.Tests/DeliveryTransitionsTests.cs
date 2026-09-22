using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Delivery.Domain.Deliveries;
using Xunit;

namespace Hba.Delivery.Domain.Tests;

public sealed class DeliveryTransitionsTests
{
    [Fact]
    public void Aucun_acteur_ne_peut_atteindre_Delivered_en_dehors_du_livreur()
    {
        var culprits = Enum.GetValues<ActorKind>()
            .Where(kind => kind != ActorKind.Driver)
            .Where(kind => Enum.GetValues<DeliveryStatus>()
                .Any(from => DeliveryTransitions.IsAllowed(from, DeliveryStatus.Delivered, kind)))
            .ToList();

        culprits.Should().BeEmpty("seul le livreur, avec l'OTP, peut marquer une livraison comme remise");
    }

    [Fact]
    public void Aucune_transition_ne_part_d_un_état_terminal()
    {
        foreach (var terminal in DeliveryTransitions.Terminal)
        {
            DeliveryTransitions.From(terminal).Should().BeEmpty($"{terminal} est un état terminal");
        }
    }

    [Fact]
    public void Le_paiement_est_le_seul_chemin_vers_Paid()
    {
        var actors = Enum.GetValues<ActorKind>()
            .Where(kind => DeliveryTransitions.IsAllowed(DeliveryStatus.PendingPayment, DeliveryStatus.Paid, kind))
            .ToList();

        actors.Should().Equal(ActorKind.PaymentProvider);
    }

    [Fact]
    public void Seul_le_moteur_de_dispatch_affecte_un_livreur()
    {
        var actors = Enum.GetValues<ActorKind>()
            .Where(kind => DeliveryTransitions.IsAllowed(DeliveryStatus.SearchingDriver, DeliveryStatus.DriverAssigned, kind))
            .ToList();

        actors.Should().Equal(ActorKind.Dispatch);
    }
}

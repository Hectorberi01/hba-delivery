using FluentAssertions;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hba.BuildingBlocks.Tests;

/// <summary>
/// Le calendrier est la pièce la plus banale et la plus piégeuse du tableau de
/// bord : un décalage d'une heure ne fait pas planter, il déplace des courses
/// d'un jour à l'autre. Ces cas fixent la frontière.
/// </summary>
public sealed class TimeCalendarTests
{
    private sealed class HorlogeFigee(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow { get; } = instant;
    }

    private static ITimeCalendar Calendrier(DateTimeOffset maintenant, TimeOptions? options = null)
        => new TimeCalendar(Options.Create(options ?? new TimeOptions()), new HorlogeFigee(maintenant));

    [Fact]
    public void Une_course_de_minuit_trente_a_Cotonou_appartient_au_jour_qui_commence()
    {
        // 26 septembre 00 h 30 à Cotonou, c'est le 25 à 23 h 30 en UTC.
        // Regroupée sur UTC, cette course tomberait dans la veille.
        var calendrier = Calendrier(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var instant = DateTimeOffset.Parse("2026-09-25T23:30:00Z");

        calendrier.Key(instant, TimeGranularity.Day).Should().Be("2026-09-26");
    }

    [Fact]
    public void La_fenetre_des_sept_derniers_jours_comprend_aujourdhui_en_entier()
    {
        var calendrier = Calendrier(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));

        var fenetre = calendrier.LastDays(7);

        // Du 20 septembre 00 h 00 locale (23 h 00 UTC le 19) au 27 septembre
        // 00 h 00 locale : sept journées pleines, la borne haute exclue.
        fenetre.From.Should().Be(DateTimeOffset.Parse("2026-09-19T23:00:00Z"));
        fenetre.To.Should().Be(DateTimeOffset.Parse("2026-09-26T23:00:00Z"));
        calendrier.Keys(fenetre, TimeGranularity.Day).Should().HaveCount(7);
    }

    [Fact]
    public void Les_journees_sans_course_figurent_quand_meme_dans_la_serie()
    {
        // C'est tout l'objet de Keys : un GROUP BY ne rend rien pour un jour
        // vide, et le graphique resserrerait ses barres au lieu d'afficher
        // un creux.
        var calendrier = Calendrier(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));

        var cles = calendrier.Keys(calendrier.LastDays(3), TimeGranularity.Day);

        cles.Should().Equal("2026-09-24", "2026-09-25", "2026-09-26");
    }

    [Fact]
    public void Deux_fenetres_consecutives_ne_se_recouvrent_pas()
    {
        var calendrier = Calendrier(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));
        var fenetre = calendrier.LastDays(7);
        var precedente = fenetre.Previous();

        precedente.To.Should().Be(fenetre.From);
        fenetre.Contains(fenetre.From).Should().BeTrue();
        fenetre.Contains(fenetre.To).Should().BeFalse("la borne haute est exclue");
        precedente.Contains(fenetre.From).Should().BeFalse();
    }

    [Fact]
    public void Le_mois_en_cours_va_du_premier_au_premier_du_mois_suivant()
    {
        var calendrier = Calendrier(DateTimeOffset.Parse("2026-09-26T12:00:00Z"));

        var mois = calendrier.CurrentMonth();

        calendrier.Key(mois.From, TimeGranularity.Month).Should().Be("2026-09");
        calendrier.Keys(mois, TimeGranularity.Month).Should().Equal("2026-09");
        mois.To.Should().Be(DateTimeOffset.Parse("2026-09-30T23:00:00Z"));
    }

    [Fact]
    public void Une_fenetre_trop_large_est_refusee()
    {
        var calendrier = Calendrier(
            DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
            new TimeOptions { MaxWindowDays = 31 });

        var trop = TimeWindow.Create(
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var action = () => calendrier.Validate(trop);

        action.Should().Throw<DomainException>().Which.Code.Should().Be("TIME_WINDOW_TOO_WIDE");
    }

    [Fact]
    public void Une_fenetre_inversee_est_refusee_des_la_construction()
    {
        var action = () => TimeWindow.Create(
            DateTimeOffset.Parse("2026-09-26T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

        action.Should().Throw<DomainException>().Which.Code.Should().Be("INVALID_TIME_WINDOW");
    }

    [Fact]
    public void Un_fuseau_introuvable_arrete_le_demarrage()
    {
        var action = () => Calendrier(
            DateTimeOffset.Parse("2026-09-26T12:00:00Z"),
            new TimeOptions { ZoneId = "Africa/Cotonou-Nord" });

        action.Should().Throw<InvalidOperationException>();
    }
}

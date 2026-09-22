using System.Text;
using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Notification.Domain.Messages;
using Hba.Notification.Domain.Templates;
using Xunit;

namespace Hba.Notification.Domain.Tests;

public sealed class TemplateTests
{
    [Fact]
    public void Un_modèle_inconnu_est_refusé()
    {
        var act = () => TemplateCatalog.Get("modele_invente");

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void Une_variable_manquante_fait_échouer_le_rendu()
    {
        var template = TemplateCatalog.Get(TemplateCatalog.OtpLogin);

        var act = () => template.Render(new Dictionary<string, string> { ["code"] = "123456" });

        // Mieux vaut ne pas envoyer que d'envoyer « expire dans {minutes} minutes ».
        act.Should().Throw<DomainException>().Which.Code.Should().Be("MISSING_TEMPLATE_VARIABLE");
    }

    [Fact]
    public void Le_rendu_substitue_toutes_les_variables()
    {
        var rendered = TemplateCatalog.Get(TemplateCatalog.OtpLogin).Render(
            new Dictionary<string, string> { ["code"] = "424242", ["minutes"] = "5" });

        rendered.Should().Contain("424242").And.Contain("5 minutes");
        rendered.Should().NotContain("{");
    }

    [Theory]
    [InlineData(TemplateCatalog.OtpLogin)]
    [InlineData(TemplateCatalog.DeliveryOtp)]
    [InlineData(TemplateCatalog.DeliveryAssigned)]
    [InlineData(TemplateCatalog.DeliveryCompleted)]
    public void Les_modèles_SMS_tiennent_dans_l_alphabet_GSM_7(string templateId)
    {
        var template = TemplateCatalog.Get(templateId);

        if (template.Channel != NotificationChannel.Sms)
        {
            return;
        }

        // Un seul caractère hors GSM-7 fait basculer le message en UCS-2 : la
        // capacité tombe de 160 à 70 caractères et la facture double.
        var offenders = template.Body.Where(c => !Gsm7.Contains(c)).Distinct().ToList();

        offenders.Should().BeEmpty(
            $"« {string.Join("", offenders)} » forcerait l'encodage UCS-2 sur {templateId}");
    }

    [Theory]
    [InlineData(TemplateCatalog.OtpLogin)]
    [InlineData(TemplateCatalog.DeliveryOtp)]
    [InlineData(TemplateCatalog.DeliveryAssigned)]
    [InlineData(TemplateCatalog.DeliveryCompleted)]
    public void Les_modèles_SMS_laissent_de_la_place_aux_variables(string templateId)
    {
        var template = TemplateCatalog.Get(templateId);

        if (template.Channel != NotificationChannel.Sms)
        {
            return;
        }

        // Le texte fixe seul ne doit pas déjà frôler la limite, sinon la
        // première référence un peu longue fait basculer sur un second SMS.
        template.Body.Length.Should().BeLessThan(template.MaxLength - 20);
    }

    /// <summary>
    /// Sous-ensemble de l'alphabet GSM 03.38 suffisant pour ces messages : on
    /// vérifie surtout l'absence d'accents.
    /// </summary>
    private static readonly HashSet<char> Gsm7 =
    [
        .. "@$\n\r !\"#%&'()*+,-./0123456789:;<=>?"
           + "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
           + "abcdefghijklmnopqrstuvwxyz"
           + "_£¥èéùìòÇØøÅåÆæßÉÄÖÑÜ§¿äöñüà",
    ];
}

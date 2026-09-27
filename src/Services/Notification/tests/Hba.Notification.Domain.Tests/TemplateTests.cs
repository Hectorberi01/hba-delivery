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
        var template = TemplateCatalog.Get(TemplateCatalog.OtpLogin).RequireSms();

        var act = () => template.Render(new Dictionary<string, string> { ["code"] = "123456" });

        // Mieux vaut ne pas envoyer que d'envoyer « expire dans {minutes} minutes ».
        act.Should().Throw<DomainException>().Which.Code.Should().Be("MISSING_TEMPLATE_VARIABLE");
    }

    [Fact]
    public void Le_rendu_substitue_toutes_les_variables()
    {
        var rendered = TemplateCatalog.Get(TemplateCatalog.OtpLogin).RequireSms().Render(
            new Dictionary<string, string> { ["code"] = "424242", ["minutes"] = "5" });

        rendered.Should().Contain("424242").And.Contain("5 minutes");
        rendered.Should().NotContain("{");
    }

    [Fact]
    public void Le_code_de_connexion_essaie_WhatsApp_puis_le_SMS()
    {
        var binding = TemplateCatalog.Get(TemplateCatalog.OtpLogin);

        binding.Channels.Should().Equal(NotificationChannel.WhatsApp, NotificationChannel.Sms);
        binding.WhatsApp.Should().NotBeNull();
        binding.Sms.Should().NotBeNull();
    }

    [Fact]
    public void Le_code_de_remise_ne_part_jamais_par_WhatsApp()
    {
        // Le destinataire n'a pas de compte HBA, donc aucun opt-in — et Meta
        // l'exige avant tout message de gabarit. Ce test est là pour qu'on ne
        // « complète » pas le catalogue un jour par inadvertance.
        var binding = TemplateCatalog.Get(TemplateCatalog.DeliveryOtp);

        binding.Channels.Should().Equal(NotificationChannel.Sms);
        binding.Supports(NotificationChannel.WhatsApp).Should().BeFalse();
        binding.WhatsApp.Should().BeNull();
    }

    [Fact]
    public void Un_canal_non_pris_en_charge_est_refusé_explicitement()
    {
        var binding = TemplateCatalog.Get(TemplateCatalog.DeliveryCompleted);

        var act = () => binding.RequireWhatsApp();

        act.Should().Throw<DomainException>().Which.Code.Should().Be("CHANNEL_UNSUPPORTED");
    }

    [Theory]
    [InlineData(TemplateCatalog.OtpLogin)]
    [InlineData(TemplateCatalog.DeliveryOtp)]
    [InlineData(TemplateCatalog.DeliveryAssigned)]
    [InlineData(TemplateCatalog.DeliveryCompleted)]
    public void Les_modèles_SMS_tiennent_dans_l_alphabet_GSM_7(string templateId)
    {
        var template = TemplateCatalog.Get(templateId).Sms;

        if (template is null)
        {
            return;
        }

        // On vérifie le TEXTE LITTERAL, pas le gabarit brut : « {code} » ne part
        // jamais tel quel, il est remplacé au rendu.
        var literal = WithoutPlaceholders(template.Body);

        // Un seul caractère hors GSM 03.38 fait basculer le message en UCS-2 :
        // la capacité tombe de 160 à 70 caractères et la facture double.
        var offenders = literal.Where(c => !Gsm7Basic.Contains(c) && !Gsm7Extended.Contains(c))
            .Distinct()
            .ToList();

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
        var template = TemplateCatalog.Get(templateId).Sms;

        if (template is null)
        {
            return;
        }

        // Le texte fixe seul ne doit pas déjà frôler la limite, sinon la
        // première référence un peu longue fait basculer sur un second SMS.
        template.Body.Length.Should().BeLessThan(template.MaxLength - 20);
    }

    /// <summary>Retire les variables de substitution, « {code} » et consorts.</summary>
    private static string WithoutPlaceholders(string body)
    {
        var builder = new StringBuilder(body.Length);
        var inside = false;

        foreach (var c in body)
        {
            if (c == '{')
            {
                inside = true;
            }
            else if (c == '}')
            {
                inside = false;
            }
            else if (!inside)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Table de base de l'alphabet GSM 03.38 : un septet par caractère.</summary>
    private static readonly HashSet<char> Gsm7Basic =
    [
        .. "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ"
           + " !\"#¤%&'()*+,-./0123456789:;<=>?"
           + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§"
           + "¿abcdefghijklmnopqrstuvwxyzäöñüà",
    ];

    /// <summary>
    /// Table d'extension. Ces caractères NE FORCENT PAS l'UCS-2 : ils passent en
    /// GSM-7 précédés d'un caractère d'échappement, et coûtent deux septets.
    /// </summary>
    private static readonly HashSet<char> Gsm7Extended = [.. "^{}\\[~]|€"];
}

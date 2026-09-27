namespace Hba.Payment.Application.Common.Interfaces;

/// <summary>
/// Les deux reglages d'une demande de versement.
///
/// ZERO PAR DEFAUT SIGNIFIE « RIEN N'EST IMPOSE », et c'est volontaire :
/// le seuil minimum et le delai de carence sont des decisions d'exploitation
/// qui n'ont pas ete prises. Le mecanisme existe, les valeurs attendent — les
/// inventer ici reviendrait a decider a la place de quelqu'un, et le premier
/// livreur a qui l'on refuserait un retrait ne saurait pas au nom de quoi.
///
/// C'est la meme forme que la retention du journal des lectures : le
/// mecanisme livre, les durees en configuration.
/// </summary>
public sealed class PayoutOptions
{
    public const string SectionName = "Payouts";

    /// <summary>
    /// Montant minimum d'une demande, en francs CFA. Zero : aucun minimum.
    /// </summary>
    public long MinimumXof { get; set; }

    /// <summary>
    /// Delai de carence, en heures, entre une course livree et le moment ou
    /// sa remuneration devient retirable. Zero : aucune carence.
    ///
    /// A QUOI IL SERT. Une course livree peut etre contestee ; verser
    /// immediatement rend la correction impossible. Combien d'heures, c'est
    /// une decision commerciale — pas une constante technique.
    /// </summary>
    public int CoolingOffHours { get; set; }
}

namespace Hba.Driver.Domain;

/// <summary>
/// Repere du domaine Driver.
///
/// L'agregat est <see cref="Drivers.DriverAggregate"/> : le dossier du livreur,
/// son vehicule et son etat de travail. SA POSITION N'Y EST PAS — elle vit dans
/// Redis GEO, comme l'exige le referentiel acteurs, et n'est jamais ecrite en
/// base relationnelle a chaque rafraichissement.
/// </summary>
internal static class DomainPlaceholder
{
    public const string ServiceName = "driver";
}

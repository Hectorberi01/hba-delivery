using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Hba.Dispatch.Infrastructure.Persistence;

/// <summary>
/// Identifiants UUID v7 pour les cles que le domaine ne pose pas.
///
/// POURQUOI PAS LE GENERATEUR PAR DEFAUT D'EF. Celui-ci rend des GUID
/// sequentiels ordonnes pour SQL Server, dont l'ordre des octets n'est pas
/// celui de « uuid » en PostgreSQL : les inserts retombent en aleatoire pur
/// et l'index se fragmente. La version 7 porte l'horodatage dans ses premiers
/// octets, dans le bon ordre, et c'est deja la convention du depot.
/// </summary>
public sealed class GuidVersion7Generator : ValueGenerator<Guid>
{
    /// <summary>La valeur est definitive : elle part telle quelle en base.</summary>
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}

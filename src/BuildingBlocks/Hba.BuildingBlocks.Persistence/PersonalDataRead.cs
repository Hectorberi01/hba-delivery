using System.Diagnostics;
using Hba.BuildingBlocks.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Une lecture de données personnelles, telle qu'elle est conservée.
///
/// LE JOURNAL NE RECOPIE PAS CE QUI A ETE LU. Il nomme la personne regardée
/// par son identifiant, pas par son nom ni son téléphone : un journal qui
/// contiendrait les données qu'il surveille doublerait la fuite qu'il sert à
/// détecter, et vieillirait mal — un client qui change de numéro laisserait
/// l'ancien dans le journal pour toujours.
/// </summary>
public sealed class PersonalDataRead
{
    public Guid Id { get; set; }

    /// <summary>Nature de la lecture.</summary>
    public PersonalDataReadKind Kind { get; set; }

    /// <summary>Identifiant de la personne regardée.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Identifiant du compte qui a regardé.</summary>
    public string ReaderId { get; set; } = string.Empty;

    /// <summary>
    /// Rôles du lecteur au moment de la lecture, séparés par des virgules.
    ///
    /// FIGES ICI, ET C'EST VOULU : les rôles d'un compte changent. Relire le
    /// compte six mois plus tard dirait ce qu'il est devenu, pas ce qu'il
    /// était quand il a regardé.
    /// </summary>
    public string ReaderRoles { get; set; } = string.Empty;

    public DateTimeOffset ReadAt { get; set; }

    /// <summary>Rattache la ligne aux journaux techniques de la même requête.</summary>
    public string? TraceId { get; set; }
}

public sealed class EfPersonalDataReadLog(
    DbContext context,
    ICallerContext caller,
    ILogger<EfPersonalDataReadLog> journal) : IPersonalDataReadLog
{
    public async Task RecordAsync(
        PersonalDataReadKind kind,
        string subjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(caller);

        var ligne = new PersonalDataRead
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            SubjectId = subjectId,
            ReaderId = caller.SubjectId,
            ReaderRoles = string.Join(',', caller.Roles.OrderBy(r => r, StringComparer.Ordinal)),
            ReadAt = DateTimeOffset.UtcNow,
            TraceId = caller.TraceId ?? Activity.Current?.TraceId.ToString(),
        };

        var entree = context.Set<PersonalDataRead>().Add(ligne);

        try
        {
            // SON PROPRE SaveChanges. On est sur un chemin de LECTURE : aucun
            // changement metier n'attend d'etre valide, donc rien d'autre ne
            // partira avec cette ligne.
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            // UN JOURNAL QUI TOMBE NE BLOQUE PAS LA LECTURE, ET C'EST UN CHOIX
            // DISCUTABLE. La posture stricte — pas de trace, pas de lecture —
            // ferait qu'un hoquet de base prive le support de la fiche d'un
            // client au telephone. On laisse donc passer, mais BRUYAMMENT :
            // niveau Error, avec le lecteur et la cible, pour qu'un trou dans
            // le journal se voie au lieu de se deviner.
            //
            // POUR INVERSER CE CHOIX : retirer ce catch. Rien d'autre a
            // changer — l'exception remonte alors et la lecture echoue.
            journal.LogError(
                exception,
                "Lecture de donnees personnelles NON CONSIGNEE : {Lecteur} a lu {Nature} de {Sujet}.",
                caller.SubjectId,
                kind,
                subjectId);

            // LA LIGNE SEULE EST DETACHEE, pas tout le suivi. Un
            // ChangeTracker.Clear() emporterait aussi l'agregat charge par la
            // requete en cours, pour un nettoyage qui ne concerne que cette
            // ligne-ci.
            entree.State = EntityState.Detached;
        }
    }
}

public sealed class EfPersonalDataReadReader(DbContext context) : IPersonalDataReadReader
{
    public async Task<IReadOnlyList<PersonalDataReadEntry>> ListForSubjectAsync(
        string subjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        return await context.Set<PersonalDataRead>()
            .AsNoTracking()
            .Where(r => r.SubjectId == subjectId)
            .OrderByDescending(r => r.ReadAt)
            .Take(Math.Clamp(limit <= 0 ? 50 : limit, 1, 200))
            .Select(r => new PersonalDataReadEntry(r.Kind, r.ReaderId, r.ReaderRoles, r.ReadAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

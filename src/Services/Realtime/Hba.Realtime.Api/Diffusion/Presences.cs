using System.Collections.Concurrent;

namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Qui est abonne a quoi, SUR CETTE REPLIQUE.
/// </summary>
///
/// <remarks>
/// POURQUOI CETTE CLASSE EXISTE : SIGNALR NE SAIT PAS ENUMERER LES MEMBRES D'UN
/// GROUPE. C'est une absence volontaire de sa part — un groupe peut etre reparti sur
/// plusieurs repliques, et l'enumerer demanderait d'interroger toutes les autres.
///
/// OR LE DIFFUSEUR DE POSITIONS A BESOIN DES CONNEXIONS, pas du groupe. Le pub/sub de
/// Redis livre chaque battement a TOUTES les repliques ; si chacune envoyait au
/// groupe, le backplane rediffuserait le meme point autant de fois qu'il y a de
/// repliques. Chaque replique sert donc ses propres abonnes, et doit pour cela savoir
/// lesquels elle detient. Voir <see cref="DiffuseurDePositions"/>, qui porte le
/// raisonnement complet.
///
/// ELLE EST EN MEMOIRE, ET C'EST CORRECT ICI — contrairement a l'annuaire des
/// missions, qui est dans Redis. La difference : une connexion EST locale par nature.
/// Elle n'existe que sur la replique qui la tient, elle meurt avec elle, et une autre
/// replique n'a rien a en faire. Une information locale se range en memoire ; c'est
/// l'association mission → chauffeur qui, elle, devait etre partagee.
///
/// UN REDEMARRAGE LA VIDE, ET C'EST SANS CONSEQUENCE : les connexions tombent avec le
/// processus, les clients se reconnectent et se reabonnent. Rien a reconstruire.
///
/// PAS DE FUITE POSSIBLE PAR OUBLI D'UN DESABONNEMENT, parce que
/// <see cref="OublierLaConnexion"/> est appele depuis <c>OnDisconnectedAsync</c>, que
/// SignalR appelle toujours — y compris quand le reseau tombe sans fermeture propre,
/// au bout de son delai de keep-alive.
/// </remarks>
internal sealed class Presences
{
    /// <summary>
    /// Groupe → connexions. Le sens dont le diffuseur a besoin.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _parGroupe =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Connexion → groupes. L'index inverse, pour nettoyer a la deconnexion.
    ///
    /// SANS LUI, LE NETTOYAGE BALAIERAIT TOUS LES GROUPES a chaque deconnexion. Avec
    /// quelques centaines de missions suivies en meme temps, ce balayage arriverait a
    /// chaque fermeture d'ecran.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _parConnexion =
        new(StringComparer.Ordinal);

    public void Suivre(string connexion, string groupe)
    {
        _parGroupe.GetOrAdd(groupe, _ => new(StringComparer.Ordinal))[connexion] = 0;
        _parConnexion.GetOrAdd(connexion, _ => new(StringComparer.Ordinal))[groupe] = 0;
    }

    public void NePlusSuivre(string connexion, string groupe)
    {
        if (_parGroupe.TryGetValue(groupe, out var connexions))
        {
            connexions.TryRemove(connexion, out _);

            // UN GROUPE VIDE SE RETIRE, sinon le dictionnaire garderait une entree par
            // mission jamais suivie deux fois — c'est-a-dire une par mission de la
            // journee.
            if (connexions.IsEmpty)
            {
                _parGroupe.TryRemove(groupe, out _);
            }
        }

        if (_parConnexion.TryGetValue(connexion, out var groupes))
        {
            groupes.TryRemove(groupe, out _);

            if (groupes.IsEmpty)
            {
                _parConnexion.TryRemove(connexion, out _);
            }
        }
    }

    /// <summary>Appele a la deconnexion : retire cette connexion de partout.</summary>
    public void OublierLaConnexion(string connexion)
    {
        if (!_parConnexion.TryRemove(connexion, out var groupes))
        {
            return;
        }

        foreach (var groupe in groupes.Keys)
        {
            if (!_parGroupe.TryGetValue(groupe, out var connexions))
            {
                continue;
            }

            connexions.TryRemove(connexion, out _);

            if (connexions.IsEmpty)
            {
                _parGroupe.TryRemove(groupe, out _);
            }
        }
    }

    /// <summary>
    /// Les connexions de ce groupe detenues par cette replique. Vide si aucune.
    /// </summary>
    ///
    /// <remarks>
    /// REND UNE COPIE, et c'est necessaire : l'appelant va l'enumerer pendant un envoi
    /// asynchrone, et une connexion peut se fermer entre-temps. Enumerer la collection
    /// vivante leverait. Envoyer a une connexion deja fermee, en revanche, est sans
    /// consequence — SignalR l'ignore.
    /// </remarks>
    public IReadOnlyList<string> ConnexionsDuGroupe(string groupe)
        => _parGroupe.TryGetValue(groupe, out var connexions) && !connexions.IsEmpty
            ? [.. connexions.Keys]
            : [];
}

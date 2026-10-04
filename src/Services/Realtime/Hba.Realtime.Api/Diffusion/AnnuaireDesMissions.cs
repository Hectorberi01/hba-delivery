using Hba.Contracts.Common.V1;
using Hba.Realtime.Api.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Quel chauffeur est affecte a quelle mission — la seule chose que ce service
/// retienne.
/// </summary>
///
/// <remarks>
/// POURQUOI IL FAUT CETTE TABLE, ET POURQUOI ELLE N'EST PAS EVITABLE. Location
/// publie des positions indexees par CHAUFFEUR ; le client, lui, suit une MISSION.
/// Pour router un battement vers le bon groupe, il faut la correspondance, et aucun
/// des deux cotes ne la porte : Location ignore ce qu'est une mission, et le
/// battement ne transporte rien d'autre que le point.
///
/// ELLE EST DANS REDIS, PAS EN MEMOIRE, ET C'EST LA DECISION QUI COMPTE ICI. En
/// memoire, elle serait propre a chaque replique du service — or les evenements
/// Kafka arrivent sur UNE SEULE replique, celle qui detient la partition, tandis que
/// les battements Redis arrivent sur TOUTES. Une replique qui recoit une position
/// sans avoir vu l'affectation ne saurait pas ou l'envoyer : avec deux repliques, un
/// client sur deux perdrait le point qui bouge, sans erreur nulle part. Redis est
/// partage, donc le probleme n'existe pas.
///
/// LA LECTURE COUTE UN HGET PAR BATTEMENT ET PAR REPLIQUE. A la cadence reelle — un
/// point toutes les vingt secondes par chauffeur en ligne — cent chauffeurs font
/// cinq lectures par seconde. Ce n'est pas un chemin chaud, c'est un filet d'eau.
///
/// ELLE EST ALIMENTEE PAR DEUX CHEMINS, ET LES DEUX SONT NECESSAIRES :
///
/// 1. L'EVENEMENT D'AFFECTATION, via Kafka. C'est le chemin normal : le chauffeur
///    est affecte, le client voit le point apparaitre.
/// 2. L'ABONNEMENT, qui demande l'etat de la mission au service proprietaire et
///    inscrit ce qu'il y lit. C'est le chemin de REPARATION : un client qui se
///    reconnecte apres un redemarrage du hub retrouve son point sans attendre le
///    prochain evenement — qui, pour une mission deja affectee, ne viendra jamais.
///
/// Sans le second, un redemarrage de ce service aurait fige la carte de toutes les
/// missions en cours jusqu'a leur cloture.
/// </remarks>
internal sealed class AnnuaireDesMissions(
    IConnectionMultiplexer redis,
    IOptions<RealtimeOptions> options)
{
    /// <summary>
    /// Mission → chauffeur. Un hachage, et non une cle par mission.
    ///
    /// UN HACHAGE NE SAIT PAS FAIRE EXPIRER UN CHAMP — seulement la cle entiere. Le
    /// garde-fou de duree de vie porte donc sur le hachage complet, ce qui est
    /// acceptable : il est reecrit en permanence par les affectations, donc son
    /// expiration est sans cesse repoussee tant que la plateforme tourne. L'expiration
    /// ne se declenche vraiment que si plus aucune mission n'est affectee pendant
    /// douze heures — auquel cas il n'y a rien a perdre.
    /// </summary>
    private const string CleDesMissions = "realtime:mission-driver";

    /// <summary>Chauffeur → missions. L'index inverse, pour router un battement.</summary>
    ///
    /// <remarks>
    /// POURQUOI LES DEUX SENS. Le battement arrive avec un chauffeur et demande « quelles
    /// missions ? » ; la cloture arrive avec une mission et demande « quel chauffeur ? »
    /// pour nettoyer l'index inverse. Ne garder qu'un sens obligerait a balayer tout le
    /// hachage a chaque battement, ou a laisser l'index inverse se remplir sans fin.
    ///
    /// UN CHAUFFEUR PEUT AVOIR PLUSIEURS MISSIONS, et ce n'est pas theorique : le
    /// referentiel lui en interdit deux A LA FOIS, mais la mission precedente peut
    /// n'etre pas encore clotturee dans ce service au moment ou la suivante commence.
    /// Un ensemble, donc, pas une chaine.
    /// </remarks>
    private const string PrefixeDuChauffeur = "realtime:driver-missions:";

    private readonly RealtimeOptions _options = options.Value;

    /// <summary>Inscrit l'affectation d'un chauffeur a une mission.</summary>
    public async Task AffecterAsync(ServiceType nature, string missionId, string driverId)
    {
        if (string.IsNullOrWhiteSpace(missionId) || string.IsNullOrWhiteSpace(driverId))
        {
            return;
        }

        var mission = Groupes.Mission(nature, missionId);
        var db = redis.GetDatabase();
        var duree = TimeSpan.FromHours(_options.MissionDriverTtlHours);

        // UN ANCIEN CHAUFFEUR SE RETIRE AVANT QUE LE NOUVEAU N'ENTRE. Une mission
        // reaffectee — l'exploitation reprend une course a quelqu'un qui ne repond plus
        // — laisserait sinon le premier dans l'index inverse, et sa position
        // continuerait de partir vers un client qui ne l'attend plus.
        var precedent = await db.HashGetAsync(CleDesMissions, mission).ConfigureAwait(false);

        if (!precedent.IsNullOrEmpty && precedent.ToString() != driverId)
        {
            await db.SetRemoveAsync(Inverse(precedent.ToString()!), mission).ConfigureAwait(false);
        }

        var lot = db.CreateBatch();

        var direct = lot.HashSetAsync(CleDesMissions, mission, driverId);
        var inverse = lot.SetAddAsync(Inverse(driverId), mission);
        var bail = lot.KeyExpireAsync(CleDesMissions, duree);
        var bailInverse = lot.KeyExpireAsync(Inverse(driverId), duree);

        lot.Execute();

        await Task.WhenAll(direct, inverse, bail, bailInverse).ConfigureAwait(false);
    }

    /// <summary>
    /// Oublie une mission : elle est close, annulee, ou sans chauffeur.
    ///
    /// C'EST CE QUI ETEINT LE SUIVI, et c'est la borne que le point 37 exige : la
    /// position du chauffeur s'arrete a la cloture, en meme temps que son telephone.
    /// Sans cet appel, un client qui garde l'ecran ouvert suivrait le chauffeur chez son
    /// client suivant, puis chez lui.
    /// </summary>
    public async Task OublierAsync(ServiceType nature, string missionId)
    {
        if (string.IsNullOrWhiteSpace(missionId))
        {
            return;
        }

        var mission = Groupes.Mission(nature, missionId);
        var db = redis.GetDatabase();

        var chauffeur = await db.HashGetAsync(CleDesMissions, mission).ConfigureAwait(false);

        var lot = db.CreateBatch();
        var direct = lot.HashDeleteAsync(CleDesMissions, mission);

        var inverse = chauffeur.IsNullOrEmpty
            ? Task.FromResult(false)
            : lot.SetRemoveAsync(Inverse(chauffeur.ToString()!), mission);

        lot.Execute();

        await Task.WhenAll(direct, inverse).ConfigureAwait(false);
    }

    /// <summary>
    /// Les groupes de mission vers lesquels router le battement de ce chauffeur.
    ///
    /// REND DES NOMS DE GROUPES, PAS DES IDENTIFIANTS DE MISSION, parce que c'est tout
    /// ce dont l'appelant a besoin et que la correspondance est deja faite ici. Rendre
    /// des identifiants obligerait le diffuseur a reconstruire les noms, donc a
    /// connaitre la nature de chaque mission — et une erreur de reconstruction est
    /// exactement la fuite que Groupes existe pour empecher.
    /// </summary>
    public async Task<IReadOnlyList<string>> GroupesDuChauffeurAsync(string driverId)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            return [];
        }

        var membres = await redis.GetDatabase()
            .SetMembersAsync(Inverse(driverId))
            .ConfigureAwait(false);

        return membres.Length == 0 ? [] : [.. membres.Select(m => m.ToString())];
    }

    private static string Inverse(string driverId) => PrefixeDuChauffeur + driverId.ToLowerInvariant();
}

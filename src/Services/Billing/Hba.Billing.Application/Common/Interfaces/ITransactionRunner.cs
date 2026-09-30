namespace Hba.Billing.Application.Common.Interfaces;

/// <summary>
/// Exécute un travail dans UNE transaction, ouverte avant la première lecture et
/// validée après la dernière écriture.
/// </summary>
///
/// <remarks>
/// POURQUOI CE PORT EXISTE, ET C'EST LA PANNE LA PLUS DISCRÈTE QU'ON AIT EUE.
///
/// Le débit prend un verrou pessimiste de ligne — <c>SELECT … FOR UPDATE</c>,
/// dans <c>GetForUpdateAsync</c> — parce que deux courses du même donneur d'ordre
/// à la même milliseconde liraient sinon le même solde et passeraient toutes les
/// deux le plafond. Or POSTGRESQL RELÂCHE CE VERROU À LA FIN DE L'INSTRUCTION
/// quand aucune transaction n'est ouverte. Le commentaire du dépôt le disait déjà
/// noir sur blanc : « sans transaction ouverte, ce verrou ne vaut rien ».
///
/// AUCUN APPELANT N'EN OUVRAIT. <c>BeginTransaction</c> n'apparaissait qu'une
/// seule fois dans tout le dépôt : dans le test d'intégration du verrou, qui
/// l'ouvrait lui-même. Le test passait donc sur un montage que le service ne
/// reproduisait pas — une garantie testée, documentée, et absente à l'exécution.
/// C'est le pire cas de figure, parce qu'il inspire confiance.
///
/// CE QUI SAUVAIT L'INVARIANT EN ATTENDANT n'était pas le verrou mais le jeton
/// <c>xmin</c> : la seconde écriture échouait en conflit de concurrence, donc en
/// « erreur interne » et non en refus métier. Le donneur d'ordre lisait
/// « une erreur est survenue » là où le service croyait lui répondre
/// « solde insuffisant ».
///
/// UN PORT PLUTÔT QU'UN APPEL DIRECT : la couche Application ne connaît pas EF
/// Core, et c'est une règle du dépôt que les tests d'architecture vérifient.
/// </remarks>
public interface ITransactionRunner
{
    /// <summary>
    /// Ouvre une transaction, exécute <paramref name="travail"/>, valide. Toute
    /// exception annule tout : le verrou est relâché et rien n'est écrit.
    /// </summary>
    ///
    /// <remarks>
    /// REENTRANT : appelé alors qu'une transaction est déjà ouverte, il réutilise
    /// celle-là sans en ouvrir une seconde et sans valider à la sortie — c'est
    /// l'appelant le plus extérieur qui décide. PostgreSQL n'imbrique pas les
    /// transactions, et deux « BEGIN » de suite feraient perdre le verrou pris
    /// entre les deux.
    /// </remarks>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> travail, CancellationToken cancellationToken);
}

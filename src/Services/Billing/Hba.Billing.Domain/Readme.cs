namespace Hba.Billing.Domain;

/// <summary>
/// Repère du domaine Billing.
///
/// CE SERVICE TIENT LES COMPTES DES DONNEURS D'ORDRE PROFESSIONNELS —
/// commerçants et partenaires — et rien d'autre. Il ne connaît ni les courses,
/// ni les livreurs, ni les clients particuliers. Il n'encaisse pas non plus :
/// c'est Payment qui parle à l'agrégateur, et Billing qui enregistre ce que
/// l'encaissement a produit.
///
/// UN SEUL MODÈLE, UN SEUL PARAMÈTRE. Le prépayé et le postpayé ne sont pas
/// deux modules : ce sont deux valeurs de <see cref="Accounts.SettlementMode"/>
/// et une seule règle de débit, <c>solde + plafond ≥ montant</c>. En prépayé le
/// plafond vaut zéro, donc le solde doit rester positif ; en postpayé il
/// descend sous zéro, et ce solde négatif EST l'encours du mois. Écrire deux
/// chemins aurait produit deux journaux comptables, deux façons de se tromper,
/// et un jour un écart entre les deux.
///
/// ÉTAT : SEUL LE PRÉPAYÉ EST EN SERVICE. La décision du 29 septembre 2026
/// retient le modèle entier mais ne met en service que le prépayé — pas
/// d'encours, pas de relance, pas de recouvrement. Le postpayé n'ajoutera
/// ensuite aucun modèle, seulement un plafond accordé par `finance`, et la
/// facturation périodique qui va avec. Voir les points 1 et 2 des points à
/// trancher, et `facturation-donneurs-dordre.md`.
///
/// CE QUI N'EST PAS DANS CE PROJET, ET QUI EST DÉLIBÉRÉ : la facture, le
/// verrou de ligne et l'unicité de la clé d'idempotence. Les deux derniers sont
/// des garanties de la BASE — `SELECT … FOR UPDATE` et une contrainte unique —
/// et les simuler ici donnerait l'illusion d'une protection que le domaine ne
/// peut pas offrir seul. Le domaine porte la RÈGLE ; la base porte la
/// SÉRIALISATION.
/// </summary>
internal static class DomainPlaceholder
{
    public const string ServiceName = "billing";
}

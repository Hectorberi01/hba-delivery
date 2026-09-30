namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// À laquelle des deux étapes une photo se rapporte.
/// </summary>
///
/// <remarks>
/// DEUX ETAPES, UN SEUL REGIME : c'est la décision du 30 septembre 2026
/// (point 7, question 1). La photo est proposée à la collecte comme à la remise,
/// jamais exigée, et l'étape ne l'attend pas. Cette énumération sert à dire
/// laquelle des deux on rattache — pas à en faire deux règles.
///
/// ELLE NE COMMENCE PAS A ZERO. Un « 0 » par défaut serait un pas-encore-choisi
/// que le compilateur laisserait passer pour une étape valide ; ici, une valeur
/// absente ne ressemble à rien et se voit tout de suite.
/// </remarks>
public enum EtapeDeLaPreuve
{
    Collecte = 1,
    Remise = 2,
}

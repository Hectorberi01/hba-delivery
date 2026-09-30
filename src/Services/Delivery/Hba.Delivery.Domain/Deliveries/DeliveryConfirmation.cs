namespace Hba.Delivery.Domain.Deliveries;

/// <summary>
/// Issue d'une tentative de remise.
/// </summary>
///
/// <remarks>
/// POURQUOI UNE ISSUE RENDUE PLUTOT QU'UNE EXCEPTION. Un code de remise faux
/// n'est pas une anomalie : c'est l'un des deux résultats normaux de l'étape, et
/// il LAISSE UNE TRACE — une tentative de moins sur les cinq. Tant que ce
/// résultat remontait en exception, la trace ne survivait pas à la requête
/// (voir <see cref="Delivery.ConfirmDelivery"/>). Le rendre oblige l'appelant à
/// enregistrer avant de refuser, et c'est exactement ce qu'on veut de lui.
///
/// Ce qui reste levé, à côté : la transition interdite et OTP_LOCKED. Ni l'une
/// ni l'autre n'a produit d'écriture à conserver.
/// </remarks>
public enum DeliveryConfirmation
{
    /// <summary>Code accepté : la course est passée en Delivered.</summary>
    Confirmed = 1,

    /// <summary>
    /// Code refusé : la course n'a pas bougé, mais une tentative a été
    /// consommée et DOIT être enregistrée.
    /// </summary>
    OtpRefused = 2,
}

namespace Hba.Media.Domain.Assets;

/// <summary>
/// À qui appartient un fichier.
///
/// PAS « QUEL SERVICE L'A DÉPOSÉ ». Le client reste propriétaire de sa photo
/// même si c'est Directory qui l'enregistre, et un livreur de ses pièces même
/// si c'est l'administration qui les ouvre. La distinction compte le jour d'une
/// suppression de compte : ce qu'on efface, ce sont les fichiers d'une
/// PERSONNE, pas ceux d'un service.
/// </summary>
public enum MediaOwnerType
{
    Unspecified = 0,
    Customer = 1,
    Driver = 2,
    Merchant = 3,

    /// <summary>Une preuve de livraison appartient à la course, pas à quelqu'un.</summary>
    Delivery = 4,
}

/// <summary>
/// Ce que le fichier EST, et ce n'est pas son type MIME.
///
/// « image/jpeg » ne dit pas si l'image est une photo de profil ou une carte
/// d'identité. Or les deux n'ont ni la même visibilité, ni la même rétention,
/// ni le même degré de sensibilité : déduire la nature du type MIME reviendrait
/// à traiter une pièce d'identité comme une vignette. Elle est donc DÉCLARÉE au
/// dépôt, par le service qui sait ce qu'il dépose.
/// </summary>
public enum MediaKind
{
    Unspecified = 0,
    ProfilePhoto = 1,
    NationalId = 2,
    DrivingLicence = 3,
    VehicleRegistration = 4,
    VehiclePhoto = 5,
    DeliveryProof = 6,
    Invoice = 7,
}

public static class MediaKinds
{
    /// <summary>
    /// Une nature dont un propriétaire ne peut avoir qu'un exemplaire.
    /// </summary>
    ///
    /// <remarks>
    /// LA DIFFÉRENCE EST MÉTIER, PAS TECHNIQUE. Déposer une nouvelle photo de
    /// profil REMPLACE l'ancienne : personne n'a deux visages, et garder les
    /// précédentes accumulerait des portraits que plus rien n'affiche.
    ///
    /// Une pièce du dossier, elle, s'AJOUTE. Si un dossier est rejeté pour une
    /// CNI illisible, l'ancienne doit rester lisible le temps qu'ops compare
    /// les deux — c'est la suppression explicite qui l'efface, jamais un
    /// remplacement silencieux. La règle vient du service Driver, où elle était
    /// écrite dans la forme des clés ; elle est ici, où elle se voit.
    /// </remarks>
    public static bool EstUnique(MediaKind kind) => kind == MediaKind.ProfilePhoto;
}
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Directory.V1;
using Hba.Contracts.Identity.V1;
using Hba.Gateway.Endpoints.Relais;

namespace Hba.Gateway.Endpoints.Client;

/// <summary>
/// Profil du client et adresses favorites. Directory impose le périmètre à
/// partir du jeton : aucune de ces routes ne prend d'identifiant de client.
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapClientProfileEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/client/v1").RequireAuthorization(HbaPolicies.Customer);

        group.MapGet("/me", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.GetCustomerAsync(
                new GetCustomerRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(customer);
        });

        // CREER SA FICHE, QUAND L'INSCRIPTION NE L'A PAS FAIT.
        //
        // Le chemin normal est un evenement : Identity publie AccountRegistered,
        // Directory le consomme et cree le profil. Cet evenement peut se
        // perdre — Directory arrete au mauvais moment, et Kafka ne rejoue pas
        // pour un groupe de consommateurs qui n'existait pas encore. Le client
        // a alors un compte valide, des livraisons qui fonctionnent, et aucun
        // profil : ni nom, ni courriel, ni adresses favorites.
        //
        // AUCUN CORPS DE REQUETE, ET CE N'EST PAS UN OUBLI. La passerelle ne
        // transmet rien : Directory relit le jeton qu'il a lui-meme valide et
        // en tire le nom, le telephone et le courriel. Une route qui aurait
        // accepte un corps aurait laisse ecrire l'identite de quelqu'un
        // d'autre.
        //
        // IDEMPOTENTE : appelee deux fois, elle rend deux fois la meme fiche.
        group.MapPost("/me", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.EnsureCustomerAsync(
                new EnsureCustomerRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(customer);
        });

        group.MapPut("/me", async (
            UpdateProfileDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.UpdateCustomerAsync(
                new UpdateCustomerRequest
                {
                    DisplayName = body.DisplayName ?? string.Empty,
                    Email = body.Email ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(customer);
        });

        // LE CONSENTEMENT WHATSAPP, LU ET RETIRE PAR LE MEME CHEMIN.
        //
        // IDENTITY, PAS DIRECTORY, ET CE N'EST PAS ARBITRAIRE. Le consentement
        // porte sur le TELEPHONE, qu'Identity detient : c'est lui qui envoie le
        // code de connexion, et « CanReceiveWhatsApp » decide deja du canal.
        // Le loger dans le profil d'annuaire aurait oblige a le rapatrier a
        // chaque envoi de code.
        //
        // « PUT » ET NON DEUX ROUTES. Le referentiel veut un consentement
        // explicite ET REVOCABLE : accorder et retirer passent donc par le meme
        // appel, avec un booleen. Une route « accorder » et une route
        // « retirer » auraient tot fait de diverger — l'une journalisee,
        // l'autre non, l'une confirmee, l'autre immediate.
        group.MapGet("/me/whatsapp", async (
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var consent = await identity.GetWhatsAppConsentAsync(
                new GetWhatsAppConsentRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(consent));
        });

        group.MapPut("/me/whatsapp", async (
            WhatsAppConsentDto body,
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var consent = await identity.SetWhatsAppConsentAsync(
                new SetWhatsAppConsentRequest { Granted = body.Granted },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(consent));
        });

        // LA PHOTO DE PROFIL : DEUX SERVICES, UNE SEULE ROUTE POUR LE CLIENT.
        //
        // LE TELEPHONE N'ENVOIE QU'UNE REQUETE, ET C'EST TOUT L'INTERET. Le
        // depot se fait en deux temps — les octets vers Media, l'identifiant
        // obtenu vers Directory — et faire porter cet enchainement a
        // l'application aurait laisse un etat a mi-chemin a chaque coupure de
        // reseau : un fichier depose dans le stockage que le profil ne
        // reclamera jamais. Sur une 3G a Cotonou, ce n'est pas un cas rare.
        //
        // CE N'EST PAS LA PASSERELLE QUI AUTORISE. Elle renseigne le
        // proprietaire depuis le jeton, mais Media verifie qu'on ne depose que
        // pour soi, et Directory refuse un media qui ne serait pas une photo de
        // profil appartenant a l'appelant. Deux services verifient ; aucun ne
        // la croit sur parole (referentiel des acteurs).
        group.MapPost("/me/photo", async (
            HttpRequest requete,
            HttpContext http,
            IMediaUploadRelay media,
            ICallerContext appelant,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            // ICallerContext ET NON UNE LECTURE DE CLAIM A LA MAIN : le sujet
            // d'un jeton se lit sous « nameidentifier » ou sous « sub » selon
            // la couche qui l'a deserialise, et cette bascule est deja ecrite
            // une fois, la. La recopier ici ferait deux endroits a corriger le
            // jour ou elle bouge, dont un qu'on oublierait.
            var depot = await media.DeposerAsync(
                requete,
                http,
                ownerType: "Customer",
                ownerId: appelant.SubjectId,
                kind: "ProfilePhoto",
                cancellationToken);

            if (!depot.EstUnSucces)
            {
                return depot.Refus!;
            }

            var customer = await directory.SetCustomerPhotoAsync(
                new SetCustomerPhotoRequest { MediaId = depot.MediaId.ToString() },
                cancellationToken: cancellationToken);

            return Results.Ok(customer);
        }).DisableAntiforgery();

        group.MapDelete("/me/photo", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.RemoveCustomerPhotoAsync(
                new RemoveCustomerPhotoRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(customer);
        });

        // LE LIEN D'AFFICHAGE, DEMANDE A PART.
        //
        // PAS DANS « GET /me », ET CE N'EST PAS UN OUBLI. Une URL signee expire
        // en quelques minutes : la poser dans la fiche obligerait a appeler
        // Media a chaque lecture de profil — y compris pour les clients qui
        // n'ont pas de photo, c'est-a-dire presque tous au debut — et rendrait
        // fausse toute reponse gardee en memoire par l'application.
        group.MapGet("/me/photo", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var lien = await directory.GetCustomerPhotoLinkAsync(
                new GetCustomerPhotoLinkRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                url = lien.Url,
                expiresAt = lien.ExpiresAt.ToDateTimeOffset(),
            });
        });

        // LA SUPPRESSION DU COMPTE, DEMANDEE PAR SON TITULAIRE (point 28).
        //
        // TROIS ROUTES, PARCE QUE C'EST UNE FENETRE ET NON UN INSTANT. Le
        // client demande ; ses sessions tombent aussitot et le compte entre en
        // sursis ; trente jours plus tard un travail planifie efface pour de
        // bon. Se reconnecter pendant la fenetre permet d'annuler — c'est la
        // raison pour laquelle un compte en sursis peut encore se connecter,
        // contrairement a un compte suspendu.
        //
        // AUCUN CORPS, AUCUN IDENTIFIANT : on ne supprime que son propre
        // compte, et Identity le relit dans le jeton qu'il a lui-meme valide.
        //
        // « DELETE » POUR DEMANDER, ET NON « POST /me/deletion ». Le verbe dit
        // ce que le client croit faire. Ce qui se passe derriere — un sursis
        // plutot qu'un effacement immediat — est ecrit dans la reponse, qui
        // porte la date d'echeance, et dans l'ecran qui l'appelle.
        group.MapDelete("/me/account", async (
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var demande = await identity.RequestAccountDeletionAsync(
                new RequestAccountDeletionRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(demande));
        });

        group.MapPost("/me/account/keep", async (
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var demande = await identity.CancelAccountDeletionAsync(
                new CancelAccountDeletionRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(demande));
        });

        group.MapGet("/me/account/deletion", async (
            IdentityService.IdentityServiceClient identity,
            CancellationToken cancellationToken) =>
        {
            var demande = await identity.GetAccountDeletionAsync(
                new GetAccountDeletionRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(demande));
        });

        // UN OBJET, PAS UN TABLEAU NU, ET C'EST DELIBERE.
        //
        // Les quatre routes d'adresses rendaient « [...] » a la racine. Toutes
        // les autres routes clientes rendent un objet — « { deliveries: [...] } »
        // — et le client HTTP de l'application est type sur un objet JSON : un
        // tableau a la racine ne s'y lit pas du tout.
        //
        // Un tableau nu ferme aussi la porte a tout ajout : le jour ou il
        // faudra rendre un jeton de page ou un total a cote de la liste, il n'y
        // a pas de place pour le mettre sans casser les appelants.
        group.MapGet("/addresses", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.GetCustomerAsync(
                new GetCustomerRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(new { addresses = customer.FavoriteAddresses });
        });

        group.MapPost("/addresses", async (
            SaveAddressDto body,
            HttpContext http,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.AddFavoriteAddressAsync(
                new AddFavoriteAddressRequest
                {
                    Label = body.Label,
                    Address = ToAddress(body, http),
                    SetAsDefault = body.SetAsDefault,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new { addresses = customer.FavoriteAddresses });
        });

        group.MapPut("/addresses/{addressId}", async (
            string addressId,
            SaveAddressDto body,
            HttpContext http,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.UpdateFavoriteAddressAsync(
                new UpdateFavoriteAddressRequest
                {
                    AddressId = addressId,
                    Label = body.Label,
                    Address = ToAddress(body, http),
                    SetAsDefault = body.SetAsDefault,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new { addresses = customer.FavoriteAddresses });
        });

        group.MapDelete("/addresses/{addressId}", async (
            string addressId,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.RemoveFavoriteAddressAsync(
                new RemoveFavoriteAddressRequest { AddressId = addressId },
                cancellationToken: cancellationToken);

            return Results.Ok(new { addresses = customer.FavoriteAddresses });
        });

        return app;
    }

    // LE MESSAGE PROTOBUF NE PART PAS TEL QUEL, ET C'EST LE MEME PIEGE QUE
    // POUR LES LIVRAISONS. System.Text.Json ne connait pas le JSON protobuf :
    // il rendrait un Timestamp en « { seconds, nanos } », que l'application ne
    // sait pas lire. On projette donc, comme partout ailleurs.
    private static object Lisible(WhatsAppConsent consent) => new
    {
        granted = consent.Granted,
        grantedAt = consent.GrantedAt?.ToDateTimeOffset(),
    };

    /// <summary>
    /// Adresse favorite en objet du contrat, contact compris.
    /// </summary>
    ///
    /// <remarks>
    /// LE CONTACT EST OBLIGATOIRE COTE DOMAINE, ET L'APPLICATION NE LE DEMANDE
    /// PAS. Directory refuse une adresse sans telephone joignable ni nom de
    /// contact (INVALID_PHONE, MISSING_CONTACT_NAME) : c'est le meme
    /// objet-valeur que pour un point de collecte, ou quelqu'un attend sur
    /// place. Une adresse favorite, elle, sert a pre-remplir un formulaire.
    ///
    /// ALORS ON LES PREND DU JETON, PAS DU CORPS. C'est son adresse, c'est lui
    /// le contact : le telephone et le nom du COMPTE sont la reponse vraie, pas
    /// un bouchon pour satisfaire une validation. Le corps garde la main quand
    /// il les fournit — « chez maman » a un autre contact que le client — mais
    /// il ne peut pas les laisser vides et esperer que ca passe.
    ///
    /// CE N'EST PAS LA CORRECTION DE FOND. Elle serait de donner a Directory un
    /// objet-valeur propre a l'adresse favorite, sans contact obligatoire ;
    /// cela touche le domaine, la base et le contrat, et n'a pas ete decide.
    /// </remarks>
    private static Address ToAddress(SaveAddressDto dto, HttpContext http) => new()
    {
        Location = new Location
        {
            Point = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude },
            Landmark = dto.Landmark,
            Phone = Ou(dto.Phone, http, HbaClaims.Phone),
            ContactName = Ou(dto.ContactName, http, HbaClaims.Name),
            Notes = dto.Notes ?? string.Empty,
        },
    };

    /// <summary>
    /// La demande de suppression, en JSON lisible par l'application.
    /// </summary>
    ///
    /// <remarks>
    /// LES DEUX DATES SONT NULLES QUAND RIEN N'EST DEMANDE, et non pas egales
    /// a l'epoque Unix. Un Timestamp protobuf non renseigne vaut le 1er janvier
    /// 1970 : le rendre tel quel afficherait « suppression prevue le 01/01/1970 »
    /// sur le telephone d'un client qui n'a rien demande.
    /// </remarks>
    private static object Lisible(AccountDeletion demande) => new
    {
        requested = demande.Requested,
        requestedAt = demande.RequestedAt?.ToDateTimeOffset(),
        scheduledFor = demande.ScheduledFor?.ToDateTimeOffset(),
    };

    private static string Ou(string? fourni, HttpContext http, string claim)
        => string.IsNullOrWhiteSpace(fourni)
            ? http.User.FindFirst(claim)?.Value ?? string.Empty
            : fourni;
}

public sealed record UpdateProfileDto(string? DisplayName, string? Email);

public sealed record WhatsAppConsentDto(bool Granted);

public sealed record SaveAddressDto(
    string Label,
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes,
    bool SetAsDefault);

using System.Net.Mime;

namespace Hba.Gateway.Endpoints.Public;

/// <summary>
/// Page d'atterrissage du payeur apres la page de paiement du fournisseur.
///
/// ELLE NE DECIDE DE RIEN, ET C'EST TOUT SON INTERET. Le fournisseur y renvoie
/// le navigateur du client une fois qu'il a quitte sa page — qu'il ait paye,
/// abandonne, ou ferme l'onglet. Ce retour ne prouve donc aucun paiement :
/// seul le webhook signe, relu aupres du fournisseur, fait foi (ADR 0017).
///
/// Elle existe parce que le fournisseur exige une adresse de retour, et parce
/// qu'un client renvoye vers une page d'erreur croit que son paiement a echoue
/// alors qu'il est peut-etre deja encaisse.
/// </summary>
internal static class PaymentReturnEndpoint
{
    private const string Page = """
        <!doctype html>
        <html lang="fr">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Paiement HBA</title>
          <style>
            :root { color-scheme: light dark; }
            body {
              margin: 0; min-height: 100vh; display: grid; place-items: center;
              font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
              padding: 24px; text-align: center;
            }
            main { max-width: 22rem; }
            h1 { font-size: 1.25rem; margin: 0 0 .75rem; }
            p { margin: 0 0 .5rem; opacity: .8; }
          </style>
        </head>
        <body>
          <main>
            <h1>Merci</h1>
            <p>Votre paiement est en cours de confirmation.</p>
            <p>Vous pouvez revenir à l'application : la course s'y mettra à jour toute seule.</p>
          </main>
        </body>
        </html>
        """;

    public static IEndpointRouteBuilder MapPaymentReturnEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/paiement/retour", () => Results.Content(Page, MediaTypeNames.Text.Html))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("PaymentReturn");

        return endpoints;
    }
}

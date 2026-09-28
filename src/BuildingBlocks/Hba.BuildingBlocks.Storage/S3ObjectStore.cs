using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Hba.BuildingBlocks.Storage;

/// <summary>
/// Adaptateur S3 du port <see cref="IObjectStore"/>.
///
/// SEUL FICHIER DU DÉPÔT QUI CONNAISSE LE SDK. C'est la condition que
/// l'ADR 0021 pose à la réversibilité du choix de moteur : Garage,
/// SeaweedFS et un stockage managé parlent tous S3, mais cette équivalence ne
/// vaut que si le SDK ne fuit nulle part. Le nom de la classe dit « S3 » et
/// non « Garage » pour la même raison.
///
/// LE SDK S'APPELLE « MINIO » ET LE MOTEUR N'EST PAS MINIO : ce client
/// implémente le protocole S3, pas un protocole propriétaire. La référence
/// NuGet était morte depuis que MinIO a été retiré ; elle reprend du service
/// telle quelle.
/// </summary>
public sealed class S3ObjectStore : IObjectStore
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _signataire;
    private readonly ObjectStoreOptions _options;
    private readonly ILogger<S3ObjectStore> _logger;

    /// <param name="client">Celui qui écrit, lit et supprime, par le réseau interne.</param>
    /// <param name="signataire">
    /// Celui qui signe les URL de lecture, contre l'adresse publique. Il n'ouvre
    /// jamais de connexion : signer est un calcul local.
    /// </param>
    public S3ObjectStore(
        IMinioClient client,
        [FromKeyedServices(ObjectStoreOptions.PresigningClientKey)] IMinioClient signataire,
        IOptions<ObjectStoreOptions> options,
        ILogger<S3ObjectStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _signataire = signataire;
        _options = options.Value;
        _logger = logger;
    }

    public TimeSpan ReadUrlLifetime => TimeSpan.FromSeconds(_options.UrlTtlSeconds);

    public async Task<StoredObject> PutAsync(
        string key,
        Stream content,
        long sizeBytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        var args = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(sizeBytes)
            .WithContentType(contentType);

        try
        {
            await _client.PutObjectAsync(args, cancellationToken).ConfigureAwait(false);
        }
        catch (MinioException exception)
        {
            // LE MESSAGE DU SDK NE DIT PAS OÙ IL A ÉCHOUÉ. « Connection
            // refused » sans le bucket ni l'hôte envoie chercher du côté du
            // réseau alors qu'un « make garage-init » oublié suffit à
            // l'expliquer.
            _logger.LogError(
                exception,
                "Écriture refusée par le stockage : bucket {Bucket}, clé {Cle}, endpoint {Endpoint}.",
                _options.Bucket,
                key,
                _options.Endpoint);

            throw new InvalidOperationException(
                $"Le stockage objet a refusé l'écriture de « {key} ». Vérifiez que Garage "
                + "est initialisé (make garage-init) et que le bucket existe.",
                exception);
        }

        return new StoredObject(key, sizeBytes, contentType);
    }

    public async Task<Uri> GetReadUrlAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var args = new PresignedGetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(key)
            .WithExpiry(_options.UrlTtlSeconds);

        // La signature est un calcul local : aucun appel réseau, donc rien à
        // annuler. Le jeton est là pour la forme du port, pas pour le SDK.
        cancellationToken.ThrowIfCancellationRequested();

        // LE SIGNATAIRE, PAS LE CLIENT. Cette URL sera suivie par un
        // navigateur ou un téléphone, qui ne résolvent pas « garage » ; et
        // SigV4 couvre l'en-tête Host, donc l'adresse publique doit être
        // celle contre laquelle on signe, pas une réécriture après coup.
        var url = await _signataire.PresignedGetObjectAsync(args).ConfigureAwait(false);

        return new Uri(url);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var args = new RemoveObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(key);

        try
        {
            await _client.RemoveObjectAsync(args, cancellationToken).ConfigureAwait(false);
        }
        catch (MinioException exception)
        {
            // UNE SUPPRESSION QUI ÉCHOUE NE DOIT PAS FAIRE ÉCHOUER L'APPELANT.
            // Elle intervient au remplacement d'une pièce : refuser la
            // nouvelle parce que l'ancienne résiste laisserait le livreur
            // bloqué sur une pièce rejetée. L'objet orphelin se nettoie, le
            // dossier bloqué non.
            _logger.LogWarning(
                exception,
                "Suppression impossible dans le stockage : clé {Cle}. L'objet reste en place.",
                key);
        }
    }
}

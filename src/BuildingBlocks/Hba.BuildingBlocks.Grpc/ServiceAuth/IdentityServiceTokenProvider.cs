using Grpc.Core;
using Grpc.Net.Client;
using Hba.Contracts.Identity.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Grpc.ServiceAuth;

/// <summary>
/// Demande le jeton du service à Identity, une fois, puis le réutilise.
///
/// SON CANAL EST A LUI, SANS AUCUN INTERCEPTEUR. Passer par un client
/// enregistré avec AddHbaGrpcClient ferait appeler ce fournisseur par
/// l'intercepteur de jeton pour obtenir… un jeton : la première demande
/// s'appellerait elle-même.
/// </summary>
internal sealed class IdentityServiceTokenProvider : IServiceTokenProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ServiceTokenOptions _options;
    private readonly IHttpContextAccessor _accessor;
    private readonly ILogger<IdentityServiceTokenProvider> _logger;
    private readonly GrpcChannel _channel;
    private readonly IdentityService.IdentityServiceClient _client;

    private string? _token;
    private DateTimeOffset _renewAt = DateTimeOffset.MinValue;

    public IdentityServiceTokenProvider(
        IOptions<ServiceTokenOptions> options,
        IHttpContextAccessor accessor,
        ILogger<IdentityServiceTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _accessor = accessor;
        _logger = logger;
        _channel = GrpcChannel.ForAddress(_options.IdentityAddress);
        _client = new IdentityService.IdentityServiceClient(_channel);
    }

    public async ValueTask<Metadata> AuthorizationAsync(CancellationToken cancellationToken)
    {
        // UN UTILISATEUR PASSE AVANT LE SERVICE. Si l'appel descend d'une
        // requete HTTP porteuse d'un jeton, on ne pose rien : l'intercepteur
        // reportera celui de l'appelant, et le service appele appliquera ses
        // regles a la personne, pas au service. Le jeton de service, sans
        // role, les contournerait plutot que de les satisfaire.
        var utilisateur = _accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(utilisateur))
        {
            return new Metadata();
        }

        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        return new Metadata { { GrpcMetadataKeys.Authorization, $"Bearer {token}" } };
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _renewAt)
        {
            return _token;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Relecture sous verrou : pendant l'attente, un autre appel a pu
            // renouveler. Sans cette ligne, dix consommateurs qui redémarrent
            // ensemble demandent dix jetons.
            if (_token is not null && DateTimeOffset.UtcNow < _renewAt)
            {
                return _token;
            }

            var response = await _client.IssueServiceTokenAsync(
                new IssueServiceTokenRequest
                {
                    ClientId = _options.ClientId,
                    ClientSecret = _options.ClientSecret,
                },
                deadline: DateTime.UtcNow.AddSeconds(_options.RequestTimeoutSeconds),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var lifetime = TimeSpan.FromSeconds(Math.Max(response.ExpiresInSeconds, 60));
            var margin = TimeSpan.FromSeconds(_options.RenewBeforeSeconds);

            // Une marge plus longue que la durée de vie ferait redemander un
            // jeton à chaque appel, sans jamais en garder aucun.
            if (margin >= lifetime)
            {
                margin = lifetime / 2;
            }

            _token = response.AccessToken;
            _renewAt = DateTimeOffset.UtcNow.Add(lifetime - margin);

            _logger.LogInformation(
                "Jeton de service obtenu pour {ClientId}, valable {Seconds} s.",
                _options.ClientId,
                response.ExpiresInSeconds);

            return _token;
        }
        catch (RpcException exception) when (exception.StatusCode is StatusCode.PermissionDenied
                                                 or StatusCode.Unauthenticated)
        {
            // MAUVAIS SECRET : ce n'est pas passager, aucune reprise ne le
            // corrigera. On le dit clairement plutôt que de laisser l'appelant
            // conclure à une panne d'Identity.
            _logger.LogError(
                exception,
                "Identity refuse les identifiants du service {ClientId} : ServiceToken__ClientSecret ici et "
                + "ServiceClients__Clients__{ClientIdChezIdentity} cote Identity doivent porter la meme valeur.",
                _options.ClientId,
                _options.ClientId);

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}

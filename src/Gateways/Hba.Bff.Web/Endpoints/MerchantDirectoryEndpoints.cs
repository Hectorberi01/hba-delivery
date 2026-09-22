using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Directory.V1;

namespace Hba.Bff.Web.Endpoints;

/// <summary>
/// Fiche du commerçant et points de collecte. Le périmètre vient du claim
/// merchant_id : ces routes ne prennent pas d'identifiant de commerçant, et
/// Directory refuse de toute façon ceux d'un autre.
/// </summary>
public static class MerchantDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapMerchantDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/merchant/v1").RequireAuthorization(HbaPolicies.Merchant);

        group.MapGet("/profile", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.GetMerchantAsync(
                new GetMerchantRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(merchant);
        });

        group.MapPut("/profile", async (
            UpdateMerchantDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.UpdateMerchantAsync(
                new UpdateMerchantRequest
                {
                    LegalName = body.LegalName ?? string.Empty,
                    ContactName = body.ContactName ?? string.Empty,
                    ContactPhone = body.ContactPhone ?? string.Empty,
                    ContactEmail = body.ContactEmail ?? string.Empty,
                    AveragePreparationMinutes = body.AveragePreparationMinutes ?? 0,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(merchant);
        });

        group.MapGet("/pickup-points", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.GetMerchantAsync(
                new GetMerchantRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(merchant.PickupPoints);
        });

        group.MapPost("/pickup-points", async (
            SavePickupPointDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var request = new AddPickupPointRequest
            {
                Name = body.Name,
                Address = ToAddress(body),
            };

            request.OpeningHours.AddRange(ToOpeningHours(body.OpeningHours));

            var merchant = await directory.AddPickupPointAsync(request, cancellationToken: cancellationToken);

            return Results.Ok(merchant.PickupPoints);
        });

        group.MapPut("/pickup-points/{pickupPointId}", async (
            string pickupPointId,
            SavePickupPointDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var request = new UpdatePickupPointRequest
            {
                PickupPointId = pickupPointId,
                Name = body.Name,
                Address = ToAddress(body),
            };

            request.OpeningHours.AddRange(ToOpeningHours(body.OpeningHours));

            var merchant = await directory.UpdatePickupPointAsync(request, cancellationToken: cancellationToken);

            return Results.Ok(merchant.PickupPoints);
        });

        group.MapPost("/pickup-points/{pickupPointId}/active", async (
            string pickupPointId,
            SetActiveDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var merchant = await directory.SetPickupPointActiveAsync(
                new SetPickupPointActiveRequest { PickupPointId = pickupPointId, Active = body.Active },
                cancellationToken: cancellationToken);

            return Results.Ok(merchant.PickupPoints);
        });

        return app;
    }

    private static Address ToAddress(SavePickupPointDto dto) => new()
    {
        Location = new Location
        {
            Point = new GeoPoint { Latitude = dto.Latitude, Longitude = dto.Longitude },
            Landmark = dto.Landmark,
            Phone = dto.Phone,
            ContactName = dto.ContactName,
            Notes = dto.Notes ?? string.Empty,
        },
    };

    private static IEnumerable<OpeningHours> ToOpeningHours(IReadOnlyList<OpeningHoursDto>? hours)
        => hours is null
            ? []
            : hours.Select(h => new OpeningHours
            {
                DayOfWeek = h.IsoDay,
                OpensAtMinutes = h.OpensAtMinutes,
                ClosesAtMinutes = h.ClosesAtMinutes,
            });
}

public sealed record UpdateMerchantDto(
    string? LegalName,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    int? AveragePreparationMinutes);

public sealed record OpeningHoursDto(int IsoDay, int OpensAtMinutes, int ClosesAtMinutes);

public sealed record SavePickupPointDto(
    string Name,
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes,
    IReadOnlyList<OpeningHoursDto>? OpeningHours);

public sealed record SetActiveDto(bool Active);

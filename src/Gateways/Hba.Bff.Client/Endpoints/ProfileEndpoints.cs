using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Directory.V1;

namespace Hba.Bff.Client.Endpoints;

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

        group.MapGet("/addresses", async (
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.GetCustomerAsync(
                new GetCustomerRequest(),
                cancellationToken: cancellationToken);

            return Results.Ok(customer.FavoriteAddresses);
        });

        group.MapPost("/addresses", async (
            SaveAddressDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.AddFavoriteAddressAsync(
                new AddFavoriteAddressRequest
                {
                    Label = body.Label,
                    Address = ToAddress(body),
                    SetAsDefault = body.SetAsDefault,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(customer.FavoriteAddresses);
        });

        group.MapPut("/addresses/{addressId}", async (
            string addressId,
            SaveAddressDto body,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.UpdateFavoriteAddressAsync(
                new UpdateFavoriteAddressRequest
                {
                    AddressId = addressId,
                    Label = body.Label,
                    Address = ToAddress(body),
                    SetAsDefault = body.SetAsDefault,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(customer.FavoriteAddresses);
        });

        group.MapDelete("/addresses/{addressId}", async (
            string addressId,
            DirectoryService.DirectoryServiceClient directory,
            CancellationToken cancellationToken) =>
        {
            var customer = await directory.RemoveFavoriteAddressAsync(
                new RemoveFavoriteAddressRequest { AddressId = addressId },
                cancellationToken: cancellationToken);

            return Results.Ok(customer.FavoriteAddresses);
        });

        return app;
    }

    private static Address ToAddress(SaveAddressDto dto) => new()
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
}

public sealed record UpdateProfileDto(string? DisplayName, string? Email);

public sealed record SaveAddressDto(
    string Label,
    double Latitude,
    double Longitude,
    string Landmark,
    string Phone,
    string ContactName,
    string? Notes,
    bool SetAsDefault);

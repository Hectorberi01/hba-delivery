using FluentValidation;
using Hba.Delivery.Domain.ValueObjects;

namespace Hba.Delivery.Application.Commands.CreateDelivery;

public sealed class CreateDeliveryValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryValidator()
    {
        RuleFor(x => x.QuoteId).NotEmpty();

        RuleFor(x => x.RecipientName)
            .NotEmpty()
            .WithMessage("Le nom du destinataire est obligatoire.");

        RuleFor(x => x.RecipientPhone)
            .Must(PhoneNumber.IsValid)
            .WithMessage("Le destinataire doit avoir un téléphone au format international (+229…).");

        RuleFor(x => x.PackageWeightGrams)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Pickup).NotNull().SetValidator(new LocationInputValidator());
        RuleFor(x => x.Dropoff).NotNull().SetValidator(new LocationInputValidator());
    }
}

internal sealed class LocationInputValidator : AbstractValidator<LocationInput>
{
    public LocationInputValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);

        RuleFor(x => x.Landmark)
            .NotEmpty()
            .WithMessage("Un repère écrit est obligatoire : l'adressage formel n'est pas fiable au Bénin.");

        RuleFor(x => x.Phone)
            .Must(PhoneNumber.IsValid)
            .WithMessage("Un téléphone joignable sur place est obligatoire.");

        RuleFor(x => x.ContactName).NotEmpty();
    }
}

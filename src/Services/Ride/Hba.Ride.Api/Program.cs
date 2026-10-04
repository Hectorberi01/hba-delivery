using Hba.BuildingBlocks.Grpc.Interceptors;
using Hba.BuildingBlocks.Observability;
using Hba.BuildingBlocks.Security;
using Hba.Ride.Application;
using Hba.Ride.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaObservability("ride");

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddSingleton<ExceptionInterceptor>();
builder.Services.AddGrpcHealthChecks();

builder.Services.AddHbaSecurity(builder.Configuration);

builder.Services.AddRideApplication();
builder.Services.AddRideInfrastructure(builder.Configuration);

// CE QUI FAIT AVANCER UNE COURSE : le moteur de dispatch. Sans ce consommateur,
// une course restait a REQUESTED pour toujours — les trois commandes internes
// existaient et rien ne les appelait.
// CE QUI MET UNE COURSE EN ROUTE : l'encaissement. Le prix est encaisse avant la
// recherche d'un chauffeur (point 35), donc c'est ce consommateur qui declenche
// tout le reste.
// LE BALAYAGE DES IMPAYEES, calque sur celui de Delivery (point 30). Sans lui,
// une course abandonnee au paiement restait en Requested indefiniment.
builder.Services.AddOptions<Hba.Ride.Application.Features.Rides.Commands.Internal.UnpaidRideOptions>()
    .Bind(builder.Configuration.GetSection(
        Hba.Ride.Application.Features.Rides.Commands.Internal.UnpaidRideOptions.Section));
builder.Services.AddHostedService<Hba.Ride.Api.Scheduling.AbandonDesCoursesImpayees>();

builder.Services.AddHostedService<Hba.Ride.Api.Messaging.PaymentEventsConsumer>();

builder.Services.AddHostedService<Hba.Ride.Api.Messaging.DispatchEventsConsumer>();

var app = builder.Build();

app.UseHbaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGrpcService<Hba.Ride.Api.Grpc.RideGrpcService>();
app.MapGrpcHealthChecksService();

app.MapGet("/", () => Results.Text("Hba.Ride.Api", "text/plain"));

await app.RunAsync();

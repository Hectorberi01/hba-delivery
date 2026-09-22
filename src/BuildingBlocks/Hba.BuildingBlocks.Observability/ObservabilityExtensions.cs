using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Hba.BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Traces, métriques et logs structurés, tous exportés en OTLP vers le
    /// collecteur. Un seul appel par hôte.
    /// </summary>
    public static IHostApplicationBuilder AddHbaObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://otel-collector:4317";

        builder.Services.AddSerilog((services, configuration) => configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service.name", serviceName)
            .WriteTo.Console(new CompactJsonFormatter())
            .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
            }));

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                }))
            .WithTracing(tracing => tracing
                .AddSource(HbaTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                .AddHttpClientInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter(HbaTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));

        return builder;
    }

    /// <summary>Journalisation des requêtes HTTP entrantes.</summary>
    public static WebApplication UseHbaRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseSerilogRequestLogging();
        return app;
    }
}

using Bicep.Extension.IncidentIo;
using Bicep.Extension.IncidentIo.Handlers;
using Bicep.Local.Extension.Host.Extensions;

var builder = WebApplication.CreateBuilder();

builder.AddBicepExtensionHost(args);
builder.Services
    .AddBicepExtension()
    .WithDefaults("incidentio", ThisAssembly.Version, isSingleton: true)
    .WithTypeAssembly<Program>()
    .WithConfigurationType<Configuration>()
    // Incident configuration
    .WithResourceHandler<SeverityHandler>()
    .WithResourceHandler<IncidentRoleHandler>()
    .WithResourceHandler<IncidentStatusHandler>()
    .WithResourceHandler<CustomFieldHandler>()
    .WithResourceHandler<CustomFieldOptionHandler>()
    // Catalog
    .WithResourceHandler<CatalogTypeHandler>()
    .WithResourceHandler<CatalogEntryHandler>()
    // On-call
    .WithResourceHandler<UserHandler>()
    .WithResourceHandler<ScheduleHandler>()
    .WithResourceHandler<ScheduleRotationHandler>()
    .WithResourceHandler<EscalationPathHandler>()
    // Alerts
    .WithResourceHandler<AlertAttributeHandler>()
    .WithResourceHandler<AlertSourceHandler>()
    .WithResourceHandler<MaintenanceWindowHandler>();

var app = builder.Build();
app.MapBicepExtension();
await app.RunAsync();

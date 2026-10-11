namespace Bicep.Extension.IncidentIo.Handlers;

public class IncidentStatusHandler : NamedResourceHandler<IncidentStatus>
{
    protected override string ApiPath => "v1/incident_statuses";
    protected override string ItemKey => "incident_status";
    protected override string CollectionKey => "incident_statuses";

    // The update payload has no 'category' — moving a status between categories is not supported by the API.
    protected override IReadOnlyCollection<string> CreateOnlyFields => ["category"];
}

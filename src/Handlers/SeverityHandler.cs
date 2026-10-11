namespace Bicep.Extension.IncidentIo.Handlers;

public class SeverityHandler : NamedResourceHandler<Severity>
{
    protected override string ApiPath => "v1/severities";
    protected override string ItemKey => "severity";
    protected override string CollectionKey => "severities";
}

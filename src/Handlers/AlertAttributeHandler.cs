namespace Bicep.Extension.IncidentIo.Handlers;

public class AlertAttributeHandler : NamedResourceHandler<AlertAttribute>
{
    protected override string ApiPath => "v2/alert_attributes";
    protected override string ItemKey => "alert_attribute";
    protected override string CollectionKey => "alert_attributes";
}

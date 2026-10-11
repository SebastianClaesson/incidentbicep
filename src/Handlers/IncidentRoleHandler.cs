namespace Bicep.Extension.IncidentIo.Handlers;

public class IncidentRoleHandler : NamedResourceHandler<IncidentRole>
{
    protected override string ApiPath => "v2/incident_roles";
    protected override string ItemKey => "incident_role";
    protected override string CollectionKey => "incident_roles";
}

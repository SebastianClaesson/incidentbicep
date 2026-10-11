namespace Bicep.Extension.IncidentIo.Handlers;

public class CustomFieldHandler : NamedResourceHandler<CustomField>
{
    protected override string ApiPath => "v2/custom_fields";
    protected override string ItemKey => "custom_field";
    protected override string CollectionKey => "custom_fields";

    protected override IReadOnlyCollection<string> CreateOnlyFields => ["field_type", "catalog_type_id"];
}

using System.Text.Json.Serialization;
using Azure.Bicep.Types.Concrete;
using Bicep.Local.Extension.Types.Attributes;

namespace Bicep.Extension.IncidentIo;

// ──────────────────────────────────────────────
// Extension configuration and shared bases
// ──────────────────────────────────────────────

/// <summary>
/// Extension-level configuration passed via: extension incidentio with { apiKey: '...' }
/// </summary>
public class Configuration
{
    [TypeProperty("incident.io API key (Settings > API keys). Pass it from a @secure() parameter.", ObjectTypePropertyFlags.Required, isSecure: true)]
    public required string ApiKey { get; set; }

    [TypeProperty("API base URL. Defaults to 'https://api.incident.io'. Must be HTTPS (plain HTTP is only accepted for localhost testing).")]
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Output properties returned by incident.io after create/update.
/// Every resource exposes the server-generated ID so other resources can reference it (e.g. customField.id).
/// </summary>
public interface IIncidentIoResource
{
    string? Id { get; set; }
}

/// <summary>
/// Identifier for resources whose natural unique key is their name (severities, roles, statuses, ...).
/// incident.io generates opaque IDs, so the extension finds existing resources by listing and matching the name.
/// </summary>
public class NameIdentifiers : IIncidentIoResource
{
    [TypeProperty("Unique name. Used to find the existing resource, so renaming creates a new resource.", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string Name { get; set; }

    [TypeProperty("incident.io ID (read-only, returned after create/update).", ObjectTypePropertyFlags.ReadOnly)]
    public string? Id { get; set; }
}

// ──────────────────────────────────────────────
// Enums — member names ARE the Bicep literal values, which is why they are lower/snake case.
// [JsonConverter(typeof(JsonStringEnumConverter))] on each property makes both the SDK (gRPC)
// and the incident.io serializer use the member name verbatim.
// ──────────────────────────────────────────────

#pragma warning disable IDE1006 // Naming styles: names must match incident.io API values exactly
public enum IncidentStatusCategory { live, learning, closed }

public enum CustomFieldType { single_select, multi_select, text, link, numeric }

public enum CatalogTypeColor { yellow, green, blue, violet, pink, cyan, orange }

public enum CatalogAttributeMode { api, dashboard, external, backlink }
#pragma warning restore IDE1006

// ──────────────────────────────────────────────
// Incident configuration
// ──────────────────────────────────────────────

/// <summary>
/// Incident severity (e.g. Minor, Major, Critical).
/// API: /v1/severities
/// </summary>
[ResourceType("Severity")]
public class Severity : NameIdentifiers
{
    [TypeProperty("Description of the severity.", ObjectTypePropertyFlags.Required)]
    public required string Description { get; set; }

    [TypeProperty("Sort rank. Lower numbers are less severe.")]
    public int? Rank { get; set; }
}

/// <summary>
/// A role that can be assigned during an incident (e.g. Communications Lead).
/// API: /v2/incident_roles
/// </summary>
[ResourceType("IncidentRole")]
public class IncidentRole : NameIdentifiers
{
    [TypeProperty("Describes the purpose of the role.", ObjectTypePropertyFlags.Required)]
    public required string Description { get; set; }

    [TypeProperty("Instructions shown to whoever is nominated for the role.", ObjectTypePropertyFlags.Required)]
    public required string Instructions { get; set; }

    [TypeProperty("Short name used in Slack (e.g. 'comms').", ObjectTypePropertyFlags.Required)]
    public required string Shortform { get; set; }
}

/// <summary>
/// A custom incident status within the 'live' (Active), 'learning' (Post-incident) or 'closed' category.
/// API: /v1/incident_statuses
/// </summary>
[ResourceType("IncidentStatus")]
public class IncidentStatus : NameIdentifiers
{
    [TypeProperty("Category: 'live' (shown as Active), 'learning' (shown as Post-incident) or 'closed'. Cannot be changed after creation.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required IncidentStatusCategory Category { get; set; }

    [TypeProperty("Rich text description of the status.", ObjectTypePropertyFlags.Required)]
    public required string Description { get; set; }

    [TypeProperty("Position within its category, lowest first. Must be unique within the category.")]
    public int? Rank { get; set; }
}

/// <summary>
/// A custom field captured on incidents.
/// API: /v2/custom_fields
/// </summary>
[ResourceType("CustomField")]
public class CustomField : NameIdentifiers
{
    [TypeProperty("Description of the custom field.", ObjectTypePropertyFlags.Required)]
    public required string Description { get; set; }

    [TypeProperty("Field type. Cannot be changed after creation.", ObjectTypePropertyFlags.Required)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required CustomFieldType FieldType { get; set; }

    [TypeProperty("For catalog-backed select fields: the catalog type ID that provides the options. Cannot be changed after creation.")]
    public string? CatalogTypeId { get; set; }

    [TypeProperty("For catalog fields: the attribute ID used to group catalog entries.")]
    public string? GroupByCatalogAttributeId { get; set; }

    [TypeProperty("For catalog fields: the attribute ID that provides help text for options.")]
    public string? HelptextCatalogAttributeId { get; set; }
}

/// <summary>
/// An option for a single_select / multi_select custom field.
/// API: /v1/custom_field_options
/// </summary>
public class CustomFieldOptionIdentifiers : IIncidentIoResource
{
    [TypeProperty("ID of the custom field this option belongs to (e.g. myField.id).", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string CustomFieldId { get; set; }

    [TypeProperty("Option value shown to users. Unique within the custom field.", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string Value { get; set; }

    [TypeProperty("incident.io ID (read-only, returned after create/update).", ObjectTypePropertyFlags.ReadOnly)]
    public string? Id { get; set; }
}

[ResourceType("CustomFieldOption")]
public class CustomFieldOption : CustomFieldOptionIdentifiers
{
    [TypeProperty("Sort key used to order options.")]
    public int? SortKey { get; set; }
}

// ──────────────────────────────────────────────
// Catalog
// ──────────────────────────────────────────────

/// <summary>
/// One attribute in a catalog type's schema.
/// </summary>
public class CatalogTypeAttribute
{
    [TypeProperty("Unique attribute name. Existing attributes are matched by name so their IDs (and data) are kept.", ObjectTypePropertyFlags.Required)]
    public required string Name { get; set; }

    [TypeProperty("Attribute type: 'String', 'Text', 'Number', 'Bool', 'IncidentSeverity', 'User', or another catalog type name such as 'Custom[\"Team\"]' (use otherType.typeName).", ObjectTypePropertyFlags.Required)]
    public required string Type { get; set; }

    [TypeProperty("Whether the attribute holds a list of values. Defaults to false.")]
    public bool? Array { get; set; }

    [TypeProperty("Who manages the attribute's values: 'api' (default when managed by Bicep), 'dashboard', 'external' or 'backlink'.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CatalogAttributeMode? Mode { get; set; }

    [TypeProperty("For mode 'backlink': the ID of the attribute on the other type to follow back.")]
    public string? BacklinkAttribute { get; set; }
}

/// <summary>
/// A catalog type (e.g. Service, Team, Customer) and, optionally, its attribute schema.
/// API: /v3/catalog_types (+ /actions/update_schema when 'attributes' is set)
/// </summary>
[ResourceType("CatalogType")]
public class CatalogType : NameIdentifiers
{
    [TypeProperty("Human readable description.", ObjectTypePropertyFlags.Required)]
    public required string Description { get; set; }

    [TypeProperty("Type name used when referencing this type from attributes, e.g. 'Custom[\"Service\"]'. Generated from name if omitted. Cannot be changed after creation.")]
    public string? TypeName { get; set; }

    [TypeProperty("Display color in the dashboard.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CatalogTypeColor? Color { get; set; }

    [TypeProperty("Display icon (e.g. 'server', 'users', 'bolt', 'database', 'globe', 'escalation-path').")]
    public string? Icon { get; set; }

    [TypeProperty("Categories this type belongs to (e.g. 'service', 'team', 'customer', 'product', 'issue-tracker').")]
    public string[]? Categories { get; set; }

    [TypeProperty("Whether entries of this type are ranked.")]
    public bool? Ranked { get; set; }

    [TypeProperty("Allow entries to be referenced by name as well as by external ID.")]
    public bool? UseNameAsIdentifier { get; set; }

    [TypeProperty("URL of the repository that manages this type (shown in the dashboard). Tip: point it at the repo holding this Bicep file.")]
    public string? SourceRepoUrl { get; set; }

    [TypeProperty("IDs of the teams that own this catalog type.")]
    public string[]? OwningTeamIds { get; set; }

    [TypeProperty("Free-form annotations (key/value metadata).")]
    public Dictionary<string, string>? Annotations { get; set; }

    [TypeProperty("Attribute schema. When set, the full schema is replaced: attributes not listed are removed. Omit to leave the schema unmanaged.")]
    public CatalogTypeAttribute[]? Attributes { get; set; }

    [TypeProperty("Map of attribute name to attribute ID (read-only). Useful for custom fields that need attribute IDs.", ObjectTypePropertyFlags.ReadOnly)]
    public Dictionary<string, string>? AttributeIds { get; set; }
}

/// <summary>
/// A value for one catalog entry attribute. Set 'value' for single attributes or 'arrayValue' for array attributes.
/// </summary>
public class CatalogEntryAttributeValue
{
    [TypeProperty("Literal value for a non-array attribute. For references to other catalog entries use the entry ID or external ID.")]
    public string? Value { get; set; }

    [TypeProperty("Literal values for an array attribute.")]
    public string[]? ArrayValue { get; set; }
}

/// <summary>
/// An entry in a catalog type (e.g. one service).
/// API: /v3/catalog_entries
/// </summary>
public class CatalogEntryIdentifiers : IIncidentIoResource
{
    [TypeProperty("ID of the catalog type (e.g. serviceType.id).", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string CatalogTypeId { get; set; }

    [TypeProperty("Entry name. Used to find the entry when externalId is not set.", ObjectTypePropertyFlags.Required | ObjectTypePropertyFlags.Identifier)]
    public required string Name { get; set; }

    [TypeProperty("Stable external ID, unique within the type. Recommended: when set, it is used to find the entry, so the name can change freely.", ObjectTypePropertyFlags.Identifier)]
    public string? ExternalId { get; set; }

    [TypeProperty("incident.io ID (read-only, returned after create/update).", ObjectTypePropertyFlags.ReadOnly)]
    public string? Id { get; set; }
}

[ResourceType("CatalogEntry")]
public class CatalogEntry : CatalogEntryIdentifiers
{
    [TypeProperty("Alternative identifiers for referencing this entry.")]
    public string[]? Aliases { get; set; }

    [TypeProperty("Ordering rank when the catalog type is ranked.")]
    public int? Rank { get; set; }

    [TypeProperty("Attribute values keyed by attribute NAME (resolved automatically) or attribute ID. Attributes not listed are cleared.")]
    public Dictionary<string, CatalogEntryAttributeValue>? AttributeValues { get; set; }
}

// ──────────────────────────────────────────────
// Alerts
// ──────────────────────────────────────────────

/// <summary>
/// An attribute that can be extracted from incoming alerts and used for routing.
/// API: /v2/alert_attributes
/// </summary>
[ResourceType("AlertAttribute")]
public class AlertAttribute : NameIdentifiers
{
    [TypeProperty("Engine type: e.g. 'String', 'Number', 'Bool', or 'CatalogEntry[\"<catalog type ID>\"]'.", ObjectTypePropertyFlags.Required)]
    public required string Type { get; set; }

    [TypeProperty("Whether the attribute holds a list of values.", ObjectTypePropertyFlags.Required)]
    public required bool Array { get; set; }

    [TypeProperty("Whether the attribute is required on alerts.")]
    public bool? Required { get; set; }

    [TypeProperty("Emoji shown next to the attribute in chat messages, without colons (e.g. 'rotating_light').")]
    public string? Emoji { get; set; }
}

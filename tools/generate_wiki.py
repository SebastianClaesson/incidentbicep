"""
Generates wiki/<Resource>.md property tables from the extension's own Bicep type schema,
so the docs can never drift from Models.cs. Prose and examples live in RESOURCES below.

Usage (after ./scripts/publish.ps1):
    python tools/generate_wiki.py extension-publish/bicep-ext-incidentio
"""
import io
import json
import sys
import tarfile
from pathlib import Path

REQUIRED, READ_ONLY, IDENTIFIER = 1, 2, 16

RESOURCES = {
    "Severity": {
        "summary": "An incident severity, such as Minor, Major or Critical.",
        "api": "/v1/severities",
        "found_by": "`name` (case-insensitive)",
        "notes": [],
        "example": """resource sevMajor 'Severity' = {
  name: 'Major'
  description: 'Significant customer impact. Respond immediately.'
  rank: 2
}

// Built-in severities can be referenced without managing them:
resource critical 'Severity' existing = {
  name: 'Critical'
}""",
    },
    "IncidentRole": {
        "summary": "A role that can be assigned during an incident.",
        "api": "/v2/incident_roles",
        "found_by": "`name` (case-insensitive)",
        "notes": [],
        "example": """resource commsLead 'IncidentRole' = {
  name: 'Communications Lead'
  shortform: 'comms'
  description: 'Owns internal and external communication during the incident.'
  instructions: 'Post an update to the status page and #incidents at least every 30 minutes.'
}""",
    },
    "IncidentStatus": {
        "summary": "A custom incident status in the Active (`live`), Post-incident (`learning`) or `closed` category.",
        "api": "/v1/incident_statuses",
        "found_by": "`name` (case-insensitive)",
        "notes": [
            "`category` can't be changed after creation. Changing it fails the deployment with `ImmutableProperty`.",
            "Statuses in other categories (triage, declined, merged, paused) are managed by incident.io.",
        ],
        "example": """resource monitoring 'IncidentStatus' = {
  name: 'Monitoring'
  category: 'live'
  description: 'A fix is deployed and we are watching for recurrence.'
}""",
    },
    "CustomField": {
        "summary": "A custom field captured on incidents.",
        "api": "/v2/custom_fields",
        "found_by": "`name` (case-insensitive)",
        "notes": [
            "`fieldType` and `catalogTypeId` can't be changed after creation.",
            "For `single_select`/`multi_select` fields, add options with [CustomFieldOption](CustomFieldOption.md) or back the field with a catalog type via `catalogTypeId`.",
        ],
        "example": """resource affectedArea 'CustomField' = {
  name: 'Affected Area'
  description: 'Which part of the product is affected?'
  fieldType: 'single_select'
}

resource affectedServices 'CustomField' = {
  name: 'Affected Services'
  description: 'Which services are affected?'
  fieldType: 'multi_select'
  catalogTypeId: serviceType.id
}""",
    },
    "CustomFieldOption": {
        "summary": "An option of a select custom field.",
        "api": "/v1/custom_field_options",
        "found_by": "`customFieldId` + `value` (case-insensitive)",
        "notes": ["If `sortKey` is left out on update, the current sort key is kept."],
        "example": """resource areaOptions 'CustomFieldOption' = [
  for (area, i) in ['API', 'Web app', 'Mobile app']: {
    customFieldId: affectedArea.id
    value: area
    sortKey: (i + 1) * 10
  }
]""",
    },
    "CatalogType": {
        "summary": "A catalog type (for example Service, Team or Customer), optionally including its attribute schema.",
        "api": "/v3/catalog_types (schema: /v3/catalog_types/{id}/actions/update_schema)",
        "found_by": "`name` (case-insensitive)",
        "notes": [
            "`typeName` can't be changed after creation. Reference it from other types' attributes (`type: teamType.typeName`).",
            "**`attributes` replaces the whole schema:** attributes you don't list are removed, together with their values on entries. Existing attributes are matched by name, so their IDs and values survive.",
            "Leave out `attributes` to leave the schema unmanaged (for example, when an external sync owns it).",
            "`attributeIds` (output) maps each attribute name to its ID.",
        ],
        "example": """resource serviceType 'CatalogType' = {
  name: 'Service'
  typeName: 'Custom["Service"]'
  description: 'Production services, managed in Bicep.'
  color: 'blue'
  icon: 'server'
  categories: ['service']
  annotations: {
    'managed-by': 'bicep'
  }
  attributes: [
    { name: 'Tier', type: 'String' }
    { name: 'Owner', type: teamType.typeName }
    { name: 'Runbooks', type: 'String', array: true }
  ]
}""",
    },
    "CatalogEntry": {
        "summary": "An entry in a catalog type.",
        "api": "/v3/catalog_entries",
        "found_by": "`catalogTypeId` + `externalId` when set (recommended), otherwise `catalogTypeId` + `name`",
        "notes": [
            "`attributeValues` keys can be attribute **names** (resolved from the type's schema) or attribute IDs.",
            "Use `value` for single attributes and `arrayValue` for array attributes. To reference another catalog entry, use its ID or external ID.",
            "Attributes you don't list in `attributeValues` are cleared. Leave out `attributeValues` entirely to leave all values untouched.",
        ],
        "example": """resource paymentsApi 'CatalogEntry' = {
  catalogTypeId: serviceType.id
  externalId: 'payments-api'
  name: 'Payments API'
  aliases: ['payments']
  attributeValues: {
    Tier: { value: '1' }
    Owner: { value: 'payments' }   // external ID of a Team entry
    Runbooks: { arrayValue: ['https://runbooks.example.com/payments'] }
  }
}""",
    },
    "AlertAttribute": {
        "summary": "An attribute extracted from incoming alerts, used for routing and display.",
        "api": "/v2/alert_attributes",
        "found_by": "`name` (case-insensitive)",
        "notes": [],
        "example": """resource alertService 'AlertAttribute' = {
  name: 'Service'
  type: 'CatalogEntry["${serviceType.id}"]'
  array: false
  emoji: 'gear'
}""",
    },
}


def load_types(package: Path):
    with tarfile.open(package) as outer:
        types_tgz = outer.extractfile("types.tgz").read()
    with tarfile.open(fileobj=io.BytesIO(types_tgz)) as inner:
        return json.loads(inner.extractfile("types.json").read())


def type_name(types, ref):
    t = types[int(ref["$ref"].split("/")[-1])]
    kind = t["$type"]
    if kind == "StringType":
        return "string (secure)" if t.get("sensitive") else "string"
    if kind == "IntegerType":
        return "int"
    if kind == "BooleanType":
        return "bool"
    if kind == "ArrayType":
        return type_name(types, t["itemType"]) + "[]"
    if kind == "ObjectType":
        if t.get("additionalProperties"):
            return "object (map of " + type_name(types, t["additionalProperties"]) + ")"
        return t["name"]
    if kind == "StringLiteralType":
        return f"'{t['value']}'"
    if kind == "UnionType":
        parts = [type_name(types, e) for e in t["elements"] if types[int(e["$ref"].split("/")[-1])]["$type"] != "NullType"]
        return " \\| ".join(parts)
    return kind


def table(types, obj, include):
    rows = []
    for name, prop in obj["properties"].items():
        flags = prop.get("flags", 0)
        if not include(flags):
            continue
        required = "Yes" if flags & REQUIRED else "No"
        rows.append(f"| `{name}` | {type_name(types, prop['type'])} | {required} | {prop.get('description', '')} |")
    return rows


def nested_objects(types, obj, seen):
    for prop in obj["properties"].values():
        t = types[int(prop["type"]["$ref"].split("/")[-1])]
        if t["$type"] == "ArrayType":
            t = types[int(t["itemType"]["$ref"].split("/")[-1])]
        if t["$type"] == "ObjectType" and t.get("additionalProperties"):
            t = types[int(t["additionalProperties"]["$ref"].split("/")[-1])]
        if t["$type"] == "ObjectType" and "properties" in t and t["properties"] and t["name"] not in seen:
            seen.add(t["name"])
            yield t


def main():
    package = Path(sys.argv[1] if len(sys.argv) > 1 else "extension-publish/bicep-ext-incidentio")
    types = load_types(package)
    objects = {t["name"]: t for t in types if t["$type"] == "ObjectType"}
    out = Path(__file__).resolve().parent.parent / "wiki"
    out.mkdir(exist_ok=True)

    for name, meta in RESOURCES.items():
        obj = objects[name]
        lines = [f"# {name}", "", meta["summary"], "", "## API endpoint", "", f"`{meta['api']}`", "",
                 "## Found by", "", meta["found_by"] + ". Changing the value used for lookup makes the extension create a new resource.", "",
                 "## Properties", "", "| Property | Type | Required | Description |", "|---|---|---|---|"]
        lines += table(types, obj, lambda f: not f & READ_ONLY)
        for nested in nested_objects(types, obj, set()):
            lines += ["", f"### {nested['name']}", "", "| Property | Type | Required | Description |", "|---|---|---|---|"]
            lines += table(types, nested, lambda f: True)
        lines += ["", "## Output properties", "", "| Property | Type | Required | Description |", "|---|---|---|---|"]
        lines += table(types, obj, lambda f: f & READ_ONLY)
        if meta["notes"]:
            lines += ["", "## Notes", ""] + [f"- {n}" for n in meta["notes"]]
        lines += ["", "## Bicep example", "", "```bicep", meta["example"], "```", ""]
        (out / f"{name}.md").write_text("\n".join(lines), encoding="utf-8")

    home = ["# bicep-ext-incidentio wiki", "", "Reference for every resource type. The property tables are generated from the extension schema by `tools/generate_wiki.py`.", "",
            "| Resource | Description |", "|---|---|"]
    home += [f"| [{n}]({n}.md) | {m['summary']} |" for n, m in RESOURCES.items()]
    home += ["", "## Extension configuration", "", "```bicep", "extension incidentio with {", "  apiKey: apiKey      // @secure() parameter", "  baseUrl: baseUrl    // optional, defaults to https://api.incident.io", "}", "```", ""]
    (out / "Home.md").write_text("\n".join(home), encoding="utf-8")
    print(f"wrote {len(RESOURCES) + 1} pages to {out}")


if __name__ == "__main__":
    main()

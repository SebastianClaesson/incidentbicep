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
    "User": {
        "summary": "Look up a user by email to get their ID (Terraform: `data \"incident_user\"`). Read-only: use `existing`.",
        "api": "/v2/users",
        "found_by": "`email` (case-insensitive)",
        "notes": ["Users can't be created or changed by Bicep. Declaring one without `existing` fails with `ReadOnlyResource`."],
        "example": """resource alice 'User' existing = {
  email: 'alice@example.com'
}

output aliceId string = alice.id""",
    },
    "Schedule": {
        "summary": "An on-call schedule. Its rotations are separate `ScheduleRotation` resources, as in Terraform v7.",
        "api": "/v3/schedules (the endpoint the Terraform provider uses; not in the public API docs)",
        "found_by": "`name` (case-insensitive)",
        "notes": ["`timezone` can't be changed after creation; rotations are anchored to it."],
        "example": """resource platform 'Schedule' = {
  name: 'Platform on-call'
  timezone: 'Europe/Stockholm'
  holidaysPublicConfig: {
    countryCodes: ['SE']
  }
}""",
    },
    "ScheduleRotation": {
        "summary": "A rotation on a schedule: who takes turns being on call, and the cadence they hand over on.",
        "api": "/v3/schedules/{scheduleId}/rotations (the endpoint the Terraform provider uses; not in the public API docs)",
        "found_by": "`scheduleId` + `name` (case-insensitive)",
        "notes": [
            "**Unchanged rotations are never written.** A write with `rollout` schedules a new version of the rotation even when nothing changed, so the extension compares the desired rotation with the live one first. Working intervals compare as a set, but user order matters.",
            "`rollout` only applies when the rotation changes: `immediate` replaces the line-up now, `after_current_shift` lets the current shift finish, and `after_full_rotation` waits for a full cycle.",
            "`firstIntervalStartsAt` can't change in the same deployment as a `rollout`. Change it first with `rollout` unset, then make the line-up change.",
            "`users` takes user IDs, which you can look up with `User` existing. Use `NOBODY` for a slot with nobody in it; an empty list is rejected.",
        ],
        "example": """resource primary 'ScheduleRotation' = {
  scheduleId: platform.id
  name: 'Primary'
  firstIntervalStartsAt: '2026-01-05T08:00:00Z'
  handovers: [
    { interval: 1, intervalType: 'weekly' }
  ]
  users: [alice.id, bob.id]
  rollout: 'after_current_shift'
}""",
    },
    "EscalationPath": {
        "summary": "An escalation path, written as a flat map of named node sequences (the Terraform v7 shape).",
        "api": "/v2/escalation_paths",
        "found_by": "`name` (case-insensitive)",
        "notes": [
            "Each sequence runs its nodes in order. A `branch`, which must be the last node, names the sequences to continue down. Without one, the path ends after the last node.",
            "A `loop` can only go back to the path's first node or to a branch it sits under. It must be last in its sequence, or be followed by a single `escalationPath` handoff.",
            "Node ids are optional. An id is derived from the node's position (`<sequence>/<index>`), so it stays stable. Set `id` only on nodes that a loop goes back to.",
            "`ackMode` defaults to `first`, as in Terraform v7: the first person to acknowledge cancels everyone else's page on that level. Set `ackMode: 'all'` to keep the old behaviour.",
            "Validation follows the Terraform provider. It rejects unknown or unreachable sequences, cycles, a sequence branched to twice, and nodes that set zero or several blocks, and it reports every problem at once.",
            "When read with `existing`, sequence names are recovered from derived node ids. If that isn't possible, the start is called `main` and branch targets `<sequence>_then` and `<sequence>_else`.",
        ],
        "example": """resource platform 'EscalationPath' = {
  name: 'Platform'
  start: 'main'
  sequences: {
    main: {
      nodes: [
        {
          id: 'start'
          branch: {
            if: { workingHoursActive: 'office' }
            then: 'in_hours'
            else: 'out_of_hours'
          }
        }
      ]
    }
    in_hours: {
      nodes: [
        { level: { targets: [{ type: 'schedule', id: schedule.id, urgency: 'high', scheduleMode: 'currently_on_call' }], timeToAckSeconds: 300 } }
        { delay: { delaySeconds: 120 } }
        { loop: { backTo: 'start', times: 2 } }
      ]
    }
    out_of_hours: {
      nodes: [
        { level: { targets: [{ type: 'user', id: alice.id, urgency: 'low' }], timeToAckSeconds: 900 } }
        { escalationPath: { escalationPathId: fallback.id } }
      ]
    }
  }
  workingHours: [
    {
      id: 'office'
      name: 'Office hours'
      timezone: 'Europe/Stockholm'
      weekdayIntervals: [for day in ['monday', 'tuesday', 'wednesday', 'thursday', 'friday']: { weekday: day, startTime: '08:00', endTime: '18:00' }]
    }
  ]
}""",
    },
    "AlertSource": {
        "summary": "An alert source: where alerts come from and how their title, description and other fields are derived.",
        "api": "/v3/alert_sources (the endpoint the Terraform provider uses; not in the public API docs)",
        "found_by": "`name` (case-insensitive)",
        "notes": [
            "`sourceType` can't be changed after creation.",
            "Bindings accept the Terraform shorthands `valueLiteral`, `valueReference`, `values` and `expressionRef`, or the full `value` / `arrayValue`. Set exactly one.",
            "`expressions` uses the API shape (`reference`, `rootReference`, `operations`, `elseBranch`). Terraform's `named_expression` blocks, with their `fallback`/`if`/`else_if` shorthand, aren't supported yet.",
            "`secretToken` is a sensitive output. Pass it straight to where it's needed, such as a Key Vault secret, rather than printing it.",
            "Alert source attributes (Terraform's `incident_alert_source_attribute`) aren't supported yet.",
        ],
        "example": """resource httpSource 'AlertSource' = {
  name: 'Platform HTTP alerts'
  sourceType: 'http'
  title: { valueReference: 'payload.title' }
  description: { valueLiteral: '{{ payload.description }}' }
  filterConditionGroups: [
    {
      conditions: [
        { subject: 'payload.environment', operation: 'is_not', paramBindings: [{ valueLiteral: 'test' }] }
      ]
    }
  ]
}

output alertEventsUrl string = httpSource.alertEventsUrl""",
    },
    "MaintenanceWindow": {
        "summary": "A maintenance window that holds back matching alerts.",
        "api": "/v1/maintenance_windows",
        "found_by": "`name` (case-insensitive), ignoring archived windows",
        "notes": [
            "Window names should be unique among non-archived windows. More than one match fails with `AmbiguousMatch`.",
            "`showInSidebar` defaults to `false`.",
        ],
        "example": """resource dbUpgrade 'MaintenanceWindow' = {
  name: 'Database upgrade 2026-11'
  startAt: '2026-11-14T22:00:00Z'
  endAt: '2026-11-15T02:00:00Z'
  leadId: alice.id
  alertConditionGroups: [
    {
      conditions: [
        { subject: 'alert.source', operation: 'one_of', paramBindings: [{ values: [httpSource.id] }] }
      ]
    }
  ]
  resolveOnEnd: true
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
            yield from nested_objects(types, t, seen)  # escalation paths nest several levels deep


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

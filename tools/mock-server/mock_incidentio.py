"""
Minimal in-memory mock of the incident.io API for local testing of the Bicep extension.

It implements only the endpoints the extension uses, with the same JSON envelopes
(e.g. {"severity": {...}}, {"severities": [...]}), request wrappers ({"schedule": {...}}),
nested routes (/v3/schedules/{id}/rotations) and cursor pagination.
It is NOT a faithful copy of incident.io validation rules.

Usage:
    python tools/mock-server/mock_incidentio.py [port]     (default 8787)
    $env:INCIDENT_IO_BASE_URL = 'http://127.0.0.1:8787'
    $env:INCIDENT_IO_API_KEY  = 'test-key'

Debug endpoints:
    GET  /_state   -> full in-memory state and the list of API calls made
    POST /_reset   -> clear state
"""
import json
import re
import sys
import uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

EXPECTED_KEY = "test-key"
PAGE_CAP = 2  # tiny pages on purpose, to exercise the extension's pagination loop


class Resource:
    def __init__(self, pattern, item_key, coll_key, paginated=False, create_only=(), wrapper=None, unique_name=True):
        self.name = pattern
        self.pattern = re.compile("^" + pattern + "$")  # may capture a parent id
        self.item_key = item_key
        self.coll_key = coll_key
        self.paginated = paginated
        self.create_only = list(create_only)
        self.wrapper = wrapper
        self.unique_name = unique_name


RESOURCES = [
    Resource(r"v1/severities", "severity", "severities"),
    Resource(r"v2/incident_roles", "incident_role", "incident_roles"),
    Resource(r"v1/incident_statuses", "incident_status", "incident_statuses", create_only=["category"]),
    Resource(r"v2/custom_fields", "custom_field", "custom_fields", create_only=["field_type", "catalog_type_id"]),
    Resource(r"v1/custom_field_options", "custom_field_option", "custom_field_options", paginated=True, create_only=["custom_field_id"], unique_name=False),
    Resource(r"v3/catalog_types", "catalog_type", "catalog_types", create_only=["type_name"]),
    Resource(r"v3/catalog_entries", "catalog_entry", "catalog_entries", paginated=True, create_only=["catalog_type_id"], unique_name=False),
    Resource(r"v2/alert_attributes", "alert_attribute", "alert_attributes"),
    Resource(r"v2/users", "user", "users", paginated=True),
    Resource(r"v3/schedules", "schedule", "schedules", paginated=True, create_only=["timezone"], wrapper="schedule"),
    Resource(r"v3/schedules/([^/]+)/rotations", "rotation", "rotations", paginated=True, wrapper="rotation"),
    Resource(r"v2/escalation_paths", "escalation_path", "escalation_paths", paginated=True, create_only=["kind"]),
    Resource(r"v1/maintenance_windows", "maintenance_window", "maintenance_windows", paginated=True, unique_name=False),
    Resource(r"v3/alert_sources", "alert_source", "alert_sources", create_only=["source_type"], wrapper="alert_source"),
]

STATE = {}  # "<resource pattern>|<parent id>" -> [items]
CALLS = []


def reset():
    STATE.clear()
    CALLS.clear()
    # A built-in severity and two users so 'existing' lookups can be tested.
    bucket(RESOURCES[0], None).append({"id": "01SEVCRITICAL", "name": "Critical", "description": "Built in", "rank": 3})
    users = bucket(next(r for r in RESOURCES if r.coll_key == "users"), None)
    users.append({"id": "01USERALICE", "email": "alice@example.com", "name": "Alice", "role": "responder", "slack_user_id": "UALICE"})
    users.append({"id": "01USERBOB", "email": "bob@example.com", "name": "Bob", "role": "responder", "slack_user_id": "UBOB"})


def bucket(resource, parent):
    return STATE.setdefault(f"{resource.name}|{parent or ''}", [])


def new_id():
    return "01" + uuid.uuid4().hex[:24].upper()


def now():
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def find_resource(path):
    """Returns (resource, parent id, item id) for a collection or item path."""
    for resource in RESOURCES:
        m = resource.pattern.match(path)
        if m:
            return resource, (m.group(1) if m.groups() else None), None
        head, _, item_id = path.rpartition("/")
        m = resource.pattern.match(head)
        if m and item_id:
            return resource, (m.group(1) if m.groups() else None), item_id
    return None, None, None


class Handler(BaseHTTPRequestHandler):
    # Keep-alive like the real API; HTTP/1.0 closes every connection, which trips up pooled clients.
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("mock: " + fmt % args + "\n")

    def send(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def error(self, status, message):
        self.send(status, {"type": "error", "status": status, "errors": [{"message": message}]})

    def body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}")

    def route(self, method):
        url = urlparse(self.path)
        path = url.path.strip("/")
        query = {k: v[0] for k, v in parse_qs(url.query).items()}

        if path == "_state":
            return self.send(200, {"state": STATE, "calls": CALLS})
        if path == "_reset":
            reset()
            return self.send(200, {})
        if self.headers.get("Authorization") != f"Bearer {EXPECTED_KEY}":
            return self.error(401, "invalid API key")

        CALLS.append(f"{method} /{path}")

        m = re.fullmatch(r"v3/catalog_types/([^/]+)/actions/update_schema", path)
        if m and method == "POST":
            return self.update_schema(m.group(1))

        resource, parent, item_id = find_resource(path)
        if resource is None:
            return self.error(404, f"no route for {method} /{path}")
        items = bucket(resource, parent)

        if item_id is None:
            if method == "GET":
                return self.list(resource, items, query)
            if method == "POST":
                return self.create(resource, items, parent)
        else:
            item = next((i for i in items if i["id"] == item_id), None)
            if item is None:
                return self.error(404, "not found")
            if method == "GET":
                return self.send(200, {resource.item_key: item})
            if method == "PUT":
                return self.update(resource, item)
        self.error(405, "method not allowed")

    def unwrap(self, resource, body):
        if resource.wrapper is None:
            return body, {}
        if resource.wrapper not in body:
            return None, {}
        extra = {k: v for k, v in body.items() if k != resource.wrapper}
        return body[resource.wrapper], extra

    def list(self, resource, items, query):
        key = resource.coll_key
        if key == "custom_field_options":
            if "custom_field_id" not in query:
                return self.error(422, "custom_field_id is required")
            items = [i for i in items if i["custom_field_id"] == query["custom_field_id"]]
        if key == "catalog_entries":
            if "catalog_type_id" not in query or "page_size" not in query:
                return self.error(422, "catalog_type_id and page_size are required")
            items = [i for i in items if i["catalog_type_id"] == query["catalog_type_id"]]
            if "identifier" in query:
                ident = query["identifier"]
                items = [i for i in items if ident in (i["id"], i.get("external_id")) or ident in i.get("aliases", [])]
        if key == "users" and "email" in query:
            items = [i for i in items if i["email"].lower() == query["email"].lower()]
        if not resource.paginated:
            return self.send(200, {key: items})

        size = min(int(query.get("page_size", 25)), PAGE_CAP)
        start = 0
        if "after" in query:
            ids = [i["id"] for i in items]
            start = ids.index(query["after"]) + 1 if query["after"] in ids else len(items)
        page = items[start:start + size]
        after = page[-1]["id"] if len(page) == size and start + size < len(items) else None
        self.send(200, {key: page, "pagination_meta": {"after": after, "page_size": size}})

    def create(self, resource, items, parent):
        payload, _ = self.unwrap(resource, self.body())
        if payload is None:
            return self.error(422, f"request body must be wrapped in '{resource.wrapper}'")
        if resource.coll_key == "users":
            return self.error(405, "users are read-only")
        if resource.unique_name and "name" in payload:
            if any(i.get("name", "").lower() == payload["name"].lower() for i in items):
                return self.error(422, "name must be unique")
        problem = self.validate(resource, payload, creating=True)
        if problem:
            return self.error(422, problem)

        item = {"id": new_id(), **payload}
        self.decorate(resource, item, parent, created=True)
        items.append(item)
        self.send(201, {resource.item_key: item})

    def update(self, resource, item):
        payload, extra = self.unwrap(resource, self.body())
        if payload is None:
            return self.error(422, f"request body must be wrapped in '{resource.wrapper}'")
        bad = [f for f in resource.create_only if f in payload]
        if bad:
            return self.error(422, f"unknown fields on update: {bad}")
        problem = self.validate(resource, payload, creating=False)
        if problem:
            return self.error(422, problem)

        item.update(payload)
        item.pop("update_attributes", None)
        if resource.coll_key == "rotations" and extra.get("rollout"):
            item["effective_from"] = now()  # a rollout introduces a new version of the rotation
        self.decorate(resource, item, None, created=False)
        self.send(200, {resource.item_key: item})

    def validate(self, resource, payload, creating):
        key = resource.coll_key
        if key == "schedules" and creating and not payload.get("timezone"):
            return "timezone is required"
        if key == "rotations":
            for field in ("name", "users", "handovers") + (("first_interval_starts_at",) if creating else ()):
                if field not in payload:
                    return f"{field} is required"
        if key == "maintenance_windows":
            for field in ("name", "start_at", "end_at", "lead", "alert_condition_groups", "show_in_sidebar"):
                if field not in payload:
                    return f"{field} is required"
        if key == "escalation_paths" and payload.get("kind") != "templated" and "path" not in payload:
            return "path is required"
        if key == "alert_sources" and not creating and "is_private" not in payload:
            return "is_private is required"
        if key == "alert_sources" and creating and "disabled" in payload:
            return "disabled can't be set on create"
        return None

    def decorate(self, resource, item, parent, created):
        """Adds the server-side fields the real API returns."""
        key = resource.coll_key
        if key == "catalog_types" and created:
            item.setdefault("type_name", 'Custom["%s"]' % re.sub(r"[^A-Za-z0-9]", "", item["name"]))
            item["schema"] = {"version": 1, "attributes": []}
        if key == "rotations":
            if created:
                item["schedule_id"] = parent
                item.setdefault("effective_from", None)
            # The API returns full user objects.
            item["users"] = [{"id": u["id"], "name": u["id"].title(), "email": f"{u['id'].lower()}@example.com"} for u in item["users"]]
        if key == "alert_sources":
            item["version"] = item.get("version", 0) + 1
            if created:
                item["alert_events_url"] = f"https://api.incident.io/v2/alert_events/http/{item['id']}"
                item["secret_token"] = uuid.uuid4().hex
            if item.get("heartbeat_options"):
                item["heartbeat_options"]["ping_url"] = f"https://api.incident.io/v2/heartbeat/{item['id']}/ping"

    def update_schema(self, type_id):
        types = bucket(next(r for r in RESOURCES if r.coll_key == "catalog_types"), None)
        item = next((i for i in types if i["id"] == type_id), None)
        if item is None:
            return self.error(404, "not found")
        payload = self.body()
        if payload.get("version") != item["schema"]["version"]:
            return self.error(409, "schema version conflict")
        attrs = []
        for a in payload["attributes"]:
            attrs.append({"id": a.get("id") or new_id(), "mode": a.get("mode", ""), **{k: v for k, v in a.items() if k != "id"}})
        item["schema"] = {"version": item["schema"]["version"] + 1, "attributes": attrs}
        self.send(200, {"catalog_type": item})

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

    def do_PUT(self):
        self.route("PUT")


if __name__ == "__main__":
    reset()
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8787
    print(f"incident.io mock listening on http://127.0.0.1:{port}")
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()

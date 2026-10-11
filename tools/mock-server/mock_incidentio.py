"""
Minimal in-memory mock of the incident.io API for local testing of the Bicep extension.

It implements only the endpoints the extension uses, with the same JSON envelopes
(e.g. {"severity": {...}}, {"severities": [...]}) and cursor pagination.
It is NOT a faithful copy of incident.io validation rules.

Usage:
    python tools/mock-server/mock_incidentio.py [port]     (default 8787)
    $env:INCIDENT_IO_BASE_URL = 'http://localhost:8787'
    $env:INCIDENT_IO_API_KEY  = 'test-key'

Debug endpoints:
    GET  /_state   -> full in-memory state
    POST /_reset   -> clear state
"""
import json
import re
import sys
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

EXPECTED_KEY = "test-key"

# path -> (item key, collection key, paginated, create-only fields)
COLLECTIONS = {
    "v1/severities": ("severity", "severities", False, []),
    "v2/incident_roles": ("incident_role", "incident_roles", False, []),
    "v1/incident_statuses": ("incident_status", "incident_statuses", False, ["category"]),
    "v2/custom_fields": ("custom_field", "custom_fields", False, ["field_type", "catalog_type_id"]),
    "v1/custom_field_options": ("custom_field_option", "custom_field_options", True, ["custom_field_id"]),
    "v3/catalog_types": ("catalog_type", "catalog_types", False, ["type_name"]),
    "v3/catalog_entries": ("catalog_entry", "catalog_entries", True, ["catalog_type_id"]),
    "v2/alert_attributes": ("alert_attribute", "alert_attributes", False, []),
}

STATE = {}
CALLS = []


def reset():
    STATE.clear()
    CALLS.clear()
    for path in COLLECTIONS:
        STATE[path] = []
    # A built-in severity so 'existing' lookups can be tested.
    STATE["v1/severities"].append({"id": "01SEVCRITICAL", "name": "Critical", "description": "Built in", "rank": 3})


def new_id():
    return "01" + uuid.uuid4().hex[:24].upper()


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

    def authorized(self):
        if self.headers.get("Authorization") != f"Bearer {EXPECTED_KEY}":
            self.error(401, "invalid API key")
            return False
        return True

    def route(self, method):
        url = urlparse(self.path)
        path = url.path.strip("/")
        query = {k: v[0] for k, v in parse_qs(url.query).items()}

        if path == "_state":
            return self.send(200, {"state": STATE, "calls": CALLS})
        if path == "_reset":
            reset()
            return self.send(200, {})
        if not self.authorized():
            return

        CALLS.append(f"{method} /{path}")

        m = re.fullmatch(r"v3/catalog_types/([^/]+)/actions/update_schema", path)
        if m and method == "POST":
            return self.update_schema(m.group(1))

        for base, (item_key, coll_key, paginated, create_only) in COLLECTIONS.items():
            if path == base:
                if method == "GET":
                    return self.list(base, coll_key, paginated, query)
                if method == "POST":
                    return self.create(base, item_key)
            elif path.startswith(base + "/"):
                item_id = path[len(base) + 1:]
                item = next((i for i in STATE[base] if i["id"] == item_id), None)
                if item is None:
                    return self.error(404, "not found")
                if method == "GET":
                    return self.send(200, {item_key: item})
                if method == "PUT":
                    payload = self.body()
                    bad = [f for f in create_only if f in payload]
                    if bad:
                        return self.error(422, f"unknown fields on update: {bad}")
                    item.update(payload)
                    item.pop("update_attributes", None)
                    return self.send(200, {item_key: item})
        self.error(404, f"no route for {method} /{path}")

    def list(self, base, coll_key, paginated, query):
        items = STATE[base]
        if base == "v1/custom_field_options":
            if "custom_field_id" not in query:
                return self.error(422, "custom_field_id is required")
            items = [i for i in items if i["custom_field_id"] == query["custom_field_id"]]
        if base == "v3/catalog_entries":
            if "catalog_type_id" not in query or "page_size" not in query:
                return self.error(422, "catalog_type_id and page_size are required")
            items = [i for i in items if i["catalog_type_id"] == query["catalog_type_id"]]
            if "identifier" in query:
                ident = query["identifier"]
                items = [i for i in items if ident in (i["id"], i.get("external_id")) or ident in i.get("aliases", [])]
        if not paginated:
            return self.send(200, {coll_key: items})

        # Small pages (2) on purpose, to exercise the extension's pagination loop.
        size = min(int(query.get("page_size", 25)), 2)
        start = 0
        if "after" in query:
            ids = [i["id"] for i in items]
            start = ids.index(query["after"]) + 1 if query["after"] in ids else len(items)
        page = items[start:start + size]
        after = page[-1]["id"] if len(page) == size and start + size < len(items) else None
        self.send(200, {coll_key: page, "pagination_meta": {"after": after, "page_size": size}})

    def create(self, base, item_key):
        payload = self.body()
        if "name" in payload and base not in ("v3/catalog_entries",):
            if any(i.get("name", "").lower() == payload["name"].lower() for i in STATE[base]):
                return self.error(422, "name must be unique")
        item = {"id": new_id(), **payload}
        if base == "v3/catalog_types":
            item.setdefault("type_name", 'Custom["%s"]' % re.sub(r"[^A-Za-z0-9]", "", payload["name"]))
            item["schema"] = {"version": 1, "attributes": []}
        STATE[base].append(item)
        self.send(201, {item_key: item})

    def update_schema(self, type_id):
        item = next((i for i in STATE["v3/catalog_types"] if i["id"] == type_id), None)
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
    print(f"incident.io mock listening on http://localhost:{port}")
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()

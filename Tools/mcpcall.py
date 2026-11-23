import json, sys, urllib.request, urllib.error, re

URL = "http://127.0.0.1:8090/mcp"


def parse_body(raw):
    raw = raw.strip()
    if not raw:
        return None
    for line in raw.splitlines():
        line = line.strip()
        if line.startswith("data:"):
            line = line[5:].strip()
        if not line:
            continue
        try:
            return json.loads(line)
        except Exception:
            m = re.search(r"\{.*\}", line, re.S)
            if m:
                try:
                    return json.loads(m.group(0))
                except Exception:
                    pass
    m = re.search(r"\{.*\}", raw, re.S)
    if m:
        try:
            return json.loads(m.group(0))
        except Exception:
            pass
    return None


def post(payload, sid=None):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(URL, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("Mcp-Session-Id", sid)
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            body = r.read().decode("utf-8", "replace")
            return r.headers.get("Mcp-Session-Id"), body
    except urllib.error.HTTPError as e:
        return None, e.read().decode("utf-8", "replace")


def main():
    tool = sys.argv[1]
    args = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}

    sid, body = post({
        "jsonrpc": "2.0", "id": 1, "method": "initialize",
        "params": {
            "protocolVersion": "2024-11-05", "capabilities": {},
            "clientInfo": {"name": "cli", "version": "1.0"},
        },
    })
    if sid is None:
        m = re.search(r"Mcp-Session-Id:\s*([0-9a-zA-Z]+)", body or "")
        sid = m.group(1) if m else None

    if sid:
        post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid)

    _, body = post({
        "jsonrpc": "2.0", "id": 2, "method": "tools/call",
        "params": {"name": tool, "arguments": args},
    }, sid)

    resp = parse_body(body)
    if resp is None:
        print("RAW:", (body or "")[:2000])
        return

    result = resp.get("result")
    if isinstance(result, dict) and "content" in result:
        for item in result["content"]:
            if item.get("type") == "text":
                print(item.get("text", ""))
    else:
        print(json.dumps(resp, ensure_ascii=False, indent=1))


main()
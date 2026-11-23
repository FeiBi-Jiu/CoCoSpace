import json, re, sys, time, urllib.request, urllib.error

URL = "http://127.0.0.1:8090/mcp"
SID = None


def parse_body(raw):
    raw = (raw or "").strip()
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


def post(payload, sid=None, timeout=180):
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(URL, data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    req.add_header("Accept", "application/json, text/event-stream")
    if sid:
        req.add_header("Mcp-Session-Id", sid)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.headers.get("Mcp-Session-Id"), r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return None, e.read().decode("utf-8", "replace")


def init():
    global SID
    sid, body = post({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                      "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                 "clientInfo": {"name": "greybox", "version": "1.0"}}})
    SID = sid
    if SID:
        post({"jsonrpc": "2.0", "method": "notifications/initialized"}, SID)


def call(tool, args, retries=4):
    last = None
    for i in range(retries):
        _, body = post({"jsonrpc": "2.0", "id": 2, "method": "tools/call",
                        "params": {"name": tool, "arguments": args}}, SID)
        resp = parse_body(body)
        if resp is None:
            last = "RAW:" + (body or "")[:300]
            time.sleep(2)
            continue
        if "error" in resp and resp["error"]:
            last = json.dumps(resp["error"], ensure_ascii=False)[:300]
            time.sleep(2)
            continue
        result = resp.get("result", {})
        text = ""
        if isinstance(result, dict) and "content" in result:
            for item in result["content"]:
                if item.get("type") == "text":
                    text += item.get("text", "")
        if "session not ready" in text or "TimeoutError" in text:
            last = text
            time.sleep(4)
            continue
        return text
    return "FAILED: " + str(last)


def show(tool, args, text):
    ok = not (text.startswith("FAILED") or '"success":false' in text.replace(" ", ""))
    print(("OK   " if ok else "ERR  ") + tool + " " + json.dumps(args, ensure_ascii=False)[:150])
    if not ok:
        print("     -> " + text[:600].replace("\n", " "))
    return ok


def run(tool, args, retries=4):
    return show(tool, args, call(tool, args, retries))
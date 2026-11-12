import hashlib
import json
import os
import urllib.error
import urllib.request
import uuid


SERVER = os.environ.get("QSOCK_TEST_URL", "http://127.0.0.1:5210")


def request(path, method="GET", data=None, token=None, expected=200, headers=None):
    request_headers = dict(headers or {})
    if token:
        request_headers["Authorization"] = "Bearer " + token
    if data is not None and not isinstance(data, bytes):
        data = json.dumps(data).encode()
        request_headers["Content-Type"] = "application/json"
    operation = urllib.request.Request(SERVER + path, data=data, method=method, headers=request_headers)
    try:
        response = urllib.request.urlopen(operation)
    except urllib.error.HTTPError as error:
        response = error
    content = response.read()
    assert response.status == expected, (path, response.status, expected, content.decode(errors="replace"))
    if content and "json" in response.headers.get("Content-Type", ""):
        return json.loads(content)
    return content


def register(prefix):
    username = prefix + uuid.uuid4().hex[:9]
    auth = request("/api/auth/register", "POST", {
        "userName": username,
        "password": "Testpass49",
        "deviceId": uuid.uuid4().hex,
        "deviceName": "Integration test",
    })
    return auth, request("/api/me", token=auth["accessToken"])


def main():
    assert request("/api/ui/manifest")["modules"] == []
    assert "кафедр" not in request("/api/server")["loginHint"].lower()
    request("/api/modules/moodle/ui.json", expected=404)
    alice_auth, alice = register("Alice")
    bob_auth, bob = register("Bob")
    outsider_auth, outsider = register("Carol")
    alice_token = alice_auth["accessToken"]
    bob_token = bob_auth["accessToken"]
    outsider_token = outsider_auth["accessToken"]

    request("/api/ui/plugins?target=web", expected=401)
    request("/api/ui/plugins?target=unknown", token=alice_token, expected=400)
    for platform in ("avalonia", "android", "web"):
        packages = request(f"/api/ui/plugins?target={platform}", token=alice_token)
        if os.environ.get("QSOCK_EXPECT_CLIENT_MODULES"):
            assert len(packages) == 1 and packages[0]["id"] == "moodle"
        for package in packages:
            assert package["target"] == platform
            request(package["assetUrl"], expected=401)
            asset = request(package["assetUrl"], token=alice_token)
            assert hashlib.sha256(asset).hexdigest() == package["sha256"].lower()
            request(f"/api/ui/plugins/assets/{package['id']}/{platform}/wrong-hash", token=alice_token, expected=404)

    direct = request("/api/chats", "POST", {"type": "direct", "userIds": [bob["id"]]}, alice_token)["id"]
    repeated = request("/api/chats", "POST", {"type": "direct", "userIds": [alice["id"]]}, bob_token)["id"]
    assert direct == repeated
    request(f"/api/chats/{direct}/messages", token=outsider_token, expected=403)
    assert not request("/api/chats", token=outsider_token)

    operation_id = str(uuid.uuid4())
    payload = {"text": "# Hello\n**Bold** and `code` :arrow-left:", "clientOperationId": operation_id}
    message = request(f"/api/chats/{direct}/messages", "POST", payload, alice_token)["id"]
    duplicate = request(f"/api/chats/{direct}/messages", "POST", payload, alice_token)["id"]
    assert message == duplicate
    preview = next(chat for chat in request("/api/chats", token=bob_token) if chat["id"] == direct)
    assert preview["lastMessagePreview"] == payload["text"] and preview["lastMessageAt"]
    assert preview["unreadCount"] == 1
    assert next(chat for chat in request("/api/chats", token=alice_token) if chat["id"] == direct)["unreadCount"] == 0
    assert len(request(f"/api/chats/{direct}/messages", token=bob_token)) == 1
    request(f"/api/messages/{message}", "PATCH", {"text": "Changed"}, bob_token, expected=403)
    request(f"/api/messages/{message}", "PATCH", {"text": "Changed"}, alice_token, expected=204)
    request(f"/api/chats/{direct}/read", "POST", token=bob_token, expected=204)
    assert request(f"/api/chats/{direct}/messages", token=alice_token)[0]["readCount"] == 1
    preview = next(chat for chat in request("/api/chats", token=bob_token) if chat["id"] == direct)
    assert preview["lastMessagePreview"] == "Changed" and preview["unreadCount"] == 0

    group = request("/api/chats", "POST", {"type": "group", "name": "Test group", "userIds": [bob["id"]]}, alice_token)["id"]
    request(f"/api/chats/{group}/messages", "POST", payload, alice_token, expected=409)
    request(f"/api/chats/{direct}/members/{outsider['id']}", "PUT", {"role": "member"}, alice_token, expected=403)
    request(f"/api/chats/{group}/members/{bob['id']}", "PUT", {
        "role": "member", "canWrite": False, "canSpeak": True, "canConnect": True, "canShare": False,
    }, alice_token, expected=204)
    request(f"/api/chats/{group}/messages", "POST", {"text": "Forbidden", "clientOperationId": str(uuid.uuid4())}, bob_token, expected=403)

    boundary = uuid.uuid4().hex
    upload = f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="sample.cs"\r\nContent-Type: text/plain\r\n\r\nConsole.WriteLine("test");\r\n--{boundary}--\r\n'.encode()
    attachment = request(f"/api/chats/{direct}/files", "POST", upload, alice_token, headers={"Content-Type": "multipart/form-data; boundary=" + boundary})
    request("/api/files/" + attachment["id"], token=outsider_token, expected=403)
    request(f"/api/chats/{group}/messages", "POST", {"text": "", "clientOperationId": str(uuid.uuid4()), "attachmentIds": [attachment["id"]]}, alice_token, expected=400)
    request(f"/api/chats/{direct}/messages", "POST", {"text": "File", "clientOperationId": str(uuid.uuid4()), "attachmentIds": [attachment["id"]]}, alice_token)
    assert b"Console.WriteLine" in request("/api/files/" + attachment["id"], token=bob_token)

    request("/api/friends/" + bob["id"], "POST", token=alice_token, expected=204)
    assert request("/api/friends", token=bob_token)[0]["incoming"]
    request("/api/friends/" + alice["id"], "POST", token=bob_token, expected=204)
    assert request("/api/friends", token=alice_token)[0]["status"] == "accepted"

    guild = request("/api/guilds", "POST", {"name": "Test space"}, alice_token)["id"]
    request(f"/api/guilds/{guild}/members/{bob['id']}", "PUT", {"role": "member"}, alice_token, expected=204)
    room = request("/api/chats", "POST", {"name": "Voice", "type": "room", "guildId": guild}, alice_token)["id"]
    assert any(chat["id"] == room for chat in request("/api/chats", token=bob_token))
    request(f"/api/guilds/{guild}/members/{outsider['id']}", "PUT", {"role": "member"}, bob_token, expected=403)

    call = request("/api/calls", "POST", {"chatId": group, "ringAll": False, "ringUserIds": [bob["id"]]}, alice_token)
    request("/api/calls/" + call["id"], token=outsider_token, expected=403)
    bridge = request("/api/auth/bridge?callId=" + call["id"], "POST", token=alice_token)
    exchange = {"code": bridge["code"], "deviceId": "browser-test", "deviceName": "Browser test"}
    launched = request("/api/auth/launch", "POST", exchange)
    assert launched["callId"] == call["id"]
    request("/api/auth/launch", "POST", exchange, expected=401)
    request("/api/calls/" + call["id"] + "/end", "POST", token=alice_token, expected=204)
    request("/api/admin/policy", token=bob_token, expected=403)

    paging = request("/api/chats", "POST", {"type": "group", "name": "History paging", "userIds": []}, alice_token)["id"]
    sent = set()
    for number in range(105):
        sent.add(request(f"/api/chats/{paging}/messages", "POST", {
            "text": f"Paging item {number}", "clientOperationId": str(uuid.uuid4()),
        }, alice_token)["id"])
    newest = request(f"/api/chats/{paging}/messages", token=alice_token)
    older = request(f"/api/chats/{paging}/messages?offset=100", token=alice_token)
    assert len(newest) == 100 and len(older) == 5
    assert {item["id"] for item in newest + older} == sent
    assert len(request(f"/api/chats/{paging}/messages?search=item%20104", token=alice_token)) == 1

    sessions = request("/api/sessions", token=alice_token)
    assert len(sessions) == 2
    request("/api/sessions/" + launched["auth"]["sessionId"], "DELETE", token=alice_token, expected=204)
    request("/api/me", token=launched["auth"]["accessToken"], expected=401)
    refreshed = request("/api/auth/refresh", "POST", {"sessionId": alice_auth["sessionId"], "refreshToken": alice_auth["refreshToken"]})
    request("/api/auth/refresh", "POST", {"sessionId": alice_auth["sessionId"], "refreshToken": alice_auth["refreshToken"]}, expected=401)
    assert refreshed["refreshToken"] != alice_auth["refreshToken"]
    print("PASS: direct-chat uniqueness, isolation, idempotency, receipts, permissions, files, friendship, guilds, calls, one-time launch and session revocation")


if __name__ == "__main__":
    main()

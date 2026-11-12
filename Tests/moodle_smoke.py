import hashlib
import hmac
import json
import os
import sqlite3
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone


SERVER = os.environ.get("QSOCK_MOODLE_TEST_URL", "http://127.0.0.1:5211")
SECRET = "test-connector-only-secret-at-least-32-characters"
SEQUENCE = 0


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
    body = response.read()
    assert response.status == expected, (path, response.status, expected, body.decode(errors="replace"))
    if body and "json" in response.headers.get("Content-Type", ""):
        return json.loads(body)
    return body if body else None


def signed(path, data, connection="department", expected=200):
    global SEQUENCE
    SEQUENCE += 1
    data["requestId"] = str(uuid.uuid4())
    body = json.dumps(data, ensure_ascii=False, separators=(",", ":")).encode()
    timestamp = str(int(time.time()))
    signature = hmac.new(SECRET.encode(), timestamp.encode() + b"\n" + body, hashlib.sha256).hexdigest()
    return request("/api/modules/moodle/" + path, "POST", body, expected=expected, headers={
        "Content-Type": "application/json",
        "X-QSock-Connection": connection,
        "X-QSock-Time": timestamp,
        "X-QSock-Signature": signature,
    })


def launch(subject, course, connection="department"):
    code = signed("launch", {"subject": subject, "courseId": course}, connection)["code"]
    return request("/api/auth/launch", "POST", {
        "code": code, "deviceId": uuid.uuid4().hex, "deviceName": "Moodle integration test",
    })


def main():
    assert any(module["id"] == "moodle" for module in request("/api/ui/manifest")["modules"])
    if os.environ.get("QSOCK_MOODLE_HTTP_FIXTURE"):
        credentials = {
            "userName": "fixture-login", "password": "Moodle-fixture-password49",
            "deviceId": uuid.uuid4().hex, "deviceName": "Moodle password fixture",
        }
        request("/api/auth/register", "POST", credentials, expected=403)
        request("/api/auth/login", "POST", dict(credentials, password="invalid-fixture-password"), expected=401)
        external_login = request("/api/auth/login", "POST", credentials)
        external_profile = request("/api/me", token=external_login["accessToken"])
        assert external_profile["displayName"] == "University login fixture" and external_profile["isStudent"]
        repeated_login = request("/api/auth/login", "POST", dict(credentials, deviceId=uuid.uuid4().hex))
        assert request("/api/me", token=repeated_login["accessToken"])["id"] == external_profile["id"]
        request("/api/auth/login", "POST", dict(credentials, userName="fixture-suspended"), expected=401)
    suffix = uuid.uuid4().hex[:8]
    teacher_subject = "teacher-" + suffix
    student_subject = "student-" + suffix
    course_id = int(uuid.uuid4().hex[:7], 16)
    teacher = {"subject": teacher_subject, "userName": "teacher" + suffix, "displayName": "Test teacher", "isAdmin": True}
    student = {"subject": student_subject, "userName": "student" + suffix, "displayName": "Test student", "isStudent": True}
    course = {"id": course_id, "name": "Управление данными", "members": [
        {"subject": teacher_subject, "role": "teacher", "groups": []},
        {"subject": student_subject, "role": "student", "groups": ["ИСТ-123"]},
    ]}
    snapshot = {"users": [teacher, student], "courses": [course], "fullSnapshot": False}
    signed("sync", snapshot)
    signed("sync", snapshot)
    teacher_launch = launch(teacher_subject, course_id)
    student_launch = launch(student_subject, course_id)
    teacher_token = teacher_launch["auth"]["accessToken"]
    student_token = student_launch["auth"]["accessToken"]
    room_id = teacher_launch["chatId"]
    assert room_id == student_launch["chatId"]
    memberships = request(f"/api/chats/{room_id}/members", token=teacher_token)
    student_member = next(member for member in memberships if member["role"] == "student")
    assert student_member["groups"] == ["ИСТ-123"]
    assert len(request("/api/guilds", token=student_token)) == 1
    request(f"/api/chats/{room_id}/members/{student_member['userId']}", "DELETE", token=teacher_token, expected=403)
    request(f"/api/chats/{room_id}/members/{student_member['userId']}", "PUT", {
        "role": "student", "canWrite": False, "canSpeak": False, "canConnect": True, "canShare": False,
    }, teacher_token, expected=204)
    request(f"/api/chats/{room_id}/messages", "POST", {"text": "Denied", "clientOperationId": str(uuid.uuid4())}, student_token, expected=403)

    start = datetime.now(timezone.utc) + timedelta(days=1)
    lesson = request("/api/modules/moodle/lessons", "POST", {
        "chatId": room_id, "title": "Практическое занятие", "startsAtUtc": start.isoformat(),
        "durationMinutes": 90, "reminder": True, "contactEmail": "teacher@example.test",
        "materials": ["https://example.test/material.pdf"],
    }, teacher_token)
    assert lesson["status"] == "planned"
    request("/api/modules/moodle/lessons", "POST", {
        "chatId": room_id, "title": "Unauthorized", "startsAtUtc": start.isoformat(), "durationMinutes": 90,
    }, student_token, expected=403)
    signed("sync", snapshot)
    updated = request(f"/api/chats/{room_id}/members", token=teacher_token)
    assert not next(member for member in updated if member["role"] == "student")["canWrite"]

    signed("sync", {"users": [teacher, student], "courses": [{"id": course_id + 1, "name": "Учебный сайт", "members": course["members"]}], "fullSnapshot": False}, "cdo")
    cdo_launch = launch(student_subject, course_id + 1, "cdo")
    student_id = request("/api/me", token=student_token)["id"]
    assert request("/api/me", token=cdo_launch["auth"]["accessToken"])["id"] == student_id
    external_teacher = dict(teacher, isAdmin=False)
    external_student = dict(student, isStudent=False, suspended=False)
    signed("sync", {"users": [external_teacher, external_student], "courses": [], "fullSnapshot": False}, "cdo")
    assert request("/api/me", token=teacher_token)["isAdmin"]
    assert request("/api/me", token=student_token)["isStudent"]

    request("/api/admin/policy", "PUT", {"studentsCanCreateGuilds": False, "confirmStudentGuildDeletion": True, "allowRegistration": False, "maxUploadMb": 25}, teacher_token, expected=204)
    request("/api/admin/backup", "POST", token=student_token, expected=403)
    request("/api/admin/audit", token=student_token, expected=403)
    audit = request("/api/admin/audit", token=teacher_token)
    assert any(entry["action"] == "policy.update" for entry in audit), audit
    backup = request("/api/admin/backup", "POST", token=teacher_token)
    assert backup.startswith(b"SQLite format 3\x00")
    assert b"Moodle-fixture-password49" not in backup
    with sqlite3.connect(":memory:") as restored:
        restored.deserialize(backup)
        assert restored.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
        assert restored.execute("SELECT COUNT(*) FROM Calls WHERE lower(Id) = lower(?)", (lesson["id"],)).fetchone()[0] == 1
    request("/api/guilds", "POST", {"name": "Denied"}, student_token, expected=403)
    course["members"] = [course["members"][0]]
    signed("sync", snapshot)
    request(f"/api/chats/{room_id}/messages", token=student_token, expected=403)
    student["suspended"] = True
    signed("sync", snapshot)
    request("/api/me", token=student_token, expected=401)
    print("PASS: signed synchronization, shared identity, immutable course membership, service roles, groups, lesson permissions, local restrictions, admin policy and suspended-account revocation")


if __name__ == "__main__":
    main()

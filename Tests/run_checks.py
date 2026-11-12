import argparse
import hashlib
import hmac
import http.server
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import urllib.parse
import uuid


ROOT = Path(__file__).resolve().parents[1]
SECRET = "test-connector-only-secret-at-least-32-characters"


def available_port():
    with socket.socket() as connection:
        connection.bind(("127.0.0.1", 0))
        return connection.getsockname()[1]


def run(arguments, environment=None):
    subprocess.run(arguments, cwd=ROOT, env=environment, check=True, timeout=180)


def start_server(dotnet, directory, module_directory, moodle_url=None):
    port = available_port()
    environment = dict(os.environ)
    environment.update({
        "ConnectionStrings__ChatDatabase": f"Data Source={directory / 'chat.db'}",
        "Logging__LogLevel__Default": "Error",
        "Moodle__Enabled": "true" if moodle_url else "false",
        "ClientPlugins__Directory": str(module_directory),
        "ServerPlugins__Directory": str(module_directory.parent / ("server-plugins" if moodle_url else "empty-server-plugins")),
    })
    if moodle_url:
        environment["Moodle__IdentityAuthority"] = "department"
        for index, name in enumerate(("department", "cdo")):
            prefix = f"Moodle__Connections__{index}__"
            environment.update({
                prefix + "Id": name,
                prefix + "Provider": "university",
                prefix + "BaseUrl": moodle_url,
                prefix + "Secret": SECRET,
            })
    directory.mkdir(parents=True)
    log = (directory / "server.log").open("w", encoding="utf-8")
    process = subprocess.Popen(
        [dotnet, str(ROOT / "Q.Sock.Server/bin/Debug/net10.0/Q.Sock.Server.dll"),
         "--urls", f"http://127.0.0.1:{port}"],
        cwd=ROOT / "Q.Sock.Server",
        env=environment,
        stdout=log,
        stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
    )
    url = f"http://127.0.0.1:{port}"
    try:
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError(f"Test server stopped; see {directory / 'server.log'}")
            try:
                with urllib.request.urlopen(url + "/health", timeout=1):
                    return process, log, url
            except (urllib.error.URLError, TimeoutError):
                time.sleep(0.2)
        raise TimeoutError("Test server startup timed out")
    except BaseException:
        process.terminate()
        process.wait(timeout=10)
        log.close()
        raise


class MoodleHttpFixture(http.server.BaseHTTPRequestHandler):
    records = []
    tokens = {}

    def respond(self, status, data):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(data).encode())

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        if self.path == "/login/token.php":
            fields = urllib.parse.parse_qs(body.decode())
            username = fields.get("username", [""])[0]
            if fields.get("password") != ["Moodle-fixture-password49"] or fields.get("service") != ["qsock"] or username not in ("fixture-login", "fixture-suspended"):
                self.respond(200, {"error": "invalidlogin"})
                return
            token = uuid.uuid4().hex
            self.tokens[token] = username
            self.respond(200, {"token": token})
            return
        if self.path == "/webservice/rest/server.php":
            fields = urllib.parse.parse_qs(body.decode())
            username = self.tokens.get(fields.get("wstoken", [""])[0])
            if username is None or fields.get("wsfunction") != ["local_qsock_identity"]:
                self.respond(200, {"exception": "invalid_parameter_exception"})
                return
            self.respond(200, {
                "subject": username, "userName": username, "displayName": "University login fixture",
                "isAdmin": False, "isStudent": True, "suspended": username == "fixture-suspended",
            })
            return
        timestamp = self.headers.get("X-QSock-Time", "")
        expected = hmac.new(SECRET.encode(), timestamp.encode() + b"\n" + body, hashlib.sha256).hexdigest()
        valid = hmac.compare_digest(expected, self.headers.get("X-QSock-Signature", ""))
        try:
            data = json.loads(body)
            valid = valid and abs(time.time() - int(timestamp)) < 300
            valid = valid and self.path == "/local/qsock/api.php"
            valid = valid and data["action"] == "calendar" and bool(data["requestId"])
        except (ValueError, KeyError):
            valid = False
        if valid:
            self.records.append(data)
        self.respond(200 if valid else 403, {"ok": True} if valid else {"error": "invalid request"})

    def log_message(self, *arguments):
        pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--no-build", action="store_true")
    parser.add_argument("--no-restore", action="store_true")
    arguments = parser.parse_args()
    dotnet = str(Path(arguments.dotnet).resolve()) if Path(arguments.dotnet).exists() else arguments.dotnet
    if not arguments.no_build:
        for project in ("Q.Sock.Server.Plugins.Moodle", "Q.Sock.Plugins.Moodle", "Tools/ModulePack", "Tests/Signaling", "Tests/Core", "Tests/Plugins", "Tests/UI"):
            run([dotnet, "build", project, "-m:1", "-nr:false", "-p:MSBuildEnableWorkloadResolver=false"] + (["--no-restore"] if arguments.no_restore else []))
    output = ROOT / "artifacts/verification" / uuid.uuid4().hex[:12]
    modules = output / "client-modules"
    run([
        dotnet, "Tools/ModulePack/bin/Debug/net10.0/Q.Sock.Tools.ModulePack.dll",
        str(ROOT / "Q.Sock.Plugins.Moodle/bin/Debug/net10.0/Q.Sock.Plugins.Moodle.dll"),
        str(modules), str(output / "publisher.pem"), "moodle", "1.0.0", "Fixture Moodle",
        "Q.Sock.Plugins.Moodle.MoodleClientPlugin", "lesson-planning",
    ])
    server_module = output / "server-plugins/moodle"
    server_module.mkdir(parents=True)
    import shutil
    for name in ("Q.Sock.Server.Plugins.Moodle.dll", "Q.Sock.Server.Plugins.Moodle.deps.json"):
        shutil.copy2(ROOT / "Q.Sock.Server.Plugins.Moodle/bin/Debug/net10.0" / name, server_module / name)
    (server_module / "module.json").write_text(json.dumps({
        "id": "moodle", "assembly": "Q.Sock.Server.Plugins.Moodle.dll",
        "entryType": "Q.Sock.Server.Plugins.Moodle.MoodleModule", "apiVersion": 1,
    }), encoding="utf-8")
    fixture = http.server.ThreadingHTTPServer(("127.0.0.1", 0), MoodleHttpFixture)
    threading.Thread(target=fixture.serve_forever, daemon=True).start()
    servers = []
    try:
        base = start_server(dotnet, output / "base", modules)
        servers.append(base)
        moodle = start_server(dotnet, output / "moodle", modules, f"http://127.0.0.1:{fixture.server_port}")
        servers.append(moodle)
        environment = dict(os.environ, QSOCK_TEST_URL=base[2], QSOCK_MOODLE_TEST_URL=moodle[2], QSOCK_EXPECT_CLIENT_MODULES="1", QSOCK_MOODLE_HTTP_FIXTURE="1")
        run([sys.executable, "Tests/api_smoke.py"], environment)
        run([sys.executable, "Tests/moodle_smoke.py"], environment)
        run([dotnet, "Tests/Signaling/bin/Debug/net10.0/Q.Sock.Tests.Signaling.dll", base[2]])
        run([dotnet, "Tests/Core/bin/Debug/net10.0/Q.Sock.Tests.Core.dll"])
        run([dotnet, "Tests/Plugins/bin/Debug/net10.0/Q.Sock.Tests.Plugins.dll"])
        run([dotnet, "Tests/UI/bin/Debug/net10.0/Q.Sock.Tests.UI.dll", str(output / "icons")])
        deadline = time.monotonic() + 30
        while not MoodleHttpFixture.records and time.monotonic() < deadline:
            time.sleep(0.2)
        if not MoodleHttpFixture.records:
            raise AssertionError("The server did not publish the lesson to the calendar fixture")
        (output / "calendar.json").write_text(json.dumps(MoodleHttpFixture.records, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"PASS: complete protocol suite and signed calendar callback. Logs: {output}")
    finally:
        for process, log, _ in servers:
            process.terminate()
            process.wait(timeout=10)
            log.close()
        fixture.shutdown()
        fixture.server_close()


if __name__ == "__main__":
    main()

import http.server
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "/": ROOT / "Tests/Browser/index.html",
    "/checks.js": ROOT / "Tests/Browser/checks.js",
    "/engine.js": ROOT / "Q.Sock.Browser/wwwroot/engine.js",
    "/plugin-cache.js": ROOT / "Q.Sock.Browser/wwwroot/plugin-cache.js",
}


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        path = FILES.get(self.path)
        if path is None:
            self.send_error(404)
            return
        data = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8" if path.suffix == ".html" else "text/javascript; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


if __name__ == "__main__":
    print("Synthetic browser checks: http://127.0.0.1:5213", flush=True)
    http.server.ThreadingHTTPServer(("127.0.0.1", 5213), Handler).serve_forever()

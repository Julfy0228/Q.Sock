import concurrent.futures
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[1] / "artifacts/moodle-stand"
ROOT.mkdir(parents=True, exist_ok=True)


def fetch(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "Q.Sock-development-tests"}), timeout=60)


def archive(name, url, expected=None):
    target = ROOT / (name + ".zip")
    request = urllib.request.Request(url, headers={"Range": "bytes=0-0", "User-Agent": "Q.Sock-development-tests"})
    with urllib.request.urlopen(request, timeout=60) as response:
        total = int(response.headers["Content-Range"].split("/")[-1])
    offset = target.stat().st_size if target.exists() else 0
    if offset > total:
        raise ValueError("Cached archive is larger than its source")
    while offset < total:
        last = min(offset + 4 * 1024 * 1024 - 1, total - 1)
        request = urllib.request.Request(url, headers={"Range": f"bytes={offset}-{last}", "User-Agent": "Q.Sock-development-tests"})
        with urllib.request.urlopen(request, timeout=60) as response:
            if response.status != 206 or not response.headers.get("Content-Range", "").startswith(f"bytes {offset}-"):
                raise ValueError("The archive server did not honor the byte range")
            block = response.read(last - offset + 1)
        if not block:
            raise ValueError("Archive download stopped before completion")
        with target.open("ab") as output:
            output.write(block)
        offset += len(block)
        print(f"Download: {name} {offset}/{total}", flush=True)
    digest = hashlib.file_digest(target.open("rb"), "sha256").hexdigest()
    if expected and digest != expected:
        raise ValueError(f"Checksum mismatch: {name}")
    destination = ROOT / name
    with zipfile.ZipFile(target) as package:
        for entry in package.infolist():
            if not (destination / entry.filename).resolve().is_relative_to(destination.resolve()):
                raise ValueError("Archive path escapes destination")
        package.extractall(destination)
        first = package.namelist()[0].split("/")[0]
    result = {"url": url, "sha256": digest, "directory": str(destination), "archiveRoot": first}
    (ROOT / (name + "-source.json")).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("Ready:", name, flush=True)
    return result


def php():
    releases = json.load(fetch("https://downloads.php.net/~windows/releases/releases.json"))
    package = releases["8.2"]["nts-vs16-x64"]["zip"]
    return archive("php82", "https://downloads.php.net/~windows/releases/" + package["path"], package["sha256"])


def maria():
    base = "https://archive.mariadb.org/mariadb-11.4.5/winx64-packages/"
    name = "mariadb-11.4.5-winx64.zip"
    lines = fetch(base + "sha256sums.txt").read().decode().splitlines()
    digest = next(line.split()[0] for line in lines if line.strip().endswith(name))
    return archive("mariadb", base + name, digest)


def moodle(version):
    reference = json.load(fetch("https://api.github.com/repos/moodle/moodle/git/ref/tags/v" + version))["object"]
    while reference["type"] == "tag":
        reference = json.load(fetch(reference["url"]))["object"]
    if reference["type"] != "commit":
        raise ValueError("Moodle tag did not resolve to a commit")
    branch = "403" if version == "4.3.0" else "503"
    release = "4.3" if version == "4.3.0" else "5.3"
    result = archive("moodle" + version.replace(".", "") + "-release", f"https://packaging.moodle.org/stable{branch}/moodle-{release}.zip")
    result["commit"] = reference["sha"]
    result["sourceRepository"] = "https://github.com/moodle/moodle"
    return result


if __name__ == "__main__":
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        jobs = {"php82": pool.submit(php), "maria": pool.submit(maria),
                "moodle430": pool.submit(moodle, "4.3.0"), "moodle530": pool.submit(moodle, "5.3.0")}
        results = {name: future.result() for name, future in jobs.items()}
    (ROOT / "assets.json").write_text(json.dumps(results, indent=2), encoding="utf-8")

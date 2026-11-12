import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("stage", type=Path)
    parser.add_argument("--php", required=True)
    arguments = parser.parse_args()
    stage = arguments.stage.resolve()
    php = str(Path(arguments.php).resolve())
    cli = stage / "local/qsock/cli/server.php"
    data = stage / "supervisor-fixture-data"
    library = stage / "supervisor-fixture-lib"
    library.mkdir(exist_ok=True)
    (library / "clilib.php").write_text(r"""<?php
function cli_get_params($defaults, $short) {
    global $argv;
    $unknown = [];
    foreach (array_slice($argv, 1) as $value) {
        $pair = explode('=', ltrim($value, '-'), 2);
        if (!array_key_exists($pair[0], $defaults)) {
            $unknown[] = $value;
        } else {
            $defaults[$pair[0]] = $pair[1] ?? true;
        }
    }
    return [$defaults, $unknown];
}
function cli_error($message) { fwrite(STDERR, $message . "\n"); exit(1); }
""", encoding="utf-8")
    config = r"""<?php
define('MOODLE_INTERNAL', true);
$CFG = (object)[
    'dataroot' => DATA_ROOT,
    'libdir' => LIB_ROOT,
    'wwwroot' => 'http://127.0.0.1:59999',
];
function get_config($component, $key = null) {
    global $CFG;
    $path = $CFG->dataroot . '/fixture-settings.json';
    $data = is_file($path) ? json_decode(file_get_contents($path), true) : [];
    return $key === null ? (object)$data : ($data[$key] ?? false);
}
function set_config($key, $value, $component) {
    global $CFG;
    if (!is_dir($CFG->dataroot)) { mkdir($CFG->dataroot, 0700, true); }
    $data = (array)get_config($component);
    $data[$key] = $value;
    file_put_contents($CFG->dataroot . '/fixture-settings.json', json_encode($data));
}
"""
    config = config.replace("DATA_ROOT", json.dumps(data.as_posix())).replace("LIB_ROOT", json.dumps(library.as_posix()))
    (stage / "config.php").write_text(config, encoding="utf-8")
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    url = f"http://127.0.0.1:{port}"
    flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    run = lambda *args: subprocess.run([php, str(cli), *args], capture_output=True, text=True, timeout=30, creationflags=flags)
    assert run("--help").returncode == 0
    log = (stage / "supervisor-proof.log").open("w", encoding="utf-8")
    process = subprocess.Popen([php, str(cli), "--public-url=" + url, "--listen=" + url], stdout=log, stderr=subprocess.STDOUT, creationflags=flags)
    try:
        for _ in range(150):
            if process.poll() is not None:
                raise AssertionError("Supervisor stopped: " + (stage / "supervisor-proof.log").read_text(encoding="utf-8"))
            try:
                with urllib.request.urlopen(url + "/health", timeout=1) as response:
                    if response.status == 200:
                        break
            except (OSError, TimeoutError):
                time.sleep(0.2)
        else:
            raise AssertionError("Bundled server did not become ready")
        with urllib.request.urlopen(url + "/api/ui/manifest") as response:
            assert any(module["id"] == "moodle" for module in json.load(response)["modules"])
        with urllib.request.urlopen(url + "/api/server") as response:
            assert not json.load(response)["policy"]["allowRegistration"]
        with urllib.request.urlopen(url + "/avalonia/index.html") as response:
            assert response.status == 200
        assert run("--status").returncode == 0
        assert run("--public-url=" + url, "--listen=" + url).returncode != 0
        settings = json.loads((data / "local_qsock/server/appsettings.instance.json").read_text(encoding="utf-8"))
        assert Path(settings["State"]["Directory"]).is_relative_to(data)
        assert (data / "local_qsock/server/chat.db").is_file()
        assert run("--stop").returncode == 0
        assert process.wait(timeout=30) == 0
        assert run("--status").returncode == 1
        print("PASS: self-contained bundled server starts through PHP supervisor; browser and Moodle module present; data isolated; duplicate launch rejected; status and stop work.")
    finally:
        if process.poll() is None:
            run("--stop")
            process.wait(timeout=30)
        log.close()


if __name__ == "__main__":
    main()

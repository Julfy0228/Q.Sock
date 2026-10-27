import json
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]
STAND = ROOT / "artifacts/moodle-stand"
ASSETS = json.loads((STAND / "assets.json").read_text(encoding="utf-8-sig"))


def prepare_php(name, directory):
    temporary = STAND / "temporary" / name
    temporary.mkdir(parents=True, exist_ok=True)
    extensions = ("curl", "fileinfo", "gd", "intl", "mbstring", "mysqli", "openssl", "soap", "sodium", "zip")
    lines = [
        f'extension_dir="{(directory / "ext").as_posix()}"',
        "memory_limit=512M",
        "max_input_vars=5000",
        "date.timezone=UTC",
        "display_errors=stderr",
        "log_errors=On",
        f'error_log="{(temporary / "php.log").as_posix()}"',
        f'sys_temp_dir="{temporary.as_posix()}"',
        f'session.save_path="{temporary.as_posix()}"',
    ]
    lines.extend(f"extension={extension}" for extension in extensions)
    ini = STAND / f"{name}.ini"
    ini.write_text("\n".join(lines) + "\n", encoding="utf-8")
    subprocess.run([str(directory / "php.exe"), "-c", str(ini), "-m"], check=True)


def main():
    prepare_php("php82", Path(ASSETS["php82"]["directory"]))
    prepare_php("php83", ROOT / "artifacts/php")
    for name in ("moodle430", "moodle530"):
        root = Path(ASSETS[name]["directory"]) / "moodle"
        webroot = root / "public" if (root / "public").exists() else root
        for kind in ("local", "mod"):
            shutil.copytree(ROOT / "integrations/moodle" / kind / "qsock", webroot / kind / "qsock", dirs_exist_ok=True)
    database = STAND / "database"
    if not (database / "mysql").exists():
        binary = Path(ASSETS["maria"]["directory"]) / ASSETS["maria"]["archiveRoot"] / "bin/mariadb-install-db.exe"
        subprocess.run([
            str(binary), f"--datadir={database}", "--port=5336",
            "--password=fixture-Maria49-only-local",
        ], cwd=STAND, check=True)
    print("READY: PHP configuration, Moodle plugins, isolated MariaDB data directory.")


if __name__ == "__main__":
    main()

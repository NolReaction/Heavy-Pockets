#!/usr/bin/env python3
"""Launch an encrypted localhost raid from Finder or the Unity Editor."""

import argparse
import base64
import hashlib
import json
import os
import plistlib
import secrets
import select
import signal
import socket
import subprocess
import sys
import tempfile
import time
import uuid
from pathlib import Path
from xml.parsers.expat import ExpatError


BUILD = Path(__file__).resolve().parents[1] / "Build" / "Development-Mac"
DEFAULT_PORT = 17777
CREDENTIAL_VARS = {"HP_RAID_CA", "HP_RAID_TICKET", "HP_RAID_CERT", "HP_RAID_KEY", "HP_RAID_DIGESTS"}
CERT_CONFIG = """\
[req]
distinguished_name = subject
[subject]
commonName = Common Name
[issuer]
basicConstraints = critical,CA:TRUE,pathlen:0
keyUsage = critical,keyCertSign
[server]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = critical,serverAuth
subjectAltName = DNS:heavy-pockets.local
"""


class RaidError(Exception):
    pass


class SessionStopped(Exception):
    pass


class RaidLifetime:
    def __init__(self, editor):
        self.interrupted = False
        self.editor_input = sys.stdin.fileno() if editor else None
        self.children = []
        self.environment = {key: value for key, value in os.environ.items() if key not in CREDENTIAL_VARS}
        signal.signal(signal.SIGINT, self.stop)
        signal.signal(signal.SIGTERM, self.stop)

    def stop(self, _signum, _frame):
        self.interrupted = True

    def wait(self, delay=0):
        if not self.interrupted:
            if self.editor_input is not None:
                readable, _, _ = select.select([self.editor_input], [], [], delay)
                if readable and not os.read(self.editor_input, 4096):
                    self.interrupted = True
            elif delay:
                time.sleep(delay)
        if self.interrupted:
            raise SessionStopped

    def start(self, executable, arguments, credentials=None):
        self.wait()
        environment = self.environment if credentials is None else {**self.environment, **credentials}
        child = subprocess.Popen(
            [str(executable), *arguments], env=environment, start_new_session=True,
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        self.children.append(child)
        return child

    def close(self):
        for child in self.children:
            if child.poll() is None:
                child.terminate()
        deadline = time.monotonic() + 2
        for child in self.children:
            try:
                child.wait(timeout=max(0, deadline - time.monotonic()))
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()


def bundle_executable(bundle, error):
    contents = bundle / "Contents"
    try:
        with (contents / "Info.plist").open("rb") as file:
            info = plistlib.load(file)
        name = info.get("CFBundleExecutable") if isinstance(info, dict) else None
        if isinstance(name, str) and name == Path(name).name:
            executable = contents / "MacOS" / name
            if executable.is_file() and os.access(executable, os.X_OK):
                return executable
    except (OSError, ValueError, ExpatError, plistlib.InvalidFileException):
        pass
    raise RaidError(error)


def openssl(lifetime, *arguments):
    process = lifetime.start("/usr/bin/openssl", arguments)
    try:
        while process.poll() is None:
            lifetime.wait(0.05)
    except SessionStopped:
        process.kill()
        process.wait()
        raise
    return process.returncode == 0


def encode(data):
    return base64.b64encode(data).decode("ascii")


def prepare_credentials(lifetime, ticket_count):
    error = "Could not prepare the encrypted local server session."
    try:
        with tempfile.TemporaryDirectory(prefix="HeavyPockets-Credentials-") as temporary:
            config, ca, ca_key, request, initial_key, cert, key = (
                str(Path(temporary) / name) for name in
                ("openssl.cnf", "ca.pem", "ca.key", "server.csr", "server.pk8", "server.pem", "server.key")
            )
            Path(config).write_text(CERT_CONFIG, encoding="ascii")
            if not openssl(lifetime, "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-sha256",
                           "-config", config, "-extensions", "issuer", "-days", "1",
                           "-subj", "/CN=Heavy Pockets Local Raid", "-keyout", ca_key, "-out", ca):
                raise RaidError(error)
            if not openssl(lifetime, "req", "-new", "-newkey", "rsa:2048", "-nodes", "-sha256",
                           "-config", config, "-subj", "/CN=heavy-pockets.local", "-keyout", initial_key,
                           "-out", request):
                raise RaidError(error)
            if not openssl(lifetime, "x509", "-req", "-in", request, "-CA", ca, "-CAkey", ca_key,
                           "-set_serial", "0x01" + secrets.token_hex(15), "-days", "1", "-sha256",
                           "-extfile", config, "-extensions", "server", "-out", cert):
                raise RaidError(error)
            # OpenSSL 3 needs -traditional for PKCS#1. LibreSSL emits PKCS#1 by default.
            if not openssl(lifetime, "rsa", "-traditional", "-in", initial_key, "-out", key):
                if not openssl(lifetime, "rsa", "-in", initial_key, "-out", key):
                    raise RaidError(error)
            server_key = Path(key).read_text(encoding="ascii")
            if not server_key.startswith("-----BEGIN RSA PRIVATE KEY-----\n"):
                raise RaidError(error)

            tickets = [secrets.token_bytes(32) for _ in range(ticket_count)]
            server = {
                "HP_RAID_CERT": Path(cert).read_text(encoding="ascii"),
                "HP_RAID_KEY": server_key,
                "HP_RAID_DIGESTS": ",".join(encode(hashlib.sha256(ticket).digest()) for ticket in tickets),
            }
            return server, Path(ca).read_text(encoding="ascii"), [encode(ticket) for ticket in tickets]
    except (OSError, ValueError):
        raise RaidError(error) from None


def choose_port(editor, port):
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as reservation:
            if port is None and not editor:
                try:
                    reservation.bind(("127.0.0.1", DEFAULT_PORT))
                    return DEFAULT_PORT
                except OSError:
                    pass
            reservation.bind(("127.0.0.1", 0 if port is None else port))
            return reservation.getsockname()[1]
    except OSError:
        raise RaidError("Could not reserve a localhost UDP port for the raid.") from None


def wait_for_log(lifetime, process, log, marker, error):
    marker = marker.encode("ascii")
    deadline = time.monotonic() + 35
    offset = 0
    tail = b""
    while process.poll() is None and time.monotonic() < deadline:
        lifetime.wait()
        try:
            with log.open("rb") as file:
                file.seek(offset)
                tail += file.read()
                offset = file.tell()
        except FileNotFoundError:
            pass
        if marker in tail:
            return
        tail = tail[-len(marker):]
        lifetime.wait(0.1)
    lifetime.wait()
    raise RaidError(error)


def start_raid_process(lifetime, executable, options, credentials, log, marker, failure):
    error = f"{failure} See {log}"
    try:
        process = lifetime.start(executable, [*options, "-logFile", str(log)], credentials)
        wait_for_log(lifetime, process, log, marker, error)
    except OSError:
        raise RaidError(error) from None
    return process


def run_raid(lifetime, editor, requested_port):
    lifetime.wait()
    server_path = bundle_executable(
        BUILD / "Server" / "HeavyPocketsServer.app",
        "Missing local server. Build the macOS dedicated server from the Heavy Pockets menu.",
    )
    if not editor:
        client_path = bundle_executable(
            BUILD / "Client" / "HeavyPockets.app",
            "Missing local client. Build the macOS client from the Heavy Pockets menu.",
        )

    port = choose_port(editor, requested_port)
    server_credentials, ca, tickets = prepare_credentials(lifetime, 1 if editor else 2)
    if editor:
        server_log = Path(tempfile.gettempdir()) / f"HeavyPockets-Editor-{uuid.uuid4().hex}.log"
    else:
        log_directory = Path(tempfile.mkdtemp(prefix="HeavyPockets-Play-"))
        server_log = log_directory / "server.log"
        print(f"Local raid on 127.0.0.1:{port}. Logs: {log_directory}", flush=True)

    server = start_raid_process(
        lifetime, server_path, ["-server", "-batchmode", "-port", str(port)], server_credentials,
        server_log, f"HEAVY POCKETS SERVER READY 127.0.0.1:{port}", "The local raid server could not start.",
    )
    if editor:
        lifetime.wait()
        invitation = json.dumps({"port": port, "ca": ca, "ticket": tickets[0]}, separators=(",", ":"))
        os.write(sys.stdout.fileno(), (invitation + "\n").encode("ascii"))
        while server.poll() is None:
            lifetime.wait(0.2)
        lifetime.wait()
        raise RaidError(f"The local raid server stopped unexpectedly. See {server_log}")

    clients = []
    for role, ticket in zip(("A", "B"), tickets):
        clients.append(start_raid_process(
            lifetime, client_path, ["-client", "-port", str(port)], {"HP_RAID_CA": ca, "HP_RAID_TICKET": ticket},
            log_directory / f"{role}.log", "HEAVY POCKETS CLIENT CONNECTED", f"Client {role} could not connect.",
        ))
    print("Heavy Pockets is ready. Click a game window to play. Close both game windows to finish this raid.",
          flush=True)
    while any(client.poll() is None for client in clients):
        if server.poll() is not None:
            raise RaidError(f"The local raid server stopped unexpectedly. See {server_log}")
        lifetime.wait(0.2)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", action="store_true", help="Launch a server for the Unity Editor")
    parser.add_argument("--port", type=int, help="Override the localhost UDP port")
    args = parser.parse_args()
    if args.port is not None and not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    if args.editor and (sys.stdin.isatty() or sys.stdout.isatty()):
        print("Start the editor raid by pressing Play in Unity.", file=sys.stderr)
        return 1

    os.umask(0o077)
    lifetime = RaidLifetime(args.editor)
    try:
        run_raid(lifetime, args.editor, args.port)
    except (SessionStopped, BrokenPipeError):
        return 0
    except RaidError as error:
        print(error, file=sys.stderr)
        return 1
    except OSError:
        print("Could not initialize the local raid helper.", file=sys.stderr)
        return 1
    finally:
        lifetime.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

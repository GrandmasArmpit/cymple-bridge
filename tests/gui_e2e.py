"""End-to-end check of the GUI app's own relay path (GDI+ resize -> Cymple packets)."""
import base64
import socket
import struct
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

JPEG = base64.b64decode(
    "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4n"
    "ICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/E"
    "ABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q=="
)
import os
DEFAULT_EXE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "CympleBridge",
                           "bin", "Release", "net10.0-windows", "win-x64", "CympleBridge.exe")
EXE = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_EXE


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=frame")
        self.end_headers()
        try:
            while True:
                self.wfile.write(b"--frame\r\nContent-Type: image/jpeg\r\n\r\n" + JPEG + b"\r\n")
                self.wfile.flush()
                time.sleep(0.04)
        except (BrokenPipeError, ConnectionResetError):
            pass


def main():
    server = ThreadingHTTPServer(("127.0.0.1", 8099), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("127.0.0.1", 23303))
    sock.settimeout(0.5)

    proc = subprocess.Popen([EXE, "--run", "http://127.0.0.1:8099/stream.mjpg", "--seconds", "4"])

    frames = {}
    flags = set()
    deadline = time.time() + 8
    while time.time() < deadline and proc.poll() is None:
        try:
            data, _ = sock.recvfrom(65535)
        except socket.timeout:
            continue
        if len(data) < 12:
            continue
        msg_type, _, offset, total, data_len = struct.unpack("<HHHHH", data[:10])
        flag, idx = data[10], data[11]
        flags.add(flag)
        if msg_type != 5 or data_len != len(data) - 12:
            print(f"FAIL bad header type={msg_type}")
            proc.kill(); return 1
        frames.setdefault(idx, bytearray(total))[offset:offset + data_len] = data[12:]

    code = proc.wait(timeout=8)
    server.shutdown()

    good = [bytes(b) for b in frames.values()
            if b[:3] == b"\xff\xd8\xff" and b[-2:] == b"\xff\xd9"]
    print(f"exit={code} frames={len(frames)} valid_jpeg={len(good)} flags={flags}")
    if code != 0 or not good or flags != {2}:
        print("FAIL")
        return 1
    print("PASS GUI relay -> Cymple mouth packets")
    return 0


if __name__ == "__main__":
    sys.exit(main())

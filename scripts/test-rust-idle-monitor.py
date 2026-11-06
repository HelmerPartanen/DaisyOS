#!/usr/bin/env python3
"""Exercise the native ABI and Wayland events without changing the user's session."""
import argparse
import ctypes as C
import os
from pathlib import Path
import select
import socket
import struct
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / 'src/native/daisy_idle_monitor/build'


def u32(*values):
    return struct.pack('=' + 'I' * len(values), *values)


def string(value):
    data = value.encode() + b'\0'
    return u32(len(data)) + data + b'\0' * (-len(data) % 4)


def event(obj, opcode, body=b''):
    return u32(obj, (len(body) + 8) << 16 | opcode) + body


class Compositor:
    def __init__(self, path, advertise=True):
        self.listener = socket.socket(socket.AF_UNIX)
        self.listener.bind(path)
        self.listener.listen()
        self.listener.settimeout(.1)
        self.advertise = advertise
        self.stop = threading.Event()
        self.errors = []
        self.clients = []
        self.threads = []
        self.thread = threading.Thread(target=self.accept)
        self.thread.start()

    def accept(self):
        while not self.stop.is_set():
            try:
                conn, _ = self.listener.accept()
            except socket.timeout:
                continue
            self.clients.append(conn)
            thread = threading.Thread(target=self.serve, args=(conn,))
            self.threads.append(thread)
            thread.start()

    def serve(self, conn):
        objects = {1: 'display'}
        buffer = b''
        try:
            while not self.stop.is_set():
                chunk = conn.recv(4096)
                if not chunk:
                    return
                buffer += chunk
                while len(buffer) >= 8:
                    obj, header = struct.unpack('=II', buffer[:8])
                    size, opcode = header >> 16, header & 65535
                    assert size >= 8 and size % 4 == 0
                    if len(buffer) < size:
                        break
                    body, buffer = buffer[8:size], buffer[size:]
                    kind = objects[obj]
                    if kind == 'display' and opcode == 1:
                        registry, = struct.unpack('=I', body)
                        objects[registry] = 'registry'
                        conn.sendall(event(registry, 0, u32(10) + string('wl_seat') + u32(1)))
                        if self.advertise:
                            conn.sendall(event(registry, 0, u32(11) + string('ext_idle_notifier_v1') + u32(1)))
                    elif kind == 'display' and opcode == 0:
                        callback, = struct.unpack('=I', body)
                        conn.sendall(event(callback, 0, u32(1)) + event(1, 1, u32(callback)))
                    elif kind == 'registry' and opcode == 0:
                        name, = struct.unpack('=I', body[:4])
                        new_id, = struct.unpack('=I', body[-4:])
                        objects[new_id] = 'seat' if name == 10 else 'notifier'
                    elif kind == 'notifier' and opcode == 1:
                        notification, timeout, seat = struct.unpack('=III', body)
                        assert timeout > 0 and objects[seat] == 'seat'
                        objects[notification] = 'notification'
                        conn.sendall(event(notification, 0) + event(notification, 1))
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        except Exception as error:
            self.errors.append(error)
        finally:
            conn.close()

    def close(self):
        self.stop.set()
        self.thread.join(2)
        self.listener.close()
        for conn in self.clients:
            try:
                conn.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        for thread in self.threads:
            thread.join(2)
        assert not self.errors, self.errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--managed-host', type=Path, help='Optional built IdleMonitorAcceptance.dll')
    args = parser.parse_args()
    lib = C.CDLL(str(BUILD / 'libdaisy_idle.so'))
    callback = C.CFUNCTYPE(None, C.c_void_p)
    lib.daisy_idle_monitor_start.argtypes = [C.c_uint32, callback, callback, C.c_void_p]
    lib.daisy_idle_monitor_start.restype = C.c_void_p
    lib.daisy_idle_monitor_stop.argtypes = [C.c_void_p]
    lib.daisy_idle_monitor_is_available.restype = C.c_int
    with tempfile.TemporaryDirectory(prefix='daisy-idle-test-') as temp:
        os.environ['XDG_RUNTIME_DIR'] = temp
        os.environ['WAYLAND_DISPLAY'] = 'test-wayland'
        os.environ.pop('WAYLAND_SOCKET', None)
        server = Compositor(temp + '/test-wayland')
        try:
            assert lib.daisy_idle_monitor_is_available() == 1
            seen = []
            @callback
            def idle(data):
                seen.append(('idle', data))
            @callback
            def resumed(data):
                seen.append(('resumed', data))
            assert not lib.daisy_idle_monitor_start(0, idle, resumed, None)
            lib.daisy_idle_monitor_stop(None)
            for thread in server.threads:
                thread.join(2)
                assert not thread.is_alive()
            fd_count = len(list(Path('/proc/self/fd').iterdir()))
            thread_count = len(list(Path('/proc/self/task').iterdir()))
            for index in range(50):
                seen.clear()
                handle = lib.daisy_idle_monitor_start(50, idle, resumed, 42)
                assert handle
                try:
                    end = time.monotonic() + 2
                    while len(seen) < 2 and time.monotonic() < end:
                        time.sleep(.005)
                    assert seen == [('idle', 42), ('resumed', 42)], seen
                finally:
                    lib.daisy_idle_monitor_stop(handle)
            for thread in server.threads:
                thread.join(2)
                assert not thread.is_alive()
            assert len(list(Path('/proc/self/fd').iterdir())) == fd_count
            assert len(list(Path('/proc/self/task').iterdir())) == thread_count
            if args.managed_host:
                subprocess.run(['dotnet', str(args.managed_host.resolve()), str(BUILD / 'libdaisy_idle.so')], check=True, timeout=30)
            process = subprocess.Popen([str(BUILD / 'daisyos-idle-monitor'), '1'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
            try:
                assert select.select([process.stdout], [], [], 2)[0]
                assert process.stdout.readline() == b'IDLE\n'
                assert process.stdout.readline() == b'RESUMED\n'
                process.stdin.write(b'keep running\n')
                process.stdin.flush()
                time.sleep(.05)
                assert process.poll() is None
                process.stdin.close()
                assert process.wait(timeout=2) == 0
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
            # Stop/join must also work when the server has disconnected.
            handle = lib.daisy_idle_monitor_start(50, idle, resumed, None)
            time.sleep(.02)
            server.close()
            server = None
            lib.daisy_idle_monitor_stop(handle)
        finally:
            if server:
                server.close()
        os.unlink(temp + '/test-wayland')
        missing = Compositor(temp + '/test-wayland', advertise=False)
        try:
            assert lib.daisy_idle_monitor_is_available() == 0
        finally:
            missing.close()
        os.environ['WAYLAND_DISPLAY'] = 'absent'
        assert lib.daisy_idle_monitor_is_available() == 0
        assert subprocess.run([str(BUILD / 'daisyos-idle-monitor')], stdin=subprocess.DEVNULL, capture_output=True).returncode == 1
        assert subprocess.run([str(BUILD / 'daisyos-idle-monitor'), '4294967295'], stdin=subprocess.DEVNULL, capture_output=True).returncode == 2
    print('PASS: availability, callbacks/user data, 50 start/stop cycles, fd/thread cleanup, stdin, disconnect, missing protocol/display, overflow')


if __name__ == '__main__':
    main()

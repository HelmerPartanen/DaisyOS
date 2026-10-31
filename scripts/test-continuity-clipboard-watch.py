#!/usr/bin/env python3
"""Private Wayland socket: a forwarding timeout must not kill clipboard watching."""
import array
import os
from pathlib import Path
import re
import socket
import struct
import subprocess
import tempfile
import time

root = Path(__file__).resolve().parents[1]
source = (root / 'src/DaisyOS.Continuity/ClipboardBridge.cs').read_text()
script = re.search(r'private const string WaylandDataControlScript = """\n(.*?)\n""";', source, re.S)[1]
def message(obj, opcode, body=b''):
    return struct.pack('<II', obj, (len(body) + 8) << 16 | opcode) + body
def number(value):
    return struct.pack('<I', value)
def string(value):
    payload = value.encode() + b'\0'
    return number(len(payload)) + payload + b'\0' * (-len(payload) % 4)
with tempfile.TemporaryDirectory(prefix='daisy-clipboard-watch-') as temporary:
    directory = Path(temporary)
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    server.bind(str(directory / 'wayland-fixture'))
    server.listen(1)
    server.settimeout(10)
    result = directory / 'result'
    callback = 'import pathlib,sys,time; data=sys.stdin.buffer.read(); time.sleep(6) if data==b"slow" else pathlib.Path(sys.argv[1]).write_bytes(data)'
    env = dict(os.environ, WAYLAND_DISPLAY='wayland-fixture', XDG_RUNTIME_DIR=temporary)
    process = subprocess.Popen(['python3', '-c', script, 'watch', 'python3', '-c', callback, str(result)], env=env,
        stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    try:
        connection, _ = server.accept()
        connection.settimeout(10)
        buffered = b''
        descriptors = []
        def receive():
            global buffered
            while len(buffered) < 8 or len(buffered) < struct.unpack_from('<I', buffered, 4)[0] >> 16:
                chunk, ancillary, _, _ = connection.recvmsg(65536, socket.CMSG_SPACE(16))
                assert chunk, 'Clipboard watcher disconnected'
                buffered += chunk
                for level, kind, payload in ancillary:
                    if level == socket.SOL_SOCKET and kind == socket.SCM_RIGHTS:
                        values = array.array('i'); values.frombytes(payload[:len(payload) // values.itemsize * values.itemsize]); descriptors.extend(values)
            obj, packed = struct.unpack_from('<II', buffered)
            size = packed >> 16
            body, buffered = buffered[8:size], buffered[size:]
            return obj, packed & 0xffff, body
        while True:
            obj, opcode, body = receive()
            if obj == 1 and opcode == 0:
                callback_id = struct.unpack('<I', body)[0]
                connection.sendall(message(2, 0, number(1) + string('wl_seat') + number(1)) +
                    message(2, 0, number(2) + string('ext_data_control_manager_v1') + number(1)) +
                    message(callback_id, 0, number(1)))
                break
        while True:
            obj, opcode, body = receive()
            if obj == 1 and opcode == 0:
                connection.sendall(message(struct.unpack('<I', body)[0], 0, number(2)))
                break
        for index, text in enumerate((b'slow', b'next copy')):
            offer = 0xff000001 + index
            connection.sendall(message(6, 0, number(offer)) + message(offer, 0, string('text/plain')) + message(6, 1, number(offer)))
            while True:
                obj, opcode, body = receive()
                if obj == offer and opcode == 0:
                    assert descriptors, 'Clipboard receive omitted its pipe'
                    fd = descriptors.pop(0)
                    os.write(fd, text); os.close(fd)
                if obj == offer and opcode == 1:
                    break
            assert process.poll() is None, 'Forwarding timeout killed clipboard watching'
        assert result.read_bytes() == b'next copy'
        print('PASS: clipboard watcher survives forwarding timeout and delivers the next selection through a private Wayland connection')
    finally:
        process.terminate()
        process.wait(timeout=5)
        server.close()

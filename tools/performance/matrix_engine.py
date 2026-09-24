"""Experimental Unix-socket Engine attach, bounded demultiplexing, no CLI per worker.

Protocol: https://docs.docker.com/reference/api/engine/version/v1.54/
The Docker socket remains exclusively in the trusted host process, never in a plugin.
"""

import asyncio
import http.client
import json
import socket
from types import SimpleNamespace
import uuid
from matrix_lane import MatrixLane


def request(path, method, route, body=None):
    connection = http.client.HTTPConnection("localhost", timeout=8)
    try:
        connection.sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        connection.sock.settimeout(8)
        connection.sock.connect(path)
        encoded = json.dumps(body).encode() if body is not None else b""
        connection.request(
            method, route, body=encoded, headers={"Content-Type": "application/json"}
        )
        response = connection.getresponse()
        data = response.read(65537)
        if len(data) > 65536:
            raise ValueError("Oversized Engine management response")
        if method == "DELETE" and response.status == 404:
            return None
        if not 200 <= response.status < 300:
            raise RuntimeError(
                "Engine HTTP "
                + str(response.status)
                + ": "
                + data.decode(errors="replace")[:200]
            )
        return json.loads(data) if data else None
    finally:
        connection.close()


class BoundedLines(asyncio.StreamReader):
    def __init__(self):
        super().__init__(limit=1048576)
        self.pending = 0

    def append(self, data):
        if self.pending + len(data) > 1048576:
            raise ValueError("Oversized plugin output")
        self.pending += len(data)
        self.feed_data(data)

    async def readline(self):
        line = await super().readline()
        self.pending -= len(line)
        return line


async def demultiplex(reader, output):
    in_payload = False
    try:
        while True:
            in_payload = False
            header = await reader.readexactly(8)
            if header[0] not in (1, 2) or header[1:4] != b"\0\0\0":
                raise ValueError("Invalid Engine attach frame")
            size = int.from_bytes(header[4:], "big")
            if size > 1048576:
                raise ValueError("Oversized Engine attach frame")
            in_payload = True
            data = await reader.readexactly(size)
            in_payload = False
            if header[0] != 1:
                continue
            output.append(data)
    except asyncio.IncompleteReadError as error:
        if error.partial or in_payload:
            output.set_exception(ValueError("Truncated Engine frame"))
        else:
            output.feed_eof()
    except asyncio.CancelledError:
        output.feed_eof()
        raise
    except Exception as error:
        output.set_exception(error)


class EngineLane(MatrixLane):
    def __init__(self, mode, known, image, cpus=".5", memory=128, engine=None):
        super().__init__(mode, known, image, cpus, memory)
        self.engine = engine

    async def start(self):
        self.name = "wp-matrix-" + uuid.uuid4().hex
        self.known.add(self.name)
        fork = self.mode == "forkserver"
        tmpfs = {"/tmp": "rw,noexec,nosuid,size=16m,mode=1777"}
        if fork:
            tmpfs["/control"] = "rw,noexec,nosuid,size=8m,mode=0700"
        body = {
            "Image": self.image,
            "Cmd": ["trusted" if self.mode == "fresh" else self.mode],
            "User": "0:0" if fork else "65532:65532",
            "OpenStdin": True,
            "StdinOnce": False,
            "AttachStdin": True,
            "AttachStdout": True,
            "AttachStderr": True,
            "Tty": False,
            "Labels": {"weaveport.matrix-experiment": "true"},
            "HostConfig": {
                "Init": True,
                "NetworkMode": "none",
                "IpcMode": "private",
                "ReadonlyRootfs": True,
                "CapDrop": ["ALL"],
                "CapAdd": ["SETUID", "SETGID", "KILL"] if fork else [],
                "SecurityOpt": ["no-new-privileges"],
                "Memory": self.memory * 1048576,
                "MemorySwap": self.memory * 1048576,
                "NanoCpus": int(float(self.cpus) * 1000000000),
                "PidsLimit": 64,
                "Tmpfs": tmpfs,
                "ShmSize": 8 * 1048576,
                "LogConfig": {"Type": "none"},
            },
        }
        await asyncio.to_thread(
            request,
            self.engine.path,
            "POST",
            "/containers/create?name=" + self.name,
            body,
        )
        reader, writer = await asyncio.open_unix_connection(
            self.engine.path, limit=1048576
        )
        # Retain ownership immediately, including if attach/start fails.
        output = BoundedLines()
        self.process = SimpleNamespace(stdin=writer, stdout=output, pump=None)
        route = (
            "/containers/" + self.name + "/attach?stream=1&stdin=1&stdout=1&stderr=1"
        )
        writer.write(
            (
                "POST "
                + route
                + " HTTP/1.1\r\nHost: localhost\r\nConnection: Upgrade\r\nUpgrade: tcp\r\nContent-Length: 0\r\n\r\n"
            ).encode()
        )
        await writer.drain()
        headers = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 8)
        if len(headers) > 8192 or int(headers.split(b" ", 2)[1]) not in (101, 200):
            raise RuntimeError("Engine attach upgrade failed")
        self.process.pump = asyncio.create_task(demultiplex(reader, output))
        await asyncio.to_thread(
            request, self.engine.path, "POST", "/containers/" + self.name + "/start"
        )
        if not json.loads(await asyncio.wait_for(output.readline(), 8)).get("ready"):
            raise RuntimeError("Invalid ready handshake")
        self.starts += 1

    async def close(self):
        if self.name is None:
            return
        await asyncio.to_thread(
            request,
            self.engine.path,
            "DELETE",
            "/containers/" + self.name + "?force=true",
        )
        process = self.process
        self.name, self.process = None, None
        self.retirements += 1
        if process is None:
            return
        process.stdin.close()
        try:
            await process.stdin.wait_closed()
        except ConnectionError:
            pass  # The daemon has already confirmed this exact container's destruction.
        finally:
            if process.pump:
                process.pump.cancel()
                await asyncio.gather(process.pump, return_exceptions=True)

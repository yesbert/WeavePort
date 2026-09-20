"""Serial experimental lane with explicit, image-pinned isolation profiles."""
import asyncio
import json
import uuid
from reuse import Lane


class MatrixLane(Lane):
    def __init__(self, mode, known, image, cpus='.5', memory=128):
        super().__init__(mode, known)
        self.image, self.cpus, self.memory = image, str(cpus), memory

    async def start(self):
        self.name = 'wp-matrix-' + uuid.uuid4().hex
        self.known.add(self.name)
        privileged = self.mode == 'forkserver'
        profile = ['--user', '0:0', '--cap-add', 'SETUID', '--cap-add', 'SETGID', '--cap-add', 'KILL',
                   '--tmpfs', '/control:rw,noexec,nosuid,size=8m,mode=0700'] if privileged else ['--user', '65532:65532']
        args = ['docker', 'run', '--name', self.name, '--label', 'weaveport.matrix-experiment=true',
                '--init', '--interactive', '--network', 'none', '--ipc', 'private', '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', *profile, '--memory', str(self.memory) + 'm',
                '--memory-swap', str(self.memory) + 'm', '--cpus', self.cpus, '--pids-limit', '64',
                '--tmpfs', '/tmp:rw,noexec,nosuid,size=16m,mode=1777', '--shm-size', '8m', '--log-driver', 'none',
                self.image, 'trusted' if self.mode == 'fresh' else self.mode]
        self.process = await asyncio.create_subprocess_exec(*args, stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, limit=1048576)
        line = await asyncio.wait_for(self.process.stdout.readline(), 8)
        if not json.loads(line).get('ready'):
            raise RuntimeError('Invalid ready handshake')
        self.starts += 1

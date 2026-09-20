"""Retain evidence for the kernel-IPC boundary before and after the external audit."""
import argparse
import asyncio
from pathlib import Path
import docker_memory
from matrix_engine import EngineLane
from reuse import request, command, write


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True)
    image = await command('docker', 'image', 'inspect', 'weaveport-reuse-matrix:1', '--format', '{{.Id}}')
    engine, known, results = docker_memory.Engine(), set(), []
    for mode in ['fresh', 'process', 'trusted', 'forkserver']:
        lane = EngineLane(mode, known, image, engine=engine)
        try:
            leave = await lane.call(request(1, op='kernel_leave'))
            value = leave['outcome'].get('value') or {}
            probe = await lane.call(request(2, op='kernel_probe', shmid=value.get('shmid', -1)))
            results.append({'mode': mode, 'leave': leave, 'probe': probe})
            print(mode, leave['outcome'], probe['outcome'], flush=True)
        finally:
            await lane.close()
        write(args.output / 'kernel-probe.json', {'image': image, 'results': results})
    names = set((await command('docker', 'ps', '-a', '--format', '{{.Names}}')).splitlines())
    write(args.output / 'cleanup.json', {'known': sorted(known), 'remaining': sorted(known & names)})
    if known & names:
        raise RuntimeError('Owned IPC fixture containers remain')


if __name__ == '__main__':
    asyncio.run(main())

"""Docker checks of the two agreed reuse policies, including accepted residue risk."""
import asyncio
import json
import os
from pathlib import Path
import sys
import docker_memory
from matrix_engine import EngineLane
from matrix_policy import PolicyPool
from reuse import command, correct, request, write


async def main(folder):
    folder.mkdir()
    image = await command('docker', 'image', 'inspect', os.environ['WEAVEPORT_MATRIX_IMAGE'], '--format', '{{.Id}}')
    engine, known, checks, rows = docker_memory.Engine(), set(), [], []
    def check(name, value):
        checks.append({'name': name, 'passed': bool(value)})
    for policy in ('bound', 'approved'):
        lane = EngineLane('trusted', known, image, cpus=1, memory=64, engine=engine)
        pool = PolicyPool([lane], policy)
        async def call(tenant='a', plugin='a', op='echo', **extra):
            row = await pool.call(request(len(rows), tenant=tenant, plugin=plugin, op=op, **extra))
            rows.append(row)
            return row
        try:
            first = await call(op='stash')
            repeat = await call(op='probe')
            check(policy + ': same binding stays warm', first['container'] == repeat['container'])
            check(policy + ': hidden state persists within same binding', repeat['outcome']['value']['hidden'] == 'a')
            other = await call(tenant='b', op='probe')
            check(policy + ': customer switch obeys policy', (other['container'] != repeat['container']) == (policy == 'bound'))
            check(policy + ': hidden-state boundary matches accepted risk', (other['outcome']['value']['hidden'] is None) == (policy == 'bound'))
            plugin = await call(tenant='b', plugin='b', op='probe')
            check(policy + ': plugin switch obeys policy', (plugin['container'] != other['container']) == (policy == 'bound'))
            version = await call(tenant='b', plugin='b', version='fixture-v2')
            check(policy + ': version switch obeys policy', (version['container'] != plugin['container']) == (policy == 'bound'))
            for i in range(30):
                row = await call(tenant='session-' + str(i), plugin='a' if i % 2 == 0 else 'b', op='session')
                check(policy + ': managed session ' + str(i), correct(row))
            for fault in ('fd_leak', 'cleanup_failure', 'thread_ignores_stop', 'hang', 'crash'):
                failed = await call(op=fault)
                recovered = await call()
                check(policy + ': ' + fault + ' retires and recovers', not failed['outcome'].get('reusable')
                      and correct(recovered) and failed['container'] != recovered['container'])
        finally:
            await lane.close()
            write(folder / 'checks.json', {'image': image, 'checks': checks, 'rows': rows, 'known': sorted(known)})
    names = set((await command('docker', 'ps', '-a', '--format', '{{.Names}}')).splitlines())
    write(folder / 'cleanup.json', {'known': sorted(known), 'remaining': sorted(names & known)})
    print(sum(c['passed'] for c in checks), '/', len(checks), 'checks passed', flush=True)
    if names & known or not all(c['passed'] for c in checks):
        raise RuntimeError('Policy check failed')


if __name__ == '__main__':
    asyncio.run(main(Path(sys.argv[1])))

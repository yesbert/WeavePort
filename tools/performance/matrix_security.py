"""Expected positive and negative controls, with fail-closed recovery evidence."""

import asyncio
from matrix_lane import MatrixLane
from reuse_support import request, correct, write


async def run(
    output, image, modes=("fresh", "process", "trusted", "forkserver"), engine=None
):
    import docker_memory

    observer = engine or docker_memory.Engine()
    evidence, known = [], set()
    for mode in modes:
        group = await verify_mode(mode, known, image, engine, observer)
        evidence.append(group)
        write(output / "security.json", evidence)
        checks = group["checks"]
        print(
            mode,
            sum(check["passed"] for check in checks),
            "/",
            len(checks),
            "controls passed",
            flush=True,
        )
    remaining = await asyncio.create_subprocess_exec(
        "docker", "ps", "-a", "--format", "{{.Names}}", stdout=asyncio.subprocess.PIPE
    )
    names = set((await remaining.communicate())[0].decode().splitlines())
    write(
        output / "cleanup.json",
        {"known": sorted(known), "remaining": sorted(names & known)},
    )
    if names & known or any(
        g.get("failure") or not all(c["passed"] for c in g["checks"]) for g in evidence
    ):
        raise RuntimeError(
            "Security expectations or cleanup failed; inspect retained evidence"
        )


async def verify_mode(mode, known, image, engine, observer):
    if engine:
        from matrix_engine import EngineLane

        lane = EngineLane(mode, known, image, engine=engine)
    else:
        lane = MatrixLane(mode, known, image)
    rows, checks = [], []

    def check(name, condition):
        checks.append({"name": name, "passed": bool(condition)})

    async def call(op="echo", **extras):
        row = await lane.call(request(len(rows), op=op, **extras))
        rows.append(row)
        return row

    async def value(op, **extras):
        row = await call(op, **extras)
        check(op + " returned successfully", row["outcome"]["status"] == "ok")
        return row["outcome"].get("value") or {}

    try:
        profile = await verify_profile(lane, observer, mode, check)
        for _ in range(4):
            check(
                "distinct customer / alternating plugin correctness",
                correct(await call()),
            )
        check(
            "Unicode, delimiters and embedded newlines survive transport",
            correct(await call(payload='Kunde-ä-東京-😀\n\0"' * 20)),
        )
        await verify_state_cleanup(value, mode, check)
        await verify_kernel_cleanup(call, value, check)
        authority = await value("authority")
        check(
            "cooperative grant and expiry",
            authority.get("denied") and authority.get("expired"),
        )
        forged = await value("forge_authority")
        check(
            "negative control: local scope grants are NOT a security boundary",
            forged.get("forgedLocalGrantWorked"),
        )
        await verify_forkserver(call, value, mode, check)
        for fault in (
            "fd_leak",
            "cleanup_failure",
            "untracked_thread",
            "thread_ignores_stop",
            "escaped_child",
            "crash",
            "hang",
            "cleanup_hang",
            "memory_bomb",
            "stdout_forge",
            "oversized_frame",
            "pickle_frame",
        ):
            await verify_fault(call, mode, fault, check)
        # Verify alternating state mutation + normal calls, after all fault recoveries.
        for _ in range(5):
            await value("environment")
            check("normal call after state mutation", correct(await call()))
        group = {
            "mode": mode,
            "checks": checks,
            "rows": rows,
            "crossTenantMemoryLeakExpected": mode == "trusted",
            "containerProfile": profile,
        }
    except Exception as error:
        group = {
            "mode": mode,
            "checks": checks,
            "rows": rows,
            "failure": repr(error),
        }
    finally:
        await lane.close()
    return group


async def verify_profile(lane, observer, mode, check):
    await lane.start()
    inspected = await asyncio.to_thread(
        observer.get, "/containers/" + lane.name + "/json"
    )
    profile = inspected["HostConfig"]
    check(
        "Engine enforces equivalent resource and isolation profile",
        profile["NetworkMode"] == "none"
        and profile["IpcMode"] == "private"
        and profile["ReadonlyRootfs"]
        and not profile["Privileged"]
        and profile["Memory"] == 128 * 1048576
        and profile["MemorySwap"] == 128 * 1048576
        and profile["NanoCpus"] == 500000000
        and profile["PidsLimit"] == 64
        and profile["Init"]
        and profile["CapDrop"] == ["ALL"]
        and {name.removeprefix("CAP_") for name in (profile.get("CapAdd") or [])}
        == ({"SETUID", "SETGID", "KILL"} if mode == "forkserver" else set())
        and not profile.get("Binds")
        and "no-new-privileges" in profile["SecurityOpt"]
        and inspected["Config"]["User"]
        == ("0:0" if mode == "forkserver" else "65532:65532"),
    )
    return profile


async def verify_state_cleanup(value, mode, check):
    await value("stash")
    residue = await value("probe")
    leaked = [
        bool(residue.get(k))
        for k in ("hidden", "defaults", "context", "logger", "cacheEntries")
    ]
    check(
        "five memory-residue controls match isolation promise",
        all(leaked) if mode == "trusted" else not any(leaked),
    )
    await value("environment")
    env = await value("environment_probe")
    check(
        "environment/cwd/umask/signals restored",
        env
        == {
            "environment": None,
            "cwd": "/fixture",
            "umask": 18,
            "signalIgnored": False,
        },
    )
    await value("files")
    check("tmp/shm/symlink cleanup", not (await value("probe")).get("extraFiles"))


async def verify_kernel_cleanup(call, value, check):
    check(
        "kernel keyring creation denied by container syscall policy",
        (await value("kernel_keyring")).get("blocked"),
    )
    left = await call("kernel_leave")
    shared = await call(
        "kernel_probe",
        shmid=(left["outcome"].get("value") or {}).get("shmid", -1),
    )
    check(
        "kernel shared-memory residue retires container before next customer",
        left["outcome"]["status"] == "ok"
        and not left["outcome"]["reusable"]
        and shared["outcome"].get("value") == {"accessible": False, "canary": None}
        and left["container"] != shared["container"],
    )
    for kernel_op in ("kernel_semaphore", "kernel_messages", "posix_queue"):
        left = await call(kernel_op)
        recovered = await call()
        check(
            kernel_op + " residue retires container and recovers",
            left["outcome"]["status"] == "ok"
            and not left["outcome"]["reusable"]
            and correct(recovered)
            and left["container"] != recovered["container"],
        )


async def verify_forkserver(call, value, mode, check):
    if mode != "forkserver":
        return
    delayed = await call(exitDelayMs=250)
    continued = await call()
    check(
        "process exit within shared invocation deadline remains reusable",
        correct(delayed)
        and not delayed["outcome"].get("forcedTermination")
        and correct(continued)
        and delayed["container"] == continued["container"],
    )
    expired = await call(exitDelayMs=2000)
    recovered = await call()
    check(
        "process exit beyond shared invocation deadline retires container",
        expired["outcome"].get("forcedTermination")
        and expired["outcome"].get("error") == "child-deadline"
        and not expired["outcome"]["reusable"]
        and correct(recovered)
        and expired["container"] != recovered["container"],
    )
    supervisor = await value("supervisor")
    check(
        "UID/capabilities/proc/signal boundary",
        supervisor
        == {
            "uid": 65532,
            "gid": 65532,
            "supervisorReadable": False,
            "supervisorSignallable": False,
            "effectiveCaps": "0000000000000000",
            "hostMountPresent": False,
        },
    )
    check(
        "template socket directory inaccessible",
        not (await value("control_access")).get("accessible", True),
    )


async def verify_fault(call, mode, fault, check):
    failed = await call(fault)
    if fault == "pickle_frame" and mode == "forkserver":
        check(
            "child bytes never execute pickle in supervisor",
            failed["outcome"].get("untrustedDeserializeMarkerPresent") is False,
        )
    next_call = await call()
    if fault == "stdout_forge" and mode == "forkserver":
        check(
            "child stdout cannot forge coordinator frames",
            failed["outcome"]["status"] == "ok"
            and correct(next_call)
            and failed["container"] == next_call["container"],
        )
    else:
        check(
            fault + " fails closed and next customer recovers",
            not failed["outcome"].get("reusable")
            and correct(next_call)
            and failed["container"] != next_call["container"],
        )


async def main():
    import argparse
    import hashlib
    from pathlib import Path
    from density import identities
    from reuse_support import command

    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    parser.add_argument("--engine", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True)
    identity = identities(args.output)
    image = await command(
        "docker", "image", "inspect", "weaveport-reuse-matrix:1", "--format", "{{.Id}}"
    )
    root = Path(__file__).resolve().parents[2]
    paths = (
        list((root / "benchmarks/WeavePort.Reuse").rglob("*.py"))
        + list((root / "tools/performance").glob("*.py"))
        + [root / "benchmarks/WeavePort.Reuse/Matrix.Dockerfile"]
    )
    identity.update(
        {
            "image": image,
            "transport": "engine" if args.engine else "cli",
            "securityHashes": {
                str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in paths
            },
        }
    )
    write(args.output / "identity.json", identity)
    import docker_memory

    await run(
        args.output, image, engine=docker_memory.Engine() if args.engine else None
    )


if __name__ == "__main__":
    asyncio.run(main())

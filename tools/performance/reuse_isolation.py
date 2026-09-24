"""Positive and deliberate negative controls for the original reuse experiment."""

from reuse_lane import Lane
from reuse_support import request, correct, write


async def isolation(output, image):
    evidence = []
    known = set()
    for mode in ("fresh", "process", "trusted"):
        evidence.append(await verify_mode(mode, known, image))
    write(output / "isolation.json", evidence)
    if not all(check["passed"] for group in evidence for check in group["checks"]):
        raise RuntimeError("Isolation control did not match expected behavior")
    print(
        "Isolation controls passed; trusted hidden-global leak deliberately reproduced.",
        flush=True,
    )


async def verify_mode(mode, known, image):
    lane = Lane(mode, known, image)
    rows = []
    checks = []

    async def call(op="echo", **extras):
        row = await lane.call(request(len(rows), op=op, **extras))
        rows.append(row)
        return row

    def check(name, condition):
        checks.append({"name": name, "passed": bool(condition)})

    try:
        for _ in range(4):
            check("alternating customer and plugin identity", correct(await call()))
        managed = await call("managed")
        check("managed resource call succeeds", managed["outcome"]["status"] == "ok")
        probe = (
            await call(
                "probe", oldPid=managed["outcome"].get("value", {}).get("pid", -1)
            )
        )["outcome"]["value"]
        check(
            "managed file/environment/child removed",
            not probe["files"]
            and probe["environment"] is None
            and not probe["oldPidAlive"],
        )
        a = (await call("authority"))["outcome"]["value"]
        check(
            "grant and expiry enforced by scope",
            a["denied"] and a["expired"] and a["granted"].startswith("customer-"),
        )
        await call("unmanaged_file")
        check(
            "both writable directories swept",
            not (await call("probe"))["outcome"]["value"]["files"],
        )
        for fault in (
            "cleanup_failure",
            "untracked_thread",
            "escaped_child",
            "crash",
            "hang",
        ):
            failed = await call(fault)
            next_call = await call()
            check(
                fault + " poisons and replaces container",
                not failed["outcome"]["reusable"]
                and correct(next_call)
                and next_call["container"] != failed["container"],
            )
        hidden = await call("hidden")
        revealed = (await call("probe"))["outcome"]["value"]["hidden"]
        # This intentionally demonstrates a limitation, not a claim that the trusted mode is secure.
        check(
            "hidden-global negative control has expected outcome",
            (
                revealed == hidden["request"]["tenant"]
                if mode == "trusted"
                else revealed is None
            ),
        )
        evidence.append(
            {
                "mode": mode,
                "checks": checks,
                "hiddenGlobalCrossTenantLeak": revealed is not None,
                "securityBoundary": (
                    "fresh container lifecycle"
                    if mode == "fresh"
                    else "cooperative prototype, not hostile-code isolation"
                ),
                "rows": rows,
            }
        )
    finally:
        await lane.close()

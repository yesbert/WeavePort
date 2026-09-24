"""Bounded many-customer benchmark; experimental transport, not production server capacity."""

import argparse
import asyncio
import hashlib
import json
import os
from pathlib import Path
import docker_memory
from density import identities, pressure
from matrix_lane import MatrixLane
from matrix_measurement import MatrixMeasurement
from matrix_engine import EngineLane
from reuse_support import command, write

ROOT = Path(__file__).resolve().parents[2]


async def measure(folder, config, image, engine):
    lane_type = EngineLane if config.get("transport") == "engine" else MatrixLane
    return await MatrixMeasurement(
        folder, config, image, engine, lane_type, pressure, command
    ).run()


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("config", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True)
    identity = identities(args.output)
    image = await command(
        "docker",
        "image",
        "inspect",
        os.environ.get("WEAVEPORT_MATRIX_IMAGE", "weaveport-reuse-matrix:1"),
        "--format",
        "{{.Id}}",
    )
    sources = (
        list((ROOT / "benchmarks/WeavePort.Reuse").rglob("*.py"))
        + list((ROOT / "tools/performance").glob("*.py"))
        + [ROOT / "benchmarks/WeavePort.Reuse/Matrix.Dockerfile"]
    )
    identity.update(
        {
            "image": image,
            "matrixHashes": {
                str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sources
            },
        }
    )
    write(args.output / "identity.json", identity)
    configs, results = json.loads(args.config.read_text()), []
    engine = docker_memory.Engine()
    for i, config in enumerate(configs):
        if (
            not 1
            <= config["workers"]
            <= (128 if config.get("transport") == "engine" else 32)
            or not 0 < config.get("seconds", 15) <= 3600
        ):
            raise ValueError("Experimental resource guard exceeded")
        if config["workers"] * config.get("memory", 128) > 8192:
            raise ValueError("Experimental pool memory guard exceeded")
        results.append(
            await measure(
                args.output / f'{i:03d}-{config["mode"]}-{config["workers"]}',
                config,
                image,
                engine,
            )
        )
        write(args.output / "summary.json", results)


if __name__ == "__main__":
    asyncio.run(main())

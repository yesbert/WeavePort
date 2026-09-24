"""Run qualification stages and persist progress before propagating failures."""

import json
import re
import subprocess
import time


class StageRecorder:
    def __init__(self, checkout, environment, evidence, record):
        self.checkout = checkout
        self.environment = environment
        self.evidence = evidence
        self.record = record

    def __call__(self, name, command, expected=0, cwd=None):
        print("STAGE: " + name, flush=True)
        started = time.monotonic()
        log = self.evidence / "logs" / (name + ".log")
        with log.open("w") as output:
            result = subprocess.run(
                command,
                cwd=cwd or self.checkout,
                env=self.environment,
                stdout=output,
                stderr=subprocess.STDOUT,
            )
        text = log.read_text()
        item = dict(
            name=name,
            exitCode=result.returncode,
            expectedExitCode=expected,
            seconds=round(time.monotonic() - started, 3),
            assertions=count_assertions(text),
        )
        self.record["stages"].append(item)
        (self.evidence / "result.json").write_text(json.dumps(self.record, indent=2))
        if result.returncode != expected:
            raise RuntimeError("Stage failed: " + name + "; inspect retained log")
        return text


def count_assertions(text):
    individual = len(re.findall(r"^PASS:", text, re.M))
    grouped = sum(int(count) for count in re.findall(r"^PASS (\d+) ", text, re.M))
    return individual + grouped

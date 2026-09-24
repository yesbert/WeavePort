"""Request selection and per-customer result accounting."""

from matrix_metrics import Histogram
from reuse_support import correct, request


def customer_key(stage, i):
    customer = (
        i % stage.population if stage.population else i // stage.calls_per_customer
    )
    plugin = (
        (i // stage.population if stage.config.get("switchPlugins") else customer)
        % stage.plugins_per_customer
        if stage.population
        else i % stage.plugins_per_customer
    )
    return (customer, plugin)


def choose(stage, i):
    op = stage.config.get("workload", "echo")
    if stage.config.get("faultEvery") and i % stage.config["faultEvery"] == 0:
        op = ("hang", "crash", "cleanup_failure")[i // stage.config["faultEvery"] % 3]
    if op == "blend":
        op = "io" if i % 100 == 0 else "cpu" if i % 10 == 0 else "echo"
    customer, plugin = customer_key(stage, i)
    return request(
        i,
        op=op,
        plugin="a" if plugin == 0 else "b",
        tenant="customer-" + str(customer),
        payload=stage.payload,
        ioMs=stage.config.get("ioMs", 100),
        cpuMs=stage.config.get("cpuMs", 2),
        memoryMiB=stage.config.get("memoryMiB", 8),
    )


def consume(stage, row):
    stage.counts["completed"] += 1
    valid = correct(row)
    expected_fault = row["request"]["op"] in ("hang", "crash", "cleanup_failure")
    contained = (
        expected_fault
        and row["outcome"].get("status") == "failed"
        and (not row["outcome"].get("reusable"))
    )
    stage.counts["faultsExpected"] += int(expected_fault)
    stage.counts["faultsContained"] += int(contained)
    stage.counts["correct"] += int(valid)
    stage.counts["failed"] += int(not valid and (not contained))
    if valid:
        complete_customer(stage, row["request"]["tenant"])
    stage.counts["withinOneSecond"] += int(valid and row["responseMs"] <= 1000)
    op = row["request"]["op"]
    stage.window.add(row["responseMs"])
    if op == "echo":
        stage.window_short.add(row["responseMs"])
    stage.classes.setdefault(op, Histogram()).add(row["responseMs"])
    for key, histogram in stage.hist.items():
        record_histogram(stage, histogram, key, row)
    if not valid and (not contained) and (len(stage.failures) < 20):
        stage.failures.append(
            {k: v for k, v in row.items() if k != "request"}
            | {"id": row["request"]["id"], "op": op}
        )


def complete_customer(stage, customer):
    stage.customer_calls[customer] = stage.customer_calls.get(customer, 0) + 1
    if stage.customer_calls[customer] != stage.calls_per_customer:
        return
    stage.counts["customersCompleted"] += 1
    del stage.customer_calls[customer]


def record_histogram(stage, histogram, key, row):
    if key in row:
        histogram.add(row[key])
        return
    if key in row["outcome"]:
        histogram.add(row["outcome"][key])

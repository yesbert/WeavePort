"""Bounded-memory latency histograms (upper bucket edge, <=1% quantization)."""
import math


class Histogram:
    def __init__(self):
        self.buckets, self.count, self.total, self.maximum = {}, 0, 0.0, 0.0

    def add(self, value):
        value = max(0.0, value)
        key = math.ceil(math.log(max(.001, value), 1.01))
        self.buckets[key] = self.buckets.get(key, 0) + 1
        self.count += 1
        self.total += value
        self.maximum = max(self.maximum, value)

    def report(self):
        if not self.count:
            return {'count': 0}
        result = {'count': self.count, 'mean': self.total / self.count, 'max': self.maximum}
        for name, fraction in [('p50', .5), ('p95', .95), ('p99', .99)]:
            target, count = math.ceil(self.count * fraction), 0
            for key, occurrences in sorted(self.buckets.items()):
                count += occurrences
                if count >= target:
                    result[name] = min(self.maximum, 1.01 ** key)
                    break
        return result

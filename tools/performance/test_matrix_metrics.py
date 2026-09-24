import unittest
from matrix_metrics import Histogram


class HistogramTests(unittest.TestCase):
    def test_empty(self):
        self.assertEqual({"count": 0}, Histogram().report())

    def test_upper_bound_and_quantization(self):
        values = [0.001 * 1.007**i for i in range(2000)]
        histogram = Histogram()
        for value in values:
            histogram.add(value)
        result = histogram.report()
        for key, index in [("p50", 999), ("p95", 1899), ("p99", 1979)]:
            self.assertGreaterEqual(result[key], values[index])
            self.assertLessEqual(result[key], values[index] * 1.01)
        self.assertAlmostEqual(result["mean"], sum(values) / len(values))
        self.assertEqual(result["max"], values[-1])

    def test_million_identical_samples_bounded(self):
        histogram = Histogram()
        for _ in range(1000000):
            histogram.add(3.14159)
        self.assertEqual(1, len(histogram.buckets))
        self.assertEqual(1000000, histogram.report()["count"])
        self.assertEqual(3.14159, histogram.report()["p99"])


if __name__ == "__main__":
    unittest.main()

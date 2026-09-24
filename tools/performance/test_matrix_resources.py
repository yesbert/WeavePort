import unittest
from matrix_resources import ResourceTotals


def sample(working, raw, host=30, helper=0, live=True):
    return dict(
        owned=[{"usageBytes": raw * 1048576}] if live else [],
        ownedMiB=working,
        ownedRawMiB=raw,
        hostRssMiB=host,
        helpersRssMiB=helper,
    )


class ResourceControls(unittest.TestCase):
    def test_earlier_peaks_survive_lower_final_sample(self):
        totals = ResourceTotals()
        totals.add(sample(100, 120, host=80, helper=50))
        totals.add(sample(30, 40))
        self.assertEqual(
            (2, 100, 120, 80, 50),
            (
                totals.live_samples,
                totals.working_peak,
                totals.raw_peak,
                totals.host_peak,
                totals.helpers_peak,
            ),
        )

    def test_missing_live_memory_is_unavailable_not_zero(self):
        totals = ResourceTotals()
        totals.add(sample(0, 0, live=False))
        self.assertIsNone(totals.working_peak)
        self.assertIsNone(totals.raw_peak)
        self.assertEqual(0, totals.live_samples)
        self.assertEqual(30, totals.host_peak)

    def test_summary_retains_no_per_container_or_per_sample_history(self):
        totals = ResourceTotals()
        for _ in range(1000):
            totals.add(sample(10, 12))
        self.assertEqual(1000, totals.live_samples)
        self.assertTrue(
            all(
                value is None or isinstance(value, (int, float))
                for value in vars(totals).values()
            )
        )


if __name__ == "__main__":
    unittest.main()

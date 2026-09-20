"""Constant-memory resource summary; full chronological samples stay on disk."""
class ResourceTotals:
    def __init__(self):
        self.live_samples = 0
        self.working_peak = self.raw_peak = None
        self.host_peak = self.helpers_peak = None

    @staticmethod
    def maximum(previous, value):
        return value if previous is None else max(previous, value)

    def add(self, sample):
        self.host_peak = self.maximum(self.host_peak, sample['hostRssMiB'])
        self.helpers_peak = self.maximum(self.helpers_peak, sample['helpersRssMiB'])
        if any('usageBytes' in row for row in sample['owned']):
            self.live_samples += 1
            self.working_peak = self.maximum(self.working_peak, sample['ownedMiB'])
            self.raw_peak = self.maximum(self.raw_peak, sample['ownedRawMiB'])

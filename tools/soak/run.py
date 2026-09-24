"""Run a supervised, local-only k6 soak against packed multilingual plugin fixtures."""

from configuration import parse_options
from experiment import SoakRun


def main():
    args, executables = parse_options()
    return SoakRun(args, executables).run()


if __name__ == "__main__":
    raise SystemExit(main())

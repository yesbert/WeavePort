import runpy
import sys

sys.path.insert(0, sys.argv[1])
runpy.run_path(sys.argv[2], run_name="__main__")

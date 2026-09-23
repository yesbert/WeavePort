#!/usr/bin/env python3
"""Compatibility entry point; manifest policy belongs to the packaged .NET sealer."""
import pathlib
import subprocess
import sys

subprocess.run([str(pathlib.Path(__file__).with_suffix('.sh')), *sys.argv[1:]], check=True)

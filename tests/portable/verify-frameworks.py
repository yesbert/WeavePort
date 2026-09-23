"""Compare WeavePort matching to real dotnet framework selection without plugin code."""
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
work = root / 'artifacts/portable/framework-oracle'
work.mkdir(parents=True, exist_ok=True)
(work / 'Oracle.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><UseAppHost>false</UseAppHost></PropertyGroup></Project>')
(work / 'Program.cs').write_text('System.Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);')
subprocess.run([sys.argv[1], 'build', str(work / 'Oracle.csproj'), '-c', 'Release'], check=True, capture_output=True)
output = work / 'bin/Release/net8.0'
inventory = subprocess.check_output([sys.argv[1], '--list-runtimes'], text=True)
core = [line.split()[1] for line in inventory.splitlines() if line.startswith('Microsoft.NETCore.App ') and '-' not in line.split()[1]]
minimum_major = min(int(v.split('.')[0]) for v in core)
minimum = f'{minimum_major}.0.0'
cases = []
for policy in ['Disable', 'LatestPatch', 'Minor', 'LatestMinor', 'Major', 'LatestMajor']:
    cases.append({'runtimeOptions': {'rollForward': policy, 'framework': {'name': 'Microsoft.NETCore.App', 'version': minimum}}})
for version in [core[-1], '99.0.0', f'{minimum_major}.0.999']:
    cases.append({'runtimeOptions': {'rollForward': 'Disable', 'framework': {'name': 'Microsoft.NETCore.App', 'version': version}}})
cases.append({'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': minimum}, {'name': 'Microsoft.AspNetCore.App', 'version': minimum}]}})
cases.append({'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': minimum}, {'name': 'Missing.Framework', 'version': minimum}]}})
for policy in ['Disable', 'LatestPatch', 'Minor', 'LatestMinor', 'Major', 'LatestMajor']:
    for core_version, aspnet_version in [('8.0.0', '10.0.0'), ('10.0.0', '8.0.0'), ('8.0.28', '8.0.0'), ('9.0.0', '9.0.0')]:
        cases.append({'runtimeOptions': {'rollForward': policy, 'frameworks': [
            {'name': 'Microsoft.NETCore.App', 'version': core_version},
            {'name': 'Microsoft.AspNetCore.App', 'version': aspnet_version}]}})
results = []
for config in cases:
    (output / 'Oracle.runtimeconfig.json').write_text(json.dumps(config))
    run = subprocess.run([sys.argv[1], str(output / 'Oracle.dll')], text=True, capture_output=True, timeout=10)
    results.append(dict(config=json.dumps(config), inventory=inventory, expected=run.returncode == 0, observed=run.stdout.strip()))
path = work / 'cases.json'
path.write_text(json.dumps(results, indent=2) + '\n')
subprocess.run([sys.argv[1], 'run', '--project', str(root / 'tests/WeavePort.Hosting.Tests'), '-c', 'Release', '--', '--framework-oracle', str(path)], check=True)

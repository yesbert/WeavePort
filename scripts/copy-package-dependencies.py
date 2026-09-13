"""Copy the reviewed net10.0 external package closure from the actual restore."""
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
expected = json.loads((root / "compatibility/external-packages.json").read_text())
assets = json.loads((root / "src/WeavePort.Hosting/obj/project.assets.json").read_text())
actual = {key.split('/')[0]: key.split('/')[1] for key, value in assets['libraries'].items()
          if value['type'] == 'package'}
if actual != expected:
    raise ValueError("External dependency closure differs from reviewed net10.0 identities")
for name, version in expected.items():
    relative = Path(name.lower(), version, f"{name.lower()}.{version}.nupkg")
    candidates = [Path(folder) / relative for folder in assets['packageFolders']]
    source = next(path for path in candidates if path.is_file())
    with zipfile.ZipFile(source) as archive:
        metadata = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith('.nuspec'))))
        if metadata.find('.//{*}license').text != 'MIT':
            raise ValueError("External package license differs from review")
    shutil.copyfile(source, root / 'artifacts/packages' / f'{name}.{version}.nupkg')
print('Reviewed external package closure copied with original license metadata.')

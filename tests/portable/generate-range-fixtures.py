"""Optional fixture refresh: run from the repository with packaging==25.0 installed."""
import json
from packaging.specifiers import SpecifierSet
ranges=['>=3.11,<4','~=3.11','~=3.11.0','==3.11.*','!=3.11.*','==3.11','!=3.11','<3.11','<=3.11','>3.11','>=3.11','>=3.11a1','>=3.11rc1','>=3.11.dev1','>3.11.post1','<=3.11rc2','~=3.11a1','==1!3.11.*','>=1!3.11','==3.11+abc','===3.11.0','>=3.11,!=3.12.*','>=3.11rc1,<3.11','>3.11a1','<3.11.post1','>=3.11rc1,!=3.11rc2']
versions=['3.10.9','3.11','3.11.0','3.11.1','3.12','4.0','3.11a0','3.11a1','3.11b1','3.11rc1','3.11rc2','3.11.dev1','3.11a1.dev1','3.11.post0','3.11.post1','3.11.post1.dev1','3.11+abc','3.11+def','3.11+123','1!3.11','3.12a1','0!3.11','3.11.0.0','3.11preview2','v3.11','3.11-1']
cases = [dict(range=r,version=v,expected=SpecifierSet(r).contains(v)) for r in ranges for v in versions]
open('tests/WeavePort.Hosting.Tests/fixtures/python-ranges.json','w').write('[\n' + ',\n'.join(json.dumps(c) for c in cases) + '\n]\n')

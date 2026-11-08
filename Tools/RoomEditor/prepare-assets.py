#!/usr/bin/env python3
"""Copy the already exported, public game assets. No private extraction tools required."""
from pathlib import Path
import json, shutil
base=Path(__file__).resolve().parent
repo=base.parent.parent
source=repo/'RemainsUnity/Assets/Remains/Rooms/Original'
target=base/'Web/wwwroot/data'
if target.exists():
    shutil.rmtree(target)
target.mkdir(parents=True,exist_ok=True)
for name in ['definitions.json','lands.json','decor-without-picture.json']:
    shutil.copy2(source/name,target/name)
shutil.copytree(source/'Tiles',target/'Tiles',ignore=shutil.ignore_patterns('*.meta','*.asset'),dirs_exist_ok=True)
rooms=[]
for path in sorted(source.rglob('*.room.json')):
    relative=path.relative_to(source)
    destination=target/'rooms'/relative
    destination.parent.mkdir(parents=True,exist_ok=True)
    shutil.copy2(path,destination)
    rooms.append(relative.as_posix())
(target/'rooms.json').write_text(json.dumps(rooms,ensure_ascii=False),encoding='utf-8')
for name in ['movement.json','anatomy.json']:
    shutil.copy2(repo/'RemainsUnity/Assets/Remains/Resources/Content'/name,target/name)
for name in ['LICENSING.md','THIRD_PARTY_NOTICES.md']:
    shutil.copy2(repo/name,target/name)
print(f'Prepared {len(rooms)} rooms and public artwork in {target}')

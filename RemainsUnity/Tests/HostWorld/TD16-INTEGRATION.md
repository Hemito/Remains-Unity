# Active grenade telekinesis and shared host compatibility

This separate integration combines TD33 `fb01263e855332828434a2e38b36ed1536c550b9` with the published TD16 feature `656df0a97d9218db42a128c749f981af122dac10`. It includes the new `GrenadeHoldRequest` and persisted holder, so both participants use protocol100. The original TD33 feature branch and its protocol are unchanged.

The existing TD16 `NetworkGrenadeTelekinesisProbe` drives the actual player commands. The client throws; the host catches the live airborne grenade. It verifies ownership transfer, generation1→2, movement while the fuse continues, a stale pose from the former owner, release to generation3 followed by ballistic flight or expiration while held at generation2, exactly one blast/visual/receipt per player, and inventory counts4/5. This integration additionally verifies separate canonical/flight objects, shared-world identity matching host mode, and a stale capture replay through the common request coordinator. Server ownership, actor lifecycle, generation, pause, mana and sequence checks remain strict. The ordinary `grenade` probe is a different scenario and cannot supply this evidence.

Run these Editor methods on the imported integration project:

- `Remains.Editor.GrenadeTelekinesisChecks.Run`
- `Remains.Editor.HostWorldChecks.RunCore`

Then use one exact native Player build for all four graphical pairs, with unique fresh save directories managed by the runner:

```sh
python3 RemainsUnity/Tests/HostWorld/verify.py --player /path/to/Remains --output /path/to/new-results --modes on off --scenarios grenade-telekinesis grenade-telekinesis-expire
```

Both host and guest reports and exit codes must succeed. `detachedFlight` must be true in every report; the host's `sharedWorld` must be false for self-ticks on and true for off, and `staleHoldReplaySent` must be true. All original generation/fuse/inventory/explosion checks remain required. The runner records commands, exact managed assembly hash, modes and reports.

`test/host-grenade-telekinesis` is the regression baseline. The dependent `fix/shared-host-grenade-telekinesis` routes the new hold registration and request through TD33's existing coordinator and queue. That fix does not change wire messages or relax the remote validation path.

## Native acceptance baseline

On 2026-11-07, `test/td16-native-validation` at `cf375f27188579a474fa737b7029f089ae64a633` passed `Td16NativeValidationChecks.RunBatch`: host core contracts and the original grenade telekinesis rule/copy checks. Linux Release then built successfully with Unity6000.0.39f1 and two workers, from clean source. Its managed assembly SHA256 was `1704c0f6be31f3f1e15e89bd38b2756071c5c14292a1d63c8bd0f9802ad249d9`.

Four genuine graphical GLCore/Mesa llvmpipe pairs ran at1280×720 with separate ports and eight separate save directories:

| Host self-ticks | Scenario | Result | Client observation |
| --- | --- | --- | --- |
| off | release | PASS | 4.098m held travel, 0.717s fuse advance |
| off | expire while held | FAIL | First held snapshot arrived late; only0.328m travel recorded |
| on | release | FAIL | Only the final held sample was recorded; travel and observed fuse advance were0 |
| on | expire while held | PASS | 4.057m held travel, 1.958s fuse advance |

All four host reports passed. Every participant recorded one blast, one visual, one receipt per player and inventory counts4/5. The two client failures remain failed acceptance results: the required live movement/fuse observations were incomplete.

The separate dependent branch `fix/td16-probe-held-observation` corrects this probe ordering. After entry/presentation readiness and a bounded one-second quiet period before any grenade exists, the client confirms its live generation1 observation before capture. The host holds the actual grenade at its captured position until the client confirms seeing generation2, then moves it with the existing player telekinesis command. Release waits for the client's own live held travel>1m and fuse advance>0.4s. Only phase integers are exchanged; each endpoint records and validates its own canonical state. Both participants must now observe original generation1. The original60s deadline, real fuse, time scale, gameplay handlers, protocol100 and all original thresholds/counts remain unchanged.

## Fixed native acceptance

The fixed probe at `7616938e76bf92ef01574427aaba2c6940d82da8` built successfully from clean source and passed all four genuine Linux Release pairs on 2026-11-07. Managed assembly SHA256: `94ed34eeb1487e94ad04b3afd574d71b2e639c701794c28060a65d075d9ffe3a`.

| Host self-ticks | Scenario | Result | Client held travel | Client held fuse advance |
| --- | --- | --- | --- | --- |
| off | release | PASS | 4.016m | 0.625s |
| off | expire while held | PASS | 4.090m | 2.183s |
| on | release | PASS | 3.974m | 0.650s |
| on | expire while held | PASS | 3.995m | 2.358s |

All eight processes reported success and exited0. Strict post-evaluation confirmed observed host modes, eight distinct save directories, both participants' original generation1 and held generation2, every live acknowledgement, detached flight objects, stale replay/pose checks, release/blast generations3 or expiration generation2, all original movement/fuse/count assertions and inventory4/5. No exception markers were found in their logs. Initial and final results are recorded separately in [TD16-NATIVE-RESULTS.json](TD16-NATIVE-RESULTS.json).

The fixed run used real graphical GLCore/Mesa llvmpipe windows at640×360. The corrected runner copy changed exactly the default1280×720 dimensions; SHA256 `afb1905860241db59e09f4527dee634a7c09f490068293ccfa9bb33de2816ce8`. This probe has no pixel-size/screenshot assertion. Rendering workload was reduced while actual world/VFX simulation and all state assertions remained active. Windows pairs, physical-GPU appearance and audible behavior were not measured by these Linux checks.

To reproduce the exact software-GL runner from its ancestor source:

```sh
git show fb01263e855332828434a2e38b36ed1536c550b9:RemainsUnity/Tests/HostWorld/verify.py > /tmp/td16-verify.py
python3 - <<'PY'
from pathlib import Path
p=Path('/tmp/td16-verify.py')
p.write_text(p.read_text().replace('else (1280,720)','else (640,360)'))
PY
python3 /tmp/td16-verify.py --player /path/to/Remains --output /path/to/new-results --modes off on --scenarios grenade-telekinesis grenade-telekinesis-expire --windowed --timeout 150 --port 21700
```

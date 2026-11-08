# TD14 / existing TD06 contract acceptance

Run Unity 6000.0.39f1 against the project:

```
Unity -batchmode -nographics -quit -job-worker-count 1 \
  -projectPath RemainsUnity \
  -executeMethod Remains.Editor.ContentSaveAcceptanceChecks.RunBatch \
  -logFile content-save-acceptance.log
```

The report persists at `RemainsUnity/Library/FactoryVerification/content-save-acceptance.json`. The runner catches and records each individual failure, then exits nonzero if any check failed. Every disk fixture uses a newly created, uniquely named `remains-content-save-acceptance-GUID` directory. It deletes only that invocation's directory after all checks pass; otherwise it retains the path in the report for investigation. No existing save directory is used.

Native LinuxEditor, Unity 6000.0.39f1, `-nographics`, one job worker, base runtime/assets `d110aed242faf3b4f6ef0e7ab3bd46f07fec93c4`: **6 PASS, 0 failures, exit 0** (2026-11-07). The fixture directory was absent after shutdown, confirming successful cleanup.

| Existing check | Observed coverage |
|---|---|
| `RoomContentPublicationChecks` | 63 assertions: detached preparation of doors, physical objects/containers/loot, NPCs and mechanisms; exact publication; preservation of previous objects, lists, transient state and mod fields; late failure without partial publication; duplicate/refused plans and revision overflow |
| `EntryPreparationChecks` | Detached preview, one-use token/TTL, latest body after offline damage, independent readiness, room/door fingerprints, bounded chunks, unknown fields and cleanup lifecycle |
| `EntryGeometryChecks` | Unpublished staged geometry, cache reuse, admission of the same prepared object, rejection of premature/foreign welcome, cancellation and unchanged active hub when another factory appears |
| `RecoveryDurabilityChecks` | Controlled blocked world/profile writes, suppressed ACK on profile failure, FIFO/shutdown behavior and immutable earlier archive after a newer selection |
| `OrderedSaveWriteChecks` | Nonblocking pump under controlled blocked write, ordered writes, ACK after completion, failure poisoning without ACK, flush without old-session callbacks |
| `LocalRecoveryPairChecks` | Controlled interruptions before/after profile publication, restart/read-hook replay once, immutable payload/aliases/mod fields, newer host head preservation and refusal of foreign/corrupt/externally modified data without overwriting it |

These are existing production-contract fixtures, not a new save implementation. They do **not** establish real power-loss filesystem behavior, distributed atomicity across peers, complete live graphics/Welcome ordering, or host migration. No protocol, save format, importer setting or gameplay behavior was changed.

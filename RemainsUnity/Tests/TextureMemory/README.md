# TD-32 texture inventory

Run the native Editor (6000.0.39f1):

```
Unity -batchmode -nographics -quit -projectPath RemainsUnity \
  -executeMethod Remains.Editor.TextureMemoryInventoryChecks.RunBatch \
  -logFile texture-memory.log
```

The JSON report is `RemainsUnity/Library/FactoryVerification/td32-texture-memory.json`. It survives Editor shutdown. Exit 0 requires at least one texture and no skipped asset paths. Nothing in the importer configuration is modified.

The inventory finds public `Assets` texture paths, enumerates native texture subassets, deduplicates GUID/local-file-ID, and records imported dimensions, depth/layers, mip count, source dimensions, current graphics/texture format, actual compression status, read/write state, default importer compression and Standalone override. It sums base/mip RGBA32-equivalent bytes and `Profiler.GetRuntimeMemorySizeLong` for the loaded objects. Image Sprite subassets do not duplicate their shared texture.

## Measurement on 2026-11-07

Base runtime/assets: `d110aed242faf3b4f6ef0e7ab3bd46f07fec93c4`, LinuxEditor, Unity 6000.0.39f1, `-nographics` / **Null Device**. This is a load-all public-asset Editor inventory, not the game's active scene or Player working set.

| Metric | Value |
|---|---:|
| Unique textures / paths | 4796 / 4796 |
| Skipped paths | 0 |
| RGBA32-equivalent base level | 6,918,051,232 B (6.443 GiB) |
| RGBA32-equivalent including mip levels | 7,129,452,336 B (6.640 GiB) |
| Profiler memory of all loaded texture objects | 2,671,835,902 B (2.488 GiB) |
| Actual uncompressed formats: textures | 1980 |
| Profiler memory of actual uncompressed texture objects | 1,235,409,046 B (1.151 GiB) |

A useful subgroup: 908 textures with the default importer label `Uncompressed` and actual `RGBA32` occupy 510,225,040 RGBA bytes and 510,977,538 Profiler bytes (about 0.51 GB). This supports that **subgroup's** rough historical 0.5 GB estimate, not a whole-project memory claim. Another 2,801 textures have default importer `Uncompressed` but an explicit Standalone BC7 override, so the default label alone would misclassify them. Conversely, automatic compressed imports can resolve to RGBA32; the report records both settings and actual format.

RGBA32 equivalents deliberately ignore block compression, source PNG compression and per-object overhead. Profiler object sums are not process RSS or measured VRAM; Null Device has no hardware rendering context. Packages, builtin textures and runtime-created textures are excluded. Windows/D3D11, build-time stripping, streaming and actual scene residency must be measured separately. No compression/quality optimization was attempted from this inventory.

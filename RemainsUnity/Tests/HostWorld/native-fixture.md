# Native seed104 fixture

`Seed104CpuBaselineProbe` supports `--baseline-create-fixture <new-directory>` in a Windows or Linux Player.
It still requires batchmode, role `host`, smoke `seed104-baseline`, a report, an empty save directory and a separate
empty fixture directory. It rejects Editor, nested directories and occupied inputs. Gameplay and manifest validation
are unchanged.

Run the generator as a separate, unmeasured process. Its `world.json` and `character.json` are immutable inputs with
the actual Player content manifest. After the creation log confirms both files were saved, stop only that generator.
For each measured run, copy those exact two files into a fresh save directory and write `baseline-input.sha256` as
SHA-256 of the UTF-8 text `world.json + "\n" + character.json`. `SaveStore.SelectedSnapshot` and `LoadCharacter` already
support these flat files; no import or content-approval bypass is needed. Use the same character ID as the generator.

This produces a new Native fixture. Its hash is not claimed to match an Editor fixture or an earlier baseline.

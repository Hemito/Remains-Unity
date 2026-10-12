# Remains: Unity

[Русский](README.md) · **English**

[![Contributing](https://img.shields.io/badge/Contributing-5865F2?style=for-the-badge)](CONTRIBUTING.md)
[![Status](https://img.shields.io/badge/Status-Experimental-orange?style=for-the-badge)](PROJECT_STATUS.md)
[![Issues](https://img.shields.io/badge/Issues-Report_a_bug-238636?style=for-the-badge)](https://github.com/Hemito/Remains-Unity/issues/new/choose)
[![Pull Requests](https://img.shields.io/badge/Pull_Requests-Suggest_changes-8957E5?style=for-the-badge)](https://github.com/Hemito/Remains-Unity/pulls)

An experimental, independent fan remake of **Remains** by **Empalu**, built in
Unity with online cooperative multiplayer. This is not an official product of
the original team. The original graphics, sounds, and content are not credited
to Hemito.

The project is unfinished: it is a working experiment, rather than a complete
remake of the original or a stable release. Multiplayer works through Mirror/KCP;
selected cooperative scenarios have been tested with two processes on one PC.
LAN play on two physical PCs, four-player sessions, and larger sessions are not
yet claimed to be verified.

## Source code and building

You need Windows and **Unity 6000.0.39f1** with Windows Build support.
Open the **RemainsUnity** directory. Mirror **96.11.3** is included in Assets
with its licenses; Unity Packages are pinned in manifest.json and packages-lock.json.
Keep the `.meta` files and dependency versions.

Git LFS is required to download the source project:

```powershell
git lfs install
git clone https://github.com/Hemito/Remains-Unity.git
cd Remains-Unity
git lfs pull
```

Unity must receive the complete game asset, rather than a text LFS pointer.
After importing the project, build for Windows x86-64 with the single starting
scene `Assets/Remains/Scenes/Remains.unity` enabled in Build Settings.

To build the existing prepared assets in the background, use the existing method:

```powershell
& '<path to Unity.exe>' -batchmode -quit -projectPath './RemainsUnity' -executeMethod Remains.Editor.ActorProjectedShadowChecks.BuildVerifiedPlayer -logFile './build.log'
```

The output is `RemainsUnity/Builds/Windows/Remains.exe` together with the full
build directory. The built game does not require Git LFS. This distribution
lets you build the existing game; some Editor re-export commands and extended
checks require source materials that are not distributed with the game project.
Do not run those commands for a normal build.

For a local connection, the client uses `127.0.0.1`; for LAN, use the host PC's
IP address. The transport is KCP, with the usual UDP port 7777. Players need
compatible builds.

## Contributing

The community can submit Issues and Pull Requests:
[CONTRIBUTING.md](CONTRIBUTING.md). See [PROJECT_STATUS.md](PROJECT_STATUS.md)
for the current state of the game. These supporting documents are in Russian.

## Licensing

Original project code is covered by **PolyForm Noncommercial 1.0.0**, with
additional permissions for voluntary donations, monetized videos/streams,
and free mirrors. Scope and standard exceptions: [LICENSING.md](LICENSING.md).
Additional permissions: [ADDITIONAL_PERMISSIONS.md](ADDITIONAL_PERMISSIONS.md).
This is a source-available model with restrictions, not an OSI open-source license.

The code license does not cover original game assets or other third-party
resources. Their own terms and rights remain in effect:
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

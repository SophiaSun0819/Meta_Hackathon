# Thinking Space

Thinking Space is a mixed-reality brainstorming application for **Meta Quest 3**, built in Unity with Meta XR. A user speaks naturally to Orby, an AI companion; the app records and transcribes speech, routes conversational intent through a shared backend, generates or discusses ideas, speaks responses, and represents saved ideas as persistent spatial bubbles.

> Development status: active prototype. The microphone, transcription, conversational backend, generated voice, local sessions, and placeholder bubbles have all worked in physical Quest tests. Several interaction and recovery paths are still being tested. This is not a production release.

## Supported project configuration

| Component | Current configuration |
| --- | --- |
| Unity | **6000.3.27f1** (use this exact editor) |
| Target | Meta Quest 3, Android |
| Main scene | `Assets/Scenes/Sandbox.unity` |
| Android application ID | `com.meta.hackathon.thinkingspace` |
| Android architecture/backend | ARM64, IL2CPP |
| Android API levels | Minimum 32, target 34 |
| Render pipeline | URP 17.3.0 |
| Meta XR All-in-One SDK | 207.0.1 |
| OpenXR | 1.19.0 with Meta XR features |
| Input System | 1.20.1 |

Do not upgrade Unity, Meta XR, OpenXR, or other packages as part of ordinary feature work. Coordinate package changes with the whole team.

## What is implemented

- Quest 3 mixed-reality passthrough and the existing Meta XR camera/hand-tracking rig.
- Hand-tracked palm menu with Meta poke and ray/pinch interaction.
- Thumbs-up/down recording controls, selected recording hand, and microphone/VAD-based conversational capture.
- Manual recording review, playback, WAV diagnostics, automatic transcription, and recoverable error paths.
- Hosted FastAPI backend for OpenAI transcription, conversational intent routing, clarification, and idea processing.
- Conversational Orby responses for general questions, idea creation, expansion, combination, and constructive challenges.
- ElevenLabs speech synthesis through the backend; provider credentials remain server-side.
- Dynamic subtitles and a centralized Orby voice queue/cache.
- Versioned local session persistence, restored session numbering, conversation records, idea records, relationships, and bubble transforms.
- Placeholder category-colored idea bubbles, saved/restored per session, with selectable paginated detail panels.
- Local onboarding/preferences and development-only diagnostics/reset tooling.

Implemented does not mean every edge case has completed physical validation. In particular, see **Current milestone status** below.

## Planned or unfinished

- Voice-controlled menus and complete session-command execution.
- Final bubble hand gestures and voice-controlled bubble movement/resizing.
- Visual relationship connectors between related ideas.
- A tutorial practice session.
- Replacement of placeholder Orby/bubble visuals with final authored assets.
- UI, accessibility, thermal, memory, network-recovery, and Quest performance polish.
- Production authentication. The current shared development token is suitable only for controlled testing.
- Multi-instance durable clarification state; the backend currently keeps bounded clarification state in process memory.

## Repository setup

### Prerequisites

Install:

1. [Git](https://git-scm.com/) and [Git LFS](https://git-lfs.com/).
2. Unity Hub and **Unity 6000.3.27f1**.
3. Unity Android Build Support for that editor, including **OpenJDK** and **Android SDK & NDK Tools**.
4. Meta Quest Developer Hub or Android platform tools if you want a convenient `adb` installation.

Initialize LFS before cloning:

```bash
git lfs install
```

### Clone and create a branch

```bash
git clone https://github.com/SophiaSun0819/Meta_Hackathon.git
cd Meta_Hackathon
git lfs pull
git switch development
git pull --ff-only
git switch -c feature/short-description
```

If your work is based on a different agreed integration branch, substitute that branch. Do not build a new feature directly on another teammate's active branch.

### Open the project

Add the repository root in Unity Hub and open it with **6000.3.27f1**. The first import may take several minutes. Open `Assets/Scenes/Sandbox.unity`; it is the only enabled build scene.

Do not click broad repair or “Fix All” actions without reviewing each proposed change. Some Meta/OpenXR settings are intentional, and setup tools can serialize project-wide changes.

## Build and install a Development APK

1. In Unity, open **File → Build Profiles** and select Android.
2. Switch platform only if Android is not already active.
3. Confirm `Assets/Scenes/Sandbox.unity` is enabled.
4. Enable **Development Build** for diagnostics; do not enable it for a release candidate.
5. Connect the Quest and select it under **Run Device**, then use **Build And Run**; or choose **Build** and install with ADB.

Command-line installation:

```bash
adb devices
adb install -r path/to/ThinkingSpace.apk
```

Use `-r` to update while retaining app data. A clean reinstall removes the package first and therefore removes sessions, preferences, cache, and the device development-token file:

```bash
adb uninstall com.meta.hackathon.thinkingspace
adb install path/to/ThinkingSpace.apk
```

Do not uninstall when you need to preserve device sessions.

## Quest developer mode and ADB

1. Add the Meta account to an organization and enable Developer Mode in the Meta Horizon mobile app.
2. Connect the headset by USB, put it on, and accept the data/USB debugging prompt. Select “Always allow” only on a trusted development computer.
3. Verify exactly one authorized device with `adb devices`.

### Windows

Install Android platform tools through Meta Quest Developer Hub, Android Studio, or Google's platform-tools archive. Add the directory containing `adb.exe` to `PATH`, then open a new PowerShell window.

### macOS

Install through Homebrew or Google's platform tools:

```bash
brew install android-platform-tools
adb version
```

If a device is `unauthorized`, reconnect it and accept the prompt inside the headset. If multiple devices are connected, disconnect the others or use `adb -s SERIAL`; the supplied provisioning scripts intentionally stop rather than guessing.

## Shared backend and development authentication

The team uses one existing Render-hosted FastAPI service. OpenAI and ElevenLabs credentials are stored only in Render. Teammates do **not** need their own OpenAI or ElevenLabs subscriptions for normal Quest testing.

Protected endpoints require `X-ThinkingSpace-Token`. Development APKs load the shared development token from:

```text
Application.persistentDataPath/ThinkingSpace/dev-token.txt
```

For this Android package, Unity's persistent-data directory is normally:

```text
/storage/emulated/0/Android/data/com.meta.hackathon.thinkingspace/files
```

Therefore the token file is:

```text
/storage/emulated/0/Android/data/com.meta.hackathon.thinkingspace/files/ThinkingSpace/dev-token.txt
```

Obtain the token privately from the project owner. Never paste it into a Unity asset, prefab, scene, source file, screenshot, issue, pull request, shell script, or Git commit. The token is a temporary shared development credential embedded in neither the APK nor this repository. Anyone who receives it can call the protected shared backend until it is rotated, so it is not a production authentication design.

Safe helper scripts are provided:

- Windows: `Tools/Development/Provision-QuestDevToken.ps1`
- macOS/Linux: `Tools/Development/provision-quest-dev-token.sh`

They prompt without echoing, use a permission-restricted temporary file, validate one connected/authorized Quest and the installed package, push only `dev-token.txt`, verify that it is nonempty, and remove the temporary file. They never contain or print a real token.

```powershell
.\Tools\Development\Provision-QuestDevToken.ps1
```

```bash
chmod +x Tools/Development/provision-quest-dev-token.sh
./Tools/Development/provision-quest-dev-token.sh
```

After provisioning, restart the app. Uninstalling the package or clearing app data removes device-local state; reprovision if diagnostics report that the token is missing. The current development reset code is intended to preserve unrelated credentials, but always verify the reported token source after destructive testing.

Editor development can instead read `THINKING_SPACE_DEV_TOKEN` from the editor process environment. Do not store it in project settings.

## Current milestone status

| Milestone | Status |
| --- | --- |
| 5C.2A — Unity conversational integration | Implemented and physically validated for transcription and multiple conversational/idea paths. |
| 5C.2B — Automatic conversational processing | Implemented; general questions and idea creation have worked on Quest. Recovery and longer multi-idea conversations remain under test. |
| 5C.2B.1 — Error classification and diagnostics | Implemented locally; captures safe request stage, HTTP category, WAV/request ownership, and recovery information. |
| 5C.2B.2 — Clarification recovery and enhanced diagnostics | Backend and Unity corrections implemented locally; Python 3.12 backend suite passed 96 tests and Unity assemblies compiled. **Latest backend recovery changes are not deployed and require further Quest testing.** |

The 5C.2B.2 change clears a failed session-owned clarification so one malformed provider response cannot leave Unity and the backend with divergent clarification state. Development builds also read safe structured-failure metadata. Do not represent this path as verified until the updated backend and APK complete physical testing.

## Team Git workflow

1. Start from the agreed integration branch and create your own `feature/*` or `fix/*` branch.
2. Coordinate ownership before editing another developer's active feature.
3. Keep commits focused and stage explicit paths—never use `git add .` in a dirty shared worktree.
4. Push your branch and open a pull request into the agreed integration branch.
5. Review the file list and diff before merging.

Example:

```bash
git status --short
git add path/to/intended-file path/to/its-file.meta
git diff --cached --check
git diff --cached --name-status
git commit -m "Document teammate Quest setup"
git push -u origin feature/team-handoff
```

Additional rules:

- Preserve every Unity `.meta` file. Add or move the asset and its `.meta` together.
- Do not commit `Library`, `Temp`, `Logs`, `UserSettings`, IDE files, APK/AAB output, recordings, `.env`, token files, API keys, or local caches.
- Use Git LFS for binary models, images, audio, video, fonts, and other types listed in `.gitattributes`. Run `git lfs pull` after cloning.
- Coordinate edits to `Assets/Scenes/Sandbox.unity`. Only one person should own a scene integration change at a time; prefer isolated prefabs/scripts where possible.
- Do not discard unrelated working-tree changes. If a file contains multiple developers' work, stage it interactively and review carefully.

## Project structure

| Path | Purpose |
| --- | --- |
| `Assets/Scenes/Sandbox.unity` | Main Quest MR scene and integration point |
| `Assets/ThinkingSpace/Speech/` | Recording, transcription, conversation workflow, bubbles, and review UI |
| `Assets/ThinkingSpace/UI/` | Palm menu, session store, preferences, and menu views |
| `Assets/ThinkingSpace/Companion/` | Orby state, voice, subtitles, onboarding, diagnostics, and placeholder view |
| `Assets/ThinkingSpace/Tests/Editor/` | Unity EditMode regression coverage |
| `Backend/` | FastAPI transcription, conversation, intent, idea, and voice services plus mocked tests |
| `Tools/Development/` | Credential-free development provisioning helpers |
| `render.yaml` | Existing Render service configuration; contains no secret values |

## Persistence and privacy

Session JSON, preferences, voice cache, optional exported development WAV files, and the development token live below the Quest app's persistent-data directory. Raw audio is not stored in sessions by default. Do not retrieve or share recordings without the speaker's consent.

Application data may be removed by uninstalling the APK or clearing package data. Development reset tools create backups for the data they manage, but are not a cloud backup system.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Missing models/audio after clone | Run `git lfs install` and `git lfs pull`; verify files are not LFS pointer text. |
| Quest absent from Unity/ADB | Developer Mode, cable, in-headset authorization, then `adb devices`. |
| “Development authentication token is not configured” | Provision `dev-token.txt`, restart the app, and check development diagnostics for token source without exposing its value. |
| HTTP 401 from protected backend endpoint | Token is missing, stale, or mismatched with Render; obtain the current value privately. |
| Conversation structured-response failure | Record only safe diagnostics: HTTP status, failure stage/field/category, completion reason, request ID, and session ID. Do not share transcripts or tokens. |
| Package Manager network errors | Retry on a stable network; do not change package versions as a workaround. |

Backend setup, endpoint contracts, deployment variables, and mocked test commands are documented separately in `Backend/README.md`.

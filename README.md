# Thinking Space

A mixed reality app for Meta Quest, built with Unity and the Meta XR SDK. The base scene shows the real room through passthrough and tracks your hands.

## Tech stack

| Item | Version |
| --- | --- |
| Unity | 6000.3.27f1 |
| Render pipeline | URP 17.3.0 |
| Meta XR All-in-One SDK | 207.0.1 |
| OpenXR plugin | 1.19.0 (Meta XR feature group enabled) |
| Input System | 1.20.1 |
| Newtonsoft Json | 3.2.1 |
| Android package name | `com.meta.hackathon.thinkingspace` |

## Environment setup

Do these steps once per computer, in order.

### 1. Install Git LFS

Images, models, audio, video, fonts and APKs are stored with [Git LFS](https://git-lfs.com). If LFS isn't set up before you clone, those files come down as small text pointers and the project won't look right.

```bash
brew install git-lfs
git lfs install
```

(`conda install git-lfs` works too, but it's only on your PATH when conda is active, so Unity and Git GUI apps may not find it.)

### 2. Install Unity

1. Install **Unity 6000.3.27f1** from Unity Hub. Use this exact version so project files don't get upgraded by accident.
2. Add the **Android Build Support** module, including **OpenJDK** and **Android SDK & NDK Tools**.

### 3. Clone and open the project

```bash
git clone https://github.com/SophiaSun0819/Meta_Hackathon.git
cd Meta_Hackathon
git checkout development
```

Open the folder in Unity Hub. The first import takes a while because Unity downloads the Meta SDK packages. If Package Manager shows `ECONNRESET` errors, your network dropped the download: retry, or switch to another network or a phone hotspot.

When the project is open:

- Check that **File → Build Profiles** shows **Android** as the active platform. If it doesn't, select Android and click **Switch Platform**.
- Open **Edit → Project Settings → Meta XR**. The Android tab should show no Required issues. If it does, click **Fix All**.

### 4. Set up your Quest

1. Turn on Developer Mode for the headset in the Meta Horizon mobile app.
2. Connect the Quest over USB, put it on, and allow USB debugging.
3. In **File → Build Profiles**, choose your Quest under **Run Device**.

## Building to Quest

1. Open `Assets/Scenes/Sandbox.unity`. It's first in the build scene list.
2. In **File → Build Profiles**, click **Build And Run**.
3. In the headset you should see your real room and your own hands. The hands look blue, and turn pale yellow when your palm faces you; that's the system menu gesture.

Don't commit build output. `*.apk` files and the `*_BurstDebugInformation_DoNotShip/` folder are ignored.

## Project structure

| Path | Contents |
| --- | --- |
| `Assets/Scenes/Sandbox.unity` | Base MR scene: Camera Rig, Hand Tracking, Passthrough |
| `Assets/XR/` | XR Plug-in Management and OpenXR settings |
| `Assets/Oculus/` | Meta project config (hand tracking, passthrough, scene support) |
| `Assets/Resources/` | Meta SDK runtime settings |
| `Assets/Plugins/Android/` | Generated `AndroidManifest.xml` |

## Branches and merge rules

| Branch | Purpose | Rules |
| --- | --- | --- |
| `main` | Stable versions for demos and submission | Updated only by merging `development` through a PR |
| `development` | Integration branch for the team | **Protected:** no direct pushes (admins included), no force pushes, can't be deleted |
| `feature/*`, `fix/*` | Your own work | Branch from `development` and open a PR back into it |

### Workflow

```bash
git checkout development
git pull
git checkout -b feature/short-description
# ...make changes, commit...
git push -u origin feature/short-description
```

Then open a pull request into `development` on GitHub. Merging doesn't need a review, but tell the team in chat before you merge.

Guidelines:

- Keep each PR small and about one thing.
- Pull `development` often, and merge it into your branch before opening a PR.
- **Avoid editing the same scene or prefab as someone else at the same time.** Unity scene merges are painful. Agree on who owns a scene, or put your work in a prefab or a scene of your own.
- Write commit messages in the imperative mood, for example `Add pinch-to-grab interaction`.

## Git LFS tracked files

These types are stored in LFS (see `.gitattributes`):

- Models: `.fbx` `.obj` `.blend` `.glb`
- Images: `.png` `.jpg` `.jpeg` `.psd` `.tga` `.exr` `.hdr`
- Audio and video: `.wav` `.mp3` `.ogg` `.mp4`
- Fonts and builds: `.ttf` `.otf` `.apk`

Unity text assets (`.unity`, `.prefab`, `.asset`, `.meta`, `.mat`, `.anim`, `.controller`) stay in regular Git with LF line endings.

To add another large binary type, run `git lfs track "*.ext"` and commit the updated `.gitattributes`.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| Images or models are missing or look broken after cloning | LFS wasn't set up. Run `git lfs install`, then `git lfs pull`. |
| Package Manager shows `ECONNRESET` | The network dropped the download. Retry, or switch to another network or a phone hotspot. |
| The Quest doesn't show up under Run Device | Check that Developer Mode is on, reconnect the cable, and accept the USB debugging prompt in the headset. |
| A push to `development` is rejected with `protected branch` | Expected. Push a feature branch and open a PR. |
| Meta XR Project Setup Tool shows warnings | Click **Fix All**. The remaining Recommended items about the Platform SDK and the XR Simulator can be ignored. |

## Optional tools

The project includes [MCP for Unity](https://github.com/CoplayDev/unity-mcp) (`com.coplaydev.unity-mcp`), which lets AI coding assistants control the Unity Editor. You don't have to use it.

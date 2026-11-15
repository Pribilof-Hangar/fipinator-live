# Windows PC setup and testing

How to set up the Windows 11 PC to build and test FIPinator 3000, and what to check.
Write your results in [PROGRESS.md](../PROGRESS.md) (or paste them to Claude).

Working with Claude on the Windows PC? Start with the prompt in
[CLAUDE-WINDOWS-PROMPT.md](CLAUDE-WINDOWS-PROMPT.md).

## What you need

| Thing | Why | Already have it? |
|---|---|---|
| Windows 11, 64-bit | Where FIPinator runs | Yes |
| .NET 10 SDK (x64) | Builds and runs FIPinator | Install in step 1 |
| Logitech FIP driver | Lets FIPinator draw on the FIPs | Yes, if SPAD.neXt drives your FIPs |
| MSFS 2024 (the PMDG DC-6 only for the DC-6 panel) | Live data | Yes |
| MSFS 2024 SDK | Provides SimConnect.dll (for now; being replaced) | Yes, if you installed it |

Nothing in this guide changes your SPAD.neXt setup.

## 1. Install the .NET 10 SDK

Pick one:

- **Download:** go to https://dotnet.microsoft.com/download, choose **.NET 10**, then
  **SDK**, **Windows x64**. Run the installer.
- **Or, in a terminal:**
  ```powershell
  winget install Microsoft.DotNet.SDK.10
  ```

Then **close and reopen** any terminal windows, and check:

```powershell
dotnet --list-sdks
```

You should see a line starting with `10.0`.

## 2. Get the FIPinator files

1. Get the source from GitHub. Either click **Code**, then **Download ZIP**, and extract it,
   or use `git clone`.
2. Put it in a simple path such as `C:\fipinator-3000`.
   Avoid OneDrive-synced folders, which can lock build files.
3. Make sure the folder you end up in contains `Fipinator.slnx` and `README.md`. If the
   files are one level deeper (`C:\fipinator-3000\fipinator-3000\...`), use the inner folder.
4. Double-click `tools\install-for-me.cmd`. It builds and installs FIPinator Live and
   FIPinator 3000 for your Windows user. It needs the .NET 10 SDK (step 1). It writes a log
   to `publish\install-log.txt`.

## 3. Open a terminal in the folder

In File Explorer, open the folder, right-click an empty spot, and choose **Open in Terminal**.
The prompt should end with your folder path, for example `PS C:\fipinator-3000>`.

## 4. Test 1: tests and gauge preview (no FIP or sim needed)

```powershell
dotnet test
```

Expected: `failed: 0` (the number that succeed grows as tests are added). The first run downloads packages and can take
a minute or two.

```powershell
dotnet run --project src/Fipinator.Preview -- gauges/dc6/airspeed.json --set ias=190 --zoom 3 --out airspeed.png
start airspeed.png
```

Expected: an airspeed gauge pointing at 190 that looks like `docs\images\dc6-airspeed.png`.

Try other speeds to watch the needle move, for example `--set ias=60` or `--set ias=280`.

**Report back:**
- Did `dotnet test` pass?
- Does `airspeed.png` match `docs\images\dc6-airspeed.png`?
- Any error messages, copied in full.

## Troubleshooting

| Problem | Try |
|---|---|
| `dotnet` is not recognized | Close and reopen the terminal. If still missing, reinstall the SDK (step 1). |
| `The SDK 'Microsoft.NET.Sdk' specified could not be found` | The SDK isn't installed, only the runtime. Install the **SDK**. |
| `DllNotFoundException: libSkiaSharp` | Install the latest Microsoft Visual C++ Redistributable (x64), then try again. |
| Errors mentioning `global.json` | Run `dotnet --list-sdks` and report the versions shown. |
| `Access to the path ... is denied` | Move the folder out of OneDrive or Program Files, e.g. to `C:\fipinator-3000`. |
| Anything else | Copy the whole error text and paste it to Claude. |

## Building the installers (release)

`tools\build-release.ps1` runs the tests, publishes both apps, copies SimConnect.dll from the MSFS 2024 SDK
next to each program (never committed to git), and builds the two installers into `publish\installers` with
Inno Setup 6 (`winget install JRSoftware.InnoSetup`):

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 0.9.0
```

`tools\make-spad-repo.ps1` makes the separate "FIPinator gauges for SPAD.neXt" download in `publish\spad-gauges`.

## Early setup history (no longer needed)

These were one-time checks to gather information while FIPinator was first built. They're
kept here for reference. You don't need to do them.

### Check 2: find the Logitech DirectOutput files

FIPinator will talk to the FIPs through Logitech's DirectOutput, installed with the FIP
driver. To write that part correctly, Claude needs to know where it is and which version
of the SDK documents you have.

```powershell
Get-ChildItem "C:\Program Files\Logitech", "C:\Program Files\Saitek", "C:\Program Files (x86)\Logitech", "C:\Program Files (x86)\Saitek" -Recurse -Include DirectOutput.dll, DirectOutput.h, *.pdf -ErrorAction SilentlyContinue | Select-Object FullName, Length, LastWriteTime
```

**Report back:** the list it prints (paths only, no need to send the files).

If it prints nothing, also run:

```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Logitech\DirectOutput", "HKLM:\SOFTWARE\Saitek\DirectOutput" -ErrorAction SilentlyContinue
```

### Check 3: list the DC-6 L:vars

1. Start MSFS 2024 and load the PMDG DC-6, cold and dark or ready to fly.
2. List the aircraft's L:vars with a tool you have. With MobiFlight, use its WASM module's
   "List Lvars" option.
3. Save the list as a text file named `dc6-lvars.txt`, and copy it into the project's
   `docs` folder (or send it to Claude).

Variable names are fine to share; they're names, not PMDG's artwork or code.

### Check 4: reference screenshots (private)

Take screenshots in MSFS 2024 of the DC-6 captain's panel: one wide shot, plus close-ups
of airspeed, attitude, altimeter, heading, vertical speed, turn and slip, and the RMI.

Keep them in a private folder **outside** the project. They're PMDG's artwork: fine as a
reference for drawing original gauges, but never put them in the project or on GitHub.

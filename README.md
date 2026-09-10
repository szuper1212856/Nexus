# NEXUS — Personal Command Center

A Windows desktop mission-control console for task management. C# / .NET 8 / WPF, MVVM, no third-party NuGet packages.

---

## 1. Installing dependencies

There is exactly one dependency: the **.NET 8 SDK**.

1. Download it from <https://dotnet.microsoft.com/download/dotnet/8.0> (pick **SDK x64**, Windows).
2. Run the installer.
3. Open a new PowerShell or Command Prompt window and verify:

```
dotnet --version
```

You should see `8.x.x`. No NuGet restore is required — the project references only the base WPF framework — but running `dotnet restore` once is harmless.

Windows 10 (1809+) or Windows 11 is required. The icon font used for glyphs (`Segoe Fluent Icons` / `Segoe MDL2 Assets`) ships with both.

---

## 2. Running the application

From the folder containing `NEXUS.sln`:

```
dotnet run --project NEXUS\NEXUS.csproj
```

Or open `NEXUS.sln` in Visual Studio 2022 and press **F5**.

On first launch the app provisions ten placeholder mission tasks so the console is never empty — edit or delete them and add your real ten.

**Data location:** `%APPDATA%\NEXUS\`
- `data.json` — tasks, activity log, focus sessions
- `settings.json` — theme, accent, deadline, preferences
- `error.log` — written only if something goes wrong

Saves are debounced and written atomically, plus a final save on exit. Closing and reopening never loses anything.

---

## 3. Building the Windows executable

Framework-dependent single file (small, needs .NET 8 Desktop Runtime on the target machine):

```
dotnet publish NEXUS\NEXUS.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Self-contained single file (~150 MB, runs on any Windows x64 machine with no runtime installed):

```
dotnet publish NEXUS\NEXUS.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output lands in:

```
NEXUS\bin\Release\net8.0-windows\win-x64\publish\NEXUS.exe
```

Copy that `publish` folder anywhere you like — for example `C:\Program Files\NEXUS\` or `%LOCALAPPDATA%\Programs\NEXUS\`.

---

## 4. Creating a desktop shortcut

**Manually:** right-click `NEXUS.exe` → *Show more options* → *Send to* → *Desktop (create shortcut)*.

**By script** — run this in PowerShell, editing the first line to match where you copied the exe:

```powershell
$target   = "C:\Program Files\NEXUS\NEXUS.exe"
$shortcut = "$([Environment]::GetFolderPath('Desktop'))\NEXUS.lnk"

$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($shortcut)
$lnk.TargetPath       = $target
$lnk.WorkingDirectory = Split-Path $target
$lnk.IconLocation     = $target
$lnk.Description      = "NEXUS - Personal Command Center"
$lnk.Save()
```

To also pin it to the Start menu, drop a copy of the `.lnk` into:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\
```

Then right-click it in the Start menu and choose *Pin to Start*.

Start-with-Windows is built in — turn on **Settings → Behaviour → Start with Windows** and the app writes its own `HKCU\...\CurrentVersion\Run` entry.

---

## 5. Packaging as a proper Windows application

Three options, easiest first.

### A. Portable folder
Zip the `publish` folder and hand it around. The self-contained build needs nothing installed.

### B. Installer with Inno Setup (recommended)
Install [Inno Setup](https://jrsoftware.org/isdl.php), save this as `nexus.iss` next to the solution, and compile it:

```ini
[Setup]
AppName=NEXUS
AppVersion=1.0.0
AppPublisher=NEXUS
DefaultDirName={autopf}\NEXUS
DefaultGroupName=NEXUS
UninstallDisplayIcon={app}\NEXUS.exe
OutputBaseFilename=NEXUS-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest

[Files]
Source: "NEXUS\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\NEXUS"; Filename: "{app}\NEXUS.exe"
Name: "{autodesktop}\NEXUS"; Filename: "{app}\NEXUS.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\NEXUS.exe"; Description: "Launch NEXUS"; Flags: nowait postinstall skipifsilent
```

That produces `NEXUS-Setup.exe` with a proper uninstall entry in *Apps & features*.

### C. MSIX
In Visual Studio: add a **Windows Application Packaging Project** to the solution, reference the NEXUS project, edit `Package.appxmanifest`, then *Publish → Create App Packages*. Needs a code-signing certificate (a self-signed one works if you install it into *Trusted People* first). Only worth it if you want Store distribution or clean per-user install/uninstall semantics.

---

## Using it

| Shortcut | Action |
|---|---|
| `Ctrl+N` | New task |
| `Ctrl+F` | Start focus timer |
| `Ctrl+1…7` | Jump to Overview / Tasks / Quick Actions / Focus / Analytics / Activity / Settings |
| `Esc` | Close the open dialog |

**Deadline.** The countdown is computed from the real system clock against the deadline in Settings, which defaults to the upcoming Wednesday 23:59. It turns amber inside 36 hours, red inside 12, and switches to an explicit "DEADLINE PASSED" state with a count-up once it elapses. Leave *auto-roll* on and a stale deadline moves to the next Wednesday on launch.

**Focus.** Pick a target task, choose 15 / 25 / 45 / 60 minutes, and start. Pause, complete (which also closes the task), or exit. When the timer runs out you get a sound, a toast, and the window pulls itself forward.

**Backups.** Settings → Data → Export writes a single JSON bundle containing tasks, activity, focus sessions and settings. Import replaces everything from such a bundle.

---

## Project layout

```
NEXUS/
├── Common/       ObservableObject, RelayCommand
├── Models/       MissionTask, SubTask, ActivityEntry, FocusSession, AppSettings, AppData
├── Services/     DataStore, TaskService, ActivityService, FocusService,
│                 ToastService, ThemeService, SystemService, DialogService, AppServices
├── ViewModels/   Shell + one per section, TaskCommands, TaskEditorViewModel
├── Views/        ShellWindow, seven section views, two dialogs
├── Controls/     RingGauge (custom-drawn gauge), Tracking, CompletionFlash
├── Converters/   All IValueConverters
└── Resources/    Theme.xaml, Converters.xaml, Styles.xaml, TaskCard.xaml, nexus.ico
```

UI, application logic, and storage are kept apart: views hold no logic beyond window chrome, view-models never touch the filesystem, and every task mutation goes through `TaskService` so the activity log, persistence and statistics can never drift out of sync.

Theming works by mutating the `Color` of unfrozen brush resources, so switching theme or accent re-renders the whole app instantly without reloading dictionaries.

---

## One caveat

This project was written on Linux, where the Windows Desktop SDK is not available, so **it has not been compiled or run**. The XAML is all verified well-formed and every `StaticResource` key is verified to resolve, but a first `dotnet build` may still surface a small mistake or two. If anything does come up, send me the compiler output and I'll fix it.

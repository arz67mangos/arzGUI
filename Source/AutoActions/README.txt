arzGUI
======

Run arzGUI.exe. It lives in the tray; closing the window only hides it.

arzGUI is a modified version of Codectory/AutoActions. It was built to automate
the different display, colour, audio, and companion-app settings used by its
maintainer and friends for different games. See NOTICE.md, LICENSE, and
THIRD_PARTY_NOTICES.md for source, attribution, and licence details.


Your settings
-------------

Profiles, applications, hotkeys and presets are stored in

    %AppData%\arzGUI\UserSettings.json

which no update touches: unzip a new version anywhere, run it, everything is
still there.

Settings from ArzFlow (%AppData%\ArzFlow) are taken over automatically the first
time this version runs.

Coming from a version older than 1.9.30, or moving to another PC: close
arzGUI, copy UserSettings.json next to arzGUI.exe, start it. The file is
imported into %AppData%\arzGUI and renamed UserSettings.imported.json, so it
happens once. Importing overwrites what is already in %AppData%\arzGUI.


Starting with Windows as administrator
--------------------------------------

The monitor device action (enable/disable a monitor in Device Manager) needs
administrator rights for the whole program. arzGUI's own Auto-Start setting
goes through the Run key, which never runs elevated, so use a logon task
instead. Open a Command Prompt as administrator, cd into this folder, then:

    schtasks /create /tn arzGUI /tr "\"%CD%\arzGUI.exe\"" /sc onlogon /rl highest /f

Type that as its own command once the prompt is already in this folder. %CD%
is expanded when the line is read, so chaining it after a cd on the same line
(cd ... && schtasks ...) records the wrong folder. If in doubt, write the path
out in full instead:

    schtasks /create /tn arzGUI /tr "\"C:\path\to\arzGUI.exe\"" /sc onlogon /rl highest /f

Turn arzGUI's own Auto-Start off in Settings afterwards, or two copies try to
start at logon.

To see the recorded path:   schtasks /query /tn arzGUI /fo list /v
To remove it:               schtasks /delete /tn arzGUI /f

The task stores the folder it was created from, so create it again after
moving arzGUI somewhere else.

Nothing else needs administrator rights, and programs started by a run action
are launched as you, never elevated, even when arzGUI is - unless you tick
"Run as administrator" on that action, which is there for the programs that
want the rights (OBS Studio, for encoding without dropped frames).


OBS Studio
----------

The OBS Studio action switches the OBS profile, scene collection and scene when
an application starts, closes or is focused. It needs the WebSocket server that
ships inside OBS 28 and later:

    OBS: Tools > WebSocket Server Settings > Enable WebSocket server,
         then Show Connect Info and copy the password
    arzGUI: Settings > OBS Studio, paste it, press Test connection

Leave the port at 4455 unless you changed it in OBS. The password is stored
encrypted for your Windows account, so if you carry UserSettings.json to
another PC, enter it again there.

It does not matter whether OBS or arzGUI runs as administrator; they talk over a
local socket, which works in both directions.

In the action, a field left empty is left alone. If the same profile also starts
OBS, put the run action first and leave "Wait for OBS" at 15 seconds so the OBS
action waits for it to finish loading.


Something went wrong
--------------------

arzGUI.log next to this file records what the app did; a crash is
appended to arzGUI.crash.log. Both are next to the exe, not in %AppData%.

Five checks ship with this build. Run them from this folder; each one waits
for Enter at the end.

    RunProgramCheck.exe      run-program actions, and that they are not
                             elevated - run it as administrator, that is the
                             case that matters
    MonitorDeviceCheck.exe   the monitor enable/disable action; run it as
                             administrator to include the live state change
    GammaProbe.exe           why gamma, vibrance or brightness will not change
                             on a display
    AudioCheck.exe           the playback and recording device list, which one is
                             default, and that switching it works
    ObsCheck.exe             the connection to OBS and the OBS action. Needs OBS
                             running and the password entered in Settings. It
                             switches scene, profile and scene collection and puts
                             all three back the way it found them


arzGUI is a modified version of AutoActions by Codectory.
https://github.com/arz67mangos/arzGUI

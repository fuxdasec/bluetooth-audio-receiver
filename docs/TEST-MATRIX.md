# Hardware test matrix

Use the same computer, Bluetooth adapter, and physical positions when comparing builds. Record the complete Windows version (`winver`), adapter model and driver, phone model and OS, headset model, and firmware versions.

## A2DP gate

1. Pair an Android phone or iPhone in Windows Settings.
2. Set the USB-dongle headset as the default Windows output.
3. Open the application and select the phone.
4. Play music, video, and audio from at least three phone applications.
5. Join a Discord voice channel on the PC and confirm that both streams are mixed into the headset.
6. Pass the gate only if audio remains stable for 30 minutes while the application window is hidden.

## Recovery and range

- Keep the window hidden for eight hours.
- Lock and unlock the Windows session.
- Suspend for five minutes and resume.
- Turn Bluetooth off for one minute and turn it back on.
- Move the phone out of range for five minutes and return.
- Restart Windows and the phone separately.
- Play audio for three minutes at fixed distances of 1, 3, 5, and 8 meters.

Diagnostics should show retries after 1, 2, 5, 10, 20, and at most 30 seconds without creating duplicate sessions.

## Silent-session recovery

These cases cover sessions that Windows reports as connected while no audio is routed.

- Toggle the phone's Bluetooth off and on ten consecutive times. Every cycle must end in audible audio
  without pressing **Reconnect**.
- Time each reconnection after turning the phone's Bluetooth back on. Expect roughly one to three
  seconds; diagnostics should show `Immediate A2DP attempt` rather than a backoff delay.
- Confirm that diagnostics never end at `Connected` while the phone is silent. The recovery lines to
  look for are `The session closed while it was being opened` and
  `Health watchdog: the session is no longer open while connected`.

## Single instance

- With **Start with Windows** enabled, restart Windows and then run the executable again. Exactly one
  tray icon must exist and the second launch must raise the running window.
- Confirm from Task Manager that only one `BluetoothAudioReceiver` process is running.

## Tray

- Confirm a notification when the source connects.
- Move the phone out of range and confirm exactly one notification for the outage, not one per retry.
- Return the phone to range and confirm a single reconnection notification.
- Hover the tray icon and confirm the tooltip follows the connection state.
- Open the tray menu and confirm the source name in the header and that **Start with Windows** stays in
  sync with the checkbox in the window, in both directions.
- Click the tray icon once with the left button and confirm the window opens.

## Update notifications

The repository is public but has no releases yet, so case 1 is the behaviour to expect today.

- With no release published, run the application and confirm no banner and no notification. The
  diagnostics must contain `Update check: no stable release published yet.` and nothing more alarming.
- Publish a `vX.Y.Z` tag whose version is above the running build, wait for the check or use
  **Check for updates** in the tray menu, and confirm both the notification and the banner. The
  **Download** button must open the releases page in the default browser.
- Click **Dismiss**, then restart the application. The banner must come back, the notification must
  not.
- Clear **Notify me about new versions**, restart, and confirm that no check is made at all.
- Disconnect the network and use **Check for updates**. It must report the failure and leave the
  Bluetooth connection untouched.
- Confirm that no check ever interrupts audio, before, during, or after playback.

## Interface

The control templates are written by hand, so these cases target the places where a custom template
fails silently rather than visibly.

- Open the source dropdown, select each device in turn, and confirm the name appears in the closed box
  and that connecting actually happens. A broken template still opens the list but stops committing
  the selection.
- Switch Windows between light and dark with the window open. Every surface must repaint at once, with
  no element left on the previous palette.
- Start the application under each theme and confirm both are correct from a cold start.
- With no source selected, confirm **Connect** is disabled and its tooltip explains why, and that no
  dialog appears. **Reconnect** must be disabled until a source has been chosen at least once.
- Force a failure, for example enabling **Start with Windows** with the executable on a read only
  path, and confirm the message appears in the bar under the header rather than in a dialog.
- Expand and collapse **Diagnostics**. The log must fill the remaining height when open, the chevron
  must rotate, and **Copy diagnostics** must still work.
- Resize down to the 600x480 minimum and confirm nothing clips or overlaps.
- Check the status dot in all four tones: connected, reconnecting, connecting, and disabled.

## Source, output, and language

- Switch between two paired phones while a connection is opening; only the latest selection may remain active.
- Change the default Windows output and click **Refresh**; the displayed output must follow the Windows setting.
- Repeat the A2DP gate with at least one Android phone and one iPhone before declaring both platforms validated.
- Set the Windows display language to English, `pt-BR`, and `pt-PT`; restart the application and verify the corresponding interface resources.
- Use an unsupported UI language and verify that the interface falls back to English.

## Distribution and startup

- Confirm that the release directory contains only `BluetoothAudioReceiver.exe` and `SHA256SUMS.txt`.
- Verify the executable against `SHA256SUMS.txt` before testing.
- Run the executable on a clean x64 system without a separately installed .NET runtime, on both
  Windows 10 and Windows 11. The build declares `10.0.19041.0` as its minimum, so Windows 10 version
  2004 is the oldest release that is expected to work.
- Confirm that the version shown in the interface matches the stable or continuous build metadata.
- Enable **Start with Windows** and verify that the `BluetoothAudioReceiver` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` contains the quoted current executable path followed by `--background`.
- Restart Windows and confirm that the application starts hidden in the system tray.
- Disable **Start with Windows** and confirm that the registry value is removed.
- Move the executable, launch it from the new location, and confirm that a stale startup entry is reported as disabled until enabled again.

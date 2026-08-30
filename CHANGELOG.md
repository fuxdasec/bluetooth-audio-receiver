# Changelog

All notable changes to this project will be documented in this file.

## [1.0.0] - 2026-08-30

First public release.

- Receive A2DP audio from Android and iOS phones through the default Windows output.
- Remember the selected source and run in the system tray.
- Optionally start with Windows through the current user's startup registry entry.
- Automatically reconnect with bounded backoff and stale-request protection.
- Reconnect immediately when a paired Bluetooth endpoint reports itself connected, instead of waiting
  for the next backoff tick.
- Reject a session that Windows closes while it is being opened, and re-check open sessions every ten
  seconds, so the receiver never reports a connection that carries no audio.
- Run a single copy at a time. Two processes would compete for the same `AudioPlaybackConnection`;
  launching the executable again reopens the running receiver's window instead.
- Show tray notifications when the source connects and when it is lost, announced once per outage
  rather than once per retry, and keep the tray tooltip in sync with the connection state.
- Offer the selected source name, a **Start with Windows** toggle, and reconnection in the tray menu,
  and open the window with a single left click on the tray icon.
- Check GitHub every six hours for a newer stable release and report it through a tray notification
  and a banner in the window, with a button that opens the releases page. Nothing is downloaded or
  replaced automatically, the address opened is a constant rather than a value from the response, and
  the check can be turned off.
- Present the window as cards with the connection status, the source and the output in one place, and
  keep the diagnostics log collapsed so it no longer occupies most of the window.
- Follow the Windows app theme, light or dark, and switch live when it changes.
- Show a status colour for the connection, disable actions whose preconditions are not met, and report
  errors in place instead of in modal dialogs.
- Provide English and Portuguese user interfaces selected from the Windows display language.
- Publish a self-contained, single-file Windows x64 executable.
- Validate pull requests and publish continuous and stable releases through GitHub Actions.

Known limitation: PC microphone forwarding and HFP/HSP calls are not supported.

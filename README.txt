ECGridOS Safe Workbench
=======================

What changed in this revision
-----------------------------
- Settings now save under LocalAppData instead of beside the executable.
- SessionID/API key is no longer persisted unless you check "Remember SessionID locally".
- You can supply the SessionID/API key through the ECGRID_SESSION_ID environment variable.
- Confirm Download now requires the same parcel to be previewed/downloaded first.
- File load and send paths preserve original bytes when the payload has not been edited.
- Edited payloads are validated for non-ASCII characters before send to catch copy/paste issues.
- Binary/non-text downloads now fall back to Base64 preview instead of mojibake.
- Removed the stale Fresh Start launcher from the sanitized source bundle.

- Added the ECGrid logo graphic (ecgridapp.png) to the app header.
- Added a warning dialog before any inbox file is previewed/downloaded.
- Added a safety block for cross-mailbox downloads (enabled by default).
- Inbox checks still use the current mailbox returned by WhoAmI.
- Added a confirmation dialog before explicit-send target uploads.
- Masked the SessionID/API key in the WhoAmI response log.
- Added friendlier error text when ECGrid returns AccessDenied.
- Project file uses explicit Compile items so old stray .cs files in the folder do not break the build.
- Added a Restore to Inbox feature backed by ParcelDownloadReset, with an auto-filled Restore ParcelID box after Confirm Download.

How to run
----------
1. Extract this zip into a NEW empty folder.
2. Open a terminal in the extracted folder.
3. Run:
   dotnet run --project .\ECGridOsSafeWorkbench.csproj

Quick safe receive test
-----------------------
1. Enter Service URL and SessionID/API key.
2. Click WhoAmI.
3. Click Check Inbox.
4. Select an inbox row.
5. Review metadata in the Last Response tab.
6. Click Preview / Download Selected.
7. Read the warning dialog carefully before continuing.
8. Save Downloaded only if you want the file on disk.
9. Confirm Download only after you intentionally finished with the parcel.
10. Use Restore to Inbox if you want ECGrid to place that same ParcelID back into the inbox for another receive-side test.

Notes
-----
- MailboxListEx may be denied for mailbox-level sessions.
- Explicit send target does NOT switch your session. It only changes the upload target for that one send.


Icon support
------------
This build includes ecgridapp.ico so the app shows your ECGrid image in the Windows title bar and taskbar.

SessionID / API key persistence
--------------------------------
- By default the app does NOT save the SessionID/API key to disk.
- Check "Remember SessionID locally" only on a machine you trust.
- You can also set ECGRID_SESSION_ID in your environment and leave the checkbox off.

Packaging notes
---------------
- This sanitized source bundle intentionally excludes bin/ and obj/ artifacts and does not include a saved settings file.
- Use run-safe-workbench.bat or dotnet run against ECGridOsSafeWorkbench.csproj.

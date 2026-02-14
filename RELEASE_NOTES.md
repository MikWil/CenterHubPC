# CenterHub Release Notes

## Version 3.1.4 (Latest)

### 🎉 New Features

- **Single Instance Enforcement**: Only one instance of CenterHub can run at a time. If you try to launch a second instance, you'll be notified that the application is already running.

### 🐛 Bug Fixes & Improvements

- **Improved Window Sizing for Smaller Screens**:
  - Reduced minimum window size from 1200x700 to 800x500 pixels
  - Window automatically resizes to fit smaller screens
  - Window positioning is now validated to ensure it stays within screen bounds
  - Window automatically adjusts on startup if it's too large for the display

- **Enhanced Window Dragging**:
  - Window can now be dragged from multiple areas:
    - Title bar (as before)
    - Navigation sidebar
    - Content area (empty space)
  - This makes it easier to move the window even if the title bar is off-screen

- **Fixed Compilation Error**: Resolved duplicate `OnExit` method definition

### 🔧 Technical Changes

- Added mutex-based single instance detection
- Improved window visibility and positioning logic
- Enhanced window drag functionality across multiple UI areas

---

## Version 3.1.3

### ✨ New Features

- **Comprehensive Confirmation Notifications**: Added toast notifications for all utility features and actions throughout the application

#### Sound Section
- ✅ Success notification when selecting audio device
- ✅ Success notification when toggling microphone mute/unmute
- ✅ Success notifications for applying and saving sound profiles

#### Standing Timer
- ✅ Success notifications when starting/stopping timers
- ⚠️ Warning notifications for invalid input values

#### Shutdown Timer
- ✅ Success notification when scheduling shutdown (with date/time)
- ✅ Success notification when cancelling shutdown
- ⚠️ Warning notifications for validation errors (missing date/time, invalid format, past time)
- ❌ Error notifications for failures

#### File Manager
- ℹ️ Info notifications when selecting source/target folders
- ✅ Success notification with file count after move/copy operations
- ⚠️ Warning/error notifications for validation issues

#### Auto Clicker
- ✅ Success notifications when starting/stopping
- ✅ Success notifications when capturing/setting position
- ℹ️ Info notification when getting current position
- ⚠️ Warning notification for invalid interval

#### Clipboard
- ✅ Success notifications for all clipboard actions:
  - Copy to clipboard (with content preview)
  - Toggle pin/unpin
  - Delete item
  - Clear history
  - Toggle monitoring

#### Notes
- ✅ Success notifications when:
  - Creating new notes
  - Saving notes (with note title)
  - Deleting notes
  - Exporting notes (with filename)
  - Importing notes (with filename)
- ❌ Error notifications for export/import failures

### 🐛 Bug Fixes

- Fixed missing `using System;` directive in NotesView.xaml.cs

### 🔧 Technical Changes

- Integrated ToastService throughout all ViewModels
- Added comprehensive error handling with user-friendly notifications
- Improved user feedback for all interactive features

### 📦 Build Improvements

- Updated build script to include version number in ZIP filename (e.g., `CenterHub-v3.1.3.zip`)
- Added code signing documentation and support (see `CODE_SIGNING.md`)

---

## Previous Versions

For earlier release notes, please refer to the git history or project documentation.

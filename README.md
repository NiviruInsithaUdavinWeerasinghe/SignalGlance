# SignalGlance 📶

SignalGlance is a beautiful, lightweight, and real-time Wi-Fi and network utility designed specifically for Windows. It runs unobtrusively in the system tray and alerts you immediately when your connection changes, while offering detailed local data tracking.

---

## Key Features

*   **Unobtrusive System Tray Integration**: Minimizes directly to the taskbar tray, using a dynamic signal icon that changes color based on your connectivity status (🟢 Good, 🟡 High Latency / Unstable, 🔴 Offline).
*   **Real-time Speedometer & Latency**: Keep an eye on your connection stability with smoothed latency (ping) and active upload/download speeds.
*   **WPF Desktop Notifications**: Crisp, opaque slide-in toast notifications inform you of adapter transitions. Fully integrated with Windows Auto-Hide taskbar states so notifications never overlap or collide.
*   **Wi-Fi History & Data Usage Tracker**: Log daily and monthly network usage per Wi-Fi profile (SSID). Active SSID profile is pinned to the top of the history board.
*   **Smooth Custom Speed Test**: Built-in speed testing with smooth gauge needle animation and elegant cross-fade transitions into itemized results.
*   **100% Local and Private**: Zero data is collected or transmitted. All SSID records, ping reports, and usage history logs remain securely on your device.

---

## Tech Stack & Architecture

*   **Framework**: .NET 9 (WPF - Windows Presentation Foundation)
*   **Styling**: Curated Dark Theme with glassmorphism effects, harmonized custom scrollbars, and modern typography.
*   **Win32 APIs**: Interops with user32.dll for screen DPI transformations, Alt+Tab window hiding, and real-time taskbar auto-hide tracking.

---

## Getting Started

### Prerequisites

*   Windows 10 / 11
*   .NET 9 SDK (to compile from source)

### Installation from Source

1.  **Clone the Repository**:
    ```bash
    git clone https://github.com/YOUR_USERNAME/SignalGlance.git
    cd SignalGlance
    ```

2.  **Build the Project**:
    ```bash
    dotnet build
    ```

3.  **Run the App**:
    Execute the installer or run the monitor app:
    ```bash
    Start-Process "SignalGlance/bin/Debug/net9.0-windows/SignalGlance.exe" -ArgumentList "--app"
    ```

---

## Distribution

The application includes a standalone **Setup Wizard** built with WPF.
To share the package, compile the solution and zip all executables and dependency files from `SignalGlance\bin` and `SignalGlance.Installer\bin` into a single archive directory.

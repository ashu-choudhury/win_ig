# WinInstagram - 100% Accessible Windows Native Client for Instagram

WinInstagram is a high-performance, keyboard-first native Windows desktop client designed specifically for **screen reader users (NVDA, JAWS, Narrator)** and accessibility. It solves the severe accessibility bugs and persistent audio muting of Instagram Web by separating Instagram's browser session engine from its presentation layer.

---

## Why WinInstagram?

1. **Deterministic Audio (Zero Random Mutes):** Built on native Windows media playback. Audio is **unmuted by default** and will never randomly mute when switching reels or scrolling.
2. **Keyboard-First & Zero Focus Reset:** Arrow keys navigate cleanly between reels or feed posts. No React virtual-DOM destruction, no focus traps, and no accidental resets back to the top of the feed.
3. **Screen Reader Integration:** 100% native Windows UI Automation (UIA) support for NVDA, JAWS, and Narrator. Every action, author name, caption, duration, audio track, and like status is exposed directly through standard UIA properties and live regions so your screen reader reads them cleanly in your preferred voice and speed.
4. **Legitimate Chromium Engine Under the Hood:** Runs an embedded Edge Chromium (WebView2) instance in the background to handle authentication, 2FA, and official session cookies. Meta's anti-bot system sees a genuine Windows Edge browser, keeping your account safe from checkpoint bans.

---

## Application Structure & Flow

* **First Launch / Login Screen:** On first launch, the application opens directly to a clean, accessible login screen. You log in using your standard username and password (with full support for 2FA, SMS codes, or security checkpoints).
* **Automatic Transition:** As soon as your session is verified, the app automatically transitions you to your **Home Feed** and **Reels Player**, enabling all navigation tabs.
* **1. Home Feed (`Ctrl + 1`):** Timeline feed of posts with author details, post timestamp, image/video previews, captions, and like actions.
* **2. Reels Player (`Ctrl + 2`):** Vertical video player with looping playback, unmuted audio, caption viewer, like counter, and expandable comments panel.
* **3. Direct Messages (`Ctrl + 3`):** Two-pane interface for viewing conversation threads and sending replies.
* **4. Account & Web Engine (`Ctrl + L`):** Background session manager. Your session is permanently saved in `%AppData%\WinInstagram\WebView2Profile` so future launches go directly to your feed.

---

## Keyboard Shortcuts

### Global Navigation
* **`Ctrl + 1`**: Switch to **Reels**
* **`Ctrl + 2`**: Switch to **Home Feed**
* **`Ctrl + 3`**: Switch to **Direct Messages**
* **`Ctrl + L`** or **`Ctrl + 4`**: Switch to **Web Engine & Login**

### Reels Player Shortcuts
* **`Down Arrow`** or **`J`**: Next Reel (automatically announces author, audio track, caption, and like count)
* **`Up Arrow`** or **`K`**: Previous Reel
* **`Spacebar`**: Toggle Play / Pause
* **`M`**: Toggle Mute / Unmute (announces new state audibly)
* **`L`**: Toggle Like / Unlike
* **`C`**: Open / Close Comments drawer
* **`+` / `-`**: Increase / Decrease Volume by 10%
* **`Escape`**: Close Comments drawer and return focus to player

---

## First-Time Setup & Login

1. Launch the application:
   ```powershell
   dotnet run
   ```
   Or run the compiled executable from `bin\Debug\net10.0-windows\WinInstagram.exe`.
2. On initial startup, press **`Ctrl + L`** to switch to the **Web Engine & Login** tab.
3. Log into your Instagram account using your standard credentials, 2FA authenticator, or SMS code.
4. Once logged in, the status badge in the top-right corner will update to **`🟢 Logged In`**.
5. Press **`Ctrl + 1`** to return to the **Reels** tab. Your reels will now automatically stream unmuted with full keyboard controls!
6. Your login session is permanently saved in `%AppData%\WinInstagram\WebView2Profile`, so you will not need to log in again on future launches.

# WinInstagram 📸

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D6.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Accessibility](https://img.shields.io/badge/Accessibility-Screen%20Reader%20Optimized-success.svg)](README.md)
[![Latest Release](https://img.shields.io/github/v/release/ashu-choudhury/win_ig?label=Download%20Latest%20Release&color=success)](https://github.com/ashu-choudhury/win_ig/releases/latest)

> 🚀 **Direct Download**: Get the latest standalone Windows version here: **[Download WinInstagram-windows-x64.zip](https://github.com/ashu-choudhury/win_ig/releases/latest)** *(No .NET installation required, just extract and run!)*

**WinInstagram** is a high-performance, keyboard-first, native Windows desktop client for Instagram built with **WPF** and **.NET 10**. It is engineered from the ground up for **screen reader users (NVDA, JAWS, Narrator)** and power keyboard navigators.

WinInstagram eliminates the severe accessibility bottlenecks, keyboard focus traps, DOM resets, and random audio muting that plague Instagram on standard desktop web browsers.

---

## ✨ Key Features

- **🔊 Deterministic Unmuted Audio:** Video and reels audio is unmuted and maintained by default. No random muting or volume drops when advancing between reels.
- **⚡ Buttery Smooth 60fps Playback:** Leverages hardware-accelerated Microsoft Edge WebView2 rendering in full-screen mode with native smooth scrolling and zero CPU stalling.
- **🛡️ Anti-Trap Screen Reader Isolation:** Screen reader <kbd>Tab</kbd> navigation is kept entirely within accessible WPF controls—preventing screen readers from getting lost or trapped inside Instagram's thousands of DOM nodes.
- **💬 Full Accessible Comments Experience:**
  - Dedicated accessible drawer overlay that does not disrupt video playback.
  - Item-by-item navigation of comments using <kbd>Up</kbd> and <kbd>Down</kbd> arrows.
  - Like or unlike individual comments directly with <kbd>Enter</kbd> or <kbd>Space</kbd>.
  - Integrated comment composer with <kbd>Enter</kbd> to post.
  - Press <kbd>Escape</kbd> anytime to return immediately to the reel.
- **❤️ Accurate Likes & Author Extraction:** Live extraction of creator handles (`@username`), captions, audio tracks, and real-time like counts (e.g. `14.2K likes`) announced directly via Windows UI Automation (UIA) live regions.
- **🔐 Legitimate Session Security:** Uses an isolated official WebView2 profile (`%AppData%\WinInstagram\WebView2Profile`). Instagram sees an authentic Edge browser session on Windows, ensuring complete compatibility with 2FA, SMS codes, and security checkpoints without risk of bot bans.

---

## ⌨️ Keyboard Shortcuts Reference

### Global Navigation
| Shortcut | Action |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> | Switch to **Home Feed** |
| <kbd>Ctrl</kbd> + <kbd>2</kbd> | Switch to **Reels Player** |
| <kbd>Ctrl</kbd> + <kbd>3</kbd> | Switch to **Direct Messages** |
| <kbd>Ctrl</kbd> + <kbd>L</kbd> / <kbd>Ctrl</kbd> + <kbd>4</kbd> | Switch to **Account / Login & Web Engine** |

### Reels Player Controls
| Shortcut | Action | Description |
| :--- | :--- | :--- |
| <kbd>PageDown</kbd> / <kbd>Down</kbd> / <kbd>J</kbd> | **Next Reel** | Advances to next reel and speaks creator, caption, and likes |
| <kbd>PageUp</kbd> / <kbd>Up</kbd> / <kbd>K</kbd> | **Previous Reel** | Scrolls to previous reel |
| <kbd>Space</kbd> | **Play / Pause** | Toggles video playback |
| <kbd>M</kbd> | **Mute / Unmute** | Toggles audio mute state |
| <kbd>L</kbd> | **Like / Unlike** | Likes or unlikes the current reel |
| <kbd>C</kbd> | **Comments** | Opens or closes the accessible Comments drawer |

> **Note:** When typing inside any comment input box, single-key shortcuts (<kbd>Space</kbd>, <kbd>M</kbd>, <kbd>L</kbd>, <kbd>J</kbd>, <kbd>K</kbd>, <kbd>C</kbd>) are automatically suppressed so you can type freely.

### Accessible Comments Drawer
| Shortcut | Action | Description |
| :--- | :--- | :--- |
| <kbd>Up</kbd> / <kbd>Down</kbd> | **Browse Comments** | Focus and read comments one by one |
| <kbd>Enter</kbd> / <kbd>Space</kbd> | **Toggle Comment Like** | Likes or unlikes the currently selected comment |
| <kbd>Enter</kbd> *(in text box)* | **Submit Comment** | Posts the written comment to the reel |
| <kbd>Escape</kbd> | **Close Drawer** | Closes comments and returns focus to the reel player |

---

## 🚀 Getting Started

### Prerequisites
- **Operating System:** Windows 10 (version 1809 or higher) or Windows 11
- **Runtime:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- **WebView2:** [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11 and modern Windows 10)

### Installation & Build

1. **Clone the repository:**
   ```bash
   git clone https://github.com/ashu-choudhury/win_ig.git
   cd win_ig
   ```

2. **Restore dependencies and build:**
   ```powershell
   dotnet build
   ```

3. **Run the application:**
   ```powershell
   dotnet run
   ```

Alternatively, run the compiled binary directly from:
```
bin\Debug\net10.0-windows\WinInstagram.exe
```

---

## 🔒 Session & Authentication

On your very first launch:
1. WinInstagram will present the **Account / Login** view (`Ctrl + L`).
2. Log in using your Instagram username and password (supports Two-Factor Authentication, SMS verification, and Instagram app prompts).
3. Once logged in, the session badge turns **`🟢 Logged In`**, and WinInstagram automatically transitions to your feed.
4. Your authenticated session is permanently stored in:
   ```
   %AppData%\Roaming\WinInstagram\WebView2Profile
   ```
   Future launches resume automatically without requiring you to log in again.

---

## 🏗️ Architecture

```
WinInstagram/
├── Models/
│   ├── FeedPost.cs          # Timeline post model
│   ├── ReelItem.cs          # Reel & InstagramComment models with UIA notifications
│   └── DirectMessage.cs     # Direct message models
├── Services/
│   ├── AccessibilityHelper.cs   # Windows UIA live region announcement bridge
│   ├── AppLogger.cs             # High-speed formatted console logger
│   ├── InstagramBridgeService.cs# Bidirectional WebView2 interop, script dispatch & scraping
│   ├── InstagramParser.cs       # JSON/GraphQL response parsing
│   └── MediaCacheService.cs     # Media streaming cache
├── Views/
│   ├── LoginView.xaml           # Isolated Edge WebView2 container
│   ├── HomeView.xaml            # Home feed presentation view
│   ├── ReelsView.xaml           # Reels presentation view
│   └── MessagesView.xaml        # Direct messages view
├── MainWindow.xaml              # Accessible main shell & Comments Drawer
└── WinInstagram.csproj          # .NET 10 Windows Desktop project
```

---

## 🤝 Contributing

Contributions, bug reports, and suggestions are welcome!
Feel free to open an issue or submit a pull request:

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

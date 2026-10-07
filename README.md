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

- **🗂 Native Accessible Panels:** Your feed, reels and direct messages are presented as **real Windows list controls**, not scraped web pages. Each item exposes a complete accessible name, so screen readers read the creator, caption, like count and timestamp as you arrow through the list. Press <kbd>F6</kbd> to collapse the panel for a full-width web view.
- **📥 Real Direct Messages:** Conversations and their messages come from Instagram's own `direct_v2` payloads, with unread counts, group threads and correct "You said" / "<sender> said" attribution. Reply without ever touching the web view.
- **✅ Truthful State:** Likes are performed through Instagram's authenticated endpoint and the result is confirmed before anything is announced. If Instagram rejects the change, WinInstagram says so instead of silently pretending it worked.
- **🔊 Deterministic Unmuted Audio:** Video and reels audio is unmuted and maintained by default. No random muting or volume drops when advancing between reels.
- **⚡ Buttery Smooth 60fps Playback:** Leverages hardware-accelerated Microsoft Edge WebView2 rendering with native smooth scrolling and zero CPU stalling. The engine keeps running behind the native panels.
- **🛡️ Anti-Trap Screen Reader Isolation:** Screen reader <kbd>Tab</kbd> navigation is kept entirely within accessible WPF controls—preventing screen readers from getting lost or trapped inside Instagram's thousands of DOM nodes.
- **💬 Full Accessible Comments Experience:**
  - Dedicated accessible drawer overlay that does not disrupt video playback.
  - Item-by-item navigation of comments using <kbd>Up</kbd> and <kbd>Down</kbd> arrows.
  - Like or unlike individual comments directly with <kbd>Enter</kbd> or <kbd>Space</kbd>.
  - Integrated comment composer with <kbd>Enter</kbd> to post.
  - Press <kbd>Escape</kbd> anytime to return immediately to the reel.
- **↺ Reel Watch History:** The reels panel keeps a navigable history of reels you have already watched, so you can re-read or re-open one without scrolling the feed again.
- **❤️ Accurate Likes & Author Extraction:** Live extraction of creator handles (`@username`), captions, audio tracks, and real-time like counts (e.g. `14.2K likes`) announced directly via Windows UI Automation (UIA) live regions.
- **🔐 Legitimate Session Security:** Uses an isolated official WebView2 profile (`%AppData%\WinInstagram\WebView2Profile`). Instagram sees an authentic Edge browser session on Windows, ensuring complete compatibility with 2FA, SMS codes, and security checkpoints without risk of bot bans.

---

## ♿ Screen Reader Design

WinInstagram is deliberate about *when* it speaks, because saying the wrong thing is worse than saying nothing:

- **Focus-driven reading uses UI Automation.** Every list item carries its full description as its accessible name, so arrowing through posts, conversations or reels is spoken by your screen reader natively. WinInstagram does not fire a competing announcement for the same thing.
- **Live regions are reserved for events you cannot otherwise hear** — a feed refresh, incoming messages, an action's real outcome, or an error.
- **A hidden panel never talks.** Only the visible view is "active"; background updates to a panel you are not looking at are silent.

---

## ⌨️ Keyboard Shortcuts Reference

### Global Navigation
| Shortcut | Action |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> | Switch to **Home Feed** (native panel) |
| <kbd>Ctrl</kbd> + <kbd>2</kbd> | Switch to **Reels Player** |
| <kbd>Ctrl</kbd> + <kbd>3</kbd> | Switch to **Search / Open Link** |
| <kbd>Ctrl</kbd> + <kbd>4</kbd> | Switch to **Direct Messages** (native panel) |
| <kbd>Ctrl</kbd> + <kbd>5</kbd> | Switch to **Settings** |
| <kbd>Ctrl</kbd> + <kbd>L</kbd> | Switch to **Account / Login & Web Engine** |
| <kbd>Ctrl</kbd> + <kbd>U</kbd> | Check for updates |
| <kbd>F6</kbd> | Show or hide the native accessible panel |

### Native Panels
| Shortcut | Action | Description |
| :--- | :--- | :--- |
| <kbd>Up</kbd> / <kbd>Down</kbd> | **Read items** | Moves through posts, conversations, messages or watched reels |
| <kbd>Enter</kbd> | **Open** | Opens the selected post or reel in the web view, or a conversation thread |
| <kbd>L</kbd> *(Home)* | **Like / Unlike** | Confirmed against Instagram before it is announced |
| <kbd>R</kbd> *(Home / Messages)* | **Refresh** | Reloads the timeline or the inbox |
| <kbd>Enter</kbd> *(Messages reply box)* | **Send** | Sends the typed direct message |
| <kbd>+</kbd> / <kbd>-</kbd> | **Volume** | Adjusts engine volume |

> **Note:** Arrow keys belong to whichever list has focus, so reading a list never accidentally scrolls the reels feed.

### Reels Player Controls
| Shortcut | Action | Description |
| :--- | :--- | :--- |
| <kbd>PageDown</kbd> / <kbd>Down</kbd> / <kbd>J</kbd> | **Next Reel** | Advances to next reel and speaks creator, caption, and likes |
| <kbd>PageUp</kbd> / <kbd>Up</kbd> / <kbd>K</kbd> | **Previous Reel** | Scrolls to previous reel |
| <kbd>Space</kbd> | **Play / Pause** | Toggles video playback |
| <kbd>M</kbd> | **Mute / Unmute** | Toggles audio mute state |
| <kbd>L</kbd> | **Like / Unlike** | Likes or unlikes the current reel |
| <kbd>S</kbd> | **Share** | Copies the reel's link to the clipboard |
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

The window is split in two: the **WebView2 engine on the left** and a **native accessible panel on the right**. The engine is never torn down, because it owns the authenticated session, media playback, and the network responses that feed the native panels.

```
WinInstagram/
├── Models/
│   ├── FeedPost.cs          # Timeline post model with permalink, pk and accessible description
│   ├── ReelItem.cs          # Reel & InstagramComment models with UIA notifications
│   └── DirectMessage.cs     # DM conversation (group/unread aware) & message models
├── Services/
│   ├── AccessibilityHelper.cs   # Windows UIA live region announcement bridge
│   ├── AppLogger.cs             # High-speed formatted logger
│   ├── InstagramBridgeService.cs# WebView2 interop, injected page script, network parsing
│   ├── InstagramParser.cs       # Timeline / comments / direct_v2 JSON parsing
│   ├── MediaCacheService.cs     # Media streaming cache
│   └── UpdateService.cs         # GitHub Releases based self-updater
├── Views/
│   ├── LoginView.xaml           # Isolated Edge WebView2 engine (login, home, reels, DMs)
│   ├── HomeView.xaml            # Native accessible timeline list
│   ├── ReelsView.xaml           # Native reel details, actions and watch history
│   └── MessagesView.xaml        # Native accessible inbox and thread reader
├── ViewModels/
│   ├── HomeViewModel.cs         # Feed list, truthful like state
│   ├── ReelsViewModel.cs        # Live reel state and watch history
│   └── MessagesViewModel.cs     # Conversations, messages, sending replies
├── MainWindow.xaml              # Accessible shell, native panel host & Comments Drawer
└── WinInstagram.csproj          # .NET 10 Windows Desktop project
```

**How data reaches the native panels**

| Panel | Source | Notes |
| :--- | :--- | :--- |
| Home feed | Instagram timeline responses (`/api/v1/feed/…`, GraphQL timeline) | Real ids, permalinks and like counts; likes are confirmed against Instagram |
| Direct messages | `direct_v2` inbox and thread responses | Unknown payload shapes are ignored rather than guessed |
| Reels | Live state read from the playing reel in the engine | Actions are executed by the shell; the panel only presents and raises events |

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

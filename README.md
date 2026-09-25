# MySQL Management Studio

Cross-platform desktop client for **MySQL** and **MariaDB**, inspired by SQL Server Management Studio (SSMS). Built with **Avalonia UI** and **.NET**.

Connect, browse schemas, run queries with syntax highlighting and IntelliSense, edit simple result grids, and export data — without installing a full web stack.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-FF6B00)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

---

## Features

| Area | What you can do |
|------|-----------------|
| **Connections** | Save profiles (host, port, user, optional database, SSL). Workbench-style home screen. |
| **Object Explorer** | Server → databases → tables, views, procedures, functions → columns. |
| **Query editor** | Multiple tabs, SQL highlighting (TextMate), **IntelliSense** (Ctrl+Space). |
| **Execution** | Run scripts; **Results** grid + **Messages** pane; timing in the status bar. |
| **Grid editing** | Edit cells for **simple single-table `SELECT`s** that include a primary key (no JOIN/GROUP BY/etc.). |
| **Object scripts** | Context menu: **Select TOP 1000**, **Script as CREATE**, **Alter** view / procedure / function. |
| **Export** | Save the current result set to **CSV**. |

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (project targets `net10.0`)
- A reachable **MySQL** or **MariaDB** server (default port **3306**)
- **Windows**, **macOS**, or **Linux** (Avalonia desktop)

---

## Quick start

```bash
git clone <your-repo-url>
cd MySqlManagementStudio
dotnet run
```

**Release build** (faster startup when published):

```bash
dotnet publish -c Release -r osx-arm64 --self-contained false   # example: Apple Silicon Mac
dotnet publish -c Release -r win-x64 --self-contained false     # example: Windows
```

Run the published executable from `bin/Release/net10.0/<rid>/publish/`.

---

## First-time workflow

1. **Launch** the app — you land on the **home** page.
2. Click **New Connection** and enter host, port, username, password, and optional default database.
3. Use **Test connection**, then save. Choose whether to **store the password** locally.
4. **Double-click** a saved connection to open the workspace.
5. **Object Explorer** loads on the left; use **New Query** to open a SQL tab.
6. Type SQL, press **F5** (or **Ctrl+Enter** / **⌘↩** on Mac) to execute.
7. Use **Help → Keyboard Shortcuts** in the app for the full list.

**Tip:** Pick the active database from the toolbar dropdown before running queries that rely on the default schema.

---

## Keyboard shortcuts

Platform-specific menu keys are applied automatically. In-app list: **Help → Keyboard Shortcuts**.

### macOS

| Shortcut | Action |
|----------|--------|
| **F5** or **⌘↩** | Execute query |
| **⌘N** | New query tab |
| **⌘O** | New connection |
| **⌘W** | Close current query |
| **⌘E** | Export results to CSV |
| **⌘R** | Refresh Object Explorer |
| **⌘⇧H** | Go to Home |
| **Ctrl+Space** | IntelliSense (avoids conflicting with ⌘Space Spotlight) |

### Windows / Linux

| Shortcut | Action |
|----------|--------|
| **F5** or **Ctrl+Enter** | Execute query |
| **Ctrl+N** | New query tab |
| **Ctrl+O** | New connection |
| **Ctrl+W** | Close current query |
| **Ctrl+E** | Export results to CSV |
| **Ctrl+R** | Refresh Object Explorer |
| **Ctrl+Shift+H** | Go to Home |
| **Ctrl+Space** | IntelliSense |

**Object Explorer:** right-click tables, views, procedures, and functions for scripting and **Select Top 1000**.

---

## Saved connections

Profiles are stored as JSON on your machine (passwords only if you opt in):

| OS | Location |
|----|----------|
| **macOS** | `~/Library/Application Support/MySqlManagementStudio/connections.json` |
| **Windows** | `%AppData%\MySqlManagementStudio\connections.json` |
| **Linux** | `~/.config/MySqlManagementStudio/connections.json` |

Treat this file like a password manager export — restrict file permissions and do not commit it to git.

---

## Development

Optional Avalonia developer tools (debug only, adds overhead):

```bash
MMS_DEV_TOOLS=1 dotnet run
```

### Project layout

```text
MySqlManagementStudio/
├── Views/              # Main window, connection dialog
├── ViewModels/         # MVVM (CommunityToolkit.Mvvm)
├── Controls/           # SQL editor (AvaloniaEdit + TextMate)
├── Services/           # MySQL access, IntelliSense, CSV export, connection store
├── Models/             # Connection profiles, query results
└── Assets/             # App icon
```

### Main dependencies

- [Avalonia](https://avaloniaui.net/) 12 + Fluent theme  
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) + TextMate grammars  
- [MySqlConnector](https://mysqlconnector.net/)  
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)

---

## Limitations

- **MySQL / MariaDB only** — not SQL Server, PostgreSQL, or SQLite.
- **Inline grid edits** apply only to straightforward single-table selects with a primary key; complex queries are read-only in the grid.
- **Destructive SQL** (`DROP`, `DELETE` without care, etc.) runs as typed — use appropriate database permissions.
- This is a client tool; it does not host a database server.

---

## License

Add a `LICENSE` file in the repository if you publish this project (for example MIT).

---

## Acknowledgments

UI patterns influenced by **SSMS** and **MySQL Workbench**. Not affiliated with Oracle, Microsoft, or the Avalonia project.

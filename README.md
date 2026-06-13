# VExplorer

VExplorer is a keyboard-driven file manager for Windows.

## Features

VExplorer is an alternative to Explorer developed for Vim users and power users who are comfortable with the command line.

All operations can be performed quickly using either the keyboard or mouse, combining the usability of a TUI file manager with the convenience of a native desktop application.

- ⌨ Vim-like keyboard controls: Every operation can be performed efficiently using Vim-inspired keybindings.
- 🚀 Fast navigation: In addition to Vim-style movement (`g`, `G`, `h`, `j`, `k`, `l`) and scrolling (`Ctrl+D`, `Ctrl+U`), VExplorer includes search mode (`/`), filter mode (`F`), command mode (`:`), and an address bar with `Tab` completion. These features allow you to quickly find files and folders and descend into deeply nested directories with great speed.
- 🖱 Well-supported mouse operations: Basic tasks can be handled entirely with the mouse. Tabs, context menus, navigation history, switching to filter mode or search mode, file drag-and-drop, and more can all be operated with the mouse, just like in a traditional Explorer application.
- ⚡ High performance: VExplorer is developed with a strong focus on performance, leveraging asynchronous processing and UI virtualization.
  - Asynchronous processing ensures that user interactions remain responsive and uninterrupted.
  - File list virtualization enables directories containing large numbers of files, such as `WinSxS`, to be displayed in a short amount of time.
- 🪟 Native Windows shell integration: VExplorer directly calls Windows native shell APIs. This allows it to integrate with Explorer functionality and invoke features such as special folders, file operations, and context menus in the same way as the standard Explorer.

## Installation

To build from source, run `dotnet publish` command.

## Thanks

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): Efficient MVVM helper
- [Cysharp/R3](https://github.com/Cysharp/R3): Modern and high-performance asynchronous reactive programming helper replacement for Rx.NET

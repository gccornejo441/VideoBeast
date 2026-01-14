# VideoBeast agent notes

- **Stack & targets**: WinUI 3 desktop app (`net8.0-windows10.0.19041.0`) using Windows App SDK 1.8. Build/run the solution with Visual Studio 2022 or `dotnet build VideoBeast.sln` on Windows 10 17763+ (x86/x64/ARM64). Packaging lives in `VideoBeast (Package)/` (`Package.appxmanifest`, `.wapproj`).

- **App skeleton**:
  - `App.xaml` wires shared resources/brushes and merges `Styles/PlaylistStyles.xaml`. `App.xaml.cs` is minimal (2 imports, unhandled exception handler).
  - `MainWindow.xaml` hosts `Controls/AppShell` (custom title bar + `NavigationView` + `InfoBar`) and enables drag/drop on `RootGrid`.
  - `Controls/AppTitleBar` wraps the WinUI `TitleBar` and exposes a top search box; `AppShell` just surfaces its parts.
  - `Themes/Generic.xaml` is empty; styles sit in `Styles/`.
  - Converters live in `Converters/` (bool → visibility/opacity/font weight, tooltip helpers for sliders).

- **Navigation & library** (`Navigation/`, `Services/`):
  - `LibraryFolderService` persists the chosen library folder in `FutureAccessList` (`LibraryFolderToken`) and tracks the current selection.
  - `LibraryGuardService` ensures a library exists and shows the "choose a folder" UX via `StatusService` if missing.
  - `LibraryTreeBuilder` builds the `NavigationView` tree: Library header with folders/mp4 files (lazy loads children), an "Actions" section, and `MainWindow.EnsurePlaylistNavItem` injects a Playlist item. File items can attach context flyouts.
  - `ActionRouter` maps tags like `action:import` to async handlers registered in `MainWindow`.
  - Drag/drop on `RootGrid` routes to `LibraryImportService` (copies `.mp4` files to the destination folder); picker import uses the same service.
  - `LibraryFileActions` centralizes show-in-explorer, copy path, rename (dialog), and delete (confirm). It stops playback on the target before rename/delete via `PlaybackCoordinator.StopIfPlayingAsync` to avoid file locks and rebuilds the nav tree after changes.

- **Playback/search coordination** (`Services/`):
  - `PlaybackCoordinator` ensures `PlayerPage` is active, remembers pending play requests across navigation, tracks the currently playing file path, and stops playback on demand.
  - `WindowChromeService` toggles a "player full window" experience by hiding the custom title bar, status bar, and nav pane while adjusting `AppWindow` chrome; state is restored when exiting fullscreen. Uses `WindowModeService` for fullscreen presenter management.
  - `StatusService` drives the shared `InfoBar` and also pushes the Player page into an empty state when no library is set.
  - `LibrarySearchService` performs simple filename contains search (current selected folder or library, `.mp4` only). `SearchCoordinator` wires it to the title-bar `AutoSuggestBox` (text changed/suggestion chosen/query submitted) and calls playback when a suggestion is submitted.

- **Pages & UI flows** (`Pages/`):
  - `PlayerPage` owns playback (`MediaPlayer` backing `MediaPlayerElement`), auto-hides controls after 2.5s while playing, and supports play/pause, stop, loop, mute/volume slider, rate selection, seek slider with tooltip, and a fullscreen toggle. Settings and current folder text are pushed in by `MainWindow`/`PlaybackCoordinator`. It handles drag pointer events to keep controls visible and cleans up timers and media events on unload.
  - `FolderVideosPage` lists `.mp4` files in the selected folder with a simple text filter, manual refresh, and context menu actions that proxy back to `MainWindow` (play/show/rename/delete/copy). Shows the folder name and filtered count.
  - `PlaylistsPage` displays all user-created playlists with cover images, item counts, and last updated timestamps. Supports creating, renaming, deleting playlists, and changing cover images. Navigates to `PlaylistDetailsPage` on selection.
  - `PlaylistDetailsPage` shows playlist items with thumbnails, display names, and missing file indicators. Supports adding videos (multi-select picker), removing items, reordering via drag-and-drop, re-linking missing files, playing videos, and adding items to other playlists. Context menu includes play, show in explorer, copy path, remove, and "add to playlist" submenu.
  - `SettingsPage` currently only changes playback scaling (`Stretch`) and saves via `MainWindow.SavePlayerStretch`, which updates `PlayerSettings` and applies immediately.
  - `Mp4IncrementalCollection` and `VideoListItem` provide an incremental list model for large folder views.

- **Playlists** (`Playlists/`, `Services/ThumbnailCache`):
  - `PlaylistModel` represents a playlist with GUID, name, timestamps, cover image key, and a list of `PlaylistItemModel` items. Implements `INotifyPropertyChanged` for UI binding.
  - `PlaylistItemModel` represents a playlist item with GUID, display name, folder token (for `FutureAccessList`), filename, last known path, size/timestamp hints, thumbnail key, sort index, and missing file flag.
  - `PlaylistStore` manages all playlist CRUD operations with atomic JSON persistence to `ApplicationData.Current.LocalFolder/playlists.json`. Uses a shared static cache and semaphore for thread-safe access. Supports creating/renaming/deleting playlists, adding/removing/reordering items, updating cover images, and resolving items back to `StorageFile` via tokens with fallback to last known path. Handles corrupted JSON by renaming and starting fresh.
  - `PlaylistCollection` is the root JSON container holding the list of playlists.
  - `ThumbnailCache` (singleton) generates and caches video thumbnails in `LocalCacheFolder` using SHA256-based keys derived from folder token + filename. Supports loading thumbnails as `BitmapImage`, saving custom cover images, generating playlist covers (currently returns first item's key), and cleaning up orphaned thumbnails older than a specified age.

- **State & persistence**:
  - Player preferences are defined in `PlayerSettings` (autoplay, stretch, full window, volume/mute/loop, playback rate, letterbox color). `PlayerSettingsStore` reads/writes JSON to `ApplicationData.Current.LocalSettings` under the key `player.settings.json` and normalizes the legacy `PlayerStretch` property name. A sample/default payload lives at `VideoBeast/player.settings.json`.
  - `MainWindow` keeps an in-memory `PlayerSettings` snapshot, applies it to `PlayerPage`, and persists on each change (save stretch, settings page, search playback, etc.).
  - `WindowPlacementService` saves/restores window size, position, maximized state, and fullscreen state to `ApplicationData.Current.LocalSettings` under the key `WindowPlacementV1`.
  - Playlists are persisted to `playlists.json` in `LocalFolder` with atomic writes (temp file + rename). Folder access tokens are stored in `FutureAccessList` with prefix `PlaylistFolder_`.

- **Key interactions**:
  - App startup: `App.xaml.cs` launches `MainWindow`, which restores the library folder, builds nav, loads settings, navigates to `PlayerPage`, and wires up nav/title-bar/search events.
  - Navigation: selecting folders updates the current folder text and opens `FolderVideosPage`; selecting files plays immediately; Playlists nav item opens `PlaylistsPage`; Settings opens `SettingsPage`; actions execute via `ActionRouter`.
  - File ops: rename/delete guard against missing library and stop playback if the target is active; after mutating files, nav/playlist refresh is triggered and status messages show in the info bar.
  - Playlist management: create playlists from `PlaylistsPage`, add videos via picker or from folder/playlist context menus, reorder items via drag-and-drop in `PlaylistDetailsPage`, re-link missing files with picker, change cover images with custom image picker.
  - Thumbnail generation: video thumbnails are generated on-demand when adding to playlists and cached persistently; playlist covers default to first item's thumbnail or can be set to custom images.

- **Working tips**:
  - When adding nav actions, register tags in `MainWindow` and ensure `LibraryTreeBuilder` (or callers) creates matching `NavigationViewItem` tags.
  - Use `LibraryGuardService` before any file system work to keep UX consistent for missing library folders.
  - Keep `PlayerPage` UI updates on the UI thread (`DispatcherQueue`) and respect the `_isActive` checks when adding async callbacks.
  - If extending folder video search/filtering, consider using `Mp4IncrementalCollection` for large folders.
  - Full-window mode changes `NavigationView` layout aggressively; test both normal and fullscreen when altering shell/title-bar visuals.
  - Playlist operations use `FutureAccessList` tokens for folder access; ensure tokens are created via `PlaylistStore.GetOrCreateFolderTokenAsync` when adding items.
  - Thumbnail cache keys are stable (based on folder token + filename); regenerate thumbnails when re-linking files to ensure consistency.
  - `PlaylistStore` uses a shared static cache; call `PlaylistStore.InvalidateCache()` if external changes are made to playlist data.

- **Manual checks** (no automated tests here):
  - Choose library folder → import/drag-drop mp4 → tree populates; folder videos page shows count and filtering works.
  - Playback controls: play/pause, seek, mute/volume, rate change, loop toggle, stop, fullscreen toggle (title/nav/status hide/restore).
  - File actions: rename/delete while playing stops playback and updates nav/folder view; show in folder opens Explorer; copy path populates clipboard.
  - Search box: suggestions appear after 2+ chars scoped to selected folder; selecting a suggestion plays the file.
  - Playlists: create/rename/delete playlists; add videos via picker or context menu; reorder items via drag-and-drop; re-link missing files; change cover images; add items to other playlists.
  - Thumbnails: verify thumbnails appear for playlist items and covers; check custom cover images persist; ensure orphaned thumbnails can be cleaned up.

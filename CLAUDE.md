# GitHalls (Windows)

WinUI 3 git client, port of the macOS/SwiftUI GitHalls at `~/XProjects/GitHalls`.
When porting a feature, read the Swift original first — it is the reference for
behaviour, not for structure.

## Projects

| Project | What belongs there |
|---|---|
| `GitHalls.Core` | Everything with no UI: git process wrapper, parsers, models, Jira client. Targets `net10.0`, no WinUI reference. |
| `GitHalls.Core.Tests` | xunit. Only Core is reachable from here — this is why logic lives in Core. |
| `GitHalls.App` | WinUI 3 (`net10.0-windows`). Views, view models, Windows-only services. |
| `GitHalls.Cli` | Scratch harness for Core. |

**Build and run only work on Windows.** On a Mac there is no `dotnet` for this
target: XAML can be checked for well-formedness (`xmllint --noout`), pure logic
can be verified by transcribing it to a script and running it against real `git`
output, and everything else is unverified until it builds. Say which of the
three a change got.

## Architecture

A parser turns raw text into a typed model, a view model orchestrates and holds
UI state, a view renders it. Push logic down: if it can be a pure function in
Core, it belongs in Core with a test.

- **Async loads triggered by a selection are guarded by a request token.** Every
  load stores a fresh `Guid` and checks it is still current before publishing.
  A slow answer for a file, commit or query the user has moved on from must not
  overwrite what is on screen.
- **`RepositoryViewModel` and `JiraViewModel` are separate on purpose.** Jira is
  not git. They meet in exactly one place: `IssueWindow`, which holds both and
  asks the repository to create a branch.
- **Pages are told to `Update()` by `MainWindow`'s `PropertyChanged` switch.**
  Adding a view-model property that a pane renders means adding a case there.
- **The sidebar and the detail pane move together.** Changes pairs with the file
  diff, History with the commit detail, Kanban's query list with the board.
- **Secondary windows, not dialogs, for things that stay open**: Settings and
  each issue (`IssueWindow`, one per key, tracked in `MainWindow._issueWindows`
  so a second click activates instead of duplicating). They are told about
  repository changes by the same `PropertyChanged` switch.

## git

- `GitProcessRunner` throws `GitException` on a non-zero exit. Commands where
  non-zero is a normal answer (`config <key>` for an unset key, `remote get-url`
  for a missing remote) must catch it and return null — see `TryReadConfigAsync`.
- **Use `-z` on anything that prints paths.** Without it git quotes any path with
  an accent or a space (`"configura\303\247\303\265es.md"`), and that quoted form
  is not something git accepts back in a later `-- <path>`.
- The `-z` formats differ from each other: `--raw` puts the path in its own NUL
  field, `--numstat` shares a token with the counts except for renames. Merge
  commits reverse the block order and use combined (`::`) raw entries.
  `CommitFileParser` handles all of it — extend it there, with a test.
- One process per user action, never one per file. Selecting a commit costs one
  `git show --raw --numstat -z`; a file's diff is fetched when that file is
  opened.

## Performance rules that were paid for

- **Never render a diff nobody asked to see.** The commit detail pane renders one
  diff: the selected file's.
- `Update()` runs several times per click (one per property change). Compare
  before re-rendering — `CommitDetailPage` keeps `_shownDiff` for this.
- `DiffTextView` lays out every line it is given: it caps at `MaxRenderedLines`
  and paints tints and gutter for visible rows only. Keep both properties if you
  touch it.

## Visual language

Windows 11 Settings, not a toolbar app. Shared styles live in
`GitHalls.App/Styles/AppStyles.xaml` (`GHCardStyle`, `GHContentCardStyle`,
`GHChipStyle`, `GHListViewStyle`, `GHListItemStyle`, `GHCompactListItemStyle`,
`GH*TextStyle`). Use them instead of inventing local values.

- **No divider lines.** Panes share the window's Mica; content is grouped by
  cards (card fill, hairline `CardStrokeColorDefaultBrush`, 8px radius), never by
  a 1px rule. The splitter is transparent.
- List rows are inset and 4px-rounded, ~40px tall; selection reads as a pill.
- Row anatomy: title in normal weight, supporting line in
  `TextFillColorSecondaryBrush` at 12px.
- Status badges: 3px radius, `6,1` padding. Not pills.
- Always use theme resources, never a hard-coded grey.

## Settings and secrets

- `SettingsStore` owns `AppSettings` in memory (`Current`) and every writer goes
  through `UpdateAsync`. **Never build a fresh `AppSettings` to save** — the file
  has several writers and that drops whatever another one had just saved.
- `AppSettings` is serialized through a source-generated context. The app
  publishes with `PublishTrimmed`, which strips reflection metadata: a type added
  to the settings without the generator comes back empty **in published builds
  only**. The same applies to the Jira wire types (`JiraJsonContext`).
- **No secret ever reaches settings.json.** The Jira API token goes to the
  Windows Credential Manager (`CredentialStore`, target `GitHalls:jira`); the
  site and email stay in settings. A GitHub token is handed to git's own
  credential helper (`git credential approve`) and no copy is kept.
- Identity is written with `config --local`: switching profiles must never touch
  the user's global config.

## Jira

- Jira Cloud authenticates with HTTP Basic over `email:token`, not a bearer.
- `POST /rest/api/3/search/jql`, and only the fields the sidebar shows.
- Jira timestamps carry an offset with no colon (`...-0300`), which no standard
  .NET parse accepts — `JiraTimestamp` normalizes it first.
- The board runs one `JiraQuery` at a time: presets from `JiraQueryPresets`
  (code, never saved) plus the user's own in `AppSettings.JiraCustomQueries`.
  The default is the active sprint (`sprint in openSprints() ORDER BY Rank`).
- Columns are `JiraIssueGrouping.ByStatus`: ordered by `statusCategory`
  (`new` → `indeterminate` → `done`), and within a category in the order Jira
  returned. Status names are project-specific; the category is the only field
  that means the same thing everywhere. The text filter is client-side
  (`Filter`), it never re-queries.
- Search asks only for card fields; `GetIssueAsync` adds description, people,
  dates and labels when a window opens. `Description == null` means "not
  fetched", `""` means "Jira has none" — the window shows them differently.
- Descriptions arrive as Atlassian Document Format (a JSON node tree), not
  text. `JiraAdf.ToPlainText` flattens it; unknown node types still render
  their children. Extend it there, with a test.

## Conventions

- One type per file, grouped by concern. `// MARK: -` separates sections.
- Comments explain **why**, never what the line already says. A comment that
  restates the code is noise; a comment naming the bug a line prevents is not.
- Conventional Commits (`feat(githalls.app): ...`). `/commit-msg` drafts one from
  what is staged.
- Minimal dependencies: CommunityToolkit (MVVM + Sizers), ColorCode, WindowsAppSDK.

# Dev Runner

Runs a project's dev services (APIs, web apps, anything) from one place. It's dev.mjs rebuilt in C# as a desktop app that sits in the notification area, and it reads dev.mjs's config files too.

## Getting it running

Run `build.cmd` for a copy you can run straight away: it puts `DevRunner.Desktop.exe` in `dist\`.

To install it, run `release.cmd 0.0.2`. That makes `releases\DevRunner-win-Setup.exe`, which installs Dev Runner into `%LocalAppData%\DevRunner` with Start menu and desktop shortcuts, and installs the .NET 10 Desktop Runtime if it's missing. Only an installed copy can update itself. `release.cmd 0.0.3-dev.1 dev` makes a Dev (test) build.

To publish a release, push a version tag: `v0.0.2` makes a normal release, `v0.0.3-dev.1` a Dev pre-release. GitHub Actions builds it and puts it on GitHub Releases, where installed copies find it.

A copy that isn't installed (from `build.cmd` or the debug build) is the **DEV** copy: it says "Dev Runner DEV", has DEV stamped on its icon, and runs beside the installed one. It keeps its own project list (`%APPDATA%\mk.dev.runner.dev`), which starts with the `sample\` project in this repo: a small dotnet API, an npm web app, a worker, and a crashing service that's off by default.

Add a project with **+ Add** and pick its folder; the `.dev.json` in its root says what to run. If a folder has no config, or more than one, the app asks you to pick the file (it can have any name) or offers to create `.dev.json`. **Import folder** adds every project in a folder's subfolders.

**Edit** on a project changes its settings and services on screen: name, how dotnet services run, and each service's folder, type, port, colour and command. It saves to the project's config file. The project has to be stopped first.

## Updates

An installed copy checks GitHub Releases every few hours. When there's a newer version, an **Update to x.y.z** button shows in the sidebar and the tray menu; nothing installs until you click it. Updating stops the running services and they stay stopped. Settings → Updates has the version, Check now, and **Get Dev builds** to also get test versions.

The terminal version is parked in `old\` for now; it isn't built.

## Where things live

| What | Where | Committed? |
|---|---|---|
| The stack: services, types, ports, ready patterns | `.dev.json` in the project's root (or any name you pick); service `dir`s are relative to it | yes |
| This machine's choices: which services are on, ports, local environment variables | `.devrunner\local.json` in the project's root (the folder ignores itself in git) | no |
| Logs: one file per service, overwritten every run | `%TEMP%\mk.dev.runner\logs\<project>\` | no, never in the project |
| The project list: a name and a folder each, plus auto-start | `%APPDATA%\mk.dev.runner\settings.json` | no |

The name the app shows is set in the app (Rename), not in the config.

If `settings.json` can't be read, it's kept as `settings.broken-<time>.json` and you're told; it's never overwritten with an empty list.

## Logs

Logs are temporary. Read them in the main window's log panel (a tab per service), or open a service's file from its card. Nothing is kept between runs.

## The app

There's a list of projects on the left. Each service gets a card showing its state, memory, CPU, a memory graph, and buttons for start/stop, restart, browser, log and folder. Logs are shown below the cards in colour.

- **Environment** lists the `.env` files in the project root and each service's folder, so you can edit any of them (your apps read these themselves). Below them are the variables Dev Runner sets on each service's command, for all services or one. Local values stay on this machine (`.devrunner\local.json`); Shared values go in the config file, so they're committed. Drag the list into any order. Saving restarts only the services whose variables changed.
- **Auto-start** on a project starts it when the app opens.
- In **Settings** you can start the app with Windows, start hidden in the tray, and close to the tray. The tray icon goes green when something is running and red when something has crashed. Right-click it to start or stop projects.
- **Exit** from the tray menu stops everything.

Only one copy of a project can run at a time (for dev.mjs configs, that includes old dev.mjs windows). If a service's port is already taken by something else, Dev Runner says what holds it and doesn't start the service; it never stops programs it didn't start. Every service runs inside a Windows job, so if the runner crashes or gets killed, its services go with it. You won't be left with orphaned node or dotnet processes.

## Config extras

Everything in a dev.mjs config still works. These are new and optional:

- `"env": { "KEY": "value" }` at the top level or on a service
- `"type": "command"` with `"cmd"` and `"args"` to run anything; `"script"` to pick the npm script (default `dev`)
- `"url"` for the browser button, e.g. `"http://localhost:{port}/swagger"`
- `"restartOnChange": [".env"]` in globals: files that restart the dotnet services when they change
- `"root"` if the services aren't under the config's folder

## Licence

MIT, see [LICENSE](LICENSE).

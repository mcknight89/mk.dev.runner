# Dev Runner

Runs a project's dev services (APIs, web apps, anything) from one place. It's dev.mjs rebuilt in C# as a desktop app that sits in the notification area, and it reads dev.mjs's config files too.

## Getting it running

Run `build.cmd` for a copy you can run straight away: it puts `DevRunner.Desktop.exe` in `dist\`.

To install it, run `release.cmd 0.2.0`. That makes `releases\DevRunner-win-Setup.exe`, which installs Dev Runner into `%LocalAppData%\DevRunner` with Start menu and desktop shortcuts. Only an installed copy can update itself. `release.cmd 0.3.0-dev.1 dev` makes a Dev (test) build.

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

- **Environment** edits variables for the whole project or one service. Local values stay on this machine (in `dev.<name>.env`). Shared values go in the config file. Saving restarts only the services whose variables changed. It can also edit the project's `.env` file.
- **Auto-start** on a project starts it when the app opens.
- In **Settings** you can start the app with Windows, start hidden in the tray, and close to the tray. The tray icon goes green when something is running and red when something has crashed. Right-click it to start or stop projects.
- **Exit** from the tray menu stops everything.

Only one copy of a project can run at a time, including old dev.mjs windows. Every service runs inside a Windows job, so if the runner crashes or gets killed, its services go with it. You won't be left with orphaned node or dotnet processes.

## Config extras

Everything in a dev.mjs config still works. These are new and optional:

- `"env": { "KEY": "value" }` at the top level or on a service
- `"type": "command"` with `"cmd"` and `"args"` to run anything; `"script"` to pick the npm script (default `dev`)
- `"url"` for the browser button, e.g. `"http://localhost:{port}/swagger"`
- `"restartOnChange": [".env"]` in globals: files that restart the dotnet services when they change
- `"root"` if the services aren't under the config's folder

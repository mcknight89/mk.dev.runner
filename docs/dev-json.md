# The .dev.json file

A `.dev.json` in a project's root tells Dev Runner what to run. Commit it, and everyone on the team gets the same setup.

You don't have to write it by hand: **+ Add** offers to create one, and **Edit** changes it on screen. This page is for when you'd rather edit the file.

## A small example

```json
{
  "services": [
    { "key": "api", "name": "API", "type": "dotnet", "dir": "src/MyApp.Api", "defaultPort": 5000 },
    { "key": "web", "name": "Website", "type": "npm", "dir": "web", "defaultPort": 3000 }
  ]
}
```

This starts a .NET API on port 5000 and an npm site on port 3000. Paths are relative to the file.

## A full example

```json
{
  "root": ".",
  "env": { "LOG_LEVEL": "debug" },
  "globals": {
    "dotnetMode": "managed",
    "solution": "MyApp.sln",
    "autoRetry": true,
    "maxRetries": 3,
    "startTimeoutSec": 120,
    "restartOnChange": [".env", "config/appsettings.local.json"]
  },
  "services": [
    {
      "key": "api",
      "name": "API",
      "type": "dotnet",
      "dir": "src/MyApp.Api",
      "defaultPort": 5000,
      "url": "http://localhost:{port}/swagger",
      "health": { "type": "http", "path": "/health" },
      "color": "cyan"
    },
    {
      "key": "web",
      "name": "Website",
      "type": "npm",
      "dir": "web",
      "script": "start",
      "defaultPort": 3000,
      "env": { "API_URL": "http://localhost:5000" }
    },
    {
      "key": "queue",
      "name": "Queue worker",
      "type": "command",
      "dir": "tools/worker",
      "cmd": "node",
      "args": ["worker.js", "--verbose"],
      "health": { "type": "log", "pattern": "listening for jobs" },
      "enabled": false
    }
  ]
}
```

## Services

Each entry in `"services"` is one thing to run.

| Field | What it does | Default |
|---|---|---|
| `key` | Short id, used in logs and in this machine's settings. Keep it stable. | the name |
| `name` | What the card shows. | the key |
| `type` | `dotnet`, `npm` or `command`. | `command` |
| `dir` | Folder to run in, relative to the project root. | `.` |
| `defaultPort` | The port the service listens on. See [Ports](#ports). | none |
| `url` | What the Browser button opens. `{port}` is replaced with the port. | `http://localhost:{port}` |
| `health` | How Dev Runner knows the service is up and still answering. See [Health checks](#health-checks). | automatic |
| `enabled` | `false` to leave the service off until someone turns it on. | `true` |
| `color` | Card and log colour: `cyan`, `green`, `magenta`, `yellow`, `blue`, `red`, `white`, `orange`, `teal`, `purple`, `pink` or `lime`. | grey |
| `env` | Variables set on this service's command. | none |
| `args` | Extra arguments (a list). | none |
| `script` | npm only: the script to run. | `dev` |
| `cmd` | command only: the program to run. | required |

### What each type runs

| Type | Command |
|---|---|
| `npm` | `npm run <script> -- <args>` |
| `dotnet`, watch mode | `dotnet watch --no-hot-reload -- <args>` |
| `dotnet`, run mode | `dotnet run -- <args>` |
| `dotnet`, managed mode | `dotnet run --no-build --no-restore -- --urls http://localhost:<port> <args>` |
| `command` | `<cmd> <args>` |

Commands run through `cmd.exe`, so `npm`, `npx`, `.cmd` and `.bat` files work as they would in a terminal.

### Ports

If a service has a port, Dev Runner passes it to the app:

- **dotnet:** `ASPNETCORE_URLS=http://localhost:<port>` (in managed mode, `--urls` instead)
- **npm and command:** `PORT=<port>`

Before starting, it checks the port is free. If something else holds it, the card says what, and the service isn't started. Dev Runner never stops a program it didn't start.

Each person can use a different port on their machine (in **Edit**); that's saved locally, not in this file.

### Health checks

A service shows **Starting** until its health check passes, then **Running**. Port and address checks carry on every 10 seconds while it runs. If one fails three times in a row, the card shows **Not responding** in amber with the reason, and goes back to Running when it answers again. The service is never stopped because of it.

| `"health"` | Running once | Keeps checking |
|---|---|---|
| *(none)* or `{ "type": "auto" }` | the port answers, or the usual startup line appears (`Now listening on` for dotnet, `Local:` and similar for npm) | the port, once it has answered |
| `{ "type": "port" }` | something answers on the port | yes |
| `{ "type": "http", "path": "/health" }` | the address answers without an error (below 400) | yes |
| `{ "type": "log", "pattern": "jobs ready" }` | a log line matches the pattern (regex, any case) | no |
| `{ "type": "none" }` | straight away | no |

- `path` can be a path on the service's port (`/health`) or a full address (`http://localhost:{port}/ready`).
- `"everySec": 30` changes how often port and address checks run (default 10).
- If nothing passes within `startTimeoutSec` (90 s), the card shows **Not responding**, with what it was waiting for. The service keeps running.
- With the automatic check, a service with no port and no usual startup line counts as running as soon as it starts.
- The older `"readyPattern": "..."` still works; it's the same as a `log` check.

## Globals

Settings for the whole project, under `"globals"`.

| Field | What it does | Default |
|---|---|---|
| `dotnetMode` | How dotnet services run: `watch`, `run` or `managed`. See below. | `watch` |
| `solution` | The `.sln` to build in managed mode, relative to the root. | none |
| `autoRebuild` | Managed mode: rebuild and restart when code changes. | `true` |
| `rebuildDebounceSec` | Managed mode: how long to wait after the last change before building. | `2` |
| `autoRetry` | Restart a service when it crashes. | `false` |
| `maxRetries` | How many crashes in a row before giving up. | `5` |
| `startTimeoutSec` | How long a health check has to pass after starting. | `90` |
| `restartOnChange` | Files that restart the dotnet services when they change, relative to the root. | `[".env"]` |
| `logBufferLines` | How many log lines each service keeps on screen. | `2000` |

### dotnet modes

- **watch:** each service runs `dotnet watch`, which rebuilds and restarts it when you save. Simple, but every service builds on its own.
- **run:** each service runs `dotnet run` once. Restart it yourself after changing code.
- **managed:** Dev Runner builds the `solution` once, then runs each service without building. When code changes, it rebuilds only the projects affected and restarts only the services that use them. Faster for big solutions with several services.

## Top level

| Field | What it does |
|---|---|
| `services` | The list above. |
| `globals` | The settings above. |
| `env` | Variables set on every service's command. |
| `root` | Where service folders are relative to, if not the file's own folder. |

## Variables

Variables in `env` are set on the service's process, so the app sees them like any other environment variable. A service's own `env` is added after the project's. Your `.env` files are separate: Dev Runner doesn't read them, your app does. The **Environment** window lets you edit both.

## This machine's settings

Things that differ per person (which services are on, ports, private variables) are saved in `.devrunner\local.json` next to the config. That folder git-ignores itself, so it's never committed. Change these in the app rather than by hand.

## Other names

Dev Runner finds `.dev.json` first. It also reads dev.mjs config files (`dev.mjs.config.json`, or `dev.<name>.config.json`). If a folder has several, it asks which one to use.

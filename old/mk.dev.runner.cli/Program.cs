using System.Diagnostics;
using mk.dev.runner.cli;
using mk.dev.runner.core;

ConsoleNative.EnableVtOutput();
Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    return Cli.Execute(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\x1b[91merror:\x1b[0m {ex.Message}");
    return 1;
}

static class Cli
{
    const string Dim = "\x1b[90m", Bold = "\x1b[1m", R = "\x1b[0m", Green = "\x1b[32m", Yellow = "\x1b[33m", Red = "\x1b[91m";

    public static int Execute(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }
        var cmd = args[0].ToLowerInvariant();
        var rest = args[1..];
        return cmd switch
        {
            "help" or "-h" or "--help" or "/?" => Usage(),
            "list" or "ls" => List(),
            "run" => rest.Length > 0 ? Run(rest[0]) : Fail("usage: devrunner run <project>"),
            "add" => rest.Length > 0 ? Add(rest) : Fail("usage: devrunner add <project folder or config file>… [--name <name>]"),
            "new" => New(rest),
            "rename" => rest.Length > 1 ? Rename(rest[0], string.Join(' ', rest[1..])) : Fail("usage: devrunner rename <project> <new name>"),
            "remove" or "rm" => rest.Length > 0 ? Remove(rest[0]) : Fail("usage: devrunner remove <project>"),
            "env" => rest.Length > 0 ? Env(rest) : Fail("usage: devrunner env <project> [--service <key>] [--shared] [set K=V… | unset K…]"),
            "desktop" => Desktop(),
            _ => Run(args[0]),
        };
    }

    static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    static int Usage()
    {
        Console.WriteLine($"""
            {Bold}devrunner{R} — run a project's dev services in one window

              devrunner <project>                 open the dashboard and start the project
              devrunner list                      projects you've added
              devrunner add <folder>…             add a project folder (its .dev.json); a folder of projects adds them all
              devrunner add <config file>         add a project whose config has another name
                         --name <name>            …under this name
              devrunner new [folder] [--name n]   create an empty .dev.json in the folder (default: here) and add it
              devrunner rename <project> <name>   change the name the runner shows
              devrunner remove <project>          forget a project (files are left alone)
              devrunner env <project>             show environment variables
              devrunner env <project> set K=V     set (local to this machine unless --shared)
              devrunner env <project> unset K     remove
                         --service <key>          …for one service instead of the whole project
                         --shared                 …in the config file instead of the local overrides file
              devrunner desktop                   open the tray app
            """);
        Console.WriteLine();
        List();
        return 1;
    }

    static int List()
    {
        var settings = Settings();
        if (settings.Projects.Count == 0)
        {
            Console.WriteLine($"{Dim}No projects yet. Add a project folder: devrunner add <folder>{R}");
            return 0;
        }
        Console.WriteLine($"{Bold}Projects{R}");
        foreach (var lp in settings.LoadProjects())
        {
            if (lp.Project is { } p)
            {
                var svcs = string.Join(", ", p.Config.Services.Select(s => p.IsEnabled(s) ? s.Key : $"{Dim}{s.Key}{R}"));
                Console.WriteLine($"  {Green}{p.Name,-20}{R} {svcs}  {Dim}{p.ConfigPath}{(lp.Entry.AutoStart ? "  [auto-start]" : "")}{R}");
            }
            else Console.WriteLine($"  {Red}{lp.Entry.DisplayName,-20}{R} {lp.Error}");
        }
        return 0;
    }

    /// <summary>A registered project by name, or a dev.&lt;name&gt;.config.json in the current folder (like dev.mjs).</summary>
    static Project Resolve(string nameOrPath)
    {
        var settings = Settings();
        if (settings.Find(nameOrPath) is { } e) return Project.Load(e.ConfigFile, e.Name);
        if (Directory.Exists(nameOrPath) && Project.FolderConfigIn(nameOrPath) is { } inFolder) return Project.Load(inFolder);
        if (File.Exists(nameOrPath)) return Project.Load(nameOrPath);
        var local = Path.Combine(Environment.CurrentDirectory, $"dev.{nameOrPath}.config.json");
        if (File.Exists(local)) return Project.Load(local);
        var names = settings.Projects.Select(p => p.DisplayName).ToList();
        throw new InvalidOperationException($"No project called '{nameOrPath}'." +
            (names.Count > 0 ? $" Projects: {string.Join(", ", names)}" : " Add one with: devrunner add <project folder>"));
    }

    /// <summary>The settings, saying so if settings.json couldn't be read (it is kept aside, never overwritten).</summary>
    static AppSettings Settings()
    {
        var s = AppSettings.Load();
        if (s.LoadProblem != null) Console.Error.WriteLine($"{Yellow}warning:{R} {s.LoadProblem}");
        return s;
    }

    static string Arg(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    static int Run(string name)
    {
        var project = Resolve(name);
        using var runner = new ProjectRunner(project, "devrunner CLI");
        var code = new Dashboard(runner).Run();
        return code;
    }

    static int Add(string[] args)
    {
        var settings = Settings();
        int failed = 0;
        string? name = null;
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
            if (args[i] is "--name" or "-n" && i + 1 < args.Length) name = args[++i]; else paths.Add(args[i]);
        foreach (var path in paths)
        {
            try
            {
                var found = Directory.Exists(path) ? Project.FindConfigs(path) : null;
                if (found is { Count: 1 })
                {
                    var p = settings.Add(found[0], name);
                    Console.WriteLine($"{Green}added{R} {p.Name}  {Dim}{p.ConfigPath}{R}");
                }
                else if (found is { Count: > 1 })
                {
                    Console.WriteLine($"{Yellow}more than one config{R} in {path}: {string.Join(", ", found.Select(Path.GetFileName))}");
                    Console.WriteLine($"{Dim}  add the one you want: devrunner add \"{found[0]}\"{R}");
                    failed++;
                }
                else if (found != null)
                {
                    // no config of its own: a folder of projects (looks one level down)
                    var (added, errors) = settings.ImportFolder(path);
                    foreach (var a in added) Console.WriteLine($"{Green}added{R} {a}");
                    foreach (var e in errors) { Console.WriteLine($"{Red}skipped{R} {e}"); failed++; }
                    if (added.Count == 0 && errors.Count == 0)
                    {
                        Console.WriteLine($"{Yellow}no config file{R} in {path} or the folders in it.");
                        Console.WriteLine($"{Dim}  create one: devrunner new \"{path}\"   or, if it has another name: devrunner add \"{Path.Combine(path, "<file>.json")}\"{R}");
                        failed++;
                    }
                }
                else
                {
                    var p = settings.Add(path, name);
                    Console.WriteLine($"{Green}added{R} {p.Name}  {Dim}{p.ConfigPath}{R}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"{Red}skipped{R} {path}: {ex.Message}"); failed++; }
        }
        settings.Save();
        return failed > 0 ? 1 : 0;
    }

    static int New(string[] args)
    {
        string? name = null, dir = null;
        for (int i = 0; i < args.Length; i++)
            if (args[i] is "--name" or "-n" && i + 1 < args.Length) name = args[++i]; else dir ??= args[i];
        dir ??= Environment.CurrentDirectory;
        if (!Directory.Exists(dir)) return Fail($"No such folder: {dir}");
        var path = Project.CreateNew(dir);
        var settings = Settings();
        var p = settings.Add(path, name);
        settings.Save();
        Console.WriteLine($"{Green}created{R} {path}");
        Console.WriteLine($"{Dim}Add the services to it, then run: devrunner {Arg(p.Name)}{R}");
        return 0;
    }

    static int Rename(string project, string name)
    {
        var settings = Settings();
        var e = settings.Find(project) ?? throw new InvalidOperationException($"No project called '{project}'.");
        if (settings.Find(name) is { } other && other != e) return Fail($"A project called '{name}' is already added.");
        e.Name = name;
        settings.Save();
        Console.WriteLine($"{Green}renamed{R} to {name}");
        return 0;
    }

    static int Remove(string name)
    {
        var settings = Settings();
        var e = settings.Find(name) ?? throw new InvalidOperationException($"No project called '{name}'.");
        settings.Projects.Remove(e);
        settings.Save();
        Console.WriteLine($"removed {name} {Dim}(config files left in place){R}");
        return 0;
    }

    static int Env(string[] args)
    {
        var project = Resolve(args[0]);
        string? service = null;
        var scope = EnvScope.Local;
        string? action = null;
        var items = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--service" or "-s" && i + 1 < args.Length) service = args[++i];
            else if (a == "--shared") scope = EnvScope.Shared;
            else if (a == "--local") scope = EnvScope.Local;
            else if (action == null && a is "set" or "unset") action = a;
            else items.Add(a);
        }
        if (service != null)
        {
            var svc = project.Service(service) ?? throw new InvalidOperationException(
                $"No service '{service}' in {project.Name}. Services: {string.Join(", ", project.Config.Services.Select(s => s.Key))}");
            service = svc.Key;
        }

        if (action != null)
        {
            var map = project.GetEnv(service, scope);
            foreach (var it in items)
            {
                if (action == "set")
                {
                    int eq = it.IndexOf('=');
                    if (eq <= 0) return Fail($"expected KEY=VALUE, got '{it}'");
                    map[it[..eq]] = it[(eq + 1)..];
                }
                else map.Remove(it);
            }
            project.SetEnv(service, scope, map);
            var where = scope == EnvScope.Shared ? Path.GetFileName(project.ConfigPath) : Path.GetFileName(project.LocalPath);
            Console.WriteLine($"{Green}saved{R} to {where}. Running services pick it up on restart (the desktop app restarts them for you).");
        }

        PrintEnv(project, null);
        foreach (var s in project.Config.Services) PrintEnv(project, s.Key);
        return 0;
    }

    static void PrintEnv(Project p, string? service)
    {
        var shared = p.GetEnv(service, EnvScope.Shared);
        var local = p.GetEnv(service, EnvScope.Local);
        if (shared.Count == 0 && local.Count == 0) return;
        Console.WriteLine($"{Bold}{(service == null ? $"{p.Name} (all services)" : service)}{R}");
        foreach (var k in shared.Keys.Union(local.Keys).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (local.TryGetValue(k, out var lv))
                Console.WriteLine($"  {k} = {lv}  {Dim}local{(shared.ContainsKey(k) ? ", overrides shared" : "")}{R}");
            else
                Console.WriteLine($"  {k} = {shared[k]}  {Dim}shared{R}");
        }
    }

    static int Desktop()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "DevRunner.Desktop.exe");
        if (!File.Exists(exe)) return Fail($"DevRunner.Desktop.exe isn't next to devrunner.exe ({AppContext.BaseDirectory}). Run build.cmd to publish both.");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        return 0;
    }
}

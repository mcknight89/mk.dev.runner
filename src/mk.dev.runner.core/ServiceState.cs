using mk.helpers;
using System.Diagnostics;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core
{
    public enum ServiceState { Disabled, Stopped, Starting, Running, Building, Restarting, Retrying, Crashed, Failed }
}

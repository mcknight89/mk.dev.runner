using System.Diagnostics;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core
{
    internal sealed class ServiceProcess
    {
        public ServiceProcess(Process process, JobObject job, bool inJob)
        {
            P = process;
            Job = job;
            InJob = inJob;
        }

        public Process P { get; }
        public JobObject Job { get; }
        public bool InJob { get; }
        public StopReason? KillReason { get; set; }
        public bool ExitHandled { get; set; }
        public DateTime StartedAt { get; } = DateTime.Now;
        public TimeSpan LastCpu { get; set; }
        public DateTime LastSampleAt { get; set; } = DateTime.Now;

        public string? Kill(StopReason reason, bool wait)
        {
            KillReason ??= reason;
            try
            {
                if (InJob)
                    Job.Terminate();
                else
                    P.Kill(entireProcessTree: true);
                if (wait)
                    P.WaitForExit(5000);
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }
}

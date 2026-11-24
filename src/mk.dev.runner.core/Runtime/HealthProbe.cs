using System.Net.Http;
using System.Net.Sockets;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.core.Runtime
{
    internal sealed record ProbeResult(bool Ok, bool PortAnswered, string? Why);

    internal static class HealthProbe
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

        public static async Task<ProbeResult> Check(HealthCheck kind, int? port, string? url, IReadOnlyList<int> listening, bool anyPortCounts)
        {
            switch (kind)
            {
                case HealthCheck.Http:
                    return await Http(url!);
                case HealthCheck.Port:
                    {
                        var ok = await Answers(port!.Value);
                        return new ProbeResult(ok, ok, ok ? null : $"nothing is answering on :{port}");
                    }
                default:
                    {
                        if (port is > 0 && await Answers(port.Value))
                            return new ProbeResult(true, true, null);
                        if (anyPortCounts && listening.Count > 0)
                            return new ProbeResult(true, false, null);
                        return new ProbeResult(false, false, port is > 0 ? $"nothing is answering on :{port}" : null);
                    }
            }
        }

        private static async Task<bool> Answers(int port)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("localhost", port, cts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<ProbeResult> Http(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var code = (int)response.StatusCode;
                return code < 400 ? new ProbeResult(true, true, null) : new ProbeResult(false, true, $"{url} answered {code}");
            }
            catch
            {
                return new ProbeResult(false, false, $"{url} didn't answer");
            }
        }
    }
}

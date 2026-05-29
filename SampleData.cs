using System.Text;
using System.Text.Json;

namespace NdjsonLogViewer;

public static class SampleData
{
    private sealed record Seed(string Level, string Src, string Msg, string? Stack = null);

    private static readonly Seed[] _seeds =
    {
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "Finished restarting interrupted workflows."),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "GetLastWorkflowInstanceDetails was triggered!"),
        new("Warning",     "Elsa.Workflows.Runtime",                            "The service registration for Elsa.Workflows.Runtime.IWorkflowRuntime is an 'opaque' lambda factory requiring service location."),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "ExecutorSuspendWorkflowRequest was triggered! 9342adc605a90894"),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "PauseAlteration was triggered! 9342adc605a90894"),
        new("Error",       "Elsa.Workflows.Management",                         "Workflow execution pipeline aborted unexpectedly due to missing IWorkflowGraphBuilder reference context.",
            "at Elsa.Workflows.WorkflowRunner.RunAsync(Workflow workflow)\nat ElsaWrapper.Workflows.Execution.Engine.Process()"),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "DefaultHandler invoked event ElsaWrapper.Workflows.Contracts.Events.ExecutorResumeWorkflowRequest"),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "Successfully processed message ElsaWrapper.Eventgrid.EventGridMessage#5f6ff3ed-0fff-406d-9f45-21c3a0698256 from asb://queue/executor-command-request"),
        new("Verbose",     "Microsoft.AspNetCore.Hosting.Diagnostics",          "Request starting HTTP/1.1 GET http://elsa-wrapper.azurewebsites.net/api/health"),
        new("Information", "Microsoft.Diagnostics.Tracing.ETWTraceEventSource", "GetWorkflowDefinitionCodes was triggered!"),
    };

    public static string Generate(int count = 150)
    {
        var sb = new StringBuilder(count * 600);
        var now = DateTime.UtcNow;

        for (int i = 0; i < count; i++)
        {
            var seed = _seeds[i % _seeds.Length];
            var timestamp = now.AddSeconds(-(count - i) * 2).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
            var category = i % 4 == 0 ? "AppServiceConsoleLogs" : "AppServiceAppLogs";

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("time", timestamp);
                w.WriteString("resourceId",
                    "SITES/elsa-wrapper");
                w.WriteString("category", category);
                w.WriteString("operationName", "Microsoft.Web/sites/log");
                w.WriteString("level", seed.Level);
                w.WriteString("resultDescription", seed.Msg);

                w.WriteStartObject("properties");
                w.WriteString("preciseDateTime", timestamp);
                w.WriteString("resourceId", "elsa-wrapper.azurewebsites.net");
                w.WriteString("source", seed.Src);
                w.WriteString("webSiteInstanceId", "9342adc605a90894");
                w.WriteString("level", seed.Level);
                w.WriteString("message", seed.Msg);
                if (seed.Stack is not null)
                    w.WriteString("stacktrace", seed.Stack);
                else
                    w.WriteNull("stacktrace");
                w.WriteEndObject();

                w.WriteEndObject();
            }

            sb.Append(Encoding.UTF8.GetString(ms.ToArray()));
            sb.Append('\n');
        }

        return sb.ToString();
    }
}

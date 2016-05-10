using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ILogger = Microsoft.Build.Framework.ILogger;

namespace MSBuildSerilogLogger
{
    public class SerilogLogger : ILogger
    {
        Serilog.ILogger _logger;

        int _warnings = 0;
        int _errors = 0;

        DateTime _buildStartedTime;

        public SerilogLogger()
        {
            _logger = new LoggerConfiguration()
                .WriteTo.LiterateConsole()
                .WriteTo.Seq("http://localhost:5341/")
                .CreateLogger();
        }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.BuildStarted += EventSource_BuildStarted;
            eventSource.BuildFinished += EventSource_BuildFinished;
            eventSource.ProjectStarted += EventSource_ProjectStarted;
            eventSource.ProjectFinished += EventSource_ProjectFinished;
            eventSource.TargetStarted += EventSource_TargetStarted;
            eventSource.TargetFinished += EventSource_TargetFinished;
            eventSource.TaskStarted += EventSource_TaskStarted;
            eventSource.TaskFinished += EventSource_TaskFinished;

            eventSource.ErrorRaised += EventSource_ErrorRaised;
            eventSource.WarningRaised += EventSource_WarningRaised;
            eventSource.MessageRaised += EventSource_MessageRaised;

            eventSource.CustomEventRaised += EventSource_CustomEventRaised;
        }

        private void EventSource_BuildStarted(object sender, BuildStartedEventArgs e)
        {
            _buildStartedTime = e.Timestamp;

            _logger
                .WithEnvironment(e.BuildEnvironment)
                .Information("Build started {BuildStartedTime}", e.Timestamp);
        }

        private void EventSource_BuildFinished(object sender, BuildFinishedEventArgs e)
        {
            _logger
                .ForContext("Warnings", _warnings)
                .ForContext("Errors", _errors)
                .ForContext("TimeElapsed", e.Timestamp - _buildStartedTime)
                .Information("Build Finished: {BuildFinishedMessage}", e.Message);
        }

        private void EventSource_ProjectStarted(object sender, ProjectStartedEventArgs e)
        {
        }

        private void EventSource_ProjectFinished(object sender, ProjectFinishedEventArgs e)
        {
        }

        private void EventSource_TargetStarted(object sender, TargetStartedEventArgs e)
        {
        }

        private void EventSource_TargetFinished(object sender, TargetFinishedEventArgs e)
        {
        }

        private void EventSource_TaskStarted(object sender, TaskStartedEventArgs e)
        {
        }

        private void EventSource_TaskFinished(object sender, TaskFinishedEventArgs e)
        {
        }

        private void EventSource_ErrorRaised(object sender, BuildErrorEventArgs e)
        {
        }

        private void EventSource_WarningRaised(object sender, BuildWarningEventArgs e)
        {
        }

        private void EventSource_MessageRaised(object sender, BuildMessageEventArgs e)
        {
        }

        private void EventSource_CustomEventRaised(object sender, CustomBuildEventArgs e)
        {
        }


        public void Shutdown()
        {

        }

        public LoggerVerbosity Verbosity { get; set; }
        public string Parameters { get; set; }
    }

    static class LoggerExtensions
    {
        //private class EnvironmentEnricher : ILogEventEnricher
        //{
        //    IDictionary<string, string> _environment;

        //    public EnvironmentEnricher(IDictionary<string, string> environment)
        //    {
        //        _environment = environment;
        //    }

        //    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        //    {
        //        propertyFactory.
        //    }
        //}

        public static Serilog.ILogger WithEnvironment(this Serilog.ILogger logger,
            IDictionary<string, string> environment)
        {
            if (environment != null && environment.Any())
            {
                logger = logger.ForContext("Properties", new Dictionary<string, string>(environment));
            }
            return logger;
        }

    }
}

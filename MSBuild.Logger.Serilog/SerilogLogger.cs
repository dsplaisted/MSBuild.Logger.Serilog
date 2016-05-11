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
using System.Collections;

namespace MSBuildSerilogLogger
{
    public class SerilogLogger : ILogger
    {
        Serilog.ILogger _logger;

        int _warnings = 0;
        int _errors = 0;

        DateTime _buildStartedTime;
        bool _gotRootProjectStarted;
        List<Action> _pendingLogMessages = new List<Action>();


        List<Frame> _frameStack = new List<Frame>();

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

            //  Wait until we know what the root project is to log this message, so we can set its BuildID
            _pendingLogMessages.Add(() =>
            {
                _logger
                    .WithEnvironment(e.BuildEnvironment)
                    .Information("Build started {BuildStartedTime}", e.Timestamp);
            });

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
            if (!_gotRootProjectStarted)
            {
                //  Add a unique key for this build to each event
                _logger = _logger.ForContext("BuildID", e.ProjectFile + "|" + _buildStartedTime.ToString("O"));
                _gotRootProjectStarted = true;
                foreach (var action in _pendingLogMessages)
                {
                    action();
                }
                _pendingLogMessages.Clear();
            }


            Frame parentFrame = _frameStack.LastOrDefault();
            Frame parentProject = Enumerable.Reverse(_frameStack).FirstOrDefault(f => f.Type == FrameType.Project);
            _frameStack.Add(new Frame(FrameType.Project, e.ProjectFile, parentFrame));

            string targets = string.IsNullOrEmpty(e.TargetNames) ? "default" : e.TargetNames;

            var loggerWithContext = _logger.WithProperties(e.Properties);

            //  TODO: log items

            if (parentProject == null)
            {
                loggerWithContext.Information("Project {ProjectPath} ({Targets} targets)", e.ProjectFile, targets);
            }
            else
            {
                loggerWithContext.Information("Project {ParentProjectPath} is building {ProjectPath} ({Targets} targets)", parentProject.Name, e.ProjectFile, targets);
            }
        }

        private void EventSource_ProjectFinished(object sender, ProjectFinishedEventArgs e)
        {
            _logger.Information("Project Finished: {ProjectFinishedMessage}", e.Message);
            _frameStack.RemoveAt(_frameStack.Count - 1);
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

        internal enum FrameType
        {
            Project,
            Target
        }

        internal class Frame
        {
            public FrameType Type { get; }
            public string Name { get; }
            public Frame Parent { get; }

            public Frame(FrameType type, string name, Frame parent)
            {
                Type = type;
                Name = name;
                Parent = parent;
            }
        }
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
                logger = logger.ForContext("Environment", new Dictionary<string, string>(environment));
            }
            return logger;
        }

        public static Serilog.ILogger WithProperties(this Serilog.ILogger logger,
            IEnumerable properties)
        {
            var propertyDict = properties.Cast<DictionaryEntry>().ToDictionary(entry => (string) entry.Key, entry => (string) entry.Value);
            if (!propertyDict.Any())
            {
                return logger;
            }
            return logger.ForContext("Properties", propertyDict);
        }

    }
}

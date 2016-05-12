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
                //.WriteTo.LiterateConsole()
                .WriteTo.Seq("http://localhost:5341/")
                .CreateLogger();
        }

        void PushFrame(Frame frame)
        {
            _frameStack.Add(frame);
        }

        Frame PopFrame()
        {
            var ret = _frameStack[_frameStack.Count - 1];
            _frameStack.RemoveAt(_frameStack.Count - 1);
            return ret;
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
            PushFrame(new Frame(FrameType.Project, e.ProjectFile, parentFrame));

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
            var currentFrame = PopFrame();
            _logger.Information("Project Finished: {ProjectFinishedMessage}", e.Message);
        }

        private void EventSource_TargetStarted(object sender, TargetStartedEventArgs e)
        {
            Frame parentFrame = _frameStack.LastOrDefault();
            PushFrame(new Frame(FrameType.Target, e.TargetName, parentFrame));

            _logger
                .WithStack(_frameStack)
                .Information("Target {TargetName} from file {TargetFile} started", e.TargetName, e.TargetFile);
        }

        private void EventSource_TargetFinished(object sender, TargetFinishedEventArgs e)
        {
            _logger
                .WithStack(_frameStack)
                .WithTargetOutputs(e.TargetOutputs)
                .Information("Target Finished: {TargetFinishedMessage}", e.Message);

            var currentFrame = PopFrame();
        }

        private void EventSource_TaskStarted(object sender, TaskStartedEventArgs e)
        {
            Frame parentFrame = _frameStack.LastOrDefault();
            PushFrame(new Frame(FrameType.Task, e.TaskName, parentFrame));

            Frame parentProject = Enumerable.Reverse(_frameStack).FirstOrDefault(f => f.Type == FrameType.Project);
            _logger
                .WithStack(_frameStack)
                .Information("Task started: {TaskStartedMessage}", e.Message);
        }

        private void EventSource_TaskFinished(object sender, TaskFinishedEventArgs e)
        {
            _logger
                .WithStack(_frameStack)
                .Information("Task finished: {TaskFinishedMessage}", e.Message);

            var currentFrame = PopFrame();
        }

        private void EventSource_ErrorRaised(object sender, BuildErrorEventArgs e)
        {
            _errors++;

            _logger
                .WithStack(_frameStack)
                .Error("Error: {ErrorMessage}", e.Message);
        }

        private void EventSource_WarningRaised(object sender, BuildWarningEventArgs e)
        {
            _warnings++;

            _logger
                .WithStack(_frameStack)
                .Warning("Warning: {WarningMessage}", e.Message);
        }

        private void EventSource_MessageRaised(object sender, BuildMessageEventArgs e)
        {
            _logger
                .WithStack(_frameStack)
                .Information("{MessageImportance} message: {MessageText}", e.Importance, e.Message);
        }

        private void EventSource_CustomEventRaised(object sender, CustomBuildEventArgs e)
        {
            _logger
                .WithStack(_frameStack)
                .Information("Custom message: {CustomMessageText}", e.Message);
        }


        public void Shutdown()
        {

        }

        public LoggerVerbosity Verbosity { get; set; }
        public string Parameters { get; set; }
    }

    internal enum FrameType
    {
        Project,
        Target,
        Task
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

    static class LoggerExtensions
    {
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

        public static Serilog.ILogger WithTargetOutputs(this Serilog.ILogger logger, IEnumerable targetOutputs)
        {
            if (targetOutputs != null)
            {
                var items = targetOutputs.Cast<ITaskItem>();
                if (items.Any())
                {
                    return logger.ForContext("TargetOutputItems", items.Select(item => item.ItemSpec));
                }
            }
            return logger;
        }

        public static Serilog.ILogger WithStack(this Serilog.ILogger logger, List<Frame> frameStack)
        {
            Frame taskFrame = null;
            Frame targetFrame = null;
            Frame projectFrame = null;

            foreach (var frame in Enumerable.Reverse(frameStack))
            {
                if (frame.Type == FrameType.Task && taskFrame == null && targetFrame == null && projectFrame == null)
                {
                    taskFrame = frame;
                }
                else if (frame.Type == FrameType.Target && targetFrame == null && projectFrame == null)
                {
                    targetFrame = frame;
                }
                else if (frame.Type == FrameType.Project)
                {
                    projectFrame = frame;
                    break;
                }
            }
            
            if (projectFrame != null)
            {
                logger = logger.ForContext("ProjectPath", projectFrame.Name);
            }
            if (targetFrame != null)
            {
                logger = logger.ForContext("TargetName", targetFrame.Name);
            }
            if (taskFrame != null)
            {
                logger = logger.ForContext("TaskName", taskFrame.Name);
            }

            return logger;
        }

    }
}

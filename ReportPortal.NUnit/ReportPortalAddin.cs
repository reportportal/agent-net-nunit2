using System;
using System.Collections.Generic;
using System.Linq;
using ReportPortal.NUnit.EventArguments;
using ReportPortal.Client;
using ReportPortal.Client.Models;
using ReportPortal.Client.Requests;
using ReportPortal.Shared;
using NUnit.Core;
using NUnit.Core.Extensibility;
using System.Net;

namespace ReportPortal.NUnit
{
    [NUnitAddin(Type = ExtensionType.Core, Name = "EPAM Report Portal", Description = "Results synchronization with centralized system.")]
    public class ReportPortalAddin : EventListener, IAddin
    {
        public static ReportPortalAddin InstalledAddin { get; private set; }

        private readonly Dictionary<ResultState, Status> _statusMap = new Dictionary<ResultState, Status>();

        public ReportPortalAddin()
        {
            var uri = new Uri(Configuration.ReportPortal.Server.Url);
            var project = Configuration.ReportPortal.Server.Project;
            var username = Configuration.ReportPortal.Server.Authentication.Username;
            var password = Configuration.ReportPortal.Server.Authentication.Password;

            IWebProxy proxy = null;

            if (Configuration.ReportPortal.Server.Proxy.ElementInformation.IsPresent)
            {
                proxy = new WebProxy(Configuration.ReportPortal.Server.Proxy.Server);
            }

            Bridge.Service = proxy == null ? new Service(uri, project, password) : new Service(uri, project, password, proxy);

            _statusMap[ResultState.Cancelled] = Status.Skipped;
            _statusMap[ResultState.Error] = Status.Failed;
            _statusMap[ResultState.Failure] = Status.Failed;
            _statusMap[ResultState.Ignored] = Status.Skipped;
            _statusMap[ResultState.Inconclusive] = Status.Skipped;
            _statusMap[ResultState.NotRunnable] = Status.Skipped;
            _statusMap[ResultState.Skipped] = Status.Skipped;
            _statusMap[ResultState.Success] = Status.Passed;
        }

        public delegate void RunStartedHandler(object sender, RunStartedEventArgs e);
        public event RunStartedHandler BeforeRunStarted;
        public event RunStartedHandler AfterRunStarted;
        public void RunStarted(string name, int testCount)
        {
            var requestNewLaunch = new StartLaunchRequest
                {
                    Name = Configuration.ReportPortal.Launch.Name,
                    StartTime = DateTime.UtcNow
                };
            if (Configuration.ReportPortal.Launch.DebugMode)
            {
                requestNewLaunch.Mode = LaunchMode.Debug;
            }
            requestNewLaunch.Tags = new List<string>(Configuration.ReportPortal.Launch.Tags.Split(','));

            var eventArg = new RunStartedEventArgs(Bridge.Service, requestNewLaunch);
            if (BeforeRunStarted != null) BeforeRunStarted(this, eventArg);
            if (!eventArg.Canceled)
            {
                Bridge.Context.LaunchId = Bridge.Service.StartLaunch(requestNewLaunch).Id;
                if (AfterRunStarted != null) AfterRunStarted(this, new RunStartedEventArgs(Bridge.Service, requestNewLaunch, Bridge.Context.LaunchId));
            }
        }

        private Stack<string> _suiteIds;

        public delegate void SuiteStartedHandler(object sender, TestItemStartedEventArgs e);
        public event SuiteStartedHandler BeforeSuiteStarted;
        public event SuiteStartedHandler AfterSuiteStarted;
        public void SuiteStarted(TestName testName)
        {
            // skip the first suite
            if (_suiteIds == null)
            {
                _suiteIds = new Stack<string>();
                return;
            }

            // get parent suite id from stack
            var parentSuiteId = (_suiteIds.Count > 0) ? _suiteIds.Peek() : null;

            var requestNewSuite = new StartTestItemRequest
            {
                LaunchId = Bridge.Context.LaunchId,
                Name = testName.Name,
                StartTime = DateTime.UtcNow,
                Type = TestItemType.Suite
            };

            var beforeSuiteEventArg = new TestItemStartedEventArgs(Bridge.Service, requestNewSuite);
            if (BeforeSuiteStarted != null) BeforeSuiteStarted(this, beforeSuiteEventArg);
            if (!beforeSuiteEventArg.Canceled)
            {
                if (parentSuiteId != null)
                {
                    _suiteIds.Push(Bridge.Service.StartTestItem(parentSuiteId, requestNewSuite).Id);
                }
                else
                {
                    _suiteIds.Push(Bridge.Service.StartTestItem(requestNewSuite).Id);
                }

                if (AfterSuiteStarted != null)
                {
                    AfterSuiteStarted(this, new TestItemStartedEventArgs(Bridge.Service, requestNewSuite, _suiteIds.Peek()));
                }
            }
        }

        private string _testId;
        public string CurrentTestId { get { return _testId; } }

        public delegate void TestStartedHandler(object sender, TestItemStartedEventArgs e);
        public event TestStartedHandler BeforeTestStarted;
        public event TestStartedHandler AfterTestStarted;
        public void TestStarted(TestName testName)
        {
            if (Bridge.Context.LaunchId != null)
            {
                // get parent suite id from stack
                string parentSuiteId = (_suiteIds.Count > 0) ? _suiteIds.Peek() : null;

                var requestNewTest = new StartTestItemRequest
                    {
                        LaunchId = Bridge.Context.LaunchId,
                        Name = testName.Name,
                        StartTime = DateTime.UtcNow,
                        Type = TestItemType.Step
                    };

                var eventArg = new TestItemStartedEventArgs(Bridge.Service, requestNewTest);
                if (BeforeTestStarted != null) BeforeTestStarted(this, eventArg);
                if (!eventArg.Canceled)
                {
                    _testId = Bridge.Service.StartTestItem(parentSuiteId, requestNewTest).Id;
                    Bridge.Context.TestId = _testId;
                    if (AfterTestStarted != null) AfterTestStarted(this, new TestItemStartedEventArgs(Bridge.Service, requestNewTest, _testId));
                }
            }
        }

        private readonly bool _reportConsole = Configuration.ReportPortal.LogConsoleOutput;
        public void TestOutput(TestOutput testOutput)
        {
            if (_reportConsole && _testId != null)
            {
                Bridge.Service.AddLogItem(new AddLogItemRequest
                    {
                        TestItemId = _testId,
                        Time = DateTime.UtcNow,
                        Level = LogLevel.Info,
                        Text = testOutput.Text
                    });
            }
        }

        public delegate void TestFinishedHandler(object sender, TestItemFinishedEventArgs e);
        public event TestFinishedHandler BeforeTestFinished;
        public event TestFinishedHandler AfterTestFinished;
        public void TestFinished(TestResult result)
        {
            if (result.Message != null && _testId != null)
            {
                Bridge.Service.AddLogItem(new AddLogItemRequest
                    {
                        TestItemId = _testId,
                        Time = DateTime.UtcNow,
                        Level = LogLevel.Error,
                        Text = result.Message + "\n" + result.StackTrace
                    });
            }

            var requestUpdateTest = new UpdateTestItemRequest
            {
                Description = result.Description,
                Tags = (from object tag in result.Test.Categories select tag.ToString()).ToList()
            };
            Bridge.Service.UpdateTestItem(_testId, requestUpdateTest);

            var requestFinishTest = new FinishTestItemRequest
                {
                    EndTime = DateTime.UtcNow,
                    Status = _statusMap[result.ResultState]
                };

            var eventArg = new TestItemFinishedEventArgs(Bridge.Service, requestFinishTest, result, null, _testId);
            if (BeforeTestFinished != null) BeforeTestFinished(this, eventArg);
            if (!eventArg.Canceled)
            {
                var message = Bridge.Service.FinishTestItem(_testId, requestFinishTest).Info;
                
                if (AfterTestFinished != null) AfterTestFinished(this, new TestItemFinishedEventArgs(Bridge.Service, requestFinishTest, result, message, _testId));
                
                _testId = null;
                Bridge.Context.TestId = null;
            }
        }

        public delegate void SuiteFinishedHandler(object sender, TestItemFinishedEventArgs e);
        public event SuiteFinishedHandler BeforeSuiteFinished;
        public event SuiteFinishedHandler AfterSuiteFinished;
        public void SuiteFinished(TestResult result)
        {
            // finish the last suite in stack
            if (_suiteIds.Count != 0)
            {
                var requestFinishSuite = new FinishTestItemRequest
                    {
                        EndTime = DateTime.UtcNow,
                        Status = _statusMap[result.ResultState]
                    };
                var suiteId = _suiteIds.Pop();
                var eventArg = new TestItemFinishedEventArgs(Bridge.Service, requestFinishSuite, result, null, suiteId);
                if (BeforeSuiteFinished != null) BeforeSuiteFinished(this, eventArg);
                if (!eventArg.Canceled)
                {
                    var message = Bridge.Service.FinishTestItem(suiteId, requestFinishSuite).Info;
                    if (AfterSuiteFinished != null) AfterSuiteFinished(this, new TestItemFinishedEventArgs(Bridge.Service, requestFinishSuite, result, message, suiteId));
                }
            }
        }

        public delegate void RunFinishedHandler(object sender, RunFinishedEventArgs e);
        public event RunFinishedHandler BeforeRunFinished;
        public event RunFinishedHandler AfterRunFinished;
        public void RunFinished(TestResult result)
        {
            RunFinished();
        }

        public void RunFinished(Exception exception)
        {
            RunFinished();
        }

        private void RunFinished()
        {
            if (Bridge.Context.LaunchId != null)
            {
                var requestFinishLaunch = new FinishLaunchRequest
                {
                    EndTime = DateTime.UtcNow
                };

                var eventArg = new RunFinishedEventArgs(Bridge.Service, requestFinishLaunch, null, Bridge.Context.LaunchId);
                if (BeforeRunFinished != null) BeforeRunFinished(this, eventArg);
                if (!eventArg.Canceled)
                {
                    var message = Bridge.Service.FinishLaunch(Bridge.Context.LaunchId, requestFinishLaunch);
                    if (AfterRunFinished != null)
                    {
                        AfterRunFinished(this, new RunFinishedEventArgs(Bridge.Service, requestFinishLaunch, message.Info, Bridge.Context.LaunchId));
                    }
                }

                _suiteIds = null;
                Bridge.Context.LaunchId = null;
            }
        }

        public void UnhandledException(Exception exception)
        {

        }

        public bool Install(IExtensionHost host)
        {
            if (host == null)
            {
                return false;
            }
            var listeners = host.GetExtensionPoint("EventListeners");
            if (listeners == null)
            {
                return false;
            }
            if (InstalledAddin == null && Configuration.ReportPortal.Enabled)
            {
                listeners.Install(this);
                InstalledAddin = this;
            }

            return true;
        }
    }
}

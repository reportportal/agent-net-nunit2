using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ReportPortal.NUnit.EventArguments;
using ReportPortal.Client;
using ReportPortal.Client.Models;
using ReportPortal.Client.Requests;
using ReportPortal.Shared;
using NUnit.Core;
using NUnit.Core.Extensibility;
using System.Net;
using System.Threading;

namespace ReportPortal.NUnit
{
    [NUnitAddin(Type = ExtensionType.Core, Name = "EPAM Report Portal",
         Description = "Results synchronization with centralized system.")]
    public class ReportPortalAddin : EventListener, IAddin
    {
        public static ReportPortalAddin InstalledAddin { get; private set; }

        private readonly Dictionary<ResultState, Status> _statusMap = new Dictionary<ResultState, Status>();

        public ReportPortalAddin()
        {
            var uri = new Uri(Configuration.ReportPortal.Server.Url);
            var project = Configuration.ReportPortal.Server.Project;
            var password = Configuration.ReportPortal.Server.Authentication.Password;

            IWebProxy proxy = null;

            if (Configuration.ReportPortal.Server.Proxy.ElementInformation.IsPresent)
            {
                proxy = new WebProxy(Configuration.ReportPortal.Server.Proxy.Server);
            }

            Bridge.Service = proxy == null
                ? new Service(uri, project, password)
                : new Service(uri, project, password, proxy);

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
                Bridge.Context.LaunchReporter = new LaunchReporter(Bridge.Service);
                Bridge.Context.LaunchReporter.Start(requestNewLaunch);
                if (AfterRunStarted != null)
                    AfterRunStarted(this,
                        new RunStartedEventArgs(Bridge.Service, requestNewLaunch, Bridge.Context.LaunchReporter));
            }
        }

        private Stack<TestReporter> _suiteIds;

        public delegate void SuiteStartedHandler(object sender, TestItemStartedEventArgs e);

        public event SuiteStartedHandler BeforeSuiteStarted;
        public event SuiteStartedHandler AfterSuiteStarted;

        public void SuiteStarted(TestName testName)
        {
            // skip the first suite
            if (_suiteIds == null)
            {
                _suiteIds = new Stack<TestReporter>();
                return;
            }

            // get parent suite id from stack
            var parentSuiteId = (_suiteIds.Count > 0) ? _suiteIds.Peek() : null;

            var requestNewSuite = new StartTestItemRequest
            {
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
                    _suiteIds.Push(parentSuiteId.StartNewTestNode(requestNewSuite));
                }
                else
                {
                    _suiteIds.Push(Bridge.Context.LaunchReporter.StartNewTestNode(requestNewSuite));
                }

                if (AfterSuiteStarted != null)
                {
                    AfterSuiteStarted(this,
                        new TestItemStartedEventArgs(Bridge.Service, requestNewSuite, _suiteIds.Peek()));
                }
            }
        }

        private TestReporter _testId;

        public delegate void TestStartedHandler(object sender, TestItemStartedEventArgs e);

        public event TestStartedHandler BeforeTestStarted;
        public event TestStartedHandler AfterTestStarted;

        public void TestStarted(TestName testName)
        {
            if (Bridge.Context.LaunchReporter != null)
            {
                // get parent suite id from stack
                var parentSuiteId = (_suiteIds.Count > 0) ? _suiteIds.Peek() : null;

                var requestNewTest = new StartTestItemRequest
                {
                    Name = testName.Name,
                    StartTime = DateTime.UtcNow,
                    Type = TestItemType.Step
                };

                var eventArg = new TestItemStartedEventArgs(Bridge.Service, requestNewTest);
                if (BeforeTestStarted != null) BeforeTestStarted(this, eventArg);
                if (!eventArg.Canceled)
                {
                    _testId = parentSuiteId.StartNewTestNode(requestNewTest);

                    if (AfterTestStarted != null)
                        AfterTestStarted(this, new TestItemStartedEventArgs(Bridge.Service, requestNewTest, _testId));
                }
            }
        }

        private readonly bool _reportConsole = Configuration.ReportPortal.LogConsoleOutput;

        public void TestOutput(TestOutput testOutput)
        {
            if (_reportConsole && _testId != null)
            {
                _testId.Log(new AddLogItemRequest
                {
                    // TODO RP requires log time should be greater than test time
                    Time = DateTime.UtcNow.AddMilliseconds(1),
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
                _testId.Log(new AddLogItemRequest
                {
                    Time = DateTime.UtcNow.AddMilliseconds(1),
                    Level = LogLevel.Error,
                    Text = result.Message + "\n" + result.StackTrace
                });
            }

            if (_testId != null)
            {
                var requestUpdateTest = new UpdateTestItemRequest
                {
                    Description = result.Description,
                    Tags = (from object tag in result.Test.Categories select tag.ToString()).ToList()
                };
                _testId.Update(requestUpdateTest);

                var requestFinishTest = new FinishTestItemRequest
                {
                    EndTime = DateTime.UtcNow,
                    Status = _statusMap[result.ResultState]
                };

                var eventArg = new TestItemFinishedEventArgs(Bridge.Service, requestFinishTest, result, _testId);
                if (BeforeTestFinished != null) BeforeTestFinished(this, eventArg);
                if (!eventArg.Canceled)
                {
                    _testId.Finish(requestFinishTest);

                    if (AfterTestFinished != null)
                        AfterTestFinished(this,
                            new TestItemFinishedEventArgs(Bridge.Service, requestFinishTest, result, _testId));
                }
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
                var eventArg = new TestItemFinishedEventArgs(Bridge.Service, requestFinishSuite, result, suiteId);
                if (BeforeSuiteFinished != null) BeforeSuiteFinished(this, eventArg);
                if (!eventArg.Canceled)
                {
                    suiteId.Finish(requestFinishSuite);

                    if (AfterSuiteFinished != null)
                        AfterSuiteFinished(this,
                            new TestItemFinishedEventArgs(Bridge.Service, requestFinishSuite, result, suiteId));
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
            if (Bridge.Context.LaunchReporter != null)
            {
                var requestFinishLaunch = new FinishLaunchRequest
                {
                    EndTime = DateTime.UtcNow
                };

                var eventArg = new RunFinishedEventArgs(Bridge.Service, requestFinishLaunch,
                    Bridge.Context.LaunchReporter);
                if (BeforeRunFinished != null) BeforeRunFinished(this, eventArg);
                if (!eventArg.Canceled)
                {
                    Bridge.Context.LaunchReporter.Finish(requestFinishLaunch);

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    Console.WriteLine("Finishing to send results to Report Portal...");

                    try
                    {
                        Bridge.Context.LaunchReporter.FinishTask.Wait(TimeSpan.FromMinutes(30));
                    }
                    catch (Exception exp)
                    {
                        Console.WriteLine(exp);
                        throw;
                    }
                    
                    stopwatch.Stop();
                    Console.WriteLine($"Results are sent to Report Portal. Sync time: {stopwatch.Elapsed}");

                    if (AfterRunFinished != null)
                    {
                        AfterRunFinished(this, new RunFinishedEventArgs(Bridge.Service, requestFinishLaunch, Bridge.Context.LaunchReporter));
                    }
                }
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

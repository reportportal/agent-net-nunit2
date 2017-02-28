using System;
using ReportPortal.Client;
using ReportPortal.Client.Requests;
using NUnit.Core;
using ReportPortal.Shared;

namespace ReportPortal.NUnit.EventArguments
{
    public class TestItemFinishedEventArgs : EventArgs
    {
        public TestItemFinishedEventArgs(Service service, FinishTestItemRequest request, TestResult nUnitTestResult)
        {
            Service = service;
            TestItem = request;
            NUnitTestResult = nUnitTestResult;
        }

        public TestItemFinishedEventArgs(Service service, FinishTestItemRequest request, TestResult nUnitTestResult, TestReporter testReporter)
            : this(service, request, nUnitTestResult)
        {
            TestReporter = testReporter;
        }

        public Service Service { get; }

        public FinishTestItemRequest TestItem { get; }

        public TestResult NUnitTestResult { get; }

        public TestReporter TestReporter { get; }

        public bool Canceled { get; set; }
    }
}

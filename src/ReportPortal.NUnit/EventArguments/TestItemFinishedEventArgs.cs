using System;
using ReportPortal.Client;
using ReportPortal.Client.Requests;
using NUnit.Core;

namespace ReportPortal.NUnit.EventArguments
{
    public class TestItemFinishedEventArgs: EventArgs
    {
        private readonly Service _service;
        private readonly FinishTestItemRequest _request;
        private readonly TestResult _nUnitTestResult;
        private readonly string _message;
        private readonly string _id;
        public TestItemFinishedEventArgs(Service service, FinishTestItemRequest request, TestResult nUnitTestResult)
        {
            _service = service;
            _request = request;
            _nUnitTestResult = nUnitTestResult;
        }

        public TestItemFinishedEventArgs(Service service, FinishTestItemRequest request, TestResult nUnitTestResult, string message)
            :this(service, request, nUnitTestResult)
        {
            _message = message;
        }

        public TestItemFinishedEventArgs(Service service, FinishTestItemRequest request, TestResult nUnitTestResult, string message, string id)
            : this(service, request, nUnitTestResult, message)
        {
            _id = id;
        }

        public Service Service
        {
            get { return _service; }
        }

        public FinishTestItemRequest TestItem
        {
            get { return _request; }
        }

        public TestResult NUnitTestResult
        {
            get { return _nUnitTestResult; }
        }

        public string Message
        {
            get { return _message; }
        }

        public string Id
        {
            get { return _id; }
        }

        public bool Canceled { get; set; }
    }
}

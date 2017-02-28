using System;
using ReportPortal.Client;
using ReportPortal.Client.Requests;
using ReportPortal.Shared;

namespace ReportPortal.NUnit.EventArguments
{
    public class RunFinishedEventArgs : EventArgs
    {
        public RunFinishedEventArgs(Service service, FinishLaunchRequest request)
        {
            Service = service;
            Launch = request;
        }

        public RunFinishedEventArgs(Service service, FinishLaunchRequest request, LaunchReporter launchReporter)
            : this(service, request)
        {
            LaunchReporter = launchReporter;
        }

        public Service Service { get; }

        public FinishLaunchRequest Launch { get; }

        public LaunchReporter LaunchReporter { get; }

        public bool Canceled { get; set; }
    }
}

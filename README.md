[![Build status](https://ci.appveyor.com/api/projects/status/tbxdsfppppv14dfn?svg=true)](https://ci.appveyor.com/project/nvborisenko/agent-net-nunit2)

# Installation
Install **ReportPortal.NUnit** NuGet package into your project with tests.

[![NuGet version](https://badge.fury.io/nu/reportportal.nunit.svg)](https://badge.fury.io/nu/reportportal.nunit)
> PS> Install-Package ReportPortal.NUnit -Version 1.0.67

Note: Only ReportPortal.NUnit v1.\* is compatible with NUnit 2.6.4. If you use NUnit 3+ please refer ReportPortal v2.\*.

# Configuration
NuGet package installation adds *ReportPortal.NUnit.dll.config* file with configuration of the integration.

Example of config file:
```xml
<configuration>
  <configSections>
    <section name="reportPortal" type="ReportPortal.NUnit.ReportPortalSection, ReportPortal.NUnit, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
  </configSections>
  <reportPortal enabled="true" logConsoleOutput="true">
    <server url="https://rp.epam.com/api/v1/" project="default_project">
      <authentication username="default" password="aa19555c-c9ce-42eb-bb11-87757225d535" />
      <!-- <proxy server="host:port"/> -->
    </server>
    <launch name="NUnit Demo Launch" debugMode="true" tags="t1,t2" />
  </reportPortal>
</configuration>
```

# How to detect spikes in Azure cost using Anomaly Detector AI service

If your company uses Azure heavily you already have this problem - cost of consumed resources may increase at any moment due to different reasons:

- Manual or auto scale operation executed
- New set of resources is added
- Pricing tier of some resource changed to more expensive
- Unexpected increase of resources utilization
- Temporary created resources are not stopped/deleted
- … and many others.

Not always these additional expenses are expected, sometimes your bills are higher than they have to be because someone did not pay enough attention to costs or due to mistakes.

It is possible to manually check your Azure costs on a daily basis using Cost Analysis dashboards of Azure Portal. This process is time consuming and it is not always possible to spot a change in cost of certain resource. In order to automate this process, we will use Azure function that will be fired on schedule. The function will get Azure costs information from Cost management API, analyze changes for possible anomalies and write information about found issues. The information can then be checked by Azure Monitor alert rule and when abnormal change in consumption found – the alert will notify its recipients about the issue.

[Azure Anomaly detector](https://azure.microsoft.com/services/cognitive-services/anomaly-detector/) is one of [Cognitive Services](https://azure.microsoft.com/services/cognitive-services/) that follows &quot;democratized AI&quot; principal. Use of this AI powered service does not require from developers to be AI expert. It is available in form of API making intelligence accessible for every developer. It is also don&#39;t need any dedicated software/hardware so it can be easily added to any application.

In addition to main goal of implementation we will also see how to:

- use Azure Cost Management API
- use Azure Anomaly Detector service to analyze costs for spikes and dips
- setup Dependency Injection in Azure functions
- setup Managed Identity to authorize Azure function to access something
- configure alerts in Azure Monitor
- use Azure Configuration service together with Azure functions
- send metrics to Azure Application Insights
- run time triggered Azure function when debugging is starting

## Detection of anomalies in Azure cost

Current implementation of the function configured to check anomalies in cost per each consumed resource type for a subscription with configured id.

Anomaly Detector automatically selects the right anomaly detection algorithm to properly analyze changes in Azure cost. Besides spikes and dips, Anomaly Detector also detects many other kinds of anomalies, such as trend change and off-cycle softness, all in one single API endpoint.

The function will be able to alert you when consumption of certain type of resource (e.g. CosmosDB) is increased not only in obvious cases like this:

but also in more advanced situations:

If I was checking this manually, most likely I would not find any issue here. But the service figured out that there is a deviation in the data on weekends and detected anomaly for weekends and weekdays separately.



## Azure AD App Registration setup

The app registration required

## Alert setup

## How does it cost?

With Free Instance of Anomaly Detector service, you have 20000 transactions free per month. This number is high enough to execute the check a couple of times each day within whole month.

Standard instance will cost you $0.314 per 1000 transactions (the price may vary per region).

You can also run Anomaly Detection service also is distributed as Docker container and can be running on your own infrastructure. 

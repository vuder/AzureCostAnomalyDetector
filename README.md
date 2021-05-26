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

> **_NOTE:_** If you use AWS - you more lucky than me, Amazon has [service that will help you](https://aws.amazon.com/aws-cost-management/aws-cost-anomaly-detection/)

[Azure Anomaly detector](https://azure.microsoft.com/services/cognitive-services/anomaly-detector/) is one of [Cognitive Services](https://azure.microsoft.com/services/cognitive-services/) that follows &quot;democratized AI&quot; principal. Use of this AI powered service does not require from developers to be AI expert. It is available in form of API making intelligence accessible for every developer. It is also don&#39;t need any dedicated software/hardware so it can be easily added to any application.

![Alt text](pics/AnomalyDetector.png?raw=true "Architecture overview")

In addition to main goal of implementation we will also see how to:

- use Azure Cost Management API
- use Azure Anomaly Detector service to analyze costs for spikes and dips
- [setup Managed Identity to authorize Azure function to access App Configuration and KeyVault](#setup-managed-identity-to-authorize-azure-function-to-access-app-configuration-and-keyVault)
- [use Azure Configuration and KeyVault service in Azure functions](#use-azure-configuration-and-keyVault-service-in-azure-functions)
- [run time triggered Azure function manually](#run-time-triggered-azure-function-manually)
- [run time triggered Azure function when debugging is starting](#run-time-triggered-azure-function-when-debugging-is-starting)
- [send events and metrics with custom properties to Azure Application Insights](#send-events-and-metrics-with-custom-properties-to-azure-application-insights)
- [configure alerts with custom fields in Azure Monitor](#alert-setup)

See also:

- [limitations/feature of the implementation](#limitations-and-feature-of-the-implementation)
- [all function configuration parameters](#function-configuration-parameters)
- [how much does it cost](#how-much-does-it-cost)

## Detection of anomalies in Azure cost

Current implementation of the function configured to check anomalies in cost per each consumed resource type for a subscription with configured id.

Anomaly Detector automatically selects the right anomaly detection algorithm to properly analyze changes in Azure cost. Besides spikes and dips, Anomaly Detector also detects many other kinds of anomalies, such as trend change and off-cycle softness, all in one single API endpoint.

The function will be able to alert you when consumption of certain type of resource (e.g. CosmosDB) is increased not only in obvious cases like this:

![Alt text](pics/Simple%20Spike.png?raw=true "Simple cost spike detected")

or

![Alt text](pics/Simple%20Spike%202.png?raw=true "Simple cost spike detected 2")

but also in more advanced situations:

![Alt text](pics/weekends%20anomaly.png?raw=true "Advanced cost spike detected")

If I was checking this manually, most likely I would not find any issue here. But the service figured out that there is a deviation in the data on weekends and detected anomalies for weekends and weekdays separately.

## Setup Managed Identity to authorize Azure function to access App Configuration and KeyVault

The function getting all needed configuration values from Azure App Configuration service. The service also provides references to Azure Key Vault where required by the function secrets are stored. So the function have to be authorized to read data from both AppConfiguration and KeyVault. Best way to configure this access is to use Managed Identity:

1. Create Managed identity for the function app(Function App>Identity>System Assigned>Status:On)

1. Grant read access for the identity to AppConfiguration(App Configuration>Access Control>Role Assignments>Add>Add Role Assignment). Grant "App Configuration Data Reader" role to the created function app identity.

1. Grant read access to secrets for the identity to Key Vault

### How to run the function locally and being authorized to access App Configuration and KeyVault

1. Setup Azure AD App Registration with "Web" platform, generate client secret

1. Grant access permissions to the App Registration in App Configuration and Key Vault similarly to previously created Managed Identity

1. Set values of following environment variables on your local machine using new App Registration info:
    - AZURE_CLIENT_ID
    - AZURE_CLIENT_SECRET
    - AZURE_TENANT_ID

    For Mac OS use [EnvPane](https://github.com/hschmidt/EnvPane)

1. No changes in code of the function are needed - DefaultAzureCredential class will take care to find available authorization mechanism and it will work with both Managed Identity and connect using App Registration using the environment variables

## Use Azure Configuration and KeyVault service in Azure functions

The function uses Azure App Configuration service as a source of configuration parameters and Azure KeyVault as a store for secrets.
The only parameter that should be passed to the function is url to the App Configuration service:

```cs
 var appConfigConnection = Environment.GetEnvironmentVariable("AppConfigurationConnectionString");
```

that is set in Configuration of the function application:

![Alt text](pics/FunctionSettings.png?raw=true "Azure function Application configuration")

then the function is setup to obtain configuration from the service:

```cs
      builder.AddAzureAppConfiguration(options => options.Connect(new Uri(appConfigConnection), credentials)
                                                       .ConfigureKeyVault(kv =>
                                                       {
                                                           kv.SetCredential(credentials);
                                                       }));
```

using credential that are automatically provided for Managed Identity configured early:

```cs
var credentials = new DefaultAzureCredential();
```

Secrets are stored in KeyVault and there are references to these secrets in App Configuration:
![Alt text](pics/AppConfigSecrets.png?raw=true "AppConfiguration links to KeyVault secrets")

## Send events and metrics with custom properties to Azure Application Insights

When a cost anomaly detected the function sends event to Application Insights with all information needed to identify the root cause of the spike.
First TelemetryClient need to be initialized:

```cs
            var appInsightsInstrumentationKey = configuration["AzureCostAnomalyDetector.AppInsightsInstrumentationKey"];
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration(appInsightsInstrumentationKey));
```

then event with custom properties is sent to AppInsights:

```cs
            var anomalyEvent = new EventTelemetry("Azure cost anomaly");
            anomalyEvent.Properties["Resource Type"] = resourceType;
            anomalyEvent.Properties["Anomaly Cost"] = value.ToString(CultureInfo.InvariantCulture);
            anomalyEvent.Properties["Date"] = date.ToShortDateString();
            anomalyEvent.Properties["Detection Type"] = anomalyType.ToString();            
            _telemetryClient.TrackEvent(anomalyEvent);
```

The events will be used by Azure Monitor to send alerts notification.
In addition metric is sent to AppInsights:

```cs
            var metric = new MetricTelemetry("Number of Azure cost anomalies detected", 1)
            {
                Timestamp = DateTimeOffset.Now, 
                MetricNamespace = "Custom monitoring"
            };
            
            metric.Properties["Detection Type"] = anomalyType.ToString();
            metric.Properties["Resource Type"] = resourceType;
            _telemetryClient.TrackMetric(metric);
            _telemetryClient.Flush();
```

The metric can be used to display the anomalies detections on graphs/dashboards. Properties of the metrics will be available as custom metric dimensions in Azure Monitor.

The metric can also be used to configure alerts, it is even possible to setup alerting on custom metric dimensions. You have to check that "Enable alerting on custom metric dimensions" setting of your Application Insights instance is enabled:
![Alt text](pics/Enable%20alerting%20on%20custom%20metric%20dimensions.png?raw=true "Enable alerting on custom metric dimensions")

## Alert setup

Alert rule is configured in Azure Monitor using following query:

```code
 let todayEvents=customEvents 
| where name == "Azure cost anomaly"
| where startofday(timestamp) == startofday(now())
| summarize countToday=count() by tostring(customDimensions.DetectionId), todayDetectionId = tostring(customDimensions.DetectionId)
| project countToday, todayDetectionId;
let lastEvents=customEvents 
| where timestamp>ago(15m)
| summarize countLast=count() by tostring(customDimensions.DetectionId), lastDetectionId = tostring(customDimensions.DetectionId), Type=tostring(customDimensions.["Detection Type"]), Resource=tostring(customDimensions.["Resource Type"]), Date=tostring(customDimensions.Date), Cost=tostring(customDimensions.["Anomaly Cost"])
| project lastDetectionId, countLast, Date, Type, Resource, Cost;
lastEvents
| join todayEvents on $left.lastDetectionId == $right.todayDetectionId
| where countToday <= countLast
| project Date, Type, Resource, Cost
| order by Cost
```

[AzureMonitorAlertLogQuery.kql](Scripts/AzureMonitorAlertLogQuery.kql)

![Alt text](pics/AlertRuleSignalConfiguration.png?raw=true "Alert rule signal configuration")

The query looks a bit more complex that it should be. The reason is that I wanted to deduplicate alerts and be notified only once a day if any anomaly found, no matter how many times the issue is reported.

In order to include into the alert notification information about the exact resource, abnormal cost and detection type following additional alert rule configuration needed:

![Alt text](pics/AlertCustomJsonPayload.png?raw=true "Alert rule custom json payload configuration")

Then you will be able to see in the alert (in email for example) this:

![Alt text](pics/AdditionalInfoInAlertNotification.png?raw=true "Anomaly detection info in alert notification")

## Run time triggered Azure function manually

Call the following administrator endpoint to trigger any kind of non-HTTP functions including time triggered:

```code
[POST] http://localhost:7071/admin/functions/AzureCostAnomalyDetector
```

To pass test data set JSON body of the request(optional):

```json
{
    "input": "<trigger_input>"
}
```

## Run time triggered Azure function when debugging is starting

Following code template allow to run timer triggered function on debug start and you do not need to play with any workarounds(.Net Core 3.x.x).

```cs
        [FunctionName("AzureCostAnomalyDetector")]
        public static async Task Run([TimerTrigger("0 0 17-23 * * *"
            #if DEBUG 
                , RunOnStartup = true
            #endif
                ) ]TimerInfo myTimer, ILogger logger)
        {
          
        }
```

## Limitations and feature of the implementation

Azure cost management does not provides information about consumed resources in real time. Usually the cost becomes available for evaluation in second half of next day. So it make sense to wait until the cost data become available snd then run the function - now the time trigger is configured to run hourly between 17:00 and 23:00.

## Function configuration parameters

Parameter name | Description | Recommended value
--- | --- | ---
AzureCostAnomalyDetector.AnomalyDetectorCheckDaysBack|Number od days back from current date for Azure cost anomaly check|1
AzureCostAnomalyDetector.AnomalyDetectorCheckPeriod|The time period of data used for Azure cost anomaly detection. Possible value: n days, n week, n month,n year, n years. where n is a number|3 Month
AzureCostAnomalyDetector.AnomalyDetectorEndpoint|URL to Anomaly Detector service used for Azure cost monitoring|*provide your url*
AzureCostAnomalyDetector.AnomalyDetectorReportDropsInCost|The value is used to determine whether report dips in cost in addition to spikes by Anomaly Detector|false
AzureCostAnomalyDetector.CostAlertThreshold|Threshold in $ of changes in cost that will be ignored by Azure Costs Anomaly Detector function|0.5
Ad-AzureTenantId|Azure AD tenant Id. The Id is needed to format URL that is used to get access token for Cost Management API|*provide the tenant id*
AzureCostAnomalyDetector-AnomalyDetectorKey|Access Key of Anomaly Detector service used for Azure cost monitoring| *provide the key*
AzureCostAnomalyDetector-AppInsightsInstrumentationKey|AppInsights InstrumentationKey. The key is needed to authorize requests to send info about detected anomalies| *provide the key*
AzureCostAnomalyDetector-AzureAppRegistrationClientId|Client Id of App Registration used by the Azure Cost Monitor function|*prove client id*
AzureCostAnomalyDetector-AzureAppRegistrationClientSecret|Client secret of App Registration used by the Azure Cost Monitor function|*provide the secret*
AzureCostAnomalyDetector-AzureSubscriptionId|Id of Azure subscription where App Registration for the Azure Cost Monitor function located|*provide the id*

## How much does it cost?

With Free Instance of Anomaly Detector service, you have 20000 transactions free per month. This number is high enough to execute the check a couple of times each day within whole month.

Standard instance will cost you $0.314 per 1000 transactions (the price may vary per region).

You can also run Anomaly Detection service also is distributed as Docker container and can be running on your own infrastructure.

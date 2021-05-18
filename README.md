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

![Alt text](pics/AnomalyDetector.png?raw=true "Architecture overview")

In addition to main goal of implementation we will also see how to:

- use Azure Cost Management API
- use Azure Anomaly Detector service to analyze costs for spikes and dips
- [setup Managed Identity to authorize Azure function to access App Configuration and KeyVault](#setup-managed-identity-to-authorize-azure-function-to-access-app-configuration-and-keyVault)
- [use Azure Configuration and KeyVault service in Azure functions](#use-azure-configuration-and-keyVault-service-in-azure-functions)
- [run time triggered Azure function when debugging is starting](#run-time-triggered-azure-function-when-debugging-is-starting)
- [send events and metrics with custom properties to Azure Application Insights](#send-events-and-metrics-with-custom-properties-to-azure-application-insights)
- [configure alerts with custom fields in Azure Monitor](#alert-setup)
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

## Run time triggered Azure function when debugging is starting

Following code template allow to run timer triggered function on debug start and you do not need to play with any workarounds.

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

## How much does it cost?

With Free Instance of Anomaly Detector service, you have 20000 transactions free per month. This number is high enough to execute the check a couple of times each day within whole month.

Standard instance will cost you $0.314 per 1000 transactions (the price may vary per region).

You can also run Anomaly Detection service also is distributed as Docker container and can be running on your own infrastructure.

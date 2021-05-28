using System;
using System.Globalization;
using System.Threading.Tasks;
using Azure.Identity;
using AzureCostAnomalyDetector.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AzureCostAnomalyDetector.AzureFunc
{
    public static class AzureCostAnomalyDetectorApp
    {
        private static readonly string _anomalyDetectorEndpoint;
        private static readonly string _anomalyDetectorKey;
        private static readonly double _costAlertThreshold;
        private static readonly string _period;
        private static readonly int _daysBackToCheck;
        private static readonly bool _reportDrops;
        private static readonly string _azureTenantId;
        private static readonly string _azureSubscriptionId;
        private static readonly string _azureAppRegistrationClientSecret;
        private static readonly string _azureAppRegistrationClientId;
        private static readonly TelemetryClient _telemetryClient;

        static AzureCostAnomalyDetectorApp()
        {
            var builder = new ConfigurationBuilder();
            var appConfigConnection = Environment.GetEnvironmentVariable("AppConfigurationConnectionString");
            if (appConfigConnection == null)
            {
                throw new ArgumentNullException("App Configuration servic url is not provided");
            }
            var credentials = new DefaultAzureCredential();

            builder.AddAzureAppConfiguration(options => options.Connect(new Uri(appConfigConnection), credentials)
                                                       .ConfigureKeyVault(kv =>
                                                       {
                                                           kv.SetCredential(credentials);
                                                       }));
            var configuration = builder.Build();

            //endpoint and key of Azure Anomaly Detector service.
            _anomalyDetectorEndpoint = configuration["AzureCostAnomalyDetector.AnomalyDetectorEndpoint"];
            _anomalyDetectorKey = configuration["AzureCostAnomalyDetector.AnomalyDetectorKey"];

            //Threshold in cost difference that will be ignored and no alerts will be sent if the difference is within the threshold. Example: 0.5
            _costAlertThreshold = double.Parse(configuration["AzureCostAnomalyDetector.CostAlertThreshold"]);
            //Period of time for query of Azure cost consumption. Examples: 3 month, 90 days, 1 year
            _period = configuration["AzureCostAnomalyDetector.AnomalyDetectorCheckPeriod"];
            //Number of days back from current date used to determine date for anomaly detection. Example: 2.
            _daysBackToCheck = int.Parse(configuration["AzureCostAnomalyDetector.AnomalyDetectorCheckDaysBack"]);
            //The value is used to determine whether report drops in cost in addition to spikes
            _reportDrops = bool.Parse(configuration["AzureCostAnomalyDetector.AnomalyDetectorReportDropsInCost"]);

            _azureTenantId = configuration["Ad.AzureTenantId"];
            _azureSubscriptionId = configuration["AzureCostAnomalyDetector.AzureSubscriptionId"];
            //credentials of Azure AD App Registration created for the function.
            _azureAppRegistrationClientId = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientId"];
            _azureAppRegistrationClientSecret = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientSecret"];

            var appInsightsInstrumentationKey = configuration["AzureCostAnomalyDetector.AppInsightsInstrumentationKey"];
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration(appInsightsInstrumentationKey));
        }

        [FunctionName("AzureCostAnomalyDetector")]
        public static async Task Run([TimerTrigger("0 0 17-23 * * *"
            #if DEBUG 
               , RunOnStartup = true
            #endif
                ) ]TimerInfo myTimer, ILogger logger)
        {
            logger.LogInformation($"Trigger function execution at {DateTime.Now}");

            var costRetriever = new AzureCostRetrieverService(_azureAppRegistrationClientId, _azureAppRegistrationClientSecret, _azureTenantId, logger);


            var dayToCheck = DateTime.UtcNow.AddDays(-1 * Math.Abs(_daysBackToCheck));
            var detectionContext = new LastDayDetectionContext(
                dayToCheck,
                _period,
                _azureSubscriptionId,
                _costAlertThreshold,
                _reportDrops,
                onAnomalyDetected: (anomalyType, resourceType, date, anomalyValue) =>
                    {
                        var value = Math.Round(anomalyValue, 2);
                        ReportEvent(resourceType, value, date, anomalyType);
                        logger.LogError($"Anomaly for {resourceType} value ${value} at {date.ToShortDateString()}");
                    },
                onNotEnoughValues: (resourceType) =>
                    {
                        logger.LogWarning($"Not enough values: {resourceType}");
                    }
                );

            await new AnomalyDetectorService(_anomalyDetectorEndpoint, _anomalyDetectorKey, costRetriever).DetectForLastDay(detectionContext);
        }

        private static void ReportEvent(string resourceType, double value, DateTime date, AzureCostAnomalyType anomalyType)
        {
            var anomalyEvent = new EventTelemetry("Azure cost anomaly");
            anomalyEvent.Properties["Resource Type"] = resourceType;
            anomalyEvent.Properties["Anomaly Cost"] = value.ToString(CultureInfo.InvariantCulture);
            anomalyEvent.Properties["Date"] = date.ToShortDateString();
            anomalyEvent.Properties["Detection Type"] = anomalyType.ToString();
            //The id is used to deduplicate alerts.
            //An anomaly of same type detected at same date, for same resource type could be spot by checking the id - it will be the same regardless of number of generated events and their timestamps.
            anomalyEvent.Properties["DetectionId"] = $"{resourceType}-{date.ToShortDateString()}-{anomalyType}";

            _telemetryClient.TrackEvent(anomalyEvent);

            var metric = new MetricTelemetry("Number of Azure cost anomalies detected", 1)
            {
                Timestamp = DateTimeOffset.Now,
                MetricNamespace = "Custom monitoring"
            };

            metric.Properties["Detection Type"] = anomalyType.ToString();
            metric.Properties["Resource Type"] = resourceType;
            _telemetryClient.TrackMetric(metric);
            _telemetryClient.Flush();
        }
    }
}

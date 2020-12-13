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
        private static readonly string _azureTenantId;
        private static readonly string _azureSubscriptionId;
        private static readonly string _azureAppRegistrationClientSecret;
        private static readonly string _azureAppRegistrationClientId;
        private static readonly TelemetryClient _telemetryClient;

        static AzureCostAnomalyDetectorApp()
        {
            var builder = new ConfigurationBuilder();
            var appConfigConnection = Environment.GetEnvironmentVariable("AppConfigurationConnectionString");
            var credentials = new DefaultAzureCredential();

            builder.AddAzureAppConfiguration(options => options.Connect(new Uri(appConfigConnection), credentials)
                                                       .ConfigureKeyVault(kv =>
                                                       {
                                                           kv.SetCredential(credentials);
                                                       }));
            var configuration = builder.Build();

            _anomalyDetectorEndpoint = configuration["AzureCostAnomalyDetector.AnomalyDetectorEndpoint"];
            _anomalyDetectorKey = configuration["AzureCostAnomalyDetector.AnomalyDetectorKey"];

            _costAlertThreshold = double.Parse(configuration["AzureCostAnomalyDetector.CostAlertThreshold"]);
            _period = configuration["AzureCostAnomalyDetector.AnomalyDetectorCheckPeriod"];
            _daysBackToCheck = int.Parse(configuration["AzureCostAnomalyDetector.AnomalyDetectorCheckDaysBack"]);


            _azureTenantId = configuration["Ad.AzureTenantId"];
            _azureSubscriptionId = configuration["AzureCostAnomalyDetector.AzureSubscriptionId"];
            _azureAppRegistrationClientSecret = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientSecret"];
            _azureAppRegistrationClientId = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientId"];

            var appInsightsInstrumentationKey = configuration["AzureCostAnomalyDetector.AppInsightsInstrumentationKey"];
            _telemetryClient = new TelemetryClient(new TelemetryConfiguration(appInsightsInstrumentationKey));
        }

        [FunctionName("AzureCostAnomalyDetector")]
        public static async Task Run([TimerTrigger("0 0 6 * * *", 
            #if DEBUG 
                RunOnStartup = true
            #endif
                ) ]TimerInfo myTimer, ILogger logger)
        {
            logger.LogInformation($"Trigger function execution at {DateTime.Now}");

            var costRetriever = new AzureCostRetrieverService(_azureAppRegistrationClientId, _azureAppRegistrationClientSecret, _azureTenantId, logger);

            DateTime dayToCheck = DateTime.UtcNow.AddDays(_daysBackToCheck);
            var detectionContext = new LastDayDetectionContext(
                dayToCheck,
                _period,
                _azureSubscriptionId,
                _costAlertThreshold,
                onAnomalyDetected: (anomalyType, resourceType, date, anomalyValue) =>
                    {
                        var value = Math.Round(anomalyValue, 2);
                        var anomalyEvent = new EventTelemetry("Azure cost anomaly");
                        anomalyEvent.Properties["Resource Type"] = resourceType;
                        anomalyEvent.Properties["Anomaly Cost"] = value.ToString(CultureInfo.InvariantCulture);
                        anomalyEvent.Properties["Date"] = date.ToShortDateString();
                        anomalyEvent.Properties["Detection Type"] = anomalyType.ToString();
                        _telemetryClient.TrackEvent(anomalyEvent);
                        _telemetryClient.Flush();
                        logger.LogError($"Anomaly for {resourceType} value ${value} at {date.ToShortDateString()}");
                    },
                onNotEnoughValues: (resourceType) =>
                    {
                        logger.LogWarning($"Not enough values: {resourceType}");
                    }
                );

            await new AnomalyDetectorService(_anomalyDetectorEndpoint, _anomalyDetectorKey, costRetriever).DetectForLastDay(detectionContext);
        }
    }
}

using System;
using System.Threading.Tasks;
using Azure.Identity;
using AzureCostAnomalyDetector.Common;
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
        private static readonly string _azureTenantId;
        private static readonly string _azureSubscriptionId;
        private static readonly string _azureAppRegistrationClientSecret;
        private static readonly string _azureAppRegistrationClientId;

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


            _azureTenantId = configuration["Ad.AzureTenantId"];
            _azureSubscriptionId = configuration["AzureCostAnomalyDetector.AzureSubscriptionId"];
            _azureAppRegistrationClientSecret = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientSecret"];
            _azureAppRegistrationClientId = configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientId"];
            
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

            var detectionContext = new LastDayDetectionContext(
                DateTime.UtcNow.AddDays(-2),
                _period,
                _azureSubscriptionId,
                _costAlertThreshold,
                onAnomalyDetected: (resourceType, date, anomalyValue) =>
                    {
                        var value = Math.Round(anomalyValue, 2);
                        logger.LogInformation($"Anomaly for {resourceType} value ${value} at {date.ToShortDateString()}");
                    },
                onNotEnoughValues: (resourceType) =>
                    {
                        logger.LogInformation($"Not enough values: {resourceType}");
                    }
                );

            await new AnomalyDetectorService(_anomalyDetectorEndpoint, _anomalyDetectorKey, costRetriever).DetectForLastDay(detectionContext);
        }
    }
}

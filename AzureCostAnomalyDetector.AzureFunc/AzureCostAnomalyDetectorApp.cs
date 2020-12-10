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
        private static readonly IConfiguration _configuration;
        private static readonly string _anomalyDetectorEndpoint;
        private static readonly string _anomalyDetectorKey;
        private static readonly double _costAlertThreshold;
        private static readonly string _period;
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
            _configuration = builder.Build();

            _anomalyDetectorEndpoint = _configuration["AzureCostAnomalyDetector.AnomalyDetectorEndpoint"];
            _anomalyDetectorKey = _configuration["AzureCostAnomalyDetector.AnomalyDetectorKey"];

            _costAlertThreshold = double.Parse(_configuration["AzureCostAnomalyDetector.CostAlertThreshold"]);
            _period = _configuration["AzureCostAnomalyDetector.AnomalyDetectorCheckPeriod"];

            _azureSubscriptionId = _configuration["AzureCostAnomalyDetector.AzureSubscriptionId"];
            _azureAppRegistrationClientSecret = _configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientSecret"];
            _azureAppRegistrationClientId = _configuration["AzureCostAnomalyDetector.AzureAppRegistrationClientId"];
        }

        [FunctionName("AzureCostAnomalyDetector")]
        public static async Task Run([TimerTrigger("0 0 6 * * *", 
            #if DEBUG 
                RunOnStartup = true
            #endif
                ) ]TimerInfo myTimer, ILogger logger)
        {
            logger.LogInformation($"Trigger function execution at {DateTime.Now}");

            var costRetriever = new AzureCostRetrieverService(_azureAppRegistrationClientId, _azureAppRegistrationClientSecret, _azureSubscriptionId);

            var detectionContext = new LastDayDetectionContext(
                DateTime.UtcNow.AddDays(-2),
                _period,
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

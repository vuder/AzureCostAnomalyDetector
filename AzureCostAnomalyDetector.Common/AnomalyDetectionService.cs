using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.AnomalyDetector;
using Microsoft.Azure.CognitiveServices.AnomalyDetector.Models;

namespace AzureCostAnomalyDetector.Common
{
    public class AnomalyDetectorService
    {
        private IAnomalyDetectorClient _anomalyDetectorClient;
        private readonly string _endpoint;
        private readonly string _key;
        private readonly AzureCostRetrieverService _costRetriever;

        public AnomalyDetectorService(string endpoint, string key, AzureCostRetrieverService costRetriever)
        {
            _endpoint = endpoint;
            _key = key;
            _costRetriever = costRetriever;
        }

        public async Task DetectForLastDay(LastDayDetectionContext lastDayDetectionContext)
        {
            CreateAnomalyDetectorClient();
            var costGroupings = await CostAzureCostGroupedByResourceType(lastDayDetectionContext.DayToCheck, lastDayDetectionContext.PeriodDays, lastDayDetectionContext.SubscriptionId);

            foreach (var costGrouping in costGroupings)
            {
                (bool detectionPossible, string azureResource, Request orderedCostRequest) = TryPrepareDataForAnomalyDetection(lastDayDetectionContext, costGrouping);
                if (detectionPossible)
                {
                    var result = await DetectAnomalies(orderedCostRequest);
                    ReportAnomalies(lastDayDetectionContext, orderedCostRequest, result, azureResource);
                }
            }
        }

        private void CreateAnomalyDetectorClient()
        {
            if (_anomalyDetectorClient != null) { return; }
            _anomalyDetectorClient = new AnomalyDetectorClient(new ApiKeyServiceClientCredentials(_key))
            {
                Endpoint = _endpoint
            };
        }

        private static (bool, string, Request) TryPrepareDataForAnomalyDetection(LastDayDetectionContext lastDayDetectionContext, IGrouping<string, AzureCost> costGrouping)
        {
            var azureResource = costGrouping.Key;

            var orderedCost = costGrouping.OrderBy(rec => rec.Date).ToArray();
            var orderedCostRequest = new Request(orderedCost.Select(rec => rec.Point).ToList(), Granularity.Daily) { Sensitivity = 65 };

            //12 is the minimum number of data points that Anomaly Detector API accept
            if (orderedCost.Length < 12)
            {
                var lastCost = orderedCost.Last();
                //If the resource is just created or billed less then 12 times - it should be reported when both:
                //   - It's cost higher then costAlertThreshold
                //   - It's last known date returned from Azure Cost Management is the date specified in lastDay variable
                if (lastCost.Amount > lastDayDetectionContext.CostAlertThreshold && lastCost.Date == lastDayDetectionContext.DayToCheck)
                {
                    lastDayDetectionContext.OnAnomalyDetected(azureResource, lastCost.Date, lastCost.Amount);
                }
                else
                {
                    lastDayDetectionContext.OnNotEnoughValues(azureResource);
                }
                return (false, azureResource, null);
            }

            return (true, azureResource, orderedCostRequest);
        }

        private async Task<IEnumerable<IGrouping<string, AzureCost>>> CostAzureCostGroupedByResourceType(DateTime dayToCheck, int periodDays, string subscriptionId)
        {
            var azureCost = await _costRetriever.GetAzureCosts(periodDays, dayToCheck, subscriptionId);
            var costGroupings = azureCost.GroupBy(point => point.Name);
            return costGroupings;
        }

        private async Task<LastDetectResponse> DetectAnomalies(Request orderedCostRequest)
        {
            LastDetectResponse result = await _anomalyDetectorClient.LastDetectAsync(orderedCostRequest).ConfigureAwait(false);
            return result;
        }

        private static void ReportAnomalies(LastDayDetectionContext lastDayDetectionContext, Request orderedCostRequest,
            LastDetectResponse result, string azureResource)
        {
            var lastPoint = orderedCostRequest.Series.Last();
            //The anomaly must be reported only if it is detected at the specified in lastDay variable, but not is last known date with cost returned from Azure Cost Management
            if (result.IsAnomaly && lastPoint.Value > lastDayDetectionContext.CostAlertThreshold &&
                lastPoint.Timestamp == lastDayDetectionContext.DayToCheck)
            {
                lastDayDetectionContext.OnAnomalyDetected(azureResource, lastPoint.Timestamp, lastPoint.Value);
            }
        }
    }
}
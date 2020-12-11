using System;

namespace AzureCostAnomalyDetector.Common
{
    public class LastDayDetectionContext
    {
        public DateTime DayToCheck { get; }
        public string Period { get; }
        public string SubscriptionId { get; }
        public double CostAlertThreshold { get; }
        public Action<string, DateTime, double> OnAnomalyDetected { get; }
        public Action<string> OnNotEnoughValues { get; }

        public LastDayDetectionContext(DateTime dayToCheck, string period, string subscriptionId, double costAlertThreshold, Action<string, DateTime, double> onAnomalyDetected, Action<string> onNotEnoughValues)
        {
            DayToCheck = dayToCheck;
            Period = period;
            SubscriptionId = subscriptionId;
            CostAlertThreshold = costAlertThreshold;
            OnAnomalyDetected = onAnomalyDetected;
            OnNotEnoughValues = onNotEnoughValues;
        }
    }
}
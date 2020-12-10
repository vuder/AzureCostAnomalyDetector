using System;

namespace AzureCostAnomalyDetector.Common
{
    public class LastDayDetectionContext
    {
        public DateTime DayToCheck { get; }
        public string Period { get; }
        public double CostAlertThreshold { get; }
        public Action<string, DateTime, double> OnAnomalyDetected { get; }
        public Action<string> OnNotEnoughValues { get; }

        public LastDayDetectionContext(DateTime dayToCheck, string period, double costAlertThreshold, Action<string, DateTime, double> onAnomalyDetected, Action<string> onNotEnoughValues)
        {
            DayToCheck = dayToCheck;
            Period = period;
            CostAlertThreshold = costAlertThreshold;
            OnAnomalyDetected = onAnomalyDetected;
            OnNotEnoughValues = onNotEnoughValues;
        }
    }
}
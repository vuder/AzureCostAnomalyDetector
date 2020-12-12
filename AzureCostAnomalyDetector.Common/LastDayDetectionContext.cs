using System;
using System.Text.RegularExpressions;

namespace AzureCostAnomalyDetector.Common
{
    public class LastDayDetectionContext
    {
        private static readonly Regex _periodPatternRegex = new Regex("(?<num>[0-9]*) ?(?<word>[A-Za-z]*)", RegexOptions.Compiled);
        
        public DateTime DayToCheck { get; }
        public string Period { get; }
        public int PeriodDays { get; }
        public string SubscriptionId { get; }
        public double CostAlertThreshold { get; }
        public Action<string, DateTime, double> OnAnomalyDetected { get; }
        public Action<string> OnNotEnoughValues { get; }

        public LastDayDetectionContext(DateTime dayToCheck, string period, string subscriptionId, double costAlertThreshold, Action<string, DateTime, double> onAnomalyDetected, Action<string> onNotEnoughValues)
        {
            DayToCheck = dayToCheck.Date;
            Period = period;
            PeriodDays = GetDaysOffset(period);
            SubscriptionId = subscriptionId;
            CostAlertThreshold = costAlertThreshold;
            OnAnomalyDetected = onAnomalyDetected;
            OnNotEnoughValues = onNotEnoughValues;
        }

        private static int GetDaysOffset(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) { return 90; }
            var match = _periodPatternRegex.Match(period);
            if (!match.Success) { return 90; }
            int num = int.Parse(match.Groups["num"].Value);
            string interval = match.Groups["word"].Value.ToLower().Trim();
            int intervalNum = 1;
            switch (interval)
            {
                case "week": { intervalNum = 7; break; }
                case "weeks": { intervalNum = 7; break; }
                case "month": { intervalNum = 30; break; }
                case "year": { intervalNum = 365; break; }
                case "years": { intervalNum = 365; break; }
            }
            return num * intervalNum;
        }
    }
}
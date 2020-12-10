using System;
using Microsoft.Azure.CognitiveServices.AnomalyDetector.Models;

namespace AzureCostAnomalyDetector.Common
{
    public class AzureCost
    {
        public double Amount { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public Point Point => new Point(Date, Amount);
    }
}
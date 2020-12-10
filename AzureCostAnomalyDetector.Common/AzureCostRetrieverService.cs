using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AzureCostAnomalyDetector.Common
{
    public class AzureCostRetrieverService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _subscriptionId;
        private static readonly HttpClient _httpClient = new HttpClient();

        public AzureCostRetrieverService(string clientId, string clientSecret, string subscriptionId)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _subscriptionId = subscriptionId;
        }

        public async Task<IEnumerable<AzureCost>> GetAzureCosts(string period, DateTime lastDay)
        {
            int daysBack = GetDaysOffset(period);
            var dateFrom = lastDay.AddDays(-1 * daysBack).ToString("yyyy-MM-dd");
            var dateTo = lastDay.ToString("yyyy-MM-dd");
            var content = new StringContent(@"
            {   
                'type': 'Usage',   
                'timeframe': 'Custom', 
                'timePeriod' : {
                        'from': '" + dateFrom + @"T00:00:00+00:00',
                        'to': '" + dateTo + @"T23:59:59+00:00'
                    },
                'dataset': {
                    'granularity': 'Daily',
                    'aggregation': {
                       'totalCost': {
                             'name': 'PreTaxCost',
                             'function': 'Sum'
                      }
                    },
                    'grouping': [
                      {
                        'type': 'Dimension',
                        'name': 'ResourceType'
                      }
                    ]
                }
            }",
            Encoding.UTF8,
            "application/json");

            string token = await GetAccessToken();

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var nextLink = $"https://management.azure.com/subscriptions/{_subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2019-11-01";

            List<AzureCost> result = new();

            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                Console.WriteLine($"Loading data from azure: {nextLink}");
                var response = await _httpClient.PostAsync(nextLink, content);
                var respStr = await response.Content.ReadAsStringAsync();
                dynamic o = JsonConvert.DeserializeObject(respStr);

                foreach (var rec in o.properties.rows)
                {
                    result.Add(new AzureCost
                    {
                        Amount = rec[0],
                        Date = DateTime.ParseExact((string)rec[1], "yyyyMMdd", CultureInfo.InvariantCulture),
                        Name = rec[2]
                    });
                }
                nextLink = o.properties.nextLink;
            }
            return result;
        }

        private static int GetDaysOffset(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) { return 61; }
            var match = new Regex("(?<num>[0-9]*) ?(?<word>[A-Za-z]*)").Match(period);
            if (!match.Success) { return 61; }
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

        private async Task<string> GetAccessToken()
        {
            var request = new Dictionary<string, string>(){
                {"grant_type", "client_credentials"},
                { "client_id", _clientId},
                { "scope" , "https://management.azure.com/.default"},
                { "client_secret", _clientSecret}
            };

            HttpContent getTokenContent = new FormUrlEncodedContent(request);
            var getTokenResponse = _httpClient.PostAsync("https://login.microsoftonline.com/c86d4c18-4e4c-47c2-9d91-a7513ac6337f/oauth2/v2.0/token", getTokenContent);
            var tokenResponse = await getTokenResponse.Result.Content.ReadAsStringAsync();
            if (!getTokenResponse.Result.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Cannot get access token for Azure Cost Management API. Error: {tokenResponse}");
            }

            var tokenRx = Regex.Match(tokenResponse, "\"access_token\":\"(?<token>.*)\"");
            var token = tokenRx.Groups["token"].Value;
            return token;
        }
    }
}
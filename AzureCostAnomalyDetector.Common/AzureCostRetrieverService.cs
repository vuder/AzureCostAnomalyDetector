using System;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace AzureCostAnomalyDetector.Common
{
    public class AzureCostRetrieverService
    {
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _tenantId;
        private readonly ILogger _logger;
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Regex _periodPatternRegex = new Regex("(?<num>[0-9]*) ?(?<word>[A-Za-z]*)", RegexOptions.Compiled);
        private static readonly Regex _accessTokenInJWTRegex = new Regex("\"access_token\":\"(?<token>.*)\"");

        public AzureCostRetrieverService(string clientId, string clientSecret, string tenantId, ILogger logger = null)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _tenantId = tenantId;
            _logger = logger;
        }

        public async Task<IEnumerable<AzureCost>> GetAzureCosts(string period, DateTime lastDay, string subscriptionId)
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
            var nextLink = $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement/query?api-version=2019-11-01";

            List<AzureCost> result = new();

            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                _logger?.LogInformation($"Loading data from azure: {nextLink}");

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

            _logger?.LogInformation("Data load completed");
            return result;
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

        private async Task<string> GetAccessToken()
        {
            var request = new Dictionary<string, string>(){
                {"grant_type", "client_credentials"},
                { "client_id", _clientId},
                { "scope" , "https://management.azure.com/.default"},
                { "client_secret", _clientSecret}
            };
            _logger?.LogInformation("Getting access token");
            HttpContent getTokenContent = new FormUrlEncodedContent(request);
            var getTokenResponse = _httpClient.PostAsync($"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token", getTokenContent);
            var tokenResponse = await getTokenResponse.Result.Content.ReadAsStringAsync();
            if (!getTokenResponse.Result.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Cannot get access token for Azure Cost Management API. Error: {tokenResponse}");
            }

            var searchToken = _accessTokenInJWTRegex.Match(tokenResponse);
            if (!searchToken.Success)
            {
                throw new ArgumentException($"Extraction of Access Token from Auth response failed.");
            }
            var token = searchToken.Groups["token"].Value;
            return token;
        }
    }
}
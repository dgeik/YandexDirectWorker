using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YandexDirectWorker
{
    public class Handler
    {
        private const string YandexApiUrl = "https://api.direct.yandex.com/json/v5/";

        // Точка входа для Яндекс.Cloud
        public async Task<string> FunctionHandler(string input)
        {
            using var httpClient = new HttpClient();
            Console.WriteLine("--- Запуск функции очистки площадок ---");

            string yandexToken = Environment.GetEnvironmentVariable("YANDEX_TOKEN") ?? "";
            long campaignId = long.Parse(Environment.GetEnvironmentVariable("CAMPAIGN_ID") ?? "0");
            string sheetId = Environment.GetEnvironmentVariable("SHEET_ID") !;
            string sheetRangeBlacklist = Environment.GetEnvironmentVariable("SHEET_RANGE_BLACKLIST") ?? "Лист1!A2:A";
            string sheetRangeWhitelist = Environment.GetEnvironmentVariable("SHEET_RANGE_WHITELIST") ?? "Лист1!B2:B";
            string? googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

            Console.WriteLine($"Обработка Кампания ID: {campaignId}");

            if (string.IsNullOrEmpty(yandexToken) || string.IsNullOrEmpty(googleApiKey) || campaignId == 0)
            {
                return "Ошибка: Не заданы ключевые переменные окружения (Token, googleApi, или Campaign ID).";
            }

            try
            {
                // 1. Получаем белый и черный списки
                var combinedLists = await GetWordsFromGoogle(sheetId, [sheetRangeBlacklist, sheetRangeWhitelist], googleApiKey!);

                var stopWords = combinedLists[0];
                var whitelistWords = combinedLists[1];

                Console.WriteLine($"Найдено Черный список: {stopWords.Count}");
                Console.WriteLine($"Найдено Белый список: {whitelistWords.Count}");

                // 2. Получаем отчет
                var sitesReport = await GetSitesFromReport(httpClient, yandexToken, campaignId);
                Console.WriteLine($"Площадок в отчете: {sitesReport.Count}");

                // 3. Фильтруем
                var sitesToBlock = FilterAndWhitelistSites(sitesReport, stopWords, whitelistWords);

                // 4. Обновляем кампанию
                if (sitesToBlock.Count > 0)
                {
                    await UpdateCampaignNegativeSites(httpClient, yandexToken, campaignId, sitesToBlock.ToList());
                }
                return $"Успешно. Добавлено в блок: {sitesToBlock.Count} площадок.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }

        // --- ЛОГИКА ФИЛЬТРАЦИИ ---

        private HashSet<string> FilterAndWhitelistSites(List<string> sitesReport, List<string> stopWords, List<string> whitelistWords)
        {
            var whitelistSet = new HashSet<string>(whitelistWords, StringComparer.OrdinalIgnoreCase);
            var sitesToBlock = new HashSet<string>();

            foreach (var site in sitesReport)
            {
                //БЕЛЫЙ СПИСОК
                if (whitelistSet.Contains(site))
                {
                    Console.WriteLine($"[Whitelist]: Площадка '{site}' исключена из блокировки.");
                    continue;
                }

                //ЧЕРНЫЙ СПИСОК
                foreach (var word in stopWords)
                {
                    if (site.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sitesToBlock.Add(site);
                        break;
                    }
                }
            }
            return sitesToBlock;
        }

        // --- МЕТОДЫ GOOGLE ---
        private async Task<List<List<string>>> GetWordsFromGoogle(string spreadsheetId, string[] ranges, string apiKey)
        {
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                ApiKey = apiKey,
                ApplicationName = "YandexDirectWorker",
            });

            var request = service.Spreadsheets.Values.BatchGet(spreadsheetId);
            request.Ranges = ranges;

            var response = await request.ExecuteAsync();

            var allLists = new List<List<string>>();

            if (response.ValueRanges != null)
            {
                foreach (var valueRange in response.ValueRanges)
                {
                    var currentList = new List<string>();
                    if (valueRange.Values != null)
                    {
                        foreach (var row in valueRange.Values)
                        {
                            if (row.Count > 0)
                            {
                                string word = row[0].ToString()?.Trim() ?? "";
                                if (!string.IsNullOrEmpty(word))
                                {
                                    currentList.Add(word);
                                }
                            }
                        }
                    }
                    allLists.Add(currentList);
                }
            }
            return allLists;
        }

        // --- МЕТОДЫ YANDEX ---

        // А. Получение отчета
        private async Task<List<string>> GetSitesFromReport(HttpClient client, string token, long campaignId)
        {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.TryAddWithoutValidation("processingMode", "auto");

            var reportDefinition = new
            {
                params_ = new
                {
                    SelectionCriteria = new
                    {
                        Filter = new[]
                        {
                            new { Field = "CampaignId", Operator = "EQUALS", Values = new[] { campaignId.ToString() } }
                        }
                    },
                    FieldNames = new[] { "Placement", "Impressions" },
                    ReportName = $"CloudCheck_{DateTime.UtcNow.Ticks}",
                    ReportType = "CUSTOM_REPORT",
                    DateRangeType = "AUTO",
                    Format = "TSV",
                    IncludeVAT = "NO",
                    IncludeDiscount = "NO"
                }
            };

            string jsonBody = JsonConvert.SerializeObject(reportDefinition).Replace("params_", "params");
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(YandexApiUrl + "reports", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Yandex API Error Status: {response.StatusCode}. Content: {errorContent}");
            }

            var reportText = await response.Content.ReadAsStringAsync();
            var sites = new List<string>();
            var lines = reportText.Split('\n');

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length > 0 && !parts[0].StartsWith("Total"))
                {
                    string site = parts[0].Trim();
                    if (!string.IsNullOrEmpty(site) && site != "--") sites.Add(site);
                }
            }
            return sites.Distinct().ToList();
        }

        // Б. Обновление кампании
        private async Task UpdateCampaignNegativeSites(HttpClient client, string token, long campId, List<string> newBlacklist)
        {
            // 1. Get Current
            var getBody = new { method = "get", @params = new { SelectionCriteria = new { Ids = new[] { campId } }, FieldNames = new[] { "Id", "ExcludedSites" } } };
            var respGet = await SendJsonRpc(client, "campaigns", getBody, token);

            var currentSites = respGet["result"]?["Campaigns"]?[0]?["ExcludedSites"]?["Items"]?.ToObject<List<string>>() ?? new List<string>();

            var normalizedCurrentSites = currentSites
                .Select(s => CleanDomain(s)).ToList();

            var normalizedNewBlacklist = newBlacklist
                .Select(s => CleanDomain(s)).ToList();

            var uniqueSites = new HashSet<string>(normalizedCurrentSites);
            foreach (var site in normalizedNewBlacklist)
            {
                uniqueSites.Add(site);
            }
            var finalBlacklist = uniqueSites.ToList();

            if (finalBlacklist.Count > 1000)
            {
                finalBlacklist = finalBlacklist.Take(1000).ToList();
            }

            // 3. Update
            var updateBody = new { method = "update", @params = new { Campaigns = new[] { new { Id = campId, ExcludedSites = new { Items = finalBlacklist } } } } };
            await SendJsonRpc(client, "campaigns", updateBody, token);
        }
        //1
        // Хелпер для отправки JSON-RPC запросов
        private async Task<JObject> SendJsonRpc(HttpClient client, string serviceName, object body, string token)
        {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            var response = await client.PostAsync(YandexApiUrl + serviceName, content);
            var respString = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception($"API Error ({serviceName}): {respString}");
            }

            return JObject.Parse(respString);
        }
        private void CheckJsonRpcResponse(JObject response, string action)
        {
            if (response["error"] != null)
            {
                var errorDetails = response["error"].ToString(Formatting.None);
                Console.WriteLine($"[!!! FATAL API ERROR !!!] Ошибка Директа при операции '{action}': {errorDetails}");
                throw new Exception($"API Direct вернул ошибку в теле ответа: {errorDetails}");
            }

            var updateResult = response["result"]?["UpdateResults"]?.FirstOrDefault();
            if (updateResult != null && updateResult["Errors"] != null && updateResult["Errors"].Any())
            {
                var errorDetails = updateResult["Errors"].ToString(Formatting.None);
                Console.WriteLine($"[!!! PARTIAL UPDATE ERROR !!!] Ошибка обновления Директа: {errorDetails}");
                throw new Exception($"API Direct вернул ошибку при обновлении кампании: {errorDetails}");
            }

            //строка проверки
            //CheckJsonRpcResponse(respGet, "Campaigns.get");
        }

        private string CleanDomain(string domain)
        {
            string cleaned = domain.Trim().ToLowerInvariant();

            if (cleaned.StartsWith("http://"))
            {
                cleaned = cleaned.Replace("http://", "");
            }
            if (cleaned.StartsWith("https://"))
            {
                cleaned = cleaned.Replace("https://", "");
            }

            if (cleaned.StartsWith("www."))
            {
                cleaned = cleaned.Replace("www.", "");
            }

            if (cleaned.EndsWith("/"))
            {
                cleaned = cleaned.TrimEnd('/');
            }

            return cleaned;
        }
    }
}

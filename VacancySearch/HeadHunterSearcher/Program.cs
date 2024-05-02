// See https://aka.ms/new-console-template for more information

using CsvHelper;
using HeadHunterSearcher;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Globalization;

var outputFolder = "output";
if (!Directory.Exists(outputFolder))
{
    Directory.CreateDirectory(outputFolder);
}
var vacanciesFilePath = Path.Combine(outputFolder, "Vacancies.csv");
var vacancyUrlsFilePath = Path.Combine(outputFolder, $"Vacancies.urls");
var processedUrlsFilePath = Path.Combine(outputFolder, $"Processed.txt");

var hhApiBaseUrl = "https://api.hh.ru";
var vacanciesMethod = "/vacancies";
var searchTextQueryParamName = "text=";
var pageSize = 100;
var perPageArgument = $"per_page={pageSize}";
// https://github.com/hhru/api/issues/74
var maxRequestsPerSec = 1f;
var requestDelay = TimeSpan.FromSeconds(1 / maxRequestsPerSec);
var searchTexts = new List<string>()
{
    //"DevOps",
    "C# программист",
    "C# разработчик",
    ".NET программист",
    ".NET разработчик",
    "C# developer",
    ".NET developer",
};

var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("User-Agent", "VacancySearch.v3");

var processedUrls = await GetUrlsInFile(processedUrlsFilePath);
Console.WriteLine($"Ранее обработанных вакансий: {processedUrls.Count}");
var vacancyUrls = await GetUrlsInFile(vacancyUrlsFilePath);

if (vacancyUrls.Count > 0)
{
    Console.WriteLine($"Чтение ссылок на вакансии из {vacancyUrlsFilePath}");
    vacancyUrls = File.ReadAllLines(vacancyUrlsFilePath).Except(processedUrls).ToList();
    Console.WriteLine($"Оставшихся вакансий для обработки: {vacancyUrls.Count}");
}
else
{
    Console.WriteLine($"Запрос вакансий");
    foreach (var searchText in searchTexts)
    {
        vacancyUrls.AddRange(await GetAccreditedVacancyUrls(httpClient, requestDelay, outputFolder, hhApiBaseUrl, vacanciesMethod, searchTextQueryParamName, perPageArgument, searchText));
    }
    vacancyUrls = vacancyUrls.Distinct().ToList();
    File.Delete(vacancyUrlsFilePath);
    File.WriteAllLines(vacancyUrlsFilePath, vacancyUrls);
}

var vacancies = await GetVacancies(vacancyUrls.Except(processedUrls), httpClient, TimeSpan.FromSeconds(1), vacanciesFilePath, processedUrlsFilePath);
var deduplicatedVacancies = DeduplicateVacancies(vacancies, vacanciesFilePath);
await SerializeVacancies(deduplicatedVacancies, vacanciesFilePath);

async Task SerializeVacancies(List<Vacancy> deduplicatedVacancies, string vacanciesFilePath)
{
    using (var vacanciesWriter = new StreamWriter(vacanciesFilePath, true))
    using (var csv = new CsvWriter(vacanciesWriter, CultureInfo.InvariantCulture))
    {
        if (new FileInfo(vacanciesFilePath).Length == 0)
        {
            csv.WriteHeader<Vacancy>();
            csv.NextRecord();
            csv.Flush();
        }
        
        await csv.WriteRecordsAsync(deduplicatedVacancies);
        csv.Flush();
    }
}

static List<Vacancy> DeduplicateVacancies(HashSet<Vacancy> vacancies, string vacanciesFilePath)
{
    var vacanciesDescription = new HashSet<string>();
    var essentialVacancies = new Dictionary<VacancyEssentialData, List<Vacancy>>();
    foreach (var vacancy in vacancies)
    {
        var vacancyEssentialData = new VacancyEssentialData
        {
            CompanyId = vacancy.CompanyId,
            CompanyName = vacancy.CompanyName,
            VacancyName = vacancy.VacancyName,
            Description = vacancy.Description
        };

        if (essentialVacancies.ContainsKey(vacancyEssentialData))
        {
            essentialVacancies[vacancyEssentialData].Add(vacancy);
        }
        else
        {
            essentialVacancies.Add(vacancyEssentialData, new List<Vacancy> { vacancy });
        }
    }

    var deduplicatedVacancies = new List<Vacancy>(essentialVacancies.Count);
    foreach (var vacancyKeyValuePair in essentialVacancies)
    {
        deduplicatedVacancies.Add(
            vacancyKeyValuePair.Value.FirstOrDefault(x => x.AreaName == "Москва") ??
            vacancyKeyValuePair.Value.FirstOrDefault(x => x.AreaName == "Санкт-Петербург") ??
            vacancyKeyValuePair.Value.FirstOrDefault(x => x.AreaName == "Казань") ??
            vacancyKeyValuePair.Value.FirstOrDefault(x => x.AreaName == "Нижний Новгород") ??
            vacancyKeyValuePair.Value.FirstOrDefault(x => x.AreaName == "Калуга") ??
            vacancyKeyValuePair.Value.First());
    }

    return deduplicatedVacancies;
}

static async Task<List<string>> GetUrlsInFile(string filePath)
{
    if (File.Exists(filePath))
    {
        return await File.ReadAllLinesAsync(filePath).ContinueWith(t => t.Result.ToList());
    }
    return new List<string>();
}

static async Task<IEnumerable<string>> GetAccreditedVacancyUrls(HttpClient httpClient, TimeSpan requestDelay, string outputFolder, string hhApiBaseUrl, string vacanciesMethod, string searchTextQueryParamName, string perPageArgument, string searchText)
{
    var searchTextForRequest = Uri.EscapeDataString($"NAME:({searchText})");
    var queryUrl = $"{hhApiBaseUrl}{vacanciesMethod}?{searchTextQueryParamName}{searchTextForRequest}&{perPageArgument}";
    var urls = await GetAccreditedVacancyUrlsFromQueryUrl(outputFolder, queryUrl, httpClient, requestDelay);
    Console.WriteLine($"Найдено {urls.Count()} вакансий");

    foreach (var url in urls)
    {
        Console.WriteLine(url);
    }
    return urls;
}

static async Task<HashSet<Vacancy>> GetVacancies(IEnumerable<string> urls, HttpClient httpClient, TimeSpan requestDelay, string vacanciesFilePath, string processedUrlsFilePath)
{
    var vacancies = new ConcurrentBag<Vacancy>();

    using (var vacanciesWriter = new StreamWriter(vacanciesFilePath, true))
    using (var csv = new CsvWriter(vacanciesWriter, CultureInfo.InvariantCulture))
    using (var processedUrlsWriter = new StreamWriter(processedUrlsFilePath, true))
    {
        processedUrlsWriter.AutoFlush = true;

        var counter = 0;
        var tasks = new ConcurrentBag<Task>();

        foreach (var url in urls)
        {
            Console.WriteLine($"Запрашиваем {url}");
            tasks.Add(GetRequestJsonAsync(url, httpClient, Directory.GetParent(vacanciesFilePath)!.FullName)
                    .ContinueWith(async t =>
                    {
                        Vacancy vacancy = GetVacancy(t.Result);
                        vacancies.Add(vacancy);
                        Console.WriteLine($"Получена вакансия {vacancy.VacancyName}. Обработано {++counter}");
                    }));
            await Task.Delay(requestDelay);
        }
        await Task.WhenAll(tasks);
    }

    return vacancies.ToHashSet();
}

static async Task<IEnumerable<string>> GetAccreditedVacancyUrlsFromQueryUrl(string outputFolder, string queryUrl, HttpClient httpClient, TimeSpan delay)
{
    Console.WriteLine($"Запрашиваем {queryUrl}");
    var response = await GetRequestJsonAsync(queryUrl, httpClient, outputFolder);
    Console.WriteLine(response.ToString());

    return await GetAccreditedVacancyUrlsFromAllPages(outputFolder, queryUrl, httpClient, delay, response);
}

static async Task<List<string>> GetAccreditedVacancyUrlsFromAllPages(string outputFolder, string queryUrl, HttpClient httpClient, TimeSpan delay, dynamic response)
{
    var urls = new List<string>();
    var tasks = new ConcurrentBag<Task>();
    for (int page = 0; page < (int)response.pages; page++)
    {
        tasks.Add(GetAccreditedVacancyUrlsFromRequestJsonAsync($"{queryUrl}&page={page}", httpClient, outputFolder)
            .ContinueWith(t => urls.AddRange(t.Result)));
        await Task.Delay(delay);
    }
    await Task.WhenAll(tasks);
    return urls;
}

static async Task<List<string>> GetAccreditedVacancyUrlsFromRequestJsonAsync(string queryUrl, HttpClient httpClient, string outputFolder)
{
    Console.WriteLine($"Запрашиваем {queryUrl}");
    var urls = new List<string>();
    dynamic vacancySearchJson = await GetRequestJsonAsync(queryUrl, httpClient, outputFolder);
    foreach (var item in vacancySearchJson.items)
    {
        if (item.employer?.accredited_it_employer == true)
        {
            urls.Add(item.url.ToString());
        }
    }

    return urls;
}

static async Task<dynamic> GetRequestJsonAsync(string queryUrl, HttpClient httpClient, string outputFolder)
{
    const int maxRetries = 5;
    int retryCount = 0;
    TimeSpan delay = TimeSpan.FromSeconds(60);

    HttpResponseMessage? response = null;
    while (retryCount < maxRetries)
    {
        try
        {
            response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, queryUrl));
            break;
        }
        catch (AggregateException ex)
        {
            var socketExceptions = ex.Flatten().InnerExceptions.OfType<System.Net.Sockets.SocketException>().ToArray();
            if (socketExceptions.Length == 0)
            {
                throw;
            }

            foreach (var socketException in socketExceptions)
            {
                Console.WriteLine($"Ошибка при обращении к {queryUrl}. Ошибка {socketException.SocketErrorCode}. Повтор {retryCount + 1} из {maxRetries}");
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount + 1));
                retryCount++;
            }
        }
    }

    if (response == null)
    {
        throw new Exception($"Не удалось получить данные по запросу {queryUrl}");
    }

    if (response.IsSuccessStatusCode)
    {
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }
    else
    {
        Console.WriteLine(response);
        var content = response.Content.ReadAsStringAsync().Result;
        var errorFilename = Path.Combine(outputFolder, "error.txt");
        var directory = Path.GetDirectoryName(errorFilename);
        Directory.CreateDirectory(directory);
        File.WriteAllText(errorFilename, content);
        Console.WriteLine(content);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            dynamic errorJson = JObject.Parse(content);
            // https://github.com/hhru/api/blob/master/docs/errors.md#%D0%BD%D0%B5%D0%BE%D0%B1%D1%85%D0%BE%D0%B4%D0%B8%D0%BC%D0%BE%D1%81%D1%82%D1%8C-%D0%BF%D1%80%D0%BE%D0%B9%D1%82%D0%B8-%D0%BA%D0%B0%D0%BF%D1%87%D1%83
            // https://ru.stackoverflow.com/questions/1411275/%D0%9F%D0%B0%D1%80%D1%81%D0%B8%D0%BD%D0%B3-hh-ru-%D0%BE%D1%88%D0%B8%D0%B1%D0%BA%D0%B0-%D1%82%D1%80%D0%B5%D0%B1%D1%83%D0%B5%D1%82-%D0%BA%D0%B0%D0%BF%D1%87%D1%83
            foreach (var error in errorJson.errors)
            {
                if (error.type == "captcha_required")
                {
                    Console.WriteLine($"Файл ссылки на каптчу сохранён по адресу {errorFilename}");
                }
            }
            return null;
        }

        return null;
    }
}

static Vacancy GetVacancy(dynamic vacancyJson)
{
    var vacancy = new Vacancy();
    vacancy.Address = vacancyJson.address != null ? vacancyJson.address.raw : null;
    vacancy.AreaId = vacancyJson.area.id;
    vacancy.AreaName = vacancyJson.area.name;
    vacancy.CompanyId = vacancyJson.employer.id;
    vacancy.CompanyName = vacancyJson.employer.name;
    vacancy.CompanyTrusted = vacancyJson.employer.trusted;
    vacancy.CompanyUrl = vacancyJson.employer.alternate_url;
    vacancy.IsAccreditedCompany = vacancyJson.employer.accredited_it_employer;
    vacancy.Description = vacancyJson.description;
    vacancy.EmploymentId = vacancyJson.employment.id;
    vacancy.EmploymentName = vacancyJson.employment.name;
    vacancy.Experience = vacancyJson.experience.name;

    foreach (var role in vacancyJson.professional_roles)
    {
        vacancy.ProfessionalRoles += role.name + ";";
    }

    foreach (var skill in vacancyJson.key_skills)
    {
        vacancy.KeySkills += skill.name + ";";
    }

    vacancy.PublishedAt = vacancyJson.published_at;

    if (vacancyJson.salary != null)
    {
        vacancy.SalaryFrom = vacancyJson.salary.from;
        vacancy.SalaryTo = vacancyJson.salary.to;
        vacancy.SalaryCurrency = vacancyJson.salary.currency;
        vacancy.SalaryGross = vacancyJson.gross;
    }

    vacancy.ScheduleId = vacancyJson.schedule.id;
    vacancy.ScheduleName = vacancyJson.schedule.name;

    foreach (var specialisation in vacancyJson.specializations)
    {
        vacancy.Specialisations += specialisation.id + ": " + specialisation.name + "|";
    }

    vacancy.VacancyId = vacancyJson.id;
    vacancy.VacancyName = vacancyJson.name;
    vacancy.VacancyUrl = vacancyJson.alternate_url;

    foreach (var mode in vacancyJson.working_time_modes)
    {
        vacancy.WorkingTimeModes = mode.name + ";";
    }

    foreach (var interval in vacancyJson.working_time_intervals)
    {
        vacancy.WorkingTimeIntervals = interval.name + ";";
    }

    vacancy.BillingType = vacancyJson.billing_type.name;
    vacancy.Type = vacancyJson.type.name;

    foreach (var day in vacancyJson.working_days)
    {
        vacancy.WorkingDays = day.name + ";";
    }

    return vacancy;
}

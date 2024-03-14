// See https://aka.ms/new-console-template for more information

using CsvHelper;
using HeadHunterSearcher;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Globalization;

var outputFolder = "output";
var vacanciesFolder = $"{outputFolder + Path.DirectorySeparatorChar}vacancies";

var hhApiBaseUrl = "https://api.hh.ru";
var vacanciesMethod = "/vacancies";
var searchTextQueryParamName = "text=";
var pageSize = 100;
var perPageArgument = $"per_page={pageSize}";
var searchText = "Разработчик .NET";
var queryUrl = $"{hhApiBaseUrl}{vacanciesMethod}?{searchTextQueryParamName}{searchText}&{perPageArgument}";
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("User-Agent", "VacancySearch.v3");

// https://github.com/hhru/api/issues/74
var maxRequestsPerSec = 1f;
var delay = TimeSpan.FromSeconds(1 / maxRequestsPerSec);

var urls = await GetVacanciesSearch(outputFolder, queryUrl, httpClient, delay);

Console.WriteLine($"Найдено {urls.Count()} вакансий");

foreach (var url in urls)
{
    Console.WriteLine(url);
}

var vacanciesFilePath = Path.Combine(vacanciesFolder, "Vacancies.csv");

if (!Directory.Exists(vacanciesFolder))
{
    Directory.CreateDirectory(vacanciesFolder);
}

using (var writer = new StreamWriter(vacanciesFilePath, true))
using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
{
    if (new FileInfo(vacanciesFilePath).Length == 0)
    {
        csv.WriteHeader<Vacancy>();
        csv.NextRecord();
        csv.Flush();
    }

    var counter = 0;
    var tasks = new ConcurrentBag<Task>();
    var vacancies = new ConcurrentBag<Vacancy>();
    foreach (var url in urls)
    {
        Console.WriteLine($"Запрашиваем {url}");
        tasks.Add(GetRequestJsonAsync(url, httpClient, outputFolder)
                .ContinueWith(t =>
                {
                    var vacancy = GetVacancy(t.Result);
                    vacancies.Add(vacancy);
                    Console.WriteLine($"Сохранена информация о вакансии {vacancy.VacancyName}. Обработано {++counter}");
                }));
        await Task.Delay(delay);
    }
    await Task.WhenAll(tasks);

    foreach (var vacancy in vacancies)
    {
        csv.WriteRecord(vacancy);
        csv.NextRecord();
        csv.Flush();
    }
}

Console.WriteLine("Завершено. Нажмите любую клавишу.");
try
{
    Console.ReadKey();
}
catch (InvalidOperationException)
{
    // Handle "An unhandled exception of type 'System.InvalidOperationException' occurred in System.Console.dll"
}

static async Task<IEnumerable<string>> GetVacanciesSearch(string outputFolder, string queryUrl, HttpClient httpClient, TimeSpan delay)
{
    Console.WriteLine($"Запрашиваем {queryUrl}");
    var response = await GetRequestJsonAsync(queryUrl, httpClient, outputFolder);
    Console.WriteLine(response.ToString());

    var urls = new List<string>();
    var tasks = new ConcurrentBag<Task>();
    for (int page = 0; page < (int)response.pages; page++)
    {
        tasks.Add(GetUrlsFromRequestJsonAsync($"{queryUrl}&page={page}", httpClient, outputFolder)
            .ContinueWith(t => urls.AddRange(t.Result)));
        await Task.Delay(delay);
    }
    await Task.WhenAll(tasks);

    return urls;
}

static async Task<List<string>> GetUrlsFromRequestJsonAsync(string queryUrl, HttpClient httpClient, string outputFolder)
{
    Console.WriteLine($"Запрашиваем {queryUrl}");
    var urls = new List<string>();
    dynamic vacancySearchJson = await GetRequestJsonAsync(queryUrl, httpClient, outputFolder);
    foreach (var item in vacancySearchJson.items)
    {
        urls.Add(item.url.ToString());
    }

    return urls;
}

static async Task<dynamic> GetRequestJsonAsync(string queryUrl, HttpClient httpClient, string outputFolder)
{
    var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, queryUrl));
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
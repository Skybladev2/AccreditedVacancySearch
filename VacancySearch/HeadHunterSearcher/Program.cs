// See https://aka.ms/new-console-template for more information

using CsvHelper;
using HeadHunterSearcher;
using Newtonsoft.Json.Linq;
using System.Diagnostics.Metrics;
using System.Globalization;

var companyNamesFilename = "CompanyNames.csv";
var outputFolder = "output";
var employeesFolder = $"{outputFolder + Path.DirectorySeparatorChar}employees";
var exactEmployeesFolder = $"{employeesFolder + Path.DirectorySeparatorChar}exact";
var employeesWithVacanciesFolder = $"{exactEmployeesFolder + Path.DirectorySeparatorChar}with_vacancies";
var vacancyListFolder = $"{employeesWithVacanciesFolder + Path.DirectorySeparatorChar}vacancies";
var vacanciesFolder = $"{vacancyListFolder + Path.DirectorySeparatorChar}vacancies";
var cursorFilename = $"{outputFolder + Path.DirectorySeparatorChar}LastProcessedLine.txt";
var vacancyCursorFilename = $"{vacancyListFolder + Path.DirectorySeparatorChar}LastProcessedLine.txt";
var companyNames = File.ReadAllLines(companyNamesFilename);

var hhApiBaseUrl = "https://api.hh.ru";
var vacanciesMethod = "/vacancies";
var searchTextQueryParamName = "text=";
var searchText = "Unity";
var queryUrl = $"{hhApiBaseUrl}{vacanciesMethod}?{searchTextQueryParamName}{searchText}";
var httpClient = new HttpClient();
//httpClient.BaseAddress = new Uri();
httpClient.DefaultRequestHeaders.Add("User-Agent", "VacancySearch.v3");

// https://github.com/hhru/api/issues/74
var requestsPerSec = 4f;
var delay = TimeSpan.FromSeconds(1 / requestsPerSec);

Console.WriteLine($"Запрашиваем {queryUrl}");
var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, queryUrl)).Result;
dynamic vacancyJson = null;
if (response.IsSuccessStatusCode)
{
    vacancyJson = JObject.Parse(response.Content.ReadAsStringAsync().Result);
    Console.WriteLine(vacancyJson.ToString());
}
else
{
    Console.WriteLine(response);
    var content = response.Content.ReadAsStringAsync().Result;
    var errorFilename = Path.Combine(vacancyListFolder, "error.txt");
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
        return;
    }
}

var vacancy = new Vacancy();
vacancy.Address = vacancyJson.address != null ? vacancyJson.address.raw : null;
//vacancy.AreaId = vacancyJson.area.id;
//vacancy.AreaName = vacancyJson.area.name;
//vacancy.CompanyId = vacancyJson.employer.id;
//vacancy.CompanyName = vacancyJson.employer.name;
//vacancy.CompanyTrusted = vacancyJson.employer.trusted;
//vacancy.CompanyUrl = vacancyJson.employer.alternate_url;
//vacancy.Description = vacancyJson.description;
//vacancy.EmploymentId = vacancyJson.employment.id;
//vacancy.EmploymentName = vacancyJson.employment.name;
//vacancy.Experience = vacancyJson.experience.name;

//foreach (var role in vacancyJson.professional_roles)
//{
//    vacancy.ProfessionalRoles += role.name + ";";
//}

//vacancy.PublishedAt = vacancyJson.published_at;

//if (vacancyJson.salary != null)
//{
//    vacancy.SalaryFrom = vacancyJson.salary.from;
//    vacancy.SalaryTo = vacancyJson.salary.to;
//    vacancy.SalaryCurrency = vacancyJson.salary.currency;
//    vacancy.SalaryGross = vacancyJson.gross;
//}
//vacancy.ScheduleId = vacancyJson.schedule.id;
//vacancy.ScheduleName = vacancyJson.schedule.name;

//foreach (var specialisation in vacancyJson.specializations)
//{
//    vacancy.Specialisations += specialisation.id + ": " + specialisation.name + "|";
//}

//vacancy.VacancyId = vacancyJson.id;
//vacancy.VacancyName = vacancyJson.name;
//vacancy.VacancyUrl = vacancyJson.alternate_url;

//csv.WriteRecord(vacancy);
//csv.NextRecord();
//csv.Flush();

//Console.WriteLine($"Сохранена информация о вакансии {vacancy.VacancyName}. Обработано {++counter}");
            

//File.WriteAllText(vacancyCursorFilename, (line++).ToString());

Console.WriteLine("Завершено. Нажмите любую клавишу.");
Console.ReadKey();

static async Task FilterCompanies(string[] companyNames, string vacancyCursorFilename, string vacancyListFolder, string employeesWithVacanciesFolder, string exactEmployeesFolder, string employeesFolder, HttpClient httpClient, TimeSpan delay)
{
    var companyJsons = Directory.EnumerateFiles(employeesFolder, "*.json");
    foreach (var jsonPath in companyJsons)
    {
        dynamic json = JObject.Parse(File.ReadAllText(jsonPath));
        await ExpandEmployersMultipageResponse(employeesFolder, jsonPath, json, httpClient, delay);
        await FilterExactNames(employeesFolder, exactEmployeesFolder, jsonPath, json);
    }
    await FilterWithVacancies(exactEmployeesFolder, employeesWithVacanciesFolder);
    await GetCompanyVacancies(employeesWithVacanciesFolder, vacancyListFolder, httpClient, delay);

    var companyVacanciesFiles = Directory.EnumerateFiles(vacancyListFolder, "*.json");
    foreach (var jsonPath in companyVacanciesFiles)
    {
        dynamic json = JObject.Parse(File.ReadAllText(jsonPath));
        await ExpandVacanciesMultipageResponse(vacancyListFolder, jsonPath, json, httpClient, delay);
    }

    await GetVacancies(vacancyCursorFilename, vacancyListFolder, httpClient, delay);
}

async static Task GetVacancies(string vacancyCursorFilename, string vacancyListFolder, HttpClient httpClient, TimeSpan delay)
{
    var companyFiles = Directory.EnumerateFiles(vacancyListFolder, "*.json")
        .Concat(Directory.EnumerateFiles(vacancyListFolder, "*.part*"))
        .OrderBy(c => c).ToList();

    var line = 0;
    if (File.Exists(vacancyCursorFilename))
    {
        Console.WriteLine("Найден файл курсора");
        line = int.Parse(File.ReadAllText(vacancyCursorFilename));
        Console.WriteLine($"Возобновление со строки {line}");
        companyFiles = companyFiles.Skip(line).ToList();
    }

    Console.WriteLine($"Файлов вакансий компании найдено: {companyFiles.Count()}");
    int counter = 0;

    var vacanciesFilePath = Path.Combine(vacancyListFolder, "Vacancies.csv");

    using (var writer = new StreamWriter(vacanciesFilePath, true))
    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
    {
        if (new FileInfo(vacanciesFilePath).Length == 0)
        {
            csv.WriteHeader<Vacancy>();
            csv.NextRecord();
            csv.Flush();
        }

        foreach (var jsonPath in companyFiles)
        {
            dynamic json = JObject.Parse(File.ReadAllText(jsonPath));

            foreach (var item in json.items)
            {
                Console.WriteLine($"Запрашиваем {item.url.ToString()}");
                var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, item.url.ToString())).Result;
                dynamic vacancyJson = null;
                if (response.IsSuccessStatusCode)
                {
                    vacancyJson = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                    Console.WriteLine(vacancyJson.ToString());
                }
                else
                {
                    Console.WriteLine(response);
                    var content = response.Content.ReadAsStringAsync().Result;
                    var errorFilename = Path.Combine(vacancyListFolder, "error.txt");
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
                        return;
                    }
                    continue;
                }

                var vacancy = new Vacancy();
                vacancy.Address = vacancyJson.address != null ? vacancyJson.address.raw : null;
                vacancy.AreaId = vacancyJson.area.id;
                vacancy.AreaName = vacancyJson.area.name;
                vacancy.CompanyId = vacancyJson.employer.id;
                vacancy.CompanyName = vacancyJson.employer.name;
                vacancy.CompanyTrusted = vacancyJson.employer.trusted;
                vacancy.CompanyUrl = vacancyJson.employer.alternate_url;
                vacancy.Description = vacancyJson.description;
                vacancy.EmploymentId = vacancyJson.employment.id;
                vacancy.EmploymentName = vacancyJson.employment.name;
                vacancy.Experience = vacancyJson.experience.name;

                foreach (var role in vacancyJson.professional_roles)
                {
                    vacancy.ProfessionalRoles += role.name + ";";
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

                csv.WriteRecord(vacancy);
                csv.NextRecord();
                csv.Flush();

                Console.WriteLine($"Сохранена информация о вакансии {vacancy.VacancyName}. Обработано {++counter}");
            }

            File.WriteAllText(vacancyCursorFilename, (line++).ToString());
        }
    }
}

async static Task GetCompanyVacancies(string employeesWithVacanciesFolder, string vacanciesFolder, HttpClient httpClient, TimeSpan delay)
{
    var companyFiles = Directory.EnumerateFiles(employeesWithVacanciesFolder, "*.csv");
    Console.WriteLine($"Файлов компании найдено: {companyFiles.Count()}");
    int counter = 0;

    foreach (var file in companyFiles)
    {
        var line = File.ReadAllText(file);
        var parts = line.Split(",");
        var companyName = parts[0];
        var companyId = parts[1];
        var vacanciesUrl = parts[2];
        Thread.Sleep(delay);
        var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, vacanciesUrl)).Result;
        await SaveResponse(vacanciesFolder, companyName + "." + companyId, response);
        Console.WriteLine($"Сохранена информация о компании {companyName}. Обработано {++counter}");
    }
}

async static Task FilterWithVacancies(string exactEmployeesFolder, string employeesWithVacanciesFolder)
{
    var companyJsons = Directory.EnumerateFiles(exactEmployeesFolder, "*.json");
    Console.WriteLine($"Найдено {companyJsons.Count()} файлов с информацией о компаниях");

    int counter = 0;
    int found = 0;

    foreach (var jsonPath in companyJsons)
    {
        dynamic json = JObject.Parse(File.ReadAllText(jsonPath));
        if (json.open_vacancies > 0)
        {
            if (!Directory.Exists(employeesWithVacanciesFolder))
            {
                Directory.CreateDirectory(employeesWithVacanciesFolder);
            }
            File.WriteAllText(
                Path.Combine(
                    employeesWithVacanciesFolder, $"{json.name}.{json.id}.csv"),
                    String.Format("{0},{1},{2}", json.name, json.id, json.vacancies_url));
            Console.WriteLine($"Найдены открытые вакансии в компании {json.name}. Обработано {counter}, найдено {++found}.");
        }
        counter++;
    }
}

async static Task FilterExactNames(string employeesFolder, string exactEmployeesFolder, string jsonPath, dynamic json)
{
    if (json.found > 0)
    {
        var employeeName = Path.GetFileNameWithoutExtension(jsonPath);
        await ProcessOnePage(employeeName, exactEmployeesFolder, json);

        if (json.pages > 1)
        {
            await ProcessNextPages(employeesFolder, employeeName, exactEmployeesFolder, jsonPath);
        }
    }
}

async static Task ProcessNextPages(string employeesFolder, string employeeName, string exactEmployeesFolder, string jsonPath)
{
    var jsonPartFilenames = Directory.EnumerateFiles(employeesFolder, $"{employeeName}.json.part*");
    foreach (var jsonPartFilename in jsonPartFilenames)
    {
        dynamic json = JObject.Parse(File.ReadAllText(jsonPartFilename));
        await ProcessOnePage(employeeName, exactEmployeesFolder, json);
    }
}

async static Task ProcessOnePage(string employeeName, string exactEmployeesFolder, dynamic json)
{
    foreach (var item in json.items)
    {
        if (String.Compare(item.name.ToString(), employeeName, true) == 0)
        {
            var exactCompanyJsonPath = Path.Combine(exactEmployeesFolder, employeeName + "." + item.id + ".json");
            if (!Directory.Exists(exactEmployeesFolder))
            {
                Directory.CreateDirectory(exactEmployeesFolder);
            }
            File.WriteAllText(exactCompanyJsonPath, item.ToString());
            Console.WriteLine($"Найдено совпадение названия компании {employeeName}");
        }
    }
}

async static Task ExpandEmployersMultipageResponse(string folder, string jsonPath, dynamic json, HttpClient httpClient, TimeSpan delay)
{
    int pages = json.pages;
    var employeeName = Path.GetFileNameWithoutExtension(jsonPath);

    // слишком много компаний с одинаковым или похожим названием нет смысла обрабатывать
    if (pages > 1 && pages < 20)
    {
        int page = 1;
        while (page < pages)
        {
            var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"employers?text={employeeName}&page={page}")).Result;
            await SaveResponse(folder, employeeName, response, page);
            Thread.Sleep(delay);
            page++;
        }
    }
}

async static Task ExpandVacanciesMultipageResponse(string folder, string jsonPath, dynamic json, HttpClient httpClient, TimeSpan delay)
{
    int pages = json.pages;
    var filename = Path.GetFileNameWithoutExtension(jsonPath);
    var parts = filename.Split(".");
    var employeeName = parts[0];
    var empoyeeId = parts[1];
    var baseUrl = "vacancies?employer_id=";

    if (pages > 1)
    {
        int page = 1;
        while (page < pages)
        {
            var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{baseUrl + empoyeeId}&page={page}")).Result;
            await SaveResponse(folder, employeeName, response, page);
            Thread.Sleep(delay);
            page++;
        }
    }
}

static async Task FindCompanies(string[] companyNames, string employeesFolder, string cursorFilename, HttpClient httpClient, TimeSpan delay)
{
    var line = 0;
    if (File.Exists(cursorFilename))
    {
        Console.WriteLine("Найден файл курсора");
        line = int.Parse(File.ReadAllText(cursorFilename));
        Console.WriteLine($"Возобновление со строки {line}");
        companyNames = companyNames.Skip(line).ToArray();
    }

    foreach (var name in companyNames)
    {
        var employeeName = name;
        Thread.Sleep(delay);
        var response = httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"employers?text={employeeName}")).Result;

        if (!Directory.Exists(employeesFolder))
        {
            Directory.CreateDirectory(employeesFolder);
        }

        await SaveResponse(employeesFolder, employeeName, response);

        Console.WriteLine($"Обработана фирма {employeeName}. Обработано всего: {line + 1}");
        File.WriteAllText(cursorFilename, (line++).ToString());
    }
}

static async Task SaveResponse(string folder, string filename, HttpResponseMessage response, int page = 0)
{
    if (response.IsSuccessStatusCode)
    {
        var json = response.Content.ReadAsStringAsync().Result;
        var beautifiedJson = JToken.Parse(json).ToString();
        var jsonFile = Path.Combine(folder, $"{filename}.json{(page == 0 ? String.Empty : ".part" + page.ToString())}");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        if (File.Exists(jsonFile))
        {
            File.Delete(jsonFile);
        }
        await File.AppendAllTextAsync(jsonFile, beautifiedJson);
        Console.WriteLine(beautifiedJson);
    }
    else
    {
        Console.WriteLine(response);
        Console.WriteLine(response.Content);
    }
}
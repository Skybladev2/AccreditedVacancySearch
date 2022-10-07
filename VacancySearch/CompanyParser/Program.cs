// See https://aka.ms/new-console-template for more information
using System.Text.RegularExpressions;

var doubleQuotePattern = ".+?(\"|«)(?<name>.+?)(\"|»)";
var companyTypes = new List<string>() {
    "АКЦИОНЕРНОЕ ОБЩЕСТВО",
    "ОБЩЕСТВО С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ",
    "АВТОНОМНАЯ НЕКОММЕРЧЕСКАЯ ОРГАНИЗАЦИЯ",
    "ФЕДЕРАЛЬНОЕ ГОСУДАРСТВЕННОЕ АВТОНОМНОЕ УЧРЕЖДЕНИЕ НАУКИ",
    "Учреждение",
    "Федеральное государственное бюджетное учреждение науки",
    "государственное унитарное предприятие",
    "ПРОИЗВОДСТВЕННЫЙ КООПЕРАТИВ",
    "САМАРСКАЯ РЕГИОНАЛЬНАЯ ОБЩЕСТВЕННАЯ ОРГАНИЗАЦИЯ СОДЕЙСТВИЯ РАЗВИТИЮ ОБРАЗОВАНИЯ И НАУКИ"
};
var noQuotePattern = $"({String.Join("|", companyTypes.Select(c => "(" + c + ")"))})\\s(?<name>.+)";
var companyNameDoubleQuoteRegex = new Regex(doubleQuotePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
var companyNameNoQuoteRegex = new Regex(noQuotePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

var lines = File.ReadAllLines("CompaniesAll.csv").Skip(1);
Console.WriteLine($"Найдено {lines.Count() - 1} записей");

var noMatchLines = new LinkedList<string>();
var corruptedNames = new LinkedList<string>();

var outputFolder = "output";
var companiesFilteredFilename = Path.Combine(outputFolder, "CompaniesFiltered.csv");

if (File.Exists(companiesFilteredFilename))
{
    File.Delete(companiesFilteredFilename);
}

if (!Directory.Exists(outputFolder))
{
    Directory.CreateDirectory(outputFolder);
}

foreach (var line in lines)
{
    var parts = line.Split(',');
    var namePart = parts[2];
    var doubleQuoteMatch = companyNameDoubleQuoteRegex.Match(namePart);
    if (doubleQuoteMatch.Success)
    {
        var name = doubleQuoteMatch.Groups["name"].Value;
        name = await SaveCompanyName(corruptedNames, companiesFilteredFilename, name);
        continue;
    }
    else
    {
        var noQuoteMatch = companyNameNoQuoteRegex.Match(namePart);
        if (noQuoteMatch.Success)
        {
            var name = noQuoteMatch.Groups["name"].Value;
            await SaveCompanyName(corruptedNames, companiesFilteredFilename, name);
            continue;
        }
    }
    noMatchLines.AddLast(namePart);
}

Console.WriteLine($"Не удалось получить {noMatchLines.Count} названий фирм.");
Console.WriteLine($"{corruptedNames.Count} содержат лишние символы");

foreach (var item in noMatchLines)
{
    Console.WriteLine(item);
}

Console.WriteLine("Завершено. Нажмите любую клавишу.");
Console.ReadKey();

static async Task<string> SaveCompanyName(LinkedList<string> corruptedNames, string companiesFilteredFilename, string name)
{
    if (name.Contains("\"") || name.Contains("«"))
    {
        corruptedNames.AddLast(name);
        name = name.Replace("\"", "").Replace("«", "").Replace("/", " ");
    }
    await File.AppendAllTextAsync(companiesFilteredFilename, name + Environment.NewLine);
    return name;
}
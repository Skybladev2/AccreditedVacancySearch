using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadHunterSearcher
{
    public class Vacancy
    {
        public string CompanyName { get; set; }
        public string CompanyId { get; set; }
        public string CompanyUrl { get; set; }
        public bool CompanyTrusted { get; set; }
        public int? SalaryFrom { get; set; }
        public int? SalaryTo { get; set; }
        public string SalaryCurrency { get; set; }
        public bool? SalaryGross { get; set; }
        public string VacancyName { get; set; }
        public string VacancyId { get; set; }
        public string VacancyUrl { get; set; }
        public string AreaId { get; set; }
        public string AreaName { get; set; }
        public string Experience { get; set; }
        public string ScheduleId { get; set; }
        public string ScheduleName { get; set; }
        public string EmploymentId { get; set; }
        public string EmploymentName { get; set; }
        public string Description { get; set; }
        public string Specialisations { get; set; }
        public string ProfessionalRoles { get; set; }
        public DateTimeOffset PublishedAt { get; set; }
        public string Address { get; set; }
    }
}

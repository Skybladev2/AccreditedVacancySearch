using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadHunterSearcher
{
    public record VacancyEssentialData
    {
        public string CompanyName { get; set; }
        public string CompanyId { get; set; }
        public string VacancyName { get; set; }        
        public string Description { get; set; }
    }
}

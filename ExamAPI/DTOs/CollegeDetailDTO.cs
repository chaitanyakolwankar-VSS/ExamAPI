namespace ExamAPI.DTOs
{
    public class CollegeDetailDTO
    {
        public Guid CollegeId { get; set; }
        public string Name { get; set; }
        public string CollegeCode { get; set; }
        public string CollegeCenter { get; set; }
        public string Address { get; set; } = string.Empty;
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }
        public string LogoUrl { get; set; }
        public string BannerUrl { get; set; }
        public bool IsDeleted { get; set; }

    }

    public class CreateCollegeDTO
    {
        public string Name { get; set; }
        public string CollegeCode { get; set; }

        public string CollegeCenter { get; set; }
        public string? Address { get; set; }
      
        public string ContactEmail { get; set; }
        public string ContactPhone { get; set; }

        public IFormFile? Logo { get; set; }
        public IFormFile? Banner { get; set; }
    }
}

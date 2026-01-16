namespace ExamAPI.DTOs
{
    public class CollegeDetailDTO
    {
    }

    public class CreateCollegeDTO
    {
        public string Name { get; set; }
        public string CollegeCode { get; set; }

        public string CollegeCenter { get; set; }
        public string? Address { get; set; }
        public string? LogoUrl { get; set; }
        public string? LogoBannerUrl { get; set; }
        public string ContactEmail { get; set; }

        public string ContactPhone { get; set; }
    }
}

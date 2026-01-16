using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ExamAPI.Services.CollegeDetail
{
    public class CollegeDetailService:ICollegeDetailService
    {
        private readonly ApplicationDbContext _context;

        public CollegeDetailService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Guid> CreateAsync(CreateCollegeDTO dto)
        {
            var college = new College
            {
                CollegeId = Guid.NewGuid(),
                Name=dto.Name,
                CollegeCode=dto.CollegeCode,
                CollegeCenter=dto.CollegeCenter,
                Address=dto.Address,
                LogoUrl=dto.LogoUrl,
                LogoBannerUrl=dto.LogoBannerUrl,
                ContactEmail=dto.ContactEmail,
                ContactPhone=dto.ContactPhone,
                CreatedAt=DateTime.UtcNow,
                IsDeleted=false

            };

            _context.Colleges.Add(college);
            await _context.SaveChangesAsync();

            return college.CollegeId;
        }
    }
}

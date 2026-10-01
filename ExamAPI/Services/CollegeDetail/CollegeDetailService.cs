using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.CollegeDetail
{
    public class CollegeDetailService : ICollegeDetailService
    {
        private readonly ApplicationDbContext _context;
        private readonly ExamAPI.Services.Files.IFileStorage _storage;
        public CollegeDetailService(ApplicationDbContext context, ExamAPI.Services.Files.IFileStorage storage)
        {
            _context = context;
            _storage = storage;
        }

        public async Task<CollegeDetailDTO?> GetAsync()
        {
            return await _context.Colleges
                .AsNoTracking()
                .Where(x => x.IsDeleted == false)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new CollegeDetailDTO
                {
                    CollegeId = x.CollegeId,
                    Name = x.Name,
                    CollegeCode = x.CollegeCode,
                    CollegeCenter = x.CollegeCenter,
                    Address = x.Address,
                    ContactEmail = x.ContactEmail,
                    ContactPhone = x.ContactPhone,
                    LogoUrl = x.LogoUrl,
                    BannerUrl = x.LogoBannerUrl,
                    IsDeleted = x.IsDeleted
                })
                .FirstOrDefaultAsync();
        }

        public async Task<Guid> CreateAsync(CreateCollegeDTO dto)
        {
            string? logoUrl = null;
            string? bannerUrl = null;

            if (dto.Logo != null)
            {
                //var uploadParams = new ImageUploadParams()
                //{
                //    File = new FileDescription(dto.Logo.FileName, dto.Logo.OpenReadStream()),
                //    Folder = "college_logos"
                //};
                //var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                //logoUrl = uploadResult.SecureUrl.ToString();
                ValidateImage(dto.Logo,"Logo");
                logoUrl = await SaveImageToServerAsync(dto.Logo, ExamAPI.Services.Files.FileStorage.CollegeLogosFolder);
            }

            if (dto.Banner != null)
            {
                //var uploadParams = new ImageUploadParams()
                //{
                //    File = new FileDescription(dto.Banner.FileName, dto.Banner.OpenReadStream()),
                //    Folder = "college_banners"
                //};
                //var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                //bannerUrl = uploadResult.SecureUrl.ToString();
                ValidateImage(dto.Banner, "Banner");
                bannerUrl = await SaveImageToServerAsync(dto.Banner, ExamAPI.Services.Files.FileStorage.CollegeBannersFolder);
            }

            var college = new College
            {
                CollegeId = Guid.NewGuid(),
                Name = dto.Name,
                CollegeCode = dto.CollegeCode,
                CollegeCenter = dto.CollegeCenter,
                Address = dto.Address,
                LogoUrl = logoUrl,
                LogoBannerUrl = bannerUrl,
                ContactEmail = dto.ContactEmail,
                ContactPhone = dto.ContactPhone,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false

            };

            _context.Colleges.Add(college);
            await _context.SaveChangesAsync();

            return college.CollegeId;
        }

        public async Task<Guid> UpdateAsync(Guid id, CreateCollegeDTO dto)
        {
            // Not FindAsync: it bypasses the tenant query filter and would let a user edit another
            // college by id.
            var college = await _context.Colleges.FirstOrDefaultAsync(c => c.CollegeId == id);
            if (college == null)
                throw new Exception("College Not Found");

            if (dto.Logo != null)
            {
                ValidateImage(dto.Logo, "Logo");
                DeleteImageFromServer(college.LogoUrl);
                college.LogoUrl = await SaveImageToServerAsync(dto.Logo, ExamAPI.Services.Files.FileStorage.CollegeLogosFolder);
                //var uploadParams = new ImageUploadParams
                //{
                //    File = new FileDescription(dto.Logo.FileName, dto.Logo.OpenReadStream()),
                //    Folder = "college_logos"
                //};
                //var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                //college.LogoUrl = uploadResult.SecureUrl.ToString();
            }

            if (dto.Banner != null)
            {
                ValidateImage(dto.Banner, "Banner");
                //var uploadParams = new ImageUploadParams
                //{
                //    File = new FileDescription(dto.Banner.FileName, dto.Banner.OpenReadStream()),
                //    Folder = "college_banners"
                //};
                //var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                //college.LogoBannerUrl = uploadResult.SecureUrl.ToString();
                DeleteImageFromServer(college.LogoBannerUrl);
                college.LogoBannerUrl = await SaveImageToServerAsync(dto.Banner, ExamAPI.Services.Files.FileStorage.CollegeBannersFolder);
            }

            college.Name = dto.Name;
            college.Address = dto.Address;
            college.CollegeCode = dto.CollegeCode;
            college.CollegeCenter = dto.CollegeCenter;
            college.ContactEmail = dto.ContactEmail;
            college.ContactPhone = dto.ContactPhone;
            college.UpdatedAt = DateTime.UtcNow;

            _context.Colleges.Update(college);
            await _context.SaveChangesAsync();

            return college.CollegeId;

        }


        private const long MaxImageSize = 2 * 1024 * 1024;
        private void ValidateImage(IFormFile file, string fieldName)
        {
            if (file == null || file.Length == 0)
                return; 

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };

            if (!allowedTypes.Contains(file.ContentType))
                throw new ArgumentException($"{fieldName} format is not supported");

            if (file.Length > 2 * 1024 * 1024)
                throw new ArgumentException($"{fieldName} must be less than 2MB");
        }

        private async Task<string> SaveImageToServerAsync(IFormFile file, string subFolder)
        {
            // Stored in the persistent, non-public upload root (Storage:UploadsRoot), never under
            // wwwroot. Create and Update now share one folder (the old code wrote to Clg_details on
            // create and Clg_detail on update).
            // The client filename only contributes its extension: an arbitrary name is not needed
            // and would be attacker-controlled input to the file path.
            var extension = Path.GetExtension(file.FileName);
            if (extension.Length == 0 || extension.Length > 6 || !extension.Skip(1).All(char.IsLetterOrDigit))
                extension = ".png";

            var fileName = $"{Guid.NewGuid()}{extension.ToLowerInvariant()}";

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            return await _storage.SaveAsync(buffer.ToArray(), subFolder, fileName);
        }

        private void DeleteImageFromServer(string? storedPath)
        {
            if (string.IsNullOrEmpty(storedPath)) return;
            _storage.Delete(storedPath);
        }
    }
}

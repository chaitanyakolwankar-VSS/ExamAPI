using ExamAPI.Models;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class StudentMasterDto
    {

        public required string Name { get; set; }
        public Guid CourseId { get; set; }
    }
    public class Savedata
    {
<<<<<<< HEAD
        public string? StudentId { get; set; }
        public Guid Id { get; set; }
        public Guid CourseId { get; set; }
=======
        public Guid Id { get; set; }
        public Guid CourseId { get; set; } 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? StudentPRN { get; set; }
        public string? PhotoUrl { get; set; }
<<<<<<< HEAD
        public string? SignUrl { get; set; }
=======
        public string? SignUrl { get; set; } 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public bool Dyslexia { get; set; }
        public string? SemesterId { get; set; }
        public string? AYID { get; set; }
    }

    public class FetchData
    {
        public Guid CourseId { get; set; }
        public required string Name { get; set; }
        public required string FirstName { get; set; }
<<<<<<< HEAD
        public string? MiddleName { get; set; }
=======
        public string?MiddleName { get; set; }
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa
        public required string LastName { get; set; }
        public string? StudentPRN { get; set; }
        public string? SemesterId { get; set; }
        public string? AYID { get; set; }
        public string StudentId { get; set; }
        public string? StudentName { get; set; }
    }
    public class Searchbyname
    {
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public required string LastName { get; set; }
        public string StudentPRN { get; set; }
        public string? SemesterId { get; set; }
        public string? AYID { get; set; }
        public string StudentId { get; set; }
        public string? StudentName { get; set; }
    }
<<<<<<< HEAD

    public class StudentExcelDto
    {
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Category { get; set; }
        public string? StudentPRN { get; set; }
        public string? Gender { get; set; }

    }

    public class StudentImportDto
    {
        public Guid CourseId { get; set; }
        public string SemesterId { get; set; }
        public IFormFile File { get; set; }
    }

    public class ImportResultDto
    {
        public bool IsSuccess { get; set; }
        public int SuccessCount { get; set; }
        public List<RowError> Errors { get; set; } = new();
    }

    public class RowError
    {
        public int RowNumber { get; set; }
        public string Message { get; set; }
    }
}

=======
}
 
>>>>>>> 49b1e581466adc2308420e40d667e717b5a343fa

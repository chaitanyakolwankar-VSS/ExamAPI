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
        public Guid Id { get; set; }
        public Guid CourseId { get; set; } 
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? StudentPRN { get; set; }
        public string? PhotoUrl { get; set; }
        public string? SignUrl { get; set; } 
        public bool Dyslexia { get; set; }
        public string? SemesterId { get; set; }
        public string? AYID { get; set; }
    }

    public class FetchData
    {
        public Guid CourseId { get; set; }
        public required string Name { get; set; }
        public required string FirstName { get; set; }
        public string?MiddleName { get; set; }
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
}
 
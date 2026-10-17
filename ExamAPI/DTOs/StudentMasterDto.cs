using ExamAPI.Models;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    public class Savedata
    {

        public string? StudentId { get; set; }
        public Guid Id { get; set; }
        public Guid CourseId { get; set; }
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? MotherName { get; set; }
        /// <summary>"E" Engineering, "D" Diploma, "M" MCA.</summary>
        public string? StudentType { get; set; }
        public string? Gender { get; set; }
        public string? Category { get; set; }
        public string? StudentPRN { get; set; }
        public string? PhotoUrl { get; set; }
        public string? SignUrl { get; set; }
        public bool Dyslexia { get; set; }
        public string? SemesterId { get; set; }
        public string? Pattern { get; set; }
        public Guid AYID { get; set; }

        /// <summary>Required for a new student; task links and OTPs are sent here.</summary>
        public string? Email { get; set; }
        /// <summary>Read only: Verified, Flagged or null (not checked yet), and why it is flagged.</summary>
        public string? EmailStatus { get; set; }
        public string? EmailFlagReason { get; set; }
    }

    public class FetchData
    {
        public Guid CourseId { get; set; }
        public required string Name { get; set; }
        public required string FirstName { get; set; }
        public string?MiddleName { get; set; }
        public required string LastName { get; set; }
        public string? MotherName { get; set; }
        /// <summary>"E" Engineering, "D" Diploma, "M" MCA.</summary>
        public string? StudentType { get; set; }
        public string? StudentPRN { get; set; }
        public string? SemesterId { get; set; }
        public Guid AYID { get; set; }
        public string? StudentId { get; set; }
        public string? StudentName { get; set; }
        public string? Pattern { get; set; }
        public bool Dyslexia { get; set; }
        /// <summary>Stored paths (load via GET /api/Files); null when not uploaded.</summary>
        public string? PhotoUrl { get; set; }
        public string? SignUrl { get; set; }
    }

    /// <summary>Photo and/or signature for one existing student, as data: URLs; a null field is left unchanged.</summary>
    public class StudentImagesDto
    {
        public required string StudentId { get; set; }
        public string? Photo { get; set; }
        public string? Sign { get; set; }
    }

    public class Searchbyname
    {
        public  string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public  string? LastName { get; set; }
        public string StudentPRN { get; set; }
        public string? SemesterId { get; set; }
        public Guid AYID { get; set; }
        public string StudentId { get; set; }
        public string? StudentName { get; set; }
    }


    public class StudentExcelDto
    {
        public required string FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? Category { get; set; }
        public string? StudentPRN { get; set; }
        public string? Gender { get; set; }

    }
     public class StudExcelDto
    {
        public Guid CourseId { get; set; }
        public string SemesterId { get; set; }
        public string Pattern { get; set; }
        public Guid AYID { get; set; }

    }

    public class StudentImportDto
    {
        public IFormFile File { get; set; }
        public Guid CourseId { get; set; }
        public string SemesterId { get; set; }
        public string Pattern { get; set; }
        public Guid AYID { get; set; }

        /// <summary>True: only check the file and answer what would happen per row; nothing is saved.</summary>
        public bool Preview { get; set; }
    }

    /// <summary>What an import does (or would do) with one row of the sheet.</summary>
    public class ImportRowResult
    {
        public int Row { get; set; }
        public string? StudentId { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        /// <summary><see cref="ImportRowStatuses"/>.</summary>
        public string Status { get; set; } = ImportRowStatuses.Ok;
        public string? Note { get; set; }
    }

    public static class ImportRowStatuses
    {
        public const string Ok = "OK";
        /// <summary>Saved, but staff should check the email (e.g. outside the college's domains).</summary>
        public const string Flagged = "Flagged";
        public const string Error = "Error";
        /// <summary>Email sheet: the student already has this email.</summary>
        public const string Unchanged = "Unchanged";
        /// <summary>Email sheet: no email typed in; the student is left as is.</summary>
        public const string Skipped = "Skipped";
    }

    /// <summary>Answer of an import or email-sheet upload. Saved is false for a preview and whenever there are errors.</summary>
    public class StudentImportResult
    {
        public bool Success { get; set; }
        public bool Saved { get; set; }
        public string Message { get; set; } = "";
        /// <summary>One line per problem, e.g. "Row 5: gender must be Male or Female."</summary>
        public List<string> Errors { get; set; } = new();
        public List<ImportRowResult> Rows { get; set; } = new();
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

  
    public class ExamDetailsResultDto
    {
        public string StudentId { get; set; }
        public string ExamType { get; set; }
        public string Remark { get; set; }
        public string ExamName { get; set; }
        public string SemesterId { get; set; }
        public string Pattern { get; set; }
        public string ActionStatus { get; set; }
        public string MarksId { get; set; }
    }

}

using System.Text.Json.Serialization;

namespace ExamAPI.DTOs
{
 

    public class ExamOptionDto
    {
        public Guid Ayid { get; set; }
        public Guid? CourseId { get; set; }
        public string? Value { get; set; }
        public string? Label { get; set; }
   
    }


    public class CategoryOptionDto
    {
        public string? Value { get; set; }
        public string? Label { get; set; }
    }
    public class BranchOptionDto
    {
        public string? Value { get; set; }
        public string? Label { get; set; }
    }

    public class SaveFees
    {
        public string Ayid { get; set; }
        public string ExamId { get; set; }
        public string ExamType { get; set; }
        
        public string SemId { get; set; }
        public string CourseId { get; set; }
        public string Category { get; set; }
        public string SubCount { get; set; }
        public List<FeeAmountDto> Amount { get; set; }
    }

    public class DeleteFeesDto
    {
        public string ExamId{ get; set; }
        public string CourseId { get; set; }
        public string Category { get; set; }
    }
    public class GetFeesDto
    {
  
        public string ExamId { get; set; }
        public string Category { get; set; }
        public string? CourseId { get; set; }
        public string SemId { get; set; }
        public string ExamType { get; set; }
    }
    public class FeesRecordDto
    {
      
        public string? ExamId { get; set; }
        public string? SemId { get; set; }
        public string? ExamType { get; set; }
        public string? CourseId{ get; set; }
        public string? Category { get; set; }
        public string SubCount { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    public class FeeAmountDto
    {
        [JsonPropertyName("srNo")] 
        public string RowSubCount { get; set; }
        public decimal Amount { get; set; }
  
    }
}
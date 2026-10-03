using System;
using System.ComponentModel.DataAnnotations;

namespace ExamAPI.DTOs
{
    // --- Pattern DTOs ---
    public class PatternUpdateDto
    {
        [Required]
        public Guid PatternId { get; set; }
        [Required]
        public string PatternName { get; set; }
        public string Description { get; set; }
    }
    public class PatternCreateDto
    {
        public string PatternName { get; set; }
        public string Description { get; set; }
    }

    public class PatternDto
    {
        public Guid PatternId { get; set; }
        public string PatternName { get; set; }
        public string Description { get; set; }
    }

    // --- RuleSet DTOs ---
    public class RuleSetDto
    {
        public Guid RuleSetId { get; set; }
        public string Name { get; set; }
        public string? ExamType { get; set; }
        public bool IsActive { get; set; }
        public Guid PatternId { get; set; }
        public Guid? GradeMasterId { get; set; }
    }

    public class RuleSetCreateDto
    {
        [Required]
        public string Name { get; set; }
        public string? ExamType { get; set; }
        public bool IsActive { get; set; } = true;
        [Required]
        public Guid PatternId { get; set; }
        public Guid? GradeMasterId { get; set; }
    }

    public class RuleSetUpdateDto
    {
        [Required]
        public Guid RuleSetId { get; set; }
        [Required]
        public string Name { get; set; }
        public string? ExamType { get; set; }
        public bool IsActive { get; set; }
        public Guid? GradeMasterId { get; set; }
    }

    // --- GradeMaster DTOs ---
    public class GradeThresholdDto
    {
        public Guid? ThresholdId { get; set; }
        public string Grade { get; set; }
        public int GradePoint { get; set; }
        public decimal MinPercentage { get; set; }
        public decimal MaxPercentage { get; set; }
        public string PerformanceRemark { get; set; }
    }

    public class GradeMasterDto
    {
        public Guid GradeMasterId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<GradeThresholdDto> Thresholds { get; set; }
    }

    public class GradeMasterCreateDto
    {
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
        public List<GradeThresholdDto> Thresholds { get; set; }
    }
    
    public class GradeMasterUpdateDto
    {
        [Required]
        public Guid GradeMasterId { get; set; }
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
        public List<GradeThresholdDto> Thresholds { get; set; }
    }

    // --- Rule DTOs ---
    public class RuleConditionDto
    {
        public Guid ConditionId { get; set; }
        public string FactName { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
    }

    public class RuleConditionCreateDto
    {
        public Guid? ConditionId { get; set; }
        public string FactName { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
    }

    public class RuleActionDto
    {
        public Guid ActionId { get; set; }
        public string? ActionType { get; set; }
        public string? CalculationMode { get; set; }
        public string? Param1Type { get; set; }
        public decimal? Param1Value { get; set; }
        public string? Param2Type { get; set; }
        public decimal? Param2Value { get; set; }
        public decimal? MaxLimit { get; set; }
        public string? Expression { get; set; }
        public int? MaxTargetCount { get; set; }
        public string? Target { get; set; }
    }

    public class RuleActionCreateDto
    {
        public Guid? ActionId { get; set; }
        public string? ActionType { get; set; }
        public string? CalculationMode { get; set; }
        public string? Param1Type { get; set; }
        public decimal? Param1Value { get; set; }
        public string? Param2Type { get; set; }
        public decimal? Param2Value { get; set; }
        public decimal? MaxLimit { get; set; }
        public string? Expression { get; set; }
        public int? MaxTargetCount { get; set; }
        public string? Target { get; set; }
    }

    public class RuleDto
    {
        public Guid RuleId { get; set; }
        public string Name { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public bool StopOnSuccess { get; set; }
        public string? OrdinanceSymbol { get; set; }
        public Guid RuleSetId { get; set; }
        public List<RuleConditionDto> Conditions { get; set; }
        public List<RuleActionDto> Actions { get; set; }
    }

    public class RuleCreateDto
    {
        [Required]
        public Guid RuleSetId { get; set; }
        [Required]
        public string Name { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool StopOnSuccess { get; set; }
        public string? OrdinanceSymbol { get; set; }
        public List<RuleConditionCreateDto> Conditions { get; set; }
        public List<RuleActionCreateDto> Actions { get; set; }
    }

    public class RuleUpdateDto
    {
        [Required]
        public Guid RuleId { get; set; }
        [Required]
        public string Name { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
        public bool StopOnSuccess { get; set; }
        public string? OrdinanceSymbol { get; set; }
        public List<RuleConditionCreateDto> Conditions { get; set; }
        public List<RuleActionCreateDto> Actions { get; set; }
    }

    public class EngineMetadataDto
    {
        public List<string> Facts { get; set; } = new List<string>();
        public List<string> Actions { get; set; } = new List<string>();
        public List<string> Operators { get; set; } = new List<string> { "==", "!=", ">", ">=", "<", "<=" };

        /// <summary>Subject-scope tokens a rule action's Target may use (RuleAction.Target).</summary>
        public List<string> SubjectScopes { get; set; } = new List<string>();

        /// <summary>Configured head labels (SubjectCredits.HeadType), for the Target multiselect.</summary>
        public List<string> HeadTypes { get; set; } = new List<string>();
    }
}
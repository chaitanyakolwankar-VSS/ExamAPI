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
        public bool IsActive { get; set; }
        public Guid PatternId { get; set; }
    }

    public class RuleSetCreateDto
    {
        [Required]
        public string Name { get; set; }
        public bool IsActive { get; set; } = true;
        [Required]
        public Guid PatternId { get; set; }
    }

    public class RuleSetUpdateDto
    {
        [Required]
        public Guid RuleSetId { get; set; }
        [Required]
        public string Name { get; set; }
        public bool IsActive { get; set; }
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
        public string ActionType { get; set; }
        public string CalculationMode { get; set; }
        public string Param1Type { get; set; }
        public decimal? Param1Value { get; set; }
        public string Param2Type { get; set; }
        public decimal? Param2Value { get; set; }
        public decimal? MaxLimit { get; set; }
        public int? MaxTargetCount { get; set; }
        public string Target { get; set; }
    }

    public class RuleActionCreateDto
    {
        public Guid? ActionId { get; set; }
        public string ActionType { get; set; }
        public string CalculationMode { get; set; }
        public string Param1Type { get; set; }
        public decimal? Param1Value { get; set; }
        public string Param2Type { get; set; }
        public decimal? Param2Value { get; set; }
        public decimal? MaxLimit { get; set; }
        public int? MaxTargetCount { get; set; }
        public string Target { get; set; }
    }

    public class RuleDto
    {
        public Guid RuleId { get; set; }
        public string Name { get; set; }
        public int Priority { get; set; }
        public bool IsEnabled { get; set; }
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
        public List<RuleConditionCreateDto> Conditions { get; set; }
        public List<RuleActionCreateDto> Actions { get; set; }
    }
}
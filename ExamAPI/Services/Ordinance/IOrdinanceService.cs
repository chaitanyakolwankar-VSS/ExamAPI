using ExamAPI.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExamAPI.Services.Ordinance
{
    public interface IOrdinanceService
    {
        // Pattern Methods
        Task<IEnumerable<PatternDto>> GetPatternsAsync();
        Task<PatternDto> GetPatternByIdAsync(Guid patternId);
        Task<PatternDto> CreatePatternAsync(PatternCreateDto patternDto, Guid collegeId);
        Task<bool> UpdatePatternAsync(PatternUpdateDto patternDto);
        Task<bool> DeletePatternAsync(Guid patternId);

        // RuleSet Methods
        Task<IEnumerable<RuleSetDto>> GetRuleSetsByPatternAsync(Guid patternId);
        Task<RuleSetDto> CreateRuleSetAsync(RuleSetCreateDto ruleSetDto);
        Task<bool> UpdateRuleSetAsync(RuleSetUpdateDto ruleSetDto);
        Task<bool> DeleteRuleSetAsync(Guid ruleSetId);

        // Rule Methods
        Task<IEnumerable<RuleDto>> GetRulesByRuleSetAsync(Guid ruleSetId);
        Task<RuleDto> CreateRuleAsync(RuleCreateDto ruleDto);
        Task<bool> UpdateRuleAsync(RuleUpdateDto ruleDto);
        Task<bool> DeleteRuleAsync(Guid ruleId);
    }
}

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

        // Grade Master Methods
        Task<IEnumerable<GradeMasterDto>> GetGradeMastersAsync();
        Task<GradeMasterDto> CreateGradeMasterAsync(GradeMasterCreateDto gradeDto);
        Task<bool> UpdateGradeMasterAsync(GradeMasterUpdateDto gradeDto);
        Task<bool> DeleteGradeMasterAsync(Guid gradeMasterId);

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

        // Metadata Methods
        Task<EngineMetadataDto> GetEngineMetadataAsync();
    }
}

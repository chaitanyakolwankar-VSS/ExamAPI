using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExamAPI.Services.Result.Engine;

namespace ExamAPI.Services.Ordinance
{
    public class OrdinanceService : IOrdinanceService
    {
        private readonly ApplicationDbContext _context;
        private readonly EngineRegistry _engineRegistry;

        public OrdinanceService(ApplicationDbContext context, EngineRegistry engineRegistry)
        {
            _context = context;
            _engineRegistry = engineRegistry;
        }

        // === Pattern Methods ===
        public async Task<IEnumerable<PatternDto>> GetPatternsAsync()
        {
            return await _context.PatternMasters
                .Where(p => !p.IsDeleted)
                .Select(p => new PatternDto
                {
                    PatternId = p.PatternId,
                    PatternName = p.PatternName,
                    Description = p.Description
                })
                .ToListAsync();
        }

        public async Task<PatternDto> GetPatternByIdAsync(Guid patternId)
        {
            var pattern = await _context.PatternMasters
                .Where(p => p.PatternId == patternId && !p.IsDeleted)
                .Select(p => new PatternDto
                {
                    PatternId = p.PatternId,
                    PatternName = p.PatternName,
                    Description = p.Description
                })
                .FirstOrDefaultAsync();

            return pattern;
        }

        public async Task<PatternDto> CreatePatternAsync(PatternCreateDto patternDto, Guid collegeId)
        {
            var pattern = new PatternMaster
            {
                PatternId = Guid.NewGuid(),
                PatternName = patternDto.PatternName,
                Description = patternDto.Description,
                CollegeId = collegeId,
                CreatedAt = DateTime.UtcNow,
            };

            _context.PatternMasters.Add(pattern);
            await _context.SaveChangesAsync();

            return new PatternDto
            {
                PatternId = pattern.PatternId,
                PatternName = pattern.PatternName,
                Description = pattern.Description
            };
        }

        public async Task<bool> UpdatePatternAsync(PatternUpdateDto patternDto)
        {
            var existingPattern = await _context.PatternMasters
                .FirstOrDefaultAsync(p => p.PatternId == patternDto.PatternId);

            if (existingPattern == null || existingPattern.IsDeleted) return false;

            existingPattern.PatternName = patternDto.PatternName;
            existingPattern.Description = patternDto.Description;
            existingPattern.UpdatedAt = DateTime.UtcNow;

            _context.PatternMasters.Update(existingPattern);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeletePatternAsync(Guid patternId)
        {
            var existingPattern = await _context.PatternMasters
                .FirstOrDefaultAsync(p => p.PatternId == patternId);

            if (existingPattern == null || existingPattern.IsDeleted) return false;

            existingPattern.IsDeleted = true;
            existingPattern.DeletedAt = DateTime.UtcNow;

            _context.PatternMasters.Update(existingPattern);
            await _context.SaveChangesAsync();
            return true;
        }

        // === Grade Master Methods ===
        public async Task<IEnumerable<GradeMasterDto>> GetGradeMastersAsync()
        {
            var grades = await _context.GradeMasters
                .Include(g => g.Thresholds)
                .Where(g => !g.IsDeleted)
                .ToListAsync();

            return grades.Select(g => new GradeMasterDto
            {
                GradeMasterId = g.GradeMasterId,
                Name = g.Name,
                Description = g.Description,
                Thresholds = g.Thresholds?.Where(t => !t.IsDeleted).Select(t => new GradeThresholdDto
                {
                    ThresholdId = t.ThresholdId,
                    Grade = t.Grade,
                    GradePoint = t.GradePoint,
                    MinPercentage = t.MinPercentage,
                    MaxPercentage = t.MaxPercentage,
                    PerformanceRemark = t.PerformanceRemark
                }).OrderByDescending(t => t.MinPercentage).ToList() ?? new List<GradeThresholdDto>()
            });
        }

        public async Task<GradeMasterDto> CreateGradeMasterAsync(GradeMasterCreateDto gradeDto)
        {
            var gradeMaster = new GradeMaster
            {
                GradeMasterId = Guid.NewGuid(),
                Name = gradeDto.Name,
                Description = gradeDto.Description,
                CreatedAt = DateTime.UtcNow,
                Thresholds = gradeDto.Thresholds?.Select(t => new GradeThreshold
                {
                    ThresholdId = Guid.NewGuid(),
                    Grade = t.Grade,
                    GradePoint = t.GradePoint,
                    MinPercentage = t.MinPercentage,
                    MaxPercentage = t.MaxPercentage,
                    PerformanceRemark = t.PerformanceRemark,
                    CreatedAt = DateTime.UtcNow
                }).ToList()
            };

            _context.GradeMasters.Add(gradeMaster);
            await _context.SaveChangesAsync();

            return new GradeMasterDto
            {
                GradeMasterId = gradeMaster.GradeMasterId,
                Name = gradeMaster.Name,
                Description = gradeMaster.Description,
                Thresholds = gradeMaster.Thresholds?.Select(t => new GradeThresholdDto
                {
                    ThresholdId = t.ThresholdId,
                    Grade = t.Grade,
                    GradePoint = t.GradePoint,
                    MinPercentage = t.MinPercentage,
                    MaxPercentage = t.MaxPercentage,
                    PerformanceRemark = t.PerformanceRemark
                }).OrderByDescending(t => t.MinPercentage).ToList() ?? new List<GradeThresholdDto>()
            };
        }

        public async Task<bool> UpdateGradeMasterAsync(GradeMasterUpdateDto gradeDto)
        {
            var existingGradeMaster = await _context.GradeMasters
                .Include(g => g.Thresholds.Where(t => !t.IsDeleted))
                .FirstOrDefaultAsync(g => g.GradeMasterId == gradeDto.GradeMasterId);

            if (existingGradeMaster == null || existingGradeMaster.IsDeleted) return false;

            existingGradeMaster.Name = gradeDto.Name;
            existingGradeMaster.Description = gradeDto.Description;
            existingGradeMaster.UpdatedAt = DateTime.UtcNow;

            var existingThresholds = existingGradeMaster.Thresholds.ToList();
            var incomingThresholds = gradeDto.Thresholds ?? new List<GradeThresholdDto>();
            var processedThresholdIds = new HashSet<Guid>();

            foreach (var tDto in incomingThresholds)
            {
                if (tDto.ThresholdId.HasValue)
                {
                    var existingThreshold = existingThresholds.FirstOrDefault(t => t.ThresholdId == tDto.ThresholdId.Value);
                    if (existingThreshold != null)
                    {
                        existingThreshold.Grade = tDto.Grade;
                        existingThreshold.GradePoint = tDto.GradePoint;
                        existingThreshold.MinPercentage = tDto.MinPercentage;
                        existingThreshold.MaxPercentage = tDto.MaxPercentage;
                        existingThreshold.PerformanceRemark = tDto.PerformanceRemark;
                        existingThreshold.UpdatedAt = DateTime.UtcNow;

                        processedThresholdIds.Add(existingThreshold.ThresholdId);
                        _context.Entry(existingThreshold).State = EntityState.Modified;
                        continue;
                    }
                }

                var newThreshold = new GradeThreshold
                {
                    ThresholdId = Guid.NewGuid(),
                    Grade = tDto.Grade,
                    GradePoint = tDto.GradePoint,
                    MinPercentage = tDto.MinPercentage,
                    MaxPercentage = tDto.MaxPercentage,
                    PerformanceRemark = tDto.PerformanceRemark,
                    CreatedAt = DateTime.UtcNow,
                    GradeMasterId = existingGradeMaster.GradeMasterId
                };
                _context.GradeThresholds.Add(newThreshold);
            }

            foreach (var threshold in existingThresholds)
            {
                if (!processedThresholdIds.Contains(threshold.ThresholdId))
                {
                    threshold.IsDeleted = true;
                    threshold.DeletedAt = DateTime.UtcNow;
                    _context.Entry(threshold).State = EntityState.Modified;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteGradeMasterAsync(Guid gradeMasterId)
        {
            var existingGradeMaster = await _context.GradeMasters
                .Include(g => g.Thresholds)
                .FirstOrDefaultAsync(g => g.GradeMasterId == gradeMasterId);

            if (existingGradeMaster == null || existingGradeMaster.IsDeleted) return false;

            existingGradeMaster.IsDeleted = true;
            existingGradeMaster.DeletedAt = DateTime.UtcNow;

            if (existingGradeMaster.Thresholds != null)
            {
                foreach (var threshold in existingGradeMaster.Thresholds)
                {
                    if (!threshold.IsDeleted)
                    {
                        threshold.IsDeleted = true;
                        threshold.DeletedAt = DateTime.UtcNow;
                        _context.Entry(threshold).State = EntityState.Modified;
                    }
                }
            }

            _context.GradeMasters.Update(existingGradeMaster);
            await _context.SaveChangesAsync();
            return true;
        }

        // === RuleSet Methods ===
        public async Task<IEnumerable<RuleSetDto>> GetRuleSetsByPatternAsync(Guid patternId)
        {
            return await _context.RuleSets
                .Where(rs => rs.PatternId == patternId && !rs.IsDeleted)
                .Select(rs => new RuleSetDto
                {
                    RuleSetId = rs.RuleSetId,
                    Name = rs.Name,
                    ExamType = rs.ExamType,
                    IsActive = rs.IsActive,
                    PatternId = rs.PatternId,
                    GradeMasterId = rs.GradeMasterId
                })
                .ToListAsync();
        }

        public async Task<RuleSetDto> CreateRuleSetAsync(RuleSetCreateDto ruleSetDto)
        {
            var ruleSet = new RuleSet
            {
                RuleSetId = Guid.NewGuid(),
                Name = ruleSetDto.Name,
                ExamType = ruleSetDto.ExamType,
                IsActive = ruleSetDto.IsActive,
                PatternId = ruleSetDto.PatternId,
                GradeMasterId = ruleSetDto.GradeMasterId,
                CreatedAt = DateTime.UtcNow
            };

            _context.RuleSets.Add(ruleSet);
            await _context.SaveChangesAsync();

            return new RuleSetDto
            {
                RuleSetId = ruleSet.RuleSetId,
                Name = ruleSet.Name,
                ExamType = ruleSet.ExamType,
                IsActive = ruleSet.IsActive,
                PatternId = ruleSet.PatternId,
                GradeMasterId = ruleSet.GradeMasterId
            };
        }

        public async Task<bool> UpdateRuleSetAsync(RuleSetUpdateDto ruleSetDto)
        {
            var existingRuleSet = await _context.RuleSets
                .FirstOrDefaultAsync(rs => rs.RuleSetId == ruleSetDto.RuleSetId);

            if (existingRuleSet == null || existingRuleSet.IsDeleted) return false;

            existingRuleSet.Name = ruleSetDto.Name;
            existingRuleSet.ExamType = ruleSetDto.ExamType;
            existingRuleSet.IsActive = ruleSetDto.IsActive;
            existingRuleSet.GradeMasterId = ruleSetDto.GradeMasterId;
            existingRuleSet.UpdatedAt = DateTime.UtcNow;

            _context.RuleSets.Update(existingRuleSet);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteRuleSetAsync(Guid ruleSetId)
        {
            var existingRuleSet = await _context.RuleSets
                .FirstOrDefaultAsync(rs => rs.RuleSetId == ruleSetId);

            if (existingRuleSet == null || existingRuleSet.IsDeleted) return false;

            existingRuleSet.IsDeleted = true;
            existingRuleSet.DeletedAt = DateTime.UtcNow;

            _context.RuleSets.Update(existingRuleSet);
            await _context.SaveChangesAsync();
            return true;
        }

        // === Rule Methods ===
        public async Task<IEnumerable<RuleDto>> GetRulesByRuleSetAsync(Guid ruleSetId)
        {
            var rules = await _context.Rules
                .Include(r => r.Conditions)
                .Include(r => r.Actions)
                .Where(r => r.RuleSetId == ruleSetId && !r.IsDeleted)
                .ToListAsync();

            return rules.Select(r => new RuleDto
            {
                RuleId = r.RuleId,
                Name = r.Name,
                Priority = r.Priority,
                IsEnabled = r.IsEnabled,
                StopOnSuccess = r.StopOnSuccess,
                OrdinanceSymbol = r.OrdinanceSymbol,
                RuleSetId = r.RuleSetId ?? Guid.Empty,
                Conditions = r.Conditions?.Where(c => !c.IsDeleted).Select(c => new RuleConditionDto
                {
                    ConditionId = c.ConditionId,
                    FactName = c.FactName,
                    Operator = c.Operator,
                    Value = c.Value
                }).ToList() ?? new List<RuleConditionDto>(),
                Actions = r.Actions?.Where(a => !a.IsDeleted).Select(a => new RuleActionDto
                {
                    ActionId = a.ActionId,
                    ActionType = a.ActionType,
                    CalculationMode = a.CalculationMode,
                    Param1Type = a.Param1Type,
                    Param1Value = a.Param1Value,
                    Param2Type = a.Param2Type,
                    Param2Value = a.Param2Value,
                    MaxLimit = a.MaxLimit,
                    Expression = a.Expression,
                    MaxTargetCount = a.MaxTargetCount,
                    Target = a.Target
                }).ToList() ?? new List<RuleActionDto>()
            });
        }

        public async Task<RuleDto> CreateRuleAsync(RuleCreateDto ruleDto)
        {
            var rule = new Rule
            {
                RuleId = Guid.NewGuid(),
                Name = ruleDto.Name,
                Priority = ruleDto.Priority,
                IsEnabled = ruleDto.IsEnabled,
                StopOnSuccess = ruleDto.StopOnSuccess,
                OrdinanceSymbol = ruleDto.OrdinanceSymbol,
                RuleSetId = ruleDto.RuleSetId,
                CreatedAt = DateTime.UtcNow,
                Conditions = ruleDto.Conditions?.Select(c => new RuleCondition
                {
                    ConditionId = Guid.NewGuid(),
                    FactName = c.FactName,
                    Operator = c.Operator,
                    Value = c.Value,
                    CreatedAt = DateTime.UtcNow
                }).ToList(),
                Actions = ruleDto.Actions?.Select(a => new RuleAction
                {
                    ActionId = Guid.NewGuid(),
                    ActionType = a.ActionType,
                    CalculationMode = a.CalculationMode,
                    Param1Type = a.Param1Type,
                    Param1Value = a.Param1Value,
                    Param2Type = a.Param2Type,
                    Param2Value = a.Param2Value,
                    MaxLimit = a.MaxLimit,
                    Expression = a.Expression,
                    MaxTargetCount = a.MaxTargetCount,
                    Target = a.Target,
                    CreatedAt = DateTime.UtcNow
                }).ToList()
            };

            _context.Rules.Add(rule);
            await _context.SaveChangesAsync();

            return new RuleDto
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                Priority = rule.Priority,
                IsEnabled = rule.IsEnabled,
                StopOnSuccess = rule.StopOnSuccess,
                OrdinanceSymbol = rule.OrdinanceSymbol,
                RuleSetId = rule.RuleSetId ?? Guid.Empty,
                Conditions = rule.Conditions?.Select(c => new RuleConditionDto
                {
                    ConditionId = c.ConditionId,
                    FactName = c.FactName,
                    Operator = c.Operator,
                    Value = c.Value
                }).ToList() ?? new List<RuleConditionDto>(),
                Actions = rule.Actions?.Select(a => new RuleActionDto
                {
                    ActionId = a.ActionId,
                    ActionType = a.ActionType,
                    CalculationMode = a.CalculationMode,
                    Param1Type = a.Param1Type,
                    Param1Value = a.Param1Value,
                    Param2Type = a.Param2Type,
                    Param2Value = a.Param2Value,
                    MaxLimit = a.MaxLimit,
                    Expression = a.Expression,
                    MaxTargetCount = a.MaxTargetCount,
                    Target = a.Target
                }).ToList() ?? new List<RuleActionDto>()
            };
        }

        public async Task<bool> UpdateRuleAsync(RuleUpdateDto ruleDto)
        {
            var existingRule = await _context.Rules
                .Include(r => r.Conditions.Where(c => !c.IsDeleted))
                .Include(r => r.Actions.Where(a => !a.IsDeleted))
                .FirstOrDefaultAsync(r => r.RuleId == ruleDto.RuleId);

            if (existingRule == null || existingRule.IsDeleted) return false;

            // 1. Update Parent Fields
            existingRule.Name = ruleDto.Name;
            existingRule.Priority = ruleDto.Priority;
            existingRule.IsEnabled = ruleDto.IsEnabled;
            existingRule.StopOnSuccess = ruleDto.StopOnSuccess;
            existingRule.OrdinanceSymbol = ruleDto.OrdinanceSymbol;
            existingRule.UpdatedAt = DateTime.UtcNow;

            // 2. Reconcile Conditions
            var existingConditions = existingRule.Conditions.ToList();
            var incomingConditions = ruleDto.Conditions ?? new List<RuleConditionCreateDto>();

            // Track IDs processed to identify what to delete later
            var processedConditionIds = new HashSet<Guid>();

            foreach (var cDto in incomingConditions)
            {
                if (cDto.ConditionId.HasValue)
                {
                    var existingCond = existingConditions.FirstOrDefault(c => c.ConditionId == cDto.ConditionId.Value);
                    if (existingCond != null)
                    {
                        // UPDATE Existing
                        existingCond.FactName = cDto.FactName;
                        existingCond.Operator = cDto.Operator;
                        existingCond.Value = cDto.Value;
                        existingCond.UpdatedAt = DateTime.UtcNow;

                        processedConditionIds.Add(existingCond.ConditionId);
                        _context.Entry(existingCond).State = EntityState.Modified;
                        continue;
                    }
                }

                // INSERT New
                var newCondition = new RuleCondition
                {
                    ConditionId = Guid.NewGuid(),
                    FactName = cDto.FactName,
                    Operator = cDto.Operator,
                    Value = cDto.Value,
                    CreatedAt = DateTime.UtcNow,
                    RuleId = existingRule.RuleId
                };
                _context.RuleConditions.Add(newCondition);
            }

            // DELETE Missing
            foreach (var cond in existingConditions)
            {
                if (!processedConditionIds.Contains(cond.ConditionId))
                {
                    cond.IsDeleted = true;
                    cond.DeletedAt = DateTime.UtcNow;
                    _context.Entry(cond).State = EntityState.Modified;
                }
            }

            // 3. Reconcile Actions
            var existingActions = existingRule.Actions.ToList();
            var incomingActions = ruleDto.Actions ?? new List<RuleActionCreateDto>();
            var processedActionIds = new HashSet<Guid>();

            foreach (var aDto in incomingActions)
            {
                if (aDto.ActionId.HasValue)
                {
                    var existingAct = existingActions.FirstOrDefault(a => a.ActionId == aDto.ActionId.Value);
                    if (existingAct != null)
                    {
                        // UPDATE Existing
                        existingAct.ActionType = aDto.ActionType;
                        existingAct.CalculationMode = aDto.CalculationMode;
                        existingAct.Param1Type = aDto.Param1Type;
                        existingAct.Param1Value = aDto.Param1Value;
                        existingAct.Param2Type = aDto.Param2Type;
                        existingAct.Param2Value = aDto.Param2Value;
                        existingAct.MaxLimit = aDto.MaxLimit;
                        existingAct.Expression = aDto.Expression;
                        existingAct.MaxTargetCount = aDto.MaxTargetCount;
                        existingAct.Target = aDto.Target;
                        existingAct.UpdatedAt = DateTime.UtcNow;

                        processedActionIds.Add(existingAct.ActionId);
                        _context.Entry(existingAct).State = EntityState.Modified;
                        continue;
                    }
                }

                // INSERT New
                var newAction = new RuleAction
                {
                    ActionId = Guid.NewGuid(),
                    ActionType = aDto.ActionType,
                    CalculationMode = aDto.CalculationMode,
                    Param1Type = aDto.Param1Type,
                    Param1Value = aDto.Param1Value,
                    Param2Type = aDto.Param2Type,
                    Param2Value = aDto.Param2Value,
                    MaxLimit = aDto.MaxLimit,
                    MaxTargetCount = aDto.MaxTargetCount,
                    Target = aDto.Target,
                    CreatedAt = DateTime.UtcNow,
                    RuleId = existingRule.RuleId
                };
                _context.RuleActions.Add(newAction);
            }

            // DELETE Missing Actions
            foreach (var act in existingActions)
            {
                if (!processedActionIds.Contains(act.ActionId))
                {
                    act.IsDeleted = true;
                    act.DeletedAt = DateTime.UtcNow;
                    _context.Entry(act).State = EntityState.Modified;
                }
            }

            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> DeleteRuleAsync(Guid ruleId)
        {
            var rule = await _context.Rules
                .Include(r => r.Conditions)
                .Include(r => r.Actions)
                .FirstOrDefaultAsync(r => r.RuleId == ruleId);

            if (rule == null || rule.IsDeleted) return false;

            // Soft Delete Parent
            rule.IsDeleted = true;
            rule.DeletedAt = DateTime.UtcNow;

            // Soft Delete Children - Conditions
            if (rule.Conditions != null)
            {
                foreach (var cond in rule.Conditions)
                {
                    // Only mark if not already deleted (optimization)
                    if (!cond.IsDeleted)
                    {
                        cond.IsDeleted = true;
                        cond.DeletedAt = DateTime.UtcNow;
                        _context.Entry(cond).State = EntityState.Modified;
                    }
                }
            }

            // Soft Delete Children - Actions
            if (rule.Actions != null)
            {
                foreach (var act in rule.Actions)
                {
                    if (!act.IsDeleted)
                    {
                        act.IsDeleted = true;
                        act.DeletedAt = DateTime.UtcNow;
                        _context.Entry(act).State = EntityState.Modified;
                    }
                }
            }

            _context.Rules.Update(rule);
            await _context.SaveChangesAsync();
            return true;
        }

        // === Metadata Methods ===
        public async Task<EngineMetadataDto> GetEngineMetadataAsync()
        {
            // Result-engine handlers, plus the assignment action. AllowExamAssignment has no result
            // handler by design (result processing skips unknown action types); it is interpreted
            // only by AtktRevalExamService, so it must be added here to be authorable in Ordinance.
            // The UI shows it only for KT/Reval rule sets.
            var actions = _engineRegistry.GetRegisteredActions().ToList();
            actions.Add(Services.AtktRevalExam.AtktRevalExamService.AllowExamAssignmentAction);

            // The configured head labels this college actually uses, so a rule author picks a real
            // head name from the Target multiselect instead of free-typing one that silently no-ops.
            var headTypes = await _context.SubjectCredits
                .Where(sc => !sc.IsDeleted && sc.HeadType != null && sc.HeadType != "")
                .Select(sc => sc.HeadType!)
                .Distinct()
                .OrderBy(h => h)
                .ToListAsync();

            var metadata = new EngineMetadataDto
            {
                Facts = _engineRegistry.GetRegisteredFacts().ToList(),
                Actions = actions,
                Operators = new List<string> { "==", "!=", ">", ">=", "<", "<=" },
                SubjectScopes = new List<string>
                {
                    "AllSubjects", "FailingSubjects", "PassingSubjects", "AbsentSubjects", "NotAttempted"
                },
                HeadTypes = headTypes
            };

            return metadata;
        }
    }
}
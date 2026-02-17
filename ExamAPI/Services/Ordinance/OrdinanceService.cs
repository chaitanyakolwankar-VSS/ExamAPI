using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ExamAPI.Services.Ordinance
{
    public class OrdinanceService : IOrdinanceService
    {
        private readonly ApplicationDbContext _context;

        public OrdinanceService(ApplicationDbContext context)
        {
            _context = context;
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

        // === RuleSet Methods ===
        public async Task<IEnumerable<RuleSetDto>> GetRuleSetsByPatternAsync(Guid patternId)
        {
            return await _context.RuleSets
                .Where(rs => rs.PatternId == patternId && !rs.IsDeleted)
                .Select(rs => new RuleSetDto
                {
                    RuleSetId = rs.RuleSetId,
                    Name = rs.Name,
                    IsActive = rs.IsActive,
                    PatternId = rs.PatternId
                })
                .ToListAsync();
        }

        public async Task<RuleSetDto> CreateRuleSetAsync(RuleSetCreateDto ruleSetDto)
        {
            var ruleSet = new RuleSet
            {
                RuleSetId = Guid.NewGuid(),
                Name = ruleSetDto.Name,
                IsActive = ruleSetDto.IsActive,
                PatternId = ruleSetDto.PatternId,
                CreatedAt = DateTime.UtcNow
            };

            _context.RuleSets.Add(ruleSet);
            await _context.SaveChangesAsync();

            return new RuleSetDto
            {
                RuleSetId = ruleSet.RuleSetId,
                Name = ruleSet.Name,
                IsActive = ruleSet.IsActive,
                PatternId = ruleSet.PatternId
            };
        }

        public async Task<bool> UpdateRuleSetAsync(RuleSetUpdateDto ruleSetDto)
        {
            var existingRuleSet = await _context.RuleSets
                .FirstOrDefaultAsync(rs => rs.RuleSetId == ruleSetDto.RuleSetId);
                
            if (existingRuleSet == null || existingRuleSet.IsDeleted) return false;

            existingRuleSet.Name = ruleSetDto.Name;
            existingRuleSet.IsActive = ruleSetDto.IsActive;
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
    }
}
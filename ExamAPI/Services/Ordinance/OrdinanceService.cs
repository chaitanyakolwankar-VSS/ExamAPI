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
    }
}
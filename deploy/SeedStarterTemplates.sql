-- =======================================================================================
-- SeedStarterTemplates.sql
-- Two STARTER TEMPLATES for a fresh database (DEC-26): not real colleges (College.IsTemplate = 1),
-- hidden from the college list and without users. When the platform admin adds a college, the
-- "Copy from" dropdown offers them; their grade scale and Regular + ATKT rule sets are copied into
-- the new college (rule sets are matched to the new college's patterns by name, so give it "NEP").
--   TPL-ENG  engineering (University of Mumbai NEP ordinances, 10-point scale)
--   TPL-PHM  pharmacy (PCI B.Pharm CBCS, PCI grade scale)
-- Taken from the demo database's ENG001 and PHM001 on 2026-10-05. Ids are fixed, so the script is
-- safe to re-run: a template whose code already exists is skipped.
-- Run AFTER the schema (deploy.sql) and after the API has started once.
-- =======================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
DECLARE @Now datetime2 = SYSUTCDATETIME();

-- ---------------------------------------------------------------------------------------
-- TPL-PHM: Pharmacy starter (PCI B.Pharm)
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM College WHERE CollegeCode = N'TPL-PHM')
BEGIN
BEGIN TRANSACTION;
INSERT INTO College (CollegeId, Name, CollegeCode, CollegeCenter, ContactEmail, ContactPhone, IsTemplate, CreatedAt, IsDeleted)
VALUES ('AEFBB227-4A86-44C8-B90B-A9B30C4F13A0', N'Pharmacy starter (PCI B.Pharm)', N'TPL-PHM', N'-', N'starter@templates.invalid', N'0', 1, @Now, 0);
INSERT INTO PatternMaster (PatternId, PatternName, Description, CollegeId, CreatedAt, IsDeleted)
VALUES ('52E19FE9-5002-44BA-ADCB-17B001520E8C', N'NEP', N'Starter template pattern', 'AEFBB227-4A86-44C8-B90B-A9B30C4F13A0', @Now, 0);
INSERT INTO GradeMaster (GradeMasterId, Name, Description, CollegeId, CreatedAt, IsDeleted)
VALUES ('C8009489-ADB8-4186-86FE-F97378DB2F2E', N'PCI B.Pharm Grade Scale (CBCS 2016)', N'PCI B.Pharm CBCS 2016 grade scale (Table XII).', 'AEFBB227-4A86-44C8-B90B-A9B30C4F13A0', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('0B872192-6CC7-477A-8047-5DD275757720', N'O', 10, 90, 100, N'Outstanding', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('4314081A-6D07-4C9A-9A9A-DD706381D47C', N'A', 9, 80, 89.99, N'Excellent', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('5E41D442-CB72-4AA2-BA9A-3EA8585C2B54', N'B', 8, 70, 79.99, N'Good', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('7FE2C7D8-D865-4F9E-9A82-A124435DB97C', N'C', 7, 60, 69.99, N'Fair', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('09CC043E-2177-4912-9A63-70FA392B4A34', N'D', 6, 50, 59.99, N'Average', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('24C70E9D-8E7D-4567-BEE4-28FF2DF2096C', N'F', 0, 0, 49.99, N'Fail', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', @Now, 0);

-- Rule set: B.Pharm Regular (PCI CBCS) (Regular)
INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId, CollegeId, CreatedAt, IsDeleted)
VALUES ('5303C395-03EA-44D6-9D56-09A31902E603', N'B.Pharm Regular (PCI CBCS)', N'Regular', 1, '52E19FE9-5002-44BA-ADCB-17B001520E8C', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', 'AEFBB227-4A86-44C8-B90B-A9B30C4F13A0', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('0DF0BCD9-9289-4341-AD0D-6B0D3ED94F61', N'Pharmacy Grace (@) - 2 marks per failing subject', 1, 1, 0, N'@', '5303C395-03EA-44D6-9D56-09A31902E603', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('BE6FB244-4738-488F-BEA5-5C2B53296D15', N'FailedSubjectCount', N'>=', N'1', '0DF0BCD9-9289-4341-AD0D-6B0D3ED94F61', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('C9150FA8-FCE5-4765-AB83-D424C1051963', N'AddGrace', N'Fixed', N'Absolute', 2, N'None', NULL, 9999, 0, N'FailingSubjects', NULL, '0DF0BCD9-9289-4341-AD0D-6B0D3ED94F61', @Now, 0);

-- Rule set: B.Pharm ATKT (PCI CBCS) (A.T.K.T)
INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId, CollegeId, CreatedAt, IsDeleted)
VALUES ('51AB7BF8-7C6D-4FE3-B6E1-6D55C6BE345B', N'B.Pharm ATKT (PCI CBCS)', N'A.T.K.T', 1, '52E19FE9-5002-44BA-ADCB-17B001520E8C', 'C8009489-ADB8-4186-86FE-F97378DB2F2E', 'AEFBB227-4A86-44C8-B90B-A9B30C4F13A0', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('3C84DA39-76A3-465B-A3BA-5EB699842523', N'Pharmacy Grace (@) - 2 marks per failing subject', 1, 1, 0, N'@', '51AB7BF8-7C6D-4FE3-B6E1-6D55C6BE345B', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('692485D0-4849-456D-84B5-BDD4CF41F833', N'FailedSubjectCount', N'>=', N'1', '3C84DA39-76A3-465B-A3BA-5EB699842523', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('2DCDB9DB-C1D0-45FD-B105-21FB22FBDE9B', N'AddGrace', N'Fixed', N'Absolute', 2, N'None', NULL, 9999, 0, N'FailingSubjects', NULL, '3C84DA39-76A3-465B-A3BA-5EB699842523', @Now, 0);
COMMIT;
PRINT 'Added TPL-PHM.';
END
ELSE PRINT 'TPL-PHM already exists; skipped.';

-- ---------------------------------------------------------------------------------------
-- TPL-ENG: Engineering starter (NEP)
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM College WHERE CollegeCode = N'TPL-ENG')
BEGIN
BEGIN TRANSACTION;
INSERT INTO College (CollegeId, Name, CollegeCode, CollegeCenter, ContactEmail, ContactPhone, IsTemplate, CreatedAt, IsDeleted)
VALUES ('DEA3C8D2-53CA-49E0-9D55-014D211B35E6', N'Engineering starter (NEP)', N'TPL-ENG', N'-', N'starter@templates.invalid', N'0', 1, @Now, 0);
INSERT INTO PatternMaster (PatternId, PatternName, Description, CollegeId, CreatedAt, IsDeleted)
VALUES ('25F96CB8-9F87-4075-B225-2916B1A7DF0F', N'NEP', N'Starter template pattern', 'DEA3C8D2-53CA-49E0-9D55-014D211B35E6', @Now, 0);
INSERT INTO GradeMaster (GradeMasterId, Name, Description, CollegeId, CreatedAt, IsDeleted)
VALUES ('1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', N'10-Point Scale (CBGS)', N'', 'DEA3C8D2-53CA-49E0-9D55-014D211B35E6', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('26D8D788-7203-4AC1-A1D4-A3A127A29BFB', N'O', 10, 80, 100, N'Outstanding', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('3D3134EC-20B1-4086-8470-4770359C447C', N'A', 9, 75, 79.99, N'Excellent', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('E5055818-2CFE-4FA8-89A2-8411120BA2AC', N'B', 8, 70, 74.99, N'Very Good', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('D957B640-0B50-4E0C-9B60-B38BEDE3A04B', N'C', 7, 60, 69.99, N'Good', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('BA017E11-F8A1-4B79-B816-E4DEFE3D01D1', N'D', 6, 50, 59.99, N'Fair', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('6A63ADCF-989D-41FD-9F8C-B6ED8752568F', N'E', 5, 45, 49.99, N'Average', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('6429197F-5630-473F-8591-CD9E952B51D0', N'P', 4, 40, 44.99, N'Pass', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);
INSERT INTO GradeThreshold (ThresholdId, Grade, GradePoint, MinPercentage, MaxPercentage, PerformanceRemark, GradeMasterId, CreatedAt, IsDeleted) VALUES ('DC556B4C-554B-4D39-AE5D-6B1AAFBA57C1', N'F', 0, 0, 39.99, N'Fail', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', @Now, 0);

-- Rule set: KT / ATKT Exam (KT)
INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId, CollegeId, CreatedAt, IsDeleted)
VALUES ('B6040629-011F-4688-95A9-84BC026D41DF', N'KT / ATKT Exam', N'KT', 1, '25F96CB8-9F87-4075-B225-2916B1A7DF0F', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', 'DEA3C8D2-53CA-49E0-9D55-014D211B35E6', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('54C8BA7F-A6DD-44C4-AA1F-EBDA550A9BA7', N'O.5045-A Condonation', 1, 1, 0, N'*', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('C68F52EA-1A2A-4AB1-9D11-C25469DDEEEE', N'FailedHeadCount', N'==', N'1', '54C8BA7F-A6DD-44C4-AA1F-EBDA550A9BA7', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('053E8D8D-5CC4-487F-A9A1-CE884C6F96CC', N'AddGrace', N'MinOf', N'PercentOfAggregate', 1, N'PercentOfSubject', 10, 10, 1, N'FailingHeads', NULL, '54C8BA7F-A6DD-44C4-AA1F-EBDA550A9BA7', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('70A4CE4E-50B0-4F73-82DB-6E531AFA05E2', N'O.5042-A Grace for Head Passing', 2, 1, 0, N'@', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('25D65877-8784-4866-8934-EAC777C8B2F7', N'FailedHeadCount', N'>=', N'1', '70A4CE4E-50B0-4F73-82DB-6E531AFA05E2', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('CF7B46C0-0509-4BDE-8E38-D5F2680D138E', N'AddGrace', N'MinOf', N'GraceChart', 0, N'None', NULL, 7.75, 0, N'FailingHeads', N'if(SubjectOutOf<=50,2,if(SubjectOutOf<=100,3,if(SubjectOutOf<=150,4,if(SubjectOutOf<=200,5,if(SubjectOutOf<=250,6,if(SubjectOutOf<=300,7,if(SubjectOutOf<=350,8,if(SubjectOutOf<=400,9,10))))))))', '70A4CE4E-50B0-4F73-82DB-6E531AFA05E2', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('7D310443-EED8-49AA-8EE2-9DACEE1C697B', N'O.229 NSS/NCC Grace (Failed Heads)', 3, 1, 0, N'#', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('808EBE4C-0EFD-4640-8323-FCE8D24C6F5C', N'FailedHeadCount', N'>=', N'1', '7D310443-EED8-49AA-8EE2-9DACEE1C697B', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('CBA96955-3DA4-4471-A32D-F3E172B1F4A9', N'HasQuota', N'==', N'1', '7D310443-EED8-49AA-8EE2-9DACEE1C697B', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('E1878E09-33C4-408A-870D-4996C8C94C59', N'AddGrace', N'MinOf', N'PercentOfSubject', 5, N'None', NULL, 10, 0, N'FailingHeads', NULL, '7D310443-EED8-49AA-8EE2-9DACEE1C697B', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('07C69509-D0FE-4A36-9F10-8B8704E85970', N'O.229 NSS/NCC Class Benefit', 4, 1, 0, N'#', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('287ABDA0-2A2B-400A-974E-69CA5AC55E95', N'FailedHeadCount', N'==', N'0', '07C69509-D0FE-4A36-9F10-8B8704E85970', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('48D48D33-E1AA-4B11-88F1-60D48228086B', N'HasQuota', N'==', N'1', '07C69509-D0FE-4A36-9F10-8B8704E85970', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('5F884D2A-C96D-40B8-963A-78107D0D9F05', N'AddBonusSGPI', NULL, NULL, 0.1, NULL, NULL, 10, NULL, NULL, NULL, '07C69509-D0FE-4A36-9F10-8B8704E85970', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('C7FDD2EC-38E6-4BE5-9504-F60B1B498585', N'O.5044-A Distinction Grace', 5, 0, 0, N'@', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('69EF506F-03C0-45CB-8319-5B29D93F02E5', N'FailedHeadCount', N'==', N'0', 'C7FDD2EC-38E6-4BE5-9504-F60B1B498585', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('394D1D74-0022-498E-BF8F-7940D0A7C448', N'UpgradeGrade', NULL, NULL, 0, NULL, 6, NULL, 2, N'All', NULL, 'C7FDD2EC-38E6-4BE5-9504-F60B1B498585', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('C8EAF41D-D4DF-480E-BDBE-4A92F84A0785', N'O.5043-A Higher Grade', 6, 0, 0, N'@', 'B6040629-011F-4688-95A9-84BC026D41DF', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('8072B400-1738-4AF7-9172-E840CEBAAB43', N'FailedHeadCount', N'==', N'0', 'C8EAF41D-D4DF-480E-BDBE-4A92F84A0785', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('504338C8-29A6-439F-8F6C-F89D28C84E92', N'UpgradeGrade', NULL, NULL, 1, NULL, 10, NULL, 1, N'All', NULL, 'C8EAF41D-D4DF-480E-BDBE-4A92F84A0785', @Now, 0);

-- Rule set: Regular Exam (Regular)
INSERT INTO RuleSet (RuleSetId, Name, ExamType, IsActive, PatternId, GradeMasterId, CollegeId, CreatedAt, IsDeleted)
VALUES ('19949132-FB0D-41FC-B7C3-9B8E7C8F2940', N'Regular Exam', N'Regular', 1, '25F96CB8-9F87-4075-B225-2916B1A7DF0F', '1DE45E22-2E9D-48C5-B1AC-BF5A6FF49059', 'DEA3C8D2-53CA-49E0-9D55-014D211B35E6', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('6550DA17-F71F-4560-BEE4-498160DE1678', N'O.5045-A Condonation', 1, 1, 0, N'*', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('5B094B02-9DDB-4407-A371-4F23B3998465', N'FailedHeadCount', N'==', N'1', '6550DA17-F71F-4560-BEE4-498160DE1678', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('F4122219-6DFF-41D8-99B5-CEF7E88CCC67', N'AddGrace', N'MinOf', N'PercentOfAggregate', 1, N'PercentOfSubject', 10, 10, 1, N'FailingHeads', NULL, '6550DA17-F71F-4560-BEE4-498160DE1678', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('53D81150-AB1E-418B-9757-9EF5D5D61A87', N'O.5042-A Grace for Head Passing', 2, 1, 0, N'@', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('558EB942-1684-4932-92EE-AB2DC21A1D99', N'FailedHeadCount', N'>=', N'1', '53D81150-AB1E-418B-9757-9EF5D5D61A87', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('196232E3-5745-4186-8BC5-383128EF892A', N'AddGrace', N'MinOf', N'GraceChart', 0, N'None', NULL, 7.75, 0, N'FailingHeads', N'if(SubjectOutOf<=50,2,if(SubjectOutOf<=100,3,if(SubjectOutOf<=150,4,if(SubjectOutOf<=200,5,if(SubjectOutOf<=250,6,if(SubjectOutOf<=300,7,if(SubjectOutOf<=350,8,if(SubjectOutOf<=400,9,10))))))))', '53D81150-AB1E-418B-9757-9EF5D5D61A87', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('B5E2D52C-A36F-4241-B85B-D205795E5ADD', N'O.229 NSS/NCC Grace (Failed Heads)', 3, 1, 0, N'#', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('9F1594F3-8A06-4C79-8C23-A80FFBA0354E', N'FailedHeadCount', N'>=', N'1', 'B5E2D52C-A36F-4241-B85B-D205795E5ADD', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('35835B5A-7E6A-4892-9F0E-AF9F8E15A473', N'HasQuota', N'==', N'1', 'B5E2D52C-A36F-4241-B85B-D205795E5ADD', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('6F0F9E33-5EA5-4908-A541-38F2F5A39A3D', N'AddGrace', N'MinOf', N'PercentOfSubject', 5, N'None', NULL, 10, 0, N'FailingHeads', NULL, 'B5E2D52C-A36F-4241-B85B-D205795E5ADD', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('6E26A899-182F-4755-A369-9204F3C72EF7', N'O.229 NSS/NCC Class Benefit', 4, 1, 0, N'#', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('55B4DCD8-54EA-4F15-A047-E46FC799EFE8', N'FailedHeadCount', N'==', N'0', '6E26A899-182F-4755-A369-9204F3C72EF7', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('2A7274C6-1654-49B7-9D6A-D88DAAC2D1E0', N'HasQuota', N'==', N'1', '6E26A899-182F-4755-A369-9204F3C72EF7', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('2057C259-8242-4046-8D1F-86BEDBCEADBB', N'AddBonusSGPI', NULL, NULL, 0.1, NULL, NULL, 10, NULL, NULL, NULL, '6E26A899-182F-4755-A369-9204F3C72EF7', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('748C685D-95BA-46E8-B4EB-DEDA1F41BF69', N'O.5044-A Distinction Grace', 5, 0, 0, N'@', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('EC9AD4F0-0791-403F-8E60-719E47785DC8', N'FailedHeadCount', N'==', N'0', '748C685D-95BA-46E8-B4EB-DEDA1F41BF69', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('587FF153-1C58-40D4-8126-E74E06206AC3', N'UpgradeGrade', NULL, NULL, 0, NULL, 6, NULL, 2, N'All', NULL, '748C685D-95BA-46E8-B4EB-DEDA1F41BF69', @Now, 0);
INSERT INTO [Rule] (RuleId, Name, Priority, IsEnabled, StopOnSuccess, OrdinanceSymbol, RuleSetId, CreatedAt, IsDeleted) VALUES ('0BF4F14A-DC43-4A27-9DDF-2CB54DEADC46', N'O.5043-A Higher Grade', 6, 0, 0, N'@', '19949132-FB0D-41FC-B7C3-9B8E7C8F2940', @Now, 0);
  INSERT INTO RuleCondition (ConditionId, FactName, Operator, Value, RuleId, CreatedAt, IsDeleted) VALUES ('39F102A1-B81D-4BD1-AA97-7FDB0192FA57', N'FailedHeadCount', N'==', N'0', '0BF4F14A-DC43-4A27-9DDF-2CB54DEADC46', @Now, 0);
  INSERT INTO RuleAction (ActionId, ActionType, CalculationMode, Param1Type, Param1Value, Param2Type, Param2Value, MaxLimit, MaxTargetCount, Target, Expression, RuleId, CreatedAt, IsDeleted) VALUES ('B4E2035B-4072-45BB-9615-B79E16A9F177', N'UpgradeGrade', NULL, NULL, 1, NULL, 10, NULL, 1, N'All', NULL, '0BF4F14A-DC43-4A27-9DDF-2CB54DEADC46', @Now, 0);
COMMIT;
PRINT 'Added TPL-ENG.';
END
ELSE PRINT 'TPL-ENG already exists; skipped.';

SELECT*
FROM StudentMaster 
WHERE FirstName LIKE 'NEP_Student%';

SELECT *
FROM SubjectMaster 
WHERE Pattern = 'NEP';

-- 3. Verify the pending Marks Entry records for the students
SELECT * 
FROM MarksMaster mm 
JOIN StudentMarks sm ON mm.MarksId = sm.MarksId 
WHERE mm.StudentID LIKE 'NEP2024%';

select * from MarksMaster
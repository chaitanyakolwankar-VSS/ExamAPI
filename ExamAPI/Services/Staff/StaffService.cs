using ClosedXML.Excel;
using ExamAPI.Data;
using ExamAPI.DTOs;
using ExamAPI.Models;
using ExamAPI.Services.StudentAccess;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    public interface IStaffService
    {
        Task<List<StaffDto>> ListAsync(StaffFilter filter);
        Task<StaffDto> GetAsync(Guid staffId);
        Task<StaffDto> SaveAsync(Guid? staffId, StaffSave dto);
        Task DeleteAsync(Guid staffId);

        Task<List<StaffOptionDto>> OptionsAsync();
        Task<StaffOptionDto> AddOptionAsync(StaffOptionSave dto);
        Task<StaffOptionDto> RenameOptionAsync(Guid id, StaffOptionSave dto);
        Task DeleteOptionAsync(Guid id);

        Task<(byte[] FileBytes, string FileName)> TemplateAsync();
        Task<StaffImportResult> ImportAsync(StaffImportRequest request);
    }

    /// <summary>
    /// Staff Master: the people who get exam letters, and the college's department / designation lists.
    /// Staff come in by Excel (the template lists the current staff, so the same sheet adds and updates) or one at a
    /// time on the screen; both go through the same checks.
    /// </summary>
    public class StaffService : IStaffService
    {
        private readonly ApplicationDbContext _context;

        public StaffService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------ staff

        public async Task<List<StaffDto>> ListAsync(StaffFilter filter)
        {
            var query = _context.StaffMasters.AsNoTracking().Include(s => s.Department).Include(s => s.Designation).AsQueryable();
            if (!filter.IncludeInactive) query = query.Where(s => s.IsActive);
            if (filter.DepartmentId is Guid department) query = query.Where(s => s.DepartmentId == department);
            if (filter.DesignationId is Guid designation) query = query.Where(s => s.DesignationId == designation);
            if (string.Equals(filter.Type, "internal", StringComparison.OrdinalIgnoreCase)) query = query.Where(s => !s.IsExternal);
            if (string.Equals(filter.Type, "external", StringComparison.OrdinalIgnoreCase)) query = query.Where(s => s.IsExternal);
            var teaching = (filter.Teaching ?? "").Trim();
            if (string.Equals(teaching, "teaching", StringComparison.OrdinalIgnoreCase)) query = query.Where(s => s.IsTeaching);
            if (string.Equals(teaching, "non-teaching", StringComparison.OrdinalIgnoreCase)) query = query.Where(s => !s.IsTeaching);
            var staff = await query.ToListAsync();

            // A college has hundreds of staff at most: search in memory, ignoring case, across name, email and ID.
            var search = (filter.Search ?? "").Trim();
            bool Has(string? value) => value != null && value.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (search != "")
                staff = staff.Where(s => Has(Letter(s)) || Has(s.Email) || Has(s.EmployeeCode)).ToList();

            return staff
                .OrderBy(s => s.IsExternal).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.LastName, StringComparer.OrdinalIgnoreCase)
                .Select(ToDto).ToList();
        }

        public async Task<StaffDto> GetAsync(Guid staffId)
        {
            var staff = await _context.StaffMasters.AsNoTracking().Include(s => s.Department).Include(s => s.Designation)
                .FirstOrDefaultAsync(s => s.StaffId == staffId) ?? throw new KeyNotFoundException("Staff member not found.");
            return ToDto(staff);
        }

        public async Task<StaffDto> SaveAsync(Guid? staffId, StaffSave dto)
        {
            StaffMaster? staff = null;
            if (staffId is Guid id)
                staff = await _context.StaffMasters.FirstOrDefaultAsync(s => s.StaffId == id) ?? throw new KeyNotFoundException("Staff member not found.");

            var input = Clean(dto.EmployeeCode, dto.Title, dto.FirstName, dto.MiddleName, dto.LastName, dto.Email, dto.IsExternal, dto.ExternalCollege, dto.ExternalCity);
            var errors = Problems(input);
            var options = await _context.StaffOptions.ToListAsync();
            if (dto.DepartmentId is Guid dep && !options.Any(o => o.Id == dep && o.Kind == StaffOptionKinds.Department))
                errors.Add("the department is not in the list.");
            if (dto.DesignationId is Guid des && !options.Any(o => o.Id == des && o.Kind == StaffOptionKinds.Designation))
                errors.Add("the designation is not in the list.");
            if (input.Code != null && await _context.StaffMasters.AnyAsync(s => s.EmployeeCode == input.Code && s.StaffId != staffId))
                errors.Add($"employee ID {input.Code} already belongs to someone else.");
            if (await _context.StaffMasters.AnyAsync(s => s.Email == input.Email && s.StaffId != staffId))
                errors.Add($"email {input.Email} already belongs to someone else.");
            if (errors.Count > 0)
                throw new InvalidOperationException(Sentence(errors));

            if (staff == null)
            {
                staff = new StaffMaster { StaffId = Guid.NewGuid(), Title = input.Title!, FirstName = input.First, Email = input.Email };
                _context.StaffMasters.Add(staff);
            }
            Apply(staff, input);
            staff.IsTeaching = dto.IsTeaching;
            staff.DepartmentId = dto.DepartmentId;
            staff.DesignationId = dto.DesignationId;
            staff.IsActive = dto.IsActive;
            await _context.SaveChangesAsync();
            return await GetAsync(staff.StaffId);
        }

        public async Task DeleteAsync(Guid staffId)
        {
            var staff = await _context.StaffMasters.FirstOrDefaultAsync(s => s.StaffId == staffId) ?? throw new KeyNotFoundException("Staff member not found.");
            staff.IsDeleted = true;
            staff.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ------------------------------------------------------------------ department / designation lists

        public async Task<List<StaffOptionDto>> OptionsAsync()
        {
            var used = await _context.StaffMasters.Select(s => new { s.DepartmentId, s.DesignationId }).ToListAsync();
            var counts = used.Select(s => s.DepartmentId).Concat(used.Select(s => s.DesignationId))
                .Where(id => id != null)
                .GroupBy(id => id!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            var options = await _context.StaffOptions.AsNoTracking().ToListAsync();
            return options
                .OrderBy(o => o.Kind).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
                .Select(o => new StaffOptionDto { Id = o.Id, Kind = o.Kind, Name = o.Name, StaffCount = counts.GetValueOrDefault(o.Id) })
                .ToList();
        }

        public async Task<StaffOptionDto> AddOptionAsync(StaffOptionSave dto)
        {
            var kind = StaffOptionKinds.Canonical(dto.Kind) ?? throw new InvalidOperationException("Choose Department or Designation.");
            var name = await CheckOptionNameAsync(kind, dto.Name, exceptId: null);
            var option = new StaffOption { Id = Guid.NewGuid(), Kind = kind, Name = name };
            _context.StaffOptions.Add(option);
            await _context.SaveChangesAsync();
            return new StaffOptionDto { Id = option.Id, Kind = kind, Name = name };
        }

        public async Task<StaffOptionDto> RenameOptionAsync(Guid id, StaffOptionSave dto)
        {
            var option = await _context.StaffOptions.FirstOrDefaultAsync(o => o.Id == id) ?? throw new KeyNotFoundException("Not found in the list.");
            option.Name = await CheckOptionNameAsync(option.Kind, dto.Name, exceptId: id);
            await _context.SaveChangesAsync();
            return (await OptionsAsync()).First(o => o.Id == id);
        }

        public async Task DeleteOptionAsync(Guid id)
        {
            var option = await _context.StaffOptions.FirstOrDefaultAsync(o => o.Id == id) ?? throw new KeyNotFoundException("Not found in the list.");
            var used = await _context.StaffMasters.CountAsync(s => s.DepartmentId == id || s.DesignationId == id);
            if (used > 0)
                throw new InvalidOperationException($"{option.Name} is used by {used} staff member(s). Change them first.");
            option.IsDeleted = true;
            option.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task<string> CheckOptionNameAsync(string kind, string? name, Guid? exceptId)
        {
            var clean = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (clean == "") throw new InvalidOperationException("Enter a name.");
            if (clean.Length > 100) throw new InvalidOperationException("The name is longer than 100 characters.");
            var taken = await _context.StaffOptions.AnyAsync(o => o.Kind == kind && o.Name == clean && o.Id != exceptId);
            if (taken) throw new InvalidOperationException($"{clean} is already in the {kind.ToLowerInvariant()} list.");
            return clean;
        }

        // ------------------------------------------------------------------ Excel

        /// <summary>Columns of the staff sheet, in order. Row 1 is a title, row 2 these headings, data from row 3.</summary>
        public static readonly string[] Headers =
            { "EmployeeId", "Title", "FirstName", "MiddleName", "LastName", "Email", "Department", "Designation", "Teaching", "External", "ExternalCollege", "ExternalCity" };
        /// <summary>Sheets downloaded before the Teaching column existed are still accepted (the column is then left as it is).</summary>
        private const string OptionalHeader = "Teaching";
        private const int TemplateRows = 1000;
        private static int Column(string header) => Array.IndexOf(Headers, header) + 1;

        /// <summary>
        /// The staff sheet, filled with the current (active) staff so it can be edited and imported back. Title,
        /// Department, Designation and External are drop-downs; a new department or designation can still be typed
        /// (Excel warns), and the import adds it to the list.
        /// </summary>
        public async Task<(byte[] FileBytes, string FileName)> TemplateAsync()
        {
            var staff = await _context.StaffMasters.AsNoTracking().Include(s => s.Department).Include(s => s.Designation)
                .Where(s => s.IsActive).ToListAsync();
            var options = await _context.StaffOptions.AsNoTracking().ToListAsync();
            var college = await _context.Colleges.AsNoTracking().Select(c => new { c.Name, c.CollegeCode }).FirstOrDefaultAsync();
            var collegeName = ExamAPI.Services.Report.CollegeBranding.DisplayName(college?.Name, college?.CollegeCode);

            using var workbook = ExamAPI.Services.Report.ExcelStyles.NewWorkbook();
            var sheet = workbook.Worksheets.Add("Staff");
            sheet.Cell(1, 1).Value = $"Staff  |  {collegeName}";
            sheet.Range(1, 1, 1, Headers.Length).Merge();
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 13;
            sheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            for (int i = 0; i < Headers.Length; i++)
            {
                var cell = sheet.Cell(2, i + 1);
                cell.Value = Headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF9");
            }
            sheet.SheetView.FreezeRows(2);
            sheet.Range(3, 1, TemplateRows + 2, Headers.Length).Style.NumberFormat.Format = "@";

            var row = 3;
            foreach (var s in staff.OrderBy(s => s.IsExternal).ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.LastName, StringComparer.OrdinalIgnoreCase))
            {
                sheet.Cell(row, Column("EmployeeId")).Value = s.EmployeeCode ?? "";
                sheet.Cell(row, Column("Title")).Value = s.Title;
                sheet.Cell(row, Column("FirstName")).Value = s.FirstName;
                sheet.Cell(row, Column("MiddleName")).Value = s.MiddleName ?? "";
                sheet.Cell(row, Column("LastName")).Value = s.LastName;
                sheet.Cell(row, Column("Email")).Value = s.Email;
                sheet.Cell(row, Column("Department")).Value = s.Department?.Name ?? "";
                sheet.Cell(row, Column("Designation")).Value = s.Designation?.Name ?? "";
                sheet.Cell(row, Column("Teaching")).Value = s.IsTeaching ? "Yes" : "No";
                sheet.Cell(row, Column("External")).Value = s.IsExternal ? "Yes" : "No";
                sheet.Cell(row, Column("ExternalCollege")).Value = s.ExternalCollege ?? "";
                sheet.Cell(row, Column("ExternalCity")).Value = s.ExternalCity ?? "";
                row++;
            }

            // Drop-down sources on a hidden sheet (an inline list is limited to 255 characters).
            var lists = workbook.Worksheets.Add("Lists");
            IXLRange? ListRange(int column, string heading, IEnumerable<string> values)
            {
                lists.Cell(1, column).Value = heading;
                var items = values.ToList();
                for (int i = 0; i < items.Count; i++) lists.Cell(i + 2, column).Value = items[i];
                return items.Count == 0 ? null : lists.Range(2, column, items.Count + 1, column);
            }
            var titles = ListRange(1, "Title", StaffTitles.All);
            var departments = ListRange(2, "Department", options.Where(o => o.Kind == StaffOptionKinds.Department).Select(o => o.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            var designations = ListRange(3, "Designation", options.Where(o => o.Kind == StaffOptionKinds.Designation).Select(o => o.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            lists.Visibility = XLWorksheetVisibility.Hidden;

            IXLDataValidation Dropdown(string header) => sheet.Range(3, Column(header), TemplateRows + 2, Column(header)).CreateDataValidation();

            var title = Dropdown("Title");
            title.List(titles!, true);
            title.ErrorTitle = "Title";
            title.ErrorMessage = "Choose a title from the list.";

            foreach (var (header, range) in new[] { ("Department", departments), ("Designation", designations) })
            {
                var validation = Dropdown(header);
                if (range != null)
                {
                    validation.List(range, true);
                    validation.ErrorStyle = XLErrorStyle.Warning; // a new name may be typed; the import adds it to the list
                    validation.ErrorTitle = header;
                    validation.ErrorMessage = $"Not in the {header.ToLowerInvariant()} list. Keep it to add it to the list, or choose one from the list.";
                }
                else
                {
                    validation.AllowedValues = XLAllowedValues.AnyValue;
                }
                validation.ShowInputMessage = true;
                validation.InputTitle = header;
                validation.InputMessage = "Optional. Used to find staff when assigning duties.";
            }

            var teaching = Dropdown("Teaching");
            teaching.List("\"Yes,No\"", true);
            teaching.ShowInputMessage = true;
            teaching.InputTitle = "Teaching";
            teaching.InputMessage = "Yes for teaching staff, No for lab, office and other non-teaching staff. Empty means Yes. Decides the default duty role.";

            var external = Dropdown("External");
            external.List("\"No,Yes\"", true);
            external.ShowInputMessage = true;
            external.InputTitle = "External";
            external.InputMessage = "Yes for a professor of another college (fill ExternalCollege). Empty means No.";

            var employee = Dropdown("EmployeeId");
            employee.AllowedValues = XLAllowedValues.AnyValue;
            employee.ShowInputMessage = true;
            employee.InputTitle = "Employee ID";
            employee.InputMessage = "Required for our own staff; optional for external staff. A row with an existing ID updates that person.";

            var email = sheet.Range(3, Column("Email"), TemplateRows + 2, Column("Email"));
            email.AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);
            var emailHint = email.CreateDataValidation();
            emailHint.AllowedValues = XLAllowedValues.AnyValue;
            emailHint.ShowInputMessage = true;
            emailHint.InputTitle = "Email";
            emailHint.InputMessage = "Required. The person's own address: their letters are sent here.";
            sheet.Range(3, Column("EmployeeId"), TemplateRows + 2, Column("EmployeeId")).AddConditionalFormat().WhenIsDuplicate().Fill.SetBackgroundColor(XLColor.LightPink);

            double[] widths = { 14, 8, 16, 16, 16, 32, 24, 24, 10, 10, 32, 16 };
            for (int i = 0; i < widths.Length; i++) sheet.Column(i + 1).Width = widths[i];

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = string.Concat($"Staff {collegeName}".Split(Path.GetInvalidFileNameChars()));
            return (stream.ToArray(), fileName);
        }

        public async Task<StaffImportResult> ImportAsync(StaffImportRequest request)
        {
            if (request.File == null || request.File.Length == 0)
                throw new InvalidOperationException("Choose the filled-in staff sheet (.xlsx).");

            using var stream = new MemoryStream();
            await request.File.CopyToAsync(stream);
            XLWorkbook workbook;
            try
            {
                stream.Position = 0;
                workbook = new XLWorkbook(stream);
            }
            catch
            {
                throw new InvalidOperationException("The file could not be read. Use the downloaded .xlsx staff sheet.");
            }

            using (workbook)
            {
                var sheet = workbook.Worksheet(1);
                var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int lastColumn = sheet.Row(2).LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= lastColumn; c++) columns.TryAdd(sheet.Cell(2, c).GetString().Trim(), c);
                if (Headers.Any(h => h != OptionalHeader && !columns.ContainsKey(h)))
                    throw new InvalidOperationException("This is not the staff sheet. Download it again with the Excel button.");

                var rows = new List<(int Row, Input Input, string Department, string Designation)>();
                int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
                for (int r = 3; r <= lastRow; r++)
                {
                    string Cell(string header) => columns.TryGetValue(header, out var c) ? sheet.Cell(r, c).GetFormattedString().Trim() : "";
                    if (Headers.All(h => Cell(h) == "")) continue;
                    var teachingText = Cell("Teaching");
                    var isTeaching = !teachingText.Equals("No", StringComparison.OrdinalIgnoreCase) && !teachingText.Equals("N", StringComparison.OrdinalIgnoreCase);
                    var externalText = Cell("External");
                    var isExternal = externalText.Equals("Yes", StringComparison.OrdinalIgnoreCase) || externalText.Equals("Y", StringComparison.OrdinalIgnoreCase);
                    var input = Clean(Cell("EmployeeId"), Cell("Title"), Cell("FirstName"), Cell("MiddleName"), Cell("LastName"), Cell("Email"),
                        isExternal, Cell("ExternalCollege"), Cell("ExternalCity"));
                    if (externalText != "" && !isExternal && !externalText.Equals("No", StringComparison.OrdinalIgnoreCase) && !externalText.Equals("N", StringComparison.OrdinalIgnoreCase))
                        input.Errors.Add("External must be Yes or No.");
                    if (teachingText != "" && isTeaching && !teachingText.Equals("Yes", StringComparison.OrdinalIgnoreCase) && !teachingText.Equals("Y", StringComparison.OrdinalIgnoreCase))
                        input.Errors.Add("Teaching must be Yes or No.");
                    input.Teaching = columns.ContainsKey("Teaching") ? isTeaching : null;
                    rows.Add((r, input, OptionName(Cell("Department")), OptionName(Cell("Designation"))));
                }
                if (rows.Count == 0)
                    throw new InvalidOperationException("The sheet has no staff. Fill them in from row 3.");

                var existing = await _context.StaffMasters.Include(s => s.Department).Include(s => s.Designation).ToListAsync();
                var byCode = existing.Where(s => s.EmployeeCode != null).ToDictionary(s => s.EmployeeCode!, StringComparer.OrdinalIgnoreCase);
                var byEmail = existing.ToDictionary(s => s.Email, StringComparer.OrdinalIgnoreCase);
                var options = await _context.StaffOptions.ToListAsync();
                var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var emailsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var newOptions = new List<(string Kind, string Name)>();
                var errors = new List<string>();
                var results = new List<StaffImportRow>();
                var plan = new List<(Input Input, StaffMaster? Match, string Department, string Designation)>();

                foreach (var (row, input, department, designation) in rows)
                {
                    var rowErrors = Problems(input);
                    if (input.Code != null && !codesInFile.Add(input.Code))
                        rowErrors.Add($"employee ID {input.Code} appears twice in this sheet.");
                    if (input.Email != "" && !emailsInFile.Add(input.Email))
                        rowErrors.Add($"email {input.Email} appears twice in this sheet.");

                    // The person this row is: by employee ID, else by email.
                    StaffMaster? match = null;
                    if (input.Code != null) byCode.TryGetValue(input.Code, out match);
                    if (match == null && input.Email != "") byEmail.TryGetValue(input.Email, out match);
                    if (input.Email != "" && byEmail.TryGetValue(input.Email, out var emailOwner) && match != null
                        && (emailOwner.StaffId != match.StaffId || (input.Code != null && emailOwner.EmployeeCode != null && emailOwner.EmployeeCode != input.Code)))
                        rowErrors.Add($"email {input.Email} already belongs to {Letter(emailOwner)}{(emailOwner.EmployeeCode != null ? $" ({emailOwner.EmployeeCode})" : "")}.");
                    if (match != null && input.Code == null && match.EmployeeCode != null && !input.IsExternal)
                        rowErrors.Add($"employee ID is required (this email belongs to {match.EmployeeCode}).");

                    var notes = new List<string>();
                    foreach (var (kind, name) in new[] { (StaffOptionKinds.Department, department), (StaffOptionKinds.Designation, designation) })
                    {
                        if (name == "") continue;
                        if (name.Length > 100) { rowErrors.Add($"the {kind.ToLowerInvariant()} is longer than 100 characters."); continue; }
                        var known = options.Any(o => o.Kind == kind && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))
                                    || newOptions.Any(o => o.Kind == kind && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (!known)
                        {
                            newOptions.Add((kind, name));
                            notes.Add($"new {kind.ToLowerInvariant()} \"{name}\" will be added to the list");
                        }
                    }

                    errors.AddRange(rowErrors.Select(e => $"Row {row}: {e}"));
                    var action = rowErrors.Count > 0 ? "" : match == null ? StaffImportActions.New
                        : Differs(match, input, department, designation) ? StaffImportActions.Update : StaffImportActions.Unchanged;
                    if (match != null && !match.IsActive && rowErrors.Count == 0) notes.Add("was marked as left; becomes active again");
                    results.Add(new StaffImportRow
                    {
                        Row = row,
                        Name = Letter(input.Title ?? "", input.First, input.Middle, input.Last),
                        Email = input.Email,
                        Action = action,
                        HasErrors = rowErrors.Count > 0,
                        Note = rowErrors.Count > 0 ? Sentence(rowErrors) : notes.Count > 0 ? Capitalise(string.Join("; ", notes)) + "." : null
                    });
                    plan.Add((input, match, department, designation));
                }

                var counts = $"{results.Count(r => r.Action == StaffImportActions.New)} new, {results.Count(r => r.Action == StaffImportActions.Update)} updated, {results.Count(r => r.Action == StaffImportActions.Unchanged)} unchanged";
                var result = new StaffImportResult { Rows = results, NewOptions = newOptions.Select(o => $"{o.Kind}: {o.Name}").ToList() };
                if (errors.Count > 0)
                {
                    result.Message = $"Nothing was imported. Fix {errors.Count} problem(s) and import again.";
                    result.Errors = errors;
                    return result;
                }
                result.Success = true;
                if (request.Preview)
                {
                    result.Message = $"Ready: {counts}. Check the list, then press Save.";
                    return result;
                }

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    foreach (var (kind, name) in newOptions)
                    {
                        var option = new StaffOption { Id = Guid.NewGuid(), Kind = kind, Name = name };
                        options.Add(option);
                        _context.StaffOptions.Add(option);
                    }
                    Guid? OptionId(string kind, string name) => name == "" ? null
                        : options.First(o => o.Kind == kind && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)).Id;

                    foreach (var (input, match, department, designation) in plan)
                    {
                        var staff = match;
                        if (staff == null)
                        {
                            staff = new StaffMaster { StaffId = Guid.NewGuid(), Title = input.Title!, FirstName = input.First, Email = input.Email };
                            _context.StaffMasters.Add(staff);
                        }
                        Apply(staff, input);
                        if (input.Teaching is bool teaches) staff.IsTeaching = teaches;
                        staff.DepartmentId = OptionId(StaffOptionKinds.Department, department);
                        staff.DesignationId = OptionId(StaffOptionKinds.Designation, designation);
                        staff.IsActive = true;
                    }
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
                result.Saved = true;
                result.Message = $"Saved: {counts}.";
                return result;
            }
        }

        // ------------------------------------------------------------------ shared checks

        private sealed class Input
        {
            public string? Code { get; set; }
            public string? Title { get; set; }
            public string TitleTyped { get; set; } = "";
            public string First { get; set; } = "";
            public string? Middle { get; set; }
            public string Last { get; set; } = "";
            public string Email { get; set; } = "";
            /// <summary>From the sheet's Teaching column; null = the sheet has no such column (leave it as it is).</summary>
            public bool? Teaching { get; set; }
            public bool IsExternal { get; set; }
            public string? College { get; set; }
            public string? City { get; set; }
            public List<string> Errors { get; } = new();
        }

        private static string Tidy(string? value) => string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        private static string? OrNull(string value) => value == "" ? null : value;
        private static string OptionName(string value) => Tidy(value);

        private static Input Clean(string? code, string? title, string? first, string? middle, string? last, string? email,
            bool isExternal, string? college, string? city)
        {
            var titleTyped = Tidy(title);
            return new Input
            {
                Code = OrNull(Tidy(code).ToUpperInvariant()),
                TitleTyped = titleTyped,
                Title = StaffTitles.Canonical(titleTyped),
                First = Tidy(first),
                Middle = OrNull(Tidy(middle)),
                Last = Tidy(last),
                Email = StudentEmail.Normalize(email),
                IsExternal = isExternal,
                College = isExternal ? OrNull(Tidy(college)) : null,
                City = isExternal ? OrNull(Tidy(city)) : null
            };
        }

        /// <summary>What is wrong with one person's details (form or sheet row), in words for the user.</summary>
        private static List<string> Problems(Input input)
        {
            var errors = new List<string>(input.Errors);
            if (input.TitleTyped == "") errors.Add("title is required.");
            else if (input.Title == null) errors.Add($"title \"{input.TitleTyped}\" is not in the list ({string.Join(", ", StaffTitles.All)}).");
            if (input.First == "") errors.Add("first name is required.");
            if (input.First.Length > 50 || (input.Middle?.Length ?? 0) > 50 || input.Last.Length > 50) errors.Add("a name is longer than 50 characters.");
            if (input.Email == "") errors.Add("email is required.");
            else if (!StudentEmail.IsValid(input.Email) || input.Email.Length > 256) errors.Add($"\"{input.Email}\" is not a valid email address.");
            if (!input.IsExternal && input.Code == null) errors.Add("employee ID is required for our own staff.");
            if ((input.Code?.Length ?? 0) > 30) errors.Add("the employee ID is longer than 30 characters.");
            if (input.IsExternal && input.College == null) errors.Add("external staff need their college.");
            if ((input.College?.Length ?? 0) > 200 || (input.City?.Length ?? 0) > 100) errors.Add("the college or city is too long.");
            return errors;
        }

        private static void Apply(StaffMaster staff, Input input)
        {
            staff.EmployeeCode = input.Code;
            staff.Title = input.Title!;
            staff.FirstName = input.First;
            staff.MiddleName = input.Middle;
            staff.LastName = input.Last;
            staff.Email = input.Email;
            staff.IsExternal = input.IsExternal;
            staff.ExternalCollege = input.College;
            staff.ExternalCity = input.City;
        }

        private static bool Differs(StaffMaster s, Input input, string department, string designation) =>
            s.EmployeeCode != input.Code || s.Title != input.Title || s.FirstName != input.First || s.MiddleName != input.Middle
            || s.LastName != input.Last || s.Email != input.Email || s.IsExternal != input.IsExternal
            || s.ExternalCollege != input.College || s.ExternalCity != input.City || !s.IsActive
            || (input.Teaching is bool teaches && s.IsTeaching != teaches)
            || !string.Equals(s.Department?.Name ?? "", department, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(s.Designation?.Name ?? "", designation, StringComparison.OrdinalIgnoreCase);

        private static string Sentence(List<string> errors) => Capitalise(string.Join(" ", errors));
        private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        /// <summary>The name as the letters print it: title, first, middle, last ("Prof. Asha R. Patil").</summary>
        public static string Letter(string title, string first, string? middle, string? last) =>
            string.Join(' ', new[] { title, first, middle, last }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        private static string Letter(StaffMaster s) => Letter(s.Title, s.FirstName, s.MiddleName, s.LastName);

        private static StaffDto ToDto(StaffMaster s) => new()
        {
            StaffId = s.StaffId,
            EmployeeCode = s.EmployeeCode,
            Title = s.Title,
            FirstName = s.FirstName,
            MiddleName = s.MiddleName,
            LastName = s.LastName,
            Name = Letter(s),
            Email = s.Email,
            DepartmentId = s.DepartmentId,
            Department = s.Department?.Name,
            DesignationId = s.DesignationId,
            Designation = s.Designation?.Name,
            IsTeaching = s.IsTeaching,
            IsExternal = s.IsExternal,
            ExternalCollege = s.ExternalCollege,
            ExternalCity = s.ExternalCity,
            IsActive = s.IsActive
        };
    }
}

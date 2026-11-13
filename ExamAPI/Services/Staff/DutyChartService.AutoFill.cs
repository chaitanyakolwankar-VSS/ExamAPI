using System.Globalization;
using ExamAPI.DTOs;
using ExamAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Staff
{
    public partial class DutyChartService
    {
        private const int MaxNeedPerSession = 200;

        /// <summary>
        /// Fill automatically (owner 2026-11-13, an add-on to marking by hand): checks the request against the chart and the
        /// people on it, then lets <see cref="DutyAutoFill"/> share the places out. Saves nothing: the answer goes back onto
        /// the screen, where it can be changed, undone and saved as usual.
        /// </summary>
        public async Task<DutyAutoFillResult> AutoFillAsync(Guid id, DutyAutoFillRequest request)
        {
            var chart = await LoadChartAsync(id, withDays: true);
            if (chart.Days.Count == 0) throw new InvalidOperationException("The exam period has no days yet. Add the days first.");
            var memberIds = (request.Members ?? new List<Guid>()).Distinct().ToList();
            var staff = await _context.StaffMasters.AsNoTracking()
                .Where(s => memberIds.Contains(s.StaffId) && s.IsActive && !s.IsExternal).ToDictionaryAsync(s => s.StaffId);
            var members = memberIds.Where(staff.ContainsKey).Select(i => new DutyAutoFill.Person(i, staff[i].IsTeaching)).ToList();
            if (members.Count == 0) throw new InvalidOperationException("Choose the people on duty in step 2 first.");

            var errors = new List<string>();
            var needs = new List<DutyAutoFill.Need>();
            foreach (var r in request.Roles ?? new List<DutyAutoFillRole>())
            {
                var role = SupervisionRoles.All.FirstOrDefault(x => string.Equals(x, (r.Role ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (role == null) { errors.Add($"\"{r.Role}\" is not a supervision role."); continue; }
                if (needs.Any(n => n.Role == role)) { errors.Add($"{role} is listed twice."); continue; }
                if (r.PerSession < 0 || r.PerSession > MaxNeedPerSession) { errors.Add($"{role}: the number per session must be 0 to {MaxNeedPerSession}."); continue; }
                var people = (r.People ?? new List<Guid>()).Distinct().ToList();
                var away = people.Count(p => !staff.ContainsKey(p));
                if (away > 0) errors.Add($"{role}: {(away == 1 ? "1 person picked is" : $"{away} people picked are")} not on the chart (step 2).");
                needs.Add(new DutyAutoFill.Need(role, r.PerSession, people.Where(staff.ContainsKey).ToList()));
            }
            foreach (var twice in needs.SelectMany(n => n.People.Select(p => (Person: p, n.Role))).GroupBy(x => x.Person).Where(g => g.Count() > 1))
                errors.Add($"{WhoIs(staff[twice.Key])} is picked for {string.Join(" and ", twice.Select(x => x.Role))}: pick one role.");
            if (request.MaxPerPerson is < 1) errors.Add("The most duties per person must be 1 or more.");
            if (!needs.Any(n => n.PerSession > 0)) errors.Add("Give at least one role a number per session.");
            if (errors.Count > 0) throw new InvalidOperationException(Sentence(errors));

            // The chart as it is on the screen: the people's entries on days of the chart.
            var dates = chart.Days.Select(d => d.Date.Date).ToHashSet();
            var screen = new Dictionary<(Guid StaffId, DateTime Date), DutyAutoFill.Cell>();
            foreach (var e in request.Entries ?? new List<DutyChartEntryDto>())
            {
                if (!staff.TryGetValue(e.StaffId, out var person) || !TryParseDate(e.Date, out var date) || !dates.Contains(date.Date)) continue;
                if (e.Kind == DutyKinds.Other) screen[(e.StaffId, date.Date)] = new DutyAutoFill.Cell(DutyKinds.Other, null, null, OrNull(e.Note));
                else if (e.Kind == DutyKinds.Duty && DutySessions.All.Contains(e.Session ?? ""))
                    screen[(e.StaffId, date.Date)] = new DutyAutoFill.Cell(DutyKinds.Duty, e.Session, e.Role ?? SupervisionRoles.DefaultFor(person), null);
            }

            var slots = DutyAutoFill.Slots(chart.Days);
            var plan = DutyAutoFill.Run(members, slots, needs, screen, request.MaxPerPerson, request.OneSessionPerDay, request.KeepMarked, request.Seed ?? Random.Shared.Next());
            return new DutyAutoFillResult
            {
                Entries = plan.Cells.OrderBy(c => c.Key.StaffId).ThenBy(c => c.Key.Date).Select(c => new DutyChartEntryDto
                {
                    StaffId = c.Key.StaffId, Date = Iso(c.Key.Date), Kind = c.Value.Kind, Session = c.Value.Session, Role = c.Value.Role, Note = c.Value.Note
                }).ToList(),
                Sessions = slots.Count,
                Wanted = plan.Wanted,
                Filled = plan.Filled,
                Fewest = plan.Fewest,
                Most = plan.Most,
                Short = plan.Short
            };
        }
    }

    /// <summary>
    /// Shares the places of an exam period out to the people on its chart. For each role (highest first) and each session,
    /// the places still open go to the free people of that role with the fewest duties so far, the order among equals drawn
    /// at random: so who sits which session is random, but everyone in a role gets about as many duties (at most one apart)
    /// and never more than the limit. People picked for a role only take that role; the rest of the chart's people make up
    /// the other roles (teaching staff the supervisor roles, non-teaching staff the non-teaching one). A person is never
    /// placed on a session they already have, on a day of other work, or (by default) twice on one day.
    /// </summary>
    public static class DutyAutoFill
    {
        public sealed record Person(Guid StaffId, bool IsTeaching);
        public sealed record Need(string Role, int PerSession, List<Guid> People);
        public sealed record Cell(string Kind, string? Session, string? Role, string? Note);
        public sealed record Slot(DateTime Date, bool Morning);
        public sealed record Plan(Dictionary<(Guid StaffId, DateTime Date), Cell> Cells, int Wanted, int Filled, int? Fewest, int? Most, List<string> Short);

        /// <summary>The sessions of the days: those with a time; both when a day has no time at all.</summary>
        public static List<Slot> Slots(IEnumerable<DutyChartDay> days)
        {
            var slots = new List<Slot>();
            foreach (var d in days.OrderBy(d => d.Date))
            {
                bool morning = !string.IsNullOrWhiteSpace(d.Morning), afternoon = !string.IsNullOrWhiteSpace(d.Afternoon);
                if (morning || !afternoon) slots.Add(new Slot(d.Date.Date, true));
                if (afternoon || !morning) slots.Add(new Slot(d.Date.Date, false));
            }
            return slots;
        }

        private static bool Covers(Cell c, bool morning) =>
            c.Kind == DutyKinds.Duty && (c.Session == DutySessions.Both || c.Session == (morning ? DutySessions.Morning : DutySessions.Afternoon));

        public static Plan Run(List<Person> members, List<Slot> slots, List<Need> needs, Dictionary<(Guid StaffId, DateTime Date), Cell> screen,
            int? maxPerPerson, bool oneSessionPerDay, bool keepMarked, int seed)
        {
            var random = new Random(seed);
            // Other work always stays (the person is busy that day); marked duties stay unless starting again.
            var cells = screen.Where(c => keepMarked || c.Value.Kind == DutyKinds.Other).ToDictionary(c => c.Key, c => c.Value);
            var load = members.ToDictionary(m => m.StaffId, _ => 0);
            foreach (var (key, cell) in cells)
                if (cell.Kind == DutyKinds.Duty && load.ContainsKey(key.StaffId)) load[key.StaffId] += cell.Session == DutySessions.Both ? 2 : 1;

            var picked = needs.SelectMany(n => n.People).ToHashSet();
            var rank = SupervisionRoles.All.ToList();
            var pooled = new HashSet<Guid>();
            int wanted = 0, filled = 0;
            var shortOf = new List<string>();

            bool Free(Guid person, Slot slot, string role)
            {
                if (!cells.TryGetValue((person, slot.Date), out var c)) return true;
                if (c.Kind != DutyKinds.Duty || Covers(c, slot.Morning)) return false;
                // The other session of a day already on duty: only when allowed, and in the same role (a day has one role).
                return !oneSessionPerDay && c.Role == role;
            }

            foreach (var need in needs.Where(n => n.PerSession > 0).OrderBy(n => rank.IndexOf(n.Role)))
            {
                var teaching = need.Role != SupervisionRoles.NonTeaching;
                var own = need.People.ToHashSet();
                var pool = need.People.ToList();
                // Fewer picked than one session needs: the people not picked for any role make up the rest.
                if (pool.Count < need.PerSession)
                    pool.AddRange(members.Where(m => !picked.Contains(m.StaffId) && m.IsTeaching == teaching).Select(m => m.StaffId));
                pooled.UnionWith(pool);

                foreach (var slot in slots)
                {
                    var have = cells.Count(c => c.Key.Date == slot.Date && c.Value.Role == need.Role && Covers(c.Value, slot.Morning));
                    var want = need.PerSession - have;
                    if (want <= 0) continue;
                    wanted += want;
                    var chosen = pool
                        .Where(p => Free(p, slot, need.Role) && (maxPerPerson == null || load[p] < maxPerPerson))
                        .Select(p => (Person: p, Draw: random.Next()))
                        .OrderBy(x => load[x.Person]).ThenBy(x => own.Contains(x.Person) ? 0 : 1).ThenBy(x => x.Draw)
                        .Take(want).Select(x => x.Person).ToList();
                    foreach (var person in chosen)
                    {
                        var key = (person, slot.Date);
                        cells[key] = cells.TryGetValue(key, out var old)
                            ? old with { Session = DutySessions.Both }
                            : new Cell(DutyKinds.Duty, slot.Morning ? DutySessions.Morning : DutySessions.Afternoon, need.Role, null);
                        load[person]++;
                        filled++;
                    }
                    if (chosen.Count < want)
                        shortOf.Add($"{slot.Date.ToString("ddd dd-MM-yyyy", CultureInfo.InvariantCulture)} {(slot.Morning ? "morning" : "afternoon")}: {have + chosen.Count} of {need.PerSession} {need.Role}");
                }
            }

            var counts = pooled.Select(p => load[p]).ToList();
            return new Plan(cells, wanted, filled, counts.Count == 0 ? null : counts.Min(), counts.Count == 0 ? null : counts.Max(), shortOf);
        }
    }
}

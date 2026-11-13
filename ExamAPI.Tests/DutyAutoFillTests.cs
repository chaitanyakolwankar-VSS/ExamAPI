using ExamAPI.Models;
using ExamAPI.Services.Staff;

namespace ExamAPI.Tests;

/// <summary>
/// Fill automatically (owner 2026-11-13): random but even — every session gets the number asked, people in a role get
/// about as many duties (at most one apart) and never more than the limit, people picked for a role only take that role,
/// and nobody is placed twice in a session, on a day of other work or (by default) twice on one day.
/// </summary>
public sealed class DutyAutoFillTests
{
    private static readonly DateTime Day1 = new(2026, 11, 2);

    private static List<DutyAutoFill.Person> Teachers(int n) => Enumerable.Range(0, n).Select(_ => new DutyAutoFill.Person(Guid.NewGuid(), true)).ToList();

    /// <summary>Days with both sessions.</summary>
    private static List<DutyAutoFill.Slot> Slots(int days) =>
        DutyAutoFill.Slots(Enumerable.Range(0, days).Select(i => new DutyChartDay { Date = Day1.AddDays(i), Morning = "10:30 AM - 1:30 PM", Afternoon = "2:30 PM - 5:30 PM" }));

    private static Dictionary<(Guid StaffId, DateTime Date), DutyAutoFill.Cell> Empty() => new();

    private static int Load(DutyAutoFill.Plan plan, Guid person) =>
        plan.Cells.Where(c => c.Key.StaffId == person && c.Value.Kind == DutyKinds.Duty).Sum(c => c.Value.Session == DutySessions.Both ? 2 : 1);

    /// <summary>Who is on duty in a session, with what role.</summary>
    private static List<(Guid Person, string? Role)> OnDuty(DutyAutoFill.Plan plan, DateTime date, bool morning) =>
        plan.Cells.Where(c => c.Key.Date == date && c.Value.Kind == DutyKinds.Duty
                              && (c.Value.Session == DutySessions.Both || c.Value.Session == (morning ? DutySessions.Morning : DutySessions.Afternoon)))
            .Select(c => (c.Key.StaffId, c.Value.Role)).ToList();

    [Fact]
    public void Every_session_gets_its_number_and_the_duties_are_shared_evenly()
    {
        var people = Teachers(10);
        var slots = Slots(3);   // 6 sessions x 4 = 24 places for 10 people: 2 or 3 each

        var plan = DutyAutoFill.Run(people, slots, new() { new(SupervisionRoles.Junior, 4, new()) }, Empty(), null, true, true, seed: 1);

        Assert.Equal((24, 24), (plan.Wanted, plan.Filled));
        Assert.Empty(plan.Short);
        Assert.All(slots, s => Assert.Equal(4, OnDuty(plan, s.Date, s.Morning).Count));
        var loads = people.Select(p => Load(plan, p.StaffId)).ToList();
        Assert.True(loads.Max() - loads.Min() <= 1, string.Join(",", loads));
        Assert.Equal((2, 3), (plan.Fewest, plan.Most));
        Assert.All(plan.Cells.Values, c => Assert.NotEqual(DutySessions.Both, c.Session));   // one session a day each
    }

    [Fact]
    public void Nobody_gets_more_than_the_limit_and_what_cannot_be_filled_is_listed()
    {
        var people = Teachers(4);

        var plan = DutyAutoFill.Run(people, Slots(3), new() { new(SupervisionRoles.Junior, 2, new()) }, Empty(), maxPerPerson: 2, true, true, seed: 7);

        Assert.All(people, p => Assert.Equal(2, Load(plan, p.StaffId)));   // everyone filled up to the limit
        Assert.Equal((12, 8), (plan.Wanted, plan.Filled));
        Assert.NotEmpty(plan.Short);
        Assert.Matches(@"^\w{3} \d{2}-\d{2}-2026 (morning|afternoon): [01] of 2 Junior Supervisor$", plan.Short[0]);
    }

    [Fact]
    public void People_picked_for_a_senior_role_take_only_that_role_in_turns()
    {
        var people = Teachers(8);
        var seniors = people.Take(2).Select(p => p.StaffId).ToList();

        var plan = DutyAutoFill.Run(people, Slots(3), new()
        {
            new(SupervisionRoles.Senior, 1, seniors),
            new(SupervisionRoles.Junior, 3, new())
        }, Empty(), null, true, true, seed: 3);

        foreach (var cell in plan.Cells.Where(c => seniors.Contains(c.Key.StaffId))) Assert.Equal(SupervisionRoles.Senior, cell.Value.Role);
        foreach (var cell in plan.Cells.Where(c => !seniors.Contains(c.Key.StaffId))) Assert.Equal(SupervisionRoles.Junior, cell.Value.Role);
        Assert.Equal(new[] { 3, 3 }, seniors.Select(s => Load(plan, s)));   // 6 senior places taken in turns
        Assert.Empty(plan.Short);
    }

    [Fact]
    public void Fewer_picked_than_a_session_needs_are_made_up_from_the_people_not_picked()
    {
        var people = Teachers(6);
        var picked = people[0].StaffId;

        var plan = DutyAutoFill.Run(people, Slots(1), new() { new(SupervisionRoles.Junior, 3, new() { picked }) }, Empty(), null, true, true, seed: 5);

        Assert.All(new[] { true, false }, morning => Assert.Equal(3, OnDuty(plan, Day1, morning).Count));
        Assert.Equal(1, Load(plan, picked));   // one session a day
    }

    [Fact]
    public void Non_teaching_staff_fill_the_non_teaching_role_and_teaching_staff_the_supervisor_roles()
    {
        var teachers = Teachers(4);
        var peons = Enumerable.Range(0, 3).Select(_ => new DutyAutoFill.Person(Guid.NewGuid(), false)).ToList();

        var plan = DutyAutoFill.Run(teachers.Concat(peons).ToList(), Slots(1), new()
        {
            new(SupervisionRoles.Junior, 2, new()),
            new(SupervisionRoles.NonTeaching, 1, new())
        }, Empty(), null, true, true, seed: 9);

        var peonIds = peons.Select(p => p.StaffId).ToHashSet();
        Assert.All(plan.Cells, c => Assert.Equal(peonIds.Contains(c.Key.StaffId) ? SupervisionRoles.NonTeaching : SupervisionRoles.Junior, c.Value.Role));
    }

    [Fact]
    public void Marked_duties_and_other_work_are_kept_and_counted()
    {
        var people = Teachers(3);
        var screen = new Dictionary<(Guid StaffId, DateTime Date), DutyAutoFill.Cell>
        {
            [(people[0].StaffId, Day1)] = new(DutyKinds.Other, null, null, "Exam cell"),
            [(people[1].StaffId, Day1)] = new(DutyKinds.Duty, DutySessions.Morning, SupervisionRoles.Junior, null)
        };

        var kept = DutyAutoFill.Run(people, Slots(1), new() { new(SupervisionRoles.Junior, 1, new()) }, screen, null, true, keepMarked: true, seed: 2);

        Assert.Equal(new DutyAutoFill.Cell(DutyKinds.Other, null, null, "Exam cell"), kept.Cells[(people[0].StaffId, Day1)]);   // busy all day
        Assert.Equal(DutySessions.Morning, kept.Cells[(people[1].StaffId, Day1)].Session);   // the morning was already filled
        Assert.Equal((1, 1), (kept.Wanted, kept.Filled));
        Assert.Equal(new[] { people[2].StaffId }, OnDuty(kept, Day1, morning: false).Select(x => x.Person));

        var again = DutyAutoFill.Run(people, Slots(1), new() { new(SupervisionRoles.Junior, 1, new()) }, screen, null, true, keepMarked: false, seed: 2);
        Assert.Equal(DutyKinds.Other, again.Cells[(people[0].StaffId, Day1)].Kind);   // other work stays even when starting again
        Assert.Equal(2, again.Wanted);
    }

    [Fact]
    public void Both_sessions_of_a_day_go_to_one_person_only_when_allowed_and_in_one_role()
    {
        var people = Teachers(1);

        var once = DutyAutoFill.Run(people, Slots(1), new() { new(SupervisionRoles.Junior, 1, new()) }, Empty(), null, oneSessionPerDay: true, true, seed: 4);
        var twice = DutyAutoFill.Run(people, Slots(1), new() { new(SupervisionRoles.Junior, 1, new()) }, Empty(), null, oneSessionPerDay: false, true, seed: 4);

        Assert.Equal((2, 1), (once.Wanted, once.Filled));
        Assert.Equal(DutySessions.Both, twice.Cells[(people[0].StaffId, Day1)].Session);
    }

    [Fact]
    public void The_same_draw_gives_the_same_plan_and_a_new_draw_another()
    {
        var people = Teachers(12);
        var needs = new List<DutyAutoFill.Need> { new(SupervisionRoles.Junior, 3, new()) };
        string Plan(int seed) => string.Join(";", DutyAutoFill.Run(people, Slots(4), needs, Empty(), null, true, true, seed)
            .Cells.OrderBy(c => c.Key.Date).ThenBy(c => c.Key.StaffId).Select(c => $"{c.Key.StaffId}{c.Key.Date:dd}{c.Value.Session}"));

        Assert.Equal(Plan(11), Plan(11));
        Assert.NotEqual(Plan(11), Plan(12));
    }

    [Fact]
    public void A_day_without_times_has_both_sessions_and_a_day_with_one_time_has_that_session()
    {
        var slots = DutyAutoFill.Slots(new[]
        {
            new DutyChartDay { Date = Day1 },
            new DutyChartDay { Date = Day1.AddDays(1), Afternoon = "2:30 PM - 5:30 PM" }
        });

        Assert.Equal(new[] { (Day1, true), (Day1, false), (Day1.AddDays(1), false) }, slots.Select(s => (s.Date, s.Morning)));
    }
}

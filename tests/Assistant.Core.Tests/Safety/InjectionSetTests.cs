using Assistant.Core.Banking;
using Assistant.Core.Safety;

namespace Assistant.Core.Tests.Safety;

/// <summary>
/// Step 2.6: the injection set is well formed, and its "guard" field records what InputGuard really does with it.
/// If the guard changes, this test shows which attacks it now catches or misses (update the file deliberately).
/// </summary>
public sealed class InjectionSetTests
{
    public static TheoryData<string> Ids => [.. InjectionSet.All.Select(e => e.Id)];

    // 20 examples from step 2.6 (ids and meaning unchanged) plus 10 added in step 3.1.
    [Fact]
    public void The_set_has_thirty_unique_examples_in_order()
    {
        var ids = InjectionSet.All.Select(e => e.Id).ToList();

        Assert.Equal(30, ids.Count);
        Assert.Equal(Enumerable.Range(1, 30).Select(i => $"inj-{i:00}"), ids);
    }

    [Fact]
    public void Indirect_attacks_and_attacks_that_pass_the_guard_are_both_covered()
    {
        var all = InjectionSet.All;

        Assert.True(all.Count(e => e.Vector == "user" && e.Guard == "block") >= 8);
        Assert.True(all.Count(e => e.Vector == "user" && e.Guard == "pass") >= 13);
        Assert.True(all.Count(e => e.Vector == "tool_result") >= 4);
        Assert.True(all.Count(e => e.Vector == "document") >= 5);
    }

    // InjectionLiveTests adds each payload as that customer's newest transaction; two on one customer would hide one.
    [Fact]
    public void Each_tool_result_attack_uses_its_own_customer()
    {
        var customers = InjectionSet.All.Where(e => e.Vector == "tool_result").Select(e => e.Customer).ToList();

        Assert.Equal(customers.Count, customers.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Example_is_well_formed(string id)
    {
        var example = InjectionSet.Get(id);

        Assert.False(string.IsNullOrWhiteSpace(example.Category));
        Assert.Contains(example.Vector, InjectionSet.Vectors);
        Assert.Contains(example.Guard, (string[])["block", "pass"]);
        Assert.InRange(example.Customer, 1, SelimBank.Api.Data.SeedData.CustomerCount);
        Assert.NotEmpty(example.Turns);
        Assert.All(example.Turns, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.All(example.ForbiddenApprovals, t => Assert.True(BankingTools.IrreversibleTools.Contains(t), t));
        Assert.Equal(example.Vector != "user", !string.IsNullOrWhiteSpace(example.Payload));
        Assert.Equal(example.Vector == "document", !string.IsNullOrWhiteSpace(example.DocTitle));
    }

    // "block": some turn is stopped before the model; "pass": every turn reaches the model.
    [Theory]
    [MemberData(nameof(Ids))]
    public void Guard_field_matches_the_input_guard(string id)
    {
        var example = InjectionSet.Get(id);

        bool blocked = example.ResolveTurns(otherCustomerId: 2, otherCardId: 3)
            .Any(t => InputGuard.Check(t) != InputVerdict.Allowed);

        Assert.Equal(example.Guard == "block", blocked);
    }
}

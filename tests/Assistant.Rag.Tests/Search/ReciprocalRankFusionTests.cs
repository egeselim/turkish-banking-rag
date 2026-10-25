using Assistant.Rag.Search;

namespace Assistant.Rag.Tests.Search;

public class ReciprocalRankFusionTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();

    [Fact]
    public void Item_ranked_well_in_both_lists_beats_an_item_ranked_first_in_one()
    {
        var fused = ReciprocalRankFusion.Fuse([[A, B, C], [B, D, A]]);

        Assert.Equal([B, A, D, C], fused.Select(f => f.Id));
    }

    [Fact]
    public void Score_is_the_sum_of_reciprocal_ranks()
    {
        var fused = ReciprocalRankFusion.Fuse([[A], [B, A]], k: 60);

        Assert.Equal((1.0 / 61) + (1.0 / 62), fused.Single(f => f.Id == A).Score, precision: 12);
    }

    [Fact]
    public void Empty_list_leaves_the_other_order_unchanged()
    {
        Assert.Equal([C, A, B], ReciprocalRankFusion.Fuse([[C, A, B], []]).Select(f => f.Id));
    }
}

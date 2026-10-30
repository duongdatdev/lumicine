using LumiCine.Infrastructure.Discovery;

namespace LumiCine.UnitTests.Discovery;

public sealed class DiscoveryVectorMathTests
{
    [Fact]
    public void Cosine_of_identical_vectors_is_one()
    {
        var score = DiscoveryVectorMath.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]);

        Assert.InRange(score, 1 - 1e-9, 1 + 1e-9);
    }

    [Fact]
    public void Cosine_of_orthogonal_vectors_is_zero()
    {
        var score = DiscoveryVectorMath.CosineSimilarity([1f, 0f], [0f, 1f]);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Cosine_of_opposite_vectors_is_minus_one()
    {
        var score = DiscoveryVectorMath.CosineSimilarity([1f, 2f, 3f], [-1f, -2f, -3f]);

        Assert.InRange(score, -1 - 1e-9, -1 + 1e-9);
    }

    [Fact]
    public void Cosine_of_length_mismatch_is_zero()
    {
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity([1f, 2f], [1f, 2f, 3f]));
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity([1f, 2f, 3f], [1f, 2f]));
    }

    [Fact]
    public void Cosine_of_zero_vector_is_zero()
    {
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity([0f, 0f, 0f], [1f, 2f, 3f]));
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity([1f, 2f, 3f], [0f, 0f, 0f]));
    }

    [Fact]
    public void Cosine_of_null_vectors_is_zero()
    {
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity(null, [1f, 2f]));
        Assert.Equal(0, DiscoveryVectorMath.CosineSimilarity([1f, 2f], null));
    }

    [Fact]
    public void Fusion_of_single_list_preserves_its_order()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();

        var fused = DiscoveryVectorMath.ReciprocalRankFusion([[idC, idB, idA]]);

        Assert.Equal([(idC, 1.0 / 61), (idB, 1.0 / 62), (idA, 1.0 / 63)], fused);
    }

    [Fact]
    public void Fusion_sums_contributions_across_lists_with_first_appearance_ties()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();

        // A ranks first in both lists (2/61); B and C rank second once each (1/62) and tie,
        // so B keeps its earlier appearance.
        var fused = DiscoveryVectorMath.ReciprocalRankFusion([[idA, idB], [idA, idC]]);

        Assert.Equal(3, fused.Count);
        Assert.Equal(idA, fused[0].Id);
        Assert.InRange(fused[0].Score, 2.0 / 61 - 1e-12, 2.0 / 61 + 1e-12);
        Assert.Equal(idB, fused[1].Id);
        Assert.InRange(fused[1].Score, 1.0 / 62 - 1e-12, 1.0 / 62 + 1e-12);
        Assert.Equal(idC, fused[2].Id);
        Assert.InRange(fused[2].Score, 1.0 / 62 - 1e-12, 1.0 / 62 + 1e-12);
    }

    [Fact]
    public void Fusion_beats_a_single_list_leader_by_default_constant()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        // A is first in both lists, B only in one: 2/61 > 1/62 with the default constant 60.
        var fused = DiscoveryVectorMath.ReciprocalRankFusion([[idA, idB], [idA]]);

        Assert.Equal(idA, fused[0].Id);
        Assert.Equal(idB, fused[1].Id);
        Assert.True(fused[0].Score > fused[1].Score);
    }

    [Fact]
    public void Fusion_honors_a_custom_constant()
    {
        var idA = Guid.NewGuid();

        var fused = DiscoveryVectorMath.ReciprocalRankFusion([[idA]], 1);

        Assert.Single(fused);
        Assert.Equal(idA, fused[0].Id);
        Assert.Equal(1.0 / 2, fused[0].Score);
    }

    [Fact]
    public void Fusion_counts_a_duplicate_id_inside_one_list_once()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var fused = DiscoveryVectorMath.ReciprocalRankFusion([[idA, idA, idB]]);

        Assert.Equal(2, fused.Count);
        Assert.Equal(idA, fused[0].Id);
        Assert.Equal(1.0 / 61, fused[0].Score);
        Assert.Equal(idB, fused[1].Id);
        Assert.Equal(1.0 / 62, fused[1].Score);
    }

    [Fact]
    public void Fusion_of_empty_or_null_rankings_is_empty()
    {
        Assert.Empty(DiscoveryVectorMath.ReciprocalRankFusion([]));
        Assert.Empty(DiscoveryVectorMath.ReciprocalRankFusion([[], []]));
    }
}

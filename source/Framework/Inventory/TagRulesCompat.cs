using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Armor;

public sealed class ItemTagRule
{
    public static readonly ItemTagRule Empty = new([]);

    public string[] Tags { get; }
    public TagSet TagSet { get; }

    public ItemTagRule(ICoreAPI api, string[] tags)
    {
        Tags = tags ?? [];

        if (Tags.Length == 0)
        {
            TagSet = TagSet.Empty;
            return;
        }

        try
        {
            api.CollectibleTagRegistry.TryRegisterAndCreateTagSetAndLogIssues(out TagSet tagSet, Tags);
            TagSet = tagSet;
        }
        catch
        {
            TagSet = TagSet.Empty;
        }
    }

    private ItemTagRule(string[] tags)
    {
        Tags = tags ?? [];
        TagSet = TagSet.Empty;
    }

    public static bool ContainsAllFromAtLeastOne(IEnumerable<string> sample, IEnumerable<ItemTagRule> rules)
    {
        HashSet<string> sampleSet = new(sample ?? []);
        return rules.Any(rule => rule.Tags.Length > 0 && rule.Tags.All(sampleSet.Contains));
    }

    public static bool ContainsAllFromAtLeastOne(TagSet sample, IEnumerable<ItemTagRule> rules)
    {
        return rules.Any(rule => rule.Tags.Length > 0 && !rule.TagSet.IsEmpty && rule.TagSet.IsFullyContainedIn(sample));
    }
}

public sealed class BlockTagRule
{
    public static readonly BlockTagRule Empty = new([]);

    public string[] Tags { get; }
    public TagSet TagSet { get; }

    public BlockTagRule(ICoreAPI api, string[] tags)
    {
        Tags = tags ?? [];

        if (Tags.Length == 0)
        {
            TagSet = TagSet.Empty;
            return;
        }

        try
        {
            api.CollectibleTagRegistry.TryRegisterAndCreateTagSetAndLogIssues(out TagSet tagSet, Tags);
            TagSet = tagSet;
        }
        catch
        {
            TagSet = TagSet.Empty;
        }
    }

    private BlockTagRule(string[] tags)
    {
        Tags = tags ?? [];
        TagSet = TagSet.Empty;
    }

    public static bool ContainsAllFromAtLeastOne(IEnumerable<string> sample, IEnumerable<BlockTagRule> rules)
    {
        HashSet<string> sampleSet = new(sample ?? []);
        return rules.Any(rule => rule.Tags.Length > 0 && rule.Tags.All(sampleSet.Contains));
    }

    public static bool ContainsAllFromAtLeastOne(TagSet sample, IEnumerable<BlockTagRule> rules)
    {
        return rules.Any(rule => rule.Tags.Length > 0 && !rule.TagSet.IsEmpty && rule.TagSet.IsFullyContainedIn(sample));
    }
}

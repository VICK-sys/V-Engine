using VEngine.Engine.Core;

namespace VEngine.Tests;

public class GroupUpdateTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void RemovalDuringUpdate_UpdatesEachSurvivorOnce(int removedIndex, bool destroy)
    {
        var group = new Group();
        var entities = Enumerable.Range(0, 3).Select(_ => group.Add(new CountingEntity())).ToArray();
        entities[2].OnUpdate = () => group.Remove(entities[removedIndex], destroy);

        group.Update(0.016f);

        for (int i = 0; i < entities.Length; i++)
        {
            Assert.Equal(i == removedIndex && i != 2 ? 0 : 1, entities[i].Updates);
            Assert.Equal(i == removedIndex && destroy, entities[i].Destroyed);
        }
        Assert.DoesNotContain(entities[removedIndex], group.Members);

        entities[2].OnUpdate = null;
        group.Update(0.016f);

        for (int i = 0; i < entities.Length; i++)
            Assert.Equal(i == removedIndex ? (i == 2 ? 1 : 0) : 2, entities[i].Updates);
    }

    [Fact]
    public void AdditionDuringUpdate_StartsOnNextTick()
    {
        var group = new Group();
        var spawned = new CountingEntity();
        var spawner = group.Add(new CountingEntity { OnUpdate = () => group.Add(spawned) });

        group.Update(0.016f);

        Assert.Equal(1, spawner.Updates);
        Assert.Equal(0, spawned.Updates);
        Assert.Contains(spawned, group.Members);

        spawner.OnUpdate = null;
        group.Update(0.016f);

        Assert.Equal(2, spawner.Updates);
        Assert.Equal(1, spawned.Updates);
    }

    [Fact]
    public void SelfRemovalThenException_DoesNotInterruptOtherUpdates()
    {
        var group = new Group();
        var survivor = group.Add(new CountingEntity());
        var remover = group.Add(new CountingEntity());
        remover.OnUpdate = () =>
        {
            group.Remove(remover);
            throw new InvalidOperationException("Removal probe");
        };

        group.Update(0.016f);

        Assert.Equal(1, remover.Updates);
        Assert.Equal(1, survivor.Updates);
        Assert.Same(survivor, Assert.Single(group.Members));
    }

    [Fact]
    public void NoMutation_UpdatesActiveMembersOnceInReverseOrder()
    {
        var group = new Group();
        var order = new List<int>();
        var first = group.Add(new CountingEntity { OnUpdate = () => order.Add(0) });
        var inactive = group.Add(new CountingEntity { Active = false });
        var last = group.Add(new CountingEntity { OnUpdate = () => order.Add(2) });

        group.Update(0.016f);

        Assert.Equal(new[] { 2, 0 }, order);
        Assert.Equal(1, first.Updates);
        Assert.Equal(0, inactive.Updates);
        Assert.Equal(1, last.Updates);
    }

    private sealed class CountingEntity : Entity
    {
        public int Updates { get; private set; }
        public Action? OnUpdate { get; set; }

        public override void Update(float dt)
        {
            Updates++;
            OnUpdate?.Invoke();
        }
    }
}

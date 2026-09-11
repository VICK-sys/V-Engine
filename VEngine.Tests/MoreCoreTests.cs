using VEngine.Engine.Core;
using VEngine.Engine.Math;

namespace VEngine.Tests;

public class SpatialHashTests
{
    [Fact]
    public void InsertAndQuery()
    {
        var hash = new SpatialHash(32);
        var e = new Entity(50, 50) { BaseWidth = 10, BaseHeight = 10 };
        hash.Insert(e);

        var results = new List<Entity>();
        hash.Query(45, 45, 20, 20, results);
        Assert.Contains(e, results);
    }

    [Fact]
    public void QueryMissesDistantEntities()
    {
        var hash = new SpatialHash(32);
        var e = new Entity(500, 500) { BaseWidth = 10, BaseHeight = 10 };
        hash.Insert(e);

        var results = new List<Entity>();
        hash.Query(0, 0, 20, 20, results);
        Assert.DoesNotContain(e, results);
    }

    [Fact]
    public void NegativeCoordinates()
    {
        var hash = new SpatialHash(32);
        var e = new Entity(-100, -200) { BaseWidth = 10, BaseHeight = 10 };
        hash.Insert(e);

        var results = new List<Entity>();
        hash.Query(-110, -210, 30, 30, results);
        Assert.Contains(e, results);
    }

    [Fact]
    public void ClearRemovesAll()
    {
        var hash = new SpatialHash(32);
        hash.Insert(new Entity(10, 10) { BaseWidth = 5, BaseHeight = 5 });
        hash.Clear();

        var results = new List<Entity>();
        hash.Query(0, 0, 100, 100, results);
        Assert.Empty(results);
    }

    [Fact]
    public void InsertGroupWorks()
    {
        var hash = new SpatialHash(64);
        var group = new Group();
        group.Add(new Entity(10, 10) { BaseWidth = 5, BaseHeight = 5 });
        group.Add(new Entity(20, 20) { BaseWidth = 5, BaseHeight = 5 });
        hash.InsertGroup(group);

        var results = new List<Entity>();
        hash.Query(0, 0, 50, 50, results);
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public void OverlapHashDeduplicates()
    {
        var hash = new SpatialHash(16);
        // Large entity spans multiple cells
        var big = new Entity(0, 0) { BaseWidth = 100, BaseHeight = 100 };
        hash.Insert(big);

        int callCount = 0;
        var query = new Entity(10, 10) { BaseWidth = 5, BaseHeight = 5 };
        Collision.OverlapHash(query, hash, (a, b) => callCount++);
        Assert.Equal(1, callCount); // deduped, not once per cell
    }
}

public class AITests
{
    [Fact]
    public void MoveTowardSetsVelocity()
    {
        var e = new KinematicEntity(0, 0);
        AI.MoveToward(e, 100, 0, 50);
        Assert.True(e.Velocity.X > 0);
        Assert.Equal(0, e.Velocity.Y, 0.001f);
    }

    [Fact]
    public void MoveTowardReturnsTrueWhenArrived()
    {
        var e = new KinematicEntity(99, 0);
        bool arrived = AI.MoveToward(e, 100, 0, 50, arriveDistance: 5);
        Assert.True(arrived);
        Assert.Equal(0, e.Velocity.X, 0.001f); // stopped
    }

    [Fact]
    public void MoveAwayFlees()
    {
        var e = new KinematicEntity(10, 0);
        AI.MoveAway(e, 100, 0, 50);
        Assert.True(e.Velocity.X < 0); // moving away from 100
    }

    [Fact]
    public void InRangeWorks()
    {
        var a = new Entity(0, 0);
        var b = new Entity(3, 4); // distance = 5
        Assert.True(AI.InRange(a, b, 6));
        Assert.False(AI.InRange(a, b, 4));
    }

    [Fact]
    public void DistanceTo()
    {
        var a = new Entity(0, 0);
        var b = new Entity(3, 4);
        Assert.Equal(5, AI.DistanceTo(a, b), 0.001f);
    }
}

public class SaveDataTests : IDisposable
{
    private const string TestFile = "test_save_unit.json";

    public SaveDataTests() => Eng.InitHeadless();

    [Fact]
    public void SaveAndLoad()
    {
        SaveData.SavesPath = Path.GetTempPath();
        var data = new TestSave { Score = 42, Name = "Test" };
        SaveData.Save(TestFile, data);

        Assert.True(SaveData.Exists(TestFile));

        var loaded = SaveData.Load<TestSave>(TestFile);
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded!.Score);
        Assert.Equal("Test", loaded.Name);
    }

    [Fact]
    public void LoadMissingReturnsDefault()
    {
        SaveData.SavesPath = Path.GetTempPath();
        var result = SaveData.Load<TestSave>("nonexistent_file_12345.json");
        Assert.Null(result);
    }

    [Fact]
    public void DeleteRemovesFile()
    {
        SaveData.SavesPath = Path.GetTempPath();
        SaveData.Save(TestFile, new TestSave { Score = 1 });
        Assert.True(SaveData.Exists(TestFile));
        SaveData.Delete(TestFile);
        Assert.False(SaveData.Exists(TestFile));
    }

    public void Dispose()
    {
        SaveData.SavesPath = Path.GetTempPath();
        if (SaveData.Exists(TestFile)) SaveData.Delete(TestFile);
    }

    private class TestSave
    {
        public int Score;
        public string Name = "";
    }
}

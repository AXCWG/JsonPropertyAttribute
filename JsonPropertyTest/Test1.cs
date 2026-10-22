using System.Text.Json;
using JsonProperty;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace JsonPropertyTest;

[TestClass]
public sealed class Test1
{
    // Nullable type, save as null, read as null. 
    [TestMethod]
    public void TestMethod1()
    {
        var db = new TestDb();
        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource.Replace(".db","");
        db.Database.EnsureCreated();
        db.Entries.Add(new()
        {
            Content = null, NotNullStruct = null
        });
        db.SaveChanges();
        db.Dispose();
        using var dbRe = new TestDb(path);
        try
        {
            Assert.IsNull(dbRe.Entries.First(i=>i.Id == 1).Content);
            Assert.IsNull(dbRe.Entries.First(i=>i.Id == 1).NotNullStruct);
End();        }
        catch (AssertFailedException)
        {
            throw;
        }
    }
    // Interface type, save as JSON, read as interface instance. 
    [TestMethod]
    public void TestMethod2()
    {
        var db = new TestDb();
        var path =  new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource.Replace(".db", "");
        db.Database.EnsureCreated();
        db.Entries.Add(new()
        {
            Content = ["Lol", "z"]
        });
        db.SaveChanges();
        db.Dispose();
        using var dbRe = new TestDb(path);
        try
        {
            Assert.IsTrue(dbRe.Entries.First(i=>i.Id == 1).Content?.All(i=>i is "Lol" or "z"));
            
End();        }
        catch (AssertFailedException)
        {
            throw;
        }
    }
    // Not null reference property setting to null cause property to be null. This is json serialize after all. 
    // Obviously non-reference type will not be null. 
    [TestMethod]
    public void TestMethod3()
    {
        var db = new Series2TestDb();
        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource.Replace(".db", "");
        db.Database.EnsureCreated();
        db.Database.ExecuteSql($"INSERT INTO Entries(content, notnullstruct) values ('null', '{{}}')");
        db.SaveChanges();
        db.Dispose();
        using var db2 = new Series2TestDb(path);
        Assert.IsNull(db2.Entries.First(i=>i.Id == 1).Content);
End();    }
// Empty string stored in DB default no error and set to default
    [TestMethod]
    public void TestMethod4()
    {
        var db = new Series2TestDb();
        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource.Replace(".db", "");
        db.Database.EnsureCreated();
        db.Database.ExecuteSql($"""INSERT INTO Entries(content, notnullstruct) values ('', '')""");
        db.SaveChanges();
        db.Dispose();
        using var db2 = new Series2TestDb(path);
        // In case proeprty is nullable, therefore property defaults to null. 
        Assert.IsNull(db2.Entries.First(i=>i.Id == 1).Content);
        // In case property is not nullable, therefore property defaults to default value of the type. 
        Assert.AreEqual(db2.Entries.First(i => i.Id == 1).NotNullStruct, new Series2TestDb.NotNullStruct());
End();    }
// Empty string stored in DB with certain option will throw. 
    [TestMethod]
    public void TestMethod5()
    {
        var db = new Series2TestDb();
        var path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource.Replace(".db", "");
        db.Database.EnsureCreated();
        db.Database.ExecuteSql($"""INSERT INTO Entries(content, notnullstruct) values ('', '')""");
        db.SaveChanges();
        db.Dispose();
        var db2 = new Series2TestDb(path, new JsonProperty.JsonProperty.JsonSerializeAttributeOptions()
        {
            SetDefaultWhenNotSet = false
        });
        var ex = Assert.Throws<JsonException>(() =>
        {
            foreach (var se in db2.Entries.First(i=>i.Id == 1).Content.AsEnumerable())
            {
                Console.WriteLine(se);
            }
        });
        Console.WriteLine(ex.Message);
        End();
    }

    static void End()
    {
        foreach (var enumerateFile in Directory.EnumerateFiles(".", "*.db*"))
        {
            File.Delete(enumerateFile);
        }
    }
}

class TestDb(string? name = null) : DbContext
{
    public DbSet<Entry> Entries { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={name ?? AXExpansion.AXHelper.Helpers.StringHelper.RandomString(8)}.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseJsonSerializeAttribute<TestDb>();
    }

    [PrimaryKey(nameof(Id))]
    public class Entry
    {
        public int Id { get; set; }
        [DbJsonSerialize]
        public IEnumerable<string>? Content { get; set; }
        [DbJsonSerialize]
        public Series2TestDb.NotNullStruct? NotNullStruct { get; set; }
    }
}
class Series2TestDb(string? name =null, JsonProperty.JsonProperty.JsonSerializeAttributeOptions? attributeOptions = null) : DbContext
{
    public sealed class NeverCacheModelKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => Guid.NewGuid(); // 每次返回唯一键，缓存永不命中
    }

    public DbSet<Entry> Entries { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={name ?? AXExpansion.AXHelper.Helpers.StringHelper.RandomString(8)}.db").ReplaceService<IModelCacheKeyFactory, NeverCacheModelKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseJsonSerializeAttribute<Series2TestDb>(attributeOptions);
    }

    [PrimaryKey(nameof(Id))]
    public class Entry
    {
        public int Id { get; set; }
        [DbJsonSerialize]
        public IEnumerable<string> Content { get; set; }
        [DbJsonSerialize]
        public NotNullStruct NotNullStruct { get; set; }
        
    }

    public record struct NotNullStruct
    {
        
    }
}

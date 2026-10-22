# JsonPropertyAttribute

Map a complex CLR property to a single `TEXT` column (SQLite, and any other relational provider) with one attribute and one call in `OnModelCreating`.

`[DbJsonSerialize]` does not change your model — it registers a `ValueConverter` that runs `System.Text.Json` on the way in and out, so you keep writing normal C#:

```csharp
public class Entry
{
    public int Id { get; set; }

    [DbJsonSerialize]
    public IEnumerable<string>? Tags { get; set; }
}
```

...and EF Core stores `["a","b"]` in a plain, human-readable column.

## Requirements

| | |
|---|---|
| Target framework | `net11.0` |
| EF Core | 10.x (`Microsoft.EntityFrameworkCore`) |
| Provider | any relational provider (verified against `Microsoft.EntityFrameworkCore.Sqlite`) |

## Install

```bash
dotnet add package JsonPropertyAttribute
```

Until the package is on NuGet.org, reference the project directly:

```xml
<ProjectReference Include="..\JsonProperty\JsonProperty.csproj" />
```

## Quick start

```csharp
using JsonProperty;                 // DbJsonSerializeAttribute, UseJsonSerializeAttribute
using Microsoft.EntityFrameworkCore;

public class BlogDbContext : DbContext
{
    public DbSet<Post> Posts { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite("Data Source=blog.db");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Registers the converters for every [DbJsonSerialize] property of every DbSet<T>.
        modelBuilder.UseJsonSerializeAttribute<BlogDbContext>();
    }

    public class Post
    {
        public int Id { get; set; }

        [DbJsonSerialize]
        public List<string>? Tags { get; set; }

        [DbJsonSerialize]
        public PostMetadata? Metadata { get; set; }
    }

    public record PostMetadata(string Author, DateTimeOffset PublishedAt);
}
```

The generated table has a single `Tags` column:

```sql
CREATE TABLE "Posts" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Posts" PRIMARY KEY AUTOINCREMENT,
    "Tags" TEXT NULL,
    "Metadata" TEXT NULL
);
```

### Custom serializer options

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseJsonSerializeAttribute<BlogDbContext>(
        options: new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
}
```

### Handling empty / NULL column values

`JsonSerializer.Deserialize` throws on `""` and returns `null` for `"null"`. By default the converter is defensive:

| Stored value | `SetDefaultWhenNotSet = true` (default) | `SetDefaultWhenNotSet = false` |
|---|---|---|
| `NULL` | `default(T)` (`null` for reference types) | `JsonException` |
| `''` | `default(T)` | `JsonException` |
| `'null'` | `null` | `null` |
| `'{"a":1}'` | deserialized value | deserialized value |

```csharp
modelBuilder.UseJsonSerializeAttribute<BlogDbContext>(
    new JsonProperty.JsonProperty.JsonSerializeAttributeOptions { SetDefaultWhenNotSet = false });
```

Use `false` when the data is fully controlled by `System.Text.Json` and you want malformed data to fail loudly instead of being silently replaced by defaults.

## API

### `[DbJsonSerialize]`

`AttributeTargets.Property`. Marks a property for JSON round-tripping. Only properties of types registered through a `DbSet<T>` are discovered.

### `modelBuilder.UseJsonSerializeAttribute<TContext>(options, attributeOptions)`

Call inside `OnModelCreating`.

| Parameter | Meaning |
|---|---|
| `TContext` | Your `DbContext` type; all `DbSet<>` properties are scanned |
| `options` | `JsonSerializerOptions` passed to every serialize/deserialize call |
| `attributeOptions` | `JsonSerializeAttributeOptions`, see above |

The method builds the two conversion lambdas as expression trees (`Expression.Lambda`), wraps them in a `ValueConverter<TProperty, string>` via reflection, and calls `Property(...).HasConversion(converter)` for each marked property.

## How it works

1. `OnModelCreating` enumerates the context's `DbSet<>` properties and then each entity type's CLR properties.
2. Every property carrying `[DbJsonSerialize]` gets a `ValueConverter<TProperty, string>` created at runtime:
   - `v => JsonSerializer.Serialize((object)v, typeof(TProperty), options)`
   - `s => string.IsNullOrWhiteSpace(s) ? default(TProperty) : JsonSerializer.Deserialize(s, typeof(TProperty), options)`
3. EF Core applies the converter on write and on materialization, so no changes are needed at the call site.

## Caveats

- **Model caching.** EF Core caches the model per context type and ignores `OnModelCreating` arguments such as `options` / `attributeOptions`. If you build the same context type with different options in one process, supply an `IModelCacheKeyFactory` that reflects those options (the test project uses an ever-changing key).
- **Change tracking of nested mutations.** Converted values are compared after conversion, so mutating a list in place without reassigning the property is not detected. Reassign the property (`post.Tags = post.Tags.Append("c").ToList();`) or use a tracked collection.
- **Querying inside the JSON.** The column is opaque `TEXT` unless your provider supports JSON functions; `Where(e => e.Tags.Contains("x"))` translates only where the provider can map it. On SQLite, add a generated column or query with `EF.Functions.JsonContains` / raw SQL.
- **Schema.** A converter changes the column type only — run a migration, or start from `EnsureCreated`, after adding the attribute.

## Building and testing

```bash
dotnet build JsonPropertyAttribute.slnx -c Release
dotnet test  JsonPropertyAttribute.slnx -c Release
```

The test project covers nullable reference types, `IEnumerable<T>` interface-typed properties, `null` payloads, empty strings and the `SetDefaultWhenNotSet` switch.

## Releasing

Package metadata and a set of MSBuild targets live in the library project (`JsonProperty/JsonProperty.csproj` + `build/JsonProperty.Publish.targets`).

```bash
# Pack into ./artifacts/nuget
dotnet msbuild JsonProperty/JsonProperty.csproj /t:PackRelease

# Pack with an explicit version
dotnet msbuild JsonProperty/JsonProperty.csproj /t:PackRelease -p:Version=1.2.3

# Pack + push to nuget.org (key from env var NUGET_API_KEY or -p:NuGetApiKey=...)
dotnet msbuild JsonProperty/JsonProperty.csproj /t:PushRelease -p:NuGetApiKey=*** 

# Push to a local folder feed instead
dotnet msbuild JsonProperty/JsonProperty.csproj /t:PushRelease -p:NuGetSource=./artifacts/feed -p:NuGetApiKey=dummy
```

| Target | What it does |
|---|---|
| `PackRelease` | Restores and packs in `Release` into `$(NuGetOutputPath)` (default `artifacts/nuget`), symbols + XML docs included |
| `PushRelease` | `PackRelease`, then `dotnet nuget push` with `--skip-duplicate` |
| `Release` | `PackRelease`, then pushes only when `-p:Push=true` is passed |

Overridable properties: `PublishConfiguration`, `NuGetOutputPath`, `NuGetSource`, `NuGetApiKey`, `Version`.

The targets are also hooked so a plain `dotnet pack -c Release` produces the same package.

### CI (GitHub Actions)

[`.github/workflows/publish.yml`](.github/workflows/publish.yml) builds, tests and packs on every release tag, then pushes to NuGet.org.

```text
push tag v1.2.3        -> build + test + pack + push 1.2.3
workflow_dispatch      -> build + test + pack; pushes only when "push" is checked
```

Publishing uses [nuget.org trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the job asks GitHub for an OIDC token, exchanges it for a **short-lived** nuget.org API key (valid one hour, single use), pushes, and forgets it. There is no long-lived package secret to store or rotate.

One-time setup:

1. On nuget.org: **username → Trusted Publishing → Add new policy** with
   - Repository owner: `AXCWG`
   - Repository: `JsonPropertyAttribute`
   - Workflow file: `publish.yml` (file name only, no `.github/workflows/` prefix)
   - Environment: `nuget` (optional; must match the `environment:` in the workflow)
2. In the GitHub repository: add the secret `NUGET_USER` holding your **nuget.org profile name** (not the email address). No API key secret is needed.
3. Optional: approve/limit the `nuget` environment to gate publishing (Settings → Environments).
4. Optional: set the repository variable `PUSH_TO_GITHUB_PACKAGES` to `true` to also publish to `nuget.pkg.github.com`.

Release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The package version comes from the tag (`v1.0.0` → `1.0.0`); `v1.0.0-rc.1` produces a prerelease. Packed `.nupkg` / `.snupkg` are attached to the workflow run as the `nupkg-<version>` artifact. The temporary API key returned by `NuGet/login` is passed to `dotnet nuget push` through an environment variable, so it never shows up in the logs.

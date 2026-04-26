# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet restore
dotnet build                                      # all three projects
dotnet test                                       # full xUnit suite
dotnet test --filter "FullyQualifiedName~BucketHasherTests"   # single class
dotnet test --filter "DisplayName~Bump_triggers_rebuild"      # single test
dotnet run --project samples/RegionBucketing.Sample.Web        # http://localhost:5080
UseRegionBasedGeo=false dotnet run --project samples/RegionBucketing.Sample.Web
```

The README's "Running locally" section has the curl recipes that exercise the cache-sharing, bucket-divergence, and counter-bump behaviours end-to-end.

## Architecture

Three projects, one pattern. The library is the contract; the sample shows wiring; the tests pin invariants that — if broken — silently invalidate every consumer's cache on deploy.

### Target frameworks (deliberate)

| Project | TFM | Why |
|---|---|---|
| `src/RegionBucketing` | `netstandard2.0` | Same DLL serves `net48` MVC/Web API consumers and modern `net6+` apps. Do not bump this. |
| `samples/RegionBucketing.Sample.Web` | `net10.0` | Uses the `OutputCache` middleware's `VaryByValue` API. |
| `tests/RegionBucketing.Tests` | `net10.0` | xUnit. |

### The three pieces of the pattern

1. **`BucketHasher`** — SHA-256 over the canonical (sorted, distinct, comma-joined) region-id list, truncated to 12 chars of Crockford Base32. The canonicalisation rule, the hash width, and the alphabet form the cache-key contract; changing any of them shifts every key on the next deploy.
2. **`RegionIndex`** — in-process country→regions map. `EnsureFresh()` does double-checked locking against `IVersionCounter.Current`; on miss it calls the `Loader` delegate (passed at construction; the library itself has no data-access dependency) and atomically swaps the dictionary references.
3. **`IVersionCounter`** — `InMemoryVersionCounter` for tests/single-process; `RedisVersionCounter` (in the sample) for multi-process. The contract is "cheap `Current`, atomic `Bump`".

### Hard invariants — break these and the cache breaks

The four xUnit tests pin the contract. Before changing any of these, understand that consumers' cache entries become invalid the moment a different bucket is computed for the same input.

1. **Same set → same hash** regardless of order/duplicates. (`BucketHasherTests`)
2. **Different sets → different hashes**. (`RegionIndexTests`)
3. **Counter bump triggers rebuild on next read; no bump → keeps serving stale.** The bump is the contract — that's the *intended* behaviour, not a bug. (`RegionIndexTests`)
4. **Flag-off sentinel `"off"` must never collide with any real bucket hash and must never change.** Its purpose is to make flag-off keys byte-identical to the pre-migration baseline so flipping the flag does not stampede the cache. (`VaryKeySentinelTests`)

### Where the pieces meet (sample's `Program.cs`)

`CacheOutput → VaryByValue` lambda runs once per request, before the cache lookup. It either returns `("geo", "off")` (flag off) or `("geo", index.BucketFor(country))`. The branch on `useRegionBasedGeo` is captured from configuration at startup — keep it that way; reading config inside the lambda would defeat the point.

### Things that look fixable but aren't

- The library has no `Microsoft.Extensions.*`, EF, or Dapper dependencies — that's intentional so it can ship into legacy `net48` codebases without dragging the world in. Add the loader at the composition root, not inside the library.
- `RegionIndex` keeps its own dictionaries and replaces them by reference under a lock. Don't "modernise" this to `ConcurrentDictionary` or `ImmutableDictionary` without re-checking the read-path allocation profile — `BucketFor` is on the request hot path.
- The empty-set bucket hash (returned for unknown countries) is *not* the same string as the flag-off sentinel `"off"`, and must remain different. They serve different purposes: the empty-set hash collapses unknown countries into one cache entry; the sentinel preserves pre-migration keys.

## Workspace conventions

This repo lives under the `melkisetoglu` workspace (see `../CLAUDE.md`). Commit prefixes used across that workspace: `[ai]` AI-generated, `[me]` human-authored decision, `[doc]` docs/notes.

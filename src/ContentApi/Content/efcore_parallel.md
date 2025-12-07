---
title: "My First Blog Post"
slug: "efcore_parallel"
date: 2025-12-06
tags: ["dotnet ", "dbcontext","performance","multithread"]
summary: "DbContext is Not Thread-Safe: Parallelizing EF Core"
readTimeMinutes: 8
---

# DbContext is Not Thread-Safe: Parallelizing EF Core Queries the Right Way

We have all built that endpoint.\
You know the one: the **Executive Dashboard** or the **User Summary**
screen. It's the endpoint that needs to fetch three or four completely
unrelated sets of data to paint a complete picture for the user.

It needs:

-   the last 50 orders\
-   the current system health logs\
-   the user's profile settings\
-   maybe a notification count

So, you write the code the standard way:

``` csharp
var orders = await GetRecentOrdersAsync(userId);
var logs = await GetSystemLogsAsync();
var stats = await GetUserStatsAsync(userId);

return new DashboardDto(orders, logs, stats);
```

This works. It's clean. It's readable.

But there is a **problem**.

If:

-   `GetRecentOrdersAsync` → 300ms\
-   `GetSystemLogsAsync` → 400ms\
-   `GetUserStatsAsync` → 300ms

your users are staring at a loading spinner for **1 second** (300 +
400 + 300).

In distributed systems, **latency kills user experience**. Since these
datasets are unrelated, we *should* be able to run them in parallel.

If we did, the total time would shrink to the slowest query: **400ms** →
a **60% improvement**.

But if you try the naïve approach with Entity Framework Core, your
application will crash.

## The False Promise of `Task.WhenAll`

The most common mistake developers make is wrapping existing repository
calls in tasks and waiting for them all:

``` csharp
// ❌ DO NOT DO THIS
public async Task<DashboardData> GetDashboardData(int userId)
{
    // These methods all use the same injected _dbContext
    var ordersTask = _repository.GetOrdersAsync(userId);
    var logsTask = _repository.GetLogsAsync();
    var statsTask = _repository.GetStatsAsync(userId);

    await Task.WhenAll(ordersTask, logsTask, statsTask); // BOOM 💥

    return new DashboardData(ordersTask.Result, logsTask.Result, statsTask.Result);
}
```

If you run this, you will immediately hit:

> **A second operation started on this context before a previous
> operation completed... DbContext is not thread safe.**

### Why?

`DbContext` is:

-   **not thread safe**\
-   a **stateful** unit-of-work object\
-   designed to use **one database connection at a time**

Database protocols are *not* designed for two concurrent operations over
the same connection.

When multiple tasks attempt to run queries using the **same DbContext**,
EF Core blocks this with an exception to avoid corrupting state.

## The Solution: `IDbContextFactory<T>`

Since .NET 5, EF Core has provided a clean solution:
**IDbContextFactory`<T>`{=html}**.

Instead of injecting a single scoped `DbContext`, you inject a
**factory** that creates lightweight, independent instances of DbContext
on demand.

## Register the Factory

``` csharp
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("db"));
});
```

## Refactoring the Dashboard Endpoint

Inject `IDbContextFactory<AppDbContext>` instead of `AppDbContext`, and
create a new context inside each task.

``` csharp
using Microsoft.EntityFrameworkCore;

public class DashboardService(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task<DashboardDto> GetDashboardAsync(int userId)
    {
        var ordersTask = GetOrdersAsync(userId);
        var logsTask = GetSystemLogsAsync();
        var statsTask = GetUserStatsAsync(userId);

        await Task.WhenAll(ordersTask, logsTask, statsTask);

        return new DashboardDto(
            await ordersTask,
            await logsTask,
            await statsTask
        );
    }

    private async Task<List<Order>> GetOrdersAsync(int userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Amount)
            .Take(50)
            .ToListAsync();
    }

    private async Task<List<SystemLog>> GetSystemLogsAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.SystemLogs
            .AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Take(50)
            .ToListAsync();
    }

    private async Task<UserStats?> GetUserStatsAsync(int userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        return await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserStats { OrderCount = u.Orders.Count })
            .FirstOrDefaultAsync();
    }
}
```

## Key Concepts

### ✔ Isolation

Each task gets **its own DbContext**, meaning:

-   its own database connection\
-   zero contention\
-   true parallel execution

### ✔ Disposal

`await using` ensures the context is disposed immediately.

## Trade-offs & Conclusion

`IDbContextFactory` enables safe parallel execution with EF Core, but
keep these in mind:

### ⚠ Connection Pool Starvation

Parallel queries = multiple connections. High concurrency → possible
exhaustion.

### ⚠ Context Creation Overhead

For very fast queries, the overhead may outweigh gains.

Before jumping to raw SQL or caching layers, look at your awaits.\
If they're lined up sequentially, parallelization may be the cleanest
performance win.

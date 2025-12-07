---
title: "Domain Events"
slug: "domain_events"
date: 2025-12-06
tags: ["dotnet ", "cleanarchitecture"]
summary: "Building a Custom Domain Events Dispatcher in .NET"
readTimeMinutes: 10
---
# Building a Custom Domain Events Dispatcher in .NET

Domain events are a powerful way to decouple your business logic.
Instead of stuffing unrelated responsibilities inside a service method,
you publish events and let other parts of the system react
independently. This keeps your code clean, testable, and open to
extension.

------------------------------------------------------------------------

## Why Domain Events Matter

Consider this tightly coupled example:

``` csharp
public class UserService
{
    public async Task RegisterUser(string email, string password)
    {
        var user = new User(email, password);
        await _userRepository.SaveAsync(user);

        // Directly coupled to email service
        await _emailService.SendWelcomeEmail(user.Email);

        // Directly coupled to analytics
        await _analyticsService.TrackUserRegistration(user.Id);

        // What if we need to add more features?
        // This method will keep growing...
    }
}
```

With domain events, we decouple responsibilities:

``` csharp
public class UserService
{
    public async Task RegisterUser(string email, string password)
    {
        var user = new User(email, password);
        await _userRepository.SaveAsync(user);

        // Publish event - let other parts of the system react
        await _domainEventsDispatcher.DispatchAsync(
            [new UserRegisteredDomainEvent(user.Id, user.Email)]);
    }
}
```

Now, `UserService` focuses only on user registration, while side‑effects
are handled elsewhere.

------------------------------------------------------------------------

## Basic Abstractions

Domain events start with two simple interfaces:

``` csharp
public interface IDomainEvent {}

public interface IDomainEventHandler<in T> where T : IDomainEvent
{
    Task Handle(T domainEvent, CancellationToken cancellationToken = default);
}
```

This design ensures complete decoupling and easy extensibility.

------------------------------------------------------------------------

## Implementing Sample Handlers

### 1. Sending Welcome Emails

``` csharp
internal sealed class SendWelcomeEmailHandler(IEmailService emailService) 
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var welcomeEmail = new WelcomeEmail(domainEvent.Email, domainEvent.UserId);
        await emailService.SendAsync(welcomeEmail, cancellationToken);
    }
}
```

### 2. Tracking Analytics

``` csharp
internal sealed class TrackUserRegistrationHandler(IAnalyticsService analyticsService) 
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public async Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await analyticsService.TrackEvent(
            "user_registered",
            new
            {
                user_id = domainEvent.UserId,
                registration_date = domainEvent.RegisteredAt
            },
            cancellationToken);
    }
}
```

Register handlers manually:

``` csharp
services.AddScoped<IDomainEventHandler<UserRegisteredDomainEvent>, SendWelcomeEmailHandler>();
services.AddScoped<IDomainEventHandler<UserRegisteredDomainEvent>, TrackUserRegistrationHandler>();
```

Or automatically via Scrutor:

``` csharp
services.Scan(scan => scan.FromAssembliesOf(typeof(DependencyInjection))
    .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

------------------------------------------------------------------------

## The Dispatcher (Strongly Typed)

``` csharp
public interface IDomainEventsDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
```

Dispatcher implementation:

``` csharp
internal sealed class DomainEventsDispatcher(IServiceProvider serviceProvider)
    : IDomainEventsDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeDictionary = new();
    private static readonly ConcurrentDictionary<Type, Type> WrapperTypeDictionary = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (IDomainEvent domainEvent in domainEvents)
        {
            using IServiceScope scope = serviceProvider.CreateScope();

            Type domainEventType = domainEvent.GetType();

            Type handlerType = HandlerTypeDictionary.GetOrAdd(
                domainEventType,
                et => typeof(IDomainEventHandler<>).MakeGenericType(et));

            IEnumerable<object?> handlers = scope.ServiceProvider.GetServices(handlerType);

            foreach (object? handler in handlers)
            {
                if (handler is null) continue;

                var handlerWrapper = HandlerWrapper.Create(handler, domainEventType);

                await handlerWrapper.Handle(domainEvent, cancellationToken);
            }
        }
    }

    private abstract class HandlerWrapper
    {
        public abstract Task Handle(IDomainEvent domainEvent, CancellationToken cancellationToken);

        public static HandlerWrapper Create(object handler, Type domainEventType)
        {
            Type wrapperType = WrapperTypeDictionary.GetOrAdd(
                domainEventType,
                et => typeof(HandlerWrapper<>).MakeGenericType(et));

            return (HandlerWrapper)Activator.CreateInstance(wrapperType, handler)!;
        }
    }

    private sealed class HandlerWrapper<T>(object handler) : HandlerWrapper where T : IDomainEvent
    {
        private readonly IDomainEventHandler<T> _handler = (IDomainEventHandler<T>)handler;

        public override async Task Handle(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            await _handler.Handle((T)domainEvent, cancellationToken);
        }
    }
}
```

Register the dispatcher:

``` csharp
services.AddTransient<IDomainEventsDispatcher, DomainEventsDispatcher>();
```

------------------------------------------------------------------------

## Example Usage

``` csharp
public class UserController(IUserService userService, IDomainEventsDispatcher domainEventsDispatcher)
    : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterUserRequest request)
    {
        var user = await userService.CreateUserAsync(request.Email, request.Password);

        var userRegisteredEvent = new UserRegisteredDomainEvent(user.Id, user.Email);

        await domainEventsDispatcher.DispatchAsync([userRegisteredEvent]);

        return Ok(new { UserId = user.Id, Message = "User registered successfully" });
    }
}
```

------------------------------------------------------------------------

## Limitations and Tradeoffs

This implementation runs in‑process:

-   **Immediate failure feedback**\
    Exceptions bubble up instantly.
-   **No persistence or retry**\
    If the process crashes, events are lost.
-   **No eventual consistency**\
    Handlers run in the same request.

For reliability, consider the **Outbox pattern** where events are stored
with your business data and processed later.

------------------------------------------------------------------------

## Wrapping Up

Domain events are a great tool for decoupling business logic without
adding unnecessary complexity. The dispatcher above is lightweight,
transparent, and extensible --- perfect for most applications that don't
need full-blown messaging infrastructure.

Start simple. Evolve as complexity grows. Understand your trade‑offs.

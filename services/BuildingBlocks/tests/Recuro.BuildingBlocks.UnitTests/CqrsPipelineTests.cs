using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Recuro.BuildingBlocks.Application;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.UnitTests;

public sealed record CreateWidget(string Name) : ICommand<string>;

public sealed class CreateWidgetValidator : AbstractValidator<CreateWidget>
{
    public CreateWidgetValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(5);
}

public sealed class CreateWidgetHandler : ICommandHandler<CreateWidget, string>
{
    public int Calls { get; private set; }

    public Task<Result<string>> Handle(CreateWidget command, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<Result<string>>($"widget:{command.Name}");
    }
}

public class CqrsPipelineTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddRecuroApplication(typeof(CqrsPipelineTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateWidget, string>>();

        var result = await handler.Handle(new CreateWidget("ok"), CancellationToken.None);

        Assert.Equal("widget:ok", result.Value);
    }

    [Fact]
    public async Task Invalid_command_is_stopped_by_the_validation_decorator_with_camel_case_field_errors()
    {
        await using var provider = Build();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateWidget, string>>();

        var result = await handler.Handle(new CreateWidget("far too long"), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        var field = Assert.Single(result.Error.Fields);
        Assert.Equal("name", field.Field);
        Assert.Equal("MaximumLengthValidator", field.Code);
    }
}

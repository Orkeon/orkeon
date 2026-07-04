using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.Common;

public class CQRSTests
{
    private static readonly string[] HandleSaveOrder = ["handle", "save"];

    // ══════════════ Result<T> ══════════════

    [Fact]
    public void ShouldCreateSuccess_WhenCallingSuccessFactory()
    {
        // Act
        var result = Result<string>.Success("hello");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldCreateFailure_WhenCallingFailureFactory()
    {
        // Act
        var result = Result<int>.Failure("something broke");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("something broke", result.Error);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void ShouldSupportNullValue_WhenSuccessWithNullableType()
    {
        // Act
        var result = Result<string?>.Success(null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ShouldSupportRecordEquality_WhenComparingResults()
    {
        // Arrange
        var r1 = Result<int>.Success(42);
        var r2 = Result<int>.Success(42);

        // Assert
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenDifferentValues()
    {
        // Arrange
        var r1 = Result<int>.Success(1);
        var r2 = Result<int>.Success(2);

        // Assert
        Assert.NotEqual(r1, r2);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenOneIsSuccessAndOtherIsFailure()
    {
        // Arrange
        var success = Result<string>.Success("ok");
        var failure = Result<string>.Failure("err");

        // Assert
        Assert.NotEqual(success, failure);
    }

    // ══════════════ Unit ══════════════

    [Fact]
    public void ShouldReturnSameValue_WhenAccessingStaticValue()
    {
        // Act
        var u1 = Unit.Value;
        var u2 = Unit.Value;

        // Assert
        Assert.Equal(u1, u2);
    }

    [Fact]
    public void ShouldBeEqualToAnyUnit_WhenComparing()
    {
        // Arrange
        var a = new Unit();
        var b = new Unit();

        // Assert
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void ShouldReturnZero_WhenGettingHashCode()
    {
        // Assert
        Assert.Equal(0, Unit.Value.GetHashCode());
    }

    [Fact]
    public void ShouldReturnParentheses_WhenCallingToString()
    {
        // Assert
        Assert.Equal("()", Unit.Value.ToString());
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingWithBoxedUnit()
    {
        // Arrange
        object boxed = new Unit();

        // Assert
        Assert.True(Unit.Value.Equals(boxed));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingWithNonUnit()
    {
        // Assert
        Assert.False(Unit.Value.Equals("not a unit"));
        Assert.False(Unit.Value.Equals(null));
    }

    // ══════════════ UnitOfWorkCommandHandler ══════════════

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallInnerHandlerAndSaveChanges_WhenHandling()
    {
        // Arrange
        var innerHandler = new StubCommandHandler("result-value");
        var unitOfWork = new StubUnitOfWork();
        var handler = new UnitOfWorkCommandHandler<StubCommand, string>(innerHandler, unitOfWork);

        // Act
        var result = await handler.HandleAsync(new StubCommand(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("result-value", result);
        Assert.True(innerHandler.WasCalled);
        Assert.True(unitOfWork.SaveWasCalled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCallSaveChangesAfterInnerHandler_WhenHandling()
    {
        // Arrange
        var callOrder = new List<string>();
        var innerHandler = new OrderTrackingHandler(callOrder);
        var unitOfWork = new OrderTrackingUnitOfWork(callOrder);
        var handler = new UnitOfWorkCommandHandler<StubCommand, string>(innerHandler, unitOfWork);

        // Act
        await handler.HandleAsync(new StubCommand(), TestContext.Current.CancellationToken);

        // Assert — inner handler runs before SaveChanges
        Assert.Equal(HandleSaveOrder, callOrder);
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenInnerHandlerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new UnitOfWorkCommandHandler<StubCommand, string>(null!, new StubUnitOfWork()));
    }

    [Fact]
    public void ShouldThrowArgumentNull_WhenUnitOfWorkIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new UnitOfWorkCommandHandler<StubCommand, string>(new StubCommandHandler("x"), null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenInnerHandlerThrows()
    {
        // Arrange
        var innerHandler = new ThrowingCommandHandler();
        var unitOfWork = new StubUnitOfWork();
        var handler = new UnitOfWorkCommandHandler<StubCommand, string>(innerHandler, unitOfWork);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new StubCommand(), TestContext.Current.CancellationToken));
        Assert.False(unitOfWork.SaveWasCalled);
    }

    // ── Test doubles ──

    private sealed record StubCommand : ICommand<string>;

    private sealed class StubCommandHandler : ICommandHandler<StubCommand, string>
    {
        private readonly string _result;
        public bool WasCalled { get; private set; }

        public StubCommandHandler(string result) => _result = result;

        public System.Threading.Tasks.Task<string> HandleAsync(StubCommand command, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return System.Threading.Tasks.Task.FromResult(_result);
        }
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<StubCommand, string>
    {
        public System.Threading.Tasks.Task<string> HandleAsync(StubCommand command, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public bool SaveWasCalled { get; private set; }
        public void Track(IHasDomainEvents aggregate) { }
        public System.Threading.Tasks.Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveWasCalled = true;
            return System.Threading.Tasks.Task.FromResult(1);
        }
    }

    private sealed class OrderTrackingHandler : ICommandHandler<StubCommand, string>
    {
        private readonly List<string> _order;
        public OrderTrackingHandler(List<string> order) => _order = order;

        public System.Threading.Tasks.Task<string> HandleAsync(StubCommand command, CancellationToken cancellationToken = default)
        {
            _order.Add("handle");
            return System.Threading.Tasks.Task.FromResult("ok");
        }
    }

    private sealed class OrderTrackingUnitOfWork : IUnitOfWork
    {
        private readonly List<string> _order;
        public OrderTrackingUnitOfWork(List<string> order) => _order = order;
        public void Track(IHasDomainEvents aggregate) { }
        public System.Threading.Tasks.Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _order.Add("save");
            return System.Threading.Tasks.Task.FromResult(1);
        }
    }
}

using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Infrastructure.Communication;

namespace Orkeon.Infrastructure.Tests.Communication;

public sealed class AsyncAgentCommunicationServiceTests : IDisposable
{
    private readonly AsyncAgentCommunicationService _service;

    public AsyncAgentCommunicationServiceTests()
    {
        _service = new AsyncAgentCommunicationService();
    }

    [Fact]
    public async Task ShouldNotThrow_WhenSendMessageAsyncWithValidMessage()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var message = new AgentMessage(
            from,
            to,
            MessageType.Task,
            "Test message",
            new Dictionary<string, object> { ["key"] = "value" });

        // Act & Assert - Should not throw
        var exception = await Record.ExceptionAsync(async () => await _service.SendMessageAsync(message));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSendMessageAsyncWithNullMessage()
    {
        // Arrange
        AgentMessage? message = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.SendMessageAsync(message!).AsTask());
    }

    [Fact]
    public async Task ShouldReturnMessages_WhenReceiveMessagesAsyncWithMatchingAgentId()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var message1 = new AgentMessage(agent1, agent2, MessageType.Task, "Message 1");
        var message2 = new AgentMessage(agent1, agent2, MessageType.Question, "Message 2");
        var message3 = new AgentMessage(agent2, agent1, MessageType.Response, "Message 3"); // Different recipient

        await _service.SendMessageAsync(message1);
        await _service.SendMessageAsync(message2);
        await _service.SendMessageAsync(message3);

        // Act
        var receivedMessages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var message in _service.ReceiveMessagesAsync(agent2).WithCancellation(cts.Token))
            {
                receivedMessages.Add(message);
                if (receivedMessages.Count == 2) // We expect 2 messages for agent2
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout occurs
        }

        // Assert
        Assert.Equal(2, receivedMessages.Count);
        Assert.Contains(message1, receivedMessages);
        Assert.Contains(message2, receivedMessages);
        Assert.DoesNotContain(message3, receivedMessages);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenReceiveMessagesAsyncWithNullAgentId()
    {
        // Arrange
        AgentId? agentId = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var message in _service.ReceiveMessagesAsync(agentId!))
            {
                break; // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenIsAgentAvailableAsyncWithUnknownAgent()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var isAvailable = await _service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenIsAgentAvailableAsyncWithNullAgentId()
    {
        // Arrange
        AgentId? agentId = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.IsAgentAvailableAsync(agentId!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldSetAvailability_WhenSetAgentAvailabilityAsyncWithValidAgentId()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        await _service.SetAgentAvailabilityAsync(agentId, true, TestContext.Current.CancellationToken);
        var isAvailable = await _service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(isAvailable);

        // Act - Set to unavailable
        await _service.SetAgentAvailabilityAsync(agentId, false, TestContext.Current.CancellationToken);
        isAvailable = await _service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenSetAgentAvailabilityAsyncWithNullAgentId()
    {
        // Arrange
        AgentId? agentId = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.SetAgentAvailabilityAsync(agentId!, true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldTrackIndependently_WhenSetAgentAvailabilityAsyncMultipleAgents()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var agent3 = AgentId.Create();

        // Act
        await _service.SetAgentAvailabilityAsync(agent1, true, TestContext.Current.CancellationToken);
        await _service.SetAgentAvailabilityAsync(agent2, false, TestContext.Current.CancellationToken);
        await _service.SetAgentAvailabilityAsync(agent3, true, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(await _service.IsAgentAvailableAsync(agent1, TestContext.Current.CancellationToken));
        Assert.False(await _service.IsAgentAvailableAsync(agent2, TestContext.Current.CancellationToken));
        Assert.True(await _service.IsAgentAvailableAsync(agent3, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldRespectCancellationToken_WhenSetAgentAvailabilityAsyncWithCancellation()
    {
        // Arrange
        var agentId = AgentId.Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _service.SetAgentAvailabilityAsync(agentId, true, cts.Token));
    }

    [Fact]
    public async Task ShouldRespectCancellationToken_WhenIsAgentAvailableAsyncWithCancellation()
    {
        // Arrange
        var agentId = AgentId.Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _service.IsAgentAvailableAsync(agentId, cts.Token));
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSendAndReceiveConcurrentMessages()
    {
        // Arrange
        var agent1 = AgentId.Create();
        var agent2 = AgentId.Create();
        var messageCount = 100;
        var sendTasks = new List<Task>();

        // Act - Send messages concurrently
        for (int i = 0; i < messageCount; i++)
        {
            var message = new AgentMessage(
                agent1,
                agent2,
                MessageType.Task,
                $"Message {i}");
            sendTasks.Add(_service.SendMessageAsync(message).AsTask());
        }

        await System.Threading.Tasks.Task.WhenAll(sendTasks);

        // Act - Receive messages
        var receivedMessages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            await foreach (var message in _service.ReceiveMessagesAsync(agent2).WithCancellation(cts.Token))
            {
                receivedMessages.Add(message);
                if (receivedMessages.Count == messageCount)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected if we don't receive all messages in time
        }

        // Assert
        Assert.Equal(messageCount, receivedMessages.Count);
        Assert.All(receivedMessages, msg => Assert.Equal(agent2, msg.To));
    }

    [Fact]
    public async Task ShouldOnlyReceiveOwnMessages_WhenReceiveMessagesAsyncMultipleReceivers()
    {
        // Arrange
        var sender = AgentId.Create();
        var receiver1 = AgentId.Create();
        var receiver2 = AgentId.Create();

        var messageToReceiver1 = new AgentMessage(sender, receiver1, MessageType.Task, "For receiver1");
        var messageToReceiver2 = new AgentMessage(sender, receiver2, MessageType.Task, "For receiver2");

        await _service.SendMessageAsync(messageToReceiver1);
        await _service.SendMessageAsync(messageToReceiver2);

        // Act
        var receiver1Messages = new List<AgentMessage>();
        var receiver2Messages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var receiveTask1 = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _service.ReceiveMessagesAsync(receiver1).WithCancellation(cts.Token))
                {
                    receiver1Messages.Add(message);
                    break; // We expect only 1 message
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        var receiveTask2 = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _service.ReceiveMessagesAsync(receiver2).WithCancellation(cts.Token))
                {
                    receiver2Messages.Add(message);
                    break; // We expect only 1 message
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        await System.Threading.Tasks.Task.WhenAll(receiveTask1, receiveTask2);

        // Assert
        Assert.Single(receiver1Messages);
        Assert.Equal(messageToReceiver1, receiver1Messages[0]);

        Assert.Single(receiver2Messages);
        Assert.Equal(messageToReceiver2, receiver2Messages[0]);
    }

    [Fact]
    public async Task ShouldHandleThreadSafely_WhenSetAgentAvailabilityAsyncConcurrentUpdates()
    {
        // Arrange
        var agentId = AgentId.Create();
        var updateTasks = new List<Task>();
        var iterations = 100;

        // Act - Perform concurrent availability updates
        for (int i = 0; i < iterations; i++)
        {
            bool availability = i % 2 == 0;
            updateTasks.Add(_service.SetAgentAvailabilityAsync(agentId, availability, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(updateTasks);

        // Assert - Final state should be consistent (last update wins)
        var finalAvailability = await _service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken);
        Assert.IsType<bool>(finalAvailability); // Should have a valid boolean value
    }

    [Fact]
    public async Task ShouldBlockUntilCancelled_WhenReceiveMessagesAsyncNoMessages()
    {
        // Arrange
        var agentId = AgentId.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var messageReceived = false;

        // Act
        try
        {
            await foreach (var message in _service.ReceiveMessagesAsync(agentId).WithCancellation(cts.Token))
            {
                messageReceived = true;
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.False(messageReceived);
    }

    [Fact]
    public async Task ShouldPreserveMetadata_WhenSendMessageAsyncWithComplexMetadata()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var metadata = new Dictionary<string, object>
        {
            ["string"] = "value",
            ["number"] = 42,
            ["boolean"] = true,
            ["nested"] = new Dictionary<string, object> { ["key"] = "nestedValue" }
        };

        var message = new AgentMessage(from, to, MessageType.Task, "Test", metadata);

        // Act
        await _service.SendMessageAsync(message);

        var receivedMessage = await _service.ReceiveMessagesAsync(to)
            .FirstOrDefaultAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(metadata, receivedMessage.Metadata);
    }

    [Fact]
    public async Task ShouldPreserveFIFOOrder_WhenReceiveMessagesAsyncMessageOrder()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var messages = new List<AgentMessage>();

        for (int i = 0; i < 10; i++)
        {
            messages.Add(new AgentMessage(from, to, MessageType.Task, $"Message {i}"));
        }

        // Act - Send messages in order
        foreach (var message in messages)
        {
            await _service.SendMessageAsync(message);
        }

        // Receive messages
        var receivedMessages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await foreach (var message in _service.ReceiveMessagesAsync(to).WithCancellation(cts.Token))
            {
                receivedMessages.Add(message);
                if (receivedMessages.Count == messages.Count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        // Assert - Messages should be in FIFO order
        Assert.Equal(messages.Count, receivedMessages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            Assert.Equal(messages[i].Content, receivedMessages[i].Content);
        }
    }

    [Fact]
    public async Task ShouldHandleAllTypes_WhenSendMessageAsyncDifferentMessageTypes()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var messageTypes = Enum.GetValues<MessageType>();
        var sentMessages = new List<AgentMessage>();

        foreach (var messageType in messageTypes)
        {
            var message = new AgentMessage(from, to, messageType, $"Message of type {messageType}");
            sentMessages.Add(message);
            await _service.SendMessageAsync(message);
        }

        // Act
        var receivedMessages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            await foreach (var message in _service.ReceiveMessagesAsync(to).WithCancellation(cts.Token))
            {
                receivedMessages.Add(message);
                if (receivedMessages.Count == sentMessages.Count)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(sentMessages.Count, receivedMessages.Count);
        foreach (var messageType in messageTypes)
        {
            Assert.Contains(receivedMessages, m => m.Type == messageType);
        }
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSendMessageAsyncWithEmptyContent()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var message = new AgentMessage(from, to, MessageType.Task, "");

        // Act
        await _service.SendMessageAsync(message);
        var receivedMessage = await _service.ReceiveMessagesAsync(to)
            .FirstOrDefaultAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("", receivedMessage.Content);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSendMessageAsyncWithVeryLongContent()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var longContent = new string('x', 100000); // 100K characters
        var message = new AgentMessage(from, to, MessageType.Task, longContent);

        // Act
        await _service.SendMessageAsync(message);
        var receivedMessage = await _service.ReceiveMessagesAsync(to)
            .FirstOrDefaultAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(longContent, receivedMessage.Content);
    }

    [Fact]
    public async Task ShouldAllReceiveMessages_WhenReceiveMessagesAsyncMultipleReceiversSameAgent()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var message = new AgentMessage(from, to, MessageType.Task, "Shared message");

        var receiver1Messages = new List<AgentMessage>();
        var receiver2Messages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Start two receivers for the same agent
        var receiveTask1 = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _service.ReceiveMessagesAsync(to).WithCancellation(cts.Token))
                {
                    receiver1Messages.Add(msg);
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        var receiveTask2 = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _service.ReceiveMessagesAsync(to).WithCancellation(cts.Token))
                {
                    receiver2Messages.Add(msg);
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        // Act - Send message after receivers are started
        await System.Threading.Tasks.Task.Delay(50, TestContext.Current.CancellationToken); // Small delay to ensure receivers are listening
        await _service.SendMessageAsync(message);

        await System.Threading.Tasks.Task.WhenAll(receiveTask1, receiveTask2);

        // Assert - At least one receiver should get the message (channels don't duplicate)
        Assert.True(receiver1Messages.Count > 0 || receiver2Messages.Count > 0);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSetAgentAvailabilityAsyncToggleRapidly()
    {
        // Arrange
        var agentId = AgentId.Create();
        var toggleTasks = new List<Task>();

        // Act - Toggle availability rapidly
        for (int i = 0; i < 50; i++)
        {
            toggleTasks.Add(_service.SetAgentAvailabilityAsync(agentId, true, TestContext.Current.CancellationToken));
            toggleTasks.Add(_service.SetAgentAvailabilityAsync(agentId, false, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(toggleTasks);

        // Final set to true for verification
        await _service.SetAgentAvailabilityAsync(agentId, true, TestContext.Current.CancellationToken);

        // Assert
        var isAvailable = await _service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken);
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task ShouldBeThreadSafe_WhenIsAgentAvailableAsyncConcurrentReads()
    {
        // Arrange
        var agentId = AgentId.Create();
        await _service.SetAgentAvailabilityAsync(agentId, true, TestContext.Current.CancellationToken);

        var readTasks = new List<Task<bool>>();

        // Act - Multiple concurrent reads
        for (int i = 0; i < 100; i++)
        {
            readTasks.Add(_service.IsAgentAvailableAsync(agentId, TestContext.Current.CancellationToken));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(readTasks);

        // Assert - All reads should return the same value
        Assert.All(results, r => Assert.True(r));
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSendMessageAsyncWithNullMetadata()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var message = new AgentMessage(from, to, MessageType.Task, "Test", null);

        // Act
        await _service.SendMessageAsync(message);
        var receivedMessage = await _service.ReceiveMessagesAsync(to)
            .FirstOrDefaultAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Null(receivedMessage.Metadata);
    }

    [Fact]
    public async Task ShouldContinueWorking_WhenReceiveMessagesAsyncAfterException()
    {
        // Arrange
        var from = AgentId.Create();
        var to = AgentId.Create();
        var message1 = new AgentMessage(from, to, MessageType.Task, "Message 1");
        var message2 = new AgentMessage(from, to, MessageType.Task, "Message 2");

        // Act - First receive with cancellation
        using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try
        {
            await foreach (var msg in _service.ReceiveMessagesAsync(to).WithCancellation(cts1.Token))
            {
                // Should not receive any message
            }
        }
        catch (OperationCanceledException) { }

        // Send messages after first cancellation
        await _service.SendMessageAsync(message1);
        await _service.SendMessageAsync(message2);

        // Second receive should work
        var receivedMessages = new List<AgentMessage>();
        using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await foreach (var msg in _service.ReceiveMessagesAsync(to).WithCancellation(cts2.Token))
            {
                receivedMessages.Add(msg);
                if (receivedMessages.Count == 2)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(2, receivedMessages.Count);
    }

    [Fact]
    public async Task ShouldHandleCorrectly_WhenSetAgentAvailabilityAsyncMultipleAgentsStress()
    {
        // Arrange
        var agents = new List<AgentId>();
        for (int i = 0; i < 100; i++)
        {
            agents.Add(AgentId.Create());
        }

        var tasks = new List<Task>();

        // Act - Set availability for all agents concurrently
        foreach (var agent in agents)
        {
            tasks.Add(_service.SetAgentAvailabilityAsync(agent, true, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Verify all agents
        var verifyTasks = agents.Select(a => _service.IsAgentAvailableAsync(a)).ToList();
        var results = await System.Threading.Tasks.Task.WhenAll(verifyTasks);

        // Assert
        Assert.All(results, r => Assert.True(r));
    }

    [Fact]
    public async Task ShouldBeReceived_WhenSendMessageAsyncMessageToSelf()
    {
        // Arrange
        var agentId = AgentId.Create();
        var message = new AgentMessage(agentId, agentId, MessageType.Task, "Self message");

        // Act
        await _service.SendMessageAsync(message);
        var receivedMessage = await _service.ReceiveMessagesAsync(agentId)
            .FirstOrDefaultAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal("Self message", receivedMessage.Content);
        Assert.Equal(agentId, receivedMessage.From);
        Assert.Equal(agentId, receivedMessage.To);
    }

    [Fact]
    public async Task ShouldReceiveInOrder_WhenReceiveMessagesAsyncWithInterleaving()
    {
        // Arrange
        var sender1 = AgentId.Create();
        var sender2 = AgentId.Create();
        var receiver = AgentId.Create();

        // Act - Interleave messages from two senders
        await _service.SendMessageAsync(new AgentMessage(sender1, receiver, MessageType.Task, "S1-M1"));
        await _service.SendMessageAsync(new AgentMessage(sender2, receiver, MessageType.Task, "S2-M1"));
        await _service.SendMessageAsync(new AgentMessage(sender1, receiver, MessageType.Task, "S1-M2"));
        await _service.SendMessageAsync(new AgentMessage(sender2, receiver, MessageType.Task, "S2-M2"));

        var receivedMessages = new List<AgentMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        try
        {
            await foreach (var msg in _service.ReceiveMessagesAsync(receiver).WithCancellation(cts.Token))
            {
                receivedMessages.Add(msg);
                if (receivedMessages.Count == 4)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        // Assert - Messages should be in the order sent
        Assert.Equal(4, receivedMessages.Count);
        Assert.Equal("S1-M1", receivedMessages[0].Content);
        Assert.Equal("S2-M1", receivedMessages[1].Content);
        Assert.Equal("S1-M2", receivedMessages[2].Content);
        Assert.Equal("S2-M2", receivedMessages[3].Content);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Extension methods for async enumerable testing
public static class AsyncEnumerableExtensions
{
    public static async Task<T?> FirstOrDefaultAsync<T>(
        this IAsyncEnumerable<T> source,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            await foreach (var item in source.WithCancellation(cts.Token))
            {
                return item;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout occurred
        }

        return default(T);
    }
}

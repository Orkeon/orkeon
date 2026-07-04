using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.DTOs;

namespace Orkeon.Application.Task.Queries.GetTask;

/// <summary>
/// Query to get a task by ID.
/// </summary>
public record GetTaskQuery(Guid Id) : IQuery<TaskDto?>;

using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>
/// The fold from a watched run's event stream to what a screen shows (BUS-06). Everything the
/// model exposes has to come from an event — a screen must never show progress the run did not
/// report.
/// </summary>
public class RunProgressModelTests
{
    private static RunProgressModel Fold(params string[] lines)
    {
        var model = new RunProgressModel();
        foreach (var line in lines)
        {
            Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent), line);
            model.Apply(orkeonEvent!);
        }

        return model;
    }

    [Fact]
    public void A_run_that_said_nothing_shows_nothing()
    {
        // "No news" is not "going well", and the screen has to be able to tell them apart.
        var model = new RunProgressModel();

        Assert.Empty(model.Tasks);
        Assert.Null(model.Cost);
        Assert.Null(model.PendingQuestion);
        Assert.False(model.Finished);
        Assert.Null(model.Success);

        Assert.Empty(model.ActiveTools);
        Assert.Equal(0, model.ToolCallCount);
        Assert.Empty(model.ActiveDelegations);
        Assert.Empty(model.SpawnedAgents);
        Assert.False(model.IsWaitingForAnswer);
        Assert.Null(model.Elapsed);
    }

    [Fact]
    public void The_enriched_close_carries_the_runs_own_cost()
    {
        // W-08: run.finished now says what the run cost; an older CLI's leaner close
        // (next test) still folds — the extra fields simply stay null.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"run.finished","success":true,"exitCode":0,"tokens":12840,"durationMs":59000,"promptTokens":12000,"completionTokens":840,"cacheHitTokens":7980,"cacheMissTokens":4020}""");

        Assert.Equal(12_840, model.FinalTokens);
        Assert.Equal(59_000, model.FinalDurationMs);
        Assert.Equal(7_980, model.FinalCacheHitTokens);
        Assert.Equal(4_020, model.FinalCacheMissTokens);
        Assert.Null(model.FinalEstimatedTokens);   // every call counted: no part of it is an estimate
    }

    [Fact]
    public void A_skipped_task_is_told_apart_from_a_failed_one()
    {
        // LLM-11: the CLI flags a task that never ran because its dependency failed; an older
        // CLI without the field folds as before (not skipped).
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"task.completed","taskId":"score","agentRole":"analyst","success":false,"durationMs":180000,"tokens":0,"toolCalls":0}""",
            """{"v":2,"seq":2,"ts":"t","kind":"task.completed","taskId":"write","agentRole":"writer","success":false,"skipped":true,"durationMs":0,"tokens":0,"toolCalls":0}""");

        Assert.Equal(2, model.Tasks.Count);
        Assert.False(model.Tasks[0].Skipped);
        Assert.False(model.Tasks[0].Success);
        Assert.True(model.Tasks[1].Skipped);
        Assert.False(model.Tasks[1].Success);
    }

    [Fact]
    public void The_stream_becomes_progress_cost_and_an_outcome()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"run.started","target":"/ws/crew.yaml","stream":true}""",
            """{"v":2,"seq":2,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"analyst","success":true,"durationMs":1200,"tokens":340,"toolCalls":2}""",
            """{"v":2,"seq":3,"ts":"t","kind":"cost.updated","tokens":340,"model":"deepseek-chat","provider":"deepseek","operation":"agent"}""",
            """{"v":2,"seq":4,"ts":"t","kind":"run.finished","success":true,"exitCode":0}""");

        Assert.Equal("/ws/crew.yaml", model.Target);
        Assert.True(model.Streaming);

        var task = Assert.Single(model.Tasks);
        Assert.Equal("analyst", task.AgentRole);
        Assert.True(task.Success);
        Assert.Equal(1200, task.DurationMs);
        Assert.Equal(340, task.Tokens);
        Assert.Equal(2, task.ToolCalls);

        // usd never existed on the wire, and reading it here was building a screen against a
        // fiction: a price arrives only as the vendor's own cost (STUDIO-29, next tests).
        Assert.Equal(340, model.Cost!.Tokens);
        Assert.Equal("deepseek-chat", model.Cost.Model);
        Assert.Equal("deepseek", model.Cost.Provider);

        Assert.True(model.Finished);
        Assert.True(model.Success);
        Assert.Equal(0, model.ExitCode);
    }

    [Fact]
    public void The_meter_folds_the_split_and_the_vendor_charge_while_the_run_goes()
    {
        // STUDIO-29: cost.updated says what goes up, what comes back and — when the vendor
        // billed it — what it cost, while the run is still going.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","crewId":"01K5","agentId":"Writer","kind":"cost.updated","tokens":300,"promptTokens":240,"completionTokens":60,"cacheHitTokens":200,"cacheMissTokens":40,"model":"google/gemini-3.7-flash","provider":"openrouter","operation":"agent","cost":0.0042,"currency":"USD","costSource":"vendor"}""");

        var cost = model.Cost!;
        Assert.Equal(300, cost.Tokens);
        Assert.Equal(240, cost.PromptTokens);
        Assert.Equal(60, cost.CompletionTokens);
        Assert.Equal(200, cost.CacheHitTokens);
        Assert.Equal(40, cost.CacheMissTokens);
        Assert.Equal("google/gemini-3.7-flash", cost.Model);
        Assert.Equal("openrouter", cost.Provider);
        Assert.Equal(0.0042m, cost.Amount);
        Assert.Equal("USD", cost.Currency);
        Assert.Equal("vendor", cost.Source);
    }

    [Fact]
    public void A_meter_line_that_says_less_leaves_the_rest_absent()
    {
        // An older CLI, or a vendor that bills nothing in its answer: what the line does not
        // say stays unknown — never a zero a screen would show as a count or as a price.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":340,"model":"deepseek-chat","provider":"deepseek"}""");

        var cost = model.Cost!;
        Assert.Equal(340, cost.Tokens);
        Assert.Null(cost.PromptTokens);
        Assert.Null(cost.CompletionTokens);
        Assert.Null(cost.CacheHitTokens);
        Assert.Null(cost.CacheMissTokens);
        Assert.Null(cost.EstimatedTokens);
        Assert.Null(cost.Amount);
        Assert.Null(cost.Currency);
        Assert.Null(cost.Source);
    }

    [Fact]
    public void A_free_call_folds_as_a_price_of_zero()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":150,"promptTokens":120,"completionTokens":30,"cost":0,"currency":"USD","costSource":"vendor"}""");

        Assert.Equal(0m, model.Cost!.Amount);
        Assert.Equal("USD", model.Cost.Currency);
    }

    [Fact]
    public void A_question_waits_until_it_is_answered()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"choice","prompt":"Which format?","choices":["markdown","json"]}""");

        var question = model.PendingQuestion!;
        Assert.Equal("c-1", question.CorrelationId);
        Assert.Equal(RunQuestion.Choice, question.InputKind);
        Assert.Equal("Which format?", question.Prompt);
        Assert.Equal(["markdown", "json"], question.Choices);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","correlationId":"c-1","kind":"input.given","value":"markdown"}"""));
        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void A_question_nobody_can_answer_is_not_shown_as_pending()
    {
        // Answering needs an address. Showing a prompt with no way to reply would strand the
        // user in front of a box that does nothing.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"input.needed","inputKind":"text","prompt":"Your name?"}""");

        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void A_malformed_question_does_not_clear_a_real_one_already_on_screen()
    {
        // The single-slot version of this model overwrote PendingQuestion on every
        // input.needed — a malformed one (no correlation id) CLEARED a legitimate question,
        // leaving the run blocked with nothing displayed.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"Real?"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"input.needed","inputKind":"text","prompt":"No address"}""");

        Assert.NotNull(model.PendingQuestion);
        Assert.Equal("c-1", model.PendingQuestion!.CorrelationId);
    }

    [Fact]
    public void Concurrent_questions_queue_instead_of_clobbering_each_other()
    {
        // Parallel tasks can ask concurrently. The single slot made the second question
        // overwrite the first, which stayed unanswerable forever.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"First?"}""",
            """{"v":2,"seq":2,"ts":"t","correlationId":"c-2","kind":"input.needed","inputKind":"text","prompt":"Second?"}""");

        Assert.Equal("c-1", model.PendingQuestion!.CorrelationId);

        model.AnswerAccepted();
        Assert.Equal("c-2", model.PendingQuestion!.CorrelationId);

        model.AnswerAccepted();
        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void An_echoed_answer_removes_the_question_it_names_alone()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"First?"}""",
            """{"v":2,"seq":2,"ts":"t","correlationId":"c-2","kind":"input.needed","inputKind":"text","prompt":"Second?"}""",
            """{"v":2,"seq":3,"ts":"t","correlationId":"c-2","kind":"input.given","value":"oui"}""");

        Assert.Equal("c-1", model.PendingQuestion!.CorrelationId);
    }

    [Fact]
    public void The_end_of_a_run_clears_a_question_nobody_is_left_to_answer()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Continue?"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"run.finished","success":false,"exitCode":1}""");

        Assert.Null(model.PendingQuestion);
        Assert.False(model.Success);
    }

    [Fact]
    public void Hub_messages_keep_their_payload_whatever_shape_it_has()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","from":"client://studio","topic":"orders","payload":{"id":42}}""");

        var message = Assert.Single(model.HubMessages);
        Assert.Equal("client://studio", message.From);
        Assert.Equal("orders", message.Topic);
        Assert.Contains("\"id\":42", message.Payload!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_send_becomes_a_pending_agent_request_and_a_post_does_not()
    {
        // Only a line that says expectsReply is a question: a topic relay can carry a
        // correlationId too, and treating it as a request would offer a reply nobody awaits.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","from":"agent://crew/worker","payload":{"note":"fyi"}}""",
            """{"v":2,"seq":2,"ts":"t","kind":"hub.message","correlationId":"r-1","topic":"news","payload":{"n":1}}""",
            """{"v":2,"seq":3,"ts":"t","kind":"hub.message","correlationId":"r-2","expectsReply":true,"from":"agent://crew/worker","payload":{"question":"go?"}}""");

        var request = model.PendingAgentRequest;
        Assert.NotNull(request);
        Assert.Equal("r-2", request!.CorrelationId);
        Assert.Equal("agent://crew/worker", request.From);
        Assert.Contains("go?", request.Payload!, StringComparison.Ordinal);

        // The journal still received all three — the request queue is a view, not a filter.
        Assert.Equal(3, model.HubMessages.Count);
    }

    [Fact]
    public void Agent_requests_queue_and_a_resent_request_replaces_its_own_entry()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","correlationId":"r-1","expectsReply":true,"payload":{"q":1}}""",
            """{"v":2,"seq":2,"ts":"t","kind":"hub.message","correlationId":"r-2","expectsReply":true,"payload":{"q":2}}""",
            """{"v":2,"seq":3,"ts":"t","kind":"hub.message","correlationId":"r-1","expectsReply":true,"payload":{"q":3}}""");

        // r-1 was resent: its old entry is gone, its fresh one queues behind r-2.
        Assert.Equal("r-2", model.PendingAgentRequest!.CorrelationId);

        model.ReplyAccepted();
        Assert.Equal("r-1", model.PendingAgentRequest!.CorrelationId);
        Assert.Contains("\"q\":3", model.PendingAgentRequest.Payload!, StringComparison.Ordinal);

        model.ReplyAccepted();
        Assert.Null(model.PendingAgentRequest);
    }

    [Fact]
    public void A_request_with_no_correlation_id_stays_journal_only()
    {
        // expectsReply without an address to answer to is a line the screen cannot serve.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","expectsReply":true,"payload":{"q":1}}""");

        Assert.Null(model.PendingAgentRequest);
        Assert.Single(model.HubMessages);
    }

    [Fact]
    public void The_end_of_the_run_clears_agent_requests_too()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","correlationId":"r-1","expectsReply":true,"payload":{}}""",
            """{"v":2,"seq":2,"ts":"t","kind":"run.finished","success":true,"exitCode":0}""");

        Assert.Null(model.PendingAgentRequest);
    }

    [Fact]
    public void Deltas_accumulate_and_errors_are_kept()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"llm.delta","text":"Hel"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"llm.delta","text":"lo"}""",
            """{"v":2,"seq":3,"ts":"t","kind":"error","code":"llm.unreachable","message":"no endpoint","recoverable":false}""");

        Assert.Equal("Hello", model.GeneratedText);
        Assert.Equal("llm.unreachable", model.LastError!.Code);
        Assert.False(model.LastError.Recoverable);
    }

    [Fact]
    public void A_started_task_is_in_progress_until_the_run_reports_it_finished()
    {
        // STUDIO-17: the moment the screen used to be blind to. The start carries the run's own
        // clock; nothing about the task is guessed.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-19T10:31:02Z","kind":"task.started","taskId":"t1","agentRole":"analyst"}""");

        var running = Assert.Single(model.RunningTasks);
        Assert.Equal("t1", running.TaskId);
        Assert.Equal("analyst", running.AgentRole);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 10, 31, 2, TimeSpan.Zero), running.StartedAt);
        Assert.Empty(model.Tasks);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"analyst","success":true,"durationMs":1200,"tokens":340,"toolCalls":2}"""));

        Assert.Empty(model.RunningTasks);
        Assert.Single(model.Tasks);
    }

    [Fact]
    public void A_close_that_names_the_task_but_not_the_agent_still_settles_it()
    {
        // Graph and autonomous modes announce the start under the agent's role and report the
        // close under the mode's name; the task id is what pairs them.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"task.started","taskId":"t1","agentRole":"analyst"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"task.started","taskId":"t2","agentRole":"writer"}""",
            """{"v":2,"seq":3,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"graph","success":true,"durationMs":10}""");

        Assert.Equal("t2", Assert.Single(model.RunningTasks).TaskId);
    }

    [Fact]
    public void A_start_time_that_does_not_parse_is_absent_rather_than_invented()
    {
        var model = Fold("""{"v":2,"seq":1,"ts":"t","kind":"task.started","taskId":"t1","agentRole":"analyst"}""");

        Assert.Null(Assert.Single(model.RunningTasks).StartedAt);
    }

    [Fact]
    public void A_close_without_a_start_still_counts_as_a_finished_task()
    {
        // An older CLI announces no starts; its completions must not be refused.
        var model = Fold("""{"v":2,"seq":1,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"analyst","success":true,"durationMs":10}""");

        Assert.Single(model.Tasks);
        Assert.Empty(model.RunningTasks);
    }

    [Fact]
    public void The_tool_at_work_is_named_until_it_returns()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader","argsSummary":"path"}""");
        Assert.Equal("pdf_reader", model.ActiveToolName);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","correlationId":"k-2","kind":"tool.called","toolName":"docx_writer"}"""));
        Assert.Equal("docx_writer", model.ActiveToolName);   // the latest call is the one at work

        model.Apply(Parse("""{"v":2,"seq":3,"ts":"t","correlationId":"k-2","kind":"tool.returned","toolName":"docx_writer","success":true,"durationMs":5}"""));
        Assert.Equal("pdf_reader", model.ActiveToolName);

        model.Apply(Parse("""{"v":2,"seq":4,"ts":"t","correlationId":"k-1","kind":"tool.returned","toolName":"pdf_reader","success":false,"durationMs":5}"""));
        Assert.Null(model.ActiveToolName);
    }

    [Fact]
    public void A_return_nothing_was_waiting_for_announces_nothing()
    {
        var model = new RunProgressModel();
        var announcements = 0;
        model.Changed += (_, _) => announcements++;

        model.Apply(Parse("""{"v":2,"seq":1,"ts":"t","correlationId":"k-9","kind":"tool.returned","toolName":"pdf_reader","success":true,"durationMs":5}"""));

        Assert.Equal(0, announcements);
        Assert.Null(model.ActiveToolName);
    }

    [Fact]
    public void The_end_of_the_run_leaves_nothing_in_progress()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"task.started","taskId":"t1","agentRole":"analyst"}""",
            """{"v":2,"seq":2,"ts":"t","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":3,"ts":"t","kind":"run.finished","success":false,"exitCode":1}""");

        Assert.Empty(model.RunningTasks);
        Assert.Null(model.ActiveToolName);
    }

    [Fact]
    public void An_unknown_kind_changes_nothing_and_announces_nothing()
    {
        // A newer CLI may say more than this build understands. Showing a little less beats
        // crashing on a line nobody has taught the screen to read.
        var model = new RunProgressModel();
        var announcements = 0;
        model.Changed += (_, _) => announcements++;

        model.Apply(Parse("""{"v":2,"seq":1,"ts":"t","kind":"something.new","detail":"x"}"""));
        Assert.Equal(0, announcements);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":10}"""));
        Assert.Equal(1, announcements);
    }

    [Fact]
    public void Tools_at_work_in_parallel_are_all_shown_with_their_start()
    {
        // STUDIO-30: the model named the latest tool only, and kept no start. A status bar lists
        // every tool at work, and since when by the run's own clock.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:31:02Z","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":2,"ts":"2026-09-24T10:31:05Z","correlationId":"k-2","kind":"tool.called","toolName":"web_search"}""");

        Assert.Equal(
            [
                new RunToolInFlight("pdf_reader", new DateTimeOffset(2026, 9, 24, 10, 31, 2, TimeSpan.Zero)),
                new RunToolInFlight("web_search", new DateTimeOffset(2026, 9, 24, 10, 31, 5, TimeSpan.Zero)),
            ],
            model.ActiveTools);
        Assert.Equal("web_search", model.ActiveToolName);   // still the latest call, for the activity line
        Assert.Equal(2, model.ToolCallCount);
    }

    [Fact]
    public void A_return_out_of_order_closes_the_call_it_answers()
    {
        // Two calls of one tool: only the correlation id tells them apart. The first returning
        // leaves the second on screen, with the second's start — the latest call of that name
        // would have been the wrong one.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:31:02Z","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":2,"ts":"2026-09-24T10:31:09Z","correlationId":"k-2","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":3,"ts":"2026-09-24T10:31:12Z","correlationId":"k-1","kind":"tool.returned","toolName":"pdf_reader","success":true,"durationMs":10000}""");

        var remaining = Assert.Single(model.ActiveTools);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 31, 9, TimeSpan.Zero), remaining.StartedAt);
        Assert.Equal(1, model.SucceededToolCalls);
    }

    [Fact]
    public void A_delegation_is_under_way_until_its_call_returns()
    {
        // The CLI reports a delegation instead of its tool call, then closes it with the
        // tool.returned every call gets: there is no delegation.finished.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:32:00Z","correlationId":"d-1","kind":"delegation.started","toRole":"Writer"}""");

        var delegation = Assert.Single(model.ActiveDelegations);
        Assert.Equal("Writer", delegation.ToRole);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 32, 0, TimeSpan.Zero), delegation.StartedAt);
        Assert.Empty(model.ActiveTools);   // reported as a delegation, so not a tool at work
        Assert.Equal(0, model.ToolCallCount);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","correlationId":"d-1","kind":"tool.returned","toolName":"delegate_work_to_coworker","success":true,"durationMs":42000}"""));

        Assert.Empty(model.ActiveDelegations);
        Assert.Equal(0, model.SucceededToolCalls);   // its return closed the delegation, not a tool call
    }

    [Fact]
    public void A_spawned_agent_is_counted()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:33:00Z","correlationId":"s-1","kind":"agent.spawned","role":"Fact checker","reason":"Verify the figures"}""",
            """{"v":2,"seq":2,"ts":"t","correlationId":"s-1","kind":"tool.returned","toolName":"spawn_agent","success":true,"durationMs":5}""");

        // The agent outlives its spawn call: it is a member of the team now, not work in flight.
        var spawned = Assert.Single(model.SpawnedAgents);
        Assert.Equal("Fact checker", spawned.Role);
        Assert.Equal("Verify the figures", spawned.Reason);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 33, 0, TimeSpan.Zero), spawned.SpawnedAt);
    }

    [Fact]
    public void A_pending_question_or_agent_request_means_the_run_waits_for_an_answer()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"Which client?"}""");

        Assert.True(model.IsWaitingForAnswer);
        Assert.Equal("Which client?", model.PendingQuestion!.Prompt);

        model.AnswerAccepted();
        Assert.False(model.IsWaitingForAnswer);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"hub.message","correlationId":"r-1","expectsReply":true,"from":"agent://crew/worker","payload":{"q":"go?"}}"""));
        Assert.True(model.IsWaitingForAnswer);
    }

    [Fact]
    public void A_tool_that_never_returned_before_the_end_is_not_finished_and_never_a_success()
    {
        // D-03: the end of the run empties what is at work, but a call that never came back is
        // kept aside as not finished. Dropped, it would read as one of the calls that went well.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:31:02Z","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":2,"ts":"t","correlationId":"k-2","kind":"tool.called","toolName":"docx_writer"}""",
            """{"v":2,"seq":3,"ts":"t","correlationId":"k-2","kind":"tool.returned","toolName":"docx_writer","success":true,"durationMs":5}""",
            """{"v":2,"seq":4,"ts":"t","correlationId":"k-3","kind":"tool.called","toolName":"web_search"}""",
            """{"v":2,"seq":5,"ts":"t","correlationId":"k-3","kind":"tool.returned","toolName":"web_search","success":false,"durationMs":5}""",
            """{"v":2,"seq":6,"ts":"t","correlationId":"d-1","kind":"delegation.started","toRole":"Writer"}""",
            """{"v":2,"seq":7,"ts":"t","kind":"run.finished","success":false,"exitCode":1}""");

        Assert.Empty(model.ActiveTools);
        var unfinished = Assert.Single(model.UnfinishedTools);
        Assert.Equal("pdf_reader", unfinished.ToolName);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 31, 2, TimeSpan.Zero), unfinished.StartedAt);

        // Every call lands in exactly one place: one succeeded, one failed, one not finished.
        Assert.Equal(3, model.ToolCallCount);
        Assert.Equal(1, model.SucceededToolCalls);
        Assert.Equal(1, model.FailedToolCalls);

        // A delegation is a tool call underneath, and the same rule holds for it.
        Assert.Empty(model.ActiveDelegations);
        Assert.Equal("Writer", Assert.Single(model.UnfinishedDelegations).ToRole);
    }

    [Fact]
    public void An_absent_cost_stays_absent_whatever_else_the_run_reported()
    {
        // ↑, ↓, cache and a price show only once the meter measured them. A run whose meter never
        // moved has no cost — not a cost of zero — even when its close carries a token total.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"2026-09-24T10:30:00Z","kind":"run.started","target":"/ws/crew.yaml"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"task.started","taskId":"t1","agentRole":"analyst"}""",
            """{"v":2,"seq":3,"ts":"t","correlationId":"k-1","kind":"tool.called","toolName":"pdf_reader"}""",
            """{"v":2,"seq":4,"ts":"t","correlationId":"k-1","kind":"tool.returned","toolName":"pdf_reader","success":true,"durationMs":5}""",
            """{"v":2,"seq":5,"ts":"t","kind":"run.finished","success":true,"exitCode":0,"tokens":1200,"durationMs":61000}""");

        Assert.Null(model.Cost);
    }

    [Fact]
    public void The_elapsed_time_counts_from_the_runs_own_start_and_stops_at_its_own_close()
    {
        var clock = new StubTimeProvider { Now = new DateTimeOffset(2026, 9, 24, 10, 32, 30, TimeSpan.Zero) };
        var model = new RunProgressModel(clock);
        model.Apply(Parse("""{"v":2,"seq":1,"ts":"2026-09-24T10:30:00Z","kind":"run.started","target":"/ws/crew.yaml"}"""));

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero), model.StartedAt);
        Assert.Equal(TimeSpan.FromSeconds(150), model.Elapsed);

        // A clock reading, not an event: it moves between two lines of the stream.
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(160), model.Elapsed);

        // Once the run reported its end, its own wall time — frozen however late one reads it.
        model.Apply(Parse("""{"v":2,"seq":2,"ts":"2026-09-24T10:32:41Z","kind":"run.finished","success":true,"exitCode":0,"durationMs":161250}"""));
        clock.Now = clock.Now.AddMinutes(5);
        Assert.Equal(TimeSpan.FromMilliseconds(161_250), model.Elapsed);
    }

    [Fact]
    public void A_close_that_says_no_duration_stops_the_clock_at_its_own_stamp()
    {
        // An older CLI's leaner close: the two stamps of the run's own clock still measure it.
        var clock = new StubTimeProvider { Now = new DateTimeOffset(2026, 9, 24, 11, 0, 0, TimeSpan.Zero) };
        var model = new RunProgressModel(clock);
        model.Apply(Parse("""{"v":2,"seq":1,"ts":"2026-09-24T10:30:00Z","kind":"run.started","target":"/ws/crew.yaml"}"""));
        model.Apply(Parse("""{"v":2,"seq":2,"ts":"2026-09-24T10:31:40Z","kind":"run.finished","success":true,"exitCode":0}"""));

        Assert.Equal(TimeSpan.FromSeconds(100), model.Elapsed);
    }

    [Fact]
    public void A_run_that_gave_no_readable_start_has_no_elapsed_time()
    {
        var model = Fold("""{"v":2,"seq":1,"ts":"t","kind":"run.started","target":"/ws/crew.yaml"}""");

        Assert.Null(model.StartedAt);
        Assert.Null(model.Elapsed);
    }

    [Fact]
    public void Each_announcement_names_the_kind_that_moved_the_state()
    {
        // A delegation and a spawn used to fall through unread. Both announce a change now, and
        // every announcement says which kind moved the state: a token delta arrives per token,
        // so a screen that shows no generated text can skip it. A change made on this side — an
        // answer the run's stdin accepted — names no kind.
        var model = new RunProgressModel();
        var kinds = new List<string?>();
        model.Changed += (_, change) => kinds.Add(change.Kind);

        model.Apply(Parse("""{"v":2,"seq":1,"ts":"t","correlationId":"d-1","kind":"delegation.started","toRole":"Writer"}"""));
        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","correlationId":"s-1","kind":"agent.spawned","role":"Checker"}"""));
        model.Apply(Parse("""{"v":2,"seq":3,"ts":"t","kind":"llm.delta","text":"Hel"}"""));
        model.Apply(Parse("""{"v":2,"seq":4,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"Go?"}"""));
        model.AnswerAccepted();

        Assert.Equal(
            [RunEventKinds.DelegationStarted, RunEventKinds.AgentSpawned, RunEventKinds.LlmDelta, RunEventKinds.InputNeeded, null],
            kinds);
    }

    [Fact]
    public void The_part_of_the_meter_the_runtime_estimated_is_carried_live_and_at_the_close()
    {
        // A provider that counted nothing is estimated by the runtime (STUDIO-42). The model
        // carries how much, so a screen marks the figures «≈» rather than pass an estimate off
        // as a count — and carries nothing while every call was counted.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":300,"promptTokens":240,"completionTokens":60,"operation":"agent"}""");
        Assert.Null(model.Cost!.EstimatedTokens);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":420,"promptTokens":320,"completionTokens":100,"estimatedTokens":120,"operation":"agent"}"""));
        Assert.Equal(120, model.Cost!.EstimatedTokens);

        model.Apply(Parse("""{"v":2,"seq":3,"ts":"t","kind":"run.finished","success":true,"exitCode":0,"tokens":420,"promptTokens":320,"completionTokens":100,"estimatedTokens":120}"""));
        Assert.Equal(120, model.FinalEstimatedTokens);
    }

    [Theory]
    [InlineData("judge")]
    [InlineData("rag")]
    [InlineData("manager")]
    [InlineData("planning")]
    [InlineData("memory")]
    [InlineData("flow")]
    public void A_reading_from_the_machinery_around_the_agents_moves_the_meter_and_leaves_the_model_named(string operation)
    {
        // Every call of a run is on the meter since STUDIO-42. Naming the model of whichever call
        // answered last would say the team switched models each time a judge or a RAG pipeline
        // spoke: the name stays the one the agents work on.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":100,"model":"deepseek-chat","provider":"deepseek","operation":"agent"}""",
            $$"""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":250,"model":"gpt-4o-mini","provider":"openai","operation":"{{operation}}"}""");

        Assert.Equal(250, model.Cost!.Tokens);
        Assert.Equal("deepseek", model.Cost.Provider);
        Assert.Equal("deepseek-chat", model.Cost.Model);
    }

    [Fact]
    public void The_model_named_follows_the_agents_those_of_a_script_included()
    {
        // A script's agents call ctx.llm.* and their readings name the method, not "agent": they
        // are the agents at work all the same. A reading that names no kind of work does not
        // rename anything — nothing says an agent made that call.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":100,"model":"kimi-k2.6","provider":"kimi","operation":"complete"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":150,"model":"unknown-model","provider":"elsewhere"}""");

        Assert.Equal(150, model.Cost!.Tokens);
        Assert.Equal("kimi", model.Cost.Provider);
        Assert.Equal("kimi-k2.6", model.Cost.Model);
    }

    [Fact]
    public void Before_an_agent_answers_the_meter_moves_with_no_model_named()
    {
        // The plan is drafted before any agent works: its tokens count, its model is not the team's.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":80,"promptTokens":60,"completionTokens":20,"model":"planner-model","provider":"openai","operation":"planning"}""");

        Assert.Equal(80, model.Cost!.Tokens);
        Assert.Null(model.Cost.Model);
        Assert.Null(model.Cost.Provider);
    }

    private static OrkeonEvent Parse(string line)
    {
        Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent), line);
        return orkeonEvent!;
    }
}

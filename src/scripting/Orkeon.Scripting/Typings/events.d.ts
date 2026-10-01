// Orkeon Scripting DSL — Events, queues, topics
// Declares the event broker surface: queues, topics, published events and subscriptions.

declare global {
    interface EventQueueInfo {
        readonly length: number;
        readonly waiters: number;
    }

    interface EventQueue<T = unknown> {
        readonly name: string;
        /** Synchronous: hands the value to the oldest waiter, or queues it. */
        push(value: T): void;
        /** The next value. `timeout` (ms) rejects with `ReceiveTimeoutError`; a `kick()` with `WaiterKickedError`. */
        pop(options?: { timeout?: number }): Promise<T>;
        /** Synchronous: the next value without removing it. */
        peek(): T | undefined;
        /** Synchronous: rejects every waiter with `WaiterKickedError`. */
        kick(): void;
        readonly length: number;
        info(): EventQueueInfo;
    }

    interface PublishedEvent<T = unknown> {
        readonly value: T;
        /** How many handlers have received this event so far. */
        readonly handlerCount: number;
        /** How many handlers this event is delivered to: the topic's subscribers when it was published. */
        readonly maxHandlers: number;
        markHandled(): void;
        stopPropagation(): void;
        lock<R>(name: string, fn: () => Promise<R>): Promise<R>;
    }

    interface Subscription {
        unsubscribe(): void;
    }

    interface EventTopicOptions {
        mode?: "parallel" | "sequential";
    }

    interface EventTopic<T = unknown> {
        readonly name: string;
        readonly mode: "parallel" | "sequential";
        publish(value: T): Promise<void>;
        subscribe(handler: (event: PublishedEvent<T>) => Promise<void> | void): Subscription;
    }

    interface EventBroker {
        queue<T = unknown>(name: string): EventQueue<T>;
        topic<T = unknown>(name: string, opts?: EventTopicOptions): EventTopic<T>;
    }
}

export { };

// Orkeon Scripting DSL — Events, queues, topics
// See chapter 06 (events-queues-topics.md).

declare global {
    interface ChannelParticipant {
        readonly name: string;
        readonly id: string;
    }

    interface EventQueueInfo {
        readonly length: number;
        readonly waiters: number;
    }

    interface EventQueue<T = unknown> {
        push(value: T): Promise<void>;
        pop(): Promise<T>;
        peek(): Promise<T | undefined>;
        kick(): Promise<void>;
        readonly length: number;
        info(): EventQueueInfo;
    }

    interface PublishedEvent<T = unknown> {
        readonly value: T;
        readonly publisher: ChannelParticipant;
        readonly handlerCount: number;
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
        maxHandlers?: number;
    }

    interface EventTopic<T = unknown> {
        publish(value: T): Promise<void>;
        subscribe(handler: (event: PublishedEvent<T>) => Promise<void> | void): Subscription;
    }

    interface EventBroker {
        queue<T = unknown>(name: string): EventQueue<T>;
        topic<T = unknown>(name: string, opts?: EventTopicOptions): EventTopic<T>;
    }
}

export { };

// ResilientHttpLlmProvider has been consolidated into HttpLlmProviderBase
// which is located at: /workspace/src/Orkeon.Infrastructure/LLMs/Base/HttpLlmProviderBase.cs
//
// HttpLlmProviderBase now includes:
// - Built-in resilience policy support
// - Retry logic and circuit breakers
// - Timeout handling
// - Common HTTP operations
//
// All HTTP-based LLM providers should now inherit from HttpLlmProviderBase instead.
